import { spawnSync } from 'child_process';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { downloadAndUnzipVSCode, resolveCliArgsFromVSCodeExecutablePath, runTests } from '@vscode/test-electron';

/**
 * 설치 스모크(I-09, doc/02 M6.1/6.2): 패키징한 `.vsix`를 **임시 VS Code 프로필에 설치**한 뒤, 개발 경로가 아니라
 * 설치된 확장이 번들 호스트(bin/host)로 실제 net10 WPF 프로젝트를 미리보기하는지 확인한다.
 *
 * 구성: ① `.vsix` 위치(XAMLVIEWER_VSIX 또는 artifacts의 최신) ② 임시 user-data/extensions 폴더에 `--install-extension`
 * ③ `dotnet new wpf`로 만든 샘플 프로젝트(사용자 컨트롤 + App.xaml 리소스 + x:Class/이벤트)를 빌드
 * ④ 개발 확장 없이 설치본만 로드한 VS Code에서 smoke.test.js 실행. 개발 호스트 경로(../host/...)는 설치 폴더에서는 존재하지 않고
 * XAMLVIEWER_HOST_EXE는 비워서 번들 호스트만 쓸 수 있게 한다.
 * 먼저 `npm run package`(또는 tools/package/build_vsix.ps1)로 .vsix를 만들어야 한다.
 */

/** dotnet 명령 제한 시간(ms). 첫 `dotnet new`/빌드는 오래 걸릴 수 있다. */
const DOTNET_TIMEOUT_MS = 300_000;

/** `.vsix`가 있는 폴더(저장소 artifacts). out/test/smoke 에서 저장소 루트까지 네 단계 위. */
const ARTIFACTS_DIR = path.resolve(__dirname, '../../../../artifacts');

function findVsix(): string {
    const fromEnv = process.env.XAMLVIEWER_VSIX;
    if (fromEnv) {
        return fromEnv;
    } else if (fs.existsSync(ARTIFACTS_DIR)) {
        const candidates = fs.readdirSync(ARTIFACTS_DIR).filter((f) => f.endsWith('.vsix'))
            .map((f) => path.join(ARTIFACTS_DIR, f))
            .sort((a, b) => fs.statSync(b).mtimeMs - fs.statSync(a).mtimeMs);
        if (candidates.length > 0) {
            return candidates[0];
        } else {
            // 아래 오류로.
        }
    } else {
        // 아래 오류로.
    }
    throw new Error('.vsix가 없습니다. 먼저 `npm run package` 를 실행하세요.');
}

function run(command: string, args: string[], cwd?: string, shell = false): void {
    const result = shell
        ? spawnSync([command, ...args].map((a) => `"${a}"`).join(' '), { cwd, shell: true, encoding: 'utf8', timeout: DOTNET_TIMEOUT_MS })
        : spawnSync(command, args, { cwd, encoding: 'utf8', timeout: DOTNET_TIMEOUT_MS });
    if (result.status !== 0) {
        throw new Error(`명령 실패: ${command} ${args.join(' ')}\n${result.stdout}\n${result.stderr}`);
    } else {
        // 성공.
    }
}

