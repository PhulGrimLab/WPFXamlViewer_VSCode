import * as vscode from 'vscode';
import { CONFIG_LANGUAGE, CONFIG_SECTION } from './constants';
import { LanguageSetting, getLanguage, resolveLanguage, setLanguage, t } from './i18n';

/** 언어 전환 명령 ID(package.json과 같아야 한다). */
export const COMMAND_SWITCH_LANGUAGE = 'wpfXamlViewer.switchLanguage';

/** 설정(`wpfXamlViewer.language`)과 VS Code 표시 언어로 현재 언어를 정해 적용한다. 바뀌었으면 true. */
function applyConfiguredLanguage(): boolean {
    const before = getLanguage();
    const setting = vscode.workspace.getConfiguration(CONFIG_SECTION).get<string>(CONFIG_LANGUAGE, 'auto');
    setLanguage(resolveLanguage(setting, vscode.env.language));
    return getLanguage() !== before;
}

/**
 * 표시 언어를 초기화하고, 설정이 바뀌거나 전환 명령을 쓰면 `onChanged`를 부른다.
 * 기본(auto)은 VS Code 표시 언어를 따른다(VS Code는 기본적으로 Windows 언어를 쓴다). 한국어가 아니면 영어.
 */
export function registerLanguage(context: vscode.ExtensionContext, onChanged: () => void): void {
    applyConfiguredLanguage();
    context.subscriptions.push(
        vscode.workspace.onDidChangeConfiguration((e) => {
            if (e.affectsConfiguration(`${CONFIG_SECTION}.${CONFIG_LANGUAGE}`) && applyConfiguredLanguage()) {
                onChanged();
            } else {
                // 언어와 무관한 설정 변경이거나 실제 언어가 같다.
            }
        }),
        vscode.commands.registerCommand(COMMAND_SWITCH_LANGUAGE, async () => {
            const items: (vscode.QuickPickItem & { value: LanguageSetting })[] = [
                { label: t('lang.auto'), value: 'auto' },
                { label: t('lang.ko'), value: 'ko' },
                { label: t('lang.en'), value: 'en' },
            ];
            const picked = await vscode.window.showQuickPick(items, { placeHolder: t('lang.pick') });
            if (!picked) {
                return;
            } else {
                // 선택함: 사용자 설정에 저장한다(설정 변경 이벤트가 적용한다).
            }
            await vscode.workspace.getConfiguration(CONFIG_SECTION).update(CONFIG_LANGUAGE, picked.value, vscode.ConfigurationTarget.Global);
            void vscode.window.showInformationMessage(t('lang.changed', picked.value === 'auto' ? t('lang.auto') : picked.label));
        }),
    );
}
