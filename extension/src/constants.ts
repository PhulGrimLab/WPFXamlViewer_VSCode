/**
 * 확장 전체에서 쓰는 이름 있는 상수(CLAUDE.md 규칙 7: Magic Number 금지).
 */

/** 요청 하나가 응답 없이 기다리는 기본 최대 시간(ms). 넘으면 호스트를 kill하고 재시작한다(doc/01 3.2). */
export const DEFAULT_REQUEST_TIMEOUT_MS = 10_000;

/** 종료 시 호스트에 shutdown을 보낸 뒤 스스로 끝나길 기다리는 시간(ms). 넘으면 kill한다. */
export const SHUTDOWN_GRACE_MS = 3_000;

/** shutdown 요청에 쓰는 예약 id. 일반 요청 id는 1부터 시작한다. */
export const SHUTDOWN_REQUEST_ID = 0;
