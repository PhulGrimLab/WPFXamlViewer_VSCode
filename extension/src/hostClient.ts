import { ChildProcessWithoutNullStreams, spawn } from 'child_process';
import { DEFAULT_REQUEST_TIMEOUT_MS, SHUTDOWN_GRACE_MS, SHUTDOWN_REQUEST_ID } from './constants';
import { LogId, LogLevel } from './logFormat';

/** 로그 출력 함수. 실제 확장에서는 OutputChannel로, 테스트에서는 배열로 연결한다. */
export type LogFn = (level: LogLevel, id: string, message: string) => void;

/** HostClient 생성 옵션. */
export interface HostClientOptions {
    /** 호스트 실행 파일(또는 테스트에서는 node). */
    command: string;
    /** 실행 인자(예: ['serve', '--log-dir', dir]). */
    args: string[];
    /** 추가 환경 변수. */
    env?: NodeJS.ProcessEnv;
    /** 요청 타임아웃(ms). 기본 {@link DEFAULT_REQUEST_TIMEOUT_MS}. */
    timeoutMs?: number;
    log: LogFn;
}

/** 호스트가 `ok:false`로 보고한 오류(렌더 실패 등). 줄/열은 1-base, 없으면 undefined. */
export class HostRequestError extends Error {
    constructor(public readonly code: string, message: string, public readonly line?: number, public readonly col?: number) {
        super(message);
        this.name = 'HostRequestError';
    }
}

/** 요청이 제한 시간 안에 응답하지 않아 호스트를 kill했다. */
export class HostTimeoutError extends Error {
    constructor(public readonly requestId: number, public readonly elapsedMs: number) {
        super(`호스트가 ${elapsedMs}ms 안에 응답하지 않았습니다 (request ${requestId}).`);
        this.name = 'HostTimeoutError';
    }
}

/** 처리 중에 호스트 프로세스가 비정상 종료했다. */
export class HostCrashedError extends Error {
    constructor(public readonly exitCode: number | null, public readonly signal: string | null, public readonly stderrTail: string) {
        super(`호스트가 비정상 종료했습니다 (exit=${exitCode}, signal=${signal}).${stderrTail ? ' ' + stderrTail : ''}`);
        this.name = 'HostCrashedError';
    }
}

/** 호스트 프로세스를 시작하지 못했다(실행 파일 없음 등). */
export class HostSpawnError extends Error {
    constructor(message: string) {
        super(`호스트를 시작할 수 없습니다: ${message}`);
        this.name = 'HostSpawnError';
    }
}

/** dispose 이후에 요청했거나 처리 중이던 요청이 dispose로 취소됐다. */
export class HostDisposedError extends Error {
    constructor() {
        super('HostClient가 종료되었습니다.');
        this.name = 'HostDisposedError';
    }
}

/** render 요청 파라미터(프로토콜: doc/01 3.1). */
export interface RenderParams {
    xaml: string;
    /** 문서의 파일 경로. 호스트가 병합 사전의 상대 경로를 풀 때 쓴다. */
    filePath?: string;
    /**
     * 프로젝트 빌드 DLL을 불러와 사용자 컨트롤을 실제로 그려도 되는가(Tier 1). 사용자 코드가 실행되므로 호출자는
     * Workspace Trust(`vscode.workspace.isTrusted`)가 true일 때만 true로 보내야 한다. 생략/false면 호스트는 사용자 코드를 절대 로드하지 않는다.
     */
    allowProjectAssemblies?: boolean;
    width?: number;
    height?: number;
    dpi?: number;
}

/** 렌더는 성공했지만 사용자가 알아야 하는 변경/제한(제거한 x:Class, 자리표시자 등). 줄/열은 1-base, 없을 수 있다. */
export interface RenderWarning {
    code: string;
    message: string;
    line?: number;
    col?: number;
}

/** render 성공 결과(프로토콜: doc/01 3.1). */
export interface RenderResult {
    png: string;
    width: number;
    height: number;
    elements: unknown[];
    warnings: RenderWarning[];
    /** 이번 렌더의 Tier 결정(0: 사용자 컨트롤은 자리표시자, 1: 프로젝트 DLL 로드)과 이유. */
    project?: { tier: number; reason: string } | null;
}

