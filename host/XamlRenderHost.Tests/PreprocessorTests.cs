using System.IO;
using XamlRenderHost.Rendering;
using XamlRenderHost.Tests.Support;

namespace XamlRenderHost.Tests;

/// <summary>
/// M4 전처리/루트 종류/병합 사전 테스트(H-X, H-W, H-U, H-RD). 골든(R11~R14)은 RenderGoldenTests가 담당하고,
/// 여기서는 경고 목록, 줄 번호 보존, 파일 기반 동작을 단언한다.
/// </summary>
[TestClass]
public class PreprocessorTests
{
    private const string Ns = RenderTestHelper.PresentationNs;
    private const string XNs = "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(TestPaths.XamlFixtures, name + ".xaml"));

    private static List<string> Codes(RenderResult r) => (r.Warnings ?? []).Select(w => w.Code).ToList();

    [TestMethod]
    public void X01_XClass_IsRemovedWithWarning()
    {
        var result = RenderTestHelper.Render($"<Grid {Ns} {XNs} x:Class=\"A.B\" Width=\"50\" Height=\"20\"/>");
        Assert.AreEqual(50, result.PixelWidth);
        CollectionAssert.Contains(Codes(result), WarningCodes.RemovedClassAttribute);
    }

    [TestMethod]
    public void X02_EventHandlers_AreRemovedAndOtherAttributesKept()
    {
        var result = RenderTestHelper.Render(Fixture("R11_XClassEvents"));
        var codes = Codes(result);
        Assert.AreEqual(1, codes.Count(c => c == WarningCodes.RemovedClassAttribute));
        Assert.AreEqual(3, codes.Count(c => c == WarningCodes.RemovedEventHandler), "Click, Loaded, Checked");
        Assert.AreEqual(200, result.PixelWidth);
        Assert.AreEqual(90, result.PixelHeight);
    }

    [TestMethod]
    public void X02b_PrefixedAttachedEventHandler_IsRemoved()
    {
        // 라이브러리의 첨부 이벤트는 접두사가 붙은 속성으로 쓴다(`materialDesign:DialogHost.DialogClosingAttached="핸들러"`).
        // 접두사 없는 속성만 이벤트로 인식하면 핸들러 이름을 값으로 변환하려다 실패한다. 접두사가 붙어도 WPF 기본 네임스페이스의 첨부 이벤트로 같은 구조를 시험한다.
        var xaml = $"<Button {Ns} xmlns:p=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" p:Mouse.MouseEnter=\"OnEnter\" Width=\"40\" Height=\"20\"/>";
        var result = RenderTestHelper.Render(xaml);
        Assert.AreEqual(40, result.PixelWidth);
        Assert.AreEqual(1, Codes(result).Count(c => c == WarningCodes.RemovedEventHandler), "첨부 이벤트 핸들러 속성 하나가 제거되어야 한다");
    }

    [TestMethod]
    public void X02c_PrefixedAttachedNonEvent_IsKept()
    {
        // 접두사가 붙은 첨부 **속성**(이벤트가 아닌 것)은 제거하면 안 된다: Grid.Row 같은 값은 그대로 적용된다.
        var xaml = $"<Grid {Ns} xmlns:p=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Width=\"50\" Height=\"50\"><Grid.RowDefinitions><RowDefinition Height=\"20\"/><RowDefinition Height=\"30\"/></Grid.RowDefinitions><Border p:Grid.Row=\"1\" Background=\"Red\"/></Grid>";
        var result = RenderTestHelper.Render(xaml);
        Assert.AreEqual(50, result.PixelWidth);
        Assert.AreEqual(0, Codes(result).Count(c => c == WarningCodes.RemovedEventHandler), "이벤트가 아닌 첨부 속성은 제거하지 않는다");
    }

    [TestMethod]
    public void X03_XCodeBlock_IsRemoved()
    {
        var xaml = $"<Grid {Ns} {XNs} Width=\"30\" Height=\"30\">\n<x:Code><![CDATA[ void Foo() {{ }} ]]></x:Code>\n</Grid>";
        var result = RenderTestHelper.Render(xaml);
        CollectionAssert.Contains(Codes(result), WarningCodes.RemovedCodeBlock);
    }

