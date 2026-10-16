import * as path from 'path';
import { t } from './i18n';

/** 호스트가 "프로젝트 빌드 산출물(bin)이 없다"고 알리는 이유 값(host ProjectLookupReasons.NoArtifact). */
export const REASON_NO_ARTIFACT = 'NoArtifact';

/** 호스트가 사용자 컨트롤을 자리표시자로 그렸다는 경고 코드(host WarningCodes.ProjectTier0). */
export const WARNING_PROJECT_TIER0 = 'ProjectTier0';

/** 프로젝트별 자동 빌드 진행 상태. 성공 후에도 산출물이 안 생기면 'noOutput'으로 두어 무한 반복을 막는다. */
export type AutoBuildState = 'building' | 'failed' | 'noOutput';

/** 산출물이 없을 때 확장이 할 일. */
export type MissingBuildAction =
    | 'none' // 이 문제가 아니다(산출물 있음/다른 이유).
    | 'build' // 자동 빌드한 뒤 다시 그린다.
    | 'wait' // 이미 빌드 중이다.
    | 'failedBefore' // 앞서 자동 빌드에 실패했다(다시 빌드하지 않고 알린다).
    | 'noOutput' // 앞서 빌드가 성공했는데도 산출물이 없다.
    | 'disabled' // 자동 빌드 설정이 꺼져 있다 — 먼저 빌드하라고 안내.
    | 'untrusted' // 신뢰되지 않은 폴더는 빌드하지 않는다.
    | 'noProject'; // 속한 .csproj를 못 찾았다.

export interface MissingBuildInput {
    /** 호스트가 알린 프로젝트 이유(오류 응답의 projectReason 또는 성공 응답의 project.reason). */
    reason: string | undefined;
    /** 성공 응답이면 자리표시자(ProjectTier0 경고)가 생겼는가. 오류 응답이면 true로 넘긴다(실패했으니 필요하다). */
    needed: boolean;
    autoBuildEnabled: boolean;
    trusted: boolean;
    project: string | undefined;
    state: AutoBuildState | undefined;
}

/** 산출물 부재 상황에서 할 일을 결정한다(순수 함수). */
export function decideMissingBuild(input: MissingBuildInput): MissingBuildAction {
    if (input.reason !== REASON_NO_ARTIFACT || !input.needed) {
        return 'none';
    } else if (!input.project) {
        return 'noProject';
    } else if (input.state === 'building') {
        return 'wait';
    } else if (input.state === 'failed') {
        return 'failedBefore';
    } else if (input.state === 'noOutput') {
        return 'noOutput';
    } else if (!input.autoBuildEnabled) {
        return 'disabled';
    } else if (!input.trusted) {
        return 'untrusted';
    } else {
        return 'build';
    }
}

/** 사용자에게 보일 안내 문구(미리보기/알림 공용). 빌드가 필요한 이유와 상태를 한 문장으로 말한다. */
export function describeMissingBuild(action: MissingBuildAction, xamlPath: string, project: string | undefined): string {
    const xaml = path.basename(xamlPath);
    const proj = project ? path.basename(project) : t('auto.unknownProject');
    switch (action) {
        case 'build':
        case 'wait':
            return t('auto.building', xaml, proj);
        case 'failedBefore':
            return t('auto.failedBefore', xaml, proj);
        case 'noOutput':
            return t('auto.noOutput', xaml, proj);
        case 'disabled':
            return t('auto.disabled', xaml, proj);
        case 'untrusted':
            return t('auto.untrusted', xaml, proj);
        case 'noProject':
            return t('auto.noProject', xaml, proj);
        default:
            return '';
    }
}
