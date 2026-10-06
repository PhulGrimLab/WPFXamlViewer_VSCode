using System.IO;
using System.Runtime.CompilerServices;

namespace XamlRenderHost.Tests.Support;

/// <summary>
/// 테스트가 참조하는 소스 트리 경로. 빌드 출력 폴더가 아니라 **소스 위치**를 가리켜야 골든 갱신이 저장소에 반영되므로,
/// 컴파일 시점의 소스 파일 경로([CallerFilePath])에서 계산한다.
/// </summary>
public static class TestPaths
{
    private static string ThisFile([CallerFilePath] string path = "") => path;

    /// <summary>XamlRenderHost.Tests 프로젝트 폴더.</summary>
    public static readonly string ProjectDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFile())!, ".."));

    /// <summary>저장소 루트(host의 상위).</summary>
    public static readonly string RepoRoot = Path.GetFullPath(Path.Combine(ProjectDir, "..", ".."));

    public static readonly string XamlFixtures = Path.Combine(ProjectDir, "Fixtures", "xaml");
    public static readonly string GoldenDir = Path.Combine(ProjectDir, "Fixtures", "golden");

    /// <summary>실패 시 expected/actual/diff 이미지를 남기는 폴더(.gitignore의 TestResults/).</summary>
    public static readonly string DiffDir = Path.Combine(RepoRoot, "TestResults", "render-diff");
}
