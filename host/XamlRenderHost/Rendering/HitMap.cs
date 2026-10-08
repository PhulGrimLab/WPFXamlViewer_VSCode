using System.Windows;
using System.Windows.Media;

namespace XamlRenderHost.Rendering;

/// <summary>
/// 렌더된 요소 하나의 화면 경계와 원본 위치(프로토콜 `elements[]`, doc/01 3.1). 좌표는 결과 PNG의 픽셀(dpi 반영)이다.
/// Index는 전처리 SourceElement 번호, 줄/열은 1-base다.
/// </summary>
public sealed record HitElement(int Index, int Line, int Col, int EndLine, int EndCol, double X, double Y, double Width, double Height);

/// <summary>
/// 렌더된 비주얼 트리에서 `Uid="xv_번호"`가 붙은 요소를 찾아 경계 사각형을 계산한다(M5 HitMap).
/// 상태가 없는 정적 클래스. 반드시 요소가 만들어진 STA 스레드에서 호출한다.
/// 결과 순서는 비주얼 트리 선위 순회(부모 → 자식, 형제는 z-order 낮은 것 → 높은 것)라서 점을 포함하는 **마지막** 항목이
/// 가장 안쪽이고 가장 위에 그려진 요소다. 템플릿이 만든 내부 요소는 Uid가 없어 자연히 제외되고 클릭은 가장 가까운 태그된 조상에 귀속된다.
/// </summary>
public static class HitMap
{
    /// <summary>응답에 싣는 최대 요소 수(거대 문서에서 응답이 폭증하는 것을 막는다).</summary>
    public const int MaxElements = 5000;

    /// <summary>좌표를 반올림하는 소수 자릿수.</summary>
    private const int CoordinateDecimals = 2;

    /// <summary>
    /// 입력: 레이아웃이 끝난 루트, 전처리의 원본 위치 목록, 논리 단위 → 픽셀 배율(dpi/96). 출력: 경계 목록(최대 <see cref="MaxElements"/>개).
    /// truncated: 한도에 걸려 일부를 버렸는지.
    /// </summary>
    public static IReadOnlyList<HitElement> Collect(FrameworkElement root, IReadOnlyList<SourceElement> sources, double scale, out bool truncated)
    {
        truncated = false;
        var result = new List<HitElement>();
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current is UIElement element && TryReadIndex(element.Uid, sources.Count, out var index) && TryGetBounds(element, root, out var bounds))
            {
                if (result.Count >= MaxElements)
                {
                    truncated = true;
                    break;
                }
                else
                {
                    var source = sources[index];
                    result.Add(new HitElement(index, source.Line, source.Col, source.EndLine, source.EndCol,
                        Round(bounds.X * scale), Round(bounds.Y * scale), Round(bounds.Width * scale), Round(bounds.Height * scale)));
                }
            }
            else
            {
                // Uid가 없거나 보이지 않는 요소(템플릿 내부, 크기 0 등): 자식만 계속 본다.
            }

            if (current is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                var count = VisualTreeHelper.GetChildrenCount(current);
                for (var i = count - 1; i >= 0; i--)
                {
                    pending.Push(VisualTreeHelper.GetChild(current, i)); // 역순으로 넣어 앞 자식이 먼저 나오게 한다.
                }
            }
            else
            {
                // 비주얼이 아님.
            }
        }
        return result;
    }

    private static bool TryReadIndex(string? uid, int sourceCount, out int index)
    {
        index = -1;
        return uid != null && uid.StartsWith(XamlPreprocessor.UidPrefix, StringComparison.Ordinal)
            && int.TryParse(uid.AsSpan(XamlPreprocessor.UidPrefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out index)
            && index >= 0 && index < sourceCount;
    }

    /// <summary>요소의 루트 기준 경계. 크기가 0이거나 루트의 후손이 아니면 false.</summary>
    private static bool TryGetBounds(UIElement element, FrameworkElement root, out Rect bounds)
    {
        bounds = Rect.Empty;
        var size = element.RenderSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return false;
        }
        else if (ReferenceEquals(element, root))
        {
            bounds = new Rect(0, 0, size.Width, size.Height);
            return true;
        }
        else
        {
            try
            {
                bounds = element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, size.Width, size.Height));
                return true;
            }
            catch (InvalidOperationException)
            {
                return false; // 루트의 후손이 아니다(해제 중인 요소 등).
            }
        }
    }

    private static double Round(double value) => Math.Round(value, CoordinateDecimals);
}
