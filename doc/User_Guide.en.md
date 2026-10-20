# WPF XAML Live Preview — User Guide

[한국어 사용자 가이드](./User_Guide.md) · English

> Purpose of this extension: **develop WPF projects in VS Code with the .NET 10 SDK, without Visual Studio**, and see the real WPF rendering of your XAML while you edit it. Windows only.

## 1. What it does and what it does not

| Task | Handled by this extension? |
|---|---|
| **Real WPF rendering preview** while editing XAML (refreshes automatically) | **Yes** |
| Show XAML errors in the Problems panel with line/column | **Yes** |
| Click an element in the preview → jump to its XAML line; move the editor cursor → highlight the element | **Yes** |
| Rendering that reflects the project's user controls, `App.xaml` resources and merged dictionaries | **Yes** (conditions in section 5) |
| Create, build, run and debug projects | No — use the `dotnet` CLI and a C# extension (C# Dev Kit or C#) (section 2) |
| **Drag-and-drop designer** (toolbox, property window) | **No (not supported)** — edit the XAML directly and check the preview |
| XAML completion / IntelliSense | **No** — use another XAML extension alongside this one |
| Animations / video / live data binding behavior | No — only a **static single frame** is drawn |

## 2. Development flow (from start to finish)

You need: **.NET SDK 10**, **.NET 10 Desktop Runtime** (runs the preview host), VS Code and this extension. Visual Studio is not needed.

1. Create a project: `dotnet new wpf -n MyApp -o MyApp --framework net10.0`, then open the folder in VS Code.
2. Open `MainWindow.xaml` and run **WPF XAML: Open Preview** from the Command Palette (`Ctrl+Shift+P`). The preview opens beside the editor. By default, simply opening a `.xaml` file or switching to another `.xaml` tab opens the preview automatically (an existing panel is reused and refreshed). To turn this off, set `wpfXamlViewer.autoOpenPreview` to `false`.
3. Edit the XAML. When typing pauses, the preview is redrawn automatically (the wait adapts to how long the last render took, 100–300 ms). If there is an error it appears in the Problems panel and the preview keeps the last good image.
4. To draw your own controls (`local:MyControl`) for real, **build** the project once (`dotnet build`), **trust the folder** and **allow the project's code to run** (section 5).
5. Run/debug the app with `dotnet run` or the C# extension's debugger (F5). If you change code (`.cs`), run `dotnet build` again so the preview picks up the change in your user controls.

## 3. Commands

