/**
 * 확장 로그 한 줄의 형식과 로그 ID 정의 (doc/01 5절의 E001~E013).
 * 순수 함수/상수만 두어 VS Code 없이 단위 테스트할 수 있다.
 * XAML 본문은 로그에 남기지 않는다(사용자 소스 보호) — 호출자가 길이/해시만 넘긴다.
 */

/** 로그 수준. */
export type LogLevel = 'Debug' | 'Info' | 'Warn' | 'Error';

/** 확장 쪽 로그 ID (doc/01 5절). 구현이 진행되며 필요한 것부터 사용한다. */
export const LogId = {
    ExtensionActivated: 'E001',
    HostStarted: 'E010',
    RequestTimeout: 'E011',
    HostCrashed: 'E012',
    RequestDiscarded: 'E013',
    /** 호스트 출력 중 해석할 수 없거나 짝이 없는 줄을 무시했다. */
    HostOutputIgnored: 'E014',
} as const;

/**
 * 로그 한 줄을 `[LEVEL] ID message` 형태로 만든다.
 * 입력: 수준, 로그 ID, 메시지. 출력: 줄바꿈이 없는 한 줄 문자열.
 * 주의: 메시지에 줄바꿈이 있으면 로그 파서가 줄 단위로 읽는 전제가 깨지므로 공백으로 치환한다.
 */
export function formatLogLine(level: LogLevel, id: string, message: string): string {
    const singleLine = message.replace(/[\r\n]+/g, ' ');
    return `[${level.toUpperCase()}] ${id} ${singleLine}`;
}
