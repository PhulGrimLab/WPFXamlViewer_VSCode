import * as assert from 'assert';
import * as fs from 'fs';
import * as path from 'path';
import { loadManifest } from '../support/nls';

/**
 * X-P01: 사용자 가이드(doc/User_Guide.md)의 명령/설정 이름이 package.json과 일치하는지 검사한다.
 * 문서가 없는 명령을 안내하거나, 있는 명령을 빠뜨리는 것을 막는다.
 */
const REPO_ROOT = path.resolve(__dirname, '../../../..');
const guide = fs.readFileSync(path.join(REPO_ROOT, 'doc', 'User_Guide.md'), 'utf8');
// 가이드는 한국어라 한국어 문구로 푼 매니페스트와 비교한다.
const manifest = loadManifest<{
    contributes: { commands: { command: string; title: string }[]; configuration?: { properties?: Record<string, unknown> } };
}>('ko');

describe('User_Guide ↔ package.json (X-P01)', () => {
    it('package.json의 모든 명령 ID와 제목이 가이드에 있다', () => {
        for (const c of manifest.contributes.commands) {
            assert.ok(guide.includes(`\`${c.command}\``), `가이드에 명령 ID가 없음: ${c.command}`);
            assert.ok(guide.includes(c.title), `가이드에 명령 제목이 없음: ${c.title}`);
        }
    });

    it('가이드가 언급하는 wpfXamlViewer.* 이름은 모두 package.json에 있다(명령 또는 설정)', () => {
        const known = new Set<string>([
            ...manifest.contributes.commands.map((c) => c.command),
            ...Object.keys(manifest.contributes.configuration?.properties ?? {}),
        ]);
        const mentioned = new Set(guide.match(/wpfXamlViewer\.[A-Za-z0-9_.]+/g) ?? []);
        assert.ok(mentioned.size > 0, '가이드에 명령 ID가 하나도 없다');
        for (const name of mentioned) {
            assert.ok(known.has(name), `가이드의 ${name} 은(는) package.json에 없다`);
        }
    });

    it('설정 항목이 없으면 가이드가 "설정 항목은 없다"고 말한다(설정이 생기면 문서를 고치게 한다)', () => {
        const settingCount = Object.keys(manifest.contributes.configuration?.properties ?? {}).length;
        if (settingCount === 0) {
            assert.ok(guide.includes('별도의 설정 항목은 없다'), '설정이 없는데 가이드에 그 사실이 적혀 있지 않다');
        } else {
            for (const key of Object.keys(manifest.contributes.configuration!.properties!)) {
                assert.ok(guide.includes(key), `가이드에 설정이 없음: ${key}`);
            }
        }
    });
});

/** 영문 사용자 가이드(doc/User_Guide.en.md)도 같은 기준으로 매니페스트와 어긋나지 않는지 검사한다(영어 문구로 푼 매니페스트와 비교). */
describe('User_Guide.en ↔ package.json (X-P02)', () => {
    const guideEn = fs.readFileSync(path.join(REPO_ROOT, 'doc', 'User_Guide.en.md'), 'utf8');
    const manifestEn = loadManifest<{
        contributes: { commands: { command: string; title: string }[]; configuration?: { properties?: Record<string, unknown> } };
    }>('en');

    it('X-P02a 모든 명령 ID와 영어 제목이 영문 가이드에 있다', () => {
        for (const c of manifestEn.contributes.commands) {
            assert.ok(guideEn.includes(`\`${c.command}\``), `영문 가이드에 명령 ID가 없음: ${c.command}`);
            assert.ok(guideEn.includes(c.title), `영문 가이드에 명령 제목이 없음: ${c.title}`);
        }
    });

    it('X-P02b 모든 설정 키가 영문 가이드에 있고, 가이드가 언급하는 wpfXamlViewer.* 이름은 모두 package.json에 있다', () => {
        const settings = Object.keys(manifestEn.contributes.configuration?.properties ?? {});
        for (const key of settings) {
            assert.ok(guideEn.includes(key), `영문 가이드에 설정이 없음: ${key}`);
        }
        const known = new Set<string>([...manifestEn.contributes.commands.map((c) => c.command), ...settings]);
        for (const name of new Set(guideEn.match(/wpfXamlViewer\.[A-Za-z0-9_.]+/g) ?? [])) {
            assert.ok(known.has(name), `영문 가이드의 ${name} 은(는) package.json에 없다`);
        }
    });

    it('X-P02c 한국어/영어 가이드와 두 README가 서로를 가리킨다', () => {
        const guideKo = fs.readFileSync(path.join(REPO_ROOT, 'doc', 'User_Guide.md'), 'utf8');
        assert.ok(guideKo.includes('User_Guide.en.md') && guideEn.includes('User_Guide.md'));
        const readmeKo = fs.readFileSync(path.join(REPO_ROOT, 'README.md'), 'utf8');
        const readmeEn = fs.readFileSync(path.join(REPO_ROOT, 'README.en.md'), 'utf8');
        assert.ok(readmeKo.includes('README.en.md') && readmeEn.includes('README.md'));
        const extensionReadme = fs.readFileSync(path.join(REPO_ROOT, 'extension', 'README.md'), 'utf8');
        assert.ok(extensionReadme.includes('User_Guide.en.md') && extensionReadme.includes('User_Guide.md') && extensionReadme.includes('# 한국어'));
    });
});
