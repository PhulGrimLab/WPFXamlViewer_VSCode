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
