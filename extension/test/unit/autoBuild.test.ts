import * as assert from 'assert';
import { decideMissingBuild, describeMissingBuild } from '../../src/autoBuild';

const base = { reason: 'NoArtifact', needed: true, autoBuildEnabled: true, trusted: true, project: 'C:/w/App/App.csproj', state: undefined } as const;

describe('미리보기 자동 빌드 판단 (X-A)', () => {
    it('X-A01 산출물이 없고 조건이 맞으면 빌드한다', () => {
        assert.strictEqual(decideMissingBuild(base), 'build');
    });

    it('X-A02 산출물 문제가 아니거나(다른 이유) 자리표시자가 필요 없으면 아무것도 하지 않는다', () => {
        assert.strictEqual(decideMissingBuild({ ...base, reason: 'Loaded' }), 'none');
        assert.strictEqual(decideMissingBuild({ ...base, reason: undefined }), 'none');
        assert.strictEqual(decideMissingBuild({ ...base, needed: false }), 'none');
    });

    it('X-A03 이미 빌드 중이면 기다리고, 앞서 실패했으면 다시 빌드하지 않고 실패를 알린다', () => {
        assert.strictEqual(decideMissingBuild({ ...base, state: 'building' }), 'wait');
        assert.strictEqual(decideMissingBuild({ ...base, state: 'failed' }), 'failedBefore');
        assert.strictEqual(decideMissingBuild({ ...base, state: 'noOutput' }), 'noOutput');
    });

    it('X-A04 설정이 꺼졌거나 신뢰되지 않았거나 프로젝트가 없으면 빌드하지 않는다', () => {
        assert.strictEqual(decideMissingBuild({ ...base, autoBuildEnabled: false }), 'disabled');
        assert.strictEqual(decideMissingBuild({ ...base, trusted: false }), 'untrusted');
        assert.strictEqual(decideMissingBuild({ ...base, project: undefined }), 'noProject');
    });

    it('X-A05 안내 문구는 XAML/프로젝트 이름과 "빌드되어야 한다/실패했다"를 담는다', () => {
        const failed = describeMissingBuild('failedBefore', 'C:/w/App/Views/MainWindow.xaml', base.project);
        assert.ok(failed.includes('MainWindow.xaml') && failed.includes('App.csproj'));
        assert.ok(failed.includes('먼저 빌드되어야') && failed.includes('실패'));
        assert.ok(describeMissingBuild('build', 'a.xaml', base.project).includes('빌드하는 중'));
        assert.strictEqual(describeMissingBuild('none', 'a.xaml', undefined), '');
    });
});
