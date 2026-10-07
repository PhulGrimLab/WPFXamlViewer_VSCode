namespace XamlRenderHost.Rendering;

/// <summary>
/// 렌더 실패 사유 코드. 확장(DiagnosticsMapper)과 프로토콜(doc/01 3.1)이 문자열 값을 그대로 사용하므로
/// 값을 바꾸면 프로토콜 호환이 깨진다.
/// </summary>
public static class RenderErrorCodes
{
    /// <summary>입력이 비어 있거나 공백뿐이다.</summary>
    public const string EmptyInput = "EmptyInput";

    /// <summary>XML 자체가 올바르지 않다(닫히지 않은 태그, 루트 2개, XAML이 아닌 텍스트 등).</summary>
    public const string XmlMalformed = "XmlMalformed";

    /// <summary>XML은 올바르나 XAML로 해석할 수 없다(알 수 없는 요소/속성, 잘못된 값 등).</summary>
    public const string XamlParse = "XamlParse";

    /// <summary>루트가 렌더할 수 없는 종류다(FrameworkElement/Window가 아닌 객체).</summary>
    public const string UnsupportedRoot = "UnsupportedRoot";

    /// <summary>요청 또는 결정된 렌더 크기가 허용 범위를 넘는다.</summary>
    public const string TooLarge = "TooLarge";

    /// <summary>위 범주에 들지 않는 렌더 중 예외.</summary>
    public const string RenderFailed = "RenderFailed";
}

/// <summary>
/// 렌더 실패를 코드와 원본 위치(1-base 줄/열, 알 수 없으면 null)와 함께 전달하는 예외.
/// 단순 데이터 운반용이라 Owner/Lifetime 설명은 생략한다.
/// </summary>
public sealed class XamlRenderException : Exception
{
    /// <summary>실패 사유 코드(<see cref="RenderErrorCodes"/>).</summary>
    public string Code { get; }

    /// <summary>오류가 난 줄(1-base). 위치를 알 수 없으면 null.</summary>
    public int? Line { get; }

    /// <summary>오류가 난 열(1-base). 위치를 알 수 없으면 null.</summary>
    public int? Column { get; }

    public XamlRenderException(string code, string message, int? line = null, int? column = null, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        Line = line;
        Column = column;
    }
}
