/**
 * 확장 ↔ 웹뷰 메시지 스키마와 검증(doc/03 X-M01~M02, 1.1절).
 * 웹뷰가 보낸 메시지는 신뢰하지 않고 형태를 검사한 뒤에만 사용한다.
 */

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

export type ToWebviewMessage = ShowImageMessage | ShowErrorMessage | ShowBusyMessage;

/** 웹뷰 → 확장: 이미지를 실제로 그렸다는 회신(테스트가 크기를 단언한다). */
export interface ImageShownMessage {
    type: 'imageShown';
    naturalWidth: number;
    naturalHeight: number;
}

/** 웹뷰 → 확장 메시지. */
export type FromWebviewMessage = ImageShownMessage;

/** 알 수 없거나 형식이 틀린 메시지면 undefined를 돌려준다(호출자는 무시한다). */
export function parseFromWebview(raw: unknown): FromWebviewMessage | undefined {
    if (typeof raw !== 'object' || raw === null) {
        return undefined;
    } else {
        // 객체: 계속 검사.
    }
    const m = raw as Record<string, unknown>;
    if (m.type === 'imageShown' && Number.isFinite(m.naturalWidth) && Number.isFinite(m.naturalHeight)) {
        return { type: 'imageShown', naturalWidth: m.naturalWidth as number, naturalHeight: m.naturalHeight as number };
    } else {
        return undefined;
    }
}
