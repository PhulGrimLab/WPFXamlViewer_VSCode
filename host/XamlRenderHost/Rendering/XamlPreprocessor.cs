using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xaml.Schema;
using System.Xaml;
using System.Xml;

namespace XamlRenderHost.Rendering;

/// <summary>렌더는 성공했지만 사용자가 알아야 하는 변경/제한(제거한 x:Class, 자리표시자 등). 줄/열은 1-base, 없으면 null.</summary>
public sealed record RenderWarning(string Code, string Message, int? Line, int? Column);

/// <summary>
/// 원본 문서의 요소 하나의 위치(HitMap 용). 줄/열은 1-base이며 열은 시작 태그의 '&lt;' 위치, 끝은 끝 태그 '&gt;' 다음 위치다.
/// 전처리가 시작 태그에 `Uid="xv_번호"`를 붙여 렌더 후 비주얼 트리의 요소와 이 목록을 연결한다.
/// 값 객체(전처리 중 끝 위치를 채우므로 setter가 있다).
/// </summary>
public sealed class SourceElement
{
    public int Line { get; init; }
    public int Col { get; init; }
    public int EndLine { get; set; }
    public int EndCol { get; set; }
}

/// <summary>전처리 결과: XamlReader가 읽을 수 있는 XAML, 수행한 변경 목록, (태그를 켰다면) 태그한 요소들의 원본 위치.</summary>
public sealed record PreprocessResult(string Xaml, IReadOnlyList<RenderWarning> Warnings, IReadOnlyList<SourceElement> Elements);

/// <summary>전처리가 남기는 경고 코드(프로토콜 warnings[].code 와 같은 문자열).</summary>
public static class WarningCodes
{
    /// <summary>x:Class 등 코드 비하인드 전용 속성을 제거했다.</summary>
    public const string RemovedClassAttribute = "RemovedClassAttribute";

    /// <summary>이벤트 핸들러 속성을 제거했다.</summary>
    public const string RemovedEventHandler = "RemovedEventHandler";

    /// <summary>x:Code 블록을 제거했다.</summary>
    public const string RemovedCodeBlock = "RemovedCodeBlock";

    /// <summary>해석할 수 없는 사용자 타입을 자리표시자로 대체했다.</summary>
    public const string PlaceholderUsed = "PlaceholderUsed";

    /// <summary>사용자 컨트롤(프로젝트 어셈블리)이 생성/초기화 중 예외를 던져 그 요소만 오류 자리표시자로 대체했다(H022).</summary>
    public const string UserControlFailed = "UserControlFailed";

    /// <summary>Tier 0으로 그렸다: 사용자 컨트롤이 자리표시자인 이유(미신뢰/산출물 없음/로드 실패)를 알린다(H023).</summary>
    public const string ProjectTier0 = "ProjectTier0";

    /// <summary>요소가 너무 많아 HitMap(클릭 매핑)을 한도까지만 만들었다.</summary>
    public const string HitMapTruncated = "HitMapTruncated";

    /// <summary>병합 사전 파일을 찾지 못했거나 읽을 수 없어 빈 사전으로 대체했다.</summary>
    public const string DictionaryUnavailable = "DictionaryUnavailable";
}

/// <summary>
/// 디자이너가 아닌 실제 프로젝트 XAML을 XamlReader.Parse가 읽을 수 있게 고친다(doc/01 3절, M4).
/// 처리: ① x:Class/x:Subclass/x:ClassModifier/x:FieldModifier 속성 제거 ② 이벤트 핸들러 속성 제거 ③ x:Code 제거
/// ④ 해석 불가 사용자 타입(clr-namespace 인데 어셈블리를 못 찾음) → 자리표시자 ⑤ ResourceDictionary Source 병합 사전 인라인.
/// 핵심 규칙: **줄/열을 보존**한다. 제거/대체한 구간은 같은 줄바꿈 수를 유지하므로 이후 파서 오류의 줄 번호가 원본과 같다
/// (열은 같은 줄에서 바뀐 부분 뒤에서만 어긋날 수 있다).
/// 상태가 없는 정적 클래스(스레드 안전). XML이 올바르지 않으면 XmlMalformed <see cref="XamlRenderException"/>을 던진다.
/// </summary>
public static partial class XamlPreprocessor
{
    /// <summary>XAML 언어 네임스페이스(x:).</summary>
    private const string XamlLanguageNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>WPF 기본 네임스페이스.</summary>
    public const string PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    /// <summary>CLR 네임스페이스 접두(사용자 타입 후보).</summary>
    private const string ClrNamespacePrefix = "clr-namespace:";

