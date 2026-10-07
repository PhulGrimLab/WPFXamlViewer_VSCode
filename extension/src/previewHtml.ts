/**
 * 미리보기 웹뷰 HTML을 만든다. 이미지는 data: URI로 표시하며, 스크립트는 nonce가 맞는 것만 허용한다(CSP).
 * 웹뷰 스크립트는 확장이 보낸 메시지를 화면에 반영하고, 이미지가 로드되면 실제 크기를 회신한다(doc/03 1.1).
 */
export function buildPreviewHtml(nonce: string, cspSource: string): string {
    return `<!DOCTYPE html>
<html lang="ko">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data: ${cspSource}; style-src 'nonce-${nonce}'; script-src 'nonce-${nonce}';">
<style nonce="${nonce}">
  body { margin: 0; padding: 8px; background: var(--vscode-editor-background); color: var(--vscode-foreground); font-family: var(--vscode-font-family); }
  #status { min-height: 1.4em; margin-bottom: 6px; }
  #status.error { color: var(--vscode-errorForeground); white-space: pre-wrap; }
  #image { display: none; background: #fff; border: 1px solid var(--vscode-panel-border); }
</style>
</head>
<body>
<div id="status">XAML 문서를 열어 주세요.</div>
<img id="image" alt="XAML preview">
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  const status = document.getElementById('status');
  const image = document.getElementById('image');
  image.addEventListener('load', () => {
    vscode.postMessage({ type: 'imageShown', naturalWidth: image.naturalWidth, naturalHeight: image.naturalHeight });
  });
  window.addEventListener('message', (event) => {
    const m = event.data;
    if (m.type === 'image') {
      status.className = ''; status.textContent = '';
      image.src = 'data:image/png;base64,' + m.png;
      image.style.display = 'block';
    } else if (m.type === 'error') {
      status.className = 'error'; status.textContent = m.message;
    } else if (m.type === 'busy') {
      status.className = ''; status.textContent = '렌더 중...';
    }
  });
</script>
</body>
</html>`;
}
