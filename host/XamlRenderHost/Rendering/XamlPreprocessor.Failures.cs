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