    /// <summary>병합 사전을 따라 들어가는 최대 깊이(순환 참조/폭주 방지).</summary>
    private const int MaxDictionaryDepth = 8;

    /// <summary>상대 경로 사전을 찾을 때 문서 폴더에서 위로 올라가는 최대 단계.</summary>
    private const int MaxAncestorSearchLevels = 6;

    /// <summary>pack URI에서 프로젝트 내 경로가 시작되는 표지.</summary>
    private const string PackComponentMarker = ";component/";

    /// <summary>자리표시자 박스의 색(오류 느낌의 붉은 계열).</summary>
    private const string PlaceholderBorderColor = "#E5484D";
    private const string PlaceholderFillColor = "#1AE5484D";

    /// <summary>사용자 컨트롤이 예외를 던졌을 때의 오류 자리표시자 색(주황 계열, 해석 불가 자리표시자와 구분).</summary>
    private const string ErrorPlaceholderColor = "#D97706";
    private const string ErrorPlaceholderFillColor = "#1AD97706";

    /// <summary>자리표시자가 원본에서 물려받는 일반 속성(레이아웃 관련). 값에 마크업 확장({)이 없을 때만 복사한다.</summary>
    private static readonly HashSet<string> InheritedPlaceholderAttributes = new(StringComparer.Ordinal)
    {
        "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "Margin",
        "HorizontalAlignment", "VerticalAlignment", "Visibility", "Opacity", "Uid",
    };

    private static readonly HashSet<string> RemovedXamlAttributes = new(StringComparer.Ordinal)
    {
        "Class", "Subclass", "ClassModifier", "FieldModifier",
    };

    /// <summary>XAML 타입 조회용 스키마 컨텍스트(스레드 안전하지 않으므로 호출마다 만든다).</summary>
    private sealed class Context
    {
        /// <summary>타입 조회용 스키마. Tier 1이면 렌더러가 만든 참조 지정 컨텍스트를 받는다(렌더와 같은 어셈블리 집합으로 판단해야 하므로).</summary>
        public XamlSchemaContext Schema { get; init; } = new();
        public readonly List<RenderWarning> Warnings = new();
        public readonly HashSet<string> VisitedDictionaries;

        /// <summary>Tier 1에서 로드한 프로젝트 어셈블리 이름. `assembly=` 없는 clr-namespace는 이 어셈블리로 해석한다. Tier 0이면 null.</summary>
        public string? ProjectAssemblyName { get; init; }

        /// <summary>true면 주 문서(깊이 0)의 UIElement 요소에 `Uid="xv_번호"`를 붙이고 위치를 <see cref="SourceElements"/>에 기록한다(HitMap).</summary>
        public bool TagElements { get; init; }

        /// <summary>태그한 요소들의 원본 위치(Uid 번호 = 목록 인덱스).</summary>
        public List<SourceElement> SourceElements { get; } = new();

        public Context(HashSet<string> visited) { VisitedDictionaries = visited; }
    }

    /// <summary>제거/대체할 구간. [Start, End) 문자 오프셋을 Replacement로 바꾼다.</summary>
    private readonly record struct Edit(int Start, int End, string Replacement);

