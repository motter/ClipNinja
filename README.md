# 🥷 ClipNinja

**A Windows clipboard manager with a chibi ninja that throws Hadoukens.**

ClipNinja captures everything you copy and lets you click any captured slot to load it back to the clipboard. Built in C#/WPF on .NET 8. Single-file `.exe` — no installer needed, no .NET runtime to install separately.

The ninja in the corner performs Street Fighter moves — with speech bubbles shouting the move name — every time you copy something. Because why not.

---

## ✨ Features

### Clipboard manager
- **Two-list model**: a curated **Pinned** shelf (no cap) plus a rolling **Recent** list (default 50, configurable 25-500). The Pinned section hides itself when empty.
- **Newest capture always lands at Recent position 1**, with older entries shifting down. When Recent overflows, the oldest item rolls into History.
- **Pin/unpin moves items between the two lists** — pinning takes an item out of Recent and puts it at the top of Pinned; unpinning does the reverse.
- **Rolling history** — items that roll out of Recent land in a separate history panel (📜 button at the bottom). Default cap 100, configurable to 1000. Most recent at the top, search by text, double-click to promote back to Recent's position 1, right-click for delete. Pinned items are NEVER displaced into history (by design).
- **Unified search** with **Ctrl+F** — filters both Pinned and Recent at once by nickname or display text. Esc closes.
- **Drag-out to other apps** — drag any item from Pinned, Recent, or History directly onto Word, Outlook, a browser, an image viewer, etc. ClipNinja keeps its copy (Copy effect); internal drags do reorder/pin/unpin.
- **Promote-on-recopy** — re-copying something already in Recent promotes it to position 1 instead of duplicating. Re-copying something Pinned is a no-op (Pinned content is "decided" content).
- **Captures**: plain text, screenshots/images, spreadsheet (Excel) data, and URLs (auto-detected)
- **Email/web hyperlinks survive the round-trip** — when you copy formatted text from Outlook, Word, a web page, or Teams, ClipNinja captures both the plain text AND the rich-text formats (CF_HTML, CF_RTF). Paste an item back into Word and the hyperlinks are still clickable. A "🔗 N" badge tells you how many hyperlinks were captured; the preview popup lists them all with "Open in browser" buttons.
- **Click-to-load**: click any item to put it on the clipboard, then `Ctrl+V` to paste in any app
- **50-char nicknames** for items (right-click → Set nickname)
- **Auto-load top of Recent** on startup if your clipboard is empty
- **Persists across restarts** — your items survive reboots
- **Automatic migration** from v2.3 and earlier — old Memory Hole content becomes Pinned

### Screenshot tools
- **Auto-applied black border on screenshots** — when enabled (default), captured images get a 3px black border baked in AND the live clipboard is replaced with the bordered version. Take a screenshot in Greenshot, paste directly into Word: the border is there. Toggle from the tray menu or the Settings dialog.
- **GIF labeling** — `.gif` files copied from File Explorer get a small red "GIF" badge in the thumbnail corner so you can tell them apart from regular images

### Visual feedback
- **Image thumbnails** for screenshots — small baked bitmap in the slot row
- **Green grid icon** for spreadsheet/Excel data
- **Ochre document icon** for plain text
- **Sky-blue link-chain icon** for slots holding a single URL (e.g. `https://example.com` — auto-detected; the preview popup includes an **Open in browser** button)
- **Hover** any item or history row to preview the full content
- **Double-click** an image preview to open it fullsize in your default image viewer
- **Capture timestamp** ("5 min ago • 3:47 PM") at the bottom of every preview
- **Active item** is highlighted in amber so you know what's currently loaded
- **Version badge** ("v2.4") in the title bar

### The ninja
A hand-drawn chibi ninja with an ochre headband performs animations on every clipboard capture, shouting the move name in a comic-style speech bubble. 12 Street Fighter moves total:

- **Kick** ("KIAI!"), **Punch** ("HYAH!")
- **Hadouken** ("HADOUKEN!") — blue chi ball materializes hip-height between cupped hands, charges, then launches
- **Shoryuken** ("SHORYUKEN!") — rising dragon punch
- **Backflip** ("WHOOSH!"), **JumpKick** ("TOBI GERI!"), **SpinKick** ("MAWASHI!")
- **Lightning Kick** ("HYAKURETSU!") — rapid-fire leg flurry
- **Helicopter Kick** ("TENSHO!") — rising spin
- **Hurricane Kick** ("TATSUMAKI!") — traveling spin
- **Spinning Bird Kick** ("SPINNING BIRD KICK!") — Chun-Li's upside-down 3-rotation kick
- **Hero Landing** ("TA-DAAA!") — leap up, slam down, arms-wide pose

