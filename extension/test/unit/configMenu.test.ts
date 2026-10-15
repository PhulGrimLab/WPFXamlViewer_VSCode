import * as assert from 'assert';
import * as fs from 'fs';
import * as path from 'path';
import { CONFIGURED_COMMANDS, buildDotnetArgs } from '../../src/dotnetCommands';

/**
 * X-C: 우클릭 "구성 선택 빌드" 서브메뉴(Debug/Release)의 명령 표와 package.json이 어긋나지 않는지 검사한다.
 */
interface MenuItem { command?: string; submenu?: string; when?: string; group?: string }
const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '../../../package.json'), 'utf8')) as {
    contributes: {
        commands: { command: string; title: string }[];
        submenus: { id: string; label: string }[];
        menus: Record<string, MenuItem[]>;
    };
};
const SUBMENU_ID = 'wpfXamlViewer.configMenu';

describe('구성 선택 빌드 메뉴 (X-M)', () => {
    it('X-M01 코드의 명령 표가 package.json 명령과 정확히 일치한다(제목에 구성이 들어간다)', () => {
        const declared = manifest.contributes.commands.filter((c) => /(Debug|Release)$/.test(c.command));
        assert.deepStrictEqual(declared.map((c) => c.command).sort(), CONFIGURED_COMMANDS.map((c) => c.command).sort());
        for (const entry of CONFIGURED_COMMANDS) {
            const title = manifest.contributes.commands.find((c) => c.command === entry.command)?.title ?? '';
            assert.ok(title.endsWith(entry.configuration), `${entry.command} 제목에 ${entry.configuration}이 없다: ${title}`);
        }
    });

    it('X-M02 탐색기 우클릭에 서브메뉴가 있고, 서브메뉴가 모든 구성 명령을 담는다', () => {
        assert.ok(manifest.contributes.submenus.some((s) => s.id === SUBMENU_ID));
        const entry = manifest.contributes.menus['explorer/context'].find((m) => m.submenu === SUBMENU_ID);
        assert.ok(entry, '탐색기 우클릭에 서브메뉴 항목이 없다');
        for (const ext of ['.csproj', '.sln', '.slnx', '.slnf']) {
            assert.ok(entry.when?.includes(ext), `서브메뉴 조건에 ${ext}가 없다`);
        }
        assert.ok(entry.when?.includes('isWorkspaceTrusted'), '신뢰된 폴더에서만 보여야 한다');
        const inSubmenu = manifest.contributes.menus[SUBMENU_ID].map((m) => m.command).sort();
        assert.deepStrictEqual(inSubmenu, CONFIGURED_COMMANDS.map((c) => c.command).sort());
    });

    it('X-M03 다시 빌드는 .csproj에서만, 나머지는 솔루션 파일에서도 보인다(기존 우클릭 규칙과 같다)', () => {
        for (const item of manifest.contributes.menus[SUBMENU_ID]) {
            const entry = CONFIGURED_COMMANDS.find((c) => c.command === item.command)!;
            const solutionOk = /\.sln/.test(item.when ?? '');
            assert.strictEqual(solutionOk, entry.action !== 'rebuild', `${item.command}의 대상 조건이 규칙과 다르다`);
            assert.ok(item.when?.includes('isWorkspaceTrusted'));
        }
    });

    it('X-M04 명령 팔레트에는 나오지 않는다(대상 파일이 필요한 명령)', () => {
        for (const entry of CONFIGURED_COMMANDS) {
            const hidden = manifest.contributes.menus.commandPalette.find((m) => m.command === entry.command);
            assert.strictEqual(hidden?.when, 'false', `${entry.command}이(가) 명령 팔레트에 노출된다`);
        }
    });

    it('X-M05 각 명령이 dotnet -c 에 자기 구성을 넘긴다', () => {
        for (const entry of CONFIGURED_COMMANDS) {
            const args = buildDotnetArgs(entry.action, 'X.csproj', entry.configuration);
            assert.strictEqual(args[args.indexOf('-c') + 1], entry.configuration);
        }
    });
});
