import * as path from 'path';
import Mocha = require('mocha');

/** VS Code 안에서 호출되는 mocha 진입점. XAMLVIEWER_TEST_SUITE가 가리키는 <suite>.test.js 를 실행한다(스위트별로 VS Code를 따로 띄운다). */
export function run(): Promise<void> {
    const mocha = new Mocha({ ui: 'bdd', timeout: 30000 });
    const suite = process.env.XAMLVIEWER_TEST_SUITE ?? 'preview';
    mocha.addFile(path.resolve(__dirname, `${suite}.test.js`));
    return new Promise((resolve, reject) => {
        mocha.run((failures) => (failures > 0 ? reject(new Error(`${failures}개 테스트 실패`)) : resolve()));
    });
}
