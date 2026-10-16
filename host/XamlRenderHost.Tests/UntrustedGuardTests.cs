using System.IO;
using System.Text.Json.Nodes;
using XamlRenderHost.Rendering;
using XamlRenderHost.Tests.Support;

namespace XamlRenderHost.Tests;

/// <summary>
/// 보안 리뷰 후속(S-G): 신뢰하지 않은 폴더(Tier 0)의 XAML이 선언만으로 코드를 실행하거나 로컬 파일을 읽지 못하게 하는 검사와, 악의적 입력이 호스트를 죽이지 못하는지 확인한다.
/// </summary>
[TestClass]
public class UntrustedGuardTests
{
    private const string Ns = RenderTestHelper.PresentationNs + " xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    private static XamlRenderException RenderBlocked(string xaml)
    {
        var ex = Assert.Throws<XamlRenderException>(() => StaRunner.Run(() => XamlRenderer.Render(new RenderRequest(xaml))));
        Assert.AreEqual(RenderErrorCodes.UntrustedBlocked, ex.Code, ex.Message);
        return ex;
    }

    [TestMethod]
    public void G01_ObjectDataProvider_IsBlocked_AndNeverRuns()
    {
        var marker = Path.Combine(Path.GetTempPath(), "xamlviewer-guard-marker-" + Guid.NewGuid().ToString("N") + ".txt").Replace("\\", "/");
        var xaml = $"<Grid {Ns} xmlns:io=\"clr-namespace:System.IO;assembly=System.Runtime\" xmlns:s=\"clr-namespace:System;assembly=System.Runtime\">"
            + "<Grid.Resources><ObjectDataProvider x:Key=\"o\" ObjectType=\"{x:Type io:File}\" MethodName=\"WriteAllText\"><ObjectDataProvider.MethodParameters>"
            + $"<s:String>{marker}</s:String><s:String>pwned</s:String></ObjectDataProvider.MethodParameters></ObjectDataProvider></Grid.Resources>"
            + "<TextBlock Text=\"{Binding Source={StaticResource o}}\"/></Grid>";
        try
        {
            var ex = RenderBlocked(xaml);
            StringAssert.Contains(ex.Message, "ObjectDataProvider");
            Assert.IsTrue(ex.Line.HasValue, "어느 줄인지 알려야 한다");
            Assert.IsFalse(File.Exists(marker), "차단되어야 하므로 파일이 만들어지면 안 된다");
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [TestMethod]
    public void G02_XStatic_OfNonWpfType_IsBlocked_ButWpfTypeWorks()
    {
        RenderBlocked($"<TextBlock {Ns} xmlns:s=\"clr-namespace:System;assembly=System.Runtime\" Text=\"{{x:Static s:Environment.UserName}}\"/>");

        var ok = RenderTestHelper.Render($"<Border {Ns} Width=\"20\" Height=\"20\" Background=\"{{x:Static SystemColors.ControlBrush}}\"/>");
        Assert.AreEqual(20, ok.PixelWidth, "WPF 형식의 {x:Static}은 계속 동작해야 한다");
    }

    [TestMethod]
    public void G03_NonWpfTypes_AreBlocked()
    {
        RenderBlocked($"<StackPanel {Ns} xmlns:d=\"clr-namespace:System.Diagnostics;assembly=System.Diagnostics.Process\"><d:Process/></StackPanel>");
    }

    [TestMethod]
    public void G04_LocalOrNetworkSources_AreBlocked()
    {
        RenderBlocked($"<Image {Ns} Source=\"file:///C:/Windows/Web/Wallpaper/Windows/img0.jpg\"/>");
        RenderBlocked($"<Image {Ns} Source=\"C:/Windows/Web/Wallpaper/Windows/img0.jpg\"/>");
        RenderBlocked($"<Image {Ns} Source=\"//127.0.0.1/share/a.png\"/>");
        RenderBlocked($"<Image {Ns} Source=\"https://example.invalid/a.png\"/>");
    }

    [TestMethod]
    public void G05_CommonWpfXaml_StillRenders()
    {
        var xaml = $"<Grid {Ns} Width=\"120\" Height=\"60\"><Grid.Resources><Style x:Key=\"S\" TargetType=\"{{x:Type Button}}\"><Setter Property=\"Margin\" Value=\"4\"/></Style></Grid.Resources>"
            + "<Button Style=\"{StaticResource S}\" Content=\"OK\"/><TextBlock Text=\"{Binding Path=Name}\" FontFamily=\"Segoe UI\"/></Grid>";
        Assert.AreEqual(120, RenderTestHelper.Render(xaml).PixelWidth);
    }

    [TestMethod]
    public void G06_CarriageReturnOnlyNewlines_DoNotCrash()
    {
        var xaml = $"<StackPanel {Ns} x:Class=\"A.B\">\r<Button Content=\"a\"\rClick=\"Foo\"/>\r<Button Content=\"b\"/></StackPanel>";
        var result = RenderTestHelper.Render(xaml);
        Assert.IsGreaterThan(0, result.PixelWidth);
    }

    [TestMethod]
    public void G07_HostSurvivesMalformedRequests()
    {
        using var host = new HostProcess();
        // 같은 키가 두 번 있는 JSON: 이전에는 처리되지 않은 예외로 호스트가 종료되었다.
        host.SendLine("{\"id\":1,\"id\":2,\"method\":\"ping\"}");
        var bad = host.ReadResponse();
        Assert.IsFalse((bool)bad["ok"]!);
        Assert.AreEqual("InvalidRequest", (string)bad["error"]!["code"]!);

        host.Send(3, "ping");
        Assert.IsTrue((bool)host.ReadResponse()["ok"]!, "이후 요청이 정상 처리되어야 한다(호스트가 살아 있다)");
    }

    [TestMethod]
    public void G08_InvalidDpiOrSize_IsRejectedBeforeRendering()
    {
        using var host = new HostProcess();
        foreach (var (dpi, width) in new[] { (0.0, 10.0), (-5.0, 10.0), (96.0, -3.0), (100000.0, 10.0) })
        {
            host.Send(1, "render", new JsonObject { ["xaml"] = $"<Rectangle {RenderTestHelper.PresentationNs} Fill=\"Red\"/>", ["dpi"] = dpi, ["width"] = width });
            var r = host.ReadResponse();
            Assert.IsFalse((bool)r["ok"]!, $"dpi={dpi} width={width}");
            Assert.AreEqual("InvalidParams", (string)r["error"]!["code"]!);
        }
    }

    [TestMethod]
    public void G09_MergedDictionaryThatIsNotXml_BecomesAWarning_NotADocumentError()
    {
        var directory = Path.Combine(Path.GetTempPath(), "xamlviewer-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "secret.txt"), "TOP-SECRET-CONTENT is not xml");
            var documentPath = Path.Combine(directory, "Main.xaml");
            var xaml = $"<Grid {Ns}><Grid.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><ResourceDictionary Source=\"secret.txt\"/></ResourceDictionary.MergedDictionaries></ResourceDictionary></Grid.Resources><Border Width=\"10\" Height=\"10\"/></Grid>";
            File.WriteAllText(documentPath, xaml);
            var result = StaRunner.Run(() => XamlRenderer.Render(new RenderRequest(xaml, FilePath: documentPath)));
            var warning = result.Warnings!.Single(w => w.Code == WarningCodes.DictionaryUnavailable);
            StringAssert.Contains(warning.Message, "secret.txt");
            Assert.DoesNotContain("TOP-SECRET", warning.Message, "파일 내용 조각이 메시지에 실리면 안 된다");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