/** renderLatest 결과: 더 새 요청에 밀려 폐기되었으면 `discarded: true`. */
export type LatestRenderOutcome = { discarded: true } | { discarded: false; result: RenderResult };

interface Pending {
    method: string;
    startedAt: number;
    timer: NodeJS.Timeout;
    resolve: (value: unknown) => void;
    reject: (reason: Error) => void;
}

interface PendingLatest {
    params: RenderParams;
    resolve: (value: LatestRenderOutcome) => void;
    reject: (reason: Error) => void;
}

/** 호스트가 보내는 stderr 중 오류 메시지에 붙일 마지막 부분의 최대 길이. */
const STDERR_TAIL_MAX_CHARS = 500;

/**
 * 호스트 프로세스(XamlRenderHost serve)와 줄 단위 JSON으로 통신하는 클라이언트.
 *
 * Owner: 확장 활성화 코드(extension.ts)가 하나를 만들고 deactivate에서 dispose한다.
 * Lifetime: 확장 활성~비활성. 호스트 프로세스는 첫 요청 때 **지연 시작**하고, 크래시/타임아웃 후에는 다음 요청에서 다시 시작한다.
 * 스레드: Node 단일 이벤트 루프. 이 클래스가 요청 id → Promise 맵과 타임아웃 타이머의 유일한 소유자다(doc/01 4절).
 *
 * 오류 규약: 호스트가 ok:false로 답하면 {@link HostRequestError}, 시간 초과면 {@link HostTimeoutError}(호스트 kill),
 * 프로세스가 죽으면 {@link HostCrashedError}, 시작 불가면 {@link HostSpawnError}. 어떤 실패 뒤에도 다음 요청은 새 호스트로 시도한다.
 */
export class HostClient {
    private readonly _options: HostClientOptions;
    private readonly _timeoutMs: number;
    private readonly _pending = new Map<number, Pending>();
    private _child: ChildProcessWithoutNullStreams | undefined;
    private _nextId = 1;
    private _startCount = 0;
    private _stdoutBuffer = '';
    private _stderrTail = '';
    private _disposed = false;

    // renderLatest 상태: 동시에 호스트로 나가는 render는 최대 1개, 대기는 가장 최신 1개만 유지한다.
    private _latestSeq = 0;
    private _latestInFlight = false;
    private _latestPending: PendingLatest | undefined;

    constructor(options: HostClientOptions) {
        this._options = options;
        this._timeoutMs = options.timeoutMs ?? DEFAULT_REQUEST_TIMEOUT_MS;
    }

    /** 현재 호스트 프로세스 id. 실행 중이 아니면 undefined. */
    get pid(): number | undefined {
        return this._child?.pid;
    }

    /**
     * 요청을 보내고 `result`를 돌려준다. 호스트가 없으면 시작한다.
     * 입력: 메서드 이름, 파라미터, (선택) 이 요청만의 타임아웃. 출력: 호스트의 result 객체.
     */
    request<T = unknown>(method: string, params?: unknown, timeoutMs: number = this._timeoutMs): Promise<T> {
        if (this._disposed) {
            return Promise.reject(new HostDisposedError());
        } else {
            // 정상: 아래에서 계속.
        }

        let child: ChildProcessWithoutNullStreams;
        try {
            child = this._ensureStarted();
        } catch (e) {
            return Promise.reject(e instanceof Error ? e : new HostSpawnError(String(e)));
        }

        const id = this._nextId++;
        return new Promise<T>((resolve, reject) => {
            const timer = setTimeout(() => this._onTimeout(id), timeoutMs);
            this._pending.set(id, { method, startedAt: Date.now(), timer, resolve: resolve as (v: unknown) => void, reject });
            child.stdin.write(JSON.stringify({ id, method, params }) + '\n');
        });
    }

