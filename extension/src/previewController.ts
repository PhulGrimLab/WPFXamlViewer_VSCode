import * as crypto from 'crypto';
import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { AutoBuildState, MissingBuildAction, WARNING_PROJECT_TIER0, decideMissingBuild, describeMissingBuild } from './autoBuild';
import { CONFIG_AUTO_BUILD, CONFIG_AUTO_OPEN, CONFIG_SECTION, PREVIEW_VIEW_TYPE, RENDER_DEBOUNCE_MS } from './constants';
import { Debouncer } from './debouncer';
import { toZeroBasedPosition } from './diagnostics';
import { findNearestProject } from './dotnetCommands';
import { HitElement, pickElementAt, pickElementAtCursor } from './hitTest';
import { HostClient, HostRequestError, LogFn, RenderWarning } from './hostClient';
import { getLanguage, t } from './i18n';
import { LogId } from './logFormat';
import { buildProjectAndWait } from './projectBuild';
import { ProjectConsent } from './projectConsent';
import { canRunProjectCode } from './workspaceTrust';
import { buildPreviewHtml } from './previewHtml';
import { Background, ToWebviewMessage, parseFromWebview } from './previewMessages';
import { DOTNET_DESKTOP_RUNTIME_URL, dotNetRuntimeMissingMessage, isDotNetRuntimeMissing } from './runtimeCheck';

/** 진단/상태 표시에 쓰는 출처 이름. */
const DIAGNOSTIC_SOURCE = 'WPF XAML Live Preview';

/** 웹뷰 nonce 길이(바이트). */
const NONCE_BYTES = 16;

/** 렌더 대상 파일 확장자. */
const XAML_EXTENSION = '.xaml';

/** 웹뷰의 보기 상태(테스트가 읽을 수 있도록 마지막 회신값을 보관한다). */
export interface ViewState {
    zoom: number;
    background: Background;
}

/**
 * 미리보기 패널 하나와 렌더 흐름(자동 갱신, 상태 표시줄, Problems), 클릭 ↔ 에디터 줄 이동, 보기 상태/렌더 크기를 소유한다.
 *
 * Owner: extension.ts(activate가 만들고 subscriptions로 dispose). Lifetime: 확장 활성~비활성.
 * 스레드: 확장 호스트 단일 스레드. 패널/디바운서/HostClient/요소 목록은 이 클래스만 접근한다.
 * 렌더 요청은 HostClient.renderLatest로 보내 "최신 요청만" 처리한다. XAML 본문은 로그에 남기지 않는다.
 * 요소 목록(_elements)은 **마지막으로 반영한 렌더**의 것이다. 편집 직후 새 렌더가 도착하기 전까지는 약간 낡았을 수 있다.
 */