    /// <summary>
    /// 전처리를 수행한다. 입력: XAML 원문, (선택) 문서 파일 경로(병합 사전의 상대 경로 해석용). 출력: 변환된 XAML과 경고 목록.
    /// 예외: 원문 XML이 올바르지 않으면 <see cref="XamlRenderException"/>(XmlMalformed).
    /// </summary>
    public static PreprocessResult Process(string xaml, string? filePath, string? projectAssemblyName = null, XamlSchemaContext? schema = null, bool tagElements = false)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(filePath))
        {
            visited.Add(Path.GetFullPath(filePath));
        }
        else
        {
            // 파일 경로 없음: 순환 감지는 사전 파일부터 시작한다.
        }
        var context = new Context(visited) { ProjectAssemblyName = projectAssemblyName, Schema = schema ?? new XamlSchemaContext(), TagElements = tagElements };
        var processed = ProcessInternal(xaml, filePath, context, depth: 0);
        processed = InjectApplicationResources(processed, filePath, context);
        return new PreprocessResult(processed, context.Warnings, context.SourceElements);
    }

    /// <summary>한 문서(루트 또는 병합 사전 파일)를 전처리한다. 경고는 context에 누적한다.</summary>
    private static string ProcessInternal(string xaml, string? filePath, Context context, int depth)
    {
        var lineStarts = BuildLineStarts(xaml);
        var edits = new List<Edit>();
        var warnings = context.Warnings;
        try
        {
            using var stringReader = new StringReader(xaml);
            using var reader = XmlReader.Create(stringReader, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            var lineInfo = (IXmlLineInfo)reader;
            var openTagged = new Stack<(SourceElement Element, int Depth)>(); // 끝 위치를 아직 모르는 태그된 요소
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (openTagged.Count > 0 && openTagged.Peek().Depth == reader.Depth)
                    {
                        var closing = openTagged.Pop().Element;
                        SetEnd(closing, lineStarts, IndexOfTagEnd(xaml, ToOffset(lineStarts, lineInfo.LineNumber, lineInfo.LinePosition)) + 1);
                    }
                    else
                    {
                        // 태그하지 않은 요소의 끝.
                    }
                    continue;
                }
                else if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }
                else
                {
                    // 요소: 아래에서 처리.
                }

                var elementStart = ToOffset(lineStarts, lineInfo.LineNumber, lineInfo.LinePosition) - 1; // '<' 위치
                if (IsXamlCodeElement(reader))
                {
                    var end = FindElementEnd(reader, xaml, lineStarts, lineInfo);
                    edits.Add(new Edit(elementStart, end, BlankPreservingNewlines(xaml, elementStart, end)));
                    warnings.Add(new RenderWarning(WarningCodes.RemovedCodeBlock, "x:Code 블록을 제거했습니다.", lineInfo.LineNumber, lineInfo.LinePosition - 1));
                }
                else if (IsDictionaryWithSource(reader))
                {
                    var (line, col) = (lineInfo.LineNumber, lineInfo.LinePosition - 1);
                    var source = reader.GetAttribute("Source")!;
                    var end = FindElementEnd(reader, xaml, lineStarts, lineInfo);
                    var inlined = InlineDictionary(source, filePath, context, depth, line, col);
                    edits.Add(new Edit(elementStart, end, inlined + NewlinesIn(xaml, elementStart, end)));
                }
                else if (IsUnresolvableUserType(reader, context))
                {
                    var (line, col) = (lineInfo.LineNumber, lineInfo.LinePosition - 1);
                    var typeName = reader.Name;
                    var tagged = depth == 0 && context.TagElements ? NewSourceElement(context, lineStarts, elementStart) : null;
                    var placeholder = BuildPlaceholder(reader, xaml, lineStarts, lineInfo, uid: tagged == null ? null : UidValue(context.SourceElements.Count - 1));
                    var end = FindElementEnd(reader, xaml, lineStarts, lineInfo);
                    if (tagged != null)
                    {
                        SetEnd(tagged, lineStarts, end);
                    }
                    else
                    {
                        // 태그하지 않음.
                    }
                    edits.Add(new Edit(elementStart, end, placeholder + NewlinesIn(xaml, elementStart, end)));
                    warnings.Add(new RenderWarning(WarningCodes.PlaceholderUsed, $"{typeName} 타입을 해석할 수 없어 자리표시자로 표시합니다.", line, col));
                }
                else
                {
                    // 태그 대상은 속성을 읽기 전에 정한다(CollectAttributeRemovals가 리더 위치를 움직였다 되돌린다).
                    var tag = depth == 0 && context.TagElements ? TryTagElement(reader, xaml, lineStarts, context, edits, elementStart) : null;
                    var isEmpty = reader.IsEmptyElement;
                    CollectAttributeRemovals(reader, xaml, lineStarts, lineInfo, context, edits);
                    if (tag != null)
                    {
                        if (isEmpty)
                        {
                            SetEnd(tag, lineStarts, IndexOfTagEnd(xaml, elementStart) + 1);
                        }
                        else
                        {
                            openTagged.Push((tag, reader.Depth));
                        }
                    }
                    else
                    {
                        // 태그하지 않은 요소.
                    }
                }
            }
        }
        catch (XmlException ex)
        {
            // XML이 올바르지 않다: 원문 그대로 XamlReader에 넘기면 x:Class 같은 부차적 오류가 먼저 보고되어 원인이 가려진다.
            // 그래서 여기서 XML 오류를 직접 보고한다(줄/열은 원문 기준).
            throw new XamlRenderException(RenderErrorCodes.XmlMalformed, ex.Message, ex.LineNumber, ex.LinePosition, ex);
        }

        return ApplyEdits(xaml, edits);
    }

    private static bool IsXamlCodeElement(XmlReader reader)
        => reader.NamespaceURI == XamlLanguageNamespace && reader.LocalName == "Code";

    private static bool IsDictionaryWithSource(XmlReader reader)
        => reader.NamespaceURI == PresentationNamespace && reader.LocalName == "ResourceDictionary" && reader.GetAttribute("Source") != null;

    /// <summary>clr-namespace 타입인데 어셈블리를 찾지 못해 XamlReader가 만들 수 없는 요소인가(속성 요소 `Type.Prop` 제외).</summary>
    private static bool IsUnresolvableUserType(XmlReader reader, Context context)
    {
        if (!reader.NamespaceURI.StartsWith(ClrNamespacePrefix, StringComparison.Ordinal) || reader.LocalName.Contains('.'))
        {
            return false;
        }
        else
        {
            var type = context.Schema.GetXamlType(new XamlTypeName(WithProjectAssembly(reader.NamespaceURI, context), reader.LocalName));
            return type == null || type.IsUnknown;
        }
    }

    /// <summary>현재 요소의 속성 중 제거 대상(x:Class 계열, 이벤트 핸들러)을 edits에 추가한다. 리더 위치는 요소로 되돌려 둔다.</summary>
    private static void CollectAttributeRemovals(XmlReader reader, string xaml, List<int> lineStarts, IXmlLineInfo lineInfo, Context context, List<Edit> edits)
    {
        if (!reader.HasAttributes)
        {
            return;
        }
        else
        {
            // 속성 있음: 아래에서 검사.
        }

        var elementNamespace = reader.NamespaceURI;
        var elementLocal = reader.LocalName;
        while (reader.MoveToNextAttribute())
        {
            if (reader.Prefix == "xmlns" || reader.Name == "xmlns")
            {
                AddProjectAssemblyToNamespace(reader, xaml, lineStarts, lineInfo, context, edits);
                continue;
            }
            else
            {
                // 일반 속성.
            }

            string? code = null;
            if (reader.NamespaceURI == XamlLanguageNamespace && RemovedXamlAttributes.Contains(reader.LocalName))
            {
                code = WarningCodes.RemovedClassAttribute;
            }
            else if (reader.NamespaceURI.Length == 0 && IsEventAttribute(context.Schema, elementNamespace, elementLocal, reader.LocalName))
            {
                code = WarningCodes.RemovedEventHandler;
            }
            else
            {
                // 유지할 속성.
            }

            if (code != null)
            {
                var start = ToOffset(lineStarts, lineInfo.LineNumber, lineInfo.LinePosition);
                var end = FindAttributeEnd(xaml, start);
                edits.Add(new Edit(start, end, BlankPreservingNewlines(xaml, start, end)));
                context.Warnings.Add(new RenderWarning(code, $"{reader.Name} 속성을 제거했습니다.", lineInfo.LineNumber, lineInfo.LinePosition));
            }
            else
            {
                // 제거 대상 아님.
            }
        }
        reader.MoveToElement();
    }

    /// <summary>HitMap용 Uid 값. 번호는 <see cref="Context.SourceElements"/>의 인덱스다.</summary>
    public const string UidPrefix = "xv_";

    internal static string UidValue(int index) => UidPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>오프셋을 1-base (줄, 열)로 바꾼다.</summary>
    private static (int Line, int Col) ToLineCol(List<int> lineStarts, int offset)
    {
        var index = lineStarts.BinarySearch(offset);
        if (index < 0)
        {
            index = ~index - 1;
        }
        else
        {
            // 정확히 줄 시작.
        }
        return (index + 1, offset - lineStarts[index] + 1);
    }

    private static void SetEnd(SourceElement element, List<int> lineStarts, int endOffset)
    {
        (element.EndLine, element.EndCol) = ToLineCol(lineStarts, endOffset);
    }

    /// <summary>시작 위치만 채운 SourceElement를 목록 끝에 추가한다(끝은 이후 SetEnd로 채운다).</summary>
    private static SourceElement NewSourceElement(Context context, List<int> lineStarts, int elementStart)
    {
        var (line, col) = ToLineCol(lineStarts, elementStart);
        var element = new SourceElement { Line = line, Col = col };
        context.SourceElements.Add(element);
        return element;
    }

    /// <summary>
    /// 현재 요소가 UIElement처럼 `Uid` 속성을 가진 알려진 타입이고 사용자가 Uid를 직접 지정하지 않았다면,
    /// 시작 태그 이름 바로 뒤에 `Uid="xv_번호"`를 붙이는 편집을 추가하고 새 SourceElement를 돌려준다. 아니면 null.
    /// 속성 요소(`Type.Prop`)와 해석 불가 타입은 대상이 아니다.
    /// </summary>
    private static SourceElement? TryTagElement(XmlReader reader, string xaml, List<int> lineStarts, Context context, List<Edit> edits, int elementStart)
    {
        if (reader.LocalName.Contains('.')
            || reader.GetAttribute("Uid") != null || reader.GetAttribute("Uid", XamlLanguageNamespace) != null)
        {
            return null;
        }
        else
        {
            // 후보: 타입 확인.
        }

        var type = context.Schema.GetXamlType(new XamlTypeName(WithProjectAssembly(reader.NamespaceURI, context), reader.LocalName));
        if (type is not { IsUnknown: false } || type.GetMember("Uid") == null)
        {
            return null;
        }
        else
        {
            var element = NewSourceElement(context, lineStarts, elementStart);
            var insertAt = elementStart + 1 + reader.Name.Length; // 시작 태그 이름 바로 뒤
            edits.Add(new Edit(insertAt, insertAt, $" Uid=\"{UidValue(context.SourceElements.Count - 1)}\""));
            return element;
        }
    }

    /// <summary>`assembly=` 없는 clr-namespace에 프로젝트 어셈블리 이름을 붙인 값을 돌려준다(해당 없으면 그대로).</summary>
    private static string WithProjectAssembly(string xmlNamespace, Context context)
    {
        var needsAssembly = context.ProjectAssemblyName != null
            && xmlNamespace.StartsWith(ClrNamespacePrefix, StringComparison.Ordinal)
            && !xmlNamespace.Contains(";assembly=", StringComparison.OrdinalIgnoreCase);
        return needsAssembly ? $"{xmlNamespace};assembly={context.ProjectAssemblyName}" : xmlNamespace;
    }

    /// <summary>
    /// xmlns 선언의 값이 `assembly=` 없는 clr-namespace이고 프로젝트 어셈블리가 있으면, 닫는 따옴표 앞에 `;assembly=이름`을 끼워 넣는다.
    /// 그래야 XamlReader가 "현재 프로젝트의 타입"을 로드된 어셈블리에서 찾는다(XamlReader.Parse에는 로컬 어셈블리 개념이 없다).
    /// </summary>
    private static void AddProjectAssemblyToNamespace(XmlReader reader, string xaml, List<int> lineStarts, IXmlLineInfo lineInfo, Context context, List<Edit> edits)
    {
        var rewritten = WithProjectAssembly(reader.Value, context);
        if (!ReferenceEquals(rewritten, reader.Value) && rewritten != reader.Value)
        {
            var start = ToOffset(lineStarts, lineInfo.LineNumber, lineInfo.LinePosition);
            var closingQuote = FindAttributeEnd(xaml, start) - 1;
            edits.Add(new Edit(closingQuote, closingQuote, $";assembly={context.ProjectAssemblyName}"));
        }
        else
        {
            // 보정 불필요: 그대로 둔다.
        }
    }

    /// <summary>속성 이름이 해당 요소 타입의 이벤트(일반 이벤트 또는 `Owner.Event` 형태의 연결 이벤트)인지 확인한다.</summary>
    private static bool IsEventAttribute(XamlSchemaContext schema, string elementNamespace, string elementLocal, string attributeLocal)
    {
        var dot = attributeLocal.IndexOf('.');
        if (dot < 0)
        {
            var elementType = schema.GetXamlType(new XamlTypeName(elementNamespace, elementLocal));
            return elementType is { IsUnknown: false } && elementType.GetMember(attributeLocal) is { IsEvent: true };
        }
        else
        {
            var ownerType = schema.GetXamlType(new XamlTypeName(PresentationNamespace, attributeLocal[..dot]));
            return ownerType is { IsUnknown: false } && ownerType.GetAttachableMember(attributeLocal[(dot + 1)..]) is { IsEvent: true };
        }
    }

    /// <summary>해석 불가 요소를 대신할 Border+TextBlock 문자열을 만든다. 레이아웃 관련/연결 속성/x:Name/x:Key만 물려받는다.</summary>
    private static string BuildPlaceholder(XmlReader reader, string xaml, List<int> lineStarts, IXmlLineInfo lineInfo, string? error = null, string? uid = null)
    {
        var typeName = reader.Name;
        var inherited = new StringBuilder();
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                var isInheritable =
                    (reader.NamespaceURI.Length == 0 && (reader.LocalName.Contains('.') || InheritedPlaceholderAttributes.Contains(reader.LocalName)))
                    || (reader.NamespaceURI == XamlLanguageNamespace && (reader.LocalName == "Name" || reader.LocalName == "Key"));
                if (isInheritable && !reader.Value.Contains('{') && reader.Prefix != "xmlns")
                {
                    var start = ToOffset(lineStarts, lineInfo.LineNumber, lineInfo.LinePosition);
                    var end = FindAttributeEnd(xaml, start);
                    inherited.Append(' ').Append(xaml, start, end - start);
                }
                else
                {
                    // 자리표시자에 필요 없는 속성(사용자 타입 전용 속성 등)은 버린다.
                }
            }
            reader.MoveToElement();
        }
        else
        {
            // 속성 없음.
        }

        if (uid != null)
        {
            inherited.Append(" Uid=\"").Append(uid).Append('"'); // HitMap: 자리표시자도 클릭하면 원본 줄로 이동한다.
        }
        else
        {
            // 태그하지 않음.
        }

        // 오류 자리표시자(사용자 컨트롤 생성 예외)는 두 번째 줄에 예외 요약을 보여 준다. 줄바꿈은 문자 참조로 넣어 문서 줄 수를 바꾸지 않는다.
        var label = System.Security.SecurityElement.Escape(error == null ? typeName : $"{typeName}\n{error}")!.Replace("\n", "&#10;");
        var color = error == null ? PlaceholderBorderColor : ErrorPlaceholderColor;
        var fill = error == null ? PlaceholderFillColor : ErrorPlaceholderFillColor;
        var trimming = error == null ? "TextTrimming=\"CharacterEllipsis\"" : "TextWrapping=\"Wrap\"";
        return $"<Border xmlns=\"{PresentationNamespace}\" BorderBrush=\"{color}\" BorderThickness=\"1\" Background=\"{fill}\" MinWidth=\"40\" MinHeight=\"22\"{inherited}>"
            + $"<TextBlock Text=\"{label}\" Foreground=\"{color}\" FontSize=\"11\" Margin=\"4,2\" {trimming}/></Border>";
    }

    /// <summary>
    /// `&lt;ResourceDictionary Source="..."/&gt;` 를 파일 내용으로 인라인한다. 찾지 못하거나 순환/너무 깊으면 경고와 함께 빈 사전으로 대체한다.
    /// </summary>
    private static string InlineDictionary(string source, string? filePath, Context context, int depth, int line, int col)
    {
        var emptyDictionary = $"<ResourceDictionary xmlns=\"{PresentationNamespace}\"/>";
        var resolved = ResolveDictionaryPath(source, filePath);
        if (resolved == null)
        {
            context.Warnings.Add(new RenderWarning(WarningCodes.DictionaryUnavailable, $"병합 사전을 찾을 수 없습니다: {source}", line, col));
            return emptyDictionary;
        }
        else if (depth >= MaxDictionaryDepth || !context.VisitedDictionaries.Add(resolved))
        {
            context.Warnings.Add(new RenderWarning(WarningCodes.DictionaryUnavailable, $"병합 사전이 순환하거나 너무 깊습니다: {source}", line, col));
            return emptyDictionary;
        }
        else
        {
            // 새로 읽을 수 있는 파일.
        }

        try
        {
            var text = File.ReadAllText(resolved);
            var inner = ProcessInternal(text, resolved, context, depth + 1);
            // XML 선언은 요소 중간에 올 수 없으므로 제거한다.
            return Regex.Replace(inner.TrimStart('\uFEFF'), @"^\s*<\?xml[^>]*\?>", string.Empty);
        }
        catch (IOException ex)
        {
            context.Warnings.Add(new RenderWarning(WarningCodes.DictionaryUnavailable, $"병합 사전을 읽을 수 없습니다: {source} ({ex.Message})", line, col));
            return emptyDictionary;
        }
        finally
        {
            // 같은 사전을 형제 위치에서 다시 병합하는 것은 순환이 아니므로 경로 위의 방문만 막는다.
            context.VisitedDictionaries.Remove(resolved);
        }
    }

    /// <summary>
    /// Source 문자열을 실제 파일로 바꾼다. 상대 경로는 문서 폴더, 없으면 위쪽 폴더들을 차례로 찾는다.
    /// pack URI는 ";component/" 뒤 경로로 같은 방식으로 찾는다. 네트워크 URI/찾지 못함은 null.
    /// </summary>
    private static string? ResolveDictionaryPath(string source, string? filePath)
    {
        if (filePath == null)
        {
            return null;
        }
        else
        {
            // 기준 폴더 있음.
        }

        var relative = source;
        var componentIndex = source.IndexOf(PackComponentMarker, StringComparison.OrdinalIgnoreCase);
        if (componentIndex >= 0)
        {
            relative = source[(componentIndex + PackComponentMarker.Length)..];
        }
        else if (source.Contains("://", StringComparison.Ordinal) || source.StartsWith("pack:", StringComparison.OrdinalIgnoreCase))
        {
            return null; // 네트워크/알 수 없는 pack URI는 읽지 않는다.
        }
        else
        {
            // 일반 상대 경로.
        }

        relative = relative.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        for (var level = 0; level <= MaxAncestorSearchLevels && directory != null; level++)
        {
            var candidate = Path.Combine(directory, relative);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
            else
            {
                directory = Path.GetDirectoryName(directory);
            }
        }
        return null;
    }

    // ---- 텍스트 위치/편집 도우미 ----

    /// <summary>각 줄의 시작 오프셋(0번째 줄 = 1행). XmlReader의 줄 번호(1-base)와 대응한다.</summary>
    private static List<int> BuildLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
            else
            {
                // 줄바꿈 아님.
            }
        }
        return starts;
    }

    private static int ToOffset(List<int> lineStarts, int line, int column) => lineStarts[line - 1] + column - 1;

    /// <summary>현재 속성의 이름 시작 오프셋에서 닫는 따옴표 다음까지의 끝 오프셋(배타)을 찾는다.</summary>
    private static int FindAttributeEnd(string xaml, int nameStart)
    {
        var equals = xaml.IndexOf('=', nameStart);
        var quoteIndex = equals + 1;
        while (char.IsWhiteSpace(xaml[quoteIndex]))
        {
            quoteIndex++;
        }
        var quote = xaml[quoteIndex];
        return xaml.IndexOf(quote, quoteIndex + 1) + 1;
    }

    /// <summary>
    /// 현재 요소(시작 태그에 위치)의 끝 오프셋(배타)을 찾고, 리더를 요소의 끝으로 옮긴다(자식은 건너뛴다).
    /// 빈 요소는 시작 태그의 '>' 까지, 아니면 짝이 되는 끝 태그의 '>' 까지다. 속성 값 속 '>' 는 따옴표로 건너뛴다.
    /// </summary>
    private static int FindElementEnd(XmlReader reader, string xaml, List<int> lineStarts, IXmlLineInfo lineInfo)
    {
        var scanFrom = ToOffset(lineStarts, lineInfo.LineNumber, lineInfo.LinePosition);
        if (!reader.IsEmptyElement)
        {
            var depth = reader.Depth;
            while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
            {
                // 자식 노드는 건너뛴다.
            }
            scanFrom = ToOffset(lineStarts, lineInfo.LineNumber, lineInfo.LinePosition);
        }
        else
        {
            // 빈 요소: 시작 태그 끝이 곧 요소 끝.
        }
        return IndexOfTagEnd(xaml, scanFrom) + 1;
    }

    /// <summary>from 이후 첫 번째 태그 종료 '>' 위치(따옴표 안은 무시).</summary>
    private static int IndexOfTagEnd(string text, int from)
    {
        var quote = '\0';
        for (var i = from; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
                else
                {
                    // 따옴표 안.
                }
            }
            else if (c == '"' || c == '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return i;
            }
            else
            {
                // 계속 탐색.
            }
        }
        return text.Length - 1;
    }

    /// <summary>구간을 줄바꿈(\r, \n)만 남기고 공백으로 바꾼 문자열. 줄 수를 보존한다.</summary>
    private static string BlankPreservingNewlines(string text, int start, int end)
    {
        var sb = new StringBuilder(end - start);
        for (var i = start; i < end; i++)
        {
            sb.Append(text[i] is '\r' or '\n' ? text[i] : ' ');
        }
        return sb.ToString();
    }

    /// <summary>구간 안의 줄바꿈 문자들만 모은 문자열(대체 텍스트 뒤에 붙여 줄 수를 맞춘다).</summary>
    private static string NewlinesIn(string text, int start, int end)
    {
        var sb = new StringBuilder();
        for (var i = start; i < end; i++)
        {
            if (text[i] is '\r' or '\n')
            {
                sb.Append(text[i]);
            }
            else
            {
                // 줄바꿈이 아닌 문자는 버린다.
            }
        }
        return sb.ToString();
    }

    /// <summary>겹치지 않는 편집을 시작 위치 순으로 적용한다. 겹치는 편집(바깥 요소가 이미 대체됨)은 건너뛴다.</summary>
    private static string ApplyEdits(string xaml, List<Edit> edits)
    {
        if (edits.Count == 0)
        {
            return xaml;
        }
        else
        {
            // 적용할 편집 있음.
        }

        var sb = new StringBuilder(xaml.Length);
        var position = 0;
        foreach (var edit in edits.OrderBy(e => e.Start))
        {
            if (edit.Start < position)
            {
                continue; // 이미 대체된 구간 안.
            }
            else
            {
                sb.Append(xaml, position, edit.Start - position).Append(edit.Replacement);
                position = edit.End;
            }
        }
        sb.Append(xaml, position, xaml.Length - position);
        return sb.ToString();
    }
}
