/**
 * 미리보기 클릭/커서 위치 → 요소 선택(M5, doc/01 3.1의 elements). VS Code 없이 단위 테스트할 수 있는 순수 함수만 둔다.
 */

/** 호스트가 보낸 요소 하나: 원본 줄/열(1-base, `<` 위치)과 끝 위치, 결과 PNG 픽셀 경계. */
export interface HitElement {
    id: string;
    line: number;
    col: number;
    endLine: number;
    endCol: number;
    x: number;
    y: number;
    w: number;
    h: number;
}

/**
 * 이미지 픽셀 좌표 (x, y)에 있는 요소를 고른다. 호스트는 선위 순회(부모 → 자식, 나중 형제가 위) 순서로 보내므로
 * 점을 포함하는 **마지막** 요소가 가장 안쪽이고 가장 위에 그려진 요소다(템플릿 내부 요소는 호스트가 제외하므로 가까운 태그된 조상이 선택된다).
 * 경계는 왼쪽/위 포함, 오른쪽/아래 제외다. 없으면 undefined.
 */
export function pickElementAt(elements: readonly HitElement[], x: number, y: number): HitElement | undefined {
    for (let i = elements.length - 1; i >= 0; i--) {
        const e = elements[i];
        if (x >= e.x && x < e.x + e.w && y >= e.y && y < e.y + e.h) {
            return e;
        } else {
            // 이 요소는 점을 포함하지 않는다: 앞쪽(아래) 요소를 계속 본다.
        }
    }
    return undefined;
}

/** (줄, 열) 두 값을 비교한다: 음수면 a가 앞. */
function comparePosition(lineA: number, colA: number, lineB: number, colB: number): number {
    return lineA !== lineB ? lineA - lineB : colA - colB;
}

/**
 * 에디터 커서(1-base 줄/열)를 포함하는 가장 안쪽 요소를 고른다: 시작 ≤ 커서 < 끝 인 요소 중 시작이 가장 늦은 것.
 * 같은 요소가 템플릿/데이터 템플릿으로 여러 번 나타나면(같은 원본 위치) 첫 번째를 돌려준다. 없으면 undefined.
 */
export function pickElementAtCursor(elements: readonly HitElement[], line: number, col: number): HitElement | undefined {
    let best: HitElement | undefined;
    for (const e of elements) {
        const afterStart = comparePosition(line, col, e.line, e.col) >= 0;
        const beforeEnd = comparePosition(line, col, e.endLine, e.endCol) < 0;
        if (afterStart && beforeEnd && (!best || comparePosition(e.line, e.col, best.line, best.col) > 0)) {
            best = e;
        } else {
            // 커서를 포함하지 않거나 더 바깥 요소.
        }
    }
    return best;
}
