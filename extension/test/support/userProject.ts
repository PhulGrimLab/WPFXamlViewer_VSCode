import { execFileSync } from 'child_process';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';

/**
 * Tier 1(사용자 컨트롤 로드) 시험용 사용자 프로젝트 도우미. 호스트 테스트와 같은 샘플 프로젝트
 * (host/XamlRenderHost.Tests/Fixtures/projects/SampleControls)를 `dotnet build`로 한 번 빌드해 산출물을 재사용한다.
 * 실제 사용자 프로젝트 구조(`.csproj` + `bin/Debug/<TFM>/<이름>.dll`)를 임시 폴더에 만들어 호스트가 산출물을 찾게 한다.
 */

/** 샘플 사용자 프로젝트 파일(저장소 기준). out/test/support 에서 저장소 루트까지 네 단계 위. */
const SAMPLE_CSPROJ = path.resolve(__dirname,
    '../../../../host/XamlRenderHost.Tests/Fixtures/projects/SampleControls/SampleControls.csproj');

/** 샘플 어셈블리 이름(= 산출물 DLL 이름). */
export const SAMPLE_ASSEMBLY = 'SampleControls';

/** 산출물이 놓이는 하위 경로(실제 프로젝트의 bin/Debug/TFM 구조). */
const BIN_RELATIVE = path.join('bin', 'Debug', 'net10.0-windows');

/** 빌드 제한 시간(ms). 첫 빌드는 복원 때문에 오래 걸릴 수 있다. */
const BUILD_TIMEOUT_MS = 180_000;

let _buildOutput: string | undefined;

/** 샘플 프로젝트를 빌드하고 산출물 폴더를 돌려준다(프로세스 안에서 한 번만 빌드). 실패하면 빌드 출력과 함께 예외. */
export function buildSampleControls(): string {
    if (_buildOutput) {
        return _buildOutput;
    } else {
        // 아직 빌드하지 않음: 아래에서 빌드.
    }
    const output = fs.mkdtempSync(path.join(os.tmpdir(), 'xamlviewer-sample-build-'));
    try {
        execFileSync('dotnet', ['build', SAMPLE_CSPROJ, '-c', 'Debug', '-o', output, '--nologo', '-v', 'q'],
            { stdio: 'pipe', timeout: BUILD_TIMEOUT_MS });
    } catch (e) {
        const detail = e instanceof Error && 'stdout' in e ? String((e as { stdout: unknown }).stdout) : String(e);
        throw new Error(`샘플 사용자 프로젝트 빌드 실패:\n${detail}`);
    }
    _buildOutput = output;
    return output;
}

/** 임시 사용자 프로젝트: 폴더, XAML 경로. */
export interface UserProject {
    dir: string;
    xamlPath: string;
}

/**
 * `<parent>/<name>` 에 프로젝트 폴더를 만든다: `.csproj`(빈 SDK 프로젝트), 빌드 산출물(withBuild), Main.xaml(xaml 내용).
 * withBuild=false면 bin이 없는 상태("먼저 dotnet build" 시나리오).
 */
export function createUserProject(parent: string, name: string, xaml: string, withBuild = true): UserProject {
    const dir = path.join(parent, name);
    fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(path.join(dir, `${SAMPLE_ASSEMBLY}.csproj`), '<Project Sdk="Microsoft.NET.Sdk"/>');
    if (withBuild) {
        fs.cpSync(buildSampleControls(), path.join(dir, BIN_RELATIVE), { recursive: true });
    } else {
        // 산출물 없음.
    }
    const xamlPath = path.join(dir, 'Main.xaml');
    fs.writeFileSync(xamlPath, xaml);
    return { dir, xamlPath };
}
