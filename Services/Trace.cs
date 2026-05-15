using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace ClipNinjaV2.Services;

/// <summary>
/// Lightweight diagnostic tracer. Writes timestamped log entries to
/// %AppData%\ClipNinja\trace.log so we can see where time is going
/// when the app feels laggy.
///
/// Usage:
///     Trace.Log("category", "message");
///     using (Trace.Time("category", "operation")) { ... heavy work ... }
///
/// The Time() helper logs both start and end with the elapsed milliseconds.
/// File writes are batched on a background thread to keep the hot path fast.
/// </summary>
public static class Trace
{
    private static readonly ConcurrentQueue<string> _queue = new();
    private static readonly DateTime _started = DateTime.UtcNow;
    private static string? _logPath;
    private static Thread? _writer;
    private static readonly object _initLock = new();

    /// <summary>Set to true to enable logging (off by default in production).</summary>
    public static bool Enabled { get; set; } = false;

    /// <summary>One-time setup: only runs when logging is first turned on.</summary>
    private static void EnsureInitialized()
    {
        if (_logPath is not null) return;
        lock (_initLock)
        {
            if (_logPath is not null) return;
            _logPath = InitLogPath();
            _writer = StartWriterThread();
        }
    }

    private static string InitLogPath()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClipNinja");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "trace.log");

            // Rotate: if existing file is over 1 MB, move to trace.log.prev
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 1_000_000)
                {
                    var prev = Path.Combine(dir, "trace.log.prev");
                    if (File.Exists(prev)) File.Delete(prev);
                    File.Move(path, prev);
                }
            }
            catch { /* rotation is best-effort */ }

            // Header line for new sessions
            try
            {
                File.AppendAllText(path,
                    $"\n=== ClipNinja session started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
            }
            catch { /* ignore */ }
            return path;
        }
        catch
        {
            return Path.Combine(Path.GetTempPath(), "clipninja-trace.log");
        }
    }

    private static Thread StartWriterThread()
    {
        var t = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "ClipNinjaTraceWriter",
        };
        t.Start();
        return t;
    }

    private static void WriterLoop()
    {
        while (true)
        {
            try
            {
                if (_queue.IsEmpty)
                {
                    Thread.Sleep(50);
                    continue;
                }
                // Batch up to ~100 lines per file write
                using var sw = new StreamWriter(_logPath, append: true);
                int wrote = 0;
                while (wrote < 100 && _queue.TryDequeue(out var line))
                {
                    sw.WriteLine(line);
                    wrote++;
                }
            }
            catch
            {
                // If the log file is locked or anything else fails, just back
                // off and try again — never crash on tracing.
                Thread.Sleep(200);
            }
        }
    }

    /// <summary>Append a one-line entry to the log.</summary>
    public static void Log(string category, string message)
    {
        if (!Enabled) return;
        EnsureInitialized();
        var now = DateTime.UtcNow;
        var elapsed = (now - _started).TotalMilliseconds;
        var threadId = Thread.CurrentThread.ManagedThreadId;
        _queue.Enqueue(
            $"[{now:HH:mm:ss.fff}] [+{elapsed,8:F0}ms] [t{threadId,2}] [{category,-12}] {message}");
    }

    /// <summary>
    /// Time a block of work. Returns an IDisposable — wrap heavy operations
    /// in a `using` block and the elapsed ms is logged on exit. When the
    /// tracer is disabled, returns a singleton no-op for zero overhead.
    /// </summary>
    public static IDisposable Time(string category, string message)
    {
        if (!Enabled) return NoOpScope.Instance;
        return new TimedScope(category, message);
    }

    private sealed class NoOpScope : IDisposable
    {
        public static readonly NoOpScope Instance = new();
        public void Dispose() { }
    }

    private sealed class TimedScope : IDisposable
    {
        private readonly string _category;
        private readonly string _message;
        private readonly System.Diagnostics.Stopwatch _sw;
        public TimedScope(string cat, string msg)
        {
            _category = cat;
            _message = msg;
            _sw = System.Diagnostics.Stopwatch.StartNew();
            Log(_category, $"BEGIN {_message}");
        }
        public void Dispose()
        {
            _sw.Stop();
            Log(_category, $"END   {_message}  ({_sw.ElapsedMilliseconds}ms)");
        }
    }
}
