import * as assert from 'assert';
import * as path from 'path';
import * as vscode from 'vscode';
import { COMMAND_OPEN_PREVIEW } from '../../src/constants';
import { ExtensionTestApi } from '../../src/extension';

const EXTENSION_ID = 'phulgrimlab.wpf-xaml-viewer';

/** 설치 스모크 대기 상한(ms): 첫 렌더는 호스트 시작 + 프로젝트 DLL 로드가 겹친다. */
const RENDER_WAIT_MS = 90_000;

async function waitUntil(condition: () => boolean, timeoutMs = RENDER_WAIT_MS): Promise<void> {
    const start = Date.now();
    while (!condition()) {
        if (Date.now() - start > timeoutMs) {
            throw new Error('조건이 시간 내에 충족되지 않음');
        }
        await new Promise((r) => setTimeout(r, 100));
    }
}

describe('I-09 설치 스모크 (.vsix 설치본 + 번들 호스트 + 실제 net10 WPF 프로젝트)', () => {
    const extensionsDir = process.env.XAMLVIEWER_SMOKE_EXTDIR as string;
    let api: ExtensionTestApi;

    it('설치된 확장(개발 경로가 아님)이 활성화되고 번들 호스트(bin/host)를 쓴다', async () => {
        const extension = vscode.extensions.getExtension<ExtensionTestApi>(EXTENSION_ID);
        assert.ok(extension, '설치된 확장을 찾을 수 없음');
        assert.ok(extension.extensionPath.toLowerCase().startsWith(extensionsDir.toLowerCase()),
            `설치 폴더의 확장이어야 한다: ${extension.extensionPath}`);
        api = await extension.activate();
        assert.ok(api.hostFound, '번들 호스트를 찾지 못함');
        assert.ok(api.hostPath?.toLowerCase().startsWith(extension.extensionPath.toLowerCase()), api.hostPath);
        assert.ok(api.hostPath?.toLowerCase().includes(path.join('bin', 'host').toLowerCase()), api.hostPath);
    });

    it('실제 WPF 프로젝트의 MainWindow.xaml이 미리보기된다(x:Class/이벤트 제거, App.xaml 리소스, 사용자 컨트롤 Tier 1)', async () => {
        const xamlPath = process.env.XAMLVIEWER_SMOKE_XAML as string;
        await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(xamlPath));
        await vscode.commands.executeCommand(COMMAND_OPEN_PREVIEW);
        await waitUntil(() => api.getPreviewState().renderCount > 0 && api.getPreviewState().lastImageSize !== undefined);

        const state = api.getPreviewState();
        assert.deepStrictEqual(state.lastImageSize, { width: 800, height: 450 }, 'Window 크기(800x450)로 그려져야 한다');
        assert.ok(state.warningCodes.includes('RemovedClassAttribute'), state.warningCodes.join(','));
        assert.ok(state.warningCodes.includes('RemovedEventHandler'), state.warningCodes.join(','));
        assert.ok(!state.warningCodes.includes('PlaceholderUsed'), '사용자 컨트롤(Badge)이 자리표시자면 안 된다(Tier 1)');
        assert.ok(!state.warningCodes.includes('ProjectTier0'), state.warningCodes.join(','));
        assert.ok(state.elementCount >= 3, `Window, Button, Badge가 매핑되어야 한다: ${state.elementCount}`);
    });
});
