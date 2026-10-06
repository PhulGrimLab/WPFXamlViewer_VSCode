import * as assert from 'assert';
import { LogId, formatLogLine } from '../../src/logFormat';

describe('formatLogLine', () => {
    it('수준은 대문자로, ID와 메시지를 한 줄로 만든다', () => {
        assert.strictEqual(
            formatLogLine('Info', LogId.ExtensionActivated, 'activated version=0.1.0'),
            '[INFO] E001 activated version=0.1.0');
    });

    it('메시지의 줄바꿈은 공백으로 치환되어 한 줄을 유지한다', () => {
        const line = formatLogLine('Error', LogId.HostCrashed, 'exit code 1\r\nlast request 7');
        assert.ok(!/[\r\n]/.test(line), '줄바꿈이 남아 있으면 안 된다');
        assert.strictEqual(line, '[ERROR] E012 exit code 1 last request 7');
    });

    it('로그 ID는 doc/01 5절의 값과 일치한다', () => {
        assert.deepStrictEqual({ ...LogId }, {
            ExtensionActivated: 'E001',
            HostStarted: 'E010',
            RequestTimeout: 'E011',
            HostCrashed: 'E012',
            RequestDiscarded: 'E013',
            HostOutputIgnored: 'E014',
        });
    });
});
