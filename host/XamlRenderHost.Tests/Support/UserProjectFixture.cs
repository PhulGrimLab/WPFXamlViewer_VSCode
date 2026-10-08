using System.Diagnostics;
using System.IO;

namespace XamlRenderHost.Tests.Support;

/// <summary>
/// Tier 1 테스트용 사용자 프로젝트(Fixtures/projects/SampleControls)를 `dotnet build`로 한 번 빌드해 두고,
/// 테스트마다 임시 폴더에 "프로젝트 폴더 + bin 산출물" 구조를 만들어 준다(원본을 건드리지 않고 DLL 교체도 시험할 수 있게).
/// 빌드는 변형 A(기본)와 B(`VARIANT_B`, RedBox 폭 80) 두 번이며 프로세스 안에서 한 번만 수행한다(스레드 안전).
/// </summary>
public static class UserProjectFixture
{
    public const string AssemblyName = "SampleControls";

    /// <summary>산출물이 놓이는 하위 경로(실제 프로젝트의 bin/Debug/TFM 구조를 흉내 낸다).</summary>
    public const string BinRelativePath = "bin/Debug/net10.0-windows";

    private static readonly Lazy<string> VariantA = new(() => Build("A", null), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<string> VariantB = new(() => Build("B", "VARIANT_B"), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>임시 프로젝트 폴더 하나. Dispose하면 폴더를 지운다.</summary>
    public sealed class TempProject : IDisposable
    {
        public required string Directory { get; init; }
        public string XamlPath => Path.Combine(Directory, "Main.xaml");
        public string BinDirectory => Path.Combine(Directory, BinRelativePath);
        public string DllPath => Path.Combine(BinDirectory, AssemblyName + ".dll");

        /// <summary>bin 산출물을 지정한 변형의 것으로 덮어쓴다(사용자가 다시 빌드한 상황).</summary>
        public void ReplaceBuild(bool variantB)
        {
            CopyDirectory(variantB ? VariantB.Value : VariantA.Value, BinDirectory);
        }

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
                // 로드된 컨텍스트가 아직 파일을 잡고 있을 수 있다(원본이 아니라 임시 복사본이 로드되므로 보통 발생하지 않는다). 임시 폴더라 무시.
            }
        }
    }

    /// <summary>
    /// 임시 프로젝트를 만든다. withBuild=false면 .csproj만 있고 bin이 없는 상태("먼저 dotnet build" 시나리오).
    /// </summary>
    public static TempProject Create(bool withBuild = true, bool variantB = false)
    {
        var directory = Path.Combine(Path.GetTempPath(), "xamlviewer-proj-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, AssemblyName + ".csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"/>");
        var project = new TempProject { Directory = directory };
        if (withBuild)
        {
            project.ReplaceBuild(variantB);
        }
        else
        {
            // 산출물 없음.
        }
        return project;
    }

    /// <summary>샘플 프로젝트를 outputDir에 빌드한다. 실패하면 빌드 출력과 함께 예외(테스트 환경 문제를 숨기지 않는다).</summary>
    private static string Build(string label, string? define)
    {
        var projectFile = Path.Combine(TestPaths.ProjectDir, "Fixtures", "projects", "SampleControls", "SampleControls.csproj");
        var output = Path.Combine(Path.GetTempPath(), "xamlviewer-fixture-" + label + "-" + Guid.NewGuid().ToString("N"));
        var args = $"build \"{projectFile}\" -c Debug -o \"{output}\" --nologo -v q" + (define == null ? string.Empty : $" -p:DefineConstants=\"{define}\"");
        var info = new ProcessStartInfo("dotnet", args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var process = Process.Start(info)!;
        var text = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 || !File.Exists(Path.Combine(output, AssemblyName + ".dll")))
        {
            throw new InvalidOperationException($"샘플 사용자 프로젝트 빌드 실패(변형 {label}):\n{text}");
        }
        else
        {
            return output;
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        System.IO.Directory.CreateDirectory(target);
        foreach (var file in System.IO.Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }
}
