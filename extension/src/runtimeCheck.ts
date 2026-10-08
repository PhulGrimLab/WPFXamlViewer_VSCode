import { HostCrashedError } from './hostClient';

/**
 * .NET Desktop Runtime이 없어 호스트가 시작하지 못한 경우를 알아본다(doc/00 5절, 테스트 I-12).
 * 실측(Windows, 프레임워크 종속 apphost): 런타임을 못 찾으면 종료 코드 0x80008083(= -2147450749, 부호 없는 값으로는 2147516547)과
 * stderr의 "You must install .NET to run this application." 문구가 나온다. 둘 중 하나라도 맞으면 런타임 없음으로 본다.
 * 순수 함수라 VS Code 없이 테스트한다.
 */

/** hostfxr의 "프레임워크/런타임을 찾을 수 없음" 계열 종료 코드(부호 있는 32비트 표현). */
const HOST_FXR_MISSING_RUNTIME_EXIT_CODES: readonly number[] = [
    -2147450749, // 0x80008083 FrameworkMissingFailure(apphost가 .NET 위치를 못 찾음)
    -2147450730, // 0x80008096 FrameworkMissing(요청한 프레임워크 버전이 없음)
];

/** stderr에 나오는 안내 문구(대소문자 무시). */
const MISSING_RUNTIME_TEXT = 'you must install .net';

/** 설치 안내 페이지. */
export const DOTNET_DESKTOP_RUNTIME_URL = 'https://dotnet.microsoft.com/download/dotnet/10.0';

/** 종료 코드를 부호 있는 32비트 정수로 맞춘다(Node가 부호 없는 값으로 줄 수도 있다). */
function toSigned32(code: number): number {
    return code | 0;
}

/** 호스트가 .NET Desktop Runtime 부재로 죽었는가. */
export function isDotNetRuntimeMissing(error: Error): boolean {
    if (!(error instanceof HostCrashedError)) {
        return false;
    } else {
        const byCode = error.exitCode !== null && HOST_FXR_MISSING_RUNTIME_EXIT_CODES.includes(toSigned32(error.exitCode));
        const byText = error.stderrTail.toLowerCase().includes(MISSING_RUNTIME_TEXT);
        return byCode || byText;
    }
}

/** 사용자에게 보여 줄 안내 문장. */
export const DOTNET_RUNTIME_MISSING_MESSAGE =
    'WPF XAML Viewer: .NET 10 Desktop Runtime이 설치되어 있지 않아 렌더 호스트를 시작할 수 없습니다. 설치한 뒤 다시 시도하세요.';
