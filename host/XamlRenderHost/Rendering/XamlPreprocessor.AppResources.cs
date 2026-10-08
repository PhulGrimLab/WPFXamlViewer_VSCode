using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace XamlRenderHost.Rendering;

/// <summary>
/// `App.xaml`의 `Application.Resources`를 문서 루트의 병합 사전으로 주입한다(M4 잔여, doc/02 4.3).
/// `{StaticResource}`는 파싱 중에 풀리므로 XamlReader.Parse 전에 텍스트로 넣어야 한다.
/// 주입 조각에는 줄바꿈을 넣지 않아 이후 오류의 줄 번호가 보존된다(같은 줄의 열만 밀린다).
/// </summary>
public static partial class XamlPreprocessor
{
    /// <summary>앱 리소스 파일 이름.</summary>
    private const string ApplicationFileName = "App.xaml";

    /// <summary>프로젝트 파일 검색 패턴. 이 파일이 있는 폴더를 프로젝트 경계로 본다.</summary>
    private const string ProjectFilePattern = "*.csproj";

    private static readonly XNamespace PresentationXNamespace = PresentationNamespace;
    private static readonly XNamespace MarkupCompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>
    /// 속성 요소(`ResourceDictionary.MergedDictionaries`)에는 xmlns 선언을 붙일 수 없어(WPF 파서가 거부) 객체 요소에
    /// 이 접두사를 선언하고 속성 요소를 이 접두사로 쓴다.
    /// </summary>
    private const string InjectedPrefix = "rxapp";

