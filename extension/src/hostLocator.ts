import * as path from 'path';
import { DEV_HOST_RELATIVE_PATH, PACKAGED_HOST_RELATIVE_PATH } from './constants';

/**
 * 호스트 exe 경로를 찾는다(doc/04 8절: 경로 해석은 이 함수 한 곳에만 둔다).
 * 입력: 확장 루트 경로, 파일 존재 확인 함수(테스트 주입용). 출력: 처음 존재하는 후보의 절대 경로, 없으면 undefined.
 * 우선순위: 환경 변수 XAMLVIEWER_HOST_EXE → 패키징 위치 → 개발 빌드 위치.
 * 순수 로직(파일 시스템은 주입)이라 VS Code 없이 테스트한다.
 */
export function resolveHostExe(
    extensionPath: string,
    exists: (p: string) => boolean,
    envOverride: string | undefined = process.env.XAMLVIEWER_HOST_EXE,
): string | undefined {
    const candidates: string[] = [];
    if (envOverride) {
        candidates.push(envOverride);
    } else {
        // 재정의 없음: 기본 후보만 사용.
    }
    candidates.push(path.resolve(extensionPath, PACKAGED_HOST_RELATIVE_PATH));
    candidates.push(path.resolve(extensionPath, DEV_HOST_RELATIVE_PATH));
    return candidates.find((c) => exists(c));
}