    [TestMethod]
    public void X03b_ApplicationPackSetter_IsRemovedAndRestRenders()
    {
        // Window Icon 같은 pack://application 값은 파싱 중 즉시 로드되어 Setter 초기화가 실패한다 → Setter만 지우고 나머지는 그린다.
        var xaml = $"<Grid {Ns} {XNs} Width=\"40\" Height=\"30\">\n<Grid.Resources><Style TargetType=\"Window\">\n<Setter Property=\"Icon\" Value=\"pack://application:,,,/app.ico\" />\n<Setter Property=\"UseLayoutRounding\" Value=\"True\" />\n</Style></Grid.Resources>\n</Grid>";
        var result = RenderTestHelper.Render(xaml);
        Assert.AreEqual(40, result.PixelWidth);
        Assert.AreEqual(1, Codes(result).Count(c => c == WarningCodes.RemovedApplicationResource));
    }

    [TestMethod]
    public void X04_DesignTimeAttributes_AreIgnoredAndDesignSizeApplies()
    {
        var result = RenderTestHelper.Render(Fixture("R12_DesignTime"));
        Assert.AreEqual(240, result.PixelWidth);
        Assert.AreEqual(70, result.PixelHeight);
        Assert.IsEmpty(Codes(result), "d:/mc: 는 XamlReader가 처리하므로 전처리 경고가 없어야 한다");
    }

    [TestMethod]
    public void X05_LineNumbers_ArePreservedAfterRemovals()
    {
        // 1~4행에 x:Class와 여러 줄에 걸친 이벤트 속성을 제거해도, 5행의 알 수 없는 속성 오류는 5행으로 보고되어야 한다.
        var xaml = $"<Grid {Ns} {XNs}\n x:Class=\"A.B\">\n<Button Click=\"A\"\n   Loaded=\"B\"/>\n<Button NoSuchProperty=\"1\"/>\n</Grid>";
        var ex = RenderTestHelper.RenderExpectingFailure(xaml);
        Assert.AreEqual(RenderErrorCodes.XamlParse, ex.Code);
        Assert.AreEqual(5, ex.Line);
    }

    [TestMethod]
    public void X06_MalformedXml_IsReportedNotMasked()
    {
        var ex = RenderTestHelper.RenderExpectingFailure($"<Grid {Ns} {XNs} x:Class=\"A\">\n<Button>\n</Grid>");
        Assert.AreEqual(RenderErrorCodes.XmlMalformed, ex.Code);
        Assert.AreEqual(3, ex.Line);
    }

    [TestMethod]
    public void W01_WindowRoot_RendersContentAtWindowSize()
    {
        var result = RenderTestHelper.Render(Fixture("R13_WindowRoot"));
        Assert.AreEqual(260, result.PixelWidth);
        Assert.AreEqual(120, result.PixelHeight);
    }

    [TestMethod]
    [DataRow("UserControl")]
    [DataRow("Page")]
    [DataRow("Grid")]
    public void W02_OtherRootKinds_Render(string root)
    {
        var result = RenderTestHelper.Render($"<{root} {Ns} Width=\"70\" Height=\"40\"/>");
        Assert.AreEqual(70, result.PixelWidth);
        Assert.AreEqual(40, result.PixelHeight);
    }

    [TestMethod]
    public void U01_UnknownUserTypes_BecomePlaceholdersWithWarnings()
    {
        var result = RenderTestHelper.Render(Fixture("R14_UserTypePlaceholder"));
        var placeholders = (result.Warnings ?? []).Where(w => w.Code == WarningCodes.PlaceholderUsed).ToList();
        Assert.HasCount(2, placeholders);
        StringAssert.Contains(placeholders[0].Message, "local:FancyGauge");
        Assert.AreEqual(6, placeholders[0].Line);
        Assert.AreEqual(220, result.PixelWidth);
    }