    /// <summary>루트 속성 값이 단순 `{StaticResource Key}` 형태인지 판별한다(Key만 캡처).</summary>
    private static readonly System.Text.RegularExpressions.Regex StaticResourceValue =
        new(@"^\{\s*StaticResource\s+(?:ResourceKey\s*=\s*)?([^\s{}]+)\s*\}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>병합 사전 속성 요소의 로컬 이름.</summary>
    private const string MergedDictionariesName = "ResourceDictionary.MergedDictionaries";

    /// <summary>루트의 `Name="{StaticResource Key}"` 속성(옮겨야 할 후보)과 원문 위치.</summary>
    private readonly record struct HoistedAttribute(string Name, string Key, int Start, int End);

    /// <summary>찾은 요소의 위치 정보(시작 태그/끝 태그 오프셋).</summary>
    private sealed class ElementSpan
    {
        public required string Name { get; init; }
        /// <summary>시작 태그의 '&lt;' 위치.</summary>
        public required int OpenStart { get; init; }
        /// <summary>시작 태그의 '&gt;' 위치(빈 요소는 "/&gt;"의 '&gt;').</summary>
        public required int StartTagEnd { get; init; }
        public required bool IsEmpty { get; init; }
        /// <summary>끝 태그의 '&lt;' 위치. 빈 요소는 -1.</summary>
        public int EndTagStart { get; set; } = -1;
    }

    /// <summary>
    /// 문서 위치에서 가까운 App.xaml의 리소스를 병합 사전으로 주입한다. 파일 경로가 없거나 App.xaml이 없거나
    /// 리소스가 없거나 루트가 사전/Application이면 원문 그대로 돌려준다. 읽기/해석 실패는 경고로 남기고 건너뛴다.
    /// </summary>
    private static string InjectApplicationResources(string xaml, string? filePath, Context context)
    {
        if (filePath == null)
        {
            return xaml;
        }
        else
        {
            // 경로 있음: App.xaml 탐색.
        }

        var appPath = FindApplicationFile(filePath);
        if (appPath == null || string.Equals(Path.GetFullPath(filePath), appPath, StringComparison.OrdinalIgnoreCase))
        {
            return xaml;
        }
        else
        {
            // 문서가 아닌 별도 App.xaml 발견.
        }

        var dictionaryXml = BuildApplicationDictionary(appPath, context);
        return dictionaryXml == null ? xaml : InsertMergedDictionary(xaml, dictionaryXml);
    }

    /// <summary>
    /// 문서 폴더에서 위로 올라가며 가장 가까운 App.xaml을 찾는다. .csproj가 있는 폴더는 프로젝트 경계라서
    /// 그 폴더까지만 본다(다른 프로젝트의 App.xaml을 쓰지 않기 위해). 못 찾으면 null.
    /// </summary>
    private static string? FindApplicationFile(string filePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        for (var level = 0; level <= MaxAncestorSearchLevels && directory != null; level++)
        {
            var candidate = Path.Combine(directory, ApplicationFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            else if (Directory.EnumerateFiles(directory, ProjectFilePattern).Any())
            {
                return null; // 프로젝트 루트인데 App.xaml이 없다: 더 올라가지 않는다.
            }
            else
            {
                directory = Path.GetDirectoryName(directory);
            }
        }
        return null;
    }

    /// <summary>
    /// App.xaml을 전처리해 Application.Resources 내용을 줄바꿈 없는 ResourceDictionary XML 문자열로 만든다.
    /// App.xaml 자체의 제거 경고(x:Class, Startup 이벤트 등)는 사용자 문서와 무관해 버리고, 대체/사전 문제만 줄 없이 전달한다.
    /// 리소스가 없거나 읽을 수 없으면 null.
    /// </summary>
    private static string? BuildApplicationDictionary(string appPath, Context context)
    {
        var appContext = new Context(new HashSet<string>(context.VisitedDictionaries, StringComparer.OrdinalIgnoreCase) { appPath })
        {
            ProjectAssemblyName = context.ProjectAssemblyName,
            Schema = context.Schema,
        };
        XDocument document;
        try
        {
            var text = ProcessInternal(File.ReadAllText(appPath), appPath, appContext, depth: 1);
            document = XDocument.Parse(text);
        }
        catch (Exception ex) when (ex is IOException or XmlException or XamlRenderException or UnauthorizedAccessException)
        {
            context.Warnings.Add(new RenderWarning(WarningCodes.DictionaryUnavailable, $"{ApplicationFileName}를 읽을 수 없어 앱 리소스를 건너뜁니다: {ex.Message}", null, null));
            return null;
        }

        foreach (var w in appContext.Warnings.Where(w => w.Code is WarningCodes.PlaceholderUsed or WarningCodes.DictionaryUnavailable))
        {
            context.Warnings.Add(new RenderWarning(w.Code, $"{ApplicationFileName}: {w.Message}", null, null));
        }

        var appRoot = document.Root;
        var resources = appRoot?.Element(PresentationXNamespace + "Application.Resources");
        if (appRoot == null || resources == null || !resources.HasElements)
        {
            return null;
        }
        else
        {
            // 주입할 리소스 있음.
        }

        var children = resources.Elements().ToList();
        var dictionary = children.Count == 1 && children[0].Name == PresentationXNamespace + "ResourceDictionary"
            ? new XElement(children[0])
            : new XElement(PresentationXNamespace + "ResourceDictionary", children.Select(c => new XElement(c)));

        // App.xaml 루트에 선언된 접두사(값 안의 `local:Foo`, `x:Type` 등이 참조)와 mc:Ignorable을 사전으로 옮긴다.
        foreach (var attribute in appRoot.Attributes())
        {
            var isDefaultNamespace = attribute.Name == "xmlns";
            var isCarried = (attribute.IsNamespaceDeclaration && !isDefaultNamespace) || attribute.Name == MarkupCompatibilityNamespace + "Ignorable";
            if (isCarried && dictionary.Attribute(attribute.Name) == null)
            {
                dictionary.Add(new XAttribute(attribute.Name, attribute.Value));
            }
            else
            {
                // 옮기지 않는 속성(기본 네임스페이스는 요소 이름이 이미 가진다).
            }
        }

        return SingleLine(dictionary.ToString(SaveOptions.DisableFormatting));
    }

    /// <summary>줄 수를 바꾸지 않도록 문자열 안의 줄바꿈 문자를 문자 참조로 바꾼다(요소 사이 공백은 파싱에서 이미 제거됨).</summary>
    private static string SingleLine(string xml) => xml.Replace("\r", "&#13;").Replace("\n", "&#10;");

    /// <summary>
    /// 사전 XML을 문서 루트의 Resources 맨 앞 병합 사전으로 끼워 넣는다(문서 자신의 리소스가 앱 리소스보다 우선하도록).
    /// 경우: A) 루트에 Resources 없음 → 속성 요소 추가, B) 명시 ResourceDictionary가 있음 → 그 MergedDictionaries에 추가(없으면 생성),
    /// C) 사전 없이 항목만 나열(암시적 사전) → ResourceDictionary로 감싼다. 루트가 사전/Application이면 건드리지 않는다.
    /// </summary>
    private static string InsertMergedDictionary(string xaml, string dictionaryXml)
    {
        ElementSpan? root = null, resources = null, firstChild = null, merged = null;
        var hoisted = new List<HoistedAttribute>();
        var lineStarts = BuildLineStarts(xaml);
        try
        {
            using var stringReader = new StringReader(xaml);
            using var reader = XmlReader.Create(stringReader, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            var lineInfo = (IXmlLineInfo)reader;
            var open = new Dictionary<int, ElementSpan>();
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    var nameOffset = ToOffset(lineStarts, lineInfo.LineNumber, lineInfo.LinePosition);
                    var openStart = nameOffset - 1;
                    var span = new ElementSpan
                    {
                        Name = reader.Name,
                        OpenStart = openStart,
                        StartTagEnd = IndexOfTagEnd(xaml, openStart),
                        IsEmpty = reader.IsEmptyElement,
                    };
                    if (reader.Depth == 0)
                    {
                        root = span;
                        CollectHoistableAttributes(reader, xaml, lineStarts, lineInfo, hoisted);
                    }
                    else if (reader.Depth == 1 && root != null && reader.Name == root.Name + ".Resources")
                    {
                        resources = span;
                    }
                    else if (reader.Depth == 2 && resources != null && firstChild == null && open.GetValueOrDefault(1) == resources)
                    {
                        firstChild = span;
                    }
                    else if (reader.Depth == 3 && firstChild != null && merged == null && open.GetValueOrDefault(2) == firstChild
                        && reader.LocalName == MergedDictionariesName)
                    {
                        merged = span;
                    }
                    else
                    {
                        // 주입 위치와 무관한 요소.
                    }
                    open[reader.Depth] = span;
                    if (reader.Depth == 0 && (reader.NamespaceURI == PresentationNamespace && reader.LocalName is "ResourceDictionary" or "Application"))
                    {
                        return xaml; // 사전/Application 문서에는 앱 리소스를 주입하지 않는다.
                    }
                    else
                    {
                        // 일반 루트.
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement && open.TryGetValue(reader.Depth, out var closing))
                {
                    var nameOffset = ToOffset(lineStarts, lineInfo.LineNumber, lineInfo.LinePosition);
                    closing.EndTagStart = xaml.LastIndexOf('<', nameOffset);
                }
                else
                {
                    // 텍스트/주석 등.
                }
            }
        }
        catch (XmlException)
        {
            return xaml; // 이후 단계의 XamlReader가 XML 오류를 보고한다(전처리 1차 패스에서 이미 걸러졌으므로 사실상 도달하지 않는다).
        }

        if (root == null)
        {
            return xaml;
        }
        else
        {
            // 루트 있음: 경우별 편집.
        }

        var ns = $"xmlns=\"{PresentationNamespace}\" xmlns:{InjectedPrefix}=\"{PresentationNamespace}\"";
        var wrapped = $"<{InjectedPrefix}:{MergedDictionariesName}>{dictionaryXml}</{InjectedPrefix}:{MergedDictionariesName}>";
        var prefixDeclaration = $" xmlns:{InjectedPrefix}=\"{PresentationNamespace}\"";
        var edits = new List<Edit>();
        // 루트 자신의 {StaticResource} 속성은 같은 요소의 Resources보다 먼저 평가되어 주입한 리소스를 못 본다.
        // 속성 요소로 옮겨 Resources 뒤에 두면(속성 요소는 문서 순서대로 평가됨) 앱 리소스를 볼 수 있다.
        var hoistedElements = string.Concat(hoisted.Select(h =>
            $"<{root.Name}.{h.Name}><StaticResource ResourceKey=\"{System.Security.SecurityElement.Escape(h.Key)}\" xmlns=\"{PresentationNamespace}\"/></{root.Name}.{h.Name}>"));
        foreach (var h in hoisted)
        {
            edits.Add(new Edit(h.Start, h.End, BlankPreservingNewlines(xaml, h.Start, h.End)));
        }

        if (resources == null)
        {
            var block = $"<{root.Name}.Resources><ResourceDictionary {ns}>{wrapped}</ResourceDictionary></{root.Name}.Resources>{hoistedElements}";
            AddInsertAfterStartTag(edits, xaml, root, block);
        }
        else
        {
            if (firstChild != null && firstChild.Name.EndsWith("ResourceDictionary", StringComparison.Ordinal))
            {
                if (merged != null)
                {
                    AddInsertAfterStartTag(edits, xaml, merged, dictionaryXml);
                }
                else
                {
                    // 기존 사전 요소에 접두사를 선언하고(빈 요소는 풀어 쓰며 함께 처리) 병합 사전 속성 요소를 맨 앞에 둔다.
                    AddInsertAfterStartTag(edits, xaml, firstChild, wrapped, prefixDeclaration);
                }
            }
            else if (resources.IsEmpty)
            {
                AddInsertAfterStartTag(edits, xaml, resources, $"<ResourceDictionary {ns}>{wrapped}</ResourceDictionary>");
            }
            else
            {
                edits.Add(new Edit(resources.StartTagEnd + 1, resources.StartTagEnd + 1, $"<ResourceDictionary {ns}>{wrapped}"));
                edits.Add(new Edit(resources.EndTagStart, resources.EndTagStart, "</ResourceDictionary>"));
            }

            if (hoistedElements.Length > 0)
            {
                // Resources 속성 요소가 끝난 바로 뒤에 둔다.
                var afterResources = resources.IsEmpty ? resources.StartTagEnd + 1 : xaml.IndexOf('>', resources.EndTagStart) + 1;
                edits.Add(new Edit(afterResources, afterResources, hoistedElements));
            }
            else
            {
                // 옮길 속성 없음.
            }
        }
        return ApplyEdits(xaml, edits);
    }

    /// <summary>
    /// 요소의 시작 태그 바로 뒤에 content를 넣는다. 빈 요소(`/&gt;`)는 `&gt;content&lt;/Name&gt;`로 풀어 쓴다.
    /// 문서 줄 수는 바뀌지 않는다(content에 줄바꿈 없음).
    /// </summary>
    private static void AddInsertAfterStartTag(List<Edit> edits, string xaml, ElementSpan element, string content, string tagDeclaration = "")
    {
        if (element.IsEmpty)
        {
            var slash = element.StartTagEnd - 1; // "/>" 의 '/'
            edits.Add(new Edit(slash, element.StartTagEnd + 1, $"{tagDeclaration}>{content}</{element.Name}>"));
        }
        else
        {
            if (tagDeclaration.Length > 0)
            {
                edits.Add(new Edit(element.StartTagEnd, element.StartTagEnd, tagDeclaration));
            }
            else
            {
                // 시작 태그에 덧붙일 선언 없음.
            }
            edits.Add(new Edit(element.StartTagEnd + 1, element.StartTagEnd + 1, content));
        }
    }

    /// <summary>
    /// 현재(루트) 요소의 속성 중 값이 단순 `{StaticResource Key}`인 일반 속성(접두사/연결 속성 제외)을 hoisted에 모은다.
    /// 리더 위치는 요소로 되돌린다.
    /// </summary>
    private static void CollectHoistableAttributes(XmlReader reader, string xaml, List<int> lineStarts, IXmlLineInfo lineInfo, List<HoistedAttribute> hoisted)
    {
        if (!reader.HasAttributes)
        {
            return;
        }
        else
        {
            // 속성 있음: 검사.
        }

        while (reader.MoveToNextAttribute())
        {
            var isPlain = reader.NamespaceURI.Length == 0 && reader.Prefix.Length == 0 && reader.Name != "xmlns" && !reader.LocalName.Contains('.');
            var match = isPlain ? StaticResourceValue.Match(reader.Value) : null;
            if (match is { Success: true })
            {
                var start = ToOffset(lineStarts, lineInfo.LineNumber, lineInfo.LinePosition);
                hoisted.Add(new HoistedAttribute(reader.LocalName, match.Groups[1].Value, start, FindAttributeEnd(xaml, start)));
            }
            else
            {
                // 옮길 필요 없는 속성.
            }
        }
        reader.MoveToElement();
    }
}
