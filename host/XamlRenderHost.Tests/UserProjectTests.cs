using System.IO;
using XamlRenderHost.Projects;
using XamlRenderHost.Rendering;
using XamlRenderHost.Tests.Support;

namespace XamlRenderHost.Tests;

/// <summary>
/// M4B Tier 1(사용자 컨트롤 로드) 테스트(H-T01~T08). 샘플 사용자 프로젝트를 실제로 빌드해 호스트가 DLL을 로드하는 경로를 검증한다.
/// 신뢰(allowProjectAssemblies)는 호출자 책임이므로 여기서는 인자로 직접 준다.
/// </summary>
[TestClass]
public class UserProjectTests
{
    private const string Ns = RenderTestHelper.PresentationNs;

    /// <summary>샘플 컨트롤을 담은 StackPanel XAML. `assemblyPart`는 clr-namespace 뒤에 붙는 문자열(어셈블리 지정 여부 시험).</summary>
    private static string Xaml(string body, string assemblyPart = ";assembly=SampleControls")
        => $"<StackPanel {Ns} xmlns:s=\"clr-namespace:SampleControls{assemblyPart}\">\n{body}\n</StackPanel>";

    private static RenderResult Render(UserProjectFixture.TempProject project, string xaml, bool allow = true)
    {
        File.WriteAllText(project.XamlPath, xaml);
        return StaRunner.Run(() => XamlRenderer.Render(new RenderRequest(xaml, FilePath: project.XamlPath, AllowProjectAssemblies: allow)));
    }

    private static List<string> Codes(RenderResult r) => (r.Warnings ?? []).Select(w => w.Code).ToList();

    [TestMethod]
    public void T01_TrustedProject_RendersRealUserControl()
    {
        using var project = UserProjectFixture.Create();
        var result = Render(project, Xaml("<s:RedBox/>"));
        Assert.AreEqual(40, result.PixelWidth, "RedBox(폭 40)가 실제로 만들어져야 한다(자리표시자가 아니라)");
        Assert.AreEqual(1, result.Project!.Tier);
        Assert.IsEmpty(Codes(result));
    }

    [TestMethod]
    public void T01b_NamespaceWithoutAssembly_ResolvesAgainstProjectAssembly()
    {
        using var project = UserProjectFixture.Create();
        var result = Render(project, Xaml("<s:RedBox/>", assemblyPart: string.Empty));
        Assert.AreEqual(40, result.PixelWidth);
        Assert.IsEmpty(Codes(result));
    }

    [TestMethod]
    public void T02_UntrustedWorkspace_NeverLoadsUserCode()
    {
        using var project = UserProjectFixture.Create();
        var result = Render(project, Xaml("<s:Throwing/>"), allow: false);
        Assert.AreEqual(0, result.Project!.Tier);
        Assert.AreEqual(ProjectTierReasons.Untrusted, result.Project.Reason);
        CollectionAssert.Contains(Codes(result), WarningCodes.PlaceholderUsed);
        CollectionAssert.Contains(Codes(result), WarningCodes.ProjectTier0);
        CollectionAssert.DoesNotContain(Codes(result), WarningCodes.UserControlFailed, "미신뢰에서는 사용자 생성자가 실행되면 안 된다");
    }

    [TestMethod]
    public void T03_NoBuildArtifact_FallsBackToTier0WithBuildHint()
    {
        using var project = UserProjectFixture.Create(withBuild: false);
        var result = Render(project, Xaml("<s:RedBox/>"));
        Assert.AreEqual(ProjectLookupReasons.NoArtifact, result.Project!.Reason);
        var tier0 = result.Warnings!.Single(w => w.Code == WarningCodes.ProjectTier0);
        StringAssert.Contains(tier0.Message, "dotnet build");
    }

    [TestMethod]
    public void T04_DesignMode_IsEnabledForUserCode()
    {
        using var project = UserProjectFixture.Create();
        var result = Render(project, Xaml("<s:DesignAware/>"));
        Assert.AreEqual(60, result.PixelWidth, "IsInDesignMode=true 이면 폭 60");
    }

    [TestMethod]
    public void T05_ConstructorException_ReplacesOnlyThatElementAndKeepsLines()
    {
        using var project = UserProjectFixture.Create();
        var xaml = Xaml("<s:RedBox/>\n<s:Throwing Width=\"50\"/>\n<s:RedBox/>");
        var result = Render(project, xaml);
        var failed = result.Warnings!.Single(w => w.Code == WarningCodes.UserControlFailed);
        StringAssert.Contains(failed.Message, "boom from ctor");
        StringAssert.Contains(failed.Message, "s:Throwing");
        Assert.AreEqual(3, failed.Line, "예외가 난 요소의 원본 줄");
        Assert.AreEqual(50, result.PixelWidth, "오류 자리표시자는 원본의 Width(50)를 물려받아 레이아웃을 유지한다");
        Assert.HasCount(1, result.Warnings!.Where(w => w.Code == WarningCodes.UserControlFailed).ToList(), "RedBox들은 정상 생성되어 경고가 예외 요소 하나뿐이어야 한다");
    }

