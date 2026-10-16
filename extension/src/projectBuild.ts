import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { CONFIG_BUILD_CONFIGURATION, CONFIG_BUILD_EXTRA_ARGS, CONFIG_BUILD_TARGET_FRAMEWORK, CONFIG_SECTION, TASK_TYPE_DOTNET } from './constants';
import {
    CONFIGURED_COMMANDS,
    DotnetAction,
    actionLabel,
    buildDotnetArgs,
    declaresTargetFramework,
    findNearestProject,
    isBuildableTarget,
    normalizeConfiguration,
    normalizeExtraArgs,
    normalizeTargetFramework,
} from './dotnetCommands';
import { LogFn } from './hostClient';
import { t } from './i18n';
import { LogId } from './logFormat';

/** 동작별 명령 ID(package.json과 같아야 한다). */
const ACTION_COMMANDS: Record<DotnetAction, string> = {
    build: 'wpfXamlViewer.buildProject',
    rebuild: 'wpfXamlViewer.rebuildProject',
    clean: 'wpfXamlViewer.cleanProject',
    restore: 'wpfXamlViewer.restoreProject',
};

/** 열린 XAML이 속한 프로젝트를 빌드하는 명령 ID. */
export const COMMAND_BUILD_XAML_PROJECT = 'wpfXamlViewer.buildXamlProject';

/**
 * `.csproj`/`.sln` 우클릭 명령과 "이 XAML의 프로젝트 빌드" 명령을 등록한다.
 * 빌드는 프로젝트의 MSBuild 대상(사용자 코드)을 실행하므로 신뢰되지 않은 폴더에서는 실행하지 않는다.
 * 호스트 exe 유무와 무관하게 동작해야 하므로 호스트 확인 앞에서 호출한다.
 */
export function registerProjectBuildCommands(context: vscode.ExtensionContext, log: LogFn): void {
    for (const action of Object.keys(ACTION_COMMANDS) as DotnetAction[]) {
        context.subscriptions.push(vscode.commands.registerCommand(ACTION_COMMANDS[action], (resource?: vscode.Uri) => {
            const target = resource?.fsPath ?? vscode.window.activeTextEditor?.document.uri.fsPath;
            return runDotnet(action, target, log);
        }));
    }
    // 우클릭 "구성 선택 빌드" 서브메뉴: 설정과 무관하게 Debug/Release를 직접 고른다.
    for (const { command, action, configuration } of CONFIGURED_COMMANDS) {
        context.subscriptions.push(vscode.commands.registerCommand(command, (resource?: vscode.Uri) => {
            const target = resource?.fsPath ?? vscode.window.activeTextEditor?.document.uri.fsPath;
            return runDotnet(action, target, log, configuration);
        }));
    }
    context.subscriptions.push(vscode.commands.registerCommand(COMMAND_BUILD_XAML_PROJECT, (resource?: vscode.Uri) => {
        const xamlPath = resource?.fsPath ?? vscode.window.activeTextEditor?.document.uri.fsPath;
        if (!xamlPath) {
            void vscode.window.showWarningMessage(t('build.noXaml'));
            return undefined;
        } else {
            // 파일 있음: 가장 가까운 프로젝트 탐색.
        }
        const project = findNearestProject(xamlPath, (directory) => fs.readdirSync(directory));
        if (!project) {
            void vscode.window.showWarningMessage(t('build.noProject', path.basename(xamlPath)));
            return undefined;
        } else {
            return runDotnet('build', project, log);
        }
    }));
}

/** 대상 파일에 동작을 Task로 실행한다. 오류/경고는 Problems 패널(`$msCompile`)에 올라간다. `configurationOverride`가 있으면 설정 대신 그 구성을 쓴다. */
async function runDotnet(action: DotnetAction, targetPath: string | undefined, log: LogFn, configurationOverride?: string): Promise<void> {
    const task = createDotnetTask(action, targetPath, log, configurationOverride);
    if (task) {
        await vscode.tasks.executeTask(task);
    } else {
        // 검사에 걸려 안내만 했다.
    }
}

/** 자동 빌드를 기다리는 최대 시간(ms). 넘으면 Task를 끝내고 실패로 처리한다. */
const AUTO_BUILD_TIMEOUT_MS = 10 * 60 * 1000;

/** 미리보기 자동 빌드의 결과. exitCode가 없으면 종료 코드를 알 수 없음(Task 시작 실패 등). */
export interface BuildOutcome {
    ok: boolean;
    exitCode?: number;
}

/**
 * 미리보기에 필요한 프로젝트를 빌드(`dotnet build`, 설정 구성)하고 끝날 때까지 기다린다. 신뢰된 폴더에서만 호출해야 한다(빌드는 프로젝트 코드를 실행한다).
 * 출력은 Task 터미널에 표시되고 오류는 Problems에 올라간다. 실패해도 예외 대신 `ok:false`를 돌려준다.
 */
