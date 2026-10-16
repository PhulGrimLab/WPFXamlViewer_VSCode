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

    /// <summary>
    /// 프로젝트 산출물이 바뀌어 다시 불러와야 하는데, 이 호스트는 이미 이전 버전을 불러 둔 상태다. 같은 프로세스에서 같은 라이브러리(예: WPF UI)를 두 번 불러오면
    /// 의존 속성이 중복 등록되어 렌더가 모두 실패하므로, 호출자가 재시작을 허용했으면(`canRestart`) 호스트를 새로 띄워 다시 요청하게 한다.
    /// </summary>
    public const string HostRestartRequired = "HostRestartRequired";

    /// <summary>신뢰하지 않은 폴더(Tier 0)의 XAML이 코드 실행/외부 읽기로 이어질 수 있는 구성을 담고 있어 거부했다(`UntrustedXamlGuard`).</summary>
    public const string UntrustedBlocked = "UntrustedBlocked";
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

    /// <summary>실패 시점의 프로젝트 탐색 이유(예: `NoArtifact` = 빌드 산출물 없음). 확장이 "먼저 빌드" 안내/자동 빌드에 쓴다. 모르면 null.</summary>
    public string? ProjectReason { get; set; }

    public XamlRenderException(string code, string message, int? line = null, int? column = null, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        Line = line;
        Column = column;
    }
}
