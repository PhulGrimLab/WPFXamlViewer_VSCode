import * as assert from 'assert';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { HostClient, HostCrashedError, HostRequestError, HostTimeoutError, RenderResult } from '../../src/hostClient';
import { LogLevel } from '../../src/logFormat';

/**
 * 실제 호스트(XamlRenderHost.exe)를 띄워 HostClient와 함께 검증한다(M2.4 장애 주입, I-04/I-08/I-07 일부).
 * VS Code 없이 돌릴 수 있는 "실제 호스트 + 실제 HostClient" 계층이다. 호스트는 먼저 빌드되어 있어야 한다
 * (doc/run_tests.ps1 또는 `dotnet build host/XamlRenderHost.slnx`). 경로는 XAMLVIEWER_HOST_EXE로 바꿀 수 있다.
 * 없으면 건너뛰지 않고 **실패**시킨다 — 조용히 건너뛰면 장애 복구가 검증되지 않은 채 통과로 보이기 때문이다.
 */
const HOST_EXE = process.env.XAMLVIEWER_HOST_EXE
    ?? path.resolve(__dirname, '../../../../host/XamlRenderHost/bin/Debug/net10.0-windows/win-x64/XamlRenderHost.exe');

const NS = 'xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"';

/** 무응답/크래시 시험용 짧은 타임아웃(ms). 호스트 시작 시간보다 충분히 길어야 한다. */
const HANG_TIMEOUT_MS = 800;

/** 거대 XAML 시험: 고정 크기 Grid 안의 요소 수(doc/03 I-08). */
const HUGE_ELEMENT_COUNT = 5000;

/** 거대 XAML이 정상 처리되어야 하는 최대 시간(ms). 기본 요청 타임아웃과 같은 기준. */
const HUGE_XAML_BUDGET_MS = 10_000;

interface LogLine { level: LogLevel; id: string; message: string }

function newClient(logDir: string, testHooks: boolean, timeoutMs?: number): { client: HostClient; logs: LogLine[] } {
    const logs: LogLine[] = [];
    const client = new HostClient({
        command: HOST_EXE,
        args: ['serve', '--log-dir', logDir, '--log-level', 'Debug'],
        env: testHooks ? { XAMLVIEWER_TEST_HOOKS: '1' } : undefined,
        timeoutMs,
        log: (level, id, message) => logs.push({ level, id, message }),
    });
    return { client, logs };
}

function isAlive(pid: number): boolean {
    try {
        process.kill(pid, 0);
        return true;
    } catch {
        return false;
    }
}

async function waitUntil(condition: () => boolean, timeoutMs = 5000): Promise<void> {
    const start = Date.now();
    while (!condition()) {
        if (Date.now() - start > timeoutMs) {
            throw new Error('조건이 시간 내에 충족되지 않음');
        }
        await new Promise((r) => setTimeout(r, 20));
    }
}