    private const string UnknownNs = "xmlns:l=\"clr-namespace:Nope;assembly=Nope\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    [TestMethod]
    public void U03_Tier0_StylesAndTemplatesForUnknownTypes_AreStoodIn()
    {
        // 사용자 타입을 못 만드는 Tier 0: {x:Type l:Foo}/TargetType="l:Foo" 정의 때문에 문서 전체가 실패하면 안 된다.
        var xaml = $"<StackPanel {Ns} {UnknownNs} Width=\"60\" Height=\"40\">\n"
            + "<StackPanel.Resources>\n"
            + "<Style x:Key=\"K\" TargetType=\"{x:Type l:Foo}\"><Setter Property=\"Orientation\" Value=\"Horizontal\"/></Style>\n"
            + "<Style x:Key=\"D\" TargetType=\"{x:Type l:Foo}\" BasedOn=\"{StaticResource K}\"/>\n"
            + "<Style TargetType=\"{x:Type l:Foo}\"><Setter Property=\"IconSize\" Value=\"3\"/></Style>\n"
            + "<DataTemplate DataType=\"{x:Type l:Foo}\"><TextBlock Text=\"x\"/></DataTemplate>\n"
            + "</StackPanel.Resources>\n"
            + "<Button Tag=\"{x:Type l:Foo}\" Style=\"{StaticResource K}\">\n<Button.Template><ControlTemplate TargetType=\"{x:Type l:Foo}\"><Border/></ControlTemplate></Button.Template>\n</Button>\n"
            + "</StackPanel>";
        var result = RenderTestHelper.Render(xaml);
        Assert.AreEqual(60, result.PixelWidth);
        Assert.IsGreaterThanOrEqualTo(4, Codes(result).Count(c => c == WarningCodes.PlaceholderUsed));
    }

    [TestMethod]
    public void U04_Tier0_TypeStandIn_KeepsOriginalLineForLaterErrors()
    {
        var xaml = $"<StackPanel {Ns} {UnknownNs}>\n<StackPanel.Resources>\n<Style x:Key=\"K\" TargetType=\"{{x:Type l:Foo}}\"/>\n</StackPanel.Resources>\n<Button NoSuchProperty=\"1\"/>\n</StackPanel>";
        var ex = RenderTestHelper.RenderExpectingFailure(xaml);
        Assert.AreEqual(5, ex.Line);
    }

    [TestMethod]
    public void U02_ErrorsAfterPlaceholder_KeepOriginalLine()
    {
        var xaml = $"<StackPanel {Ns} xmlns:l=\"clr-namespace:Nope;assembly=Nope\">\n<l:Thing>\n<Button/>\n</l:Thing>\n<Button NoSuchProperty=\"1\"/>\n</StackPanel>";
        var ex = RenderTestHelper.RenderExpectingFailure(xaml);
        Assert.AreEqual(5, ex.Line);
    }

