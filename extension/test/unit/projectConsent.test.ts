import * as assert from 'assert';
import { ConsentStore, ProjectConsent } from '../../src/projectConsent';
import { compareOrdinalIgnoreCase } from '../../src/dotnetCommands';

/** X-S: 프로젝트 코드 실행 허용 기록과, 호스트와 같은 .csproj 정렬 규칙. */
class MemoryStore implements ConsentStore {
    private readonly _values = new Map<string, unknown>();
    get<T>(key: string, defaultValue: T): T {
        return (this._values.has(key) ? this._values.get(key) : defaultValue) as T;
    }
    update(key: string, value: unknown): PromiseLike<void> {
        this._values.set(key, value);
        return Promise.resolve();
    }
}

describe('프로젝트 코드 실행 허용 (X-S)', () => {
    it('X-S01 허용하기 전에는 거부, 허용 뒤에는 허용(경로 대소문자/구분자 무시), 초기화하면 다시 거부', async () => {
        const consent = new ProjectConsent(new MemoryStore());
        assert.strictEqual(consent.isGranted('C:/Repo/App/App.csproj'), false);
        await consent.grant('C:/Repo/App/App.csproj');
        assert.strictEqual(consent.isGranted('c:/repo/app/APP.csproj'), true);
        assert.strictEqual(consent.isGranted('C:/Repo/Other/Other.csproj'), false, '다른 프로젝트는 별도로 허용해야 한다');
        await consent.grant('C:/Repo/App/App.csproj'); // 중복 허용은 한 번만 기록한다
        await consent.clear();
        assert.strictEqual(consent.isGranted('C:/Repo/App/App.csproj'), false);
    });

    it('X-S02 .csproj 정렬은 호스트(OrdinalIgnoreCase)와 같다', () => {
        const names = ['b.csproj', 'A.csproj', 'a2.csproj', 'Ä.csproj', '_x.csproj'];
        const sorted = [...names].sort(compareOrdinalIgnoreCase);
        assert.deepStrictEqual(sorted, ['A.csproj', 'a2.csproj', 'b.csproj', '_x.csproj', 'Ä.csproj'].sort((a, b) => (a.toUpperCase() < b.toUpperCase() ? -1 : 1)));
    });
});