| Command ID | Command Palette name | Description |
|---|---|---|
| `wpfXamlViewer.openPreview` | WPF XAML: Open Preview | Previews the active `.xaml` file in a panel beside the editor. Only shows a hint if the file is not `.xaml`. |
| `wpfXamlViewer.buildProject` | WPF XAML: Build Project | In the Explorer, **right-click** a `.csproj`/`.sln`/`.slnx` → **Build Project**. Runs `dotnet build` for that project only (see "Building and cleaning projects" below). |
| `wpfXamlViewer.switchLanguage` | WPF XAML: Switch Language (한국어 / English) | Choose the extension's display language: **Automatic / 한국어 / English** (saved to the setting `wpfXamlViewer.language`). The status bar, notifications, preview toolbar and host warnings change immediately. |
| `wpfXamlViewer.refreshPreview` | WPF XAML: Refresh Preview | Renders the open preview again. Same as the **↻ Refresh** button at the left end of the preview toolbar. It also retries a failed automatic build and the project-code approval prompt from scratch. |
| `wpfXamlViewer.resetProjectConsent` | WPF XAML: Reset Project Code Approvals | Clears every saved "allow this project's code to run" approval. You are asked again on the next preview (section 5). |
| `wpfXamlViewer.rebuildProject` | WPF XAML: Rebuild Project | Right-click a `.csproj`. Runs `dotnet build --no-incremental`. |
| `wpfXamlViewer.cleanProject` | WPF XAML: Clean Project | Right-click a `.csproj`/`.sln`. Runs `dotnet clean`. |
| `wpfXamlViewer.restoreProject` | WPF XAML: Restore Project | Right-click a `.csproj`. Runs `dotnet restore`. |
| `wpfXamlViewer.buildProjectDebug` | WPF XAML: Build - Debug | **Right-click → WPF XAML: Build with Configuration → Build - Debug**. `dotnet build -c <configuration>`. Always uses Debug, regardless of the `buildConfiguration` setting. |
| `wpfXamlViewer.buildProjectRelease` | WPF XAML: Build - Release | **Right-click → WPF XAML: Build with Configuration → Build - Release**. `dotnet build -c <configuration>`. Always uses Release, regardless of the `buildConfiguration` setting. |
| `wpfXamlViewer.rebuildProjectDebug` | WPF XAML: Rebuild - Debug | **Right-click → WPF XAML: Build with Configuration → Rebuild - Debug**. `dotnet build -c <configuration> --no-incremental` (`.csproj` only). Always Debug. |
| `wpfXamlViewer.rebuildProjectRelease` | WPF XAML: Rebuild - Release | **Right-click → WPF XAML: Build with Configuration → Rebuild - Release**. `dotnet build -c <configuration> --no-incremental` (`.csproj` only). Always Release. |
| `wpfXamlViewer.cleanProjectDebug` | WPF XAML: Clean - Debug | **Right-click → WPF XAML: Build with Configuration → Clean - Debug**. `dotnet clean -c <configuration>`. Always Debug. |
| `wpfXamlViewer.cleanProjectRelease` | WPF XAML: Clean - Release | **Right-click → WPF XAML: Build with Configuration → Clean - Release**. `dotnet clean -c <configuration>`. Always Release. |
| `wpfXamlViewer.buildXamlProject` | WPF XAML: Build This XAML's Project | Right-click in a `.xaml` editor (or use the Command Palette). Walks up from that file and builds the nearest `.csproj`. |

### Settings

| Setting | Default | Description |
|---|---|---|
| `wpfXamlViewer.language` | `auto` | Language of messages, status bar, preview toolbar and host warnings (`auto`/`ko`/`en`). `auto` follows the VS Code display language (VS Code uses the Windows language by default); anything other than Korean is shown in English. Menu and command titles follow the VS Code display language (**Configure Display Language**). Logs are not translated. |
| `wpfXamlViewer.autoOpenPreview` | `true` | Open the preview automatically when a `.xaml` file is opened. |
| `wpfXamlViewer.autoBuildOnPreview` | `true` | If the preview fails because the project has no build output (`bin`), build that project automatically and render again. If the build fails, the preview says "to preview this XAML the project must be built first, but the build failed". Works only for approved projects in trusted folders. |
| `wpfXamlViewer.buildConfiguration` | `Debug` | Configuration (`Debug`/`Release`) used by Build / Rebuild / Clean. |
| `wpfXamlViewer.buildTargetFramework` | (empty) | When right-click building a multi-target (`<TargetFrameworks>`) `.csproj`, build only this TFM (e.g. `net10.0-windows`). Applied with `-f` **only if** that `.csproj` declares the TFM; never used for solutions or single-target projects (a TFM that does not exist is a build error). Not applied to projects that declare the TFM in `Directory.Build.props`. |
| `wpfXamlViewer.buildExtraArgs` | `[]` | Extra arguments appended as-is to the `dotnet` command (e.g. `["-p:RunAnalyzers=false"]`). Up to 20 items, 200 characters each. Not read in untrusted folders. |

