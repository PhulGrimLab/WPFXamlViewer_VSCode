import * as path from 'path';
import Mocha = require('mocha');

/** VS Code 안에서 호출되는 mocha 진입점. 같은 폴더의 *.test.js 를 모두 실행한다. */
export function run(): Promise<void> {
    const mocha = new Mocha({ ui: 'bdd', timeout: 30000 });
    mocha.addFile(path.resolve(__dirname, 'preview.test.js'));
    return new Promise((resolve, reject) => {
        mocha.run((failures) => (failures > 0 ? reject(new Error(`${failures}개 테스트 실패`)) : resolve()));
    });
}
