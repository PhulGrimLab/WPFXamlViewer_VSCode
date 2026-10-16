using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Markup;
using System.Xaml;
using System.Xaml.Schema;
using WpfXamlReader = System.Windows.Markup.XamlReader;

namespace XamlRenderHost.Rendering;

/// <summary>
/// 신뢰하지 않은 폴더(Tier 0)의 XAML을 파싱하기 **전에** 노드 스트림을 훑어, 코드 실행/정보 노출로 이어지는 구성을 거부한다.
/// XAML은 선언만으로 임의 정적 메서드를 호출할 수 있다(`ObjectDataProvider`의 `ObjectType`/`MethodName`, `x:FactoryMethod`, 임의 타입의 `{x:Static}`).
/// 신뢰된 폴더(Tier 1)는 어차피 프로젝트 코드를 실행하므로 이 검사를 하지 않는다. 허용 목록 방식이다: WPF 어셈블리와 기본 형식(문자열/숫자 등)만 만들 수 있다.
/// 상태 없는 정적 클래스(스레드 안전).
/// </summary>
internal static class UntrustedXamlGuard
{
    /// <summary>WPF 어셈블리(접두사 일치): 이 안의 형식은 만들거나 `{x:Static}`으로 읽을 수 있다(`ObjectDataProvider` 등은 이름으로 따로 막는다).</summary>
    private static readonly string[] WpfAssemblyPrefixes =
    {
        "PresentationFramework", "PresentationCore", "PresentationUI", "WindowsBase", "System.Xaml", "System.Windows.Controls.Ribbon", "System.Windows.Presentation",
        "UIAutomation", "ReachFramework", "System.ObjectModel",
    };

    /// <summary>
    /// 기본 클래스 라이브러리(`System.Private.CoreLib` 등)에는 `Environment`, `File`, `Activator`, `Type`처럼 정적 멤버로 코드를 실행/정보를 읽는 형식이 섞여 있어
    /// 어셈블리 단위로 허용할 수 없다. 열거형·기본 형식·아래의 안전한 값 형식과 컬렉션만 허용한다.
    /// </summary>
    private static readonly HashSet<string> SafeCoreTypes = new(StringComparer.Ordinal)
    {
        "System.Object", "System.String", "System.Decimal", "System.DateTime", "System.DateTimeOffset", "System.TimeSpan", "System.Uri", "System.Guid",
        "System.Math", "System.MathF", "System.Version", "System.Nullable`1",
    };

    private static readonly string[] CoreAssemblyPrefixes = { "System.Private.CoreLib", "System.Runtime", "System.Collections", "System.Private.Uri" };

    /// <summary>WPF 어셈블리에 있지만 임의 메서드 호출/외부 데이터 읽기가 가능해 이름으로 막는 형식.</summary>
    private static readonly HashSet<string> BlockedTypeNames = new(StringComparer.Ordinal)
    {
        "ObjectDataProvider", "XmlDataProvider",
    };

    /// <summary>값이 파일/네트워크 위치를 가리키면 거부하는 멤버 이름(이미지·글꼴·미디어·사전 등 "읽어 오는" 속성).</summary>
    private static readonly HashSet<string> LocationMembers = new(StringComparer.Ordinal)
    {
        "Source", "UriSource", "ImageSource", "Uri", "BaseUri", "FontFamily", "Icon", "SoundLocation",
    };