    /**
     * 편집 중 요청이 몰릴 때 **최신 요청만** 처리하는 render. 호스트에는 동시에 최대 1개만 보내고,
     * 처리 중에 더 새 요청이 오면 이전 결과는 폐기(E013)해 `{discarded:true}`로 돌려준다. 대기 중이던 중간 요청도 폐기된다.
     */
    renderLatest(params: RenderParams): Promise<LatestRenderOutcome> {
        return new Promise<LatestRenderOutcome>((resolve, reject) => {
            if (this._latestPending) {
                // 대기 중이던 이전 요청은 한 번도 호스트로 가지 못하고 더 새 요청에 밀린다.
                this._options.log('Debug', LogId.RequestDiscarded, 'superseded while queued');
                this._latestPending.resolve({ discarded: true });
            } else {
                // 대기 중인 요청 없음.
            }
            this._latestPending = { params, resolve, reject };
            this._latestSeq++;
            this._pumpLatest();
        });
    }

    /** 호스트가 놀고 있고 대기 요청이 있으면 그것을 호스트로 보낸다. */
    private _pumpLatest(): void {
        if (this._latestInFlight || !this._latestPending) {
            return;
        } else {
            // 보낼 수 있는 상태: 계속.
        }

        const job = this._latestPending;
        this._latestPending = undefined;
        this._latestInFlight = true;
        const seqAtSend = this._latestSeq;

        this.request<RenderResult>('render', job.params).then(
            (result) => {
                this._latestInFlight = false;
                if (seqAtSend !== this._latestSeq) {
                    // 처리 중에 더 새 요청이 들어옴: 이 결과는 낡았다.
                    this._options.log('Debug', LogId.RequestDiscarded, `stale result discarded seq=${seqAtSend} latest=${this._latestSeq}`);
                    job.resolve({ discarded: true });
                } else {
                    job.resolve({ discarded: false, result });
                }
                this._pumpLatest();
            },
            (error: Error) => {
                this._latestInFlight = false;
                if (seqAtSend !== this._latestSeq) {
                    // 실패했더라도 더 새 요청이 있으므로 낡은 오류는 폐기한다.
                    job.resolve({ discarded: true });
                } else {
                    job.reject(error);
                }
                this._pumpLatest();
            });
    }

    /**
     * 정리: 호스트에 shutdown을 보내고 {@link SHUTDOWN_GRACE_MS} 안에 끝나지 않으면 kill한다.
     * 처리 중이던 요청은 {@link HostDisposedError}로 거절된다. 여러 번 호출해도 안전하다.
     */
    async dispose(): Promise<void> {
        if (this._disposed) {
            return;
        } else {
            this._disposed = true;
        }

        this._latestPending?.reject(new HostDisposedError());
        this._latestPending = undefined;
        this._rejectAllPending(new HostDisposedError());

        const child = this._child;
        this._child = undefined;
        if (!child) {
            return;
        } else {
            // 아래에서 종료 절차 수행.
        }

        await new Promise<void>((resolve) => {
            const killTimer = setTimeout(() => child.kill(), SHUTDOWN_GRACE_MS);
            child.once('exit', () => {
                clearTimeout(killTimer);
                resolve();
            });
            child.stdin.write(JSON.stringify({ id: SHUTDOWN_REQUEST_ID, method: 'shutdown' }) + '\n');
        });
    }

    /** 호스트가 없으면 시작한다. 이미 있으면 그대로 돌려준다. */
    private _ensureStarted(): ChildProcessWithoutNullStreams {
        if (this._child) {
            return this._child;
        } else {
            // 시작 필요.
        }

        const child = spawn(this._options.command, this._options.args, {
            env: { ...process.env, ...this._options.env },
            windowsHide: true,
            stdio: ['pipe', 'pipe', 'pipe'],
        });
        this._stdoutBuffer = '';
        this._stderrTail = '';

        child.stdout.setEncoding('utf8');
        child.stdout.on('data', (chunk: string) => this._onStdout(child, chunk));
        child.stderr.setEncoding('utf8');
        child.stderr.on('data', (chunk: string) => {
            this._stderrTail = (this._stderrTail + chunk).slice(-STDERR_TAIL_MAX_CHARS);
        });
        // 프로세스가 이미 죽은 뒤의 쓰기 오류(EPIPE)는 exit/error 핸들러가 처리하므로 여기서는 삼킨다.
        child.stdin.on('error', () => undefined);
        child.on('error', (err) => this._onChildGone(child, new HostSpawnError(err.message)));
        child.on('exit', (code, signal) => this._onChildGone(child, new HostCrashedError(code, signal, this._stderrTail.trim())));

        this._child = child;
        this._options.log('Info', LogId.HostStarted,
            `command=${this._options.command} pid=${child.pid ?? 'unknown'} starts=${++this._startCount}`);
        return child;
    }

