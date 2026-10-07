import * as path from 'path';
import { DEV_HOST_RELATIVE_PATH, PACKAGED_HOST_RELATIVE_PATH } from './constants';

/**
 * 호스트 exe 경로를 찾는다(doc/04 8절: 경로 해석은 이 함수 한 곳에만 둔다).
 * 입력: 확장 루트 경로, 파일 존재 확인 함수(테스트 주입용). 출력: 처음 존재하는 후보의 절대 경로, 없으면 undefined.
 * 환경 변수 XAMLVIEWER_HOST_EXE가 있으면 **그 경로만** 쓴다(없으면 undefined). 명시한 경로가 틀렸을 때 다른 호스트로 조용히
 * 대체되면 잘못된 호스트를 쓰는 것을 알아채지 못하기 때문이다. 없으면 패키징 위치 → 개발 빌드 위치 순.
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
        candidates.push(path.resolve(extensionPath, PACKAGED_HOST_RELATIVE_PATH));
        candidates.push(path.resolve(extensionPath, DEV_HOST_RELATIVE_PATH));
    }
    return candidates.find((c) => exists(c));
}