    /// <summary>파일/네트워크 위치로 보이는 값: file:, http(s):, ftp:, UNC(\\host, //host), 드라이브 경로(C:\ 또는 C:/). pack://application 과 data: 는 허용한다.</summary>
    private static readonly Regex ExternalLocation = new(@"^\s*(file:|https?:|ftp:|\\\\|//|[A-Za-z]:[\\/])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>XAML 2009의 생성 지시어(임의 생성자 인자/정적 팩터리 호출). WPF 느슨한 XAML에서도 읽힐 수 있어 막는다.</summary>
    private static readonly HashSet<string> BlockedDirectives = new(StringComparer.Ordinal) { "FactoryMethod", "Arguments" };

    /// <summary>
    /// 입력: 전처리를 마친 XAML 텍스트. 거부할 구성이 있으면 <see cref="XamlRenderException"/>(코드 UntrustedBlocked, 가능하면 줄/열 포함). 문제가 없으면 아무 일도 하지 않는다.
    /// 파싱 자체가 틀린 XAML은 여기서 오류를 만들지 않는다(뒤이은 정식 파싱이 같은 오류를 위치와 함께 보고한다).
    /// </summary>
    public static void Validate(string xaml)
    {
        try
        {
            Scan(xaml);
        }
        catch (XamlRenderException)
        {
            throw;
        }
        catch (Exception ex) when (ex is XamlException or System.Xml.XmlException or InvalidOperationException or NotSupportedException)
        {
            // 읽을 수 없는 XAML: 거부 사유가 아니다. 정식 파싱이 오류를 보고한다.
        }
    }

    private static void Scan(string xaml)
    {
        using var text = new StringReader(xaml);
        using var reader = new XamlXmlReader(text, WpfXamlReader.GetWpfSchemaContext(), new XamlXmlReaderSettings { ProvideLineInfo = true });
        var lineInfo = (IXamlLineInfo)reader;

        // 네임스페이스는 요소 범위를 따른다: 객체마다 프레임을 쌓고, 그 객체 앞의 선언을 담는다.
        var frames = new Stack<Dictionary<string, string>>();
        frames.Push(new Dictionary<string, string>(StringComparer.Ordinal));
        var pending = new List<(string Prefix, string Ns)>();
        var objects = new Stack<XamlType>();
        string? currentMember = null;
        var memberStack = new Stack<string?>();

        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XamlNodeType.NamespaceDeclaration:
                    pending.Add((reader.Namespace.Prefix, reader.Namespace.Namespace));
                    break;

                case XamlNodeType.StartObject:
                {
                    var scope = new Dictionary<string, string>(frames.Peek(), StringComparer.Ordinal);
                    foreach (var (prefix, ns) in pending)
                    {
                        scope[prefix] = ns;
                    }
                    pending.Clear();
                    frames.Push(scope);
                    objects.Push(reader.Type);
                    memberStack.Push(currentMember);
                    currentMember = null;
                    CheckType(reader.Type, lineInfo);
                    break;
                }

                case XamlNodeType.GetObject:
                    frames.Push(frames.Peek());
                    objects.Push(reader.Type ?? XamlLanguage.Object);
                    memberStack.Push(currentMember);
                    currentMember = null;
                    break;

                case XamlNodeType.EndObject:
                    frames.Pop();
                    objects.Pop();
                    currentMember = memberStack.Pop();
                    break;

                case XamlNodeType.StartMember:
                    currentMember = reader.Member.Name;
                    if (reader.Member.IsDirective && BlockedDirectives.Contains(reader.Member.Name))
                    {
                        throw Blocked(lineInfo, $"x:{reader.Member.Name}");
                    }
                    else
                    {
                        // 허용되는 멤버.
                    }
                    break;

                case XamlNodeType.Value:
                    if (reader.Value is string value && objects.Count > 0)
                    {
                        CheckValue(objects.Peek(), currentMember, value, frames.Peek(), reader.SchemaContext, lineInfo);
                    }
                    else
                    {
                        // 문자열이 아닌 값이거나 객체 밖의 값.
                    }
                    break;
            }
        }
    }

    private static void CheckType(XamlType? type, IXamlLineInfo lineInfo)
    {
        var clr = type?.UnderlyingType;
        if (clr == null)
        {
            return; // 알 수 없는 형식: 만들어지지 않는다(정식 파싱이 오류로 보고한다).
        }
        else if (BlockedTypeNames.Contains(clr.Name))
        {
            throw Blocked(lineInfo, clr.Name);
        }
        else if (!IsAllowedType(clr))
        {
            throw Blocked(lineInfo, clr.FullName ?? clr.Name);
        }
        else
        {
            // 허용된 형식.
        }
    }

    private static void CheckValue(XamlType owner, string? member, string value, IReadOnlyDictionary<string, string> namespaces, XamlSchemaContext context, IXamlLineInfo lineInfo)
    {
        if (member != null && LocationMembers.Contains(member) && ExternalLocation.IsMatch(value))
        {
            throw Blocked(lineInfo, Loc.T($"{member}=\"{Truncate(value)}\" (파일/네트워크 위치)", $"{member}=\"{Truncate(value)}\" (file/network location)"));
        }
        else
        {
            // 위치 속성이 아니거나 외부 위치가 아니다.
        }

        if (owner.UnderlyingType?.Name == "StaticExtension" && (member == "Member" || member == "_PositionalParameters" || member == "_Initialization"))
        {
            CheckStaticMember(value, namespaces, context, lineInfo);
        }
        else
        {
            // {x:Static}가 아니다.
        }
    }

    /// <summary>`{x:Static prefix:Type.Member}`의 형식이 허용된 어셈블리의 것인가. 형식을 못 찾으면(알 수 없음) 그대로 둔다(파싱이 실패한다).</summary>
    private static void CheckStaticMember(string value, IReadOnlyDictionary<string, string> namespaces, XamlSchemaContext context, IXamlLineInfo lineInfo)
    {
        var text = value.Trim();
        var dot = text.LastIndexOf('.');
        if (dot <= 0)
        {
            return; // `Type.Member` 형태가 아니다: StaticExtension이 자체 오류를 낸다.
        }
        else
        {
            // 형식 부분 해석.
        }

        var typePart = text[..dot];
        var colon = typePart.IndexOf(':');
        var prefix = colon >= 0 ? typePart[..colon] : string.Empty;
        var typeName = colon >= 0 ? typePart[(colon + 1)..] : typePart;
        if (!namespaces.TryGetValue(prefix, out var ns))
        {
            return; // 선언되지 않은 접두사: 파싱이 실패한다.
        }
        else
        {
            // 네임스페이스 확인됨.
        }

        var clr = context.GetXamlType(new XamlTypeName(ns, typeName))?.UnderlyingType;
        if (clr != null && !IsAllowedType(clr))
        {
            throw Blocked(lineInfo, $"x:Static {clr.FullName}");
        }
        else
        {
            // 허용된 형식(또는 알 수 없음).
        }
    }

    /// <summary>이 형식을 만들거나 정적 멤버를 읽어도 되는가(허용 목록).</summary>
    private static bool IsAllowedType(Type type)
    {
        var assembly = type.Assembly.GetName().Name ?? string.Empty;
        if (WpfAssemblyPrefixes.Any(prefix => assembly.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return true;
        }
        else if (CoreAssemblyPrefixes.Any(prefix => assembly.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
            return type.IsEnum || type.IsPrimitive
                || SafeCoreTypes.Contains(definition.FullName ?? string.Empty)
                || (definition.Namespace?.StartsWith("System.Collections", StringComparison.Ordinal) ?? false);
        }
        else
        {
            return false;
        }
    }

    private static string Truncate(string value) => value.Length <= 60 ? value : value[..60] + "…";

    private static XamlRenderException Blocked(IXamlLineInfo lineInfo, string what)
        => new(RenderErrorCodes.UntrustedBlocked,
            Loc.T($"신뢰하지 않은 폴더에서는 사용할 수 없는 구성입니다: {what}. 폴더를 신뢰하면 그려집니다.",
                  $"This construct is not allowed in an untrusted folder: {what}. Trust the folder to render it."),
            lineInfo.HasLineInfo ? lineInfo.LineNumber : null,
            lineInfo.HasLineInfo ? lineInfo.LinePosition : null);
}