    /** stdout 조각을 모아 완성된 줄마다 처리한다(한 줄이 여러 조각으로 올 수 있다). */
    private _onStdout(child: ChildProcessWithoutNullStreams, chunk: string): void {
        if (child !== this._child) {
            return; // 이미 버린 호스트의 늦은 출력.
        } else {
            // 현재 호스트의 출력: 계속.
        }

        this._stdoutBuffer += chunk;
        let newline = this._stdoutBuffer.indexOf('\n');
        while (newline >= 0) {
            const line = this._stdoutBuffer.slice(0, newline).trim();
            this._stdoutBuffer = this._stdoutBuffer.slice(newline + 1);
            if (line.length > 0) {
                this._onLine(line);
            } else {
                // 빈 줄은 무시.
            }
            newline = this._stdoutBuffer.indexOf('\n');
        }
    }

    /** 응답 한 줄을 해석해 대응하는 요청을 완료시킨다. 형식이 틀리거나 알 수 없는 id면 로그만 남기고 무시한다. */
    private _onLine(line: string): void {
        let message: { id?: unknown; ok?: unknown; result?: unknown; error?: { code?: string; message?: string; line?: number; col?: number } };
        try {
            message = JSON.parse(line);
        } catch {
            this._options.log('Warn', LogId.HostOutputIgnored, `JSON이 아닌 호스트 출력 무시 (length=${line.length})`);
            return;
        }

        const pending = typeof message.id === 'number' ? this._pending.get(message.id) : undefined;
        if (!pending || typeof message.id !== 'number') {
            this._options.log('Debug', LogId.HostOutputIgnored, `알 수 없는 응답 id 무시: ${String(message.id)}`);
            return;
        } else {
            // 대응하는 요청 있음.
        }

        clearTimeout(pending.timer);
        this._pending.delete(message.id);
        if (message.ok === true) {
            pending.resolve(message.result);
        } else {
            const e = message.error ?? {};
            pending.reject(new HostRequestError(e.code ?? 'Unknown', e.message ?? '(메시지 없음)', e.line, e.col));
        }
    }

    /** 타임아웃: 호스트를 kill하고 모든 대기 요청을 거절한다. 다음 요청은 새 호스트로 시작한다. */
    private _onTimeout(id: number): void {
        const pending = this._pending.get(id);
        if (!pending) {
            return; // 이미 완료됨.
        } else {
            // 타임아웃 처리 진행.
        }

        const elapsed = Date.now() - pending.startedAt;
        this._options.log('Warn', LogId.RequestTimeout, `id=${id} method=${pending.method} elapsedMs=${elapsed}`);
        const child = this._child;
        this._child = undefined; // exit 핸들러가 "예기치 않은 종료"로 오인하지 않도록 먼저 분리한다.
        this._rejectAllPending(new HostTimeoutError(id, elapsed));
        child?.kill();
    }

    /** 호스트가 스스로 사라졌을 때(크래시/시작 실패)의 처리. 의도적으로 분리한 호스트의 종료는 무시한다. */
    private _onChildGone(child: ChildProcessWithoutNullStreams, error: Error): void {
        if (child !== this._child) {
            return; // 타임아웃/dispose로 이미 분리한 호스트.
        } else {
            this._child = undefined;
        }

        const detail = error instanceof HostCrashedError ? `exit=${error.exitCode} signal=${error.signal}` : error.message;
        this._options.log('Error', LogId.HostCrashed, `${detail} pendingRequests=${this._pending.size}`);
        this._rejectAllPending(error);
    }

    private _rejectAllPending(error: Error): void {
        const all = [...this._pending.values()];
        this._pending.clear();
        for (const p of all) {
            clearTimeout(p.timer);
            p.reject(error);
        }
    }
}
