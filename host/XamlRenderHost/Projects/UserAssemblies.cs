using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;

namespace XamlRenderHost.Projects;

/// <summary>사용자 어셈블리 로드 결과. Reloaded는 이번 호출에서 새로(다시) 로드했는지, LoadMs는 그때 걸린 시간.</summary>
public sealed record UserAssemblyLoad(Assembly? Assembly, bool Reloaded, long LoadMs, string? Failure);

/// <summary>
/// 프로젝트 빌드 산출물을 호스트 프로세스 안의 수집 가능한 <see cref="AssemblyLoadContext"/>에 로드하고 관리한다(doc/01 3.3 규칙 3, M4B B.2).
///
/// Owner: 프로세스 전체에서 하나(<see cref="Instance"/>). Lifetime: 프로세스 시작~종료(종료 시 임시 복사본 삭제).
/// 스레드: 어느 스레드에서도 호출될 수 있다(렌더는 STA 스레드, 테스트는 병렬). 상태(로드 목록)는 <see cref="_gate"/> 잠금으로 보호한다.
/// 로드 방식: 산출물 폴더를 **임시 폴더에 복사한 뒤**(deps.json이 없으면 폴더 바로 아래 파일과 위성 어셈블리만, <see cref="CopyOutputDirectory"/>) 그 복사본에서 로드한다 — 원본 DLL을 잠그지 않아 사용자가 계속 빌드할 수 있다.
/// 의존 어셈블리는 미리 전부 로드하지 않고 XAML이 참조할 때(<see cref="EnsureReferencedAssemblyLoaded"/>) 또는 필요할 때 로드한다.
/// 원본의 수정 시각/크기가 바뀌면 이전 컨텍스트를 Unload하고 다시 만든다(갱신 시 재로드). 의존성은 deps.json 기반
/// <see cref="AssemblyDependencyResolver"/>로 해석하고, WPF 등 프레임워크 어셈블리는 기본 컨텍스트에서 공유한다.
/// XamlSchemaContext가 `assembly=이름`으로 찾는 어셈블리는 기본 컨텍스트의 Resolving 이벤트로 이 클래스가 대신 돌려준다.
/// </summary>
public sealed class UserAssemblies
{
    /// <summary>프로세스 전역 인스턴스(기본 AssemblyLoadContext가 하나뿐이라 로더도 하나다).</summary>
    public static UserAssemblies Instance { get; } = new();

    private readonly object _gate = new();
    private readonly Dictionary<string, LoadedProject> _projects = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>다시 불러오며 버렸지만 DLL이 잠겨 아직 못 지운 임시 복사본들(<see cref="_gate"/>로 보호).</summary>
    private readonly List<string> _abandonedTempDirectories = new();

    /// <summary>
    /// 이 스레드의 렌더가 **신뢰된(Tier 1)** 렌더인가. true일 때만 기본 컨텍스트의 이름 해석 요청(<see cref="OnDefaultResolving"/>)에 사용자 어셈블리를 돌려준다.
    /// 신뢰하지 않은 문서가 `{local:Ext}`/`clr-namespace:X;assembly=…`로 앞서 로드된 사용자 어셈블리를 이름으로 불러 사용자 코드를 실행하지 못하게 하는 장치다.
    /// 렌더러가 Tier 1 렌더 구간에서만 켠다(<see cref="BeginTrustedResolution"/>).
    /// </summary>
    [ThreadStatic]
    private static bool _resolutionAllowedOnThisThread;

    /// <summary>이 스레드에서 사용자 어셈블리 이름 해석을 허용한다. 반환된 객체를 Dispose하면 원래대로 닫힌다(using으로 감싼다).</summary>
    public static IDisposable BeginTrustedResolution()
    {
        var previous = _resolutionAllowedOnThisThread;
        _resolutionAllowedOnThisThread = true;
        return new ResolutionScope(previous);
    }

    private sealed class ResolutionScope : IDisposable
    {
        private readonly bool _previous;

        public ResolutionScope(bool previous) { _previous = previous; }

        public void Dispose() { _resolutionAllowedOnThisThread = _previous; }
    }

