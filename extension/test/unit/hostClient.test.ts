import * as assert from 'assert';
import * as path from 'path';
import {
    HostClient, HostCrashedError, HostDisposedError, HostRequestError, HostSpawnError, HostTimeoutError, RenderResult,
} from '../../src/hostClient';
import { LogLevel } from '../../src/logFormat';

/** out/test/unit 에서 소스 트리의 test/fixtures 로 가는 경로(가짜 호스트는 .js라 out으로 복사되지 않는다). */
const FAKE_HOST = path.resolve(__dirname, '../../../test/fixtures/fake-host.js');

/** 테스트에서 쓰는 짧은 타임아웃(ms). */
const SHORT_TIMEOUT_MS = 300;

interface LogLine { level: LogLevel; id: string; message: string }

function createClient(timeoutMs?: number): { client: HostClient; logs: LogLine[] } {
    const logs: LogLine[] = [];
    const client = new HostClient({
        command: process.execPath,
        args: [FAKE_HOST],
        timeoutMs,
        log: (level, id, message) => logs.push({ level, id, message }),
    });
    return { client, logs };
}

/** 프로세스가 살아 있는지(신호 0은 존재 확인만 한다). */
function isAlive(pid: number): boolean {
    try {
        process.kill(pid, 0);
        return true;
    } catch {
        return false;
    }
}

async function waitUntil(condition: () => boolean, timeoutMs = 3000): Promise<void> {
    const start = Date.now();
    while (!condition()) {
        if (Date.now() - start > timeoutMs) {
            throw new Error('조건이 시간 내에 충족되지 않음');
        }
        await new Promise((r) => setTimeout(r, 10));
    }
}

describe('HostClient', () => {
    let client: HostClient | undefined;
    afterEach(async () => {
        await client?.dispose();
        client = undefined;
    });

    it('X-C01 정상 왕복: 요청에 대한 result를 돌려주고 E010으로 시작을 기록한다', async () => {
        const c = createClient();
        client = c.client;
        const result = await client.request<{ pid: number }>('ping');
        assert.strictEqual(result.pid, client.pid);
        assert.ok(c.logs.some((l) => l.id === 'E010' && l.level === 'Info'));
    });

    it('X-C02 타임아웃: HostTimeoutError, 호스트 kill, E011 기록, 다음 요청은 새 호스트로 성공', async () => {
        const c = createClient(SHORT_TIMEOUT_MS);
        client = c.client;
        const first = await client.request<{ pid: number }>('ping');

        await assert.rejects(client.request('hang'), HostTimeoutError);
        await waitUntil(() => !isAlive(first.pid));
        assert.ok(c.logs.some((l) => l.id === 'E011'));

        const second = await client.request<{ pid: number }>('ping');
        assert.notStrictEqual(second.pid, first.pid, '재시작되어 pid가 달라야 한다');
        assert.strictEqual(c.logs.filter((l) => l.id === 'E010').length, 2);
    });

    it('X-C03 호스트 즉사: HostCrashedError(E012), 이후 요청은 자동 복구', async () => {
        const c = createClient();
        client = c.client;
        await assert.rejects(client.request('crash'), (e: unknown) => e instanceof HostCrashedError && e.exitCode === 3);
        assert.ok(c.logs.some((l) => l.id === 'E012' && l.level === 'Error'));

        const result = await client.request<{ pid: number }>('ping');
        assert.ok(result.pid > 0);
    });

    it('X-C04 응답 순서가 뒤섞여도 id로 짝을 맞춘다', async () => {
        const c = createClient();
        client = c.client;
        const order: string[] = [];
        const slow = client.request<{ tag: string }>('delay', { ms: 200, tag: 'slow' }).then((r) => { order.push(r.tag); return r; });
        const fast = client.request<{ tag: string }>('delay', { ms: 0, tag: 'fast' }).then((r) => { order.push(r.tag); return r; });
        const [s, f] = await Promise.all([slow, fast]);
        assert.strictEqual(s.tag, 'slow');
        assert.strictEqual(f.tag, 'fast');
        assert.deepStrictEqual(order, ['fast', 'slow']);
    });

    it('X-C05 JSON이 아닌 줄과 짝 없는 id 응답은 무시하고 정상 응답은 받는다(E014)', async () => {
        const c = createClient();
        client = c.client;
        const result = await client.request<{ survived: boolean }>('garbage');
        assert.strictEqual(result.survived, true);
        assert.strictEqual(c.logs.filter((l) => l.id === 'E014').length, 2);
    });

    it('한 줄이 여러 조각으로 도착해도 하나의 응답으로 합쳐 처리한다', async () => {
        const c = createClient();
        client = c.client;
        const result = await client.request<{ text: string }>('split');
        assert.strictEqual(result.text, 'split-ok');
    });

    it('호스트가 ok:false로 답하면 코드/줄/열을 가진 HostRequestError', async () => {
        const c = createClient();
        client = c.client;
        await assert.rejects(client.request('fail'), (e: unknown) =>
            e instanceof HostRequestError && e.code === 'XamlParse' && e.line === 2 && e.col === 5);
    });

    it('실행 파일이 없으면 HostSpawnError, 이후 요청도 같은 방식으로 실패(재시도 가능)', async () => {
        const logs: LogLine[] = [];
        client = new HostClient({
            command: path.join(__dirname, 'no-such-host.exe'),
            args: [],
            log: (level, id, message) => logs.push({ level, id, message }),
        });
        await assert.rejects(client.request('ping'), HostSpawnError);
        await assert.rejects(client.request('ping'), HostSpawnError);
        assert.ok(logs.some((l) => l.id === 'E012'));
    });

    it('dispose는 shutdown으로 호스트를 종료하고, 이후 요청은 HostDisposedError', async () => {
        const c = createClient();
        const local = c.client;
        const { pid } = await local.request<{ pid: number }>('ping');
        await local.dispose();
        await waitUntil(() => !isAlive(pid));
        await assert.rejects(local.request('ping'), HostDisposedError);
        await local.dispose(); // 두 번 호출해도 안전
    });

    it('dispose는 처리 중인 요청을 HostDisposedError로 거절한다', async () => {
        const c = createClient();
        const local = c.client;
        const inFlight = local.request('hang');
        const assertion = assert.rejects(inFlight, HostDisposedError);
        await local.dispose();
        await assertion;
    });
});

