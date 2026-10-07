/**
 * 확장 전체에서 쓰는 이름 있는 상수(CLAUDE.md 규칙 7: Magic Number 금지).
 */

/** 요청 하나가 응답 없이 기다리는 기본 최대 시간(ms). 넘으면 호스트를 kill하고 재시작한다(doc/01 3.2). */
export const DEFAULT_REQUEST_TIMEOUT_MS = 10_000;

/** 종료 시 호스트에 shutdown을 보낸 뒤 스스로 끝나길 기다리는 시간(ms). 넘으면 kill한다. */
export const SHUTDOWN_GRACE_MS = 3_000;

/** shutdown 요청에 쓰는 예약 id. 일반 요청 id는 1부터 시작한다. */
export const SHUTDOWN_REQUEST_ID = 0;

/** 편집 후 마지막 입력으로부터 이 시간(ms)이 지나야 렌더를 요청한다(doc/02 M3.2). */
export const RENDER_DEBOUNCE_MS = 300;

/** 개발 중 호스트 exe 위치(확장 루트 기준). 테스트가 쓰는 경로와 같다. */
export const DEV_HOST_RELATIVE_PATH = '../host/XamlRenderHost/bin/Debug/net10.0-windows/win-x64/XamlRenderHost.exe';

/** 패키징 후 호스트 exe 위치(확장 루트 기준, M6에서 채워진다). */
export const PACKAGED_HOST_RELATIVE_PATH = 'bin/host/XamlRenderHost.exe';

/** 확장 명령 ID. */
export const COMMAND_OPEN_PREVIEW = 'wpfXamlViewer.openPreview';

/** 미리보기 패널의 viewType. */
export const PREVIEW_VIEW_TYPE = 'wpfXamlViewer.preview';

/** 호스트 로그 파일 하위 폴더 이름(globalStorage 아래). */
export const HOST_LOG_DIR_NAME = 'logs';
