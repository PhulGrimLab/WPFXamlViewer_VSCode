import { MAX_ZOOM, MIN_ZOOM } from './previewMessages';

/** 줌 버튼 한 번의 배율 변화. */
const ZOOM_STEP = 1.25;

/** 마우스를 이 거리(px) 이상 움직이면 클릭이 아니라 팬(드래그 이동)으로 본다. */
const DRAG_THRESHOLD_PX = 3;

/**
 * 미리보기 웹뷰 HTML을 만든다. 이미지는 data: URI로 표시하며, 스크립트는 nonce가 맞는 것만 허용한다(CSP).
 * 기능(M5.1): 줌(버튼/Ctrl+휠/맞춤/100%), 팬(드래그), 배경 전환(체크무늬/흰색/어둡게), 렌더 크기 지정(W/H), 클릭 → 이미지 좌표 전송,
 * 요소 강조 사각형. 웹뷰 스크립트는 확장이 보낸 메시지를 화면에 반영하고, 상태가 바뀌면 viewState/imageShown으로 회신한다(doc/03 1.1).
 * CSP 때문에 인라인 style 속성은 쓰지 않고(CSSOM으로만 크기 지정) 클래스로 배경을 바꾼다.
 */
export function buildPreviewHtml(nonce: string, cspSource: string): string {
    return `<!DOCTYPE html>
<html lang="ko">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data: ${cspSource}; style-src 'nonce-${nonce}'; script-src 'nonce-${nonce}';">
<style nonce="${nonce}">
  html, body { height: 100%; }
  body { margin: 0; display: flex; flex-direction: column; background: var(--vscode-editor-background); color: var(--vscode-foreground); font-family: var(--vscode-font-family); font-size: var(--vscode-font-size); }
  #toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; padding: 4px 8px; border-bottom: 1px solid var(--vscode-panel-border); }
  #toolbar button, #toolbar select, #toolbar input { font: inherit; color: var(--vscode-input-foreground); background: var(--vscode-input-background); border: 1px solid var(--vscode-input-border, var(--vscode-panel-border)); padding: 1px 6px; }
  #toolbar button { cursor: pointer; }
  #toolbar input { width: 56px; }
  #zoomLabel { min-width: 3.5em; text-align: center; }
  #status { min-height: 1.4em; padding: 2px 8px; }
  #status.error { color: var(--vscode-errorForeground); white-space: pre-wrap; }
  #viewport { flex: 1; overflow: auto; padding: 8px; cursor: grab; }
  #viewport.dragging { cursor: grabbing; }
  #stage { position: relative; display: none; }
  #image { display: block; border: 0; }
  #image.pixelated { image-rendering: pixelated; }
  #highlight { position: absolute; display: none; box-sizing: border-box; border: 2px solid #3b82f6; background: rgba(59, 130, 246, 0.18); pointer-events: none; }
  .bg-checker #image { background-color: #ffffff; background-image: linear-gradient(45deg, #d0d0d0 25%, transparent 25%, transparent 75%, #d0d0d0 75%), linear-gradient(45deg, #d0d0d0 25%, transparent 25%, transparent 75%, #d0d0d0 75%); background-size: 16px 16px; background-position: 0 0, 8px 8px; }
  .bg-white #image { background: #ffffff; }
  .bg-dark #image { background: #1e1e1e; }
</style>
</head>
<body>
<div id="toolbar">
  <button id="zoomOut" title="축소">−</button>
  <span id="zoomLabel">100%</span>
  <button id="zoomIn" title="확대">+</button>
  <button id="zoomFit" title="창에 맞춤">맞춤</button>
  <button id="zoom100" title="실제 크기">100%</button>
  <select id="background" title="배경">
    <option value="checker">체크무늬</option>
    <option value="white">흰색</option>
    <option value="dark">어둡게</option>
  </select>
  <label>W <input id="sizeW" inputmode="numeric" placeholder="자동"></label>
  <label>H <input id="sizeH" inputmode="numeric" placeholder="자동"></label>
  <button id="applySize" title="지정한 크기로 다시 렌더">적용</button>
  <button id="autoSize" title="크기 지정 해제">자동</button>
</div>
<div id="status">XAML 문서를 열어 주세요.</div>
<div id="viewport" class="bg-checker"><div id="stage"><img id="image" alt="XAML preview"><div id="highlight"></div></div></div>
<script nonce="${nonce}">
  const MIN_ZOOM = ${MIN_ZOOM}, MAX_ZOOM = ${MAX_ZOOM}, ZOOM_STEP = ${ZOOM_STEP}, DRAG_THRESHOLD = ${DRAG_THRESHOLD_PX};
  const vscode = acquireVsCodeApi();
  const $ = (id) => document.getElementById(id);
  const status = $('status'), viewport = $('viewport'), stage = $('stage'), image = $('image'), highlight = $('highlight');
  let zoom = 1, background = 'checker', fitMode = false, natural = { w: 0, h: 0 }, highlightRect = null;

  const clampZoom = (z) => Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, z));

  function postState() { vscode.postMessage({ type: 'viewState', zoom, background }); }

  function layout() {
    const w = natural.w * zoom, h = natural.h * zoom;
    image.style.width = w + 'px'; image.style.height = h + 'px';
    stage.style.width = w + 'px'; stage.style.height = h + 'px';
    image.classList.toggle('pixelated', zoom >= 2);
    $('zoomLabel').textContent = Math.round(zoom * 100) + '%';
    if (highlightRect) {
      highlight.style.display = 'block';
      highlight.style.left = highlightRect.x * zoom + 'px'; highlight.style.top = highlightRect.y * zoom + 'px';
      highlight.style.width = highlightRect.w * zoom + 'px'; highlight.style.height = highlightRect.h * zoom + 'px';
    } else {
      highlight.style.display = 'none';
    }
  }

  // 줌을 바꾸되 anchor(뷰포트 안 좌표)가 가리키는 이미지 지점을 화면에서 그대로 유지한다.
  function setZoom(next, anchor) {
    const old = zoom;
    zoom = clampZoom(next);
    const a = anchor || { x: viewport.clientWidth / 2, y: viewport.clientHeight / 2 };
    const imageX = (viewport.scrollLeft + a.x) / old, imageY = (viewport.scrollTop + a.y) / old;
    layout();
    viewport.scrollLeft = imageX * zoom - a.x; viewport.scrollTop = imageY * zoom - a.y;
    postState();
  }

  function fit() {
    if (natural.w > 0 && natural.h > 0) {
      const pad = 16;
      zoom = clampZoom(Math.min((viewport.clientWidth - pad) / natural.w, (viewport.clientHeight - pad) / natural.h));
      layout(); postState();
    }
  }

  function setBackground(next) {
    background = next;
    viewport.className = (viewport.className.includes('dragging') ? 'dragging ' : '') + 'bg-' + next;
    $('background').value = next;
    postState();
  }

  image.addEventListener('load', () => {
    natural = { w: image.naturalWidth, h: image.naturalHeight };
    stage.style.display = 'block';
    if (fitMode) { fit(); } else { layout(); }
    vscode.postMessage({ type: 'imageShown', naturalWidth: image.naturalWidth, naturalHeight: image.naturalHeight });
  });

  window.addEventListener('message', (event) => {
    const m = event.data;
    if (m.type === 'image') {
      status.className = ''; status.textContent = '';
      image.src = 'data:image/png;base64,' + m.png;
    } else if (m.type === 'error') {
      status.className = 'error'; status.textContent = m.message;
    } else if (m.type === 'busy') {
      status.className = ''; status.textContent = '렌더 중...';
    } else if (m.type === 'highlight') {
      highlightRect = m.rect; layout();
    } else if (m.type === 'setView') {
      if (m.background) { setBackground(m.background); }
      if (m.fit) { fitMode = true; fit(); }
      else if (typeof m.zoom === 'number') { fitMode = false; setZoom(m.zoom); }
    }
  });

  $('zoomIn').addEventListener('click', () => { fitMode = false; setZoom(zoom * ZOOM_STEP); });
  $('zoomOut').addEventListener('click', () => { fitMode = false; setZoom(zoom / ZOOM_STEP); });
  $('zoom100').addEventListener('click', () => { fitMode = false; setZoom(1); });
  $('zoomFit').addEventListener('click', () => { fitMode = true; fit(); });
  $('background').addEventListener('change', (e) => setBackground(e.target.value));
  window.addEventListener('resize', () => { if (fitMode) { fit(); } });

  viewport.addEventListener('wheel', (e) => {
    if (e.ctrlKey) {
      e.preventDefault(); fitMode = false;
      const box = viewport.getBoundingClientRect();
      setZoom(zoom * (e.deltaY < 0 ? ZOOM_STEP : 1 / ZOOM_STEP), { x: e.clientX - box.left, y: e.clientY - box.top });
    }
  }, { passive: false });

  // 드래그 = 팬, 거의 움직이지 않은 mouseup = 클릭(이미지 픽셀 좌표를 확장에 보낸다).
  let press = null;
  viewport.addEventListener('mousedown', (e) => {
    if (e.button === 0) { press = { x: e.clientX, y: e.clientY, left: viewport.scrollLeft, top: viewport.scrollTop, dragged: false }; }
  });
  window.addEventListener('mousemove', (e) => {
    if (!press) { return; }
    const dx = e.clientX - press.x, dy = e.clientY - press.y;
    if (!press.dragged && Math.hypot(dx, dy) >= DRAG_THRESHOLD) { press.dragged = true; viewport.classList.add('dragging'); }
    if (press.dragged) { viewport.scrollLeft = press.left - dx; viewport.scrollTop = press.top - dy; }
  });
  window.addEventListener('mouseup', (e) => {
    if (!press) { return; }
    const wasDrag = press.dragged;
    press = null; viewport.classList.remove('dragging');
    if (!wasDrag && natural.w > 0) {
      const box = image.getBoundingClientRect();
      const x = (e.clientX - box.left) / zoom, y = (e.clientY - box.top) / zoom;
      if (x >= 0 && y >= 0 && x < natural.w && y < natural.h) { vscode.postMessage({ type: 'click', x, y }); }
    }
  });

  const parseSize = (text) => { const v = parseInt(text, 10); return Number.isFinite(v) && v > 0 ? v : null; };
  $('applySize').addEventListener('click', () => {
    vscode.postMessage({ type: 'setSize', width: parseSize($('sizeW').value), height: parseSize($('sizeH').value) });
  });
  $('autoSize').addEventListener('click', () => {
    $('sizeW').value = ''; $('sizeH').value = '';
    vscode.postMessage({ type: 'setSize', width: null, height: null });
  });
</script>
</body>
</html>`;
}
