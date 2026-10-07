/**
 * 마지막 호출 후 지정 시간이 지나야 한 번만 실행하는 디바운서(doc/03 X-D01).
 * Owner: 이를 만든 미리보기 컨트롤러. Lifetime: 컨트롤러와 같다(dispose에서 대기 중 타이머 취소).
 */
export class Debouncer {
    private _timer: NodeJS.Timeout | undefined;

    constructor(private readonly _delayMs: number) {}

    /** action 실행을 예약한다. 대기 중인 이전 예약은 취소된다. */
    schedule(action: () => void): void {
        this.cancel();
        this._timer = setTimeout(() => {
            this._timer = undefined;
            action();
        }, this._delayMs);
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
