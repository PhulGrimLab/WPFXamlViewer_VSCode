using XamlRenderHost.Rendering;
using XamlRenderHost.Tests.Support;

namespace XamlRenderHost.Tests;

/// <summary>
/// App.xaml 리소스 자동 탐색/주입 테스트(H-RD05~RD12). 프로젝트 폴더 구조를 임시 폴더에 만들어 실제 파일 경로 기준으로 렌더한다.
/// 폭 77/55 같은 값은 앱 리소스가 적용됐는지를 PixelWidth로 확인하기 위한 표식이다.
/// </summary>
[TestClass]
public class AppResourcesTests
{
    private const string Ns = RenderTestHelper.PresentationNs;
    private const string XNs = "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";
    private const string SysNs = "xmlns:sys=\"clr-namespace:System;assembly=mscorlib\"";

    private const string CsProj = "<Project Sdk=\"Microsoft.NET.Sdk.WindowsDesktop\"/>";

    private static string AppXaml(string resources, string rootAttributes = "")
        => $"<Application {Ns} {XNs} {SysNs} {rootAttributes}><Application.Resources>{resources}</Application.Resources></Application>";

    private static List<string> Codes(RenderResult r) => (r.Warnings ?? []).Select(w => w.Code).ToList();

    [TestMethod]
    public void RD05_StaticResourceFromAppXaml_ResolvesWhenRootHasNoResources()
    {
        var app = AppXaml("<sys:Double x:Key=\"W\">55</sys:Double>");
        var main = $"<Button {Ns} {XNs} Width=\"{{StaticResource W}}\" Height=\"20\"/>";
        var result = PreprocessorTests.RenderWithFiles(new() { ["App.xaml"] = app, ["Main.xaml"] = main, ["P.csproj"] = CsProj }, "Main.xaml");
        Assert.AreEqual(55, result.PixelWidth);
    }

    [TestMethod]
    public void RD06_ImplicitStyleInExplicitResourceDictionary_IsMergedAndOwnResourcesWin()
    {
        // 앱: Button Width=77 스타일 + 키 W=55. 문서: 자기 W=33(앱보다 우선)을 쓰는 Height.
        var app = AppXaml("<ResourceDictionary><Style TargetType=\"Button\"><Setter Property=\"Width\" Value=\"77\"/></Style><sys:Double x:Key=\"H\">55</sys:Double></ResourceDictionary>");
        var main = $"<StackPanel {Ns} {XNs} {SysNs}><StackPanel.Resources><ResourceDictionary><sys:Double x:Key=\"H\">33</sys:Double></ResourceDictionary></StackPanel.Resources>\n<Button Height=\"{{StaticResource H}}\"/></StackPanel>";
        var result = PreprocessorTests.RenderWithFiles(new() { ["App.xaml"] = app, ["Main.xaml"] = main, ["P.csproj"] = CsProj }, "Main.xaml");
        Assert.AreEqual(77, result.PixelWidth, "앱의 암시적 스타일이 적용되어야 한다");
        Assert.AreEqual(33, result.PixelHeight, "문서 자신의 리소스가 앱 리소스보다 우선해야 한다");
    }

    [TestMethod]
    public void RD07_ExistingMergedDictionaries_GetAppResourcesAdded()
    {
        var app = AppXaml("<sys:Double x:Key=\"W\">55</sys:Double>");
        var main = $"<Button {Ns} {XNs} {SysNs} Width=\"{{StaticResource W}}\" Height=\"{{StaticResource H}}\"><Button.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><ResourceDictionary Source=\"H.xaml\"/></ResourceDictionary.MergedDictionaries></ResourceDictionary></Button.Resources></Button>";
        var h = $"<ResourceDictionary {Ns} {XNs} {SysNs}><sys:Double x:Key=\"H\">21</sys:Double></ResourceDictionary>";
        var result = PreprocessorTests.RenderWithFiles(new() { ["App.xaml"] = app, ["Main.xaml"] = main, ["H.xaml"] = h, ["P.csproj"] = CsProj }, "Main.xaml");
        Assert.AreEqual(55, result.PixelWidth);
        Assert.AreEqual(21, result.PixelHeight);
    }

    [TestMethod]
    public void RD08_ImplicitResourceEntries_AreWrappedAndMerged()
    {
        // 루트의 Resources가 ResourceDictionary 없이 항목만 나열된 형태.
        var app = AppXaml("<sys:Double x:Key=\"W\">55</sys:Double>");
        var main = $"<Button {Ns} {XNs} {SysNs} Width=\"{{StaticResource W}}\" Height=\"{{StaticResource H}}\"><Button.Resources><sys:Double x:Key=\"H\">21</sys:Double></Button.Resources></Button>";
        var result = PreprocessorTests.RenderWithFiles(new() { ["App.xaml"] = app, ["Main.xaml"] = main, ["P.csproj"] = CsProj }, "Main.xaml");
        Assert.AreEqual(55, result.PixelWidth);
        Assert.AreEqual(21, result.PixelHeight);
    }

