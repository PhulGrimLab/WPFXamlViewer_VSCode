/**
 * 확장의 표시 문구(한국어/영어). vscode 모듈에 의존하지 않아 단위 테스트에서 그대로 쓴다.
 * 현재 언어는 확장이 설정(`wpfXamlViewer.language`)과 VS Code 표시 언어로 정해 `setLanguage`로 알린다(기본 한국어).
 * 로그(출력 채널/host.log)는 진단용이라 번역하지 않는다. 메뉴 제목 같은 매니페스트 문구는 package.nls*.json이 VS Code 표시 언어를 따른다.
 */
export type Language = 'ko' | 'en';

/** 설정 값: auto는 VS Code 표시 언어를 따른다. */
export type LanguageSetting = 'auto' | Language;

export const LANGUAGE_SETTING_VALUES: readonly LanguageSetting[] = ['auto', 'ko', 'en'];

/** 설정 값과 VS Code 표시 언어(예: `ko`, `en`, `en-us`)로 실제 언어를 정한다. 한국어가 아니면 영어. */
export function resolveLanguage(setting: unknown, displayLanguage: string | undefined): Language {
    if (setting === 'ko' || setting === 'en') {
        return setting;
    } else {
        return (displayLanguage ?? '').toLowerCase().startsWith('ko') ? 'ko' : 'en';
    }
}

