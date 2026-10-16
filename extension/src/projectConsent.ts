import * as path from 'path';

/** 값을 읽고 쓰는 최소 저장소(vscode.Memento 호환). 단위 테스트에서 가짜로 바꿀 수 있게 vscode 모듈에 의존하지 않는다. */
export interface ConsentStore {
    get<T>(key: string, defaultValue: T): T;
    update(key: string, value: unknown): PromiseLike<void>;
}

const STORE_KEY = 'projectConsent.v1';

/**
 * 프로젝트별 "코드 실행 허용" 기록. Workspace Trust만으로는 부족한 이유: 신뢰한 폴더에 **저장소가 담고 있는 `bin/` DLL**이 있으면, 빌드 없이도 .xaml을 여는 순간 그 DLL의
 * 모듈 초기화 코드와 컨트롤 생성자가 실행된다(자동 미리보기 기본 켜짐). 그래서 프로젝트(.csproj, 없으면 워크스페이스 폴더)마다 처음 한 번 사용자에게 확인받고 워크스페이스 저장소에 기억한다.
 * 키는 대소문자 무시 정규화한 경로다.
 */
export class ProjectConsent {
    constructor(private readonly _store: ConsentStore) { }

    private static normalize(key: string): string {
        return path.normalize(key).toLowerCase();
    }

    isGranted(key: string): boolean {
        return this._store.get<string[]>(STORE_KEY, []).includes(ProjectConsent.normalize(key));
    }

    async grant(key: string): Promise<void> {
        const granted = this._store.get<string[]>(STORE_KEY, []);
        const normalized = ProjectConsent.normalize(key);
        if (!granted.includes(normalized)) {
            await this._store.update(STORE_KEY, [...granted, normalized]);
        } else {
            // 이미 허용됨.
        }
    }

    /** 모든 허용 기록을 지운다(다시 확인을 받는다). */
    async clear(): Promise<void> {
        await this._store.update(STORE_KEY, []);
    }
}