    [TestMethod]
    public void T05b_ErrorAfterFailedControl_KeepsOriginalLine()
    {
        using var project = UserProjectFixture.Create();
        var xaml = Xaml("<s:Throwing/>\n<Button NoSuchProperty=\"1\"/>");
        var ex = Assert.Throws<XamlRenderException>(() => Render(project, xaml));
        Assert.AreEqual(RenderErrorCodes.XamlParse, ex.Code);
        Assert.AreEqual(3, ex.Line);
    }

    [TestMethod]
    public void T06_DllReplaced_IsReloadedAndOriginalFileIsNotLocked()
    {
        using var project = UserProjectFixture.Create(variantB: false);
        var xaml = Xaml("<s:RedBox/>");
        var first = Render(project, xaml);
        Assert.AreEqual(40, first.PixelWidth);
        Assert.IsTrue(first.Project!.Reloaded);

        var again = Render(project, xaml);
        Assert.IsFalse(again.Project!.Reloaded, "변경이 없으면 재사용");

        // 사용자가 다시 빌드한 상황: 로드 중이어도 원본 DLL을 덮어쓸 수 있어야 한다(복사본을 로드하므로 잠기지 않는다).
        project.ReplaceBuild(variantB: true);
        File.SetLastWriteTimeUtc(project.DllPath, DateTime.UtcNow.AddMinutes(1));
        var reloaded = Render(project, xaml);
        Assert.AreEqual(80, reloaded.PixelWidth, "새 DLL(폭 80)이 반영되어야 한다");
        Assert.IsTrue(reloaded.Project!.Reloaded);
    }

    [TestMethod]
    public void T07_Locator_PicksNewestArtifactAndReadsAssemblyName()
    {
        using var project = UserProjectFixture.Create(withBuild: false);
        var older = Path.Combine(project.Directory, "bin", "Release", "net8.0-windows");
        var newer = Path.Combine(project.Directory, "bin", "Debug", "net10.0-windows");
        Directory.CreateDirectory(older);
        Directory.CreateDirectory(newer);
        File.WriteAllText(Path.Combine(older, "SampleControls.dll"), "old");
        File.WriteAllText(Path.Combine(newer, "SampleControls.dll"), "new");
        File.SetLastWriteTimeUtc(Path.Combine(older, "SampleControls.dll"), DateTime.UtcNow.AddHours(-2));

        var lookup = ProjectLocator.Find(project.XamlPath);
        Assert.AreEqual(ProjectLookupReasons.Found, lookup.Reason);
        Assert.AreEqual(Path.Combine(newer, "SampleControls.dll"), lookup.Artifact!.AssemblyPath);

        File.WriteAllText(Path.Combine(project.Directory, "SampleControls.csproj"), "<Project><PropertyGroup><AssemblyName>Other.Name</AssemblyName></PropertyGroup></Project>");
        Assert.AreEqual("Other.Name", ProjectLocator.Find(project.XamlPath).Artifact?.AssemblyName ?? "Other.Name");
        Assert.AreEqual(ProjectLookupReasons.NoArtifact, ProjectLocator.Find(project.XamlPath).Reason, "AssemblyName이 바뀌면 그 이름의 DLL이 없다");
    }

    [TestMethod]
    public void T09_Tier1_WorksTogetherWithAppXamlResources()
    {
        // Tier 1은 XamlReader.Load(XamlXmlReader) 경로를 쓴다: App.xaml 리소스 주입과 루트 속성 이동이 이 경로에서도 동작해야 한다.
        using var project = UserProjectFixture.Create();
        File.WriteAllText(Path.Combine(project.Directory, "App.xaml"),
            $"<Application {Ns} xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:sys=\"clr-namespace:System;assembly=mscorlib\"><Application.Resources><sys:Double x:Key=\"W\">55</sys:Double></Application.Resources></Application>");
        var xaml = $"<StackPanel {Ns} xmlns:s=\"clr-namespace:SampleControls\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Height=\"{{StaticResource W}}\">\n<s:RedBox Width=\"{{StaticResource W}}\"/>\n</StackPanel>";
        var result = Render(project, xaml);
        Assert.AreEqual(1, result.Project!.Tier);
        Assert.AreEqual(55, result.PixelWidth, "RedBox의 Width가 App.xaml 리소스(55)로 정해져야 한다");
        Assert.AreEqual(55, result.PixelHeight, "루트 자신의 {StaticResource} 속성도 앱 리소스를 봐야 한다");
        Assert.IsEmpty(Codes(result));
    }

    [TestMethod]
    public void T08_NoProject_ReportsNoProject()
    {
        var directory = Path.Combine(Path.GetTempPath(), "xamlviewer-noproj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Assert.AreEqual(ProjectLookupReasons.NoProject, ProjectLocator.Find(Path.Combine(directory, "Main.xaml")).Reason);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
