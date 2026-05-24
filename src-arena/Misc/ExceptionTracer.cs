using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using VmmSharpEx;

namespace eft_dma_radar.Arena.Misc
{
    internal static class ExceptionTracer
    {
        private static readonly ConcurrentDictionary<string, int> _seen = new(StringComparer.Ordinal);
        private static int _installed;
        private static int _totalLogged;

        public const int MaxDistinctSites = 200;
        public static bool Enabled { get; set; } = false;

        public static void Install()
        {
            // ARENA_TRACE_DMA_EXCEPTIONS env var is an override for shell-launched
            // debugging — when present it forces Enabled=true regardless of
            // config / UI state.
            var env = Environment.GetEnvironmentVariable("ARENA_TRACE_DMA_EXCEPTIONS");
            if (!string.IsNullOrEmpty(env) && (env == "1" || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase)))
                Enabled = true;

            // Always install the hook — it's the only way live-toggling from
            // the UI can pick up exceptions thrown after the toggle flips.
            // The handler short-circuits on !Enabled so an unwanted install
            // costs ~one branch per first-chance exception, which is fine.
            if (Interlocked.Exchange(ref _installed, 1) == 1) return;
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
            if (Enabled)
            {
                Log.WriteLine("[ExceptionTracer] First-chance DMA exception tracing ENABLED. " +
                              $"Each unique call site will log once (max {MaxDistinctSites}).");
            }
            else
            {
                Log.WriteLine("[ExceptionTracer] Hook installed; tracing OFF. " +
                              "Flip the UI toggle to start logging without a restart.");
            }
        }

        private static void OnFirstChance(object? sender, FirstChanceExceptionEventArgs e)
        {
            // Live gate — checked at fire time so the UI toggle takes effect
            // immediately. Cheap branch on the hot path.
            if (!Enabled) return;
            var ex = e.Exception;
            // Filter list: DMA-layer failures (the original target) plus common
            // app-layer bugs that show up as first-chance exceptions in the
            // debugger output without ever being surfaced in a log. The Arena
            // match log showed ArgumentOutOfRangeException firing every few
            // seconds with no visible cause — same dedup-by-site treatment
            // surfaces it without spamming.
            if (ex is not VmmException
                && ex is not BadPtrException
                && ex is not ArgumentOutOfRangeException) return;
            if (_totalLogged >= MaxDistinctSites) return;
            var trace = new System.Diagnostics.StackTrace(1, fNeedFileInfo: true);
            string siteKey = BuildSiteKey(ex, trace);
            if (!_seen.TryAdd(siteKey, 1)) return;
            int n = Interlocked.Increment(ref _totalLogged);
            Log.WriteLine($"[ExceptionTracer #{n}] {ex.GetType().Name}: {ex.Message}");
            Log.WriteLine(trace.ToString());
            if (n == MaxDistinctSites)
                Log.WriteLine($"[ExceptionTracer] Reached limit ({MaxDistinctSites}) — further unique sites will be suppressed.");
        }

        private static string BuildSiteKey(Exception ex, System.Diagnostics.StackTrace trace)
        {
            var sb = new StringBuilder(ex.GetType().Name);
            var frames = trace.GetFrames();
            if (frames is null) return sb.ToString();
            int added = 0;
            foreach (var frame in frames)
            {
                var m = frame.GetMethod();
                if (m is null) continue;
                var declType = m.DeclaringType;
                if (declType == typeof(ExceptionTracer)) continue;
                sb.Append('|').Append(declType?.FullName ?? "?").Append('.').Append(m.Name);
                if (++added >= 8) break;
            }
            return sb.ToString();
        }
    }
}
