using System.IO;
using XamlRenderHost.Rendering;
using XamlRenderHost.Tests.Support;

namespace XamlRenderHost.Tests;

/// <summary>
/// T2 골든 이미지 테스트(H-R01~R10, H-D01): Fixtures/xaml/*.xaml 을 렌더해 Fixtures/golden/*.png 와 비교한다.
/// 골든 갱신: 환경 변수 XAMLVIEWER_UPDATE_GOLDEN=1 로 실행하면 현재 결과로 덮어쓴다 — 반드시 PNG diff를 눈으로 확인한 뒤 커밋할 것.
/// 실패하면 TestResults/render-diff/ 에 expected/actual/diff 이미지를 남긴다(doc/03 2절).
/// </summary>
[TestClass]
public class RenderGoldenTests
{
    private const string UpdateGoldenEnvVar = "XAMLVIEWER_UPDATE_GOLDEN";
    private const int DeterminismRepeatCount = 5;

    /// <summary>DynamicData 소스: 픽스처 파일 이름(확장자 제외) 목록.</summary>
    public static IEnumerable<object[]> FixtureNames =>
        Directory.GetFiles(TestPaths.XamlFixtures, "R*.xaml")
            .Select(f => new object[] { Path.GetFileNameWithoutExtension(f) })
            .OrderBy(a => (string)a[0], StringComparer.Ordinal);

    [TestMethod]
    [DynamicData(nameof(FixtureNames))]
    public void Render_MatchesGolden(string name)
    {
        var xaml = File.ReadAllText(Path.Combine(TestPaths.XamlFixtures, name + ".xaml"));
        var actual = RenderTestHelper.Render(xaml).Png;
        var goldenPath = Path.Combine(TestPaths.GoldenDir, name + ".png");

        if (Environment.GetEnvironmentVariable(UpdateGoldenEnvVar) == "1")
        {
            Directory.CreateDirectory(TestPaths.GoldenDir);
            File.WriteAllBytes(goldenPath, actual);
            return;
        }
        else
        {
            // 일반 실행: 비교로 진행.
        }

        Assert.IsTrue(File.Exists(goldenPath),
            $"골든 이미지가 없습니다: {goldenPath}. {UpdateGoldenEnvVar}=1 로 생성한 뒤 눈으로 확인하세요.");
        var expected = File.ReadAllBytes(goldenPath);
        var diff = StaRunner.Run(() => ImageComparer.Compare(expected, actual));

        if (diff.SizeMismatch || diff.Ratio > ImageComparer.DefaultMaxMismatchRatio)
        {
            Directory.CreateDirectory(TestPaths.DiffDir);
            File.WriteAllBytes(Path.Combine(TestPaths.DiffDir, name + "-expected.png"), expected);
            File.WriteAllBytes(Path.Combine(TestPaths.DiffDir, name + "-actual.png"), actual);
            if (diff.DiffPng != null)
            {
                File.WriteAllBytes(Path.Combine(TestPaths.DiffDir, name + "-diff.png"), diff.DiffPng);
            }
            else
            {
                // 크기가 달라 diff 이미지를 만들 수 없음.
            }
            Assert.Fail($"{name}: 골든과 다름 (크기 불일치={diff.SizeMismatch}, 불일치 {diff.MismatchedPixels}/{diff.TotalPixels} = {diff.Ratio:P3}). 차이 이미지: {TestPaths.DiffDir}");
        }
        else
        {
            // 허용 범위 이내: 통과.
        }
    }

    /// <summary>H-D01: 같은 XAML을 여러 번 렌더해도 결과 PNG 바이트가 완전히 같다(결정성).</summary>
    [TestMethod]
    public void Render_SameXamlRepeatedly_IsByteIdentical()
    {
        var xaml = File.ReadAllText(Path.Combine(TestPaths.XamlFixtures, "R02_TextBlock.xaml"));
        var first = RenderTestHelper.Render(xaml).Png;
        for (var i = 1; i < DeterminismRepeatCount; i++)
        {
            CollectionAssert.AreEqual(first, RenderTestHelper.Render(xaml).Png, $"{i + 1}번째 렌더 결과가 첫 결과와 다름");
        }
    }
}
