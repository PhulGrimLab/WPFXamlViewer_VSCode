import * as assert from 'assert';
import {
    buildDotnetArgs,
    declaresTargetFramework,
    isBuildableTarget,
    normalizeExtraArgs,
    normalizeTargetFramework,
} from '../../src/dotnetCommands';

/** X-B1x: TFM 지정, 추가 인자, .slnf 지원. */
describe('dotnetCommands 옵션 (X-B)', () => {
    const project = 'C:\\Repo\\Lib\\Lib.csproj';
    const quiet = ['--nologo', '-consoleloggerparameters:NoSummary'];

    it('X-B10 TFM과 추가 인자: -f는 구성 뒤, 추가 인자는 맨 뒤, restore에는 -f/-c가 없다', () => {
        const options = { targetFramework: 'net10.0-windows', extraArgs: ['-p:RunAnalyzers=false'] };
        assert.deepStrictEqual(buildDotnetArgs('build', project, 'Debug', options),
            ['build', project, '-c', 'Debug', '-f', 'net10.0-windows', ...quiet, '-p:RunAnalyzers=false']);
        assert.deepStrictEqual(buildDotnetArgs('rebuild', project, 'Release', options),
            ['build', project, '-c', 'Release', '-f', 'net10.0-windows', '--no-incremental', ...quiet, '-p:RunAnalyzers=false']);
        assert.deepStrictEqual(buildDotnetArgs('clean', project, 'Debug', options),
            ['clean', project, '-c', 'Debug', '-f', 'net10.0-windows', ...quiet, '-p:RunAnalyzers=false']);
        assert.deepStrictEqual(buildDotnetArgs('restore', project, 'Debug', options),
            ['restore', project, '--nologo', '-p:RunAnalyzers=false']);
    });

    it('X-B11 옵션이 없으면 이전과 같은 인자(회귀 방지)', () => {
        assert.deepStrictEqual(buildDotnetArgs('build', project, 'Debug'), ['build', project, '-c', 'Debug', ...quiet]);
        assert.deepStrictEqual(buildDotnetArgs('build', project, 'Debug', { targetFramework: '', extraArgs: [] }),
            ['build', project, '-c', 'Debug', ...quiet]);
    });

    it('X-B12 TFM 설정 정규화: 형식이 올바른 값만, 옵션 주입처럼 보이는 값은 비운다', () => {
        assert.strictEqual(normalizeTargetFramework(' net10.0-windows '), 'net10.0-windows');
        assert.strictEqual(normalizeTargetFramework('netstandard2.0'), 'netstandard2.0');
        assert.strictEqual(normalizeTargetFramework('net472'), 'net472');
        assert.strictEqual(normalizeTargetFramework('-p:Foo=1'), '');
        assert.strictEqual(normalizeTargetFramework('net8.0;net9.0'), '');
        assert.strictEqual(normalizeTargetFramework('net8.0 --no-restore'), '');
        assert.strictEqual(normalizeTargetFramework(undefined), '');
        assert.strictEqual(normalizeTargetFramework(10), '');
    });

    it('X-B13 추가 인자 정규화: 문자열만, 공백 제거, 빈 값/긴 값 제외, 개수 상한', () => {
        assert.deepStrictEqual(normalizeExtraArgs([' -p:RunAnalyzers=false ', '', 7, null, '--no-restore']), ['-p:RunAnalyzers=false', '--no-restore']);
        assert.deepStrictEqual(normalizeExtraArgs('-p:A=1'), []);
        assert.deepStrictEqual(normalizeExtraArgs(['x'.repeat(201)]), []);
        assert.strictEqual(normalizeExtraArgs(Array.from({ length: 50 }, (_, i) => `-p:A${i}=1`)).length, 20);
    });

    it('X-B14 .csproj가 <TargetFrameworks>에 선언한 TFM만 -f를 쓴다', () => {
        const multi = '<Project><PropertyGroup><TargetFrameworks>net10.0-windows;net9.0-windows;net472</TargetFrameworks></PropertyGroup></Project>';
        assert.ok(declaresTargetFramework(multi, 'net10.0-windows'));
        assert.ok(declaresTargetFramework(multi, 'NET472'), '대소문자 무시');
        assert.ok(!declaresTargetFramework(multi, 'net8.0-windows'), '선언하지 않은 TFM');
        const single = '<Project><PropertyGroup><TargetFramework>net10.0-windows</TargetFramework></PropertyGroup></Project>';
        assert.ok(!declaresTargetFramework(single, 'net10.0-windows'), '단일 대상은 -f가 필요 없다');
        assert.ok(!declaresTargetFramework('<Project/>', 'net10.0-windows'));
        const spaced = '<Project><PropertyGroup><TargetFrameworks Condition="true"> net10.0 ; net8.0 </TargetFrameworks></PropertyGroup></Project>';
        assert.ok(declaresTargetFramework(spaced, 'net8.0'), '조건 속성과 공백 허용');
    });

    it('X-B15 .slnf도 빌드 대상이다', () => {
        assert.ok(isBuildableTarget('C:\\Repo\\Wpf.Ui.Gallery.slnf'));
        assert.ok(!isBuildableTarget('C:\\Repo\\a.slnf.bak'));
    });
});
