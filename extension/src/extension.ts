import * as vscode from 'vscode';
import { LogId, formatLogLine } from './logFormat';

/** 출력 채널 이름. */
const OUTPUT_CHANNEL_NAME = 'WPF XAML Viewer';

/**
 * 확장 진입점. 활성화 시 출력 채널을 만들고 E001 로그를 남긴다.
 * Owner: VS Code 확장 호스트. Lifetime: 확장 활성~비활성. 채널은 context.subscriptions에 등록해 자동 정리한다.
 * (M0 단계: 명령/미리보기는 아직 없다. 이후 마일스톤에서 추가)
 */
export function activate(context: vscode.ExtensionContext): void {
    const channel = vscode.window.createOutputChannel(OUTPUT_CHANNEL_NAME);
    context.subscriptions.push(channel);

    const version = (context.extension.packageJSON as { version?: string }).version ?? 'unknown';
    channel.appendLine(formatLogLine('Info', LogId.ExtensionActivated, `activated version=${version}`));
}

/** 비활성화 시 정리할 별도 자원이 없다(채널은 subscriptions가 정리). */
export function deactivate(): void {
    // 의도적으로 비어 있음.
}
