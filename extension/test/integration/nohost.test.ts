import * as assert from 'assert';
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
});
