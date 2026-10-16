/**
 * 마지막 호출 후 지정 시간이 지나야 한 번만 실행하는 디바운서(doc/03 X-D01).
 * Owner: 이를 만든 미리보기 컨트롤러. Lifetime: 컨트롤러와 같다(dispose에서 대기 중 타이머 취소).
 */
export class Debouncer {
    private _timer: NodeJS.Timeout | undefined;

    constructor(private readonly _delayMs: number) {}

    /** action 실행을 예약한다. 대기 중인 이전 예약은 취소된다. `delayMs`를 주면 이번 예약에만 생성자의 기본 지연 대신 그 값을 쓴다. */
    schedule(action: () => void, delayMs?: number): void {
        this.cancel();
        this._timer = setTimeout(() => {
            this._timer = undefined;
            action();
        }, delayMs ?? this._delayMs);
    }

    /** 대기 중인 예약이 있으면 취소한다. */
    cancel(): void {
        if (this._timer) {
            clearTimeout(this._timer);
            this._timer = undefined;
        } else {
            // 취소할 예약 없음.
        }
    }
}

/** 적응형 디바운스의 최소/최대 지연(ms). */
export const ADAPTIVE_DEBOUNCE_MIN_MS = 100;
export const ADAPTIVE_DEBOUNCE_MAX_MS = 300;

/**
 * 마지막 렌더가 걸린 시간에 맞춘 디바운스 지연. 렌더가 빠르면(작은 XAML은 수 ms~수십 ms) 오래 기다릴 이유가 없어 입력 뒤 미리보기가 갱신되기까지의 체감 지연이 줄고,
 * 느리면(큰 문서, 프로젝트 컨트롤) 렌더 시간의 2배까지 기다려 타이핑 중 불필요한 렌더 요청을 줄인다. 아직 렌더한 적이 없으면 최대값.
 */
export function adaptiveDebounceMs(lastRenderMs: number | undefined): number {
    if (lastRenderMs === undefined || !Number.isFinite(lastRenderMs)) {
        return ADAPTIVE_DEBOUNCE_MAX_MS;
    } else {
        return Math.min(ADAPTIVE_DEBOUNCE_MAX_MS, Math.max(ADAPTIVE_DEBOUNCE_MIN_MS, Math.round(lastRenderMs * 2)));
    }
}
