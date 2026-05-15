# ClipNinja v1.2

**A clipboard manager with a ninja that fights Street Fighter on every copy.**

Capture everything you copy. Click any slot to load it back to the clipboard.
Press Ctrl+V to paste in whatever app you're in. Pin slots you don't want to
lose. Watch a chibi ninja throw a Hadouken every time you copy something.

---

## Quick start

1. **Run `Install-ClipNinja.cmd`** (double-click). It creates a Start Menu
   shortcut and optionally a Desktop shortcut. No admin rights needed.
2. ClipNinja shows up in your system tray (bottom-right of taskbar).
3. Copy anything from anywhere. The ninja celebrates.
4. Click any captured slot to load it back to the clipboard, then **Ctrl+V**
   to paste wherever you are.

### Or skip the installer

You can also just **double-click `ClipNinja.exe`** directly. Works fine. The
installer is just for convenience — you can launch it from Start Menu after.

---

## First-time Windows warning

The very first time you run `ClipNinja.exe`, Windows shows a blue dialog:

> Windows protected your PC

This is normal for indie software that isn't code-signed (a signing cert
costs ~$300/year, which doesn't make sense for a hobby app). To run it:

1. Click **More info** (small link in the dialog)
2. Click **Run anyway**

You only do this once per machine. After that Windows remembers.

---

## How it works

### The slot list (left side)

- **Slots 1-15**: Recent area. New copies push older entries down through here.
- **Slot 16+**: The Memory Hole. Long-term storage. New captures never touch
  these slots — drop important snippets here to keep them safe forever.

### Per-slot icons

- **Lock** — click to pin (slot stays put through new captures)
- **Up/Down arrows** — swap with neighboring slot
- **Eraser** — clear this slot
- **Drag** — drop onto another slot to move it (including into the Memory Hole)

### Slot type indicators

- **Document icon** (cream) — plain text
- **Grid icon** (green) — Excel/spreadsheet data
- **Thumbnail** (image) — screenshot or pasted image

### Hover for preview

Hover the thumbnail/icon on any slot to peek at the full content in a popup.
Double-click an image preview to open it fullsize.

### Hotkeys

- **Ctrl+Shift+N** — show/hide ClipNinja window
- **Ctrl+Shift+B** — pause/resume capture

### The ninja

- Clicks a random Street Fighter move on every copy
- **Right-click the sun** in the sky → pick a specific move
- **Left-click the sun** → random move on demand
- 11 moves total: Hadouken, Shoryuken, Kick, Punch, Backflip, JumpKick,
  SpinKick, LightningKick, HelicopterKick, HurricaneKick, HeroLanding

---

## Closing & exit

- Clicking **X** on the window **hides it to the system tray** (so capture
  keeps running). To actually exit, right-click the tray ninja → **Exit**.
- Ctrl+Shift+N brings the window back any time.

---

## Where your data lives

```
%AppData%\ClipNinja\
├── settings.json         your preferences + slot metadata
└── images\               full-resolution screenshots (PNG)
```

Paste `%AppData%\ClipNinja` into File Explorer to find it.

---

## Uninstalling

Run `Uninstall-ClipNinja.cmd`. It:
- Removes Start Menu and Desktop shortcuts
- Asks if you want to also wipe your saved slot data

Then just delete `ClipNinja.exe` and the installer files. ClipNinja never
writes to the Registry or to Program Files — it's truly portable.

---

## System requirements

- Windows 10 or 11, 64-bit
- ~150 MB free disk space (the .exe is ~80MB; another ~70MB unpacks on first run)
- No .NET install required — bundled inside the .exe

---

## Known issues & tips

- **Clipboard contention**: if another clipboard manager (Win+V history,
  password manager, RDP session) is hogging the clipboard, ClipNinja will
  retry up to 5 times. Most clicks succeed in <50ms.
- **Launch on Windows startup**: right-click the ninja tray icon and toggle
  "Launch on Windows startup". When enabled, ClipNinja starts minimized to
  the tray each time you sign in to Windows.

---

## Credits

Built solo by [your name]. Chibi ninja icon and stage art done by hand in
WPF vector shapes. No frameworks beyond .NET 8 + H.NotifyIcon.Wpf.

🥷
