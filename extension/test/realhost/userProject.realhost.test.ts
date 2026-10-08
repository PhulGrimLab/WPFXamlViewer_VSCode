import * as assert from 'assert';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { HostClient, HostTimeoutError, RenderResult } from '../../src/hostClient';
import { LogLevel } from '../../src/logFormat';
import { createUserProject } from '../support/userProject';

/**
 * M4B Tier 1 실제 호스트 테스트: 샘플 사용자 프로젝트를 빌드해 호스트가 DLL을 로드하는 경로를 HostClient로 검증한다
 * (B.4 신뢰 플래그, B.6 결함 주입, H020/H023 로그). 호스트 exe 경로 규칙은 hostClient.realhost.test.ts와 같다.
 */
const HOST_EXE = process.env.XAMLVIEWER_HOST_EXE
    ?? path.resolve(__dirname, '../../../../host/XamlRenderHost/bin/Debug/net10.0-windows/win-x64/XamlRenderHost.exe');

const NS = 'xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"';
const SAMPLE_NS = 'xmlns:s="clr-namespace:SampleControls;assembly=SampleControls"';

/** 첫 빌드 + 프로젝트 로드가 걸릴 수 있어 mocha 기본(2초)보다 길게 둔다. */
const SUITE_TIMEOUT_MS = 240_000;

/** 무한 대기 컨트롤 시험의 요청 타임아웃(ms). 어셈블리 로드 시간보다 충분히 길어야 한다. */
const HANG_TIMEOUT_MS = 5_000;

interface LogLine { level: LogLevel; id: string; message: string }

function userXaml(body: string): string {
    return `<StackPanel ${NS} ${SAMPLE_NS}>${body}</StackPanel>`;
}

describe('M4B 실제 호스트 + 사용자 프로젝트', function () {
    this.timeout(SUITE_TIMEOUT_MS);

    let root: string;
    let logDir: string;
    let client: HostClient | undefined;
    let logs: LogLine[];

    beforeEach(() => {
        root = fs.mkdtempSync(path.join(os.tmpdir(), 'xrh-tier1-'));
        logDir = path.join(root, 'logs');
        logs = [];
    });

    afterEach(async () => {
        await client?.dispose();
        client = undefined;
        fs.rmSync(root, { recursive: true, force: true });
    });

    function newClient(timeoutMs?: number): HostClient {
        client = new HostClient({
            command: HOST_EXE,
            args: ['serve', '--log-dir', logDir, '--log-level', 'Info'],
            timeoutMs,
            log: (level, id, message) => logs.push({ level, id, message }),
        });
        return client;
    }

    it('B.4 신뢰됨: 사용자 컨트롤이 실제로 그려지고 H020/H023(tier=1)이 남는다', async () => {
        const project = createUserProject(root, 'Trusted', userXaml('<s:RedBox/>'));
        const c = newClient();
        const result = await c.request<RenderResult>('render',
            { xaml: userXaml('<s:RedBox/>'), filePath: project.xamlPath, allowProjectAssemblies: true });
        assert.strictEqual(result.width, 40, 'RedBox(폭 40)가 만들어져야 한다');
        assert.deepStrictEqual(result.project, { tier: 1, reason: 'Loaded' });
        assert.deepStrictEqual(result.warnings, []);
        await c.dispose();

        const log = fs.readFileSync(path.join(logDir, 'host.log'), 'utf8');
        assert.ok(/ H020 assembly=SampleControls /.test(log), log);
        assert.ok(/ H023 tier=1 reason=Loaded/.test(log), log);
    });

    it('B.4 미신뢰: 사용자 코드를 로드하지 않고 자리표시자 + 이유 경고가 온다(H020 없음)', async () => {
        const project = createUserProject(root, 'Untrusted', userXaml('<s:Throwing/>'));
        const c = newClient();
        const result = await c.request<RenderResult>('render',
            { xaml: userXaml('<s:Throwing/>'), filePath: project.xamlPath, allowProjectAssemblies: false });
        assert.deepStrictEqual(result.project, { tier: 0, reason: 'Untrusted' });
        const codes = result.warnings.map((w) => w.code);
        assert.ok(codes.includes('PlaceholderUsed') && codes.includes('ProjectTier0'), codes.join(','));
        assert.ok(!codes.includes('UserControlFailed'), '미신뢰에서 사용자 생성자가 실행되면 안 된다');
        await c.dispose();

        const log = fs.readFileSync(path.join(logDir, 'host.log'), 'utf8');
        assert.ok(!/ H020 /.test(log), 'DLL을 로드했다는 기록이 있으면 안 된다');
        assert.ok(/ H023 tier=0 reason=Untrusted/.test(log), log);
    });

    it('B.3 생성자 예외: 그 요소만 오류 자리표시자가 되고 H022가 남는다', async () => {
        const body = '<s:RedBox/><s:Throwing/>';
        const project = createUserProject(root, 'Throwing', userXaml(body));
        const c = newClient();
        const result = await c.request<RenderResult>('render',
            { xaml: userXaml(body), filePath: project.xamlPath, allowProjectAssemblies: true });
        const failed = result.warnings.filter((w) => w.code === 'UserControlFailed');
        assert.strictEqual(failed.length, 1);
        assert.ok(failed[0].message.includes('boom from ctor'), failed[0].message);
        await c.dispose();

        const log = fs.readFileSync(path.join(logDir, 'host.log'), 'utf8');
        assert.ok(/ H022 /.test(log), log);
    });

    it('B.6 결함 주입: 끝나지 않는 생성자 → 타임아웃 → 호스트 kill → 새 호스트에서 정상 렌더', async () => {
        const body = '<s:Hanging/>';
        const project = createUserProject(root, 'Hanging', userXaml(body));
        const c = newClient();
        await c.request('ping');
        const hungPid = c.pid!;

        await assert.rejects(
            c.request('render', { xaml: userXaml(body), filePath: project.xamlPath, allowProjectAssemblies: true }, HANG_TIMEOUT_MS),
            HostTimeoutError);

        // 사용자 코드가 멈춰도 확장은 영향이 없고, 다음 요청은 새 호스트에서 처리된다.
        const ok = await c.request<RenderResult>('render', { xaml: `<Grid ${NS} Width="7" Height="7"/>` });
        assert.strictEqual(ok.width, 7);
        assert.notStrictEqual(c.pid, hungPid);
        const ids = logs.map((l) => l.id).filter((id) => id === 'E010' || id === 'E011');
        assert.deepStrictEqual(ids, ['E010', 'E011', 'E010']);
    });
});
