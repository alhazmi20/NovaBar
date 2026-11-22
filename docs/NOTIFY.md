# NovaBar notifications (group prefix `Ntf`, folder `src/Widgets/Notify/`)

NovaBar replaces the Windows notification banners with its own themed toasts plus a Notification Center widget.

## Source: WinRT UserNotificationListener (works unpackaged, no identity needed)
Tested on this PC (Windows 11 build 26200) from a plain unpackaged exe and from NovaBar.exe:
- `UserNotificationListener.Current.GetAccessStatus()` → **Allowed**, and `GetNotificationsAsync(NotificationKinds.Toast)` returns the real notifications (app name, AUMID, ToastGeneric text).
- The `NotificationChanged` event throws `0x80070490` (it needs package identity), so NovaBar **polls the id set every 2 s** on a pool timer. That's one cheap WinRT call; details and logos are only read for new ids. The timer backs off to 30 s when access is denied.
- **No sparse MSIX package, certificate, or manifest change is needed.** There is no `install-notify-identity.ps1`.
- Images: the listener API only exposes text elements (`NotificationBinding.GetTextElements`). Hero and inline images can't be read, so toasts show the app logo (`AppInfo.DisplayInfo.GetLogo`, cached under `cache\notify-icons`) or a coloured letter avatar.

### What the user must do
**Windows notifications are currently turned OFF globally on this PC** (`HKCU\Software\Microsoft\Windows\CurrentVersion\PushNotifications\ToastEnabled = 0`; the notifier reports `DisabledForUser`). In that state Windows delivers nothing, to anyone. The user must turn them on once in **Settings › System › Notifications**. The panel shows a "Windows notifications are off · Turn on" strip that opens `ms-settings:notifications`. NovaBar then hides Windows' own banners itself (below), so turning notifications on doesn't bring the stock popups back.
If Windows ever denies listener access, the panel shows a strip linking to `ms-settings:privacy-notifications`.

## Banner suppression (reversible)
While NovaBar's popups are active (`notify.replace` and `notify.suppressBanners`), NovaBar sets `HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\<AUMID>\ShowBanner = 0` for every app key under `...\Notifications\Settings`, and for each new AUMID the moment its first notification is seen. Notifications still go to the notification list, which is what the listener reads. The global toggle is never touched.
- Original values are saved in StateStore `notify.banners` (`"-"` = value was absent). They are restored when the feature is turned off, when the service stops, and on a clean exit (`Application.ApplicationExit` / `ProcessExit`). After a crash they're restored or kept on the next start, depending on the setting.
- After each change NovaBar broadcasts `WM_SETTINGCHANGE "Notifications"`.
- Caveat: an app's very first notification (before its key exists) can still appear as a Windows banner once.
- Not yet verified live: whether the Windows notification service picks up `ShowBanner` changes without a sign-out. Toasts are disabled globally on this PC, so this couldn't be tested.
- AUMIDs that contain `\` (some Win32 shortcuts) can't be registry key names, so they are skipped.

## Host integration
**None required.** `NtfCenterWidget`'s static constructor calls `NtfService.Boot()`, which runs because WidgetRegistry runs every widget static ctor on the bar UI thread. `Boot()` does nothing under `--settings/--render/--list/--checklist`. Otherwise it schedules a one-shot `Application.Idle` start on that thread, subscribes to `AppConfig.Changed`, and hooks exit cleanup. The service therefore runs whenever `notify.replace` is true, even if the bell isn't on the bar.
Optional, for robustness only: put this in BarForm after the bar is created, on the UI thread. It's idempotent.
```csharp
NtfService.Boot();
```
`app.manifest` needs **no** change.

Other notes for the host:
- Toasts look up the bar among `Application.OpenForms` by type name `BarForm` (bounds plus DPI) to stack above it, or below it when the bar is at the top, right-aligned with the bar's right edge. Without a bar they use the work-area corner.
- DND toggling saves config through `BarForm.SaveConfig()` (found by reflection, so its own file watcher doesn't reload), and falls back to `AppConfig.Current.Save()`.
- Controls agent: `CtlNotificationsWidget` (id `notifications`) must be deleted. Notify now registers the id `notifications` as an alias of `notifcenter` (category **`Hidden`**: Settings/gallery should skip `Category == "Hidden"`).

## Widgets
| id | Name | Category | RefName | Data |
|---|---|---|---|---|
| `notifcenter` | Notifications | System | Notifications | Live |
| `notifications` (alias, same class) | Notifications | Hidden | Notifications (alias) | Live |

Tile: bell, unread badge (accent; grey when DND), moon when DND is on. Left click opens the panel; middle click toggles DND.
Panel: header with a DND toggle and "Clear all"; DND / Windows-off / access strips; cards grouped by app, newest first, 3 per group with "+N more"; each card can be dismissed (×) or clicked to open its app; × on a group clears that app; scrolling; empty state "You're all caught up"; footer link "Notification settings" (`ms-settings:notifications`) and a count. Opening the panel resets the unread count.
Widget setting: `badge` (Toggle, default true).

## Toasts
- Borderless windows with `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST` that answer `WM_MOUSEACTIVATE → MA_NOACTIVATE`, so they never steal focus.
- Painted on a PARGB DIB like the bar. Glass themes use the same accent-policy acrylic as the bar.
- Each toast shows app icon, app name, time, title and up to 3 body lines. A × appears on hover and sends the toast to the center; right-click does the same. Left click runs `explorer.exe shell:AppsFolder\<AUMID>` and dismisses the notification (also from Windows' list).
- Slide-in and restack animations take 200 ms; the 15 ms timer runs only while something is animating. Otherwise one 200 ms countdown timer runs only while toasts are on screen.
- Auto-hide after `notify.duration` seconds; the countdown pauses while the pointer is over any toast. At most 3 visible; the rest are queued (dropped once older than 2 min).
- DND (`notify.dnd`) or busy/fullscreen (SHQueryUserNotificationState 2/3/4/6/7): no popup and no sound. The item lands silently in the center and raises the unread badge.
- Sound: `PlaySound("Notification.Default", SND_ALIAS|SND_ASYNC)` when `notify.sound` is on.
- Test: the Settings app writes `%APPDATA%\NovaBar\notify-test.flag`. A FileSystemWatcher (only while the service runs) deletes it and shows a sample toast.

## Settings keys (AppConfig.Extra)
| key | type | default | meaning |
|---|---|---|---|
| `notify.replace` | bool | true | NovaBar shows its own banners and runs the service even without the bell on the bar |
| `notify.suppressBanners` | bool | true | hide Windows' own banners (per-app ShowBanner=0, reversible) |
| `notify.dnd` | bool | false | Do Not Disturb (also toggled from the panel/tile) |
| `notify.sound` | bool | true | play the Windows notification sound |
| `notify.duration` | number (s) | 6 | auto-hide delay |

State: `state\notify.history.json` (last 100 + unread), `state\notify.banners.json` (originals), `cache\notify-icons\*.png`, log `notify.log` (bounded).

## Render harness
`--render … --widgets notifcenter --panels` uses realistic sample notifications by default in render mode, and also writes `<png>-toasts.png`: a toast stack over a bar in each theme.
- `NOVABAR_NOTIFY_DEMO=0`: use the real listener (read-only, no popups, no registry changes).
- `NOVABAR_NOTIFY_DEMO=empty`: empty state.
- `NOVABAR_NOTIFY_LIVE=1`: shows the 3 demo toasts for real on screen, saves `<png>-live.png` after 1.2 s, then closes them.
- Pass env vars through `WSLENV`.
