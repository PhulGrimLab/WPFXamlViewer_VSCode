using XamlRenderHost.Rendering;
using XamlRenderHost.Tests.Support;

namespace XamlRenderHost.Tests;

/// <summary>호스트가 만드는 경고/오류 문구의 언어 전환(한국어 기본, `lang`=en이면 영어) 테스트(H-L).</summary>
[TestClass]
[DoNotParallelize] // Loc은 프로세스 전역 상태라 다른 테스트와 동시에 돌면 문구가 섞인다.
public class LocalizationTests
{
    private const string Ns = RenderTestHelper.PresentationNs;
    private const string UnknownTypeXaml = $"<StackPanel {Ns} xmlns:u=\"clr-namespace:Nope;assembly=Nope\"><u:Widget/></StackPanel>";

    [TestCleanup]
    public void Reset() => Loc.Set(null);

    private static string FirstWarning(string xaml)
        => StaRunner.Run(() => XamlRenderer.Render(new RenderRequest(xaml))).Warnings!.First(w => w.Code == WarningCodes.PlaceholderUsed).Message;

    [TestMethod]
    public void L01_DefaultIsKorean()
    {
        Loc.Set(null);
        StringAssert.Contains(FirstWarning(UnknownTypeXaml), "자리표시자");
    }

    [TestMethod]
    public void L02_EnglishWarningsAndErrors()
    {
        Loc.Set("en");
        StringAssert.Contains(FirstWarning(UnknownTypeXaml), "placeholder");
        var ex = Assert.Throws<XamlRenderException>(() => StaRunner.Run(() => XamlRenderer.Render(new RenderRequest(" "))));
        StringAssert.Contains(ex.Message, "empty");
    }

    [TestMethod]
    public void L03_LanguageValues()
    {
        Loc.Set("en-US");
        Assert.IsTrue(Loc.English);
        Loc.Set("ko");
        Assert.IsFalse(Loc.English);
        Loc.Set("fr");
        Assert.IsFalse(Loc.English, "알 수 없는 값은 기본(한국어)");
    }
}
