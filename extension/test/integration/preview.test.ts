import * as assert from 'assert';
import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { COMMAND_OPEN_PREVIEW } from '../../src/constants';
import { ExtensionTestApi } from '../../src/extension';
import { getLanguage } from '../../src/i18n';
import { createUserProject } from '../support/userProject';

const NS = 'xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"';
const VALID_XAML = `<Button ${NS} Width="120" Height="40" Content="Hello"/>`;
const INVALID_XAML = `<Button ${NS} Width="120" Height="40" Content="Hello">`;
const EXTENSION_ID = 'phulgrimlab.wpf-xaml-live-preview';

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

    it('I-01b .xaml을 열기만 해도(명령 실행 없이) 미리보기가 자동으로 열려 그려진다', async () => {
        await waitUntil(() => api.getPreviewState().lastImageSize !== undefined);
        assert.deepStrictEqual(api.getPreviewState().lastImageSize, { width: 120, height: 40 });
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

    it('I-10 신뢰된 워크스페이스: 프로젝트 DLL의 사용자 컨트롤이 실제로 그려진다(Tier 1)', async () => {
        const xamlPath = process.env.XAMLVIEWER_TEST_USERPROJECT_XAML as string;
        // 프로젝트 코드 실행은 프로젝트별로 사용자가 허용해야 한다(테스트는 허용 기록을 미리 넣는다).
        await api.grantProjectConsent(path.join(path.dirname(xamlPath), 'SampleControls.csproj'));
        const doc = await vscode.workspace.openTextDocument(xamlPath);
        assert.ok(vscode.workspace.isTrusted, '이 실행은 --disable-workspace-trust 이므로 신뢰 상태여야 한다');
        await vscode.window.showTextDocument(doc);

        const before = api.getPreviewState().renderCount;
        await vscode.commands.executeCommand(COMMAND_OPEN_PREVIEW);
        await waitUntil(() => api.getPreviewState().renderCount > before);
        // RedBox는 폭 40 x 높이 20. 자리표시자였다면 텍스트 폭에 따라 훨씬 커진다.
        await waitUntil(() => api.getPreviewState().lastImageSize?.width === 40);
        assert.deepStrictEqual(api.getPreviewState().lastImageSize, { width: 40, height: 20 });
        assert.ok(api.getLogLines().some((l) => l.includes('E010')), '호스트가 시작되어야 한다');

        // 다음 테스트를 위해 원래 문서를 다시 활성화한다.
        // (미리보기 탭이 아닌 일반 편집기는 다른 문서를 열면 교체되어 기존 TextEditor가 닫히므로 새로 받아 둔다.)
        editor = await vscode.window.showTextDocument(editor.document);
    });

    it('I-11 빌드 산출물이 없으면 프로젝트를 자동 빌드하고 다시 그린다(빌드 성공)', async function () {
        this.timeout(180000); // 첫 빌드는 복원 때문에 오래 걸릴 수 있다.
        const workspace = process.env.XAMLVIEWER_TEST_WORKSPACE as string;
        const csproj = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>SampleControls</AssemblyName></PropertyGroup></Project>';
        const project = createUserProject(workspace, 'AutoBuildOk',
            '<StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:s="clr-namespace:SampleControls;assembly=SampleControls"><s:RedBox/></StackPanel>', false, csproj);
        await api.grantProjectConsent(path.join(project.dir, 'SampleControls.csproj'));
        const rendersBefore = api.getPreviewState().renderCount;
        await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(project.xamlPath));
        await vscode.commands.executeCommand(COMMAND_OPEN_PREVIEW);
        await waitUntil(() => api.getLogLines().some((l) => l.includes('autoBuild end ok=true')), 120000);
        assert.ok(fs.existsSync(path.join(project.dir, 'bin')), '자동 빌드가 bin을 만들어야 한다');
        // 처음 렌더(산출물 없음) + 빌드 뒤 다시 그리기 = 2번. 빌드가 끝나자마자 다시 그려질 수 있으므로 시작 전 횟수를 기준으로 센다.
        await waitUntil(() => api.getPreviewState().renderCount >= rendersBefore + 2, 30000);
        editor = await vscode.window.showTextDocument(editor.document);
    });

    it('I-12 자동 빌드가 실패하면 실패를 알리고 다시 빌드를 반복하지 않는다', async () => {
        const workspace = process.env.XAMLVIEWER_TEST_WORKSPACE as string;
        // TargetFramework가 없는 SDK 프로젝트는 빌드가 실패한다.
        const project = createUserProject(workspace, 'AutoBuildFail',
            '<StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:s="clr-namespace:SampleControls;assembly=SampleControls"><s:RedBox/></StackPanel>', false);
        await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(project.xamlPath));
        await api.grantProjectConsent(path.join(project.dir, 'SampleControls.csproj'));
        const startsBefore = api.getLogLines().filter((l) => l.includes('autoBuild start')).length;
        await vscode.commands.executeCommand(COMMAND_OPEN_PREVIEW);
        await waitUntil(() => api.getLogLines().filter((l) => l.includes('autoBuild end ok=false')).length >= 1, 120000);
        await new Promise((r) => setTimeout(r, 1500)); // 실패 직후 반복 빌드가 시작되지 않는지 잠시 지켜본다.
        const starts = api.getLogLines().filter((l) => l.includes('autoBuild start')).length - startsBefore;
        assert.strictEqual(starts, 1, '실패한 프로젝트를 다시 자동 빌드하면 안 된다');
        editor = await vscode.window.showTextDocument(editor.document);
    });

    it('I-13 표시 언어 설정을 바꾸면 바로 적용되고, 미리보기가 새 언어로 다시 그려진다', async () => {
        const config = vscode.workspace.getConfiguration('wpfXamlViewer');
        // 앞 테스트가 다른 문서를 보고 있을 수 있다: 정상 문서를 활성화하고 Open Preview로 그 문서를 미리보기 대상으로 확정한 뒤, 그려질 때까지 기다린다.
        editor = await vscode.window.showTextDocument(editor.document);
        const opened = api.getPreviewState().renderCount;
        await vscode.commands.executeCommand(COMMAND_OPEN_PREVIEW);
        await waitUntil(() => api.getPreviewState().renderCount > opened);
        try {
            // 시작 언어가 무엇이든 "바뀌는" 전환이 되도록 먼저 한국어로 고정한 뒤 영어로 바꾼다(같은 언어로 설정하면 바뀐 게 없어 다시 그리지 않는다).
            await config.update('language', 'ko', vscode.ConfigurationTarget.Global);
            await waitUntil(() => getLanguage() === 'ko');
            const before = api.getPreviewState().renderCount;
            await config.update('language', 'en', vscode.ConfigurationTarget.Global);
            await waitUntil(() => getLanguage() === 'en');
            await waitUntil(() => api.getPreviewState().renderCount > before); // 새 언어로 다시 그렸다.
            await config.update('language', 'ko', vscode.ConfigurationTarget.Global);
            await waitUntil(() => getLanguage() === 'ko');
        } finally {
            await config.update('language', undefined, vscode.ConfigurationTarget.Global);
        }
        // auto: VS Code 표시 언어를 따른다(한국어면 한국어, 아니면 영어).
        const expected = vscode.env.language.toLowerCase().startsWith('ko') ? 'ko' : 'en';
        await waitUntil(() => getLanguage() === expected);
    });

    it('I-14 프로젝트 코드 실행은 사용자가 허용하기 전에는 하지 않고(빌드된 DLL이 있어도), 허용하면 그린다', async () => {
        const workspace = process.env.XAMLVIEWER_TEST_WORKSPACE as string;
        const xaml = '<StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:s="clr-namespace:SampleControls;assembly=SampleControls"><s:RedBox/></StackPanel>';
        const project = createUserProject(workspace, 'ConsentGate', xaml); // 빌드된 DLL이 이미 있다(저장소에 커밋된 bin처럼)
        const hostLoadsBefore = api.getLogLines().filter((l) => l.includes('H020')).length;
        await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(project.xamlPath));
        const before = api.getPreviewState().renderCount;
        await vscode.commands.executeCommand(COMMAND_OPEN_PREVIEW);
        await waitUntil(() => api.getPreviewState().renderCount > before);
        // 허용 전: 사용자 컨트롤(폭 40)이 실제로 만들어지면 안 된다(자리표시자라 폭이 다르다).
        assert.notStrictEqual(api.getPreviewState().lastImageSize?.width, 40, '허용 전에는 프로젝트 DLL을 불러오면 안 된다');

        await api.grantProjectConsent(path.join(project.dir, 'SampleControls.csproj'));
        const afterDeny = api.getPreviewState().renderCount;
        await vscode.commands.executeCommand(COMMAND_OPEN_PREVIEW);
        await waitUntil(() => api.getPreviewState().renderCount > afterDeny);
        await waitUntil(() => api.getPreviewState().lastImageSize?.width === 40);
        assert.ok(hostLoadsBefore >= 0);
        editor = await vscode.window.showTextDocument(editor.document);
    });

    /** M5 시험용: 여러 요소가 있는 XAML을 워크스페이스에 만들어 열고 미리보기를 렌더한 뒤 요소 수가 채워질 때까지 기다린다. */
    async function openPreviewOf(fileName: string, xaml: string, expectedElements: number): Promise<vscode.TextEditor> {
        const file = path.join(process.env.XAMLVIEWER_TEST_WORKSPACE as string, fileName);
        fs.writeFileSync(file, xaml);
        const opened = await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(file));
        const before = api.getPreviewState().renderCount;
        await vscode.commands.executeCommand(COMMAND_OPEN_PREVIEW);
        await waitUntil(() => api.getPreviewState().renderCount > before && api.getPreviewState().elementCount === expectedElements);
        return opened;
    }

    const HIT_XAML = `<Grid ${NS} Width="200" Height="100">\n`
        + '  <Button Width="80" Height="30" HorizontalAlignment="Left" VerticalAlignment="Top"/>\n'
        + '  <Border Width="60" Height="40" HorizontalAlignment="Right" VerticalAlignment="Bottom">\n'
        + '    <Rectangle Width="20" Height="10" Fill="Red"/>\n'
        + '  </Border>\n'
        + '</Grid>';

    it('I-06 미리보기 클릭 → 에디터 selection이 해당 요소의 줄/열로 이동하고 그 요소가 강조된다', async () => {
        const hit = await openPreviewOf('Hit.xaml', HIT_XAML, 4);

        api.simulateWebviewMessage({ type: 'click', x: 10, y: 10 }); // Button(2행, 열 3)
        await waitUntil(() => vscode.window.activeTextEditor?.selection.active.line === 1);
        assert.strictEqual(vscode.window.activeTextEditor?.selection.active.character, 2, "'<' 위치(열 3 → 0-base 2)");
        assert.strictEqual(api.getPreviewState().highlightedId, 'e1');

        api.simulateWebviewMessage({ type: 'click', x: 170, y: 80 }); // Border 안 Rectangle(4행)
        await waitUntil(() => vscode.window.activeTextEditor?.selection.active.line === 3);
        assert.strictEqual(api.getPreviewState().highlightedId, 'e3', '가장 안쪽 요소가 선택되어야 한다');

        api.simulateWebviewMessage({ type: 'click', x: 199.5, y: 0.5 }); // 빈 곳(Grid만 있는 영역)
        await waitUntil(() => api.getPreviewState().highlightedId === 'e0');
        assert.ok(hit.document.uri.fsPath.endsWith('Hit.xaml'));
    });

    it('I-06b 에디터 커서 이동 → 가장 안쪽 요소가 미리보기에서 강조된다', async () => {
        const hit = await openPreviewOf('HitCursor.xaml', HIT_XAML, 4);
        hit.selection = new vscode.Selection(new vscode.Position(3, 8), new vscode.Position(3, 8)); // Rectangle 줄 안
        await waitUntil(() => api.getPreviewState().highlightedId === 'e3');
        hit.selection = new vscode.Selection(new vscode.Position(0, 3), new vscode.Position(0, 3)); // Grid 시작 태그 안
        await waitUntil(() => api.getPreviewState().highlightedId === 'e0');
        hit.selection = new vscode.Selection(new vscode.Position(8, 0), new vscode.Position(8, 0)); // 문서 밖(마지막 줄 뒤)
        await waitUntil(() => api.getPreviewState().highlightedId === undefined);
    });

    it('I-05b 웹뷰 보기 상태: 확장이 줌/배경을 지정하면 웹뷰가 적용하고 viewState로 회신한다', async () => {
        await openPreviewOf('View.xaml', HIT_XAML, 4);
        api.setView({ zoom: 2, background: 'dark' });
        await waitUntil(() => api.getPreviewState().viewState?.zoom === 2 && api.getPreviewState().viewState?.background === 'dark');
        api.setView({ fit: true });
        await waitUntil(() => {
            const zoom = api.getPreviewState().viewState?.zoom;
            return zoom !== undefined && zoom !== 2;
        });
        const fitted = api.getPreviewState().viewState!.zoom;
        assert.ok(fitted >= 0.1 && fitted <= 8, `맞춤 배율이 허용 범위여야 한다: ${fitted}`);
        api.setView({ zoom: 1, background: 'checker' });
        await waitUntil(() => api.getPreviewState().viewState?.zoom === 1 && api.getPreviewState().viewState?.background === 'checker');
    });

    it('I-05c 렌더 크기 지정: setSize → 다시 렌더되어 이미지 크기가 바뀌고 자동으로 되돌릴 수 있다', async () => {
        // 명시 크기가 없고 콘텐츠 크기는 10x10(최소 크기). 요청 크기가 있으면 루트가 그 크기로 늘어난다.
        await openPreviewOf('Size.xaml', `<Border ${NS} Background="Red" MinWidth="10" MinHeight="10"/>`, 1);
        await waitUntil(() => api.getPreviewState().lastImageSize?.width === 10);

        api.simulateWebviewMessage({ type: 'setSize', width: 300, height: 150 });
        await waitUntil(() => api.getPreviewState().lastImageSize?.width === 300 && api.getPreviewState().lastImageSize?.height === 150);
        assert.deepStrictEqual(api.getPreviewState().size, { width: 300, height: 150 });

        api.simulateWebviewMessage({ type: 'setSize', width: null, height: null });
        await waitUntil(() => api.getPreviewState().lastImageSize?.width === 10);
        assert.deepStrictEqual(api.getPreviewState().size, {});

        api.simulateWebviewMessage({ type: 'setSize', width: -5, height: 10 }); // 잘못된 값은 무시된다.
        await new Promise((r) => setTimeout(r, 300));
        assert.deepStrictEqual(api.getPreviewState().size, {});

        // 다음 테스트(I-04)를 위해 원래 문서를 다시 활성화한다.
        const original = await vscode.workspace.openTextDocument(path.join(process.env.XAMLVIEWER_TEST_WORKSPACE as string, 'Test.xaml'));
        editor = await vscode.window.showTextDocument(original);
        await waitUntil(() => api.getPreviewState().elementCount === 1);
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
