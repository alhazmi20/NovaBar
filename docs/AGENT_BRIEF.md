> ## RENDER HARNESS READY
> Build: `scripts/build.sh <yourname> 'src/Widgets/<YourFolder>/*.cs'`, then (from a cwd under /mnt/c):
> ```
> cd /mnt/c/Users/alhaz/NovaBar && ./build/<name>/NovaBar.exe --home 'C:\Users\alhaz\NovaBar\home-<name>' --render 'C:\Users\alhaz\NovaBar\out\<name>.png' --widgets id1,id2 --panels --wait 3000
> ```
> Options: `--widgets a,b` | `--category "System"` | `--all` (default = widgets of the home's config.json, with zones); `--themes dark,white,glass,glasslight,midnight` (default all 5); `--panels` (also draws each panel card; OnPanelOpen is called before the wait); `--size S|M|L`; `--wait ms` (pumps OnTick/Post/Hub while waiting).
> Output: the PNG + `<png>.log` (header with error count + full stack traces per widget; exit code 2 if any error). Widgets that throw are drawn as a red box. Text inputs (`c.Input`) are drawn as a fake field showing the placeholder. Glass themes are drawn over a synthetic colourful wallpaper with an approximated acrylic layer.
> Also: `NovaBar.exe --list 'C:\...\widgets.json'` dumps registry metadata (id, name, category, refName, data, account, settings keys, panel size, refreshMs).
> If you want a widget instance with custom settings in the render, put it in `<home>\config.json` "widgets" (the harness uses the first config instance with the same id).
> **Host notes (live bar behaviour):** text inputs (`c.Input`) are real TextBoxes; Enter calls `onSubmit` and then **clears a single-line box** (multiline: Ctrl+Enter submits, text kept). To set/clear the text yourself, pass a *different* `initial` value. Placeholder = Windows cue banner. Unknown widget ids in config are skipped silently on the live bar. The bar repaints after `OnTick`, on `Invalidate()` (coalesced ≥16 ms) and on hover changes of a registered `Hit` region (regions drawn with `c.IsHover` but no `Hit` won't refresh on mouse move). Right-click always opens the bar menu; middle-click calls `OnClick(MouseButtons.Middle)`. When space is short in full style, center-zone tiles hide first, then left-zone tiles from the end; right zone is kept. One bar per monitor: your widget may have several live instances with the same `InstId` (one per monitor) — keep statics/Hub keys shared, don't assume a singleton.
> Host-only build (Core+Bar+App): `scripts/build.sh host 'src/_none_/*.cs'` (the no-glob form builds *everything*).


# NovaBar — shared brief for all agents

We are building **NovaBar**, a native Windows 11 dock/status bar (a feature-for-feature replica of lumabar.xyz’s widget set, with our own name — never use the name "lumabar" in UI text) plus a Settings program. **The #1 non-functional requirement from the user: be as light as possible on CPU and RAM.** That is why it is native C#/WinForms with custom GDI+ painting, not Electron.

Project root (WSL): `/home/alhaz/new-navbar`. Windows build output: `/mnt/c/Users/alhaz/NovaBar/build/<name>/NovaBar.exe` (Windows path `C:\Users\alhaz\NovaBar\build\<name>\NovaBar.exe`).

## Hard technical constraints
- Compiler is the **.NET Framework 4.x csc.exe that ships with Windows → C# 5 ONLY.** Not allowed: string interpolation `$"..."`, `?.`, `??=`, `nameof`, expression-bodied members `=>` on members/properties, auto-property initializers (`{ get; set; } = x`), `out var`, tuples, pattern matching, local functions, `using static`, default literal. Allowed: lambdas, LINQ, generics, `async/await`, `var`, optional params.
- Referenced assemblies (see `scripts/build.sh`): System, System.Core, System.Drawing, System.Windows.Forms, System.Web.Extensions (JSON), System.Security (DPAPI), System.Management (WMI), System.Xml(.Linq), Microsoft.CSharp, plus WinRT metadata: Windows.Foundation/Media/Devices/UI/Storage/System/ApplicationModel/Networking/Data winmd + System.Runtime.WindowsRuntime (for `await` on WinRT `IAsyncOperation` use `System.WindowsRuntimeSystemExtensions.AsTask(op)`). If you truly need another reference, add it to `scripts/build.sh` with a one-line comment (additive only).
- No NuGet, no extra processes kept running, no PowerShell helper processes in the steady state (a one-shot `Process.Start` for a user action like launching an app is fine).
- **Build**: `scripts/build.sh <yourname> 'src/Widgets/<YourFolder>/*.cs'` → compiles Core+Bar+App + your folder only (so other agents' half-written code can’t break you). Full build: `scripts/build.sh main`.
- **Do not edit `src/Core/*`** (frozen contract) unless you are the HOST agent. If you need a helper, put a static class in your own folder.
- **Naming**: every top-level type you create must start with your group prefix (given in your task) to avoid collisions in the single assembly. All code in `namespace NovaBar`.
- Read these core files first, they are the contract: `src/Core/Widget.cs` (Widget base, DrawCtx, Hub, WidgetRegistry, SettingDef), `src/Core/Ui.cs` (drawing helpers, Icons, formatting), `src/Core/Theme.cs` (5 themes), `src/Core/Config.cs` (AppConfig, Accounts, StateStore), `src/Core/Http.cs`, `src/Core/Native.cs`, `src/Core/Json.cs` (`J.*`).

## Widget rules
- One class per widget: `[Widget("id","Name","Category","Description", RefName="<exact lumabar name>", Account="<acct id>", Data=DataKind.Live|NeedsAccount|Sample)] public class XxxWidget : Widget`. RefName must match the `name` in `data/reference-widgets.json` exactly (it drives the parity checklist). Category must match the reference category.
- The HOST paints the tile background (rounded `T.Tile`, hover `T.TileHover`) and the panel card background (`T.PanelBg`, rounded, border). Your `Draw` paints only content inside `c.Bounds`; keep ~`c.S(10)` horizontal padding. `DrawPanel` paints content inside the card `c.Bounds` (use `c.PanelHeader(...)` for a consistent title row).
- Scale everything with `c.S(px)`: design for bar size M at 96 DPI: **bar height 44, tile height 34** (tile = c.Bounds.Height). Typical tile widths: icon-only 34–40, small 60–110, medium 120–200, wide up to 280. Panel sizes typically 300–360 × 180–420 design px (`PanelSize(scale)` must multiply by scale).
- Colors only from the theme (`c.T.Text/TextDim/TextFaint/Accent/Good/Warn/Bad/Track/Tile/...`) so all 5 themes (Black, White, Liquid Glass, iOS Glass, Midnight) work. Text on glass must stay grayscale-AA (Ui.Hq already set by host). Icons: `Ui.Icon(g, Icons.X or "\uE..." , rect, color, px)` (Segoe Fluent Icons).
- Interactivity: `c.Hit(rect, action)` inside Draw/DrawPanel, `c.Wheel(rect, delta=>...)`, `c.Button(...)`, `c.IconButton(...)`, text input in panels via `c.Input(key, rect, placeholder, onSubmit, onChange)`. Default tile click toggles your panel (override `OnClick`).
- Account-based widgets: declare the account in a static constructor `static XxxWidget(){ Accounts.Define("stripe","Stripe","Paste a restricted read-only key…","https://dashboard.stripe.com/apikeys", Accounts.F("key","Secret key")); }` (Define is idempotent; several widgets may share an account id). When `!HasAccount`, show realistic **sample** data plus a small "Sample" badge, and in the panel a button "Connect in Settings" → `Host.OpenSettings("accounts")`. When connected, fetch for real.
- Persisted user data: `State` dictionary + `SaveState()`.

## Performance rules (the user cares most about this)
- Background work only via `Hub.Define(key, intervalMs, sampler)` + `Hub.Subscribe` in `OnStart` / `Hub.Unsubscribe` in `OnStop` (it stops polling when nobody listens). Samplers return a NEW immutable snapshot object each time (override Equals or return same reference if unchanged to avoid repaints).
- Intervals: system stats ≥ 1000 ms; process lists ≥ 3000 ms; web APIs ≥ 5–15 min (weather 15 min, prices 1–5 min, revenue 15 min). Heavy data needed only in a panel → subscribe in `OnPanelOpen`, unsubscribe in `OnPanelClose`.
- `RefreshMs` only when the tile visibly changes (a clock with seconds = 1000; without seconds = return 1000 but only Invalidate when the minute changes is fine — host repaints after OnTick, so prefer 1000 max frequency, never < 250 except while an animation is actively running in an open panel).
- No per-frame allocation of bitmaps/large arrays; cache images (album art, avatars) and dispose old ones. Use `Ui.B(color)` cached brushes, `Ui.Font(...)` cached fonts. Dispose pens/paths (`using`).
- Never block the UI thread (no HTTP/WMI/Process calls in Draw/OnClick — queue to ThreadPool or Hub.Refresh).

## Testing (visual)
The HOST agent provides a render harness (in `src/App/`, may land a few minutes after you start — keep coding meanwhile):
```
/mnt/c/Users/alhaz/NovaBar/build/<name>/NovaBar.exe --home 'C:\Users\alhaz\NovaBar\home-<name>' --render 'C:\Users\alhaz\NovaBar\out\<name>.png' --widgets id1,id2 [--category "System"] [--themes dark,white,glass,glasslight,midnight] [--panels] [--size M] [--wait 3000]
```
(run it from a cwd under /mnt/c, e.g. `cd /mnt/c/Users/alhaz/NovaBar && ./build/<name>/NovaBar.exe ...`). It renders bar strips + panels for each theme into a PNG and writes exceptions to `<png>.log`. Then **look at the PNG with the Read tool** (`/mnt/c/Users/alhaz/NovaBar/out/<name>.png`) and iterate until it looks polished, aligned, legible in all 5 themes. Do not launch the real bar (`NovaBar.exe` without args) — the user’s desktop must not be disturbed; the HOST agent alone may do short live tests.

## Style target
Modern, calm, premium dock: rounded tiles, generous spacing, Segoe UI Variable, one accent color, small dim labels + bold values, rings/sparklines/progress bars, subtle hover. Dark "Black", clean "White", translucent "Liquid Glass", light frosted "iOS Glass", deep-blue "Midnight". Think macOS-quality widgets on Windows 11.

## Deliverable
When done, reply with: list of widget ids implemented (with RefName and DataKind), anything partial and why, account ids defined, Hub keys defined, and confirm `scripts/build.sh <yourname> 'src/Widgets/<Folder>/*.cs'` builds clean and you visually verified the render.
