/**
 * 호스트 오류(줄/열 1-base, 없을 수 있음)를 VS Code 진단 위치(0-base)로 바꾸는 순수 함수(doc/03 X-G01~G02).
 */

/** 0-base 줄/열 위치. */
export interface ZeroBasedPosition {
    line: number;
    col: number;
}

/**
 * 입력: 호스트가 보고한 줄/열(1-base, 없을 수 있음), 문서의 줄 수. 출력: 문서 안으로 보정된 0-base 위치.
 * 줄 정보가 없으면 문서 첫 줄(0,0)에 표시한다. 문서보다 큰 줄 번호는 마지막 줄로 보정한다(편집 중 어긋남 방지).
 */
export function toZeroBasedPosition(line: number | undefined, col: number | undefined, lineCount: number): ZeroBasedPosition {
    if (line === undefined || line < 1) {
        return { line: 0, col: 0 };
    } else {
        // 줄 정보 있음: 아래에서 보정.
    }
    const lastLine = Math.max(lineCount - 1, 0);
    return {
        line: Math.min(line - 1, lastLine),
        col: col !== undefined && col >= 1 ? col - 1 : 0,
    };
}
