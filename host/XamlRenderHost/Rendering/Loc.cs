namespace XamlRenderHost;

/// <summary>
/// 호스트가 사용자에게 보내는 경고/오류 문구의 언어(한국어/영어). 확장이 render 요청의 `lang`(`ko`/`en`)으로 정한다. 기본은 한국어.
/// 로그는 진단용이라 번역하지 않는다. 렌더는 한 스레드에서 직렬로 처리되므로 정적 값으로 충분하다.
/// </summary>
public static class Loc
{
    /// <summary>현재 표시 언어. true면 영어, false면 한국어.</summary>
    public static volatile bool English;

    /// <summary>`lang` 값(`en`으로 시작하면 영어, 그 밖/null은 한국어)을 적용한다.</summary>
    public static void Set(string? lang) => English = lang != null && lang.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    /// <summary>현재 언어의 문구를 고른다.</summary>
    public static string T(string ko, string en) => English ? en : ko;
}