export class PreviewController implements vscode.Disposable {
    private _panel: vscode.WebviewPanel | undefined;
    private _document: vscode.TextDocument | undefined;
    private readonly _debouncer = new Debouncer(RENDER_DEBOUNCE_MS);
    private readonly _diagnostics = vscode.languages.createDiagnosticCollection(DIAGNOSTIC_SOURCE);
    private readonly _status = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left);
    private readonly _subscriptions: vscode.Disposable[] = [];
    private _renderCount = 0;
    private _lastImageSize: { width: number; height: number } | undefined;
    private _elements: HitElement[] = [];
    private _highlightedId: string | undefined;
    private _viewState: ViewState | undefined;
    private _size: { width?: number; height?: number } = {};
    private _warningCodes: string[] = [];
    /** 프로젝트(.csproj)별 자동 빌드 상태. 산출물이 있는 렌더가 성공하면 비운다. */
    private readonly _autoBuild = new Map<string, AutoBuildState>();
    /** 이미 알림을 띄운 (상태, 문서) 조합. 편집할 때마다 같은 알림이 반복되지 않게 한다. */
    private readonly _notified = new Set<string>();

    /** 이번 세션에 이미 허용 여부를 물은 프로젝트 키(같은 질문을 반복하지 않는다. 거절하면 다시 열 때까지 조용히 Tier 0로 그린다). */
    private readonly _consentAsked = new Set<string>();

    constructor(private readonly _client: HostClient, private readonly _log: LogFn = () => undefined, private readonly _consent: ProjectConsent = new ProjectConsent({ get: (_k, d) => d, update: () => Promise.resolve() })) {
        this._subscriptions.push(
            vscode.workspace.onDidChangeTextDocument((e) => {
                if (e.document === this._document) {
                    this._debouncer.schedule(() => this._renderNow());
                } else {
                    // 미리보기 대상이 아닌 문서의 편집은 무시한다.
                }
            }),
            vscode.workspace.onDidGrantWorkspaceTrust(() => {
                // 폴더를 신뢰하는 순간 사용자 컨트롤을 실제로 그리도록 다시 렌더한다.
                this._renderNow();
            }),
            vscode.window.onDidChangeActiveTextEditor((editor) => this.handleActiveEditor(editor)),
            vscode.window.onDidChangeTextEditorSelection((e) => {
                if (e.textEditor.document === this._document) {
                    this._highlightForCursor(e.selections[0].active);
                } else {
                    // 미리보기 대상이 아닌 문서의 커서는 무시한다.
                }
            }),
        );
    }

    /**
     * 활성 에디터가 바뀌었을 때(또는 활성화 직후 이미 열려 있던 에디터에 대해) 호출한다. XAML이면 패널을 재사용해 갱신하고
     * (같은 문서로 돌아와도 새로 그린다), 패널이 없으면 자동 열기 설정에 따라 연다. 활성화 이벤트가 에디터 전환보다 늦게 와서
     * 첫 파일의 전환 이벤트를 놓치므로 activate가 직접 한 번 호출한다.
     */
    handleActiveEditor(editor: vscode.TextEditor | undefined): void {
        if (editor && this._isXaml(editor.document) && (this._panel || this._isAutoOpenEnabled())) {
            this._document = editor.document;
            this._panel ??= this._createPanel();
            this._renderNow();
        } else {
            // XAML이 아닌 에디터이거나 패널이 없는데 자동 열기가 꺼짐: 마지막 미리보기를 유지한다.
        }
    }

    /** 표시 언어가 바뀌었다: 웹뷰 도구 모음을 새 언어로 다시 만들고 다시 그린다(줌 같은 보기 상태는 초기화된다). */
    applyLanguage(): void {
        if (this._panel) {
            this._panel.webview.html = buildPreviewHtml(crypto.randomBytes(NONCE_BYTES).toString('hex'), this._panel.webview.cspSource, getLanguage());
            this._renderNow();
        } else {
            // 열린 미리보기가 없다: 다음에 여는 패널이 새 언어로 만들어진다.
        }
    }

    /** 테스트/진단용: 지금까지 반영한 렌더 횟수. */
    get renderCount(): number {
        return this._renderCount;
    }

    /** 테스트용: 웹뷰가 마지막으로 회신한 이미지 크기. */
    get lastImageSize(): { width: number; height: number } | undefined {
        return this._lastImageSize;
    }

    /** 테스트용: 마지막 렌더가 보낸 요소 수. */
    get elementCount(): number {
        return this._elements.length;
    }

    /** 테스트용: 현재 강조 중인 요소 id(없으면 undefined). */
    get highlightedId(): string | undefined {
        return this._highlightedId;
    }

    /** 테스트용: 웹뷰가 마지막으로 회신한 보기 상태(줌/배경). */
    get viewState(): ViewState | undefined {
        return this._viewState;
    }

    /** 테스트용: 마지막 성공 렌더가 보고한 경고 코드들. */
    get warningCodes(): string[] {
        return [...this._warningCodes];
    }

    /** 테스트용: 사용자가 지정한 렌더 크기. */
    get size(): { width?: number; height?: number } {
        const result: { width?: number; height?: number } = {};
        if (this._size.width !== undefined) {
            result.width = this._size.width;
        } else {
            // 폭 자동.
        }
        if (this._size.height !== undefined) {
            result.height = this._size.height;
        } else {
            // 높이 자동.
        }
        return result;
    }

    /** 테스트용: 웹뷰가 보냈을 법한 메시지를 실제 수신 경로와 같은 처리 함수로 넣는다(브라우저 DOM 이벤트는 만들 수 없으므로). */
    simulateWebviewMessage(raw: unknown): void {
        void this._handleWebviewMessage(raw);
    }

    /** 테스트용: 확장 → 웹뷰 보기 상태 지정(웹뷰가 적용하고 viewState로 회신한다). */
    setView(message: { zoom?: number; background?: Background; fit?: boolean }): void {
        this._post({ type: 'setView', ...message });
    }

    /** 명령 진입점: 활성 에디터의 XAML을 에디터 옆 패널에 미리보기한다. XAML이 아니면 안내만 한다. */
    open(): void {
        const editor = vscode.window.activeTextEditor;
        if (!editor || !this._isXaml(editor.document)) {
            void vscode.window.showInformationMessage(t('preview.openXaml'));
            return;
        } else {
            this._document = editor.document;
        }

        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.Beside, true);
        } else {
            this._panel = this._createPanel();
        }
        this._autoBuild.clear(); // 사용자가 미리보기를 다시 열었다: 이전 빌드 실패 기록을 지우고 새로 판단한다.
        this._notified.clear();
        this._renderNow();
    }

    dispose(): void {
        this._debouncer.cancel();
        this._panel?.dispose();
        this._diagnostics.dispose();
        this._status.dispose();
        this._subscriptions.forEach((s) => s.dispose());
    }

    private _isAutoOpenEnabled(): boolean {
        return vscode.workspace.getConfiguration(CONFIG_SECTION).get<boolean>(CONFIG_AUTO_OPEN, true);
    }

    private _isXaml(document: vscode.TextDocument): boolean {
        return (document.uri.scheme === 'file' || document.uri.scheme === 'untitled') && document.fileName.toLowerCase().endsWith(XAML_EXTENSION);
    }

    private _createPanel(): vscode.WebviewPanel {
        const panel = vscode.window.createWebviewPanel(
            PREVIEW_VIEW_TYPE, 'WPF XAML Preview', { viewColumn: vscode.ViewColumn.Beside, preserveFocus: true },
            { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [] });
        const nonce = crypto.randomBytes(NONCE_BYTES).toString('hex');
        panel.webview.html = buildPreviewHtml(nonce, panel.webview.cspSource, getLanguage());
        panel.webview.onDidReceiveMessage((raw) => void this._handleWebviewMessage(raw));
        panel.onDidDispose(() => {
            this._panel = undefined;
            this._debouncer.cancel();
            this._diagnostics.clear();
            this._status.hide();
            this._elements = [];
            this._highlightedId = undefined;
        });
        return panel;
    }

    /** 웹뷰가 보낸 메시지를 검증하고 처리한다. 알 수 없거나 형식이 틀린 메시지는 무시한다(doc/03 X-M02). */
    private async _handleWebviewMessage(raw: unknown): Promise<void> {
        const message = parseFromWebview(raw);
        if (!message) {
            return;
        } else {
            // 유효한 메시지: 종류별 처리.
        }

        switch (message.type) {
            case 'imageShown':
                this._lastImageSize = { width: message.naturalWidth, height: message.naturalHeight };
                break;
            case 'viewState':
                this._viewState = { zoom: message.zoom, background: message.background };
                break;
            case 'setSize':
                this._size = { width: message.width ?? undefined, height: message.height ?? undefined };
                this._renderNow();
                break;
            case 'click': {
                const element = pickElementAt(this._elements, message.x, message.y);
                if (element) {
                    this._setHighlight(element);
                    await this._revealInEditor(element);
                } else {
                    // 요소가 없는 빈 곳을 클릭: 강조를 해제한다.
                    this._setHighlight(undefined);
                }
                break;
            }
        }
    }

    /** 요소의 시작 위치로 에디터 커서를 옮기고 화면에 보이게 한다(클릭 → 줄 이동, M5.3). 문서가 보이는 에디터가 없으면 연다. */
    private async _revealInEditor(element: HitElement): Promise<void> {
        const document = this._document;
        if (!document) {
            return;
        } else {
            // 대상 문서 있음.
        }

        const editor = vscode.window.visibleTextEditors.find((e) => e.document === document)
            ?? await vscode.window.showTextDocument(document, { viewColumn: vscode.ViewColumn.One, preserveFocus: false });
        const start = new vscode.Position(Math.max(element.line - 1, 0), Math.max(element.col - 1, 0));
        editor.selection = new vscode.Selection(start, start);
        editor.revealRange(new vscode.Range(start, start), vscode.TextEditorRevealType.InCenterIfOutsideViewport);
    }

    /** 에디터 커서가 있는 가장 안쪽 요소를 미리보기에서 강조한다(M5.3 커서 → 하이라이트). */
    private _highlightForCursor(position: vscode.Position): void {
        this._setHighlight(pickElementAtCursor(this._elements, position.line + 1, position.character + 1));
    }

    /** 강조 요소를 바꾸고 웹뷰에 알린다(undefined면 해제). 같은 요소면 다시 보내지 않는다. */
    private _setHighlight(element: HitElement | undefined): void {
        if (element?.id === this._highlightedId) {
            return;
        } else {
            this._highlightedId = element?.id;
            this._post({ type: 'highlight', rect: element ? { x: element.x, y: element.y, w: element.w, h: element.h } : null });
        }
    }

    /** 현재 문서를 호스트에 렌더 요청하고 결과를 웹뷰/Problems/상태 표시줄에 반영한다. */
    private _renderNow(): void {
        const document = this._document;
        if (!document || !this._panel) {
            return;
        } else {
            // 렌더 대상 있음.
        }

        const gate = this._projectGate(document);
        this._setStatus(t('preview.rendering'), undefined);
        this._post({ type: 'busy' });
        this._askConsentIfNeeded(gate);
        this._client.renderLatest({
            lang: getLanguage(),
            xaml: document.getText(),
            filePath: document.uri.scheme === 'file' ? document.uri.fsPath : undefined,
            // 사용자 코드를 실행하는 Tier 1은 신뢰된 워크스페이스에서만 허용한다(doc/01 3.3 규칙 1).
            allowProjectAssemblies: gate.allow,
            workspaceRoot: gate.workspaceRoot,
            width: this._size.width,
            height: this._size.height,
        }).then(
            (outcome) => {
                if (outcome.discarded) {
                    return; // 더 새 요청이 처리 중이므로 상태는 그 요청이 갱신한다.
                } else {
                    // 최신 결과: 아래에서 반영.
                }
                this._renderCount++;
                this._diagnostics.delete(document.uri);
                this._elements = outcome.result.elements;
                this._highlightedId = undefined; // 새 렌더의 좌표계: 강조를 다시 계산한다.
                this._post({ type: 'image', png: outcome.result.png, width: outcome.result.width, height: outcome.result.height });
                this._warningCodes = outcome.result.warnings.map((w) => w.code);
                this._showSuccess(outcome.result.warnings);
                this._afterSuccess(document, outcome.result.project?.reason, outcome.result.warnings);
                const cursor = vscode.window.visibleTextEditors.find((e) => e.document === document)?.selection.active;
                if (cursor) {
                    this._highlightForCursor(cursor);
                } else {
                    // 보이는 에디터가 없어 커서를 알 수 없다.
                }
            },
            (error: Error) => this._showFailure(document, error));
    }

    /** 성공 표시: 호스트가 변경/대체한 내용(경고)이 있으면 개수를 보여 주고 툴팁에 목록을 둔다. */
    private _showSuccess(warnings: RenderWarning[]): void {
        if (warnings.length === 0) {
            this._setStatus(t('preview.ok'), undefined);
            this._status.tooltip = undefined;
        } else {
            this._setStatus(t('preview.okWarnings', warnings.length), undefined);
            const lines = warnings.map((w) => `${w.line ? `L${w.line} ` : ''}${w.message}`);
            this._status.tooltip = lines.join('\n');
        }
    }

    /** 렌더 실패 처리: 호스트 오류는 Problems에, 그 외(타임아웃/크래시/시작 불가)는 알림에 표시한다. */
    private _showFailure(document: vscode.TextDocument, error: Error): void {
        const missing = error instanceof HostRequestError ? this._missingBuildAction(document, error.projectReason, true) : 'none';
        this._setStatus(t('preview.error'), new vscode.ThemeColor('statusBarItem.errorBackground'));
        this._post({ type: 'error', message: error.message });
        if (missing !== 'none') {
            // 빌드 산출물이 없어 실패했다: 자동 빌드(또는 빌드 필요/실패 안내)로 이어간다. 원래 XAML 오류도 Problems에 함께 남긴다.
            this._reportMissingBuild(document, missing, true);
        } else {
            // 산출물 문제가 아닌 일반 실패.
        }
        if (error instanceof HostRequestError) {
            const pos = toZeroBasedPosition(error.line, error.col, document.lineCount);
            const lineEnd = document.lineAt(pos.line).range.end.character;
            const range = new vscode.Range(pos.line, pos.col, pos.line, Math.max(lineEnd, pos.col + 1));
            const diagnostic = new vscode.Diagnostic(range, `${error.code}: ${error.message}`, vscode.DiagnosticSeverity.Error);
            diagnostic.source = DIAGNOSTIC_SOURCE;
            this._diagnostics.set(document.uri, [diagnostic]);
        } else if (isDotNetRuntimeMissing(error)) {
            // 호스트가 .NET Desktop Runtime 부재로 시작하지 못했다: 원인과 설치 경로를 알려 준다(I-12).
            const download = t('runtime.openPage');
            void vscode.window.showErrorMessage(dotNetRuntimeMissingMessage(), download).then((choice) => {
                if (choice === download) {
                    void vscode.env.openExternal(vscode.Uri.parse(DOTNET_DESKTOP_RUNTIME_URL));
                } else {
                    // 안내만 닫음.
                }
            });
        } else {
            void vscode.window.showErrorMessage(t('preview.hostFailure', error.message));
        }
    }

    /** 렌더 성공 후: 산출물이 있으면 자동 빌드 기록을 비우고, 산출물이 없어 자리표시자가 생겼으면 자동 빌드/안내를 시작한다. */
    private _afterSuccess(document: vscode.TextDocument, reason: string | undefined, warnings: RenderWarning[]): void {
        if (reason === 'Loaded') {
            this._autoBuild.clear();
            this._notified.clear(); // 프로젝트 DLL을 불러왔다: 이전 실패/대기 기록을 비운다(나중에 다시 정리해도 자동 빌드가 동작하도록).
            return;
        } else {
            // 산출물 없음 여부를 아래에서 판단.
        }
        const placeholdersShown = warnings.some((w) => w.code === WARNING_PROJECT_TIER0);
        const action = this._missingBuildAction(document, reason, placeholdersShown);
        if (action !== 'none') {
            this._reportMissingBuild(document, action, false);
        } else {
            // 빌드가 필요한 상황이 아니다.
        }
    }

    /** 이 문서가 속한 프로젝트와 현재 설정/신뢰/상태로 "산출물 부재" 상황의 처리를 결정한다. */
    private _missingBuildAction(document: vscode.TextDocument, reason: string | undefined, needed: boolean): MissingBuildAction {
        const project = this._projectOf(document);
        return decideMissingBuild({
            reason,
            needed,
            autoBuildEnabled: vscode.workspace.getConfiguration(CONFIG_SECTION).get<boolean>(CONFIG_AUTO_BUILD, true),
            // 신뢰된 워크스페이스이고 XAML과 프로젝트가 모두 열린 폴더 안에 있으며 사용자가 이 프로젝트의 코드 실행을 허용했을 때만 자동 빌드한다.
            trusted: this._projectGate(document).allow,
            project,
            state: project ? this._autoBuild.get(project) : undefined,
        });
    }

    /**
     * 이 문서의 프로젝트 코드(빌드/DLL 로드)를 실행해도 되는가. 모두 만족해야 한다: ① 신뢰된 워크스페이스이고 XAML이 열린 폴더 안에 있다 ② 가장 가까운 .csproj도 열린 폴더 안에 있다
     * (하위 폴더만 열었을 때 위쪽 폴더의 프로젝트 DLL이 실행되는 것을 막는다) ③ 사용자가 이 프로젝트(.csproj, 없으면 공유 프로젝트가 있는 워크스페이스 폴더)의 코드 실행을 허용했다.
     * `needsConsent`가 true면 ①②는 통과했고 ③만 남은 상태다(허용을 물어야 한다).
     */
    private _projectGate(document: vscode.TextDocument): { allow: boolean; needsConsent: boolean; key?: string; workspaceRoot?: string } {
        const file = document.uri.scheme === 'file' ? document.uri.fsPath : undefined;
        if (!file || !canRunProjectCode(file)) {
            return { allow: false, needsConsent: false };
        } else {
            // 신뢰된 폴더 안의 파일.
        }
        const workspaceRoot = vscode.workspace.getWorkspaceFolder(document.uri)?.uri.fsPath;
        const project = this._projectOf(document);
        if (project && !canRunProjectCode(project)) {
            return { allow: false, needsConsent: false, workspaceRoot }; // 프로젝트가 열린 폴더 밖이다.
        } else {
            // 프로젝트가 없거나 폴더 안에 있다.
        }
        // 프로젝트가 없어도 공유 프로젝트(.projitems)에 속한 XAML이면 그것을 가져가는 앱 프로젝트가 대상이라 워크스페이스 폴더를 키로 허용을 받는다.
        const key = project ?? (workspaceRoot && this._hasSharedProject(file) ? workspaceRoot : undefined);
        if (!key) {
            return { allow: false, needsConsent: false, workspaceRoot }; // 로드할 프로젝트가 없다(일반 렌더).
        } else if (!this._consent.isGranted(key)) {
            return { allow: false, needsConsent: true, key, workspaceRoot };
        } else {
            return { allow: true, needsConsent: false, key, workspaceRoot };
        }
    }

    /** XAML 폴더에서 위로 3단계 안에 `.projitems`(공유 프로젝트)가 있는가. */
    private _hasSharedProject(xamlPath: string): boolean {
        let directory = path.dirname(xamlPath);
        for (let level = 0; level <= 3; level++) {
            try {
                if (fs.readdirSync(directory).some((entry) => entry.toLowerCase().endsWith('.projitems'))) {
                    return true;
                } else {
                    // 이 폴더에는 없다.
                }
            } catch {
                // 읽을 수 없는 폴더: 위로 계속.
            }
            const parent = path.dirname(directory);
            if (parent === directory) {
                break;
            } else {
                directory = parent;
            }
        }
        return false;
    }

    /** 허용이 필요한 프로젝트를 이번 세션에 아직 묻지 않았으면 한 번 묻는다. 허용하면 기록하고 다시 그린다. 거절하면 이번 세션에는 다시 묻지 않는다. */
    private _askConsentIfNeeded(gate: { needsConsent: boolean; key?: string }): void {
        const key = gate.key;
        if (!gate.needsConsent || !key || this._consentAsked.has(key)) {
            return;
        } else {
            this._consentAsked.add(key);
        }
        const allow = t('consent.allow');
        void vscode.window.showWarningMessage(t('consent.ask', path.basename(key)), { modal: false }, allow, t('consent.deny')).then(async (choice) => {
            if (choice === allow) {
                await this._consent.grant(key);
                this._renderNow();
            } else {
                this._setStatus(t('consent.denied'), undefined);
            }
        });
    }

    /** 테스트용: 이 프로젝트(또는 워크스페이스 폴더)의 코드 실행을 허용한 것으로 기록한다. */
    async grantConsent(key: string): Promise<void> {
        await this._consent.grant(key);
    }

    /** 모든 허용 기록을 지우고 이번 세션의 질문 기록도 비운다(명령 "프로젝트 코드 실행 허용 초기화"). */
    async resetConsent(): Promise<void> {
        await this._consent.clear();
        this._consentAsked.clear();
        void vscode.window.showInformationMessage(t('consent.reset'));
        this._renderNow();
    }

    private _projectOf(document: vscode.TextDocument): string | undefined {
        return document.uri.scheme === 'file' ? findNearestProject(document.uri.fsPath, (directory) => fs.readdirSync(directory)) : undefined;
    }

    /** 결정에 따라 빌드를 시작하거나 안내를 표시한다. `failed`: 이번 렌더가 실패했는가(오류 화면을 이미 띄웠다). */
    private _reportMissingBuild(document: vscode.TextDocument, action: MissingBuildAction, failed: boolean): void {
        const project = this._projectOf(document);
        const message = describeMissingBuild(action, document.uri.fsPath, project);
        if (action === 'build' && project) {
            void this._buildThenRender(document, project, message);
        } else if (action === 'wait') {
            this._setStatus(t('preview.buildRunning'), undefined);
        } else {
            // 자동 빌드를 하지 않는다(설정 꺼짐/미신뢰/실패 이력 등): 왜 미리보기가 안 되는지 알린다.
            this._setStatus(t('preview.buildNeeded'), new vscode.ThemeColor('statusBarItem.errorBackground'));
            if (failed) {
                this._post({ type: 'error', message });
            } else {
                // 렌더는 성공(자리표시자)했으므로 이미지를 유지하고 상태 표시줄 툴팁으로만 알린다.
            }
            this._status.tooltip = message;
            this._notifyBuildNeeded(document, action, message);
        }
    }

    /** 프로젝트를 빌드하고(진행을 미리보기에 표시), 성공하면 다시 렌더하고, 실패하면 그 사실을 표시한다. */
    private async _buildThenRender(document: vscode.TextDocument, project: string, message: string): Promise<void> {
        if (this._autoBuild.get(project) === 'building') {
            return; // 같은 프로젝트의 빌드가 이미 진행 중이다(알림의 "다시 빌드"를 연달아 눌러도 빌드는 하나만 돈다).
        } else {
            // 새 빌드.
        }
        this._autoBuild.set(project, 'building');
        this._notified.clear();
        this._setStatus(t('preview.buildRunning'), undefined);
        this._status.tooltip = message;
        this._post({ type: 'busy' });
        this._post({ type: 'error', message });
        this._log('Info', LogId.ProjectBuild, `autoBuild start project=${project.split(/[\\/]/).pop()}`);
        const outcome = await buildProjectAndWait(project, this._log);
        this._log('Info', LogId.ProjectBuild, `autoBuild end ok=${outcome.ok} exitCode=${outcome.exitCode ?? 'n/a'}`);
        if (!this._panel) {
            this._autoBuild.delete(project); // 그 사이 미리보기가 닫혔다: 다음에 다시 판단한다.
            return;
        } else {
            // 미리보기가 살아 있다.
        }
        // 빌드하는 동안 다른 XAML로 바뀌었을 수 있다: 상태는 프로젝트 단위로 기록하고, 끝나면 **지금 보는 문서**를 다시 그려 같은 프로젝트의 문서가 "빌드 중"에 멈추지 않게 한다.
        // 성공: 다시 그렸을 때 산출물을 찾으면(Loaded) 상태를 비운다. 못 찾으면 'noOutput'으로 반복을 막고 안내한다. 실패: 'failed'로 두면 다시 그릴 때 실패 안내가 나온다.
        this._autoBuild.set(project, outcome.ok ? 'noOutput' : 'failed');
        this._renderNow();
    }

    /** 빌드가 필요/실패했음을 알림으로 알린다(같은 안내를 반복해 띄우지 않도록 "다시 빌드" 선택지만 준다). */
    private _notifyBuildNeeded(document: vscode.TextDocument, action: MissingBuildAction, message: string): void {
        const key = `${action}|${document.uri.fsPath}`;
        if (this._notified.has(key)) {
            return;
        } else {
            this._notified.add(key);
        }
        const retry = t('auto.retry');
        const buttons = action === 'failedBefore' || action === 'noOutput' || action === 'disabled' ? [retry] : [];
        void vscode.window.showWarningMessage(`WPF XAML Live Preview: ${message}`, ...buttons).then((choice) => {
            if (choice === retry) {
                const project = this._projectOf(document);
                if (project) {
                    void this._buildThenRender(document, project, describeMissingBuild('build', document.uri.fsPath, project));
                } else {
                    // 프로젝트를 찾지 못해 다시 빌드할 수 없다.
                }
            } else {
                // 알림만 닫음.
            }
        });
    }

    private _post(message: ToWebviewMessage): void {
        void this._panel?.webview.postMessage(message);
    }

    private _setStatus(text: string, background: vscode.ThemeColor | undefined): void {
        this._status.text = text;
        this._status.backgroundColor = background;
        this._status.show();
    }
}