### Building and cleaning projects
- **To pick Debug/Release each time**, right-click a `.csproj`/`.sln`/`.slnx`/`.slnf` → **WPF XAML: Build with Configuration** submenu and choose Build / Rebuild (`.csproj` only) / Clean in Debug or Release. The single entries above (Build Project, etc.) follow the `buildConfiguration` setting. The task name shows the configuration (e.g. `Build (Release): App.csproj`).
- Right-clicking a `.csproj` in the Explorer shows **WPF XAML: Build Project / Rebuild Project / Clean Project / Restore Project** (`.sln`/`.slnx`/`.slnf` get Build and Clean). Choosing one runs `dotnet` for **that project only**; the output appears in the Terminal panel and compiler errors are added to the **Problems panel** (click to jump to the line).
- **Referenced projects are built automatically.** MSBuild builds the projects connected by `ProjectReference` first, so you do not need to pick them (the reverse — projects that reference this one — is not automatic).
- **Why building a whole solution can be slow**: a `.sln` builds libraries for every target framework (e.g. net462 to net10, netstandard). Usually build only the project you need (right-click build builds just the one TFM you need), reduce the TFM to one with `buildTargetFramework`, and if analyzers are heavy add `-p:RunAnalyzers=false` to `buildExtraArgs`. A `.slnf` (solution filter) can group only the projects you need. Measured (wpfui `Wpf.Ui`, `--no-incremental`): all TFMs (6) 58 s → one TFM with `-f` 18 s → analyzers off 27 s → both 8 s.
- `dotnet` (.NET SDK) must be on the PATH. A build runs the project's code, so **in untrusted folders the menus are hidden and the commands do not run**.
- The user-control preview reads the build output (`bin`). When a build finishes the preview does not redraw by itself; it reads the new output on the **next render** (edit the XAML once, or press Refresh).

## 4. The preview view