    [TestMethod]
    public void RD09_AppXamlWithClassStartupAndTheme_DoesNotLeakWarningsAndResolvesRelativeDictionary()
    {
        var app = AppXaml("<ResourceDictionary><ResourceDictionary.MergedDictionaries><ResourceDictionary Source=\"Themes/T.xaml\"/></ResourceDictionary.MergedDictionaries></ResourceDictionary>",
            "x:Class=\"My.App\" StartupUri=\"Main.xaml\" Startup=\"OnStartup\"");
        var theme = $"<ResourceDictionary {Ns} {XNs} {SysNs}><sys:Double x:Key=\"W\">44</sys:Double></ResourceDictionary>";
        var main = $"<Button {Ns} Width=\"{{StaticResource W}}\" Height=\"20\"/>";
        var result = PreprocessorTests.RenderWithFiles(
            new() { ["App.xaml"] = app, ["Themes/T.xaml"] = theme, ["Main.xaml"] = main, ["P.csproj"] = CsProj }, "Main.xaml");
        Assert.AreEqual(44, result.PixelWidth);
        Assert.IsEmpty(Codes(result), "App.xaml의 x:Class/Startup 제거 경고가 사용자 문서 경고로 새면 안 된다");
    }

    [TestMethod]
    public void RD10_AppXamlUnknownUserType_BecomesPlaceholderWarningWithoutLine()
    {
        var app = AppXaml("<ResourceDictionary xmlns:l=\"clr-namespace:Nope;assembly=Nope\"><l:Converter x:Key=\"C\"/><sys:Double x:Key=\"W\">44</sys:Double></ResourceDictionary>");
        var main = $"<Button {Ns} Width=\"{{StaticResource W}}\" Height=\"20\"/>";
        var result = PreprocessorTests.RenderWithFiles(new() { ["App.xaml"] = app, ["Main.xaml"] = main, ["P.csproj"] = CsProj }, "Main.xaml");
        Assert.AreEqual(44, result.PixelWidth);
        var placeholder = (result.Warnings ?? []).Single(w => w.Code == WarningCodes.PlaceholderUsed);
        StringAssert.StartsWith(placeholder.Message, "App.xaml:");
        Assert.IsNull(placeholder.Line, "App.xaml의 줄 번호는 사용자 문서의 줄이 아니므로 싣지 않는다");
    }

    [TestMethod]
    public void RD11_SelfClosingRootAndLineNumbers_ArePreserved()
    {
        var app = AppXaml("<sys:Double x:Key=\"W\">55</sys:Double>");
        // 자기 닫는 루트 + 5행의 오류: 주입 후에도 오류는 5행.
        var main = $"<StackPanel {Ns} {XNs}>\n<Button Width=\"{{StaticResource W}}\"/>\n<Button/>\n<Button/>\n<Button NoSuchProperty=\"1\"/>\n</StackPanel>";
        var ex = Assert.Throws<XamlRenderException>(() =>
            PreprocessorTests.RenderWithFiles(new() { ["App.xaml"] = app, ["Main.xaml"] = main, ["P.csproj"] = CsProj }, "Main.xaml"));
        Assert.AreEqual(5, ex.Line);

        var selfClosing = $"<Button {Ns} {XNs} Width=\"{{StaticResource W}}\" Height=\"20\"/>";
        var ok = PreprocessorTests.RenderWithFiles(new() { ["App.xaml"] = app, ["Main.xaml"] = selfClosing, ["P.csproj"] = CsProj }, "Main.xaml");
        Assert.AreEqual(55, ok.PixelWidth);
    }

    [TestMethod]
    public void RD12_AppXamlOutsideProjectBoundary_IsNotUsed()
    {
        // 부모 폴더의 App.xaml은 다른 프로젝트 것이다: 자식 폴더에 .csproj가 있으면 거기서 탐색을 멈춘다.
        var app = AppXaml("<sys:Double x:Key=\"W\">55</sys:Double>");
        var main = $"<Button {Ns} Width=\"{{StaticResource W}}\" Height=\"20\"/>";
        var ex = Assert.Throws<XamlRenderException>(() =>
            PreprocessorTests.RenderWithFiles(new() { ["App.xaml"] = app, ["Child/Child.csproj"] = CsProj, ["Child/Main.xaml"] = main }, "Child/Main.xaml"));
        Assert.AreEqual(RenderErrorCodes.XamlParse, ex.Code);
    }

    [TestMethod]
    public void RD13_NoAppXaml_LeavesDocumentUntouched()
    {
        var main = $"<Button {Ns} Width=\"40\" Height=\"20\"/>";
        var result = PreprocessorTests.RenderWithFiles(new() { ["Main.xaml"] = main }, "Main.xaml");
        Assert.AreEqual(40, result.PixelWidth);
        Assert.IsEmpty(Codes(result));
    }
}
