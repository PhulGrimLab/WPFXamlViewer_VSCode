import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { COMMAND_OPEN_PREVIEW, HOST_LOG_DIR_NAME } from './constants';
import { HostClient, LogFn } from './hostClient';
import { resolveHostExe } from './hostLocator';
import { LogId, formatLogLine } from './logFormat';
import { PreviewController } from './previewController';

/** 통합 테스트(T4/T5)가 확장 상태를 읽는 API. activate의 반환값으로 노출한다. */
export interface ExtensionTestApi {
    getPreviewState(): { renderCount: number; lastImageSize: { width: number; height: number } | undefined };
    /** 호스트 exe를 찾았는지. */
    hostFound: boolean;
    /** 현재 호스트 프로세스 id(없으면 undefined). */
    getHostPid(): number | undefined;
    /** 확장 로그 줄(최근 {@link TEST_LOG_BUFFER_MAX}개). */
    getLogLines(): string[];
}

/** 테스트 API용 로그 버퍼 최대 줄 수. */
const TEST_LOG_BUFFER_MAX = 200;

/** 출력 채널 이름. */
const OUTPUT_CHANNEL_NAME = 'WPF XAML Viewer';

/**
 * 확장 진입점. 출력 채널을 만들고 E001 로그를 남긴 뒤, 호스트를 찾아 HostClient와 미리보기 명령을 등록한다.
 * Owner: VS Code 확장 호스트. Lifetime: 확장 활성~비활성. 모든 자원은 context.subscriptions로 정리한다.
 * 호스트 exe가 없으면 명령은 등록하되 실행 시 안내 알림만 띄운다(확장은 계속 살아 있다, doc/03 I-05).
 */
export function activate(context: vscode.ExtensionContext): ExtensionTestApi {
    const channel = vscode.window.createOutputChannel(OUTPUT_CHANNEL_NAME);
    context.subscriptions.push(channel);

    const version = (context.extension.packageJSON as { version?: string }).version ?? 'unknown';
    channel.appendLine(formatLogLine('Info', LogId.ExtensionActivated, `activated version=${version}`));

    const logLines: string[] = [];
    const log: LogFn = (level, id, message) => {
        const line = formatLogLine(level, id, message);
        channel.appendLine(line);
        logLines.push(line);
        if (logLines.length > TEST_LOG_BUFFER_MAX) {
            logLines.shift();
        } else {
            // 버퍼 여유 있음.
        }
    };
    const api: ExtensionTestApi = {
        getPreviewState: () => ({ renderCount: 0, lastImageSize: undefined }),
        hostFound: false,
        getHostPid: () => undefined,
        getLogLines: () => [...logLines],
    };
    const hostExe = resolveHostExe(context.extensionPath, fs.existsSync);
    if (!hostExe) {
        log('Error', LogId.HostCrashed, 'host exe not found');
        context.subscriptions.push(vscode.commands.registerCommand(COMMAND_OPEN_PREVIEW, () => {
            void vscode.window.showErrorMessage(
                'WPF XAML Viewer: 렌더 호스트(XamlRenderHost.exe)를 찾을 수 없습니다. `dotnet build host/XamlRenderHost.slnx`로 빌드하거나 XAMLVIEWER_HOST_EXE를 설정하세요.');
        }));
        return api;
    } else {
        // 호스트 있음: 정상 등록.
    }

    const logDir = path.join(context.globalStorageUri.fsPath, HOST_LOG_DIR_NAME);
    fs.mkdirSync(logDir, { recursive: true });
    const client = new HostClient({ command: hostExe, args: ['serve', '--log-dir', logDir], log });
    const controller = new PreviewController(client);
    context.subscriptions.push(
        controller,
        { dispose: () => { void client.dispose(); } },
        vscode.commands.registerCommand(COMMAND_OPEN_PREVIEW, () => controller.open()),
    );
    api.hostFound = true;
    api.getPreviewState = () => ({ renderCount: controller.renderCount, lastImageSize: controller.lastImageSize });
    api.getHostPid = () => client.pid;
    return api;
}

/** 비활성화 시 정리는 context.subscriptions가 수행한다. */
export function deactivate(): void {
    // 의도적으로 비어 있음.
}