describe('HostClient.renderLatest (최신 요청만 처리)', () => {
    let client: HostClient | undefined;
    afterEach(async () => {
        await client?.dispose();
        client = undefined;
    });

    const params = (ms: number) => ({ xaml: '<Grid/>', ms } as unknown as { xaml: string });

    it('X-D01 단독 요청은 그대로 결과를 돌려준다', async () => {
        const c = createClient();
        client = c.client;
        const outcome = await client.renderLatest(params(0));
        assert.strictEqual(outcome.discarded, false);
        if (!outcome.discarded) {
            assert.strictEqual((outcome.result as RenderResult & { seq: number }).seq, 1);
        }
    });

    it('X-D02 처리 중 새 요청이 오면 이전 결과는 폐기되고 중간 요청은 호스트로 가지 않는다', async () => {
        const c = createClient();
        client = c.client;
        const first = client.renderLatest(params(150));    // 즉시 호스트로 나간다
        const second = client.renderLatest(params(0));     // 대기
        const third = client.renderLatest(params(0));      // second를 밀어낸다
        const [o1, o2, o3] = await Promise.all([first, second, third]);

        assert.strictEqual(o1.discarded, true, '처리 중이던 첫 결과는 낡았으므로 폐기');
        assert.strictEqual(o2.discarded, true, '대기 중 밀려난 요청은 폐기');
        assert.strictEqual(o3.discarded, false);
        if (!o3.discarded) {
            // 호스트가 실제로 받은 render는 first와 third 두 번뿐이다.
            assert.strictEqual((o3.result as RenderResult & { seq: number }).seq, 2);
        }
        assert.ok(c.logs.filter((l) => l.id === 'E013').length >= 2);
    });

    it('낡은 요청의 실패는 에러가 아니라 폐기로 처리된다', async () => {
        // 가짜 호스트의 render를 5초 지연시켜 타임아웃으로 실패를 만든다.
        const local = new HostClient({ command: process.execPath, args: [FAKE_HOST], timeoutMs: SHORT_TIMEOUT_MS, log: () => undefined });
        try {
            const stale = local.renderLatest(params(5_000)); // 타임아웃으로 실패할 요청
            const fresh = local.renderLatest(params(0));
            assert.strictEqual((await stale).discarded, true);
            assert.strictEqual((await fresh).discarded, false);
        } finally {
            await local.dispose();
        }
    });

    it('최신 요청이 실패하면 그 오류가 전달된다', async () => {
        const local = new HostClient({ command: process.execPath, args: [FAKE_HOST], timeoutMs: SHORT_TIMEOUT_MS, log: () => undefined });
        try {
            await assert.rejects(local.renderLatest(params(5_000)), HostTimeoutError);
        } finally {
            await local.dispose();
        }
    });
});
