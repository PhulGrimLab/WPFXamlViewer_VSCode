import * as assert from 'assert';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import * as vscode from 'vscode';
import { COMMAND_OPEN_PREVIEW } from '../../src/constants';
import { ExtensionTestApi } from '../../src/extension';

const EXTENSION_ID = 'phulgrimlab.wpf-xaml-live-preview';

/** I-05: 존재하지 않는 호스트 경로 → 안내 알림 + E012 로그, 확장은 계속 살아 있다. */
describe('호스트 없음 통합 (I-05)', () => {
    it('명령 실행 시 오류 안내를 띄우고 확장은 활성 상태를 유지한다', async () => {
        const ext = vscode.extensions.getExtension(EXTENSION_ID);
        assert.ok(ext);
        const api = await ext.activate() as ExtensionTestApi;
        assert.strictEqual(api.hostFound, false);
        assert.ok(api.getLogLines().some((l) => l.includes('E012') && l.includes('host exe not found')));

        const shown: string[] = [];
        const original = vscode.window.showErrorMessage;
        (vscode.window as { showErrorMessage: unknown }).showErrorMessage = (message: string) => {
            shown.push(message);
            return Promise.resolve(undefined);
        };
        try {
            await vscode.commands.executeCommand(COMMAND_OPEN_PREVIEW);
        } finally {
            (vscode.window as { showErrorMessage: unknown }).showErrorMessage = original;
        }
        assert.strictEqual(shown.length, 1);
        assert.ok(shown[0].includes('XamlRenderHost.exe'));
        assert.ok(ext.isActive);
    });

    it('프로젝트 빌드/정리 명령은 호스트가 없어도 등록되어 있다(X-B06)', async () => {
        const ext = vscode.extensions.getExtension(EXTENSION_ID);
        assert.ok(ext);
        await ext.activate();
        const commands = await vscode.commands.getCommands(true);
        for (const id of ['buildProject', 'rebuildProject', 'cleanProject', 'restoreProject', 'buildXamlProject']) {
            assert.ok(commands.includes('wpfXamlViewer.' + id), id + ' 명령이 등록되지 않았다');
        }
    });

    it('우클릭 빌드/정리 명령이 그 프로젝트만 dotnet으로 실행한다(X-B07)', async function () {
        this.timeout(120000);
        const ext = vscode.extensions.getExtension(EXTENSION_ID);
        assert.ok(ext);
        await ext.activate();
        const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'xv build 한글-'));
        const project = path.join(dir, 'Tiny.csproj');
        fs.writeFileSync(project, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>');
        fs.writeFileSync(path.join(dir, 'P.cs'), 'class P { }');
        const run = async (command: string): Promise<number | undefined> => {
            const ended = new Promise<number | undefined>((resolve) => {
                const sub = vscode.tasks.onDidEndTaskProcess((e) => {
                    if (e.execution.task.name.endsWith('Tiny.csproj')) {
                        sub.dispose();
                        resolve(e.exitCode);
                    }
                });
            });
            await vscode.commands.executeCommand(command, vscode.Uri.file(project));
            return ended;
        };
        try {
            assert.strictEqual(await run('wpfXamlViewer.buildProject'), 0);
            assert.ok(fs.existsSync(path.join(dir, 'bin', 'Debug')), '빌드 산출물(bin/Debug)이 없다');
            assert.strictEqual(await run('wpfXamlViewer.cleanProject'), 0);
            assert.ok(!fs.existsSync(path.join(dir, 'bin', 'Debug', 'Tiny.dll')), '정리 후에도 Tiny.dll이 남아 있다');
        } finally {
            fs.rmSync(dir, { recursive: true, force: true });
        }
    });
});
