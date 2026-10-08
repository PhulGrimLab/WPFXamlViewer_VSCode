import * as crypto from 'crypto';
import * as vscode from 'vscode';
import { PREVIEW_VIEW_TYPE, RENDER_DEBOUNCE_MS } from './constants';
import { Debouncer } from './debouncer';
import { toZeroBasedPosition } from './diagnostics';
import { HitElement, pickElementAt, pickElementAtCursor } from './hitTest';
import { HostClient, HostRequestError, RenderWarning } from './hostClient';
import { buildPreviewHtml } from './previewHtml';
import { Background, ToWebviewMessage, parseFromWebview } from './previewMessages';

/** 진단/상태 표시에 쓰는 출처 이름. */
const DIAGNOSTIC_SOURCE = 'WPF XAML Viewer';

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

    constructor(private readonly _client: HostClient) {
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
            vscode.window.onDidChangeActiveTextEditor((editor) => {
                if (this._panel && editor && this._isXaml(editor.document) && editor.document !== this._document) {
                    this._document = editor.document;
                    this._renderNow();
                } else {
                    // 패널이 없거나 XAML이 아닌 에디터로 바뀜: 마지막 미리보기를 유지한다.
                }
            }),
            vscode.window.onDidChangeTextEditorSelection((e) => {
                if (e.textEditor.document === this._document) {
                    this._highlightForCursor(e.selections[0].active);
                } else {
                    // 미리보기 대상이 아닌 문서의 커서는 무시한다.
                }
            }),
        );
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
            void vscode.window.showInformationMessage('WPF XAML: .xaml 파일을 연 상태에서 실행해 주세요.');
            return;
        } else {
            this._document = editor.document;
        }

        if (this._panel) {
            this._panel.reveal(vscode.ViewColumn.Beside, true);
        } else {
            this._panel = this._createPanel();
        }
        this._renderNow();
    }

    dispose(): void {
        this._debouncer.cancel();
        this._panel?.dispose();
        this._diagnostics.dispose();
        this._status.dispose();
        this._subscriptions.forEach((s) => s.dispose());
    }

    private _isXaml(document: vscode.TextDocument): boolean {
        return document.fileName.toLowerCase().endsWith(XAML_EXTENSION);
    }

    private _createPanel(): vscode.WebviewPanel {
        const panel = vscode.window.createWebviewPanel(
            PREVIEW_VIEW_TYPE, 'WPF XAML Preview', { viewColumn: vscode.ViewColumn.Beside, preserveFocus: true },
            { enableScripts: true, retainContextWhenHidden: true });
        const nonce = crypto.randomBytes(NONCE_BYTES).toString('hex');
        panel.webview.html = buildPreviewHtml(nonce, panel.webview.cspSource);
        panel.webview.onDidReceiveMessage((raw) => void this._handleWebviewMessage(raw));
        panel.onDidDispose(() => {
            this._panel = undefined;
            this._debouncer.cancel();
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

        this._setStatus('$(sync~spin) XAML 렌더 중', undefined);
        this._post({ type: 'busy' });
        this._client.renderLatest({
            xaml: document.getText(),
            filePath: document.uri.scheme === 'file' ? document.uri.fsPath : undefined,
            // 사용자 코드를 실행하는 Tier 1은 신뢰된 워크스페이스에서만 허용한다(doc/01 3.3 규칙 1).
            allowProjectAssemblies: vscode.workspace.isTrusted,
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
                this._showSuccess(outcome.result.warnings);
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
            this._setStatus('$(check) XAML OK', undefined);
            this._status.tooltip = undefined;
        } else {
            this._setStatus(`$(check) XAML OK (경고 ${warnings.length})`, undefined);
            const lines = warnings.map((w) => `${w.line ? `L${w.line} ` : ''}${w.message}`);
            this._status.tooltip = lines.join('\n');
        }
    }

    /** 렌더 실패 처리: 호스트 오류는 Problems에, 그 외(타임아웃/크래시/시작 불가)는 알림에 표시한다. */
    private _showFailure(document: vscode.TextDocument, error: Error): void {
        this._setStatus('$(error) XAML 오류', new vscode.ThemeColor('statusBarItem.errorBackground'));
        this._post({ type: 'error', message: error.message });
        if (error instanceof HostRequestError) {
            const pos = toZeroBasedPosition(error.line, error.col, document.lineCount);
            const lineEnd = document.lineAt(pos.line).range.end.character;
            const range = new vscode.Range(pos.line, pos.col, pos.line, Math.max(lineEnd, pos.col + 1));
            const diagnostic = new vscode.Diagnostic(range, `${error.code}: ${error.message}`, vscode.DiagnosticSeverity.Error);
            diagnostic.source = DIAGNOSTIC_SOURCE;
            this._diagnostics.set(document.uri, [diagnostic]);
        } else {
            void vscode.window.showErrorMessage(`WPF XAML Viewer: ${error.message} (자세한 내용은 출력 채널 확인)`);
        }
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