Click the sun in the sky for a random move on demand. Right-click it to pick a specific move from the menu. (The clickable area extends slightly beyond the visible sun for forgiving targeting.)

### Hotkeys
- `Ctrl+Shift+N` — show/hide ClipNinja
- `Ctrl+Shift+B` — pause/resume capture
- `Ctrl+F` — search the slot list (Esc closes)

### System tray
- Closes to tray instead of quitting (so clipboard capture keeps running)
- Right-click the tray icon for: Show/Hide, Pause/Resume, **Launch on Windows startup**, **Black border on screenshots: ON/OFF**, Open trace log, Open image-debug log, Settings, Clear all slots, Exit

### Settings & About dialog
The tray menu **"Settings…"** item opens a real dialog with:
- **Behavior** section: toggles for Show Ninja, Add Black Border, Launch on Startup (each with a sublabel explaining what it does)
- **Recent list** section: max-items dropdown (25 / 50 / 100 / 200 / 500)
- **History** section: max-items dropdown (Disabled / 50 / 100 / 200 / 500 / 1000) and a "🗑️ Clear all history items" button
- **About** section: version, data folder path, pinned + recent + history counts, total disk usage
- "📁 Open data folder" button for quick access to `%AppData%\ClipNinja`

### Auto-launch on startup
Toggle **"Launch on Windows startup"** in the tray menu or Settings dialog. When enabled, ClipNinja starts minimized to the tray every time you sign in to Windows. Implemented via the standard `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registry key — no admin rights required, and you can disable it any time (from the tray menu, Settings dialog, or Windows Task Manager → Startup).

---

## 📥 Download

Get the latest release from the **[Releases page](../../releases)**.

Download `ClipNinja_v1.1.zip`, extract it, and double-click `Install-ClipNinja.cmd` to create a Start Menu shortcut. Or skip the installer and just double-click `ClipNinja.exe` directly — it's fully portable.

**System requirements:** Windows 10 or 11, 64-bit. ~100MB free disk space. No .NET install needed.

### First-time SmartScreen warning

The first time you run an unsigned indie `.exe`, Windows shows a "Windows protected your PC" dialog. Click **More info** → **Run anyway**. You only have to do this once per machine.

---

## 🗄️ Where data lives

```
%AppData%\ClipNinja\
├── settings.json           preferences (SchemaVersion=2, RecentMaxItems, etc)
├── items.json              pinned + recent items (v2.4+)
├── history.json            rolling history list
├── images\                 full-resolution PNGs for pinned/recent items
├── history\
│   └── images\             full-resolution PNGs for history items
└── slots.v1.bak            (legacy) backup of pre-2.4.0 storage, if migrated
```

ClipNinja never writes to the Registry (except the optional "Launch on startup" entry under HKCU) or to Program Files. It's truly portable — delete that folder to fully wipe it.

---

## 🛠️ Building from source

You need the **.NET 8 SDK**: <https://dotnet.microsoft.com/download/dotnet/8.0>

```sh
git clone https://github.com/YOUR_USERNAME/ClipNinja.git
cd ClipNinja
dotnet publish -c Release
```

The output `.exe` will be at:

```
bin\Release\net8.0-windows\win-x64\publish\ClipNinja.exe
```

Roughly 80MB — bundles the .NET runtime, WPF, and all dependencies into one self-contained file.

The repo includes a `global.json` pinning the build to .NET 8 SDK
(`rollForward: latestFeature`). If you have multiple SDKs installed
(e.g. a .NET 10 preview), this prevents the wrong runtime from being
bundled. If your `dotnet --version` reports a version that doesn't match
your installed 8.x SDK build, edit `global.json` to match what
`dotnet --list-sdks` shows for your 8.x install.

The csproj is preconfigured for self-contained, single-file, win-x64 publishing — you don't need any extra flags.

---

## 🏗️ Architecture (for curious devs)

```
ClipNinjaV2/
├── App.xaml(.cs)           Single-instance mutex, --hidden flag, main entry
├── MainWindow.xaml(.cs)    Frameless window, slot list, ninja stage, history panel
├── global.json             Pins build to .NET 8 SDK (defends vs. .NET 10 preview)
├── Models/
│   ├── ClipContent.cs      TextContent / ImageContent / HyperLink record
│   ├── ClipSlot.cs         Per-slot state: Content, IsPinned, IsActive, etc.
│   ├── HistoryItem.cs      Rolling-history entry (parallel to ClipSlot)
│   └── AppSettings.cs      Persisted preferences (RecentMaxItems, HistoryMaxItems, …)
├── Services/
│   ├── ClipboardWatcher.cs Win32 WM_CLIPBOARDUPDATE hook + content capture
│   │                       + CF_HTML/CF_RTF capture + AddBlackBorder
│   ├── NativeClipboard.cs  Direct Win32 SetClipboardData (bypasses slow WPF OLE)
│   ├── PersistenceService.cs  Async background save + v1→v2 migration
│   ├── HistoryService.cs   Rolling history list + history.json persistence
│   ├── HotkeyService.cs    Global hotkey registration via RegisterHotKey
│   ├── StartupService.cs   HKCU Run-key registry (auto-launch on Windows boot)
│   └── Trace.cs            Lazy-init diagnostic tracer (off in production)
├── ViewModels/
│   └── MainViewModel.cs    Pinned + Recent cascade logic; feeds History on overflow
├── Views/
│   ├── PreviewPopup.cs     Hover preview window (slots + history rows)
│   ├── SettingsDialog.cs   Behavior / Recent / History / About dialog
│   └── InputPrompt.cs      Custom WPF input dialog
├── Animations/
│   ├── NinjaAnimator.cs    24fps DispatcherTimer keyframe player
│   └── AnimationLibrary.cs 12 SF moves + IdleBob + per-move Shout strings
└── Resources/
    ├── ninja.ico           Multi-resolution app icon
    ├── Colors.xaml         Desert palette brushes
    └── Styles.xaml         Control styles
