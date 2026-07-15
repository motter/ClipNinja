# Changelog

## [2.10.3] — 2026-07-14

### Fixed — header buttons overlapped the title
The title and the button row shared one grid cell (title centered,
buttons right-aligned on top), so they overlapped — the app name and
version rendered underneath the icons. The header is now a proper
two-column layout: title on the left, all controls on the right, in
separate columns that can't collide no matter how many buttons the
header grows.

### Changed — header controls look like buttons
- Bigger touch targets (30×28, was 22×20) with larger glyphs.
- Each button gets a rounded hover background, so the icons read as
  real buttons instead of loose glyphs crammed together.
- A thin divider separates the app actions (search, capture,
  settings, border) from the window controls (minimize, close).
- Slightly taller header bar to give it all room to breathe.

## [2.10.2] — 2026-07-14

### Added — view the full-size screenshot from the capture popup
The post-capture popup's preview is a downscaled thumbnail. Now you
can see the real thing without sending or saving first: **click the
preview** (or the new **🔍 View full size** link) to open the
screenshot at actual pixels in a resizable, scrollable viewer. Esc
closes it. Shows edits too, if you annotated first.

## [2.10.1] — 2026-07-14

### Changed — hover preview is ~50% bigger
The hover preview was capped at a third of the monitor; it's now half,
with larger minimum floors (540×360). Both image and text previews get
the extra room, so you can usually read a clip at a glance without
opening it full size. Still bounded so it never takes over the screen,
and large images are downsampled to fit.

## [2.10.0] — 2026-07-14

### Changed — annotating a capture sends straight to the tray
When you Annotate from the post-capture popup, the primary button is
now **"Save & send to ClipNinja"** — it sends the edited image to the
tray and closes, no bounce back to the options popup. A secondary
**"Back to options"** returns your edited image to the popup if you
still want Save-as or Quick save on the annotated version. (Editing an
existing tray slot with the pencil is unchanged — Save just updates
the slot.)

### Added — edit text labels after clicking away
- In the annotator's Select tool (↖), **double-click any text label to
  reopen it** — its text, size, and colors are preserved, so you can
  fix a typo or add a line and commit again.
- Clicking Save with a text editor still open no longer drops that
  label; it's committed first.
