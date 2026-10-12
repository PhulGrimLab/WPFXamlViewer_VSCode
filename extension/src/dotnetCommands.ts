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
const BUILDABLE_EXTENSIONS = new Set(['.csproj', '.sln', '.slnx']);

/** 동작별 사용자 표시 이름(Task 이름/알림에 쓴다). */
export const ACTION_LABELS: Record<DotnetAction, string> = {
    build: '빌드',
    rebuild: '다시 빌드',
    clean: '정리',
    restore: '복원',
};

/** 설정 값을 허용된 구성으로 정규화한다(대소문자 무시). 모르는 값이면 기본값. */
export function normalizeConfiguration(value: unknown): string {
    const found = BUILD_CONFIGURATIONS.find((c) => typeof value === 'string' && c.toLowerCase() === value.trim().toLowerCase());
    return found ?? DEFAULT_BUILD_CONFIGURATION;
}

/** `dotnet`에 넘길 파일이 빌드 대상(.csproj/.sln/.slnx)인가. */
export function isBuildableTarget(filePath: string): boolean {
    return BUILDABLE_EXTENSIONS.has(path.extname(filePath).toLowerCase());
}

/**
 * `dotnet` 인자 배열을 만든다. 경로는 배열 요소 하나로 넘기므로 공백/한글이 있어도 따로 따옴표를 붙이지 않는다.
 * restore는 `-c`를 받지 않아 뺀다. 오류만 Problems 패널로 가져가려고 요약은 끈다.
 */
export function buildDotnetArgs(action: DotnetAction, targetPath: string, configuration: string): string[] {
    const quiet = ['--nologo', '-consoleloggerparameters:NoSummary'];
    switch (action) {
        case 'build':
            return ['build', targetPath, '-c', configuration, ...quiet];
        case 'rebuild':
            return ['build', targetPath, '-c', configuration, '--no-incremental', ...quiet];
        case 'clean':
            return ['clean', targetPath, '-c', configuration, ...quiet];
        case 'restore':
            return ['restore', targetPath, '--nologo'];
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
