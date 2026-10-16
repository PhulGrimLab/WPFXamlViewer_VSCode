import * as fs from 'fs';
import * as path from 'path';

const EXTENSION_ROOT = path.resolve(__dirname, '../../..');

/** package.nls.json(영어 기본) 또는 package.nls.<언어>.json을 읽는다. */
export function loadNls(language: 'en' | 'ko'): Record<string, string> {
    const file = language === 'en' ? 'package.nls.json' : `package.nls.${language}.json`;
    return JSON.parse(fs.readFileSync(path.join(EXTENSION_ROOT, file), 'utf8')) as Record<string, string>;
}

/** package.json 원문. */
export function readManifestText(): string {
    return fs.readFileSync(path.join(EXTENSION_ROOT, 'package.json'), 'utf8');
}

/** package.json의 `%키%` 값을 해당 언어 문구로 바꾼 매니페스트를 돌려준다(VS Code가 하는 일과 같다). */
export function loadManifest<T>(language: 'en' | 'ko'): T {
    const nls = loadNls(language);
    const resolved = readManifestText().replace(/"%([^%"]+)%"/g, (_, key: string) => {
        if (!(key in nls)) {
            throw new Error(`package.nls에 키가 없다: ${key}`);
        }
        return JSON.stringify(nls[key]);
    });
    return JSON.parse(resolved) as T;
}