    private UserAssemblies()
    {
        AssemblyLoadContext.Default.Resolving += OnDefaultResolving;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteAllTempDirectories();
        // 이전 호스트가 남긴(잠겨서 못 지운) 임시 복사본 정리. 수백 MB를 지우는 데 시간이 걸릴 수 있어 첫 렌더를 막지 않도록 백그라운드에서 한다.
        ThreadPool.QueueUserWorkItem(_ => SweepStaleTempDirectories());
    }

    /// <summary>프로젝트 하나의 로드 상태.</summary>
    private sealed class LoadedProject
    {
        public required string SourcePath { get; init; }
        public required DateTime SourceWriteTimeUtc { get; init; }
        public required long SourceLength { get; init; }
        public required string TempDirectory { get; init; }
        public required UserLoadContext Context { get; init; }
        public required Assembly Assembly { get; init; }
    }

    /// <summary>
    /// 사용자 어셈블리 전용 컨텍스트. deps.json으로 의존성을 찾는다.
    /// **수집 불가(non-collectible)**: WPF가 BAML을 로드할 때 만드는 동적 코드가 수집 가능한 어셈블리를 참조하지 못해("A non-collectible assembly may not
    /// reference a collectible assembly") 라이브러리의 컴파일된 리소스 사전을 읽을 수 없기 때문이다. 대가로 다시 불러올 때마다 이전 어셈블리가 프로세스에
    /// 남는다(메모리 누적은 <see cref="ShouldRecycle"/>로 호스트를 새로 띄워 완화한다).
    /// </summary>
    private sealed class UserLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _mainDirectory;

        /// <summary>의존성을 이미 미리 로드했는가. 폴더 전체 로드는 느리므로 컨텍스트당 한 번만 한다(xmlns마다 반복하지 않는다).</summary>
        private bool _dependenciesPreloaded;

        /// <summary>주 어셈블리 옆에 `.deps.json`이 있는가. 있으면 의존성 목록이 정해져 있어 그 목록의 DLL만 미리 로드한다.</summary>
        private readonly bool _hasDepsFile;

        public UserLoadContext(string mainAssemblyPath) : base("XamlViewer.User:" + Path.GetFileName(mainAssemblyPath), isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
            _mainDirectory = Path.GetDirectoryName(mainAssemblyPath)!;
            _hasDepsFile = File.Exists(Path.ChangeExtension(mainAssemblyPath, ".deps.json"));
        }

        /// <summary>
        /// 사용자 어셈블리가 참조하는 어셈블리 중 deps.json이 가리키는 것(프로젝트 참조/NuGet 라이브러리)을 미리 이 컨텍스트에 로드한다.
        /// XamlSchemaContext에 "이 컨텍스트의 어셈블리 전체"를 넘기려면 지연 로드 전에 목록에 들어 있어야 하기 때문이다.
        /// 프레임워크 어셈블리는 resolver가 null을 돌려주므로 건너뛴다. 로드 실패는 무시한다(실제로 쓰일 때 오류가 보고된다).
        /// </summary>
        public void PreloadDependencies(Assembly main)
        {
            if (_dependenciesPreloaded)
            {
                return; // 이미 했다(같은 컨텍스트의 의존성은 다시 목록을 훑을 필요가 없다).
            }
            else
            {
                _dependenciesPreloaded = true;
            }

            PreloadSiblingAssemblies();
            foreach (var reference in main.GetReferencedAssemblies())
            {
                var path = _resolver.ResolveAssemblyToPath(reference);
                if (path == null || IsFrameworkAssembly(reference.Name) || IsHostFrameworkAssembly(reference.Name))
                {
                    continue;
                }
                else
                {
                    try
                    {
                        LoadFromAssemblyPath(path);
                    }
                    catch (Exception ex) when (ex is IOException or BadImageFormatException or FileLoadException)
                    {
                        // 이 의존성은 지금 쓸 수 없다: 실제 사용 시점의 오류에 맡긴다.
                    }
                }
            }
        }

