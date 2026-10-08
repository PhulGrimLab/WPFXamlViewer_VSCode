import * as path from 'path';
import Mocha = require('mocha');

/** VS Code 안에서 호출되는 mocha 진입점(설치 스모크). */
export function run(): Promise<void> {
    const mocha = new Mocha({ ui: 'bdd', timeout: 120_000 });
    mocha.addFile(path.resolve(__dirname, 'smoke.test.js'));
    return new Promise((resolve, reject) => {
        mocha.run((failures) => (failures > 0 ? reject(new Error(`${failures}개 테스트 실패`)) : resolve()));
    });
}
