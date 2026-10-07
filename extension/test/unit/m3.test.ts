import * as assert from 'assert';
import * as path from 'path';
import { Debouncer } from '../../src/debouncer';
import { toZeroBasedPosition } from '../../src/diagnostics';
import { resolveHostExe } from '../../src/hostLocator';
import { buildPreviewHtml } from '../../src/previewHtml';
import { parseFromWebview } from '../../src/previewMessages';

describe('Debouncer (X-D01)', () => {
    it('연속 호출은 마지막 한 번만 실행한다', async () => {
        const d = new Debouncer(50);
        let count = 0;
        d.schedule(() => count++);
        d.schedule(() => count++);
        d.schedule(() => count++);
        await new Promise((r) => setTimeout(r, 150));
        assert.strictEqual(count, 1);
    });

    it('cancel 하면 실행되지 않는다', async () => {
        const d = new Debouncer(30);
        let count = 0;
        d.schedule(() => count++);
        d.cancel();
        await new Promise((r) => setTimeout(r, 100));
        assert.strictEqual(count, 0);
    });
});

describe('toZeroBasedPosition (X-G01, X-G02)', () => {
    it('1-base 줄/열을 0-base로 바꾼다', () => {
        assert.deepStrictEqual(toZeroBasedPosition(3, 5, 10), { line: 2, col: 4 });
    });
    it('줄 정보가 없으면 문서 첫 줄이다', () => {
        assert.deepStrictEqual(toZeroBasedPosition(undefined, undefined, 10), { line: 0, col: 0 });
    });
    it('문서보다 큰 줄은 마지막 줄로 보정한다', () => {
        assert.deepStrictEqual(toZeroBasedPosition(99, 1, 4), { line: 3, col: 0 });
    });
});

describe('resolveHostExe (I-05)', () => {
    const root = path.resolve('/ext');
    it('후보가 없으면 undefined', () => {
        assert.strictEqual(resolveHostExe(root, () => false, undefined), undefined);
    });
    it('환경 변수 재정의가 우선한다', () => {
        assert.strictEqual(resolveHostExe(root, () => true, 'C:\\x\\host.exe'), 'C:\\x\\host.exe');
    });
    it('재정의 경로가 없으면 다른 후보로 대체하지 않고 undefined', () => {
        assert.strictEqual(resolveHostExe(root, (p) => p !== 'C:\\x\\host.exe', 'C:\\x\\host.exe'), undefined);
    });
    it('패키징 위치가 개발 위치보다 우선한다', () => {
        const found = resolveHostExe(root, () => true, undefined);
        assert.ok(found?.includes(path.join('bin', 'host')), found);
    });
    it('개발 빌드 위치만 있으면 그것을 쓴다', () => {
        const found = resolveHostExe(root, (p) => p.includes('net10.0-windows'), undefined);
        assert.ok(found?.includes('net10.0-windows'));
    });
});

describe('웹뷰 메시지 (X-M01, X-M02)', () => {
    it('올바른 imageShown 메시지를 받아들인다', () => {
        assert.deepStrictEqual(parseFromWebview({ type: 'imageShown', naturalWidth: 10, naturalHeight: 20 }),
            { type: 'imageShown', naturalWidth: 10, naturalHeight: 20 });
    });
    it('알 수 없거나 형식이 틀린 메시지는 무시한다', () => {
        assert.strictEqual(parseFromWebview(null), undefined);
        assert.strictEqual(parseFromWebview({ type: 'evil' }), undefined);
        assert.strictEqual(parseFromWebview({ type: 'imageShown', naturalWidth: 'x', naturalHeight: 1 }), undefined);
    });
    it('HTML은 nonce 기반 CSP를 포함한다', () => {
        const html = buildPreviewHtml('abc', 'vscode-resource:');
        assert.ok(html.includes("script-src 'nonce-abc'"));
        assert.ok(html.includes('<script nonce="abc">'));
    });
});