- **Toolbar**: **↻ Refresh** (render again; see the command above), zoom out/in, **Fit**, **100%**, background, W/H size, **Apply**/**Auto**.
- **Zoom**: −/+ in the toolbar, **Fit** (fit to window), **100%**. `Ctrl+mouse wheel` zooms around the pointer. At 200% and above the pixels are enlarged as they are.
- **Pan**: drag the image with the mouse. Releasing it almost without moving counts as a click.
- **Background**: Checkerboard (to check transparency) / White / Dark.
- **Render size**: enter W/H and press **Apply**. Leave empty or press **Auto** to clear. An explicit `Width`/`Height` on the root element or `d:DesignWidth`/`d:DesignHeight` takes precedence (priority: explicit root size → design size → requested size → content size).
- **Click → code**: click an element in the image and the editor cursor moves to that element's start tag and the element is highlighted. For overlapping elements the innermost / topmost drawn one is chosen. Elements created inside a template are treated as the control itself.
- **Code → preview**: move the editor cursor and the innermost element containing the cursor is highlighted.
- **Status bar**: `Rendering XAML` / `XAML OK` / `XAML OK (N warnings)` / `XAML error`. Hover over the warnings to see the list.

## 5. User controls and trust (Workspace Trust)

If your XAML contains the project's own controls (`xmlns:local="clr-namespace:MyApp"`) it is drawn in one of two ways.

| Mode | Conditions | Result |
|---|---|---|
| **Tier 1 (real render)** | The folder is **trusted**, the XAML and its nearest `.csproj` are **inside an opened workspace folder**, you have **allowed that project's code to run** (below), and the `.csproj` has build output `bin/**/<AssemblyName>.dll` (or the app project's `<AssemblyName>.exe` if there is no dll) | The project DLL is loaded and the controls are really created and drawn |
| **Tier 0 (placeholders)** | The folder is not trusted, the project is not allowed yet, there is no build output, or loading failed | User controls are shown as a red box with the type name; the rest renders normally. Where possible the rest of the XAML still draws (see "What is replaced in placeholder mode" below) |

- If there are several outputs (per configuration/TFM), the **most recently modified DLL** is used. If there is no output, the project is **built automatically** according to the setting `autoBuildOnPreview` (on by default) and then drawn again (approved projects only).
- The DLL is copied to a temporary folder before loading, so **the original is not locked** (you can run `dotnet build` while previewing). When the DLL changes it is reloaded automatically (the host restarts so the new DLL is used cleanly).
- If a control's constructor throws, only that control becomes an **orange error box** (exception summary) and the rest is drawn.
- Code inside a control that branches on `DesignerProperties.GetIsInDesignMode` runs in design mode (`true`).
- The `Application.Resources` of `App.xaml` (including merged dictionaries) are applied automatically below the document's own resources, so `{StaticResource}` resolves.

### Security — please read
Tier 1 **runs the project's user code (constructors, etc.) on this PC as it is**. There is no sandbox. It is therefore enabled only when **all** of the following hold.
1. **A trusted folder** (VS Code Workspace Trust). A window with a file opened but no folder is treated as "trusted" by VS Code by default, but project code does not run because of item 2.
2. **The XAML and the `.csproj` are inside an opened workspace folder** (if you opened only a subfolder of a repository, the project in a parent folder is not run). The host keeps the same boundary and never looks for a `.csproj`/`bin` outside it.
3. **Per-project approval**: the first time you preview, you are asked once "May this project's code run?" and the answer is remembered in the workspace. Even in a trusted folder, **a DLL committed to the repository's `bin/` can run the moment you open a .xaml, without any build**. Allow only projects whose origin you know. Clear the records with the command **WPF XAML: Reset Project Code Approvals**.

In untrusted folders, or for projects you have not allowed, no user code is loaded (Tier 0). Controls whose constructors access files/network/databases still do so while previewing, and may be slow or fail to draw. If a request does not finish within the time limit (30 seconds by default; a project with a large output folder takes a few seconds the first time it is loaded) the host is forcibly stopped and restarted on the next edit.

**What is guaranteed even in untrusted folders** (verified by the automated tests `RD07`–`RD09`, `S01`–`S04`, `T02`, `T09b`, `G01`–`G11`):
- **XAML declarations alone cannot run code**: an allow-list check runs before parsing. Only WPF types and basic value types (strings/numbers/enums/collections, etc.) can be created. `ObjectDataProvider`, `XmlDataProvider`, `x:FactoryMethod`, `{x:Static}` of non-WPF types, and `Source`/`FontFamily`/`Cursor` etc. that read a file/network location (`file:`, UNC, drive paths, `http(s):`) are rejected with an `UntrustedBlocked` error. Trust the folder to render them. This is not a complete sandbox.
- `ResourceDictionary Source` and the `App.xaml` search read only files inside the **project boundary** (the folder containing the nearest `.csproj`/`.sln`/`.slnx`/`.git`, or the document folder if none). Absolute paths (`C:\…`), `..\` that leave the boundary, and paths through junctions/symbolic links are not read, even inside the boundary (warning `DictionaryUnavailable`).
- No project DLL is loaded (Tier 0), and neither the `Application.Resources` raised by an earlier trusted render nor the name resolution of loaded user assemblies can be used (defense in depth for trust changing within one process).
- The `AssemblyName` in the project file (`.csproj`) and assembly names supplied by XAML are not used if they contain dangerous characters (quotes, angle brackets, `;`, path separators, etc.), and the project file's DTD is not processed.

**Network**: the extension itself sends nothing over the network. However, `dotnet` run by an automatic build may do NuGet restore (nuget.org, etc.) and send .NET CLI telemetry (turn it off with `DOTNET_CLI_TELEMETRY_OPTOUT=1`), and Tier 1 user code can do anything.

**Remaining risk (trusted folders)**: in a trusted folder the project's DLL code runs on this PC as it is (no sandbox). When preloading, **all** managed DLLs in the output folder are loaded, so load-time code in DLLs that the project does not even reference may run. Trust only folders you created or reviewed.

### What is replaced in placeholder mode (Tier 0)
When the project DLL is not loaded (not allowed yet, not built, or untrusted), a single reference to a type or resource that cannot be created would normally make the whole preview fail. These common patterns are replaced or removed **only at that spot**, the rest is drawn, and the change is reported as a warning:

- Converters: `Converter={StaticResource UserConverter}`, `MultiBinding Converter="{StaticResource …}"`, `<Binding.Converter><local:Conv/></Binding.Converter>`, `<MultiBinding.Converter>` (the whole property element containing the MultiBinding is removed)
- Attributes whose value is a resource of a user type (`ItemTemplateSelector`, a user brush, etc.) and an `ItemsPanelTemplate` that would hold a placeholder
- Custom markup extensions (`{local:Loc Title}`, `Converter={local:Conv}`) → `{x:Null}` (or the attribute is removed); the element form `<TextBlock.Text><local:Loc Key="x"/></TextBlock.Text>` → that property element is removed
- `{x:Static}` and attached properties of user types, `x:Array` of a user type
- Controls and attached properties of external libraries (WPF UI, Blend behaviors such as `i:Interaction.Triggers`, …) — only for XAML that belongs to a project; a typo in a standalone XAML without a project is still reported as an error
- `{StaticResource MissingKey}` (e.g. a key from a library theme dictionary): only that attribute is dropped and rendering continues

Once you build the project and allow its code to run (Tier 1), everything is drawn with the real controls.

## 6. Document conversion rules (drawing without code-behind)

The preview is not a compiled app, so the document is **cleaned up before it is read**. What changed is reported as warnings (status bar).

| Original | Handling | Warning code |
|---|---|---|
| `x:Class`, `x:Subclass`, `x:ClassModifier`, `x:FieldModifier` | Removed | `RemovedClassAttribute` |
| Event-handler attributes such as `Click="..."` | Removed | `RemovedEventHandler` |
| `x:Code` blocks | Removed | `RemovedCodeBlock` |
| A user type that cannot be resolved | Placeholder box | `PlaceholderUsed` (+ reason: `ProjectTier0`) |
| Exception while constructing a user control | Error box | `UserControlFailed` |
| `Style`/`ControlTemplate`/`DataTemplate` whose `TargetType`/`DataType` is an unresolvable type (Tier 0) | An empty definition with the same key if it has `x:Key`; dictionary entries without a key are removed | `PlaceholderUsed` |
| `{x:Type local:Foo}` inside an attribute value (unresolvable, Tier 0) | Replaced by `{x:Type FrameworkElement}` | `PlaceholderUsed` |
| An element inside a merged dictionary / `App.xaml` resources fails to parse | Only that element is skipped and rendering continues (errors in the document body are not skipped) | `InlinedElementSkipped` |
| `Setter` with `Value="pack://application:..."` (app icon, etc.) | Setter removed (no running app exists in the preview to load it) | `RemovedApplicationResource` |
| A document-relative `Icon="x.ico"` on `Window`-like elements | `Icon` attribute removed (the window icon is not visible in the preview) | `RemovedApplicationResource` |
| `x:Shared` attributes such as `x:Shared="False"` | Removed (an error in text XAML, so treated as a shared resource) | `RemovedClassAttribute` |
| `ResourceDictionary Source="..."` | The file is read and inlined (empty dictionary if missing or circular) | `DictionaryUnavailable` |
| `Window` root | Only the content is drawn at `Width`/`Height`, without window chrome | — |
| `d:` / `mc:Ignorable` design-time attributes | Left as they are (runtime values are used; `d:DesignWidth/Height` is reflected in the size) | — |
| More than 5,000 elements | Click mapping is limited to 5,000 | `HitMapTruncated` |

Error line numbers always refer to the **original document**. Resources at network URIs (`http://...`) are not read.

