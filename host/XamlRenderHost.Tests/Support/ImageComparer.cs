using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace XamlRenderHost.Tests.Support;

/// <summary>두 이미지의 비교 결과. DiffPng는 크기가 같을 때만 채워진다(불일치 픽셀을 빨강으로 표시).</summary>
public sealed record ImageDiff(bool SizeMismatch, int MismatchedPixels, int TotalPixels, byte[]? DiffPng)
{
    /// <summary>전체 픽셀 대비 불일치 비율. 크기가 다르면 1.</summary>
    public double Ratio => SizeMismatch ? 1.0 : (TotalPixels == 0 ? 0.0 : (double)MismatchedPixels / TotalPixels);
}

/// <summary>
/// PNG 두 개를 픽셀 단위로 비교한다(doc/03 2절). 채널별 허용오차와 허용 불일치 비율은 호출자가 정한다.
/// 상태 없는 정적 유틸리티. 디코딩에 WPF를 쓰므로 STA 스레드에서 호출해야 한다.
/// </summary>
public static class ImageComparer
{
    /// <summary>채널별(B,G,R,A) 허용 오차 기본값.</summary>
    public const int DefaultChannelTolerance = 2;

    /// <summary>허용 불일치 픽셀 비율 기본값(0.1%).</summary>
    public const double DefaultMaxMismatchRatio = 0.001;

    private const int BytesPerPixel = 4;
    private const double LogicalDpi = 96.0;

    /// <summary>두 PNG를 비교한다. 크기가 다르면 SizeMismatch=true로 즉시 반환한다.</summary>
    public static ImageDiff Compare(byte[] expectedPng, byte[] actualPng, int channelTolerance = DefaultChannelTolerance)
    {
        var (ew, eh, expected) = Decode(expectedPng);
        var (aw, ah, actual) = Decode(actualPng);
        if (ew != aw || eh != ah)
        {
            return new ImageDiff(true, 0, 0, null);
        }
        else
        {
            // 크기가 같음: 픽셀 비교로 진행.
        }

        var diffPixels = new byte[expected.Length];
        var mismatched = 0;
        var total = ew * eh;
        for (var p = 0; p < total; p++)
        {
            var offset = p * BytesPerPixel;
            var different = false;
            for (var c = 0; c < BytesPerPixel; c++)
            {
                if (Math.Abs(expected[offset + c] - actual[offset + c]) > channelTolerance)
                {
                    different = true;
                    break;
                }
                else
                {
                    // 허용오차 이내.
                }
            }

            if (different)
            {
                mismatched++;
                // 불일치: 불투명 빨강(Pbgra32).
                diffPixels[offset] = 0;
                diffPixels[offset + 1] = 0;
                diffPixels[offset + 2] = 255;
                diffPixels[offset + 3] = 255;
            }
            else
            {
                // 일치: 기대 이미지를 흐리게 복사해 위치 파악을 돕는다(알파 1/4).
                for (var c = 0; c < BytesPerPixel; c++)
                {
                    diffPixels[offset + c] = (byte)(expected[offset + c] / 4);
                }
            }
        }

        return new ImageDiff(false, mismatched, total, EncodePng(ew, eh, diffPixels));
    }

    /// <summary>PNG를 Pbgra32 픽셀 배열로 디코딩한다.</summary>
    private static (int Width, int Height, byte[] Pixels) Decode(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Pbgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var pixels = new byte[width * height * BytesPerPixel];
        converted.CopyPixels(pixels, width * BytesPerPixel, 0);
        return (width, height, pixels);
    }

    private static byte[] EncodePng(int width, int height, byte[] pbgra32)
    {
        var source = BitmapSource.Create(width, height, LogicalDpi, LogicalDpi, PixelFormats.Pbgra32, null, pbgra32, width * BytesPerPixel);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
