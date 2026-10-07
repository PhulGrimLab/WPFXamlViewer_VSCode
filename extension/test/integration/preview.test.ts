import * as assert from 'assert';
import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { COMMAND_OPEN_PREVIEW } from '../../src/constants';
import { ExtensionTestApi } from '../../src/extension';

const NS = 'xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"';
const VALID_XAML = `<Button ${NS} Width="120" Height="40" Content="Hello"/>`;
const INVALID_XAML = `<Button ${NS} Width="120" Height="40" Content="Hello">`;
const EXTENSION_ID = 'phulgrimlab.wpf-xaml-viewer';

async function waitUntil(condition: () => boolean, timeoutMs = 20000): Promise<void> {
    const start = Date.now();
    while (!condition()) {
        if (Date.now() - start > timeoutMs) {
            throw new Error('조건이 시간 내에 충족되지 않음');
        }
        await new Promise((r) => setTimeout(r, 50));
    }
}

/** 문서 전체를 새 텍스트로 바꾼다(편집 이벤트를 발생시킨다). */
async function replaceAll(editor: vscode.TextEditor, text: string): Promise<void> {
    const full = new vscode.Range(0, 0, editor.document.lineCount, 0);
    await editor.edit((b) => b.replace(full, text));
}

describe('미리보기 통합 (실제 VS Code + 실제 호스트)', () => {
    let api: ExtensionTestApi;
    let editor: vscode.TextEditor;

    before(async () => {
        const ext = vscode.extensions.getExtension<ExtensionTestApi | undefined>(EXTENSION_ID);
        assert.ok(ext, '확장을 찾을 수 없음');
        api = await ext.activate() as ExtensionTestApi;
        assert.ok(api.hostFound, '호스트 exe를 찾지 못함(호스트를 먼저 빌드하세요)');

        const file = path.join(process.env.XAMLVIEWER_TEST_WORKSPACE as string, 'Test.xaml');
        fs.writeFileSync(file, VALID_XAML);
        const doc = await vscode.workspace.openTextDocument(file);
        editor = await vscode.window.showTextDocument(doc);
    });

    it('I-01 확장이 활성화되고 명령이 등록된다', async () => {
        const commands = await vscode.commands.getCommands(true);
        assert.ok(commands.includes(COMMAND_OPEN_PREVIEW));
    });

    it('I-02 Open Preview: 웹뷰가 이미지를 그리고 크기를 회신한다', async () => {
        await vscode.commands.executeCommand(COMMAND_OPEN_PREVIEW);
        await waitUntil(() => api.getPreviewState().lastImageSize !== undefined);
        const size = api.getPreviewState().lastImageSize;
        assert.deepStrictEqual(size, { width: 120, height: 40 });
    });

    it('I-03 오류 편집 → Problems 생성, 정상 편집 → 해제', async () => {
        await replaceAll(editor, INVALID_XAML);
        await waitUntil(() => vscode.languages.getDiagnostics(editor.document.uri).length > 0);
        const diag = vscode.languages.getDiagnostics(editor.document.uri)[0];
        assert.strictEqual(diag.severity, vscode.DiagnosticSeverity.Error);

        const before = api.getPreviewState().renderCount;
        await replaceAll(editor, VALID_XAML);
        await waitUntil(() => vscode.languages.getDiagnostics(editor.document.uri).length === 0);
        assert.ok(api.getPreviewState().renderCount > before);
    });

    it('I-04 호스트 강제 종료 → 다음 편집에서 자동 복구(E012 후 E010)', async () => {
        const oldPid = api.getHostPid();
        assert.ok(oldPid, '호스트가 실행 중이어야 함');
        process.kill(oldPid);
        await waitUntil(() => api.getLogLines().some((l) => l.includes('E012')));

        const before = api.getPreviewState().renderCount;
        await replaceAll(editor, VALID_XAML + '\n');
        await waitUntil(() => api.getPreviewState().renderCount > before);
        const newPid = api.getHostPid();
        assert.ok(newPid && newPid !== oldPid, `새 호스트가 떠야 함 old=${oldPid} new=${newPid}`);

        const lines = api.getLogLines();
        const crashIndex = lines.findIndex((l) => l.includes('E012'));
        assert.ok(lines.slice(crashIndex + 1).some((l) => l.includes('E010')), 'E012 이후 E010(재시작) 로그가 있어야 함');
    });
});