## 7. Limitations (known)

- **Static render**: code-behind after load (logic after `InitializeComponent`, event handling, setting the binding DataContext, etc.) is not executed. Bindings are drawn without values.
- **No window chrome**: the title bar/border of a `Window` is not drawn. Window properties such as `SizeToContent` and `WindowStyle` are ignored.
- A user type that cannot be resolved in a place that needs a member value, such as **`{x:Static local:Foo.Bar}`**, is replaced by `{x:Null}` or the attribute is removed in Tier 0 (section 5). When the build output (`.dll`, or `.exe` for app projects) is loaded in a trusted folder (Tier 1) the types resolve. Tested with a WPF app project (.NET Framework 4.8).
- A large project may take a few seconds the **first time** it loads the output. The same host does not reload it until the output changes.
- A user-control placeholder (Tier 0) does not draw its child elements.
- Elements of size 0 (an empty `Border`, `Collapsed`) cannot be selected by clicking. Elements with an explicit `Uid` are excluded from click mapping.
- Even if the target project is net8/net9, it is loaded in the host (net10). Behavior differences specific to that TFM were not tested.
- Rendering 180 demo XAML files of well-known open-source control libraries (MaterialDesign, MahApps.Metro, WPF UI) separately outside an app, **172 (96%) draw without errors** (the remaining 8 are `App.xaml`/`ResourceDictionary` roots, which are not preview targets) (`doc/07_Third_Party_Control_Verification.md`). "No error" does not mean "the picture is exactly right" (parts that app code fills in at run time look empty). In trusted folders the project's `internal` types (controls, converters) are also created. A library's compiled theme dictionaries (`pack://…;component/…`) are read from the loaded assemblies, and in trusted folders the host creates an `Application` and applies the `App.xaml` resources. **Host memory can grow by about 7 MB each time you rebuild and reload** (Reload Window resets it).
- XAML inside a **shared project (`.projitems`)** uses the build output of the app project (`.csproj`) that imports it. If there is no such app project or it has not been built, it is drawn in Tier 0 (130 of 149 HandyControl demo files render without errors).
- Windows only. x64.

