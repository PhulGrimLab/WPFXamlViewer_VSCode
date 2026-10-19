using System.IO;
using System.Xml;

namespace XamlRenderHost.Rendering;

/// <summary>사용자 컨트롤 하나를 오류 자리표시자로 바꾼 결과.</summary>
public sealed record FailedElementReplacement(string Xaml, string TypeName, int Line, int Column);

/// <summary>
/// 사용자 컨트롤이 생성/초기화 중 예외를 던졌을 때 그 요소만 오류 자리표시자로 바꾼다(doc/01 3.3 규칙 6, M4B B.3).
/// 렌더러가 파싱 실패를 사용자 코드 예외로 판단하면 보고된 줄/열의 요소를 찾아 이 함수로 대체하고 다시 파싱한다.
/// 줄 번호는 전처리와 같은 방식으로 보존된다.
/// </summary>
public static partial class XamlPreprocessor
{
    /// <summary>`속성이름="` 시작(접두사/점 포함).</summary>
    private static readonly System.Text.RegularExpressions.Regex AttributeStart = new(@"\G[\w:.]+\s*=\s*[""']", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// 파서가 보고한 줄/열(1-base)의 속성을 통째로 지운다(줄 수는 유지). `{StaticResource 없는키}`처럼 값을 만들지 못한 속성 하나 때문에 문서 전체가 실패하지 않게 하는 복구용이다.
    /// 보고된 위치가 속성 이름의 시작이 아니면 같은 줄에서 그 위치 이전에 시작한 마지막 속성을 찾는다. 속성을 특정하지 못하면 null.
    /// </summary>
    public static (string Xaml, string AttributeName)? RemoveAttributeAt(string xaml, int reportedLine, int reportedColumn)
    {
        var lineStarts = BuildLineStarts(xaml);
        if (reportedLine < 1 || reportedLine > lineStarts.Count)
        {
            return null;
        }
        else
        {
            // 줄 번호 유효.
        }
        var lineStart = lineStarts[reportedLine - 1];
        var lineEnd = reportedLine < lineStarts.Count ? lineStarts[reportedLine] : xaml.Length;
        var offset = Math.Clamp(lineStart + reportedColumn - 1, lineStart, Math.Max(lineStart, lineEnd - 1));
        // 보고 위치에서 시작하는 속성, 아니면 그 앞(같은 줄)에서 시작해 보고 위치를 덮는 마지막 속성.
        for (var candidate = offset; candidate >= lineStart; candidate--)
        {
            var match = AttributeStart.Match(xaml, candidate);
            if (match.Success && (candidate == lineStart || char.IsWhiteSpace(xaml[candidate - 1])))
            {
                var end = FindAttributeEnd(xaml, candidate);
                if (end > offset || candidate == offset)
                {
                    var name = xaml.Substring(candidate, match.Length).TrimEnd('=', ' ', '"', '\'', '\t');
                    return (xaml[..candidate] + BlankPreservingNewlines(xaml, candidate, end) + xaml[end..], name);
                }
                else
                {
                    return null; // 보고 위치를 덮지 않는 앞쪽 속성: 특정하지 못했다.
                }
            }
            else
            {
                // 속성 시작이 아니다: 더 앞으로.
            }
        }
        return null;
    }

    /// <summary>
    /// 입력: 전처리된 XAML, 파서가 보고한 줄/열(1-base), 표시할 오류 요약. 출력: 대체 결과, 해당 위치에 clr-namespace 요소가 없으면 null.
    /// 대상은 "보고된 위치 이전(포함)에서 가장 마지막에 시작한 요소"이며 clr-namespace 타입일 때만 대체한다
    /// (WPF 기본 요소의 오류를 가리지 않기 위해).
    /// </summary>
    public static FailedElementReplacement? ReplaceFailedUserElement(string xaml, int reportedLine, int reportedColumn, string error)
    {
        var lineStarts = BuildLineStarts(xaml);
        var targetIndex = FindElementIndexAt(xaml, lineStarts, reportedLine, reportedColumn);
        if (targetIndex < 0)
        {
            return null;
        }
        else
        {
            // 대상 요소 서수를 찾음: 두 번째 패스에서 같은 서수의 요소를 대체한다.
        }

        using var stringReader = new StringReader(xaml);
        using var reader = XmlReader.Create(stringReader, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var lineInfo = (IXmlLineInfo)reader;
        var index = -1;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || ++index != targetIndex)
            {
                continue;
            }
            else
            {
                // 대상 요소.
            }

            if (!reader.NamespaceURI.StartsWith(ClrNamespacePrefix, StringComparison.Ordinal) || reader.LocalName.Contains('.'))
            {
                return null; // 사용자 타입 요소가 아니다.
            }
            else
            {
                // 사용자 타입 요소: 대체.
            }

            var typeName = reader.Name;
            var (line, column) = (lineInfo.LineNumber, lineInfo.LinePosition - 1);
            var start = ToOffset(lineStarts, lineInfo.LineNumber, lineInfo.LinePosition) - 1;
            var placeholder = BuildPlaceholder(reader, xaml, lineStarts, lineInfo, error);
            var end = FindElementEnd(reader, xaml, lineStarts, lineInfo);
            var edit = new Edit(start, end, placeholder + NewlinesIn(xaml, start, end));
            return new FailedElementReplacement(ApplyEdits(xaml, new List<Edit> { edit }), typeName, line, column);
        }
        return null;
    }

