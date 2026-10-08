import * as assert from 'assert';
import { HitElement, pickElementAt, pickElementAtCursor } from '../../src/hitTest';
import { parseFromWebview } from '../../src/previewMessages';

/** 테스트용 요소 만들기: 줄/열/끝과 경계. */
function el(id: string, line: number, col: number, endLine: number, endCol: number, x: number, y: number, w: number, h: number): HitElement {
    return { id, line, col, endLine, endCol, x, y, w, h };
}

describe('pickElementAt (클릭 → 요소)', () => {
    // 선위 순회: 부모 e0, 자식 e1(겹침 아래), 자식 e2(겹침 위).
    const elements = [
        el('e0', 1, 1, 6, 8, 0, 0, 100, 100),
        el('e1', 2, 3, 2, 40, 20, 20, 60, 60),
        el('e2', 3, 3, 3, 40, 30, 30, 40, 40),
    ];

    it('점을 포함하는 마지막(가장 위/안쪽) 요소를 고른다', () => {
        assert.strictEqual(pickElementAt(elements, 35, 35)?.id, 'e2');
        assert.strictEqual(pickElementAt(elements, 25, 25)?.id, 'e1');
        assert.strictEqual(pickElementAt(elements, 5, 5)?.id, 'e0');
    });

    it('경계는 왼쪽/위 포함, 오른쪽/아래 제외', () => {
        assert.strictEqual(pickElementAt(elements, 0, 0)?.id, 'e0');
        assert.strictEqual(pickElementAt(elements, 100, 100), undefined);
        assert.strictEqual(pickElementAt(elements, 70, 70)?.id, 'e1', '위 요소(30..70)의 오른쪽 경계는 제외되어 아래 요소가 선택된다');
    });

    it('요소가 없거나 바깥이면 undefined', () => {
        assert.strictEqual(pickElementAt([], 1, 1), undefined);
        assert.strictEqual(pickElementAt(elements, 500, 5), undefined);
    });
});

describe('pickElementAtCursor (커서 → 요소)', () => {
    const elements = [
        el('e0', 1, 1, 6, 8, 0, 0, 100, 100), // <Grid> ... </Grid>
        el('e1', 2, 3, 4, 12, 0, 0, 10, 10), // 여러 줄 요소
        el('e2', 3, 5, 3, 30, 0, 0, 10, 10), // e1 안의 한 줄 요소
    ];

    it('커서를 포함하는 가장 안쪽(시작이 가장 늦은) 요소', () => {
        assert.strictEqual(pickElementAtCursor(elements, 3, 10)?.id, 'e2');
        assert.strictEqual(pickElementAtCursor(elements, 3, 40)?.id, 'e1', 'e2가 끝난 뒤 같은 줄은 바깥 요소');
        assert.strictEqual(pickElementAtCursor(elements, 5, 1)?.id, 'e0');
    });

    it('시작 위치는 포함, 끝 위치는 제외', () => {
        assert.strictEqual(pickElementAtCursor(elements, 2, 3)?.id, 'e1');
        assert.strictEqual(pickElementAtCursor(elements, 2, 2)?.id, 'e0');
        assert.strictEqual(pickElementAtCursor(elements, 6, 8), undefined, 'e0의 끝 위치(6,8) 자체는 제외');
    });

    it('어떤 요소에도 속하지 않으면 undefined', () => {
        assert.strictEqual(pickElementAtCursor(elements, 9, 1), undefined);
        assert.strictEqual(pickElementAtCursor([], 1, 1), undefined);
    });
});

describe('웹뷰 메시지 확장 (X-M01, X-M02)', () => {
    it('click/viewState/setSize를 받아들인다', () => {
        assert.deepStrictEqual(parseFromWebview({ type: 'click', x: 1.5, y: 2 }), { type: 'click', x: 1.5, y: 2 });
        assert.deepStrictEqual(parseFromWebview({ type: 'viewState', zoom: 2, background: 'dark' }), { type: 'viewState', zoom: 2, background: 'dark' });
        assert.deepStrictEqual(parseFromWebview({ type: 'setSize', width: 300, height: null }), { type: 'setSize', width: 300, height: null });
    });

    it('범위/형식이 틀린 값은 무시한다', () => {
        assert.strictEqual(parseFromWebview({ type: 'click', x: 'a', y: 2 }), undefined);
        assert.strictEqual(parseFromWebview({ type: 'click', x: Number.NaN, y: 2 }), undefined);
        assert.strictEqual(parseFromWebview({ type: 'viewState', zoom: 100, background: 'dark' }), undefined, '줌 상한 초과');
        assert.strictEqual(parseFromWebview({ type: 'viewState', zoom: 1, background: 'rainbow' }), undefined);
        assert.strictEqual(parseFromWebview({ type: 'setSize', width: 0, height: 10 }), undefined, '0 이하');
        assert.strictEqual(parseFromWebview({ type: 'setSize', width: 100000, height: 10 }), undefined, '최대 초과');
        assert.strictEqual(parseFromWebview({ type: 'setSize', width: '10', height: 10 }), undefined);
    });
});

describe('웹뷰 HTML/스크립트 (X-M03)', () => {
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    const vm = require('vm') as typeof import('vm');
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    const { buildPreviewHtml } = require('../../src/previewHtml') as typeof import('../../src/previewHtml');

    it('내장 스크립트가 문법 오류 없이 컴파일된다(브라우저 없이 가능한 최소 검증)', () => {
        const html = buildPreviewHtml('abc', 'vscode-webview:');
        const match = /<script nonce="abc">([\s\S]*?)<\/script>/.exec(html);
        assert.ok(match, 'script 블록이 있어야 한다');
        assert.doesNotThrow(() => new vm.Script(match[1]), '웹뷰 스크립트 문법 오류');
    });

    it('CSP가 인라인 스타일 속성을 허용하지 않으므로 HTML 마크업에 style="..." 속성이 없다', () => {
        const markup = buildPreviewHtml('abc', 'x').replace(/<style[\s\S]*?<\/style>/, '').replace(/<script[\s\S]*?<\/script>/, '');
        assert.ok(!/\sstyle\s*=/.test(markup), 'style 속성 발견');
    });
});