/** 문구 표. `{0}`, `{1}`은 인자로 바뀐다. 두 언어 키가 같아야 한다(단위 테스트가 확인). */
export const STRINGS = {
    ko: {
        // 빌드 Task 이름/동작
        'action.build': '빌드',
        'action.rebuild': '다시 빌드',
        'action.clean': '정리',
        'action.restore': '복원',
        // 프로젝트 빌드 명령
        'build.noXaml': 'WPF XAML: 빌드할 XAML 파일이 없습니다.',
        'build.noProject': 'WPF XAML: {0}이(가) 속한 .csproj를 찾지 못했습니다.',
        'build.badTarget': 'WPF XAML: .csproj/.sln/.slnx/.slnf 파일에서 실행해 주세요.',
        'build.untrusted': 'WPF XAML: 신뢰하지 않는 폴더에서는 프로젝트를 빌드하지 않습니다(빌드는 프로젝트 코드를 실행합니다). 폴더를 신뢰한 뒤 다시 시도하세요.',
        // 자동 빌드 안내
        'auto.unknownProject': '(알 수 없음)',
        'auto.building': '{0}을(를) 미리 보려면 프로젝트 {1}가 먼저 빌드되어야 합니다. 빌드하는 중입니다...',
        'auto.failedBefore': '{0}을(를) 미리 보려면 프로젝트 {1}가 먼저 빌드되어야 하는데, 빌드에 실패했습니다. 터미널의 빌드 출력과 Problems 패널의 오류를 고친 뒤 다시 시도하세요.',
        'auto.noOutput': '프로젝트 {1}를 빌드했지만 미리보기가 찾을 빌드 산출물(bin)이 생기지 않았습니다. 출력 경로 설정을 확인하세요.',
        'auto.disabled': '{0}을(를) 미리 보려면 프로젝트 {1}가 먼저 빌드되어야 합니다. 빌드한 뒤 다시 시도하세요(설정 wpfXamlViewer.autoBuildOnPreview를 켜면 자동으로 빌드합니다).',
        'auto.untrusted': '{0}을(를) 미리 보려면 프로젝트 {1}가 먼저 빌드되어야 합니다. 신뢰하지 않는 폴더에서는 자동으로 빌드하지 않습니다.',
        'auto.noProject': '{0}이(가) 속한 .csproj를 찾지 못했습니다. 프로젝트를 먼저 빌드해야 미리 볼 수 있는 XAML일 수 있습니다.',
        'auto.retry': '다시 빌드',
        // 미리보기 상태/알림
        'preview.openXaml': 'WPF XAML: .xaml 파일을 연 상태에서 실행해 주세요.',
        'preview.rendering': '$(sync~spin) XAML 렌더 중',
        'preview.ok': '$(check) XAML OK',
        'preview.okWarnings': '$(check) XAML OK (경고 {0})',
        'preview.error': '$(error) XAML 오류',
        'preview.buildRunning': '$(sync~spin) 프로젝트 빌드 중',
        'preview.buildNeeded': '$(error) 프로젝트 빌드 필요',
        'preview.hostFailure': 'WPF XAML Live Preview: {0} (자세한 내용은 출력 채널 확인)',
        'preview.noHost': 'WPF XAML Live Preview: 렌더 호스트(XamlRenderHost.exe)를 찾을 수 없습니다. `dotnet build host/XamlRenderHost.slnx`로 빌드하거나 XAMLVIEWER_HOST_EXE를 설정하세요.',
        'runtime.missing': 'WPF XAML Live Preview: .NET 10 Desktop Runtime이 설치되어 있지 않아 렌더 호스트를 시작할 수 없습니다. 설치한 뒤 다시 시도하세요.',
        'runtime.openPage': '.NET 설치 페이지 열기',
        // 호스트 통신 오류
        'host.timeout': '호스트가 {0}ms 안에 응답하지 않았습니다 (request {1}).',
        'host.crashed': '호스트가 비정상 종료했습니다 (exit={0}, signal={1}).',
        'host.spawn': '호스트를 시작할 수 없습니다: {0}',
        'host.disposed': 'HostClient가 종료되었습니다.',
        'host.noMessage': '(메시지 없음)',
        // 웹뷰(도구 모음)
        'web.zoomOut': '축소',
        'web.zoomIn': '확대',
        'web.fit': '창에 맞춤',
        'web.fitLabel': '맞춤',
        'web.actual': '실제 크기',
        'web.background': '배경',
        'web.checker': '체크무늬',
        'web.white': '흰색',
        'web.dark': '어둡게',
        'web.auto': '자동',
        'web.applyTitle': '지정한 크기로 다시 렌더',
        'web.apply': '적용',
        'web.autoTitle': '크기 지정 해제',
        'web.openDoc': 'XAML 문서를 열어 주세요.',
        'web.busy': '렌더 중...',
        // 언어 전환
        'lang.pick': 'WPF XAML 표시 언어를 선택하세요',
        'lang.auto': '자동 (VS Code 표시 언어를 따름)',
        'lang.ko': '한국어',
        'lang.en': 'English',
        'lang.changed': 'WPF XAML: 표시 언어를 {0}(으)로 바꿨습니다. 메뉴 제목은 VS Code 표시 언어를 따릅니다.',
    },
    en: {
        'action.build': 'Build',
        'action.rebuild': 'Rebuild',
        'action.clean': 'Clean',
        'action.restore': 'Restore',
        'build.noXaml': 'WPF XAML: There is no XAML file to build.',
        'build.noProject': 'WPF XAML: Could not find the .csproj that contains {0}.',
        'build.badTarget': 'WPF XAML: Run this on a .csproj/.sln/.slnx/.slnf file.',
        'build.untrusted': 'WPF XAML: Projects are not built in untrusted folders (a build runs project code). Trust the folder and try again.',
        'auto.unknownProject': '(unknown)',
        'auto.building': 'To preview {0}, project {1} must be built first. Building now...',
        'auto.failedBefore': 'To preview {0}, project {1} must be built first, but the build failed. Fix the errors in the terminal build output and the Problems panel, then try again.',
        'auto.noOutput': 'Project {1} was built, but no build output (bin) that the preview can use was produced. Check the output path settings.',
        'auto.disabled': 'To preview {0}, project {1} must be built first. Build it and try again (turn on the setting wpfXamlViewer.autoBuildOnPreview to build automatically).',
        'auto.untrusted': 'To preview {0}, project {1} must be built first. Projects are not built automatically in untrusted folders.',
        'auto.noProject': 'Could not find the .csproj that contains {0}. This XAML may need its project to be built before it can be previewed.',
        'auto.retry': 'Build again',
        'preview.openXaml': 'WPF XAML: Run this with a .xaml file open.',
        'preview.rendering': '$(sync~spin) Rendering XAML',
        'preview.ok': '$(check) XAML OK',
        'preview.okWarnings': '$(check) XAML OK ({0} warnings)',
        'preview.error': '$(error) XAML error',
        'preview.buildRunning': '$(sync~spin) Building project',
        'preview.buildNeeded': '$(error) Project build required',
        'preview.hostFailure': 'WPF XAML Live Preview: {0} (see the output channel for details)',
        'preview.noHost': 'WPF XAML Live Preview: Could not find the render host (XamlRenderHost.exe). Build it with `dotnet build host/XamlRenderHost.slnx` or set XAMLVIEWER_HOST_EXE.',
        'runtime.missing': 'WPF XAML Live Preview: The .NET 10 Desktop Runtime is not installed, so the render host cannot start. Install it and try again.',
        'runtime.openPage': 'Open .NET download page',
        'host.timeout': 'The host did not respond within {0}ms (request {1}).',
        'host.crashed': 'The host exited abnormally (exit={0}, signal={1}).',
        'host.spawn': 'Could not start the host: {0}',
        'host.disposed': 'HostClient has been disposed.',
        'host.noMessage': '(no message)',
        'web.zoomOut': 'Zoom out',
        'web.zoomIn': 'Zoom in',
        'web.fit': 'Fit to window',
        'web.fitLabel': 'Fit',
        'web.actual': 'Actual size',
        'web.background': 'Background',
        'web.checker': 'Checkerboard',
        'web.white': 'White',
        'web.dark': 'Dark',
        'web.auto': 'Auto',
        'web.applyTitle': 'Render again at the given size',
        'web.apply': 'Apply',
        'web.autoTitle': 'Clear the given size',
        'web.openDoc': 'Open a XAML document.',
        'web.busy': 'Rendering...',
        'lang.pick': 'Choose the WPF XAML display language',
        'lang.auto': 'Automatic (follow the VS Code display language)',
        'lang.ko': '한국어 (Korean)',
        'lang.en': 'English',
        'lang.changed': 'WPF XAML: Display language changed to {0}. Menu titles follow the VS Code display language.',
    },
} as const;

export type StringKey = keyof (typeof STRINGS)['ko'];

let currentLanguage: Language = 'ko';

/** 현재 표시 언어를 바꾼다. */
export function setLanguage(language: Language): void {
    currentLanguage = language;
}

export function getLanguage(): Language {
    return currentLanguage;
}

/** 현재 언어의 문구를 돌려준다. `{0}`, `{1}`을 인자로 바꾼다. */
export function t(key: StringKey, ...args: (string | number)[]): string {
    return format(STRINGS[currentLanguage][key], args);
}

/** 지정한 언어의 문구(웹뷰 HTML처럼 현재 언어와 따로 만들 때). */
export function tIn(language: Language, key: StringKey, ...args: (string | number)[]): string {
    return format(STRINGS[language][key], args);
}

function format(template: string, args: (string | number)[]): string {
    return template.replace(/\{(\d+)\}/g, (_, index: string) => String(args[Number(index)] ?? ''));
}
