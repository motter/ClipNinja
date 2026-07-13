using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace ClipNinjaV2.Services;

/// <summary>
/// In-app updates via GitHub Releases. No external update framework —
/// same zero-dependency philosophy as the rest of ClipNinja.
///
/// Flow:
///  1. Check: GET /repos/{owner}/{repo}/releases/latest, compare the
///     tag (v2.7.0) against the running assembly version.
///  2. Download: prefer a .exe asset; else a .zip asset containing the
///     published exe (that's what the GitHub Actions workflow in
///     .github/workflows/release.yml attaches on tag push).
///  3. Apply: the running exe can't overwrite itself, so we write a
///     tiny .cmd to %TEMP% that retry-copies the new exe over the
///     current one (the copy succeeds the moment this process exits
///     and releases the file lock), relaunches, and deletes itself.
///     Caller shuts the app down right after starting the script.
///
/// Unauthenticated GitHub API allows 60 requests/hour per IP — plenty
/// for update checks. Private repos would need a token; this targets
/// a public repo.
/// </summary>
public static class UpdateService
{
    public sealed class UpdateInfo
    {
        public Version Latest = new(0, 0, 0);
        public string TagName = "";
        public string Notes = "";
        public string AssetUrl = "";
        public string AssetName = "";
    }

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // GitHub rejects requests without a User-Agent.
        c.DefaultRequestHeaders.UserAgent.ParseAdd("ClipNinja-Updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>The version this process is running. csproj &lt;Version&gt;
    /// flows into the assembly version at build time.</summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>
    /// Query the latest release. Returns (info, null) when a NEWER
    /// version exists, (null, null) when up to date, (null, error)
    /// on failure — the caller can show the error or stay silent for
    /// background checks.
    /// </summary>
    public static async Task<(UpdateInfo? update, string? error)> CheckAsync(string repo)
    {
        if (string.IsNullOrWhiteSpace(repo) || !repo.Contains('/'))
            return (null, "No GitHub repo configured (Settings → Updates, e.g. \"yourname/ClipNinjaV2\").");
        try
        {
            var json = await Http.GetStringAsync(
                $"https://api.github.com/repos/{repo.Trim().Trim('/')}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString() ?? "";
            var verText = tag.TrimStart('v', 'V');
            if (!Version.TryParse(verText, out var latest))
                return (null, $"Couldn't parse release tag '{tag}' as a version — tag releases like v2.7.0.");

            // Normalize to 3 components so 2.7.0 vs 2.7.0.0 compares equal.
            var current = CurrentVersion;
            var latestN = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));
            var currentN = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
            if (latestN <= currentN) return (null, null);  // up to date

            var info = new UpdateInfo
            {
                Latest = latestN,
                TagName = tag,
                Notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            };

            // Asset preference: bare .exe beats .zip (no extraction step);
            // skip anything named like a source archive — the updater
            // wants the PUBLISHED build, not the code.
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString() ?? "";
                    var url = a.GetProperty("browser_download_url").GetString() ?? "";
                    if (name.Contains("source", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        info.AssetUrl = url; info.AssetName = name;
                        break;  // exe wins outright
                    }
                    if (info.AssetUrl == "" && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        info.AssetUrl = url; info.AssetName = name;
                    }
                }
            }
            if (info.AssetUrl == "")
                return (null, $"Release {tag} has no .exe or .zip asset to install. " +
                              "The GitHub Actions workflow attaches one automatically on tag push.");
            return (info, null);
        }
        catch (HttpRequestException hex)
        {
            return (null, $"GitHub request failed: {hex.Message} (repo private, missing, or offline?)");
        }
        catch (Exception ex)
        {
            Trace.Log("update", $"check failed: {ex}");
            return (null, $"Update check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Download the asset, stage the new exe, launch the swap script.
    /// On success the process MUST exit promptly (the script waits for
    /// the file lock to release). Returns an error string, or null on
    /// success.
    /// </summary>
    public static async Task<string?> DownloadAndStageAsync(UpdateInfo info)
    {
        try
        {
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe))
                return "Can't locate the running exe (unsupported host).";

            var stageDir = Path.Combine(Path.GetTempPath(), $"ClipNinjaUpdate_{Guid.NewGuid():N}");
            Directory.CreateDirectory(stageDir);

            // Download the asset.
            var assetPath = Path.Combine(stageDir, info.AssetName);
            Trace.Log("update", $"downloading {info.AssetUrl}");
            var bytes = await Http.GetByteArrayAsync(info.AssetUrl);
            await File.WriteAllBytesAsync(assetPath, bytes);

            // Locate the new exe: direct download, or inside the zip.
            string newExe;
            if (assetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                newExe = assetPath;
            }
            else
            {
                var extractDir = Path.Combine(stageDir, "x");
                System.IO.Compression.ZipFile.ExtractToDirectory(assetPath, extractDir);
                // Prefer an exe matching our own name; else the first exe found.
                var ownName = Path.GetFileName(currentExe);
                newExe = Directory.EnumerateFiles(extractDir, "*.exe", SearchOption.AllDirectories)
                    .OrderByDescending(p => string.Equals(Path.GetFileName(p), ownName, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault() ?? "";
                if (newExe == "") return "The release zip contains no .exe.";
            }

            // Swap script: back up the outgoing exe as .bak (instant
            // local rollback: rename the .bak if an update ever
            // misbehaves), then retry-copy the new exe (locked until we
            // exit), relaunch, self-delete. Capped at ~2 minutes of
            // retries so a wedged process can't leave an immortal loop.
            var script = Path.Combine(stageDir, "apply-update.cmd");
            await File.WriteAllTextAsync(script, $"""
                @echo off
                set tries=0
                :retry
                set /a tries+=1
                if %tries% gtr 120 exit /b 1
                timeout /t 1 /nobreak >nul
                copy /y "{currentExe}" "{currentExe}.bak" >nul 2>&1
                copy /y "{newExe}" "{currentExe}" >nul 2>&1
                if errorlevel 1 goto retry
                start "" "{currentExe}"
                exit /b 0
                """);

            Trace.Log("update", $"staged {info.TagName}; launching swap script");
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{script}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = stageDir,
            });
            return null;
        }
        catch (Exception ex)
        {
            Trace.Log("update", $"stage failed: {ex}");
            return $"Update failed: {ex.Message}";
        }
    }
}
