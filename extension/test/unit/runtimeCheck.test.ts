import * as assert from 'assert';
import { HostCrashedError, HostRequestError, HostTimeoutError } from '../../src/hostClient';
import { isDotNetRuntimeMissing } from '../../src/runtimeCheck';

describe('isDotNetRuntimeMissing (I-12)', () => {
    it('hostfxr 종료 코드 0x80008083(부호 있는/없는 표현 모두)이면 런타임 없음', () => {
        assert.ok(isDotNetRuntimeMissing(new HostCrashedError(-2147450749, null, '')));
        assert.ok(isDotNetRuntimeMissing(new HostCrashedError(2147516547, null, '')), '부호 없는 값으로 와도 같은 코드');
        assert.ok(isDotNetRuntimeMissing(new HostCrashedError(-2147450730, null, '')));
    });

    it('stderr 안내 문구만 있어도 런타임 없음(종료 코드가 다르게 와도)', () => {
        assert.ok(isDotNetRuntimeMissing(new HostCrashedError(1, null, 'You must install .NET to run this application.')));
    });

    it('일반 크래시/다른 오류는 런타임 없음이 아니다', () => {
        assert.ok(!isDotNetRuntimeMissing(new HostCrashedError(99, null, 'boom')));
        assert.ok(!isDotNetRuntimeMissing(new HostCrashedError(null, 'SIGKILL', '')));
        assert.ok(!isDotNetRuntimeMissing(new HostTimeoutError(1, 10)));
        assert.ok(!isDotNetRuntimeMissing(new HostRequestError('XamlParse', 'x')));
        assert.ok(!isDotNetRuntimeMissing(new Error('You must install .NET')), 'HostCrashedError가 아니면 무시');
    });
});
