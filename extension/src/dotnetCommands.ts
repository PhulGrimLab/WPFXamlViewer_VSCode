import { t } from './i18n';
import * as path from 'path';

/**
 * `.csproj`/`.sln` 우클릭 빌드 명령의 순수 로직(vscode 모듈에 의존하지 않아 단위 테스트가 가능하다).
 */

/** 프로젝트에 적용할 수 있는 동작. */
export type DotnetAction = 'build' | 'rebuild' | 'clean' | 'restore';

/** 빌드 구성 설정에서 허용하는 값. */
export const BUILD_CONFIGURATIONS = ['Debug', 'Release'] as const;

/** 설정 값이 올바르지 않을 때 쓰는 구성. */
export const DEFAULT_BUILD_CONFIGURATION = 'Debug';

/** XAML에서 가장 가까운 `.csproj`를 찾으려고 위로 올라가는 최대 단계(호스트의 ProjectLocator와 같은 값). */
export const MAX_PROJECT_SEARCH_LEVELS = 12;

/** 빌드 대상으로 인정하는 파일 확장자(소문자). */
const BUILDABLE_EXTENSIONS = new Set(['.csproj', '.sln', '.slnx', '.slnf']);

/** 동작별 사용자 표시 이름(Task 이름/알림에 쓴다). */
export function actionLabel(action: DotnetAction): string {
    return t(`action.${action}`);
}

/** 구성(Debug/Release)을 직접 고르는 우클릭 명령. 설정 `buildConfiguration`을 무시하고 이 구성으로 실행한다(package.json의 명령 ID와 같아야 한다). */
export const CONFIGURED_COMMANDS: readonly { command: string; action: DotnetAction; configuration: (typeof BUILD_CONFIGURATIONS)[number] }[] = [
    { command: 'wpfXamlViewer.buildProjectDebug', action: 'build', configuration: 'Debug' },
    { command: 'wpfXamlViewer.buildProjectRelease', action: 'build', configuration: 'Release' },
    { command: 'wpfXamlViewer.rebuildProjectDebug', action: 'rebuild', configuration: 'Debug' },
    { command: 'wpfXamlViewer.rebuildProjectRelease', action: 'rebuild', configuration: 'Release' },
    { command: 'wpfXamlViewer.cleanProjectDebug', action: 'clean', configuration: 'Debug' },
    { command: 'wpfXamlViewer.cleanProjectRelease', action: 'clean', configuration: 'Release' },
];

/** 설정 값을 허용된 구성으로 정규화한다(대소문자 무시). 모르는 값이면 기본값. */
export function normalizeConfiguration(value: unknown): string {
    const found = BUILD_CONFIGURATIONS.find((c) => typeof value === 'string' && c.toLowerCase() === value.trim().toLowerCase());
    return found ?? DEFAULT_BUILD_CONFIGURATION;
}

/** `dotnet`에 넘길 파일이 빌드 대상(.csproj/.sln/.slnx/.slnf)인가. */
export function isBuildableTarget(filePath: string): boolean {
    return BUILDABLE_EXTENSIONS.has(path.extname(filePath).toLowerCase());
}

/** 우클릭 빌드에 덧붙이는 선택 옵션. */
export interface BuildOptions {
    /** 다중 대상 프로젝트에서 이 TFM만 빌드한다(`dotnet -f`). 빈 값이면 모두. */
    targetFramework?: string;
    /** `dotnet` 인자 끝에 그대로 붙이는 추가 인자(예: `-p:RunAnalyzers=false`). */
    extraArgs?: readonly string[];
}

/** TFM 문자열 형식(예: net10.0-windows, net472, netstandard2.0). 옵션 주입(`-` 로 시작)이나 구분자를 막으려고 영문자로 시작하는 단어/점/하이픈만 허용한다. */
const TARGET_FRAMEWORK_PATTERN = /^[A-Za-z][A-Za-z0-9.-]*$/;

/** 추가 인자 개수/길이 상한(설정 값이 비정상적으로 큰 경우를 막는다). */
const MAX_EXTRA_ARGS = 20;
const MAX_EXTRA_ARG_LENGTH = 200;