    /// <summary>임시 폴더에 파일들을 만들고 mainFile을 렌더한다. 끝나면 폴더를 지운다.</summary>
    internal static RenderResult RenderWithFiles(Dictionary<string, string> files, string mainFile)
    {
        var dir = Path.Combine(Path.GetTempPath(), "xamlviewer-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var (name, content) in files)
            {
                var path = Path.Combine(dir, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content);
            }
            var main = Path.Combine(dir, mainFile);
            return StaRunner.Run(() => XamlRenderer.Render(new RenderRequest(files[mainFile], FilePath: main)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string ButtonWith(string sources, string attrs = "Width=\"40\" Height=\"20\"")
        => $"<Button {Ns} {attrs}><Button.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries>\n{sources}\n</ResourceDictionary.MergedDictionaries></ResourceDictionary></Button.Resources></Button>";

    private static readonly string WideButtonDictionary =
        $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<ResourceDictionary {Ns}><Style TargetType=\"Button\"><Setter Property=\"Width\" Value=\"77\"/></Style></ResourceDictionary>";

    [TestMethod]
    public void RD01_RelativeMergedDictionary_IsInlined()
    {
        var main = ButtonWith("<ResourceDictionary Source=\"Styles/Wide.xaml\"/>", "Height=\"20\"");
        var result = RenderWithFiles(new() { ["Main.xaml"] = main, ["Styles/Wide.xaml"] = WideButtonDictionary }, "Main.xaml");
        Assert.AreEqual(77, result.PixelWidth, "병합한 사전의 암시적 스타일이 적용되어야 한다");
        Assert.IsEmpty(Codes(result));
    }

    [TestMethod]
    public void RD02_MissingDictionary_WarnsAndStillRenders()
    {
        var result = RenderWithFiles(new() { ["Main.xaml"] = ButtonWith("<ResourceDictionary Source=\"Nope.xaml\"/>") }, "Main.xaml");
        Assert.AreEqual(40, result.PixelWidth);
        CollectionAssert.Contains(Codes(result), WarningCodes.DictionaryUnavailable);
    }

    [TestMethod]
    public void RD03_CircularMerge_IsBlockedWithWarning()
    {
        string Dict(string other) => $"<ResourceDictionary {Ns}><ResourceDictionary.MergedDictionaries><ResourceDictionary Source=\"{other}\"/></ResourceDictionary.MergedDictionaries></ResourceDictionary>";
        var result = RenderWithFiles(new()
        {
            ["Main.xaml"] = ButtonWith("<ResourceDictionary Source=\"A.xaml\"/>"),
            ["A.xaml"] = Dict("B.xaml"),
            ["B.xaml"] = Dict("A.xaml"),
        }, "Main.xaml");
        Assert.AreEqual(40, result.PixelWidth);
        CollectionAssert.Contains(Codes(result), WarningCodes.DictionaryUnavailable);
    }

    private static string OutsideDictionary => $"<ResourceDictionary {Ns} xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><SolidColorBrush x:Key=\"Leak\" Color=\"Red\"/></ResourceDictionary>";

    [TestMethod]
    public void RD07_AbsolutePathSource_IsNeverRead()
    {
        // 보안: Source에 절대 경로를 쓰면 Path.Combine이 기준 폴더를 무시한다. 문서 밖(다른 폴더)의 파일을 읽어 인라인하면 안 된다.
        var outside = Path.Combine(Path.GetTempPath(), "xamlviewer-outside-" + Guid.NewGuid().ToString("N") + ".xaml");
        File.WriteAllText(outside, OutsideDictionary);
        try
        {
            var result = RenderWithFiles(new() { ["Main.xaml"] = ButtonWith($"<ResourceDictionary Source=\"{outside}\"/>") }, "Main.xaml");
            CollectionAssert.Contains(Codes(result), WarningCodes.DictionaryUnavailable, "절대 경로 사전은 읽지 않고 '찾을 수 없음'으로 처리해야 한다");
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [TestMethod]
    public void RD08_DotDotEscapingTheProject_IsNeverRead()
    {
        // 보안: `..\`로 프로젝트(.csproj가 있는 폴더) 밖의 파일을 읽으면 안 된다. 같은 프로젝트 안의 `..`는 허용한다.
        var main = ButtonWith("<ResourceDictionary Source=\"../outside.xaml\"/>\n<ResourceDictionary Source=\"../Shared/inside.xaml\"/>", "Height=\"20\""); // Width는 비워 둔다(사전의 스타일이 77을 준다)
        var result = RenderWithFiles(new()
        {
            ["Proj/Proj.csproj"] = "<Project/>",
            ["Proj/Views/Main.xaml"] = main,
            ["Proj/Shared/inside.xaml"] = WideButtonDictionary,
            ["outside.xaml"] = OutsideDictionary,
        }, "Proj/Views/Main.xaml");
        Assert.AreEqual(1, Codes(result).Count(c => c == WarningCodes.DictionaryUnavailable), "프로젝트 밖(../outside.xaml)만 거부되고 프로젝트 안(../Shared)은 읽혀야 한다");
        Assert.AreEqual(77, result.PixelWidth, "프로젝트 안의 사전은 인라인되어 스타일이 적용되어야 한다");
    }

    [TestMethod]
    public void RD09_JunctionInsideProjectPointingOutside_IsNeverRead()
    {
        // 보안: 프로젝트 안에 있는 정션(디렉터리 링크)이 밖을 가리켜도 그 경로로 밖의 파일을 읽으면 안 된다(경계 검사는 문자열 경로 기준이라 링크를 별도로 막는다).
        var baseDir = Path.Combine(Path.GetTempPath(), "xamlviewer-junction-" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(baseDir, "Proj");
        var outsideDir = Path.Combine(baseDir, "Outside");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(outsideDir);
        File.WriteAllText(Path.Combine(projectDir, "Proj.csproj"), "<Project/>");
        File.WriteAllText(Path.Combine(outsideDir, "secret.xaml"), OutsideDictionary);
        var main = Path.Combine(projectDir, "Main.xaml");
        File.WriteAllText(main, ButtonWith("<ResourceDictionary Source=\"Link/secret.xaml\"/>"));
        var junction = Path.Combine(projectDir, "Link");
        try
        {
            using (var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{outsideDir}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!)
            {
                mklink.StandardOutput.ReadToEnd();
                mklink.WaitForExit();
            }
            if (!Directory.Exists(junction))
            {
                Assert.Inconclusive("이 환경에서는 정션을 만들 수 없어 시험하지 못했다.");
            }

            var result = StaRunner.Run(() => XamlRenderer.Render(new RenderRequest(File.ReadAllText(main), FilePath: main)));
            CollectionAssert.Contains(Codes(result), WarningCodes.DictionaryUnavailable, "링크를 지나는 경로의 사전은 읽지 않아야 한다");
        }
        finally
        {
            if (Directory.Exists(junction))
            {
                Directory.Delete(junction); // 링크만 지운다(재귀 삭제하면 대상 폴더까지 지워질 수 있다).
            }
            Directory.Delete(baseDir, recursive: true);
        }
    }

    private static string BadSetterStyle => "<Style TargetType=\"Button\"><Setter Property=\"Width\" Value=\"abc\"/></Style>";

    [TestMethod]
    public void RD05_FailingElementInMergedDictionary_IsSkippedWithWarning()
    {
        // 병합 사전(다른 파일)의 Setter 하나가 읽히지 않아도 문서 전체를 못 그리면 안 된다: 그 요소만 건너뛰고 경고한다.
        var dictionary = $"<ResourceDictionary {Ns}>{BadSetterStyle}</ResourceDictionary>";
        var result = RenderWithFiles(new() { ["Main.xaml"] = ButtonWith("<ResourceDictionary Source=\"Bad.xaml\"/>"), ["Bad.xaml"] = dictionary }, "Main.xaml");
        Assert.AreEqual(40, result.PixelWidth);
        CollectionAssert.Contains(Codes(result), WarningCodes.InlinedElementSkipped);
    }

    [TestMethod]
    public void RD06_SameFailureInDocumentBody_IsStillAnError()
    {
        // 사용자가 편집 중인 문서 본문의 오류는 숨기지 않고 보고한다.
        var xaml = $"<Button {Ns} Width=\"40\" Height=\"20\"><Button.Resources>{BadSetterStyle}</Button.Resources></Button>";
        var ex = RenderTestHelper.RenderExpectingFailure(xaml);
        Assert.AreEqual(RenderErrorCodes.XamlParse, ex.Code);
    }

    [TestMethod]
    public void RD04_NetworkSource_IsNotFetched()
    {
        var result = RenderWithFiles(new() { ["Main.xaml"] = ButtonWith("<ResourceDictionary Source=\"http://example.invalid/x.xaml\"/>") }, "Main.xaml");
        CollectionAssert.Contains(Codes(result), WarningCodes.DictionaryUnavailable);
    }
}
