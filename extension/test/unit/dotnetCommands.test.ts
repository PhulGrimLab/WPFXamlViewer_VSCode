import * as assert from 'assert';
import * as path from 'path';
import {
    buildDotnetArgs,
    findNearestProject,
    isBuildableTarget,
    normalizeConfiguration,
} from '../../src/dotnetCommands';

/** X-B: `.csproj`/`.sln` 우클릭 빌드 명령의 순수 로직. */
describe('dotnetCommands (X-B)', () => {
    const project = path.join('C:\\', 'My Projects', '한글 앱', 'App.csproj');

    it('X-B01 build/rebuild/clean/restore 인자: 경로는 하나의 인자로, restore에는 -c가 없다', () => {
        assert.deepStrictEqual(buildDotnetArgs('build', project, 'Debug'),
            ['build', project, '-c', 'Debug', '--nologo', '-consoleloggerparameters:NoSummary']);
        assert.deepStrictEqual(buildDotnetArgs('rebuild', project, 'Release'),
            ['build', project, '-c', 'Release', '--no-incremental', '--nologo', '-consoleloggerparameters:NoSummary']);
        assert.deepStrictEqual(buildDotnetArgs('clean', project, 'Debug'),
            ['clean', project, '-c', 'Debug', '--nologo', '-consoleloggerparameters:NoSummary']);
        assert.deepStrictEqual(buildDotnetArgs('restore', project, 'Debug'), ['restore', project, '--nologo']);
    });

    it('X-B02 구성 설정 정규화: 대소문자 무시, 모르는 값/비문자열은 Debug', () => {
        assert.strictEqual(normalizeConfiguration('release'), 'Release');
        assert.strictEqual(normalizeConfiguration(' DEBUG '), 'Debug');
        assert.strictEqual(normalizeConfiguration('Prod; calc'), 'Debug', '인자 주입처럼 보이는 값은 허용 목록에 없어 Debug');
        assert.strictEqual(normalizeConfiguration(undefined), 'Debug');
        assert.strictEqual(normalizeConfiguration(7), 'Debug');
    });

    it('X-B03 빌드 대상은 .csproj/.sln/.slnx만(대소문자 무시)', () => {
        assert.ok(isBuildableTarget('a/B.CSPROJ'));
        assert.ok(isBuildableTarget('a/b.sln'));
        assert.ok(isBuildableTarget('a/b.slnx'));
        assert.ok(!isBuildableTarget('a/b.xaml'));
        assert.ok(!isBuildableTarget('a/b.csproj.user'));
        assert.ok(!isBuildableTarget('a/csproj'));
    });

    it('X-B04 XAML에서 가장 가까운 .csproj를 위로 올라가며 찾는다(여러 개면 이름순 첫 번째)', () => {
        const root = path.resolve('C:\\', 'Repo');
        const tree: Record<string, string[]> = {
            [root]: ['Other.csproj'],
            [path.join(root, 'App')]: ['b.csproj', 'A.csproj', 'readme.md'],
            [path.join(root, 'App', 'Views')]: ['Main.xaml'],
        };
        const list = (d: string): string[] => tree[d] ?? [];
        assert.strictEqual(findNearestProject(path.join(root, 'App', 'Views', 'Main.xaml'), list), path.join(root, 'App', 'A.csproj'));
        assert.strictEqual(findNearestProject(path.join(root, 'Loose.xaml'), list), path.join(root, 'Other.csproj'));
    });

    it('X-B05 .csproj가 없으면 undefined, 읽기 오류는 건너뛰고 계속 올라간다', () => {
        const root = path.resolve('C:\\', 'Repo');
        assert.strictEqual(findNearestProject(path.join(root, 'a', 'x.xaml'), () => []), undefined);
        const list = (d: string): string[] => {
            if (d === path.join(root, 'a')) {
                throw new Error('EACCES');
            }
            return d === root ? ['P.csproj'] : [];
        };
        assert.strictEqual(findNearestProject(path.join(root, 'a', 'x.xaml'), list), path.join(root, 'P.csproj'));
    });
});
