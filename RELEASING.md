# Releasing ClipNinja

One-time setup, then every release is three commands.

## One-time setup

1. Create the GitHub repo (public) and push this folder as its root:

   ```bash
   cd ClipNinjaV2
   git init
   git add .
   git commit -m "ClipNinja v2.7.0"
   git branch -M main
   git remote add origin https://github.com/YOURNAME/ClipNinjaV2.git
   git push -u origin main
   ```

2. In the running app: Settings (⚙) → **Updates** → set the GitHub
   repo to `YOURNAME/ClipNinjaV2`. Do this once; it persists. (You can
   also hard-code your repo as the default in
   `Models/AppSettings.cs` → `UpdateRepo` before your first release so
   fresh installs are pre-wired.)

## Every release

1. Bump the version in TWO places (keep them matching):
   - `ClipNinjaV2.csproj` → `<Version>2.7.1</Version>`
   - `MainWindow.xaml` → header `<Run Text="v2.9.2 "` (version Run
     comes BEFORE the name Run, and has a TRAILING space)

2. Update `CHANGELOG.md`, commit.

3. Tag and push:

   ```bash
   git add -A && git commit -m "v2.7.1"
   git tag v2.7.1
   git push && git push --tags
   ```

That's it. The GitHub Actions workflow (`.github/workflows/release.yml`)
builds the self-contained exe, zips it as `ClipNinja-win-x64.zip`, and
attaches it to a GitHub Release for the tag. Takes ~3-5 minutes; watch
the Actions tab.

## How updates reach the app

- Running installs check the repo's latest release ~5 seconds after
  startup (Settings → Updates → toggle) and show a status-bar hint if
  newer.
- Tray → **Check for updates…** (or Settings → Check now) walks the
  full flow: shows release notes, downloads the zip asset, swaps the
  exe, restarts. The swap uses a small retry script in %TEMP% that
  waits for the app to exit before overwriting.

## Rules that keep the updater happy

- Tags must look like `v2.7.1` (the updater strips the `v` and parses
  the rest as a version).
- The release must carry a published-exe asset (`.zip` with the exe
  inside, or a bare `.exe`). The workflow does this automatically —
  don't delete its asset and don't attach source zips *named without*
  "source" (assets with "source" in the name are ignored).
- The tag's version must be GREATER than the running one or clients
  will consider themselves up to date.