```

### Notable design decisions

- **No global `Ctrl+V` hijacking.** Clicking a slot loads it to the OS clipboard; the user presses Ctrl+V natively. Earlier prototypes intercepted Ctrl+V and were unreliable due to focus drift.
- **`NativeClipboard.SetText` over WPF's `Clipboard.SetDataObject`.** The WPF API internally retries for ~1 second per failed attempt when the clipboard is contended (Win+V history, password managers). Direct Win32 with 10ms tight-loop retries succeeds in tens of milliseconds.
- **Spreadsheet detection** uses only BIFF format markers, not HTML inspection. Reading clipboard HTML on every event costs 100+ms when Word is the source.
- **Thumbnails are baked into `WriteableBitmap`** rather than left as lazy `TransformedBitmap` — lazy scaling at paint time was a major lag source.
- **Two-list model (v2.4)**: replaced the v2.3 "Memory Hole" (positional slots 16-30) with separate `PinnedItems` and `RecentItems` ObservableCollections. One mechanism (pinning) does what two used to (pin + Memory Hole). Migration from v1 to v2 happens automatically on first launch.
- **History** (`HistoryService`): items cascading off slot 15 land here. Stored separately from slot persistence so the slot save/load path isn't slowed by potentially-100s of archived items.
- **Rich-text round-trip** (hyperlinks): plain-text-only writes use the fast `NativeClipboard.SetText` path. Writes that have HTML or RTF fall back to WPF's `Clipboard.SetDataObject` with all formats populated — slower but the only way to preserve hyperlinks across paste.
- **Black border baked at 96 DPI** regardless of source DPI. Mismatched DPI caused the bordered bitmap to display as a tiny image inside an oversized white background in slot thumbnails and MS Teams paste targets.

### Dependencies

- [H.NotifyIcon.Wpf](https://www.nuget.org/packages/H.NotifyIcon.Wpf) — system tray icon
- [Microsoft.Xaml.Behaviors.Wpf](https://www.nuget.org/packages/Microsoft.Xaml.Behaviors.Wpf) — XAML behaviors

That's it. Everything else is plain WPF + .NET.

---

## 🐛 Known issues

- **Clipboard contention**: if another app holds the system clipboard, writes can fail with `OpenClipboard failed`. ClipNinja retries up to 5 times per click (~1.5s total budget). Common culprits: Windows Clipboard History viewer (Win+V), password managers, RDP sessions.
- **No code signing.** Triggers SmartScreen on first run. ($300/yr cert not worth it for a hobby app.)

---

## 📜 License

MIT — see [LICENSE](LICENSE) (add a LICENSE file before publishing if you want this clause to apply).

---

## 🙏 Credits

Built solo with a lot of iteration and clipboard-API spelunking. Chibi ninja stage drawn by hand in WPF vector shapes — no images, no SVGs imported, everything is `<Path>` and `<Ellipse>` elements.

🥷
