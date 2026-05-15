# 🥷 ClipNinja

**A Windows clipboard manager with a chibi ninja that throws Hadoukens.**

ClipNinja captures everything you copy and lets you click any captured slot to load it back to the clipboard. Built in C#/WPF on .NET 8. Single-file `.exe` — no installer needed, no .NET runtime to install separately.

The ninja in the corner performs Street Fighter moves every time you copy something. Because why not.

---

## ✨ Features

### Clipboard manager
- **30 slot capacity** with auto-scroll when content overflows
- **Memory Hole** — slots 1-15 are the rolling "recent" area; slots 16-30 are long-term storage that new captures never touch
- **Captures**: plain text, screenshots/images, and spreadsheet (Excel) data
- **Click-to-load**: click any slot to put it on the clipboard, then `Ctrl+V` to paste in any app
- **Pin slots** to keep them through new captures
- **Drag-and-drop reordering**, including into the Memory Hole
- **Auto-load slot 1** on startup if your clipboard is empty
- **Persists across restarts** — your slots survive reboots

### Visual feedback
- **Type-specific thumbnails**: image thumbnail for screenshots, green grid icon for Excel data, document icon for plain text
- **Hover the thumbnail** to preview the full content (text or scaled image)
- **Double-click** an image preview to open it fullsize in your default image viewer
- **Active slot** is highlighted in amber so you know what's currently loaded

### The ninja
A hand-drawn chibi ninja with an ochre headband performs animations on every clipboard capture. 11 Street Fighter moves total:

- Kick, Punch, Hadouken (with a glowing blue chi ball that materializes between cupped hands), Shoryuken
- Backflip, JumpKick, SpinKick
- Lightning Kick (Hyakuretsukyaku — rapid-fire leg flurry)
- Helicopter Kick (Tenshokyaku — rising spin)
- Hurricane Kick (Tatsumaki — traveling spin)
- Hero Landing (leap up, slam down, arms-wide pose)

Right-click the sun in the sky to pick a specific move from the menu. Left-click for a random one.

### Hotkeys
- `Ctrl+Shift+N` — show/hide ClipNinja
- `Ctrl+Shift+B` — pause/resume capture

### System tray
- Closes to tray instead of quitting (so clipboard capture keeps running)
- Right-click the tray icon for Show/Hide, **Launch on Windows startup toggle**, Clear all slots, Settings, Exit

### Auto-launch on startup
Toggle **"Launch on Windows startup"** in the tray context menu. When enabled, ClipNinja starts minimized to the tray every time you sign in to Windows. Implemented via the standard `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registry key — no admin rights required, and you can disable it any time (from the tray menu, or via Windows Task Manager → Startup).

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
├── settings.json        slot metadata + preferences
└── images\              full-resolution PNG files for image slots
```

ClipNinja never writes to the Registry or to Program Files. It's truly portable — delete that folder to fully wipe it.

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

The csproj is preconfigured for self-contained, single-file, win-x64 publishing — you don't need any extra flags.

---

## 🏗️ Architecture (for curious devs)

```
ClipNinjaV2/
├── App.xaml(.cs)           Single-instance mutex, main entry point
├── MainWindow.xaml(.cs)    Frameless 360×920 window, slot list, ninja stage
├── Models/
│   ├── ClipContent.cs      TextContent / ImageContent / IsSpreadsheet flag
│   ├── ClipSlot.cs         Per-slot state: Content, IsPinned, IsActive, etc.
│   └── AppSettings.cs      Persisted preferences (SlotCount, ShowNinja, …)
├── Services/
│   ├── ClipboardWatcher.cs Win32 WM_CLIPBOARDUPDATE hook + content capture
│   ├── NativeClipboard.cs  Direct Win32 SetClipboardData (bypasses slow WPF OLE)
│   ├── PersistenceService.cs  Async background save to %AppData%
│   ├── HotkeyService.cs    Global hotkey registration via RegisterHotKey
│   └── Trace.cs            Lazy-init diagnostic tracer (off in production)
├── ViewModels/
│   └── MainViewModel.cs    Slot cascade logic (respects Memory Hole)
├── Views/
│   ├── PreviewPopup.cs     Hover preview window with grace period
│   └── InputPrompt.cs      Custom WPF input dialog
├── Animations/
│   ├── NinjaAnimator.cs    24fps DispatcherTimer keyframe player
│   └── AnimationLibrary.cs 11 SF moves + IdleBob
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
- **Memory Hole** (slots 16-30): cascade on new captures only touches slots 1-15. Slots 16+ are independent long-term storage.

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