        /// <summary>
        /// 주 어셈블리와 같은 폴더의 관리 어셈블리(`*.dll`)를 모두 이 컨텍스트에 로드한다. XAML에서만 쓰는 라이브러리(예: URI xmlns로 `XmlnsDefinition`이 매핑된 컨트롤)는
        /// 컴파일된 주 어셈블리의 참조 목록에 없을 수 있어 참조만 따라가서는 놓친다. 네이티브 DLL/로드 불가 파일은 건너뛴다. 필요할 때만 호출한다(느리다).
        /// </summary>
        private void PreloadSiblingAssemblies()
        {
            foreach (var path in Directory.GetFiles(_mainDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    // 프레임워크 어셈블리는 건너뛴다: 자체 포함(self-contained) 출력에는 PresentationFramework 같은 복사본이 들어 있는데,
                    // 그것을 이 컨텍스트에 또 로드하면 호스트의 WPF와 형식 ID가 달라져 컨트롤이 깨진다. 프레임워크는 기본 컨텍스트가 책임진다.
                    var name = AssemblyName.GetAssemblyName(path);
                    if (IsFrameworkAssembly(name.Name))
                    {
                        continue;
                    }
                    else if (_hasDepsFile && _resolver.ResolveAssemblyToPath(name) == null)
                    {
                        // deps.json이 있는 출력인데 목록에 없는 DLL(참조되지 않는 파일): 로드하면 그 DLL의 로드 시점 코드(모듈 초기화 등)가 실행되므로 건드리지 않는다.
                        continue;
                    }
                    else
                    {
                        // 앱/라이브러리 어셈블리: 로드.
                    }
                    LoadFromAssemblyPath(path);
                }
                catch (Exception ex) when (ex is IOException or BadImageFormatException or FileLoadException or UnauthorizedAccessException)
                {
                    // 네이티브 DLL이거나 지금 쓸 수 없는 파일: 건너뛴다.
                }
            }
        }

        /// <summary>호스트가 쓰는 공유 프레임워크 폴더(런타임 + WPF). 사용자 출력에 같은 이름의 복사본이 있어도 이 컨텍스트에는 로드하지 않는다.</summary>
        private static readonly string[] HostFrameworkDirectories =
        {
            System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
            Path.GetDirectoryName(typeof(System.Windows.Window).Assembly.Location) ?? string.Empty,
        };

