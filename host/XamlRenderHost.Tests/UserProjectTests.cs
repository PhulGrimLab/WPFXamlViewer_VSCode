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

    /// <summary>외부 라이브러리의 XML 네임스페이스(XmlnsDefinition)로만 쓰는 형식이라 빌드 산출물이 없으면 파싱 자체가 실패하는 XAML.</summary>
    private static string LibraryXaml => $"<StackPanel {Ns} xmlns:u=\"http://example.com/library/2022/xaml\"><u:Widget/></StackPanel>";

    [TestMethod]
    public void T03b_NoBuildArtifact_ParseFailureCarriesProjectReason()
    {
        using var project = UserProjectFixture.Create(withBuild: false);
        File.WriteAllText(project.XamlPath, LibraryXaml);
        var ex = Assert.Throws<XamlRenderException>(() => StaRunner.Run(() =>
            XamlRenderer.Render(new RenderRequest(LibraryXaml, FilePath: project.XamlPath, AllowProjectAssemblies: true))));
        Assert.AreEqual(ProjectLookupReasons.NoArtifact, ex.ProjectReason, "산출물이 없으면 확장이 먼저 빌드하도록 이유를 알려야 한다");
    }

    [TestMethod]
    public void T03c_BuiltProject_ParseFailureHasNoProjectReason()
    {
        using var project = UserProjectFixture.Create();
        File.WriteAllText(project.XamlPath, LibraryXaml);
        var ex = Assert.Throws<XamlRenderException>(() => StaRunner.Run(() =>
            XamlRenderer.Render(new RenderRequest(LibraryXaml, FilePath: project.XamlPath, AllowProjectAssemblies: true))));
        Assert.IsNull(ex.ProjectReason, "산출물이 있는데 실패한 것은 빌드 문제가 아니다");
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
    public void T06b_DllReplaced_WithRestartAllowed_AsksForHostRestartInsteadOfReloading()
    {
        // 같은 프로세스에서 라이브러리(WPF UI 등)를 두 번 불러오면 의존 속성이 중복 등록되어 렌더가 깨진다. 재시작을 허용한 호출자에게는 다시 불러오지 않고 재시작을 요청한다.
        using var project = UserProjectFixture.Create();
        var xaml = Xaml("<s:RedBox/>");
        File.WriteAllText(project.XamlPath, xaml);
        RenderResult RenderAllowingRestart() => StaRunner.Run(() => XamlRenderer.Render(
            new RenderRequest(xaml, FilePath: project.XamlPath, AllowProjectAssemblies: true, AllowHostRestart: true)));

        Assert.IsTrue(RenderAllowingRestart().Project!.Reloaded, "처음 로드는 재시작 없이 진행한다");
        Assert.IsFalse(RenderAllowingRestart().Project!.Reloaded, "변경이 없으면 재사용한다");

        project.ReplaceBuild(variantB: true);
        File.SetLastWriteTimeUtc(project.DllPath, DateTime.UtcNow.AddMinutes(1));
        var ex = Assert.Throws<XamlRenderException>(() => RenderAllowingRestart());
        Assert.AreEqual(RenderErrorCodes.HostRestartRequired, ex.Code);

        // 재시작을 허용하지 않는 호출자(기존 동작)는 그대로 다시 불러온다.
        Assert.AreEqual(80, Render(project, xaml).PixelWidth);
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
    public void T06c_TempCopy_SkipsFrameworkDuplicates_ButKeepsAppFiles_AndStillRenders()
    {
        // 자체 포함 출력처럼 출력 폴더에 프레임워크 DLL 이름의 파일이 있어도(여기서는 쓸 수 없는 가짜 파일) 복사/로드하지 않고, 앱 DLL은 정상적으로 불러와 그린다.
        using var project = UserProjectFixture.Create();
        var outputDirectory = Path.GetDirectoryName(project.DllPath)!;
        File.WriteAllText(Path.Combine(outputDirectory, "System.Collections.dll"), "not a real assembly");
        var method = typeof(UserAssemblies).GetMethod("IsFrameworkCopyAtTopLevel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        bool Skips(string file) => (bool)method.Invoke(null, [outputDirectory, file])!;

        Assert.IsTrue(Skips(Path.Combine(outputDirectory, "System.Collections.dll")), "호스트 프레임워크에 있는 이름은 복사하지 않는다");
        Assert.IsFalse(Skips(project.DllPath), "앱 DLL은 복사한다");
        Assert.IsFalse(Skips(Path.Combine(outputDirectory, "Some.Library.dll")), "프레임워크에 없는 이름은 복사한다");
        Assert.IsFalse(Skips(Path.Combine(outputDirectory, "sub", "System.Collections.dll")), "하위 폴더의 파일은 건드리지 않는다");

        var result = Render(project, Xaml("<s:RedBox/>"));
        Assert.AreEqual(40, result.PixelWidth, "가짜 프레임워크 파일이 있어도 사용자 컨트롤이 그려져야 한다");
    }

    [TestMethod]
    public void T07a_Locator_WithWorkspaceRoot_NeverLooksOutsideIt()
    {
        // 저장소의 하위 폴더만 열었을 때(워크스페이스 루트 = Views): 위쪽 폴더의 .csproj와 그 bin의 DLL은 루트 밖이라 찾지 않는다.
        using var project = UserProjectFixture.Create();
        var views = Path.Combine(project.Directory, "Views");
        Directory.CreateDirectory(views);
        var xaml = Path.Combine(views, "A.xaml");
        File.WriteAllText(xaml, "<Grid/>");

        Assert.AreEqual(ProjectLookupReasons.Found, ProjectLocator.Find(xaml).Reason, "경계가 없으면 위쪽 프로젝트를 찾는다(기존 동작)");
        Assert.AreEqual(ProjectLookupReasons.Found, ProjectLocator.Find(xaml, project.Directory).Reason, "프로젝트 폴더를 루트로 열면 찾는다");
        Assert.AreEqual(ProjectLookupReasons.NoProject, ProjectLocator.Find(xaml, views).Reason, "루트가 Views뿐이면 위쪽 프로젝트는 루트 밖이다");
        Assert.IsTrue(ProjectLocator.IsUnderRoot(xaml, views));
        Assert.IsFalse(ProjectLocator.IsUnderRoot(project.Directory, views));
        Assert.IsFalse(ProjectLocator.IsUnderRoot(views + "-other", views), "이름이 접두사만 같은 형제 폴더는 안이 아니다");
    }

    [TestMethod]
    public void T07b_Locator_FallsBackToExeWhenNoDll()
    {
        // WPF 앱 프로젝트(WinExe)는 <AssemblyName>.exe만 나온다. DLL이 없을 때만 EXE를 쓰고, 둘 다 있으면 DLL이 우선이다.
        using var project = UserProjectFixture.Create(withBuild: false);
        var output = Path.Combine(project.Directory, "bin", "x64", "Debug");
        Directory.CreateDirectory(output);
        var exe = Path.Combine(output, "SampleControls.exe");
        File.WriteAllText(exe, "exe");

        var lookup = ProjectLocator.Find(project.XamlPath);
        Assert.AreEqual(ProjectLookupReasons.Found, lookup.Reason);
        Assert.AreEqual(exe, lookup.Artifact!.AssemblyPath);

        var dll = Path.Combine(output, "SampleControls.dll");
        File.WriteAllText(dll, "dll");
        File.SetLastWriteTimeUtc(dll, DateTime.UtcNow.AddHours(-2));
        Assert.AreEqual(dll, ProjectLocator.Find(project.XamlPath).Artifact!.AssemblyPath, "DLL이 있으면 더 오래돼도 DLL 우선");
    }

    [TestMethod]
    public void T12_TrustedProject_InternalTypes_AreCreated()
    {
        // 신뢰된 프로젝트의 internal 타입(컨트롤, 리소스 사전의 변환기)도 만들 수 있어야 한다. 기본 XamlReader는 public이 아닌 타입을 못 만든다.
        using var project = UserProjectFixture.Create();
        var xaml = $"<StackPanel {Ns} xmlns:s=\"clr-namespace:SampleControls;assembly=SampleControls\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">"
            + "<StackPanel.Resources><s:InternalConverter x:Key=\"Conv\"/></StackPanel.Resources><s:InternalBox/></StackPanel>";
        var result = Render(project, xaml);
        Assert.AreEqual(1, result.Project!.Tier);
        Assert.AreEqual(33, result.PixelWidth, "internal 컨트롤(폭 33)이 실제로 만들어져야 한다(자리표시자/오류가 아니라)");
        Assert.IsEmpty(Codes(result), "경고 없이 그려져야 한다");
    }

    [TestMethod]
    public void T12b_UntrustedRender_NeverCreatesInternalTypes()
    {
        // 보안: 신뢰하지 않은 렌더(Tier 0)는 같은 프로세스에 로드된 사용자 어셈블리의 internal 타입도 만들면 안 된다(자리표시자).
        using var project = UserProjectFixture.Create();
        var xaml = $"<StackPanel {Ns} xmlns:s=\"clr-namespace:SampleControls;assembly=SampleControls\"><s:InternalBox/></StackPanel>";
        var trusted = Render(project, xaml, allow: true);
        Assert.AreEqual(33, trusted.PixelWidth, "신뢰된 렌더에서는 만들어진다");

        var untrusted = Render(project, xaml, allow: false);
        Assert.AreEqual(0, untrusted.Project!.Tier);
        CollectionAssert.Contains(Codes(untrusted), WarningCodes.PlaceholderUsed, "신뢰하지 않으면 자리표시자여야 한다");
    }

    [TestMethod]
    public void T11_DelegateTypedAttachedProperty_HandlerName_IsRemovedButOtherAttachedValuesKept()
    {
        // 라이브러리(MaterialDesign DialogHost.DialogClosingAttached 등)의 델리게이트 타입 첨부 속성에 핸들러 이름을 쓴 XAML:
        // 코드 비하인드가 없어 이름을 델리게이트로 만들 수 없으니 그 속성만 제거해야 한다. 델리게이트가 아닌 첨부 속성은 그대로 둔다.
        using var project = UserProjectFixture.Create();
        var xaml = Xaml("<Button s:AttachedHandlers.Closing=\"OnClosing\" s:AttachedHandlers.Count=\"3\" Width=\"50\" Height=\"20\"/>");
        var result = Render(project, xaml);
        Assert.AreEqual(1, result.Project!.Tier);
        Assert.AreEqual(50, result.PixelWidth, "버튼이 그려져야 한다(핸들러 속성 때문에 로드가 실패하면 안 된다)");
        Assert.AreEqual(1, Codes(result).Count(c => c == WarningCodes.RemovedEventHandler), "델리게이트 타입 첨부 속성 하나만 제거된다(Count는 유지)");
    }

    [TestMethod]
    public void T09b_UntrustedRender_CannotSeeAppResourcesAppliedByEarlierTrustedRender()
    {
        // 보안(심층 방어): 신뢰된 렌더가 Application.Resources에 올린 리소스(사용자 타입 인스턴스를 담을 수 있다)를
        // 같은 프로세스의 신뢰하지 않은 렌더가 {StaticResource}로 얻으면 안 된다.
        using var project = UserProjectFixture.Create();
        var appXaml = Path.Combine(project.Directory, "App.xaml");
        File.WriteAllText(appXaml,
            $"<Application {Ns} xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:sys=\"clr-namespace:System;assembly=mscorlib\"><Application.Resources><sys:Double x:Key=\"LeakW\">55</sys:Double></Application.Resources></Application>");
        var xaml = $"<StackPanel {Ns} xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Height=\"20\"><Border Width=\"{{StaticResource LeakW}}\" Height=\"20\"/></StackPanel>";

        var trusted = Render(project, xaml, allow: true);
        Assert.AreEqual(55, trusted.PixelWidth, "신뢰된 렌더는 App.xaml 리소스를 본다");

        File.Delete(appXaml); // 문서 안 주입으로는 더 이상 보이지 않는다. 남은 경로는 Application.Resources뿐이다.
        XamlRenderException? failure = null;
        try
        {
            Render(project, xaml, allow: false);
        }
        catch (XamlRenderException ex)
        {
            failure = ex;
        }
        Assert.IsNotNull(failure, "신뢰하지 않은 렌더는 앞서 올린 Application.Resources를 볼 수 없어야 한다(키를 못 찾아 실패)");
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
    public void T10_Tier1_ControlTemplateVisualTreeLoads()
    {
        // Tier 1의 스키마 컨텍스트가 WPF 것이 아니면 ControlTemplate의 VisualTree 설정이 실패한다(FrameworkElementFactory 변환 오류).
        using var project = UserProjectFixture.Create();
        var xaml = $"<Button {Ns} Width=\"50\" Height=\"20\"><Button.Template><ControlTemplate TargetType=\"Button\"><Border Background=\"Red\"/></ControlTemplate></Button.Template></Button>";
        var result = Render(project, xaml);
        Assert.AreEqual(1, result.Project!.Tier);
        Assert.AreEqual(50, result.PixelWidth);
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
