/**
 * 확장 ↔ 웹뷰 메시지 스키마와 검증(doc/03 X-M01~M02, 1.1절).
 * 웹뷰가 보낸 메시지는 신뢰하지 않고 형태/범위를 검사한 뒤에만 사용한다.
 */

/** 배경 보기 모드. */
export const BACKGROUNDS = ['checker', 'white', 'dark'] as const;
export type Background = typeof BACKGROUNDS[number];

/** 줌 배율 허용 범위(웹뷰 UI와 같은 값). */
export const MIN_ZOOM = 0.1;
export const MAX_ZOOM = 8;

/** 사용자가 지정할 수 있는 렌더 크기의 최대 픽셀(호스트 한도와 같다). */
export const MAX_RENDER_SIZE = 8192;

/** 이미지 좌표계의 사각형(결과 PNG 픽셀). */
export interface Rect { x: number; y: number; w: number; h: number }

/** 확장 → 웹뷰: 이미지 표시. */
export interface ShowImageMessage {
    type: 'image';
    png: string;
    width: number;
    height: number;
}

/** 확장 → 웹뷰: 오류 표시(마지막 정상 이미지는 유지). */
export interface ShowErrorMessage {
    type: 'error';
    message: string;
}

/** 확장 → 웹뷰: 렌더 중 표시. */
export interface ShowBusyMessage {
    type: 'busy';
}

/** 확장 → 웹뷰: 선택 요소 강조(null이면 해제). */
export interface HighlightMessage {
    type: 'highlight';
    rect: Rect | null;
}

/** 확장 → 웹뷰: 보기 상태 지정(생략한 항목은 그대로). fit=true면 창에 맞춘다. */
export interface SetViewMessage {
    type: 'setView';
    zoom?: number;
    background?: Background;
    fit?: boolean;
}

export type ToWebviewMessage = ShowImageMessage | ShowErrorMessage | ShowBusyMessage | HighlightMessage | SetViewMessage;

/** 웹뷰 → 확장: 이미지를 실제로 그렸다는 회신(테스트가 크기를 단언한다). */
export interface ImageShownMessage {
    type: 'imageShown';
    naturalWidth: number;
    naturalHeight: number;
}

/** 웹뷰 → 확장: 이미지를 클릭했다(이미지 픽셀 좌표). */
export interface ClickMessage {
    type: 'click';
    x: number;
    y: number;
}

/** 웹뷰 → 확장: 보기 상태가 바뀌었다(줌/배경). */
export interface ViewStateMessage {
    type: 'viewState';
    zoom: number;
    background: Background;
}

/** 웹뷰 → 확장: 렌더 크기를 지정했다(null = 자동). */
export interface SetSizeMessage {
    type: 'setSize';
    width: number | null;
    height: number | null;
}

/** 웹뷰 → 확장 메시지. */
export type FromWebviewMessage = ImageShownMessage | ClickMessage | ViewStateMessage | SetSizeMessage;

function isFiniteNumber(value: unknown): value is number {
    return typeof value === 'number' && Number.isFinite(value);
}

/** 렌더 크기 값: null 또는 1~{@link MAX_RENDER_SIZE} 범위의 유한한 수. 그 외는 undefined(= 잘못된 값). */
function parseSize(value: unknown): number | null | undefined {
    if (value === null) {
        return null;
    } else if (isFiniteNumber(value) && value >= 1 && value <= MAX_RENDER_SIZE) {
        return value;
    } else {
        return undefined;
    }
}

/** 알 수 없거나 형식/범위가 틀린 메시지면 undefined를 돌려준다(호출자는 무시한다). */
export function parseFromWebview(raw: unknown): FromWebviewMessage | undefined {
    if (typeof raw !== 'object' || raw === null) {
        return undefined;
    } else {
        // 객체: 계속 검사.
    }
    const m = raw as Record<string, unknown>;
    switch (m.type) {
        case 'imageShown':
            return isFiniteNumber(m.naturalWidth) && isFiniteNumber(m.naturalHeight)
                ? { type: 'imageShown', naturalWidth: m.naturalWidth, naturalHeight: m.naturalHeight }
                : undefined;
        case 'click':
            return isFiniteNumber(m.x) && isFiniteNumber(m.y) ? { type: 'click', x: m.x, y: m.y } : undefined;
        case 'viewState':
            return isFiniteNumber(m.zoom) && m.zoom >= MIN_ZOOM && m.zoom <= MAX_ZOOM && BACKGROUNDS.includes(m.background as Background)
                ? { type: 'viewState', zoom: m.zoom, background: m.background as Background }
                : undefined;
        case 'setSize': {
            const width = parseSize(m.width);
            const height = parseSize(m.height);
            return width !== undefined && height !== undefined ? { type: 'setSize', width, height } : undefined;
        }
        default:
            return undefined;
    }
}
