import * as assert from 'assert';
import * as fs from 'fs';
import * as path from 'path';

/**
 * X-P01: 사용자 가이드(doc/User_Guide.md)의 명령/설정 이름이 package.json과 일치하는지 검사한다.
 * 문서가 없는 명령을 안내하거나, 있는 명령을 빠뜨리는 것을 막는다.
 */
const REPO_ROOT = path.resolve(__dirname, '../../../..');
const guide = fs.readFileSync(path.join(REPO_ROOT, 'doc', 'User_Guide.md'), 'utf8');
const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '../../../package.json'), 'utf8')) as {
    contributes: { commands: { command: string; title: string }[]; configuration?: { properties?: Record<string, unknown> } };
};

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