        /// <summary>호스트의 공유 프레임워크에 같은 이름의 어셈블리가 있는가.</summary>
        private static bool IsHostFrameworkAssembly(string? simpleName)
            => !string.IsNullOrEmpty(simpleName)
                && ProjectLocator.IsSafeAssemblyName(simpleName)
                && HostFrameworkDirectories.Any(dir => dir.Length > 0 && File.Exists(Path.Combine(dir, simpleName + ".dll")));

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (IsHostFrameworkAssembly(assemblyName.Name))
            {
                // 자체 포함(self-contained) 출력에는 프레임워크 복사본이 들어 있다. 그것을 이 컨텍스트에 또 로드하면 형식 ID가 갈라져
                // 라이브러리(예: WPF UI)의 `XmlnsDefinition` 특성이 호스트의 WPF에서 보이지 않는다. 호스트(기본 컨텍스트)의 것을 쓴다.
                try
                {
                    return AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(assemblyName.Name!));
                }
                catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
                {
                    // 기본 컨텍스트가 못 찾으면 아래 일반 경로로.
                }
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path == null ? null : LoadFromAssemblyPath(path); // null이면 기본 컨텍스트(프레임워크)가 처리한다.
        }
    }

    /// <summary>호스트의 기본 로드 컨텍스트(공유 프레임워크)가 책임지는 어셈블리 이름 접두사.</summary>
    private static readonly string[] FrameworkAssemblyPrefixes =
    {
        "System.", "Microsoft.Win32.", "Microsoft.CSharp", "Microsoft.VisualBasic", "Presentation", "WindowsBase", "ReachFramework", "UIAutomation", "netstandard", "mscorlib",
    };

    /// <summary>이름이 프레임워크 어셈블리인가(호스트가 이미 가진 것이거나 그 공유 프레임워크 소속이라 사용자 컨텍스트에 따로 로드하면 안 된다).</summary>
    private static bool IsFrameworkAssembly(string? simpleName)
    {
        if (string.IsNullOrEmpty(simpleName))
        {
            return false;
        }
        else
        {
            return FrameworkAssemblyPrefixes.Any(prefix => simpleName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                || AssemblyLoadContext.Default.Assemblies.Any(a => string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// 산출물을 로드한다(이미 같은 버전이 로드되어 있으면 재사용). 실패는 예외 대신 Failure 문자열로 돌려준다(사용자 코드/파일 상태 문제라서).
    /// </summary>
    public UserAssemblyLoad Ensure(ProjectArtifact artifact)
    {
        lock (_gate)
        {
            FileInfo info;
            try
            {
                info = new FileInfo(artifact.AssemblyPath);
                if (!info.Exists)
                {
                    return new UserAssemblyLoad(null, false, 0, "산출물 파일이 없습니다.");
                }
                else
                {
                    // 파일 있음.
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new UserAssemblyLoad(null, false, 0, ex.Message);
            }

            if (_projects.TryGetValue(artifact.ProjectFile, out var existing))
            {
                if (existing.SourcePath == artifact.AssemblyPath
                    && existing.SourceWriteTimeUtc == info.LastWriteTimeUtc && existing.SourceLength == info.Length)
                {
                    return new UserAssemblyLoad(existing.Assembly, false, 0, null);
                }
                else
                {
                    // 산출물이 바뀜: 이전 컨텍스트를 버리고 다시 로드한다.
                    Unload(artifact.ProjectFile, existing);
                }
            }
            else
            {
                // 처음 로드.
            }

            var started = System.Diagnostics.Stopwatch.StartNew();
            string? tempDirectory = null;
            try
            {
                tempDirectory = CopyOutputDirectory(Path.GetDirectoryName(artifact.AssemblyPath)!);
                var copiedPath = Path.Combine(tempDirectory, Path.GetFileName(artifact.AssemblyPath));
                var context = new UserLoadContext(copiedPath);
                var assembly = context.LoadFromAssemblyPath(copiedPath);
                // 의존성은 여기서 미리 로드하지 않는다(Roslyn 같은 큰 라이브러리까지 불러 첫 렌더가 수 초 느려진다). 필요할 때 Resolving/Load가 로드하고,
                // 참조 목록 방식의 스키마 컨텍스트가 필요할 때만 GetContextAssemblies가 미리 로드한다.
                _projects[artifact.ProjectFile] = new LoadedProject
                {
                    SourcePath = artifact.AssemblyPath,
                    SourceWriteTimeUtc = info.LastWriteTimeUtc,
                    SourceLength = info.Length,
                    TempDirectory = tempDirectory,
                    Context = context,
                    Assembly = assembly,
                };
                if (_baselinePrivateBytes == 0)
                {
                    _baselinePrivateBytes = CurrentPrivateBytes(); // 처음 로드 직후를 기준선으로 삼는다.
                }
                else
                {
                    // 기준선은 이미 있다.
                }
                return new UserAssemblyLoad(assembly, true, started.ElapsedMilliseconds, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or FileLoadException)
            {
                TryDeleteDirectory(tempDirectory);
                return new UserAssemblyLoad(null, false, started.ElapsedMilliseconds, $"{ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 처음 사용자 어셈블리를 로드한 직후의 프로세스 비공개 메모리(바이트). 재시작 판단의 기준선이다 — 수집 불가 컨텍스트가 다시 불러올 때마다 쌓는 **증가량**을 보려는 것이다.
    /// 프로젝트 자체가 커서 늘 1GB를 넘는 경우에도(절대 크기 기준이면 응답마다 재시작을 요청한다) 증가량이 작으면 재시작하지 않는다. 0이면 아직 기준을 잡지 않았다.
    /// </summary>
    private long _baselinePrivateBytes;

    /// <summary>기준선보다 이만큼(바이트) 더 늘면 호스트가 재시작을 요청한다(재빌드 1회당 약 7MB 측정 → 대략 100회분).</summary>
    private const long RecycleGrowthBytes = 768L * 1024 * 1024;

    /// <summary>현재 프로세스 비공개 메모리(바이트). Process 객체는 핸들을 쥐므로 바로 해제한다.</summary>
    private static long CurrentPrivateBytes()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return process.PrivateMemorySize64;
    }

    /// <summary>
    /// 호스트 재시작을 권할 때 true: 사용자 어셈블리를 로드한 적이 있고 프로세스 비공개 메모리가 기준선보다 <see cref="RecycleGrowthBytes"/> 넘게 늘었다.
    /// 호출자(렌더 응답 작성)가 응답에 `recycle: true`를 실으면 확장이 대기 요청이 없을 때 호스트를 새로 띄운다.
    /// </summary>
    public bool ShouldRecycle()
    {
        lock (_gate)
        {
            return _projects.Count > 0 && _baselinePrivateBytes > 0 && CurrentPrivateBytes() - _baselinePrivateBytes > RecycleGrowthBytes;
        }
    }

    /// <summary>
    /// 이름이 같은 어셈블리 중 **현재 로드된 프로젝트의 컨텍스트에 있는 것**을 돌려준다(없으면 null). 다시 불러온 뒤에는 이전 복사본이 AppDomain에 남아 있으므로
    /// `AppDomain.GetAssemblies()`의 첫 일치를 쓰면 낡은 복사본을 집는다 — 라이브러리 훅 같은 호출자는 이것을 써야 한다.
    /// </summary>
    public Assembly? FindCurrentAssembly(string simpleName)
    {
        lock (_gate)
        {
            foreach (var project in _projects.Values)
            {
                var found = project.Context.Assemblies.FirstOrDefault(a => string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));
                if (found != null)
                {
                    return found;
                }
                else
                {
                    // 이 프로젝트에는 없음: 다음 프로젝트 확인.
                }
            }
            return null;
        }
    }

    /// <summary>
    /// 로드된 모든 프로젝트의 의존 어셈블리를 지금 전부 로드한다(이미 로드된 것은 그대로).
    /// `xmlns="http://..."`처럼 `XmlnsDefinition` 특성으로 매핑된 URI 네임스페이스는 WPF 스키마가 **이미 로드된** 어셈블리에서만 찾으므로,
    /// 그런 xmlns가 나왔을 때만 호출한다(평소에는 첫 렌더를 느리게 하므로 호출하지 않는다).
    /// </summary>
    public void PreloadAllDependencies()
    {
        lock (_gate)
        {
            foreach (var project in _projects.Values)
            {
                project.Context.PreloadDependencies(project.Assembly);
            }
        }
    }

    /// <summary>
    /// XAML이 `xmlns="clr-namespace:...;assembly=이름"`으로 참조한 어셈블리를 로드된 프로젝트 컨텍스트에서 미리 로드한다(이미 로드됐으면 그대로).
    /// WPF 스키마 컨텍스트는 이미 로드된 어셈블리 중에서 이름으로 찾으므로, 참조되는 시점에 로드해 두어야 한다.
    /// 프로젝트가 가져오지 않는 이름(프레임워크 등)이나 로드 실패는 무시한다(해석 실패는 이후 XAML 오류로 보고된다).
    /// </summary>
    public void EnsureReferencedAssemblyLoaded(string assemblyName)
    {
        if (!ProjectLocator.IsSafeAssemblyName(assemblyName))
        {
            return; // XAML이 준 문자열이다: 경로 구분자 등이 든 이름으로 로드 경로를 바꾸지 못하게 한다.
        }
        else
        {
            // 안전한 이름: 로드 시도.
        }

        lock (_gate)
        {
            foreach (var project in _projects.Values)
            {
                try
                {
                    project.Context.LoadFromAssemblyName(new AssemblyName(assemblyName));
                    return;
                }
                catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException or ArgumentException)
                {
                    // 이 프로젝트의 컨텍스트에는 없음(또는 이름이 어셈블리 이름으로 해석되지 않음): 다음 프로젝트 확인.
                }
            }
        }
    }

    /// <summary>
    /// 지정한 사용자 어셈블리가 속한 컨텍스트의 어셈블리 전체(자신 + 미리 로드한 의존성)를 돌려준다.
    /// XamlSchemaContext의 참조 목록에 넣어 "같은 이름의 다른 복사본"과 섞이지 않게 하는 데 쓴다.
    /// </summary>
    public static IReadOnlyList<Assembly> GetContextAssemblies(Assembly userAssembly)
    {
        var context = AssemblyLoadContext.GetLoadContext(userAssembly);
        if (context is UserLoadContext userContext)
        {
            userContext.PreloadDependencies(userAssembly); // 목록에 들어 있어야 하므로 지연해 둔 로드를 지금 한다.
        }
        else
        {
            // 사용자 컨텍스트가 아님: 있는 그대로 돌려준다.
        }
        return context == null ? new[] { userAssembly } : context.Assemblies.ToList();
    }

    /// <summary>
    /// 기본 컨텍스트가 못 찾은 어셈블리 요청(XamlSchemaContext의 `assembly=이름`)을 로드된 사용자 컨텍스트에서 찾아 돌려준다.
    /// 일치하는 것이 없으면 null(다른 해석기가 이어서 시도). 사용자 어셈블리의 의존 라이브러리도 그 컨텍스트가 찾는다.
    /// </summary>
    private Assembly? OnDefaultResolving(AssemblyLoadContext _, AssemblyName name)
    {
        if (!_resolutionAllowedOnThisThread)
        {
            return null; // 신뢰된 렌더 구간이 아니다(예: 신뢰하지 않은 문서의 렌더): 사용자 어셈블리를 내주지 않는다.
        }
        else
        {
            // 신뢰된 렌더 구간: 아래에서 찾는다.
        }

        lock (_gate)
        {
            foreach (var project in _projects.Values)
            {
                if (string.Equals(project.Assembly.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return project.Assembly;
                }
                else
                {
                    try
                    {
                        var dependency = project.Context.LoadFromAssemblyName(name);
                        if (dependency != null)
                        {
                            return dependency;
                        }
                        else
                        {
                            // 이 컨텍스트에는 없음.
                        }
                    }
                    catch (FileNotFoundException)
                    {
                        // 이 컨텍스트에는 없음: 다음 프로젝트 확인.
                    }
                }
            }
            return null;
        }
    }

    private void Unload(string projectFile, LoadedProject project)
    {
        _projects.Remove(projectFile);
        if (project.Context.IsCollectible)
        {
            project.Context.Unload();
        }
        else
        {
            // 수집 불가 컨텍스트는 Unload할 수 없다: 이전 어셈블리는 프로세스가 끝날 때까지 남고 그 DLL(임시 복사본)도 잠겨 있다.
        }

        if (!TryDeleteDirectory(project.TempDirectory))
        {
            // 잠겨서 못 지웠다. 이 폴더를 잊지 않고 기억해 종료 때 다시 시도하고(그래도 잠겨 있으면 다음 호스트 시작 때의 정리가 지운다).
            _abandonedTempDirectories.Add(project.TempDirectory);
        }
        else
        {
            // 지웠다.
        }
    }

    /// <summary>위성(지역화 리소스) 어셈블리 파일 이름 끝.</summary>
    private const string SatelliteAssemblySuffix = ".resources.dll";

    /// <summary>
    /// 산출물 폴더를 새 임시 폴더로 복사한다. 원본은 읽기만 한다.
    /// deps.json이 있는 출력(.NET)은 의존성 경로가 하위 폴더(runtimes 등)를 가리킬 수 있어 하위 폴더까지 전부 복사한다.
    /// deps.json이 없는 출력(.NET Framework)은 어셈블리를 폴더 바로 아래에서만 찾으므로, 폴더 바로 아래 파일과 위성 어셈블리만 복사한다.
    /// 이렇게 하지 않으면 앱이 같이 싣는 런타임/스크립트(예: Python 런타임 100MB)까지 복사해 첫 렌더가 느려진다.
    /// </summary>
    private static string CopyOutputDirectory(string sourceDirectory)
    {
        var target = Path.Combine(Path.GetTempPath(), TempDirectoryPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        var hasDepsFile = Directory.GetFiles(sourceDirectory, "*.deps.json", SearchOption.TopDirectoryOnly).Length > 0;
        if (hasDepsFile)
        {
            foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(sourceDirectory, directory)));
            }
            foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                File.Copy(file, Path.Combine(target, Path.GetRelativePath(sourceDirectory, file)), overwrite: true);
            }
        }
        else
        {
            foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
            }
            foreach (var file in Directory.GetFiles(sourceDirectory, "*" + SatelliteAssemblySuffix, SearchOption.AllDirectories))
            {
                var destination = Path.Combine(target, Path.GetRelativePath(sourceDirectory, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: true);
            }
        }
        return target;
    }

    /// <summary>
    /// 종료 때 임시 폴더를 지운다(현재 프로젝트 것과, 다시 불러오며 버린 것 중 아직 남은 것). 단, 수집 불가 컨텍스트의 DLL은 프로세스가 끝날 때까지 매핑되어 잠겨 있어
    /// 여기서 지우지 못하는 것이 보통이다 — 그 폴더는 다음 호스트가 시작할 때 <see cref="SweepStaleTempDirectories"/>가 정리한다.
    /// </summary>
    private void DeleteAllTempDirectories()
    {
        lock (_gate)
        {
            foreach (var project in _projects.Values)
            {
                TryDeleteDirectory(project.TempDirectory);
            }
            foreach (var abandoned in _abandonedTempDirectories.ToList())
            {
                if (TryDeleteDirectory(abandoned))
                {
                    _abandonedTempDirectories.Remove(abandoned);
                }
                else
                {
                    // 아직 잠김: 다음 시작 때의 정리에 맡긴다.
                }
            }
        }
    }

    /// <summary>임시 폴더 이름 접두사. 정리 대상을 이 호스트가 만든 것으로 한정하는 데 쓴다.</summary>
    private const string TempDirectoryPrefix = "xamlviewer-user-";

    /// <summary>이 시간(분)보다 오래된 임시 복사본만 시작 때 지운다(막 시작한 다른 호스트의 폴더를 건드리지 않는다).</summary>
    private const int StaleTempDirectoryAgeMinutes = 10;

    /// <summary>
    /// 이전 호스트 프로세스가 지우지 못하고 남긴 `xamlviewer-user-*` 임시 복사본을 지운다. 수집 불가 컨텍스트는 DLL을 프로세스가 끝날 때까지 잠가
    /// 종료 시 삭제가 실패하기 때문에, 다음 시작 때 정리하지 않으면 재빌드마다 수십 MB가 %TEMP%에 계속 쌓인다.
    /// 아직 실행 중인 다른 호스트가 쓰는 폴더는 DLL이 잠겨 삭제가 실패하므로(조용히 건너뜀) 안전하다.
    /// </summary>
    private static void SweepStaleTempDirectories()
    {
        try
        {
            var threshold = DateTime.UtcNow.AddMinutes(-StaleTempDirectoryAgeMinutes);
            foreach (var directory in Directory.GetDirectories(Path.GetTempPath(), TempDirectoryPrefix + "*"))
            {
                if (Directory.GetCreationTimeUtc(directory) < threshold)
                {
                    TryDeleteDirectory(directory);
                }
                else
                {
                    // 최근 것: 다른 호스트가 쓰는 중일 수 있어 건드리지 않는다.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 임시 폴더를 훑지 못함: 정리는 선택 사항이라 무시한다.
        }
    }

    /// <summary>삭제를 시도한다. 지웠거나 없으면 true, 잠겨 있어 실패하면 false.</summary>
    private static bool TryDeleteDirectory(string? directory)
    {
        if (directory == null)
        {
            return true;
        }
        else
        {
            // 삭제 시도.
        }

        try
        {
            Directory.Delete(directory, recursive: true);
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true; // 이미 없다.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // 아직 잠겨 있다(매핑된 DLL). 임시 폴더라 치명적이지 않다.
        }
    }
}