/** 설정 값에서 TFM을 꺼낸다. 형식이 올바르지 않으면 빈 문자열(= 지정 안 함). */
export function normalizeTargetFramework(value: unknown): string {
    const text = typeof value === 'string' ? value.trim() : '';
    return TARGET_FRAMEWORK_PATTERN.test(text) ? text : '';
}

/** 설정 값에서 추가 인자 배열을 꺼낸다(문자열만, 빈 문자열/비정상 길이 제외, 최대 개수 제한). */
export function normalizeExtraArgs(value: unknown): string[] {
    if (!Array.isArray(value)) {
        return [];
    } else {
        return value
            .filter((v): v is string => typeof v === 'string')
            .map((v) => v.trim())
            .filter((v) => v.length > 0 && v.length <= MAX_EXTRA_ARG_LENGTH)
            .slice(0, MAX_EXTRA_ARGS);
    }
}

/**
 * `.csproj` 텍스트가 `<TargetFrameworks>`(복수)에 해당 TFM을 선언하는가.
 * `-f`는 프로젝트가 그 TFM을 가질 때만 쓴다(없으면 "지원하지 않는 TFM" 오류). Directory.Build.props에서 오는 선언은 보지 않는다(그땐 -f를 붙이지 않는다).
 */
export function declaresTargetFramework(projectText: string, targetFramework: string): boolean {
    const match = /<TargetFrameworks[^>]*>([^<]*)<\/TargetFrameworks>/i.exec(projectText);
    if (!match) {
        return false;
    } else {
        return match[1].split(';').some((t) => t.trim().toLowerCase() === targetFramework.toLowerCase());
    }
}

/**
 * `dotnet` 인자 배열을 만든다. 경로는 배열 요소 하나로 넘기므로 공백/한글이 있어도 따로 따옴표를 붙이지 않는다.
 * restore는 `-c`/`-f`를 받지 않아 뺀다. `-f`는 호출자가 "이 프로젝트가 그 TFM을 가진다"고 확인한 경우(`options.targetFramework`)에만 붙는다.
 * 오류만 Problems 패널로 가져가려고 요약은 끈다. 추가 인자는 맨 뒤에 붙는다.
 */
export function buildDotnetArgs(action: DotnetAction, targetPath: string, configuration: string, options: BuildOptions = {}): string[] {
    const quiet = ['--nologo', '-consoleloggerparameters:NoSummary'];
    const framework = options.targetFramework ? ['-f', options.targetFramework] : [];
    const extra = [...(options.extraArgs ?? [])];
    switch (action) {
        case 'build':
            return ['build', targetPath, '-c', configuration, ...framework, ...quiet, ...extra];
        case 'rebuild':
            return ['build', targetPath, '-c', configuration, ...framework, '--no-incremental', ...quiet, ...extra];
        case 'clean':
            return ['clean', targetPath, '-c', configuration, ...framework, ...quiet, ...extra];
        case 'restore':
            return ['restore', targetPath, '--nologo', ...extra];
    }
}

/**
 * XAML 파일에서 위로 올라가며 가장 가까운 `.csproj`를 찾는다(같은 폴더에 여러 개면 이름순 첫 번째).
 * 못 찾으면 undefined. 파일 시스템 접근은 인자로 받아 테스트에서 바꿀 수 있다.
 */
export function findNearestProject(
    xamlPath: string,
    listDirectory: (directory: string) => string[],
): string | undefined {
    let directory = path.dirname(path.resolve(xamlPath));
    for (let level = 0; level <= MAX_PROJECT_SEARCH_LEVELS; level++) {
        let entries: string[] = [];
        try {
            entries = listDirectory(directory);
        } catch {
            // 읽을 수 없는 폴더: 위로 계속.
        }
        const projects = entries.filter((e) => e.toLowerCase().endsWith('.csproj')).sort((a, b) => a.toLowerCase().localeCompare(b.toLowerCase()));
        if (projects.length > 0) {
            return path.join(directory, projects[0]);
        } else {
            // 이 폴더에는 없음.
        }
        const parent = path.dirname(directory);
        if (parent === directory) {
            return undefined;
        } else {
            directory = parent;
        }
    }
    return undefined;
}