    /// <summary>인라인한 병합 사전/앱 리소스 구간의 시작 표지(XML 주석이라 XamlReader는 무시한다).</summary>
    private const string InlineBeginMarker = "<!--xv-inline-begin-->";

    /// <summary>인라인한 구간의 끝 표지.</summary>
    private const string InlineEndMarker = "<!--xv-inline-end-->";

    /// <summary>
    /// 인라인된 병합 사전/앱 리소스 안의 요소가 파싱에 실패했을 때 그 요소만 지운다(줄 수 보존). 사용자가 고칠 수 없는 다른 파일의 요소가
    /// 문서 전체의 미리보기를 막지 않게 하기 위함이다. 대상은 "보고된 위치 이전(포함)에서 가장 마지막에 시작한 요소"이며
    /// **인라인 구간 안**에 있을 때만 지운다 — 사용자가 편집 중인 문서 본문의 오류는 숨기지 않고 그대로 보고한다. 해당하지 않으면 null.
    /// </summary>
    public static FailedElementReplacement? RemoveFailedInlinedElement(string xaml, int reportedLine, int reportedColumn)
    {
        var lineStarts = BuildLineStarts(xaml);
        var targetIndex = FindElementIndexAt(xaml, lineStarts, reportedLine, reportedColumn);
        if (targetIndex < 0)
        {
            return null;
        }
        else
        {
            // 대상 요소 서수를 찾음: 두 번째 패스에서 같은 서수의 요소를 지운다.
        }

        using var stringReader = new StringReader(xaml);
        using var reader = XmlReader.Create(stringReader, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var lineInfo = (IXmlLineInfo)reader;
        var index = -1;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || ++index != targetIndex)
            {
                continue;
            }
            else
            {
                // 대상 요소.
            }

            var start = ToOffset(lineStarts, lineInfo.LineNumber, lineInfo.LinePosition) - 1;
            if (!IsInsideInlinedRegion(xaml, start))
            {
                return null; // 문서 본문의 요소: 오류를 그대로 보고한다.
            }
            else
            {
                // 인라인 구간의 요소: 지운다.
            }

            var name = reader.Name;
            var (line, column) = (lineInfo.LineNumber, lineInfo.LinePosition - 1);
            var end = FindElementEnd(reader, xaml, lineStarts, lineInfo);
            var edit = new Edit(start, end, BlankPreservingNewlines(xaml, start, end));
            return new FailedElementReplacement(ApplyEdits(xaml, new List<Edit> { edit }), name, line, column);
        }
        return null;
    }

    /// <summary>오프셋이 인라인 표지 쌍 사이(중첩 포함)에 있는가.</summary>
    private static bool IsInsideInlinedRegion(string xaml, int offset)
    {
        var openStarts = new Stack<int>();
        var position = 0;
        while (position < offset)
        {
            var begin = xaml.IndexOf(InlineBeginMarker, position, StringComparison.Ordinal);
            var end = xaml.IndexOf(InlineEndMarker, position, StringComparison.Ordinal);
            if (begin >= 0 && begin < offset && (end < 0 || begin < end))
            {
                openStarts.Push(begin);
                position = begin + InlineBeginMarker.Length;
            }
            else if (end >= 0 && end < offset && openStarts.Count > 0)
            {
                openStarts.Pop();
                position = end + InlineEndMarker.Length;
            }
            else
            {
                break; // 오프셋 앞에는 더 이상 표지가 없다.
            }
        }
        return openStarts.Count > 0;
    }

    /// <summary>보고된 위치 이전에 시작한 요소 중 마지막 것의 0-base 서수(문서 순서). 없으면 -1.</summary>
    private static int FindElementIndexAt(string xaml, List<int> lineStarts, int reportedLine, int reportedColumn)
    {
        var found = -1;
        var index = -1;
        using var stringReader = new StringReader(xaml);
        using var reader = XmlReader.Create(stringReader, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var lineInfo = (IXmlLineInfo)reader;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }
            else
            {
                index++;
            }

            var startsBeforeReport = lineInfo.LineNumber < reportedLine
                || (lineInfo.LineNumber == reportedLine && lineInfo.LinePosition <= reportedColumn);
            if (startsBeforeReport)
            {
                found = index;
            }
            else
            {
                break; // 이후 요소는 모두 보고 위치보다 뒤다.
            }
        }
        return found;
    }
}
