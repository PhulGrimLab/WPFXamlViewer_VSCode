import * as crypto from 'crypto';
import * as vscode from 'vscode';
import { PREVIEW_VIEW_TYPE, RENDER_DEBOUNCE_MS } from './constants';
import { Debouncer } from './debouncer';
import { toZeroBasedPosition } from './diagnostics';
import { HostClient, HostRequestError, RenderWarning } from './hostClient';
import { buildPreviewHtml } from './previewHtml';
import { ToWebviewMessage, parseFromWebview } from './previewMessages';

/** 진단/상태 표시에 쓰는 출처 이름. */
const DIAGNOSTIC_SOURCE = 'WPF XAML Viewer';

/** 웹뷰 nonce 길이(바이트). */
const NONCE_BYTES = 16;

/** 렌더 대상 파일 확장자. */
const XAML_EXTENSION = '.xaml';

/**
 * 미리보기 패널 하나와 렌더 흐름(자동 갱신, 상태 표시줄, Problems)을 소유한다.
 *
 * Owner: extension.ts(activate가 만들고 subscriptions로 dispose). Lifetime: 확장 활성~비활성.
 * 스레드: 확장 호스트 단일 스레드. 패널/디바운서/HostClient는 이 클래스만 접근한다.
 * 렌더 요청은 HostClient.renderLatest로 보내 "최신 요청만" 처리한다. XAML 본문은 로그에 남기지 않는다.
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

    constructor(private readonly _client: HostClient) {
        this._subscriptions.push(
            vscode.workspace.onDidChangeTextDocument((e) => {
                if (e.document === this._document) {
                    this._debouncer.schedule(() => this._renderNow());
                } else {
                    // 미리보기 대상이 아닌 문서의 편집은 무시한다.
                }
            }),
            vscode.window.onDidChangeActiveTextEditor((editor) => {
                if (this._panel && editor && this._isXaml(editor.document) && editor.document !== this._document) {
                    this._document = editor.document;
                    this._renderNow();
                } else {
                    // 패널이 없거나 XAML이 아닌 에디터로 바뀜: 마지막 미리보기를 유지한다.
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
        panel.webview.onDidReceiveMessage((raw) => {
            const message = parseFromWebview(raw);
            if (message) {
                this._lastImageSize = { width: message.naturalWidth, height: message.naturalHeight };
            } else {
                // 알 수 없는 메시지는 무시한다(doc/03 X-M02).
            }
        });
        panel.onDidDispose(() => {
            this._panel = undefined;
            this._debouncer.cancel();
            this._status.hide();
        });
        return panel;
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
        this._client.renderLatest({ xaml: document.getText(), filePath: document.uri.scheme === 'file' ? document.uri.fsPath : undefined }).then(
            (outcome) => {
                if (outcome.discarded) {
                    return; // 더 새 요청이 처리 중이므로 상태는 그 요청이 갱신한다.
                } else {
                    // 최신 결과: 아래에서 반영.
                }
                this._renderCount++;
                this._diagnostics.delete(document.uri);
                this._post({ type: 'image', png: outcome.result.png, width: outcome.result.width, height: outcome.result.height });
                this._showSuccess(outcome.result.warnings);
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
