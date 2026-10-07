import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { runTests } from '@vscode/test-electron';

/** 이 머신에 설치된 VS Code(있으면 다운로드를 피한다). XAMLVIEWER_VSCODE_EXE로 바꿀 수 있다. */
const DEFAULT_VSCODE_EXE = 'C:\Program Files\Microsoft VS Code\Code.exe';

/**
 * 통합 테스트 실행기(T4/T5). 임시 --user-data-dir/--extensions-dir로 개발자 VS Code 환경을 건드리지 않는다.
 * 실제 호스트 exe가 먼저 빌드되어 있어야 한다(doc/run_tests.ps1).
 */
async function main(): Promise<void> {
    const extensionDevelopmentPath = path.resolve(__dirname, '../../..');
    const extensionTestsPath = path.resolve(__dirname, 'index');
    const workspace = fs.mkdtempSync(path.join(os.tmpdir(), 'xamlviewer-ws-'));
    const userDataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'xamlviewer-ud-'));
    const extensionsDir = fs.mkdtempSync(path.join(os.tmpdir(), 'xamlviewer-ext-'));
    const exe = process.env.XAMLVIEWER_VSCODE_EXE ?? (fs.existsSync(DEFAULT_VSCODE_EXE) ? DEFAULT_VSCODE_EXE : undefined);

    await runTests({
        vscodeExecutablePath: exe,
        extensionDevelopmentPath,
        extensionTestsPath,
        extensionTestsEnv: { XAMLVIEWER_TEST_WORKSPACE: workspace },
        launchArgs: [workspace, '--user-data-dir', userDataDir, '--extensions-dir', extensionsDir, '--disable-extensions', '--disable-workspace-trust'],
    });
}

main().catch((e) => {
    console.error(e);
    process.exit(1);
});
