# ClipNinja v2.4

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

### Two lists: Pinned + Recent

- **Pinned** (top of window, only shown when you have pinned items):
  your curated shelf. Items only go here when you explicitly pin them.
  They never move automatically and there's no cap.
- **Recent** (below Pinned): the rolling list of your latest captures.
  Newest copy always lands at position 1. Default cap 50 (configurable
  25/50/100/200/500 in Settings).
- When Recent overflows, the oldest item rolls into History.

### Per-item icons

- **Lock** — click to pin (Recent → Pinned) or unpin (Pinned → Recent)
- **Up/Down arrows** — reorder within the current list
- **Eraser** — remove this item
- **Drag** — drop onto another item in the same list to reorder, or
  drop across lists to pin/unpin. Drag out of ClipNinja to drop the
  content into another app (Word, Outlook, browser, etc.)

### Item type indicators

- **Document icon** (cream) — plain text
- **Grid icon** (green) — Excel/spreadsheet data
- **Link-chain icon** (blue) — a single URL (preview offers "Open in browser")
- **Thumbnail** (image) — screenshot or pasted image

### Hover for preview

Hover the thumbnail/icon on any item to peek at the full content in a popup.
Double-click an image preview to open it fullsize. The popup includes a
"📅 Captured: ..." footer showing when the clip was made.

### Hotkeys

- **Ctrl+Shift+N** — show/hide ClipNinja window
- **Ctrl+Shift+B** — pause/resume capture
- **Ctrl+F** — search Pinned + Recent (filters both at once; Esc to close)

### Tips

- **Drag an item directly into another app**: grab any item (Pinned,
  Recent, or History) and drop it onto Word, Outlook, a browser, an
  image viewer, etc. The content gets dropped in. ClipNinja keeps its
  copy too. For text with hyperlinks, the links survive the drop.
- **History** (📜 button at the bottom): items that roll out of Recent
  land here, capped at 100 by default (configurable in Settings).
  Most-recent at the top, double-click an item to promote it back
  to Recent's position 1, right-click for delete option. Search
  filters by text content. Pinned items are NEVER displaced into
  history (by design).
- **Email hyperlinks survive the round-trip**: when you copy text from
  Outlook or a web page that contains hyperlinks, ClipNinja captures
  both the plain text AND the rich-text formats. Click the slot to
  paste it back into Word/Outlook/Teams — the hyperlinks are still
  there. The slot row shows a small "🔗 N" badge to indicate N
  hyperlinks were captured. Hover the slot to see them all in the
  preview popup, where each link has an "Open" button.
- **Screenshot borders**: when the "Black border on screenshots" setting
  is on, ClipNinja bakes a thin black border into captured images AND
  immediately replaces the live clipboard with the bordered version.
  So you can take a screenshot in Greenshot and paste directly into
  Word — the border will be there. No need to click into ClipNinja first.
  (Turn this off in the tray menu or Settings dialog if you'd rather get
  unmodified screenshots from your live clipboard.)
- **GIF labeling**: GIF files copied from File Explorer show a small
  red "GIF" badge in the thumbnail corner. Browser "Copy Image" on a
  GIF still results in a static-frame capture (browsers strip animation
  before putting it on the clipboard).

### The ninja

- Plays a random Street Fighter move on every copy
- **Shouts the move name** in a comic-style speech bubble during the animation
- **Click the sun** in the sky → random move on demand
- **Right-click the sun** → pick a specific move from the menu
- 12 moves total: Hadouken (HADOUKEN!), Shoryuken (SHORYUKEN!),
  Kick (KIAI!), Punch (HYAH!), Backflip (WHOOSH!), JumpKick (TOBI GERI!),
  SpinKick (MAWASHI!), LightningKick (HYAKURETSU!),
  HelicopterKick (TENSHO!), HurricaneKick (TATSUMAKI!),
  SpinningBirdKick (SPINNING BIRD KICK!), HeroLanding (TA-DAAA!)

---

## Closing & exit

- Clicking **X** on the window **hides it to the system tray** (so capture
  keeps running). To actually exit, right-click the tray ninja → **Exit**.
- Ctrl+Shift+N brings the window back any time.

---

## Where your data lives

```
%AppData%\ClipNinja\
├── settings.json            your preferences
├── items.json               pinned + recent items
├── history.json             rolling history list
├── images\                  full-resolution PNGs for pinned/recent items
├── history\
│   └── images\              full-resolution PNGs for history items
└── slots.v1.bak             (legacy) backup of pre-2.4.0 storage, if migrated
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