## 8. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Notification ".NET 10 Desktop Runtime is not installed, so the render host cannot start" | Install the .NET 10 **Desktop** Runtime (x64). The `Open .NET download page` button in the notification takes you there. Check: `dotnet --list-runtimes` must show `Microsoft.WindowsDesktop.App 10.x`. |
| "Could not find the render host (XamlRenderHost.exe)" | The installed extension contains the host. While developing, run `dotnet build host/XamlRenderHost.slnx` or set the environment variable `XAMLVIEWER_HOST_EXE` to the exe path (when set, **only that path** is used). |
| A user control shows as a red box | Look at the `ProjectTier0` reason in the status-bar warnings: folder not trusted → trust it; project not allowed → allow it (press **Refresh** to be asked again); no output → `dotnet build`. |
| A user control shows as an orange box | A constructor exception. The warning list has the exception summary. Fix the constructor to take design mode into account. |
| `{StaticResource X}` error | The resource must be in `App.xaml` or in the document/merged dictionaries (resources added from code-behind cannot be seen). In placeholder mode a missing key only drops that attribute with a warning. |
| The preview does not refresh | Check the Problems panel and the **WPF XAML Live Preview** output channel, then press **↻ Refresh**. If the host dies it restarts automatically on the next edit. |
| The "allow this project's code" question did not appear / was dismissed | Press **↻ Refresh** in the preview toolbar (or run **WPF XAML: Refresh Preview**) to be asked again. |

## 9. Logs

- Extension log: the **WPF XAML Live Preview** channel in the VS Code Output panel. Format `[LEVEL] E0xx message`.
- Host log: `logs/host.log` under the extension's global storage folder (rotated, 1 MB × 5 files; if rotation fails it keeps appending to the current file). Entries have `H0xx` IDs and the XAML body is never written.
- Main IDs: `E010` host started, `E011` timeout (kill), `E012` host exited/failed, `H020` user DLL loaded, `H022` user control exception, `H023` tier decision.
- Logs are not translated.