export async function buildProjectAndWait(projectPath: string, log: LogFn): Promise<BuildOutcome> {
    const task = createDotnetTask('build', projectPath, log);
    if (!task) {
        return { ok: false };
    } else {
        // 빌드 Task 준비됨.
    }
    // 종료 이벤트가 `executeTask`의 결과보다 먼저 올 수 있어, 실행 전에 구독해 두고 실행 객체가 정해지면 쌓아 둔 이벤트와도 비교한다.
    // 제한 시간이 지나면 Task를 끝내고 실패로 처리한다(끝나지 않는 빌드가 "빌드 중" 상태에 영원히 머무르지 않게).
    let execution: vscode.TaskExecution | undefined;
    const early: vscode.TaskProcessEndEvent[] = [];
    let finish: (outcome: BuildOutcome) => void = () => undefined;
    const ended = new Promise<BuildOutcome>((resolve) => { finish = resolve; });
    const subscription = vscode.tasks.onDidEndTaskProcess((e) => {
        if (!execution) {
            early.push(e);
        } else if (e.execution === execution) {
            finish({ ok: e.exitCode === 0, exitCode: e.exitCode });
        } else {
            // 다른 Task의 종료.
        }
    });
    const timer = setTimeout(() => {
        execution?.terminate();
        finish({ ok: false });
    }, AUTO_BUILD_TIMEOUT_MS);
    try {
        execution = await vscode.tasks.executeTask(task);
        const hit = early.find((e) => e.execution === execution);
        if (hit) {
            finish({ ok: hit.exitCode === 0, exitCode: hit.exitCode });
        } else {
            // 아직 끝나지 않음: 구독이 알려 준다.
        }
        return await ended;
    } catch {
        return { ok: false };
    } finally {
        clearTimeout(timer);
        subscription.dispose();
    }
}

/** 동작 Task를 만든다. 대상이 빌드 가능한 파일이 아니거나 폴더를 신뢰하지 않으면 안내 후 undefined. */
function createDotnetTask(action: DotnetAction, targetPath: string | undefined, log: LogFn, configurationOverride?: string): vscode.Task | undefined {
    if (!targetPath || !isBuildableTarget(targetPath)) {
        void vscode.window.showWarningMessage(t('build.badTarget'));
        return undefined;
    } else {
        // 빌드 대상 파일.
    }
    if (!vscode.workspace.isTrusted) {
        void vscode.window.showWarningMessage(t('build.untrusted'));
        return undefined;
    } else {
        // 신뢰된 폴더.
    }

    const settings = vscode.workspace.getConfiguration(CONFIG_SECTION);
    const configuration = normalizeConfiguration(configurationOverride ?? settings.get(CONFIG_BUILD_CONFIGURATION));
    const targetFramework = pickTargetFramework(targetPath, normalizeTargetFramework(settings.get(CONFIG_BUILD_TARGET_FRAMEWORK)));
    const extraArgs = normalizeExtraArgs(settings.get(CONFIG_BUILD_EXTRA_ARGS));
    const args = buildDotnetArgs(action, targetPath, configuration, { targetFramework, extraArgs });
    const name = `${actionLabel(action)} (${configuration}): ${path.basename(targetPath)}`;
    const task = new vscode.Task(
        { type: TASK_TYPE_DOTNET, action, target: targetPath },
        vscode.TaskScope.Workspace,
        name,
        'WPF XAML',
        new vscode.ProcessExecution('dotnet', args, { cwd: path.dirname(targetPath) }),
        '$msCompile',
    );
    task.group = action === 'clean' || action === 'restore' ? undefined : vscode.TaskGroup.Build;
    task.presentationOptions = { reveal: vscode.TaskRevealKind.Always, panel: vscode.TaskPanelKind.Shared, clear: true };
    log('Info', LogId.ProjectBuild, `${action} target=${path.basename(targetPath)} configuration=${configuration} framework=${targetFramework || '(all)'} extraArgs=${extraArgs.length}`);
    return task;
}

/**
 * 설정한 TFM을 이 대상에 쓸 수 있는가를 판단한다. `.csproj`가 `<TargetFrameworks>`에 그 TFM을 선언한 경우에만 돌려주고,
 * 그 밖(솔루션 파일, 단일 대상 프로젝트, 선언을 못 찾음)에서는 빈 문자열이라 `-f`를 붙이지 않는다(없는 TFM은 빌드 오류가 된다).
 */
function pickTargetFramework(targetPath: string, configured: string): string {
    if (!configured || path.extname(targetPath).toLowerCase() !== '.csproj') {
        return '';
    } else {
        try {
            return declaresTargetFramework(fs.readFileSync(targetPath, 'utf8'), configured) ? configured : '';
        } catch {
            return ''; // 읽을 수 없음: 지정하지 않는다.
        }
    }
}