/** `dotnet new wpf` 프로젝트를 만들고 App.xaml 리소스, 사용자 컨트롤, x:Class/이벤트가 있는 MainWindow를 얹은 뒤 빌드한다. */
function createSampleProject(workspace: string): string {
    const projectDir = path.join(workspace, 'SmokeApp');
    run('dotnet', ['new', 'wpf', '-n', 'SmokeApp', '-o', projectDir, '--framework', 'net10.0']);
    fs.writeFileSync(path.join(projectDir, 'Badge.cs'), `using System.Windows;
using System.Windows.Media;

namespace SmokeApp;

/// <summary>스모크용 사용자 컨트롤: 50x20 초록 사각형.</summary>
public class Badge : FrameworkElement
{
    protected override Size MeasureOverride(Size availableSize) => new(50, 20);

    protected override void OnRender(DrawingContext drawingContext)
        => drawingContext.DrawRectangle(Brushes.SeaGreen, null, new Rect(0, 0, 50, 20));
}
`);
    fs.writeFileSync(path.join(projectDir, 'App.xaml'), `<Application x:Class="SmokeApp.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
        <SolidColorBrush x:Key="Accent" Color="#FF3B82F6"/>
    </Application.Resources>
</Application>
`);
    fs.writeFileSync(path.join(projectDir, 'MainWindow.xaml'), `<Window x:Class="SmokeApp.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:SmokeApp"
        Title="MainWindow" Height="450" Width="800">
    <Grid>
        <Button Content="Click" Width="120" Height="32" Background="{StaticResource Accent}" Click="OnClick"
                HorizontalAlignment="Left" VerticalAlignment="Top"/>
        <local:Badge HorizontalAlignment="Right" VerticalAlignment="Bottom"/>
    </Grid>
</Window>
`);
    // 코드 비하인드(x:Class 대상). 빌드가 통과해야 하므로 OnClick이 있어야 한다.
    const codeBehind = path.join(projectDir, 'MainWindow.xaml.cs');
    const original = fs.readFileSync(codeBehind, 'utf8');
    fs.writeFileSync(codeBehind, original.replace(/(public MainWindow\(\)\s*\{[\s\S]*?\}\s*)\}\s*$/, '$1    private void OnClick(object sender, RoutedEventArgs e) { }\n}\n'));
    run('dotnet', ['build', projectDir, '-c', 'Debug', '--nologo', '-v', 'q']);
    return path.join(projectDir, 'MainWindow.xaml');
}

async function main(): Promise<void> {
    const vsix = findVsix();
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'xamlviewer-smoke-'));
    const userDataDir = path.join(root, 'user-data');
    const extensionsDir = path.join(root, 'extensions');
    const workspace = path.join(root, 'ws');
    for (const dir of [userDataDir, extensionsDir, workspace]) {
        fs.mkdirSync(dir, { recursive: true });
    }

    const vscodeExe = await downloadAndUnzipVSCode();
    const [cli, ...cliArgs] = resolveCliArgsFromVSCodeExecutablePath(vscodeExe);
    run(cli, [...cliArgs, '--install-extension', vsix, '--extensions-dir', extensionsDir, '--user-data-dir', userDataDir], undefined, true);

    const xamlPath = createSampleProject(workspace);

    // 설치본만 로드하기 위한 "빈" 개발 확장(테스트 러너가 extensionDevelopmentPath를 요구한다). main이 없어 아무 일도 하지 않는다.
    const stubExtension = path.join(root, 'stub-extension');
    fs.mkdirSync(stubExtension, { recursive: true });
    fs.writeFileSync(path.join(stubExtension, 'package.json'),
        JSON.stringify({ name: 'xaml-smoke-stub', publisher: 'test', version: '0.0.0', engines: { vscode: '^1.90.0' } }));

    // 개발용 호스트 지정이 번들 호스트를 가리지 않도록 환경 변수를 제거한 자식 환경으로 실행한다.
    delete process.env.XAMLVIEWER_HOST_EXE;
    await runTests({
        vscodeExecutablePath: vscodeExe,
        extensionDevelopmentPath: stubExtension,
        extensionTestsPath: path.resolve(__dirname, 'index'),
        extensionTestsEnv: { XAMLVIEWER_SMOKE_XAML: xamlPath, XAMLVIEWER_SMOKE_EXTDIR: extensionsDir },
        launchArgs: [workspace, '--user-data-dir', userDataDir, '--extensions-dir', extensionsDir, '--disable-workspace-trust'],
    });

    fs.rmSync(root, { recursive: true, force: true });
}

main().catch((e) => {
    console.error(e);
    process.exit(1);
});
