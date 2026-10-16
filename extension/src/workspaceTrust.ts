import * as vscode from 'vscode';

/**
 * 이 파일에 대해 프로젝트 코드(빌드/DLL 로드)를 실행해도 되는가. Workspace Trust에 더해 **파일이 열린 워크스페이스 폴더 안에 있어야** 한다.
 * 폴더 없이 파일만 연 창(emptyWindow)은 기본 설정에서 "신뢰됨"으로 취급되는데, 그 파일은 어떤 폴더의 것인지 보장이 없다
 * (받은 압축을 풀어 그 안의 .xaml만 열면 위쪽 폴더의 .csproj를 빌드/로드하게 된다).
 */
export function canRunProjectCode(filePath: string | undefined): boolean {
    if (!vscode.workspace.isTrusted || !filePath) {
        return false;
    } else {
        return vscode.workspace.getWorkspaceFolder(vscode.Uri.file(filePath)) !== undefined;
    }
}
