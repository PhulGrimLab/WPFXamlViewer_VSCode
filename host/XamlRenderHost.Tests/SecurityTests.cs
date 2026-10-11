using System.IO;
using XamlRenderHost.Projects;
using XamlRenderHost.Rendering;

namespace XamlRenderHost.Tests;

/// <summary>
/// 보안 회귀 테스트: 신뢰 경계(사용자 코드 실행/로컬 파일 읽기)와 외부 입력(어셈블리 이름 등) 검증.
/// 병합 사전의 경로 탈출(RD07/RD08)은 PreprocessorTests에 있다.
/// </summary>
[TestClass]
public class SecurityTests
{
    [TestMethod]
    [DataRow("MyApp", true)]
    [DataRow("My.App-1_2", true)]
    [DataRow("My App", true)]
    [DataRow("", false)]
    [DataRow("   ", false)]
    [DataRow("a\"b", false)]
    [DataRow("a'b", false)]
    [DataRow("a<b", false)]
    [DataRow("a&b", false)]
    [DataRow("a;b", false)]
    [DataRow("a=b", false)]
    [DataRow("a,b", false)]
    [DataRow("a\\b", false)]
    [DataRow("a/b", false)]
    [DataRow("C:evil", false)]
    [DataRow("..", false)]
    [DataRow("a..b", false)]
    [DataRow("line\nbreak", false)]
    public void S01_AssemblyNameValidation(string name, bool expected)
    {
        Assert.AreEqual(expected, ProjectLocator.IsSafeAssemblyName(name));
    }

    [TestMethod]
    public void S02_AssemblyNameTooLong_IsRejected()
    {
        Assert.IsFalse(ProjectLocator.IsSafeAssemblyName(new string('a', 257)));
        Assert.IsTrue(ProjectLocator.IsSafeAssemblyName(new string('a', 256)));
    }

    [TestMethod]
    public void S03_ProjectFileWithUnsafeAssemblyName_FallsBackToFileName()
    {
        // 신뢰된 폴더라도 csproj의 AssemblyName이 XAML 텍스트(속성/URI)에 끼워지므로 위험 문자가 든 이름은 쓰지 않는다.
        var directory = Path.Combine(Path.GetTempPath(), "xamlviewer-sec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "bin", "Debug"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "Safe.csproj"),
                "<Project><PropertyGroup><AssemblyName>Evil\" Foo=\"x</AssemblyName></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(directory, "bin", "Debug", "Safe.dll"), "dll");
            var lookup = ProjectLocator.Find(Path.Combine(directory, "Main.xaml"));
            Assert.AreEqual(ProjectLookupReasons.Found, lookup.Reason);
            Assert.AreEqual("Safe", lookup.Artifact!.AssemblyName, "위험한 AssemblyName은 무시하고 파일 이름(Safe)을 쓴다");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void S04_ProjectFileWithDtd_DoesNotExpandEntitiesOrThrow()
    {
        // 프로젝트 파일의 DTD(엔티티 확장 폭주 등)는 처리하지 않는다: 예외 없이 파일 이름으로 되돌아간다.
        var directory = Path.Combine(Path.GetTempPath(), "xamlviewer-sec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "bin"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "Dtd.csproj"),
                "<?xml version=\"1.0\"?><!DOCTYPE p [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;\">]><Project><PropertyGroup><AssemblyName>&b;</AssemblyName></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(directory, "bin", "Dtd.dll"), "dll");
            var lookup = ProjectLocator.Find(Path.Combine(directory, "Main.xaml"));
            Assert.AreEqual("Dtd", lookup.Artifact?.AssemblyName, "DTD가 든 프로젝트 파일은 읽지 못하므로 파일 이름을 쓴다");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
