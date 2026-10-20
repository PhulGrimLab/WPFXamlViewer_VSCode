import * as assert from 'assert';
import { STRINGS, resolveLanguage, setLanguage, t, tIn } from '../../src/i18n';
import { buildPreviewHtml } from '../../src/previewHtml';
import { describeMissingBuild } from '../../src/autoBuild';
import { loadNls, readManifestText } from '../support/nls';

/** X-L: 한국어/영어 표시 지원(문구 표, 언어 결정, 매니페스트 번역, 웹뷰). */
describe('다국어 (X-L)', () => {
    afterEach(() => setLanguage('ko'));

    it('X-L01 설정과 VS Code 표시 언어로 언어를 정한다(auto는 표시 언어, 한국어가 아니면 영어)', () => {
        assert.strictEqual(resolveLanguage('auto', 'ko'), 'ko');
        assert.strictEqual(resolveLanguage('auto', 'ko-KR'), 'ko');
        assert.strictEqual(resolveLanguage('auto', 'en'), 'en');
        assert.strictEqual(resolveLanguage('auto', 'ja'), 'en');
        assert.strictEqual(resolveLanguage('auto', undefined), 'en');
        assert.strictEqual(resolveLanguage('en', 'ko'), 'en', '명시한 설정이 우선');
        assert.strictEqual(resolveLanguage('ko', 'en'), 'ko');
        assert.strictEqual(resolveLanguage('이상한값', 'ko'), 'ko', '모르는 값은 auto처럼');
    });

    it('X-L02 두 언어의 문구 키가 같고, 비어 있지 않고, {n} 자리표시자가 같다', () => {
        const ko = Object.keys(STRINGS.ko).sort();
        const en = Object.keys(STRINGS.en).sort();
        assert.deepStrictEqual(en, ko);
        for (const key of ko as (keyof typeof STRINGS.ko)[]) {
            assert.ok(STRINGS.ko[key].length > 0 && STRINGS.en[key].length > 0, `${key} 비어 있음`);
            const holes = (text: string) => (text.match(/\{\d+\}/g) ?? []).sort().join(',');
            // 영어/한국어 문장 구조가 달라 한쪽이 인자를 안 쓰는 경우(예: auto.noOutput의 {0})는 허용하되, 쓰는 인자는 서로 포함 관계여야 한다.
            const a = holes(STRINGS.ko[key]), b = holes(STRINGS.en[key]);
            assert.ok(a === b || a.includes(b) || b.includes(a), `${key}: 자리표시자가 다름 (${a} / ${b})`);
        }
    });

    it('X-L03 t()는 현재 언어로 인자를 채우고, tIn()은 지정한 언어를 쓴다', () => {
        setLanguage('en');
        assert.strictEqual(t('preview.okWarnings', 3), '$(check) XAML OK (3 warnings)');
        assert.strictEqual(tIn('ko', 'preview.okWarnings', 3), '$(check) XAML OK (경고 3)');
        setLanguage('ko');
        assert.strictEqual(t('preview.ok'), '$(check) XAML OK');
    });

    it('X-L04 자동 빌드 안내가 현재 언어로 나온다', () => {
        setLanguage('en');
        const en = describeMissingBuild('failedBefore', 'C:/w/App/MainWindow.xaml', 'C:/w/App/App.csproj');
        assert.ok(en.includes('must be built first') && en.includes('failed') && en.includes('MainWindow.xaml') && en.includes('App.csproj'));
        setLanguage('ko');
        assert.ok(describeMissingBuild('failedBefore', 'C:/w/App/MainWindow.xaml', 'C:/w/App/App.csproj').includes('먼저 빌드되어야'));
    });

    it('X-L05 package.nls(영어)와 package.nls.ko가 같은 키를 가지고, package.json의 모든 %키%가 둘 다에 있다', () => {
        const en = loadNls('en'), ko = loadNls('ko');
        assert.deepStrictEqual(Object.keys(en).sort(), Object.keys(ko).sort());
        const used = [...readManifestText().matchAll(/"%([^%"]+)%"/g)].map((m) => m[1]);
        assert.ok(used.length > 20);
        for (const key of used) {
            assert.ok(key in en && key in ko, `번역 누락: ${key}`);
        }
        for (const key of Object.keys(en)) {
            assert.ok(used.includes(key), `package.json이 쓰지 않는 번역 키: ${key}`);
        }
    });

    it('X-L06 웹뷰 도구 모음이 언어에 맞게 만들어진다(스크립트의 상태 문구 포함)', () => {
        const ko = buildPreviewHtml('n', 'x', 'ko');
        const en = buildPreviewHtml('n', 'x', 'en');
        assert.ok(ko.includes('<html lang="ko">') && ko.includes('체크무늬') && ko.includes('렌더 중...'));
        assert.ok(en.includes('<html lang="en">') && en.includes('Checkerboard') && en.includes('Rendering...'));
        assert.ok(!en.includes('체크무늬'));
        assert.ok(ko.includes('id="refresh"') && ko.includes('새로고침') && en.includes('id="refresh"') && en.includes('>&#x21BB; Refresh<'), '새로고침 버튼이 툴바 맨 앞에 있어야 한다');
        assert.ok(ko.indexOf('id="refresh"') < ko.indexOf('id="zoomOut"'));
        // 접근성: 툴바 역할/라벨, 상태 영역의 aria-live, 경고 목록 영역, 지역화된 이미지 대체 텍스트
        for (const [html, label, alt] of [[ko, '미리보기 도구 모음', 'XAML 미리보기'], [en, 'Preview toolbar', 'XAML preview']]) {
            assert.ok(html.includes('role="toolbar"') && html.includes(`aria-label="${label}"`), '툴바 role/aria-label');
            assert.ok(html.includes('role="status" aria-live="polite"'), '상태 영역 aria-live');
            assert.ok(html.includes('id="warnings"') && html.includes('id="warningsList"'), '경고 목록 영역');
            assert.ok(html.includes(`alt="${alt}"`), '이미지 대체 텍스트');
            assert.ok(/<button id="zoomIn"[^>]*aria-label=/.test(html), '버튼 aria-label');
        }
    });
});