describe('실제 호스트 + HostClient', function () {
    let logDir: string;
    let client: HostClient | undefined;

    before(() => {
        assert.ok(fs.existsSync(HOST_EXE),
            `호스트 exe가 없습니다: ${HOST_EXE}\n먼저 doc/run_tests.ps1 로 호스트를 빌드하세요.`);
    });

    beforeEach(() => {
        logDir = fs.mkdtempSync(path.join(os.tmpdir(), 'xrh-realhost-'));
    });

    afterEach(async () => {
        await client?.dispose();
        client = undefined;
        fs.rmSync(logDir, { recursive: true, force: true });
    });

    it('정상 렌더: PNG와 크기를 받는다', async () => {
        const c = newClient(logDir, false);
        client = c.client;
        const r = await client.request<RenderResult>('render', { xaml: `<Rectangle ${NS} Fill="Red"/>`, width: 30, height: 20 });
        assert.strictEqual(r.width, 30);
        assert.strictEqual(r.height, 20);
        assert.strictEqual(Buffer.from(r.png, 'base64').subarray(1, 4).toString('ascii'), 'PNG');
    });

    it('렌더 오류는 HostRequestError(코드/줄/열)로 오고 호스트는 계속 산다', async () => {
        const c = newClient(logDir, false);
        client = c.client;
        await assert.rejects(
            client.request('render', { xaml: `<Grid ${NS}>\n  <NoSuchElement/>\n</Grid>` }),
            (e: unknown) => e instanceof HostRequestError && e.code === 'XamlParse' && e.line === 2 && typeof e.col === 'number');
        const pidAfterError = client.pid;
        const ok = await client.request<RenderResult>('render', { xaml: `<Grid ${NS} Width="5" Height="5"/>` });
        assert.strictEqual(ok.width, 5);
        assert.strictEqual(client.pid, pidAfterError, '오류가 났어도 같은 호스트가 계속 처리해야 한다');
    });

    it('I-04 무응답(debug.hang): 타임아웃 후 호스트가 kill되고 다음 렌더는 새 호스트에서 성공한다', async () => {
        const c = newClient(logDir, true);
        client = c.client;
        await client.request('ping'); // 호스트 시작 시간을 타임아웃 계산에서 제외한다.
        const hungPid = client.pid!;

        await assert.rejects(client.request('debug.hang', undefined, HANG_TIMEOUT_MS), HostTimeoutError);
        await waitUntil(() => !isAlive(hungPid));

        const r = await client.request<RenderResult>('render', { xaml: `<Grid ${NS} Width="7" Height="7"/>` });
        assert.strictEqual(r.width, 7);
        assert.notStrictEqual(client.pid, hungPid);

        const ids = c.logs.map((l) => l.id).filter((id) => id === 'E010' || id === 'E011');
        assert.deepStrictEqual(ids, ['E010', 'E011', 'E010'], '시작 → 타임아웃 → 재시작 순서여야 한다');
    });

    it('I-04 크래시(debug.crash): HostCrashedError(exit 99), 다음 렌더는 새 호스트에서 성공한다', async () => {
        const c = newClient(logDir, true);
        client = c.client;
        await client.request('ping');
        const crashedPid = client.pid;

        await assert.rejects(client.request('debug.crash'),
            (e: unknown) => e instanceof HostCrashedError && e.exitCode === 99);
        assert.ok(c.logs.some((l) => l.id === 'E012' && l.message.includes('exit=99')));

        const r = await client.request<RenderResult>('render', { xaml: `<Grid ${NS} Width="9" Height="9"/>` });
        assert.strictEqual(r.width, 9);
        assert.notStrictEqual(client.pid, crashedPid);
    });

    it('테스트 훅이 꺼져 있으면 debug.* 는 UnknownMethod (사용자 환경에서 장애 주입이 켜지지 않는다)', async () => {
        const c = newClient(logDir, false);
        client = c.client;
        await assert.rejects(client.request('debug.crash'),
            (e: unknown) => e instanceof HostRequestError && e.code === 'UnknownMethod');
    });

    it('I-08 거대 XAML(요소 5,000개)은 제한 시간 안에 렌더되거나 오류로 끝나고 이후 요청이 정상이다', async function () {
        this.timeout(HUGE_XAML_BUDGET_MS * 2);
        const c = newClient(logDir, false, HUGE_XAML_BUDGET_MS);
        client = c.client;
        const children = Array.from({ length: HUGE_ELEMENT_COUNT }, (_, i) =>
            `<Rectangle Width="4" Height="4" Fill="SteelBlue" Margin="${(i % 100) * 5},${Math.floor(i / 100) * 5},0,0" HorizontalAlignment="Left" VerticalAlignment="Top"/>`);
        const xaml = `<Grid ${NS} Width="520" Height="260">${children.join('')}</Grid>`;

        const started = Date.now();
        const r = await client.request<RenderResult>('render', { xaml });
        assert.strictEqual(r.width, 520);
        assert.ok(Date.now() - started < HUGE_XAML_BUDGET_MS, `렌더가 ${Date.now() - started}ms 걸림`);

        const after = await client.request<RenderResult>('render', { xaml: `<Grid ${NS} Width="3" Height="3"/>` });
        assert.strictEqual(after.width, 3);
    });

    it('렌더 크기 초과(TooLarge)는 호스트를 죽이지 않고 오류로 돌려준다', async () => {
        const c = newClient(logDir, false);
        client = c.client;
        await assert.rejects(client.request('render', { xaml: `<Grid ${NS} Width="99999" Height="10"/>` }),
            (e: unknown) => e instanceof HostRequestError && e.code === 'TooLarge');
        const pid = client.pid;
        await client.request('ping');
        assert.strictEqual(client.pid, pid);
    });

    it('I-07 로그: 시나리오 후 호스트 로그에 H001/H011/H012가 순서대로 남고 XAML 본문은 없다', async () => {
        const c = newClient(logDir, false);
        client = c.client;
        const secret = 'REALHOST_SECRET_8841';
        await client.request('render', { xaml: `<TextBlock ${NS} Text="${secret}"/>` });
        await assert.rejects(client.request('render', { xaml: `<Grid ${NS}><${secret}/></Grid>` }));
        await client.dispose(); // shutdown → H002까지 기록되고 로거가 비워진다.

        const log = fs.readFileSync(path.join(logDir, 'host.log'), 'utf8');
        const ids = log.split('\n').filter((l) => l.trim()).map((l) => l.split(' ')[2]).filter((id) => id !== 'H010');
        assert.deepStrictEqual(ids, ['H001', 'H011', 'H012', 'H002'], log);
        assert.ok(!log.includes(secret), 'XAML 본문이 로그에 남았다');

        // 확장 쪽 로그에도 시작 기록(E010)이 있다.
        assert.ok(c.logs.some((l) => l.id === 'E010'));
    });
});