- (Note: once an image is sent to the tray it's a flat PNG —
  annotations are baked in and no longer editable. Re-editing applies
  while you're still in the annotator.)

### Added — 💾 quick-save badge on image thumbnails
Image slots now show a 💾 badge at the top-left (mirroring the ✏️
annotate badge). One click saves a timestamped PNG to your quick-save
folder — for when you need a file to upload rather than a paste. Same
one-click save as the right-click menu, minus the right-click.

### Added — "Copy as plain text"
New right-click item on text slots: puts just the unformatted text on
the clipboard — no HTML, fonts, colors, or link formatting. The most
common "clean up what I pasted" operation, now built in.

## [2.9.2] — 2026-07-14

### Fixed — version number hidden behind the header buttons
The title block is centered while the window buttons are right-aligned
on top of it, so adding the 🔍 button in v2.9.1 pushed the button row
over the trailing version number. The version now sits to the LEFT of
"🥷 ClipNinja", where there's nothing to collide with — and it stays
clear no matter how many buttons the header grows.

## [2.9.1] — 2026-07-14

### Added — 🔍 search button in the header
Search (and with it the whole type-filter icon row) was reachable
only by Ctrl+F, which made the feature effectively invisible. There's
now a 🔍 button in the title bar, left of 📷. Click it to open the
search bar with its All · 🖼 · 🔗 · 📄 · 🌐 · 📊 filter icons; click
it again to close and clear.

## [2.9.0] — 2026-07-14

### Fixed — Esc now reliably cancels a capture
Both the region selector and the post-capture popup listened for Esc
with KeyDown (bubbling) — which never fired if focus landed on a
child element, or if Windows didn't hand keyboard focus to the
borderless selector at all (common right after other windows are
hidden for the capture). Both now use PreviewKeyDown (tunneling,
fires at the window first) and force keyboard focus on load. Esc
discards; the selector's Enter / 1-4 keys are more reliable too.

### Changed — post-capture popup appears where you captured
The popup was centered on ClipNinja's main window, which could be on
a different monitor entirely. It now opens centered on the monitor
the capture came from: the monitor containing your dragged region,
the monitor you grabbed, or the primary monitor for a whole-desktop
capture. (DPI-aware, so it lands correctly on mixed-scaling rigs.)

### Changed — type filter is now icons, not a dropdown
The search bar's type filter is a row of icon toggles matching the
glyphs used elsewhere in the app: **All · 🖼 · 🔗 · 📄 · 🌐 · 📊**.
Hover any icon for its name (Screenshots, URLs, Plain text, HTML,
Excel). Click one to filter; click the lit one again to clear. One
click instead of two, and no dropdown to open.

## [2.8.1] — 2026-07-13

### Fixed — update dialogs buried under the Topmost main window
The main window is always-on-top, and the tray update flow's message
boxes were ownerless — so they opened UNDERNEATH ClipNinja and
appeared to "flash and vanish". The tray flow now shows the window,
owns every dialog (owned dialogs render above their topmost owner),
and reports download errors in a dialog instead of only the status
bar.

### Fixed / hardened — self-update swap reliability
- The swap script's delay now uses ping instead of `timeout`
  (`timeout` requires an interactive console and can die instantly
  when launched from a hidden no-console process, exhausting the
  retry loop in milliseconds — the likely reason "Yes" appeared to
  do nothing).
- The script writes a swap log next to the exe; on next launch the
  app surfaces a failed swap in the status bar instead of silently
  staying on the old version.
- Exiting for the swap is now guaranteed: graceful shutdown with a
  3-second hard-exit fallback, so the file lock always releases.

## [2.8.0] — 2026-07-13

### Added — post-capture chooser
Every capture (region, monitor, or full screen) now lands in a small
popup with the shot preview and four actions:
- **🔄 Redo** — discard and relaunch the same capture flow
- **🖍 Annotate** — edit, then return to the popup with the edited
  version so you can still save or send it
- **📋 Send to ClipNinja** (Enter) — the classic flow: top slot +
  clipboard, capture effects applied
- **💾 Quick save** — instant timestamped PNG into your quick-save
  folder, no dialog (falls back to Save as… if no folder is set)
- **Save as…** (small link) — named save with destination picker
Esc discards the capture.

### Added — annotator: paste images into the canvas
- **Ctrl+V** (or the 📋 toolbar button) drops the clipboard image
  onto the annotation as a movable, resizable object — compose job
  aids and IT tickets from multiple screenshots in one image.
  Oversized pastes auto-fit; the fresh paste is auto-selected so you
  can position it immediately. Flattens into the saved output.

### Added — annotator preferences persist
- Color, size (S/M/L), and text style are remembered across
  sessions: pick clay red + large once and every future annotation
  session opens that way. Saved automatically when you hit Save.

### Added — text labels: light style, resizable, multi-line
- New **Aa toggle**: "light" text style puts your colored text on a
  pale complementary field (clay red → pale blue) instead of the
  dark tint. Persisted like color/size.
- Text labels are **resizable** with the Select tool's corner
  handles — the text stays centered, so growing the box adds
  breathing room around the words. Fresh labels also start with more
  comfortable padding than before.
- **Enter adds a new line** (memo-style labels); **Ctrl+Enter** or
  clicking away commits; Esc cancels.

## [2.7.1] — 2026-07-10

### Added — update rollback safety net
- The self-update swap now saves the outgoing exe as
  `ClipNinjaV2.exe.bak` next to the app before overwriting. If an
  update ever misbehaves: exit the app, delete the bad exe, rename
  the .bak back — running the previous version again in seconds, no
  GitHub trip needed.

## [2.7.0] — 2026-07-10

### Added — in-app updates via GitHub Releases
- **Settings → Updates**: set your GitHub repo once (owner/name),
  then "Check for updates now" walks the whole flow — shows the new
  version + release notes, downloads the release asset, swaps the
  exe via a small retry script in %TEMP% (waits for the app to exit,
  copies, relaunches), and restarts. No update framework dependency.
- **Tray → ⬆️ Check for updates…** — same flow from the tray.
- **Silent startup check** (on by default, toggleable): ~5 seconds
  after launch, a newer release shows a status-bar hint. Never a
  popup; background checks don't interrupt.
- Version comparison uses the release tag (v2.7.1 style) against the
  running assembly version. Asset preference: bare .exe, else .zip
  containing the exe; anything named like a source archive is
  ignored.

### Added — GitHub repo scaffolding
- **.github/workflows/release.yml**: push a v* tag and GitHub
  Actions publishes the self-contained single-file exe, zips it as
  ClipNinja-win-x64.zip, and attaches it to an auto-created Release
  with generated notes. Local publishes and CI publishes use the
  same csproj settings, so they're identical.
- **RELEASING.md**: the one-page playbook — one-time repo setup,
  then every release is bump → commit → tag → push.

## [2.6.0] — 2026-07-09

### Added — capture a single monitor
- Tray → "Capture full screen" is now a submenu: **All monitors**
  (still the Ctrl+Shift+Z action) plus one entry per display with
  its resolution, e.g. "Monitor 1 (primary) — 2560×1440". Rebuilt
  every time the menu opens, so hot-plugged displays appear without
  a restart.
- Inside the region selector, press **1–4** (or numpad) to capture
  that monitor instantly — cropped from the same frozen capture, so
  it's exactly what was on screen when the selector opened. Hint
  bar updated. Monitors are ordered left-to-right, top-to-bottom.

### Added — Select tool in the annotator (move / resize / re-aim)
- New **↖ Select** tool. Click any annotation to select it:
  - **Lines & arrows** get a handle at each endpoint — drag to
    lengthen, shorten, or change the angle. Arrowheads re-aim
    live as you drag.
  - **Boxes, highlights, and obfuscation mosaics** get four corner
    handles for resizing; drag the body to move.
  - **Text labels and number badges** drag-move.
  - **Delete** removes the selected annotation; **Esc** deselects
    (a second Esc closes the editor).
- Edits mutate the original object, so Undo still removes whole
  annotations in creation order. (Undo of an individual move/resize
  is a future refinement.)
- Selection handles live on a separate layer that's cleared before
  Save — they can never bake into the output image.

### Changed — text labels have a proper look
- Text annotations are now pill-style labels: the text and border
  use your selected swatch color, over a dark tinted background
  (~22% of the hue mixed into near-black) so they read cleanly on
  any screenshot content. The in-place editor matches the final
  look exactly — what you type is what you get.

## [2.5.2] — 2026-07-09

### Fixed — startup NullReferenceException from the type filter
The v2.5.0 type-filter ComboBox declares SelectedIndex="0", which
fires SelectionChanged during XAML parse — before the constructor
has assigned the view model. The v2.5.0 guard checked the wrong
thing (the search box, which is built earlier in the same parse and
therefore already exists); the actual null was _vm. ApplySlotFilter
and OnTypeFilter_Changed now guard _vm directly. Swept the rest of
the XAML for other parse-time-reachable handlers — this was the
only one.

(The global exception handler added in v2.4.6 caught this one too:
the app kept running and the error dialog pinpointed the exact line.)

## [2.5.1] — 2026-07-09

### Added — configurable screen-capture hotkeys
- New defaults, laptop-friendly (no Fn hunting): **Ctrl+Shift+C** =
  region capture, **Ctrl+Shift+Z** = full screen (entire virtual
  desktop, all monitors).
- Settings → "Screen capture hotkeys": click a box, press the combo
  you want, done — takes effect immediately, no restart. Esc cancels
  the edit. Alt-based combos supported.
- If a combo is owned by another app, ClipNinja says so in the
  status bar instead of failing silently. The 📷 button and tray
  items always work regardless of hotkey state.
- Tray menu gesture hints and the 📷 tooltip update live to show
  whatever combos you configured.

## [2.5.0] — 2026-07-08

### Added — Built-in screen capture (Greenshot replacement)
- **PrintScreen** (or the new 📷 header button, or tray → Capture
  region) opens a frozen-screen region selector: screen dims, drag a
  rectangle (live size readout), release to capture. Enter grabs the
  full screen from inside the selector; Esc/right-click cancels.
- **Ctrl+PrintScreen** (or tray → Capture full screen) grabs the
  entire virtual screen instantly, all monitors.
- Captures route through the live clipboard, so the normal ingestion
  pipeline applies: border / torn edges / drop shadow bake in, the
  shot lands in the top slot, auto-save-to-folder fires if enabled,
  and it's immediately pasteable. Capture → pencil badge → annotate
  → paste, all inside ClipNinja.
- ClipNinja's own window hides during capture so it never
  photobombs the shot. If another tool (Greenshot, ShareX) still
  owns PrintScreen, ClipNinja logs it and the button/tray paths
  work regardless — uninstall the other tool to free the key.
- Zero new dependencies: capture is GDI BitBlt via P/Invoke.

### Added — Annotator: stroke sizes, text labels, step numbers
- **S / M / L size selector**: stroke width for arrow/box/line
  (2 / 3.5 / 6 px), font size for text (13 / 18 / 26 px), and badge
  size for numbers.
- **T text tool**: click the image, type, Enter (or click away) to
  commit. Esc cancels the label. Uses the selected swatch color.
- **① number tool**: each click drops the next step number (1, 2,
  3…) as a colored badge — built for job-aid walkthroughs. Undo
  rewinds the counter so a mis-click doesn't leave a gap.

### Added — Pencil badge on image slots
- Image thumbnails now show a small ✏️ at the top-right; click it to
  jump straight into annotation. The right-click menu item remains.

### Added — Filter by content type
- The search bar (Ctrl+F / 🔍) gains a type dropdown: **All /
  🖼 Screenshots / 🔗 URLs / 📄 Plain text / 🌐 HTML / 📊 Excel** —
  combines with the text search. Closing the search bar resets the
  type filter so a hidden filter can't quietly shrink your list.

## [2.4.8] — 2026-07-08

### Added — Highlight tool in the annotator
- New ▆ Highlight tool: drag to lay a classic semi-transparent
  (40% alpha) marker box over text or UI — content underneath stays
  readable. Clamped to the image bounds so a sloppy drag doesn't
  spill color into the margins.

### Added — Obfuscate (pixelate) tool in the annotator
- New ▦ Obfuscate tool: drag over names, emails, keys, or anything
  else you don't want in the shared screenshot; the region is
  pixelated into ~12px mosaic blocks sampled from the real image.
  During the drag you see a dashed preview rectangle; the mosaic is
  computed on release. Baked in on Save like everything else.

### Added — Color swatches (desert-sunset palette)
- Five swatches drawn from the app's own theme: **Sun gold, Amber,
  Agave green, Sky blue, Clay red**. Applies to highlight fill AND
  arrow/box/line strokes. Default is Sun gold. Obfuscate ignores
  color — a mosaic has no ink.

## [2.4.7] — 2026-07-08

### Fixed — Settings crash, root cause found and fixed

The v2.4.6 failure boundary did its job: instead of killing the app,
it surfaced the real exception —
`ArgumentException: '' is not a valid value for property 'FontFamily'`.

Root cause: the About section's row builder set
`FontFamily = monospaceValue ? new FontFamily("Consolas") : null`
in a TextBlock initializer. FontFamily is a non-nullable dependency
property — assigning null throws, and that exception took down the
entire Settings dialog (and, before v2.4.6's handlers, the whole
app). Every settings open hit this, because the About section always
builds at least one non-monospace row.

Fix: only set FontFamily when monospace is wanted; otherwise leave
the property untouched so it inherits the default font. Swept the
rest of the codebase for the same `cond ? value : null` pattern on
dependency properties — this was the only instance.

Settings now opens from both the ⚙ header button and the tray menu.

## [2.4.6] — 2026-07-08

### Added — ⚙ Settings button in the header
Settings is now reachable from inside the app: ⚙ button in the title
bar, left of the 🖼 border toggle. Same dialog as the tray menu item.

### Fixed / hardened — crash opening Settings from the tray
A report of the whole app crashing when opening Settings from the
tray menu (twice, reproducibly). The dialog-construction code has no
obvious thrower, so this release adds hard failure boundaries and
full diagnostics rather than a blind guess:

- **Global exception handlers** (App.xaml.cs): UI-thread exceptions
  are logged in full to the trace log and shown in an error dialog,
  and the app keeps running instead of dying. Non-UI fatal
  exceptions and unobserved task exceptions are logged too.
- **Settings opens through a local try/catch** with its own error
  report, from both the tray item and the new ⚙ button.
- If Settings still fails to open, the app now SURVIVES and shows
  the exact exception + writes it to the trace log (tray →
  Open trace log). That log pinpoints the root cause immediately.

### Changed
- Settings window is resizable now (was fixed-size) and slightly
  taller by default — the effects + quick-save sections added in
  v2.4.4 made the content list longer.

## [2.4.5] — 2026-07-08

### Fixed — v2.4.4 build errors/warnings

- **CS0246 (build blocker)**: the new annotate handler in
  MainWindow.xaml.cs declared a `BitmapSource` local but that file
  never imported `System.Windows.Media.Imaging`. Added the using.
- **CS0104 lying in wait**: ImageAnnotator.cs imports
  `System.Windows.Shapes` while ImplicitUsings globally imports
  `System.IO` — both define `Path`, making `new Path {…}` ambiguous.
  Added a `using Path = System.Windows.Shapes.Path;` alias.
- **CS8602 ×2**: the orphan sweep iterated `ItemsFileDto.Pinned` /
  `.Recent`, which are declared nullable (deserialization targets).
  Now iterates null-tolerantly.
- **CS8604 (pre-existing since before v2.4.3)**: Trace's writer
  thread passed nullable `_logPath` to StreamWriter. Added a
  pattern-match guard; the analyzer can't track initialization
  across threads.

## [2.4.4] — 2026-07-07

### Added — Quick-save screenshots to a folder
- Right-click any image slot → **💾 Save image to folder** copies the
  PNG into your quick-save folder with a friendly filename (nickname
  if set, else "Screenshot", plus a timestamp). Prompts for a folder
  on first use and remembers it in Settings.
- **Auto-save toggle** (Settings → Quick-save screenshots): every new
  capture is ALSO written to the folder immediately, named by
  timestamp. Never interrupts a capture with prompts — if the folder
  isn't set, it skips with a status hint.

### Added — Border toggle button in the header
- New 🖼 button next to minimize. Full opacity = borders ON, dimmed =
  OFF. Same source-of-truth as the tray menu item (both call one
  shared toggle). Tooltip clarifies it affects FUTURE captures only.

### Added — Double-click a clip to edit its nickname
- Single click still loads the item to the clipboard (unchanged).
  Double-click now opens the nickname editor directly — no need to
  dig into the right-click menu. (The first click of a double-click
  still loads the clip, which is harmless.)

### Added — Drop shadow + torn edge capture effects
- **Drop shadow**: soft dark falloff on the right + bottom edges so
  screenshots lift off the page. Settings checkbox.
- **Torn edges**: ragged torn-paper edges on any combination of the
  four sides (Top / Bottom / Left / Right checkboxes in Settings).
  All four = the full "ripped out of a magazine article" look.
  Deterministic per image size, so re-captures don't twitch.
- Effects bake in at capture time, in order: torn edges → border →
  drop shadow. Any effect triggers the live-clipboard replacement so
  hot-key pasting gets the processed version (same mechanism the
  border has always used).

### Added — Image annotation editor (MVP)
- Right-click any image slot → **🖍️ Annotate image…** opens a simple
  editor: **arrow / box / line** tools, red 3px stroke, **Undo**
  (button or Ctrl+Z), scrollable 1:1 canvas for large screenshots.
- **Save annotations** flattens the markup into the image, replaces
  the slot content, and loads the annotated version onto the live
  clipboard for immediate paste. Cancel (or Esc) discards.
- Deliberately minimal for v1: single color, no text tool, no
  freehand. If it proves useful, those can come next.

### Fixed
- **Cascaded image history items broke after restart.** When a Recent
  image cascaded into History, the history entry kept referencing the
  PNG file in the main images folder — but history loads from its own
  history\images\ folder, so the image silently failed to load after
  an app restart. History now always writes its own copy on cascade.
- **Orphaned PNG files accumulated forever.** Removing/replacing an
  image slot left its PNG on disk with nothing referencing it. The
  persistence layer now sweeps unreferenced PNGs from images\ after
  each save. (History images live in a separate folder and are
  managed by history's own eviction.)


## [2.4.3] — 2026-06-05

### Fixed
- **Window minimize sent it to the "show hidden icons" tray flyout
  instead of the taskbar.** When ClipNinja was launched at Windows
  startup via the `--hidden` flag, App.xaml.cs set `ShowInTaskbar=false`
  for the initial flash-and-hide HWND creation but never restored it
  to `true`. So every subsequent reveal of the window left it OFF the
  taskbar, and the minimize button effectively sent the window into
  the void. Two-part fix:
    1. App.xaml.cs now restores `ShowInTaskbar=true` after the initial
       flash-and-hide.
    2. `MainWindow.ToggleVisibility` defensively re-asserts
       `ShowInTaskbar=true` on every reveal, so even if some other path
       turns it off, the window comes back onto the taskbar.

### Cosmetic
- Slot context menu wording: "🗑️ Clear this slot" → "🗑️ Remove this item"
- Tray menu wording: "Clear all slots" → "Clear everything"
- Footer hints: "Click slot / Drag a slot" → "Click item / Drag item",
  and the right-click hint now says "Pin / Edit / Move / Remove"
- One stale doc comment mentioning the Memory Hole was updated to
  reflect the v2.4 model (no functional change)

## [2.4.2] — 2026-06-04

### Fixed
- **Clicked Pinned items had no visual highlight.** v2.4.1 fixed the
  wrong-row-highlights bug by clearing PasteIdx on Pinned clicks, but
  that left Pinned with no active feedback at all. Now Pinned clicks
  give the clicked row its own amber active highlight, symmetric to
  Recent. Internally: new `MainViewModel.SetActivePinned(item)` clears
  any Recent active highlight and marks one specific Pinned item active.
  Across both lists, exactly one row shows the amber active style at a
  time — the row you most recently clicked.

## [2.4.1] — 2026-06-04

### Fixed
- **Clicking a Pinned item highlighted the wrong row.** Clicking row N
  in the Pinned section would set `PasteIdx = N`, which always targeted
  RecentItems — so the Recent row at the same display index would light
  up as "active" instead of the Pinned row you actually clicked. Pinned
  clicks now leave PasteIdx untouched (since the Recent active-cycle
  doesn't apply to Pinned items at all). The clipboard write itself was
  always going to the right item; only the visual highlight was wrong.

## [2.4.0] — 2026-05-28

### Major UX rework — Pinned + Recent + History

This release replaces the three-zone "Memory Hole" model (slots 1-15 recent
+ 16-30 long-term + history) with a cleaner two-list model:

**Pinned**: a curated shelf at the top of the window. Items only end up
here when you explicitly pin them. Pinned items never move automatically
and have no cap.

**Recent**: a rolling list (default 50, configurable 25/50/100/200/500)
below Pinned. Newest copy always lands at position 1. When this list
overflows, the oldest item cascades into History.

**History**: unchanged from v2.3 — the rolling archive of items that
have rolled out of Recent.

### What changed
- Slots 16-30 (the old Memory Hole) are gone. Pinning is now THE
  mechanism for long-term storage.
- Newest copy always at the top of Recent.
- Pinning moves an item from Recent → top of Pinned. Unpinning moves
  it from Pinned → top of Recent.
- Re-copying something that's already Pinned: no-op (we don't disturb
  decided content).
- Re-copying something in Recent: promote to top (unchanged from v2.3).
- The Pinned section is hidden entirely when empty.
- Nicknames bumped from 20 to 50 characters.
- Search is unified — Ctrl+F filters BOTH Pinned and Recent
  simultaneously, by nickname OR display text.
- First-run empty state for Recent ("Copy something to get started.").

### Settings dialog
- New "Recent list" section with a max-items dropdown
  (25/50/100/200/500)
- About section now shows separate Pinned + Recent counts

### Migration
Existing v2.3 (and earlier) installations get migrated automatically on
first launch:
- Pinned slots stay pinned (move to the new Pinned list)
- Memory Hole content (slots 16-30 with content) becomes Pinned —
  the user clearly intended to keep these
- Slots 1-15 unpinned with content become Recent
- Empty slots are dropped
- The old `slots.json` is renamed to `slots.v1.bak` as a safety net;
  new state goes to `items.json`
- `SchemaVersion` in `settings.json` bumps to 2

### Storage
- `%AppData%\ClipNinja\items.json` — new pinned+recent file
- `%AppData%\ClipNinja\slots.v1.bak` — your old v1 file, untouched
- Everything else unchanged

### Implementation notes
- `MainViewModel.Slots` removed; replaced with `PinnedItems` +
  `RecentItems` ObservableCollections.
- `MainViewModel.ClearSlot` / `ClearAllSlots` removed; replaced with
  `RemoveItem`, `ClearAllRecent`, `ClearAllPinned`, `ClearEverything`.
- `PersistenceService.LoadSlots` / `SaveSlots` removed; replaced with
  `LoadItems` / `SaveItems`. v1→v2 migration runs once on first launch
  when `slots.json` is present but `items.json` isn't.
- Drag-and-drop simplified: no more Memory Hole edge cases. Within-list
  drag reorders; cross-list drag moves with pin/unpin.
- The 50-char nickname change is enforced in the InputPrompt's max
  length; storage already accommodated any length.

All notable changes to ClipNinja are documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [2.3.0] — 2026-05-27

### Added — Slot list search
- **Ctrl+F** in the main window opens a search bar above the slot list
- Filters slots by their visible text (substring match, case-insensitive)
- Empty slots are hidden during a search (matching nothing is the point)
- **Esc** or the **✕** button clears + closes the search
- Uses WPF's CollectionView Filter so the underlying slot collection is
  untouched — cascade, persistence, drag-and-drop all behave normally
  even with a filter applied

### Added — Drag-out to external apps
You can now drag any slot or history item OUT of ClipNinja and drop it
directly into another application (Word, Outlook, browser, image viewer,
etc.). The DataObject carries both the internal reorder format AND the
standard clipboard formats:

- **Text slots**: UnicodeText + Text + HTML + RTF (hyperlinks survive!)
- **Image slots**: CF_BITMAP / DIB + PNG byte stream
- **URL slots**: text + optional HTML/RTF if the source had them

External drops use Copy effect (slot stays in ClipNinja). Internal slot-to-slot
drops still use Move (the existing reorder logic). The receiver picks
which format to use based on its capabilities.

History rows can drag out too — purely external (no internal reorder
inside the history list).

### Added — Capture timestamp in preview popup
The hover preview now shows a small footer line at the bottom:

  📅 Captured: 5 min ago  (3:47 PM)

Useful for distinguishing similar-looking slots (especially after promotion
from history) and remembering when a particular clip was created.

### Changed
- **Title bar version badge** bumped to "v2.3"

## [2.2.2] — 2026-05-26

### Cleanup / polish pass before public release

- **Title bar version badge** now shows "v2.2" (was stuck at "v2.0")
- **Settings dialog disk usage and history count refresh** automatically
  after the user clicks "Clear all history items" — previously the
  values stayed stale until the dialog was closed and reopened
- **README.md and README.txt fully synced** with all v2.x features:
  rolling history, hyperlinks survival, GIF labeling, screenshot
  borders, settings dialog history section, double-click image
  fullsize, the 12th SF move, etc.
- **Architecture diagram in README.md** updated to reflect the current
  source tree (HistoryService, HistoryItem, SettingsDialog,
  StartupService, etc.)
- **`%AppData%\ClipNinja\` layout docs** updated to show history
  subfolders
- Final audit pass: no TODO/FIXME left in code, tracer confirmed off
  by default, no debug flags or hardcoded test values remaining

## [2.2.1] — 2026-05-26

### Changed — History panel UX parity with slot list

History rows now use the same icon/thumbnail layout as slots, with the
same hover-preview behavior:

- **Type-specific icons**: image thumbnail, green spreadsheet grid,
  ochre text-document, dusty-blue link-chain (instead of a plain
  text-only label)
- **GIF badge** on image rows whose source was a `.gif` file
- **Hover any row** → preview popup appears, same as slot hover
- **Image preview supports double-click-to-open-fullsize** (was
  already in the popup, now reachable from history too)
- **Hyperlink section in preview** (for text with embedded links)
  works for history items, with "Open in browser" buttons

### Implementation
- `HistoryItem` gained `HasGif`, `HasSpreadsheet`, `HasUrl`,
  `HasPlainText` derived properties to match `ClipSlot`'s surface
- History row `DataTemplate` rewritten to mirror the slot row template
  (just without the index badge, pin lock, drag arrows, and eraser —
  those don't apply to history items)
- `PreviewPopup.ScheduleShowContent(ClipContent, anchorTop)` new
  overload, parallel to the existing `ScheduleShow(ClipSlot, ...)`.
  Internally `ActuallyShow` resolves content from either pending entry
  point. Slot path unchanged; history just enters through a different
  door

## [2.2.0] — 2026-05-23

### Added — Rolling history
Items that cascade off the bottom of the recent area (slot 15) now flow
into a separate **history list** instead of being lost. Access via the
new **📜 History (N)** button at the bottom of the main window.

The history panel:
- Overlays the ninja stage and slot list when open (header drag bar and
  footer stay visible underneath)
- Most recent at the top; capped at 100 items by default (configurable
  to 50/100/200/500/1000 in the Settings dialog, or "Disabled" if you
  prefer the old behavior)
- **Double-click** an item to promote it back to slot 1 (single-click
  just selects, to avoid accidental clipboard replacement)
- **Right-click** for "Promote back to slot 1" or "Delete from history"
- **Search box** filters by visible text (substring match)
- Shows relative time stamps ("5 min ago", "yesterday", "Jan 12")
- 🔗 N badge on items that carry hyperlinks (from v2.1.0)
- Image thumbnails inline; full PNG kept on disk until evicted or
  cleared
- "History is empty" message when there's nothing to show

### Storage
- History list: `%AppData%\ClipNinja\history.json`
- History images: `%AppData%\ClipNinja\history\images\hist_<guid>.png`
- Image PNGs are deleted from disk when their history entry is removed
  (manually, by eviction, by Clear All, or by promotion back to a slot)

### Settings dialog updated
- New "History" section with a size dropdown (Disabled / 50 / 100 / 200
  / 500 / 1000) and a "🗑️ Clear all history items" button
- About section shows current history count alongside slot count, and
  disk-usage total now includes both `images\` and `history\images\`

### What's NOT in history
- Memory Hole slots (16-30) — these are explicit long-term storage; the
  cascade never displaces them, so nothing from them ever ends up in
  history
- Manually cleared slots — clearing a slot directly removes content,
  doesn't archive it
- Content from before v2.2.0 — earlier versions just dropped cascaded
  content; there's nothing to recover

## [2.1.0] — 2026-05-22

### Added — Hyperlink preservation for rich-text copies
When you copy text from Outlook, Word, a web page, or Teams that contains
embedded hyperlinks, ClipNinja now captures the full rich-text formats
(CF_HTML and CF_RTF) alongside the plain text. When you click the slot
to paste back, all three formats are written to the clipboard so the
destination app picks the richest one it understands:

- **Paste into Word/Outlook**: hyperlinks survive end-to-end, clickable
  just like the original copy
- **Paste into Notepad/code editors**: plain text only (as before)
- **Paste into Slack/Discord/Teams**: usually HTML — hyperlinks preserved

Previously, ClipNinja stored only plain text, so pasting a slot back
stripped any embedded hyperlinks from emails.

### Added — Hyperlink badge and preview panel
- **"🔗 N" badge** on slot rows when the captured text had N parsed
  hyperlinks. Hover the row to see the URLs in the preview popup.
- **"Links found in source" section** in the preview popup, listing
  each hyperlink as (label, URL) with an "Open in browser" button.
  Lets you act on links from a slot even when pasting it as plain text
  somewhere else (e.g. you stripped formatting on purpose but still
  want to open one of the links).

### Changed
- **Promote-on-recopy now upgrades formats** — re-copying the same
  text from a richer source (HTML with hyperlinks) replaces the
  stored plain-text-only version with the richer one. Best of both
  re-copy worlds.
- **Capture cap**: rich-format payloads >2MB are dropped (plain text
  retained). Prevents settings.json from ballooning with giant
  HTML-formatted selections.

### Implementation notes
- `TextContent` gained `HtmlFormat`, `RtfFormat`, and `Links`
  properties. CF_HTML payloads are stored verbatim including their
  byte-offset header so round-trip works correctly.
- Hyperlink parsing uses a tolerant regex (`<a href="...">label</a>`)
  rather than a strict HTML parser — Outlook/Word's CF_HTML is wildly
  varied and a regex catches 99% of links reliably.
- Persistence DTO extended with optional `HtmlFormat`, `RtfFormat`,
  and `Links` arrays. Forward-compatible: v2.0.x settings.json files
  load fine.

## [2.0.7] — 2026-05-21

### Fixed
- **Bordered images appeared tiny with empty space around them in the
  slot thumbnail, and showed a gray padding box when pasted into MS
  Teams.** Root cause: the bordered bitmap was being created at the
  source bitmap's native DPI (often 144 on high-DPI/scaled monitors),
  but the output's pixel count was used as-if it were the DIU count.
  Result: a 106-pixel-wide bordered bitmap reported a 70-DIU width
  to WPF, which then scaled it down to fit the thumbnail container,
  leaving large empty margins. Teams had the same issue rendering the
  pasted bitmap (Word was unaffected because it uses pixel-count sizing).
  Fix: produce the bordered bitmap at 96 DPI so DIU == pixels throughout.
- Existing slot images captured before this fix will still display the
  old behavior — recapture or clear those slots to get the corrected
  rendering. New captures from v2.0.7 onward are fine.

## [2.0.6] — 2026-05-20

### Changed
- **Auto-replace live clipboard with bordered version** — when the
  "Black border on screenshots" setting is on, the captured image's
  bordered version now goes onto the system clipboard immediately, not
  just into the ClipNinja slot. Take a screenshot in Greenshot, paste
  into Word: the border is there. No more "click the slot first"
  workflow. This is what users expected the auto-border feature to do
  all along.
- Implementation: the watcher fires a new
  `BorderedImageReadyForClipboardReplace` event after baking the border
  and storing the slot. MainWindow listens, sets the watcher's
  `LastWrittenSignature` to the bordered image's sig (so the resulting
  WM_CLIPBOARDUPDATE doesn't trigger a re-capture loop), extends the
  echo-suppression window by 1.5s, and calls `Clipboard.SetImage` with
  the bordered bitmap. Plain SetImage (the same path that worked from
  slot clicks).
- Failure is silent — if the live-clipboard write fails for any reason
  (clipboard locked by another app, etc.), the bordered version is
  still in the slot and clickable. Logged via tracer when enabled.

## [2.0.5] — 2026-05-20

### Added
- **Real Settings & About dialog** — the tray menu "Settings…" item now
  opens a proper window with checkboxes for Show Ninja, Add Black
  Border, and Launch on Startup, plus an About section showing the
  version, data folder path, filled slot count, and total disk usage.
  Includes an "Open data folder" button for quick access to
  `%AppData%\ClipNinja`.
- **Sun click hit target** — the visible sun is 40×40 but the clickable
  area is now 56×56 (transparent rectangle behind the visible sun).
  More forgiving target for high-DPI/touch displays.

### Fixed
- **Sun click broken in upper-left area** — the speech bubble Canvas
  was overlapping part of the sun and intercepting clicks because it's
  in the same Canvas at higher z-order. Set `IsHitTestVisible="False"`
  on the speech bubble so mouse events pass through to whatever's
  underneath. Speech bubble is purely decorative — there's nothing
  to click on it anyway.
- **Image debug log gated behind tracer** — was always-on during the
  border-paste diagnosis; now only writes when `Trace.Enabled = true`.
  No more dead diagnostic file accumulating in `%AppData%\ClipNinja`.

### Documented
- **README clarification on border + paste** — added a Tips section
  explaining that the auto-applied screenshot border only ends up on
  the clipboard when the user clicks the slot in ClipNinja. The capture
  itself doesn't replace the live clipboard (by design — ClipNinja is a
  manager, not an auto-modifier).

## [2.0.4] — 2026-05-20

### Added
- **GIF source detection** — when a .gif file is copied from File
  Explorer (or another file-aware source), the slot is now labeled
  "GIF" and shows a small red "GIF" pill in the bottom-right corner
  of the thumbnail. Doesn't animate the thumbnail (WPF doesn't natively
  support that without significant additional work), but at least the
  user can tell at a glance which slots hold animated content. Browser
  "Copy Image" on a GIF still results in a static-frame capture (no
  badge) since browsers don't include file references — there's no
  way for us to know the source was animated.
- **Always-on image clipboard diagnostic log** at
  `%AppData%\ClipNinja\image-debug.log`, accessible via tray menu
  "Open image-debug log…". Logs every image clipboard write with
  dimensions, formats sent, exceptions, and the list of formats
  present after the write. Independent of the main tracer setting.

### Changed
- **Image clipboard write rewritten** with explicit `Clipboard.Clear()`
  before write, PNG byte stream alongside the bitmap, and
  `SetDataObject(copy:true)` for cross-process persistence. Multiple
  formats describing the SAME bordered bitmap; whichever the target
  app picks during paste should show the border.

## [2.0.3] — 2026-05-20

### Changed
- **Border toggle UI revamped** — replaced the checkable menu item with
  an explicit-label menu item that reads either "Black border on
  screenshots: ON" or "Black border on screenshots: OFF". Less ambiguous
  than a checkmark, and immune to the WPF event-timing quirk that
  caused inverted behavior in earlier versions.
- **Reverted image clipboard write to plain `Clipboard.SetImage`** —
  the multi-format `SetDataObject` approach broke the Greenshot →
  Word border paste path in a way that wasn't worth chasing. Returning
  to the simpler call that worked reliably in earlier versions.

## [2.0.2] — 2026-05-20

### Added
- **Spinning Bird Kick** — Chun-Li's iconic Hazan Tenshokyaku, the
  upside-down spinning kick. Ninja crouches deep, leaps up, inverts
  body (full 1080° rotation = 3 spins), legs spread horizontal like
  helicopter blades, then lands clean. Shout: "SPINNING BIRD KICK!"
  Added to the random-move rotation AND the right-click sun menu.

## [2.0.1] — 2026-05-20

### Added
- **Speech bubble shouts** — the ninja yells the move name during each
  animation in a small comic-style speech bubble above his head.
  HADOUKEN! SHORYUKEN! TATSUMAKI! TA-DAAA! etc.
  Bubble pops in with a back-ease bounce, holds for the animation
  duration (min 0.6s), and fades out cleanly.
- **Per-move shout strings** — added a `Shout` property to NinjaAnimation
  and a lookup table for SF2-canonical names where they apply.

### Changed
- **Hadouken animation reworked into 3 distinct phases** with more
  dramatic body movement and a clearer chi-gathering moment:
    - 0.00–0.20s: stance prep, hands begin coming together LOW
    - 0.20–0.75s: ball materializes hip-height between cupped hands,
      grows to full size while body coils inward (full 0.55s charge)
    - 0.75–1.45s: explosive thrust forward, ball launches and travels
      across the stage, then exits fading
- Total animation is ~20% longer (1.8s vs 1.5s) so the chi-gathering
  phase reads clearly. The ninja sinks lower during charge and uncoils
  forward during the throw — better matches the reference.

### Fixed
- **Black border missing on paste regression** — `Clipboard.SetImage`
  alone wasn't enough; some apps (Word, Outlook) pick PNG-format
  clipboard data when available, and Greenshot's PNG entry (without
  border) was still present after `Clipboard.Clear() + SetImage`.
  Now uses `Clipboard.SetDataObject` with both PNG and Bitmap formats
  populated from the bordered bitmap, so whichever format the target
  app picks shows the border.

## [2.0.0] — 2026-05-19

ClipNinja's first 2.x release. The version bump is partly cosmetic (it shows up in the title bar now) and partly recognition that the app has crossed from "hobby experiment" into something stable and useful enough to ship to real people.

### Added
- **Version badge in the title bar** — a small "v2.0" sits next to the "🥷 ClipNinja" header so you can tell at a glance which version you're running.

### Fixed
- **Black border toggle inverted** — checking the box was disabling borders and vice versa. The handler was reading WPF's `MenuItem.IsChecked` at the wrong moment in the click lifecycle. Now toggles by inverting the source-of-truth setting directly, then pushing the new value out to the watcher and the checkbox visual. Same defensive pattern applied to the "Launch on Windows startup" toggle preemptively.
- **Black border missing on paste** — captured images had the border baked in (visible in the slot thumbnail) but pasting into Word/Outlook showed the un-bordered original. Cause: `Clipboard.SetImage` only overwrites the bitmap clipboard format, leaving other formats (CF_DIB, PNG, etc.) from the original screenshot tool intact — and Word would pick those during paste. Fixed by calling `Clipboard.Clear()` immediately before `SetImage`, ensuring only the bordered version is on the clipboard.
- **Orphan pinned-empty slots** — slots that were pinned-but-empty couldn't be unpinned because the lock icon was hidden (when slot was empty) AND the click handler bailed out (when slot was empty). Now the lock icon is shown on any pinned slot regardless of content, and clicking unpin works for empty-pinned slots. Lets users clean up legacy state from older versions.
- **Memory Hole "full" message wasn't visible** — refusing a drop into a full hole only updated the small StatusText strip, which is easy to miss. Now also shows a real MessageBox modal with the slot number and instructions on how to make space.
- **Memory Hole drops could overwrite content silently** — earlier logic would accept any unpinned slot as an "absorber" for the shift cascade, meaning unpinned but occupied memory hole slots could be silently overwritten. Now requires a truly EMPTY absorber slot. Refuses the drop with a popup if no empty slot exists at-or-after the drop target.

### Notes
- Existing settings.json files from v1.x are forward-compatible. Slot positions, pin states, and saved preferences carry over.
- The `global.json` pinning to .NET 8 SDK is now bundled with the source for anyone cloning from GitHub on a machine that has both .NET 8 and .NET 10 installed.

## [1.4.0] — 2026-05-19

### Added
- **Promote-on-recopy de-duplication** — if you copy something that's already
  in the recent area (slots 1-15), the existing slot is promoted to slot 1
  rather than creating a duplicate that pushes everything else down. Memory
  Hole contents are unaffected.
- **Auto-scroll to slot 1 on capture** — when a new clipboard event lands
  in slot 1, the list scrolls to the top automatically. No more wondering
  whether a capture happened while you were browsing the Memory Hole.
- **Auto-scroll to slot 1 on click** — clicking any slot in the top 3 also
  brings the list back to the top so you see what's active.
- **Black border on screenshots** — captured images get a 3px black border
  baked in by default. Toggle from the tray context menu. Helpful for
  screenshots pasted into docs that would otherwise blend with the page.
- **Memory Hole drag fix** — dragging a slot into the Memory Hole now
  inserts and shifts items DOWN within the hole rather than swapping
  positions (which used to bump items OUT of the hole). If the hole has
  no space at-or-after the drop target, a "Memory Hole is full" message
  appears and the drop is refused.
- **Memory Hole → Recent drag** — dragging an item OUT of the hole into
  the recent area swaps positions instead of leaving a gap in the hole.
  Keeps the long-term storage density stable.

### Changed
- **Link thumbnail color** — recolored from bright blue (`#1E5FE0`) to the
  dusty sky blue (`#99AEB5`) of the ninja stage backdrop. Chain icon
  stroke darkened to deep navy (`#1B3A55`) for legibility.
- **Excel thumbnail color** — recolored from forest green (`#1E6E3E`) to
  muted sage (`#A8CBA0`). Grid lines darkened to (`#305A2A`) for contrast.

## [1.3.0] — 2026-05-14

### Added
- **URL detection** — if a slot contains a single URL (http/https/ftp/file scheme, or a `www.`-prefixed host) it's flagged automatically. Multi-line text with a URL embedded inside is still treated as plain text.
- **Link-chain thumbnail** — URL slots show a blue chain-link icon (two interlocked links on a blue background) instead of the plain text document icon, so links are instantly recognizable in the slot list.
- **Open in browser button** in the hover preview popup for URL slots — clicks shell out to `Process.Start` with `UseShellExecute=true`, opening the link in the user's default browser.

### Changed
- `HasPlainText` now excludes URL slots (they get their own `HasUrl` category and icon).

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
