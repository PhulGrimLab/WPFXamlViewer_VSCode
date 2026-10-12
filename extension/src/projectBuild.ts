import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { CONFIG_BUILD_CONFIGURATION, CONFIG_SECTION, TASK_TYPE_DOTNET } from './constants';
import {
    ACTION_LABELS,
    DotnetAction,
    buildDotnetArgs,
    findNearestProject,
    isBuildableTarget,
    normalizeConfiguration,
} from './dotnetCommands';
import { LogFn } from './hostClient';
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
    context.subscriptions.push(vscode.commands.registerCommand(COMMAND_BUILD_XAML_PROJECT, (resource?: vscode.Uri) => {
        const xamlPath = resource?.fsPath ?? vscode.window.activeTextEditor?.document.uri.fsPath;
        if (!xamlPath) {
            void vscode.window.showWarningMessage('WPF XAML: 빌드할 XAML 파일이 없습니다.');
            return undefined;
        } else {
            // 파일 있음: 가장 가까운 프로젝트 탐색.
        }
        const project = findNearestProject(xamlPath, (directory) => fs.readdirSync(directory));
        if (!project) {
            void vscode.window.showWarningMessage(`WPF XAML: ${path.basename(xamlPath)}이(가) 속한 .csproj를 찾지 못했습니다.`);
            return undefined;
        } else {
            return runDotnet('build', project, log);
        }
    }));
}

/** 대상 파일에 동작을 Task로 실행한다. 오류/경고는 Problems 패널(`$msCompile`)에 올라간다. */
async function runDotnet(action: DotnetAction, targetPath: string | undefined, log: LogFn): Promise<void> {
    if (!targetPath || !isBuildableTarget(targetPath)) {
        void vscode.window.showWarningMessage('WPF XAML: .csproj/.sln 파일에서 실행해 주세요.');
        return;
    } else {
        // 빌드 대상 파일.
    }
    if (!vscode.workspace.isTrusted) {
        void vscode.window.showWarningMessage('WPF XAML: 신뢰하지 않는 폴더에서는 프로젝트를 빌드하지 않습니다(빌드는 프로젝트 코드를 실행합니다). 폴더를 신뢰한 뒤 다시 시도하세요.');
        return;
    } else {
        // 신뢰된 폴더.
    }

    const configuration = normalizeConfiguration(vscode.workspace.getConfiguration(CONFIG_SECTION).get(CONFIG_BUILD_CONFIGURATION));
    const args = buildDotnetArgs(action, targetPath, configuration);
    const name = `${ACTION_LABELS[action]}: ${path.basename(targetPath)}`;
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
    log('Info', LogId.ProjectBuild, `${action} target=${path.basename(targetPath)} configuration=${configuration}`);
    await vscode.tasks.executeTask(task);
}
