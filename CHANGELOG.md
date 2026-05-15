# Changelog

All notable changes to ClipNinja are documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [1.2.0] — 2026-05-14

### Added
- **Launch on Windows startup** — toggle in the tray context menu. Writes to
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (no admin needed).
  When enabled, ClipNinja launches with `--hidden` and starts minimized to
  the tray, so you don't see a window flash every login.
- **Registry self-heal**: every launch, if the setting is on, we re-write the
  registry value with the current `.exe` path. Moving or renaming the `.exe`
  no longer breaks auto-start.
- **`--hidden` / `-h` command-line argument** — start ClipNinja minimized to
  the tray without showing a window. Used by the Run-key entry; users can
  also invoke it manually.

### Changed
- Tray context menu now includes a checkable "Launch on Windows startup" item.
  The checkmark stays in sync with the actual registry state, even if you
  toggle it externally via Task Manager → Startup.

## [1.1.0] — 2026-05-14

### Added
- **30 slots** (raised from 15) with auto-scrollbar when content overflows
- **Memory Hole**: slots 16-30 are protected long-term storage. New captures only cascade through slots 1-15.
- **Visual divider** above slot 16 with a vault-door icon and "Memory Hole — long-term storage" label
- **Blue chi-energy Hadouken** — the fireball now materializes between the ninja's cupped hands as a blue energy ball (Ryu-style) then thrusts forward, instead of starting yellow
- **One-click installer** (`Install-ClipNinja.cmd` + `.ps1`) creates Start Menu / Desktop shortcuts without admin rights
- **One-click uninstaller** (`Uninstall-ClipNinja.cmd` + `.ps1`)

### Changed
- Hadouken animation timing reworked: 0.5s charge-up with the chi ball growing visibly between cupped hands, then a fast 0.12s thrust, then the ball flies across
- Settings migration: existing users on the old 15-slot default get auto-bumped to 30 on first launch

### Fixed
- N/A (1.1 was primarily a feature release; reliability fixes shipped in 1.0)

## [1.0.0] — 2026-05-10

### Added — initial release
- 15-slot clipboard manager with cascading + pinning
- Click-to-load workflow (no Ctrl+V hijacking)
- Image, text, and spreadsheet capture
- Hand-drawn chibi ninja with 11 Street Fighter animations
- Hover preview popup with double-click-to-fullsize
- Drag-and-drop slot reordering
- System tray integration with right-click menu
- Hotkeys: `Ctrl+Shift+N` (show/hide), `Ctrl+Shift+B` (pause)
- Persistent storage in `%AppData%\ClipNinja\`
- Single-file self-contained `.exe` (~80MB, no .NET install required)

### Fixed (during 1.0 development)
- **Clipboard contention reliability**: replaced WPF's `Clipboard.SetDataObject(copy:true)` with direct Win32 `SetClipboardData`. WPF's API internally retries for 1 second per failed attempt; the native path with tight 10ms retries succeeds in tens of milliseconds. Trace logs showed every text write taking ~1000ms before this fix.
- **HTML-on-every-event lag**: spreadsheet detection no longer reads clipboard HTML (Word puts 50-500KB on the clipboard on every copy). Now uses fast BIFF format-list checks only.
- **Line-ending signature mismatch**: signatures are now computed from line-ending-normalized text so self-write echo detection works correctly. Previously our own writes were being re-captured as new clipboard events.
- **`TransformedBitmap` lazy scaling**: thumbnails are now baked into `WriteableBitmap` at capture time, not lazy-scaled at paint time on the UI thread.
- **`Thread.Sleep` on UI thread**: replaced all retry sleeps with `DispatcherTimer` posts.
- **Word formatted text was triggering Excel icon**: tightened spreadsheet detection to require true Excel BIFF formats (not "Link Source" which Word also writes).
