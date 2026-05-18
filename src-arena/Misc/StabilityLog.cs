using System.Globalization;
using System.IO;

namespace eft_dma_radar.Arena.Misc
{
    /// <summary>
    /// Append-only CSV recorder for aimview stability metrics. Writes to two
    /// timestamped CSV files under
    /// <c>%AppData%\eft-dma-radar-arena\stability\</c>:
    /// <list type="bullet">
    ///   <item><b>stability-{utc}.csv</b> — one row per per-second tick, aggregate
    ///     metrics (mean head/eye jitter, bone↔eye offset mean ± std-dev).</item>
    ///   <item><b>stability-{utc}-players.csv</b> — one row per visible player per
    ///     per-second tick, with the base pointer, raw position / rotation reads
    ///     coming out of the realtime scatter, world position of the head and
    ///     lower-foot bones (skeleton scatter), and the matching screen-space
    ///     projections. Lets you see exactly what each player's data looks like
    ///     at the moment a stability sample was taken and spot optimisation
    ///     opportunities (e.g. position+rotation always lagging bones by one tick).</item>
    /// </list>
    /// Both files share the same timestamp suffix so a session's pair is easy to find.
    /// <para>
    /// Failure modes are non-fatal: if a file can't be opened the recorder marks
    /// itself failed for THAT file and continues writing the other one. The hot
    /// path is a single <see cref="bool"/> check when a writer is disabled.
    /// </para>
    /// </summary>
    public static class StabilityLog
    {
        private static StreamWriter? _aggregateWriter;
        private static StreamWriter? _playerWriter;
        private static readonly Lock _lock = new();
        private static bool _initialized;
        private static bool _aggregateFailed;
        private static bool _playerFailed;
        private static string? _aggregatePath;
        private static string? _playerPath;
        private static string? _sessionStamp;

        private const string AggregateHeader =
            "unix_ms,utc_iso,mode,frames,drawn,candidates," +
            "head_jitter_px,eye_jitter_px,bone_eye_avg_px,bone_eye_std_px,bone_eye_samples";

        private const string PlayerHeader =
            "unix_ms,utc_iso,player_base,name,type,is_local,is_ai," +
            "is_alive,health_status,ohc_addr," +
            "pos_x,pos_y,pos_z,yaw,pitch," +
            "head_world_x,head_world_y,head_world_z,foot_world_x,foot_world_y,foot_world_z," +
            "eye_scr_x,eye_scr_y,head_scr_x,head_scr_y,foot_scr_x,foot_scr_y," +
            "head_y_delta_px,eye_y_delta_px,distance_m";

        /// <summary>Absolute path of the aggregate CSV (null until first successful write).</summary>
        public static string? AggregateFilePath => _aggregatePath;

        /// <summary>Absolute path of the per-player CSV (null until first successful write).</summary>
        public static string? PlayerFilePath => _playerPath;

        // Lazily initialise both files together so they share an identical timestamp suffix.
        private static void EnsureInitialized()
        {
            if (_initialized) return;
            lock (_lock)
            {
                if (_initialized) return;
                _initialized = true;
                try
                {
                    var dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "eft-dma-radar-arena", "stability");
                    Directory.CreateDirectory(dir);
                    _sessionStamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

                    _aggregatePath = Path.Combine(dir, $"stability-{_sessionStamp}.csv");
                    _aggregateWriter = OpenCsv(_aggregatePath, AggregateHeader);
                    if (_aggregateWriter is null) _aggregateFailed = true;

                    _playerPath = Path.Combine(dir, $"stability-{_sessionStamp}-players.csv");
                    _playerWriter = OpenCsv(_playerPath, PlayerHeader);
                    if (_playerWriter is null) _playerFailed = true;

                    if (_aggregateWriter is not null || _playerWriter is not null)
                    {
                        Log.WriteLine(
                            $"[StabilityLog] Writing stability metrics. aggregate={_aggregatePath} players={_playerPath}");
                    }

                    AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                    {
                        StreamWriter? a, p;
                        lock (_lock)
                        {
                            a = _aggregateWriter; _aggregateWriter = null;
                            p = _playerWriter;    _playerWriter    = null;
                        }
                        try { a?.Dispose(); } catch { }
                        try { p?.Dispose(); } catch { }
                    };
                }
                catch (Exception ex)
                {
                    _aggregateFailed = _playerFailed = true;
                    Log.Write(AppLogLevel.Warning,
                        $"[StabilityLog] Init failed: {ex.Message}; metrics will not be persisted.");
                }
            }
        }

        private static StreamWriter? OpenCsv(string path, string header)
        {
            try
            {
                var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                var w  = new StreamWriter(fs, Encoding.UTF8, 0x1000) { AutoFlush = true };
                w.WriteLine(header);
                return w;
            }
            catch (Exception ex)
            {
                Log.Write(AppLogLevel.Warning,
                    $"[StabilityLog] Failed to open '{path}': {ex.Message}; this file will not be written.");
                return null;
            }
        }

        /// <summary>
        /// Append one row of aggregate aimview stability metrics. Thread-safe.
        /// </summary>
        public static void Record(
            string mode, int frames, int drawn, int candidates,
            double headJitterPx, double eyeJitterPx,
            double boneEyeAvgPx, double boneEyeStdPx, int boneEyeSamples)
        {
            if (_aggregateFailed) return;
            EnsureInitialized();
            var w = _aggregateWriter;
            if (w is null) return;

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var iso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            var line = FormattableString.Invariant(
                $"{nowMs},{iso},{mode},{frames},{drawn},{candidates},{headJitterPx:F3},{eyeJitterPx:F3},{boneEyeAvgPx:F3},{boneEyeStdPx:F3},{boneEyeSamples}");
            lock (_lock)
            {
                try { w.WriteLine(line); }
                catch (Exception ex)
                {
                    _aggregateFailed = true;
                    Log.Write(AppLogLevel.Warning,
                        $"[StabilityLog] Aggregate write failed: {ex.Message}; aggregate logging disabled.");
                }
            }
        }

        /// <summary>
        /// Append one row of per-player stability data. <paramref name="playerBase"/>
        /// and <paramref name="ohcAddr"/> are emitted in hex (<c>0x...</c>) so they
        /// sort naturally and match the debug-log/MatchDumper format. All
        /// <see cref="float"/> values use <see cref="CultureInfo.InvariantCulture"/>
        /// so the CSV is portable across regional decimal-separator settings.
        /// </summary>
        public static void RecordPlayer(
            ulong playerBase, string name, string type, bool isLocal, bool isAI,
            bool isAlive, string healthStatus, ulong ohcAddr,
            Vector3 position, float yaw, float pitch,
            Vector3 headWorld, Vector3 footWorld,
            Vector2 eyeScr, Vector2 headScr, Vector2 footScr,
            float headYDeltaPx, float eyeYDeltaPx, float distanceM)
        {
            if (_playerFailed) return;
            EnsureInitialized();
            var w = _playerWriter;
            if (w is null) return;

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var iso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            // Escape commas/quotes/CR/LF in name (defensive — Arena names usually clean ASCII).
            var safeName = NeedsCsvQuoting(name) ? CsvQuote(name) : name;
            var ohcCell  = ohcAddr != 0 ? "0x" + ohcAddr.ToString("X") : "";
            var line = FormattableString.Invariant(
                $"{nowMs},{iso},0x{playerBase:X},{safeName},{type},{isLocal},{isAI},{isAlive},{healthStatus},{ohcCell},{position.X:F3},{position.Y:F3},{position.Z:F3},{yaw:F2},{pitch:F2},{headWorld.X:F3},{headWorld.Y:F3},{headWorld.Z:F3},{footWorld.X:F3},{footWorld.Y:F3},{footWorld.Z:F3},{eyeScr.X:F1},{eyeScr.Y:F1},{headScr.X:F1},{headScr.Y:F1},{footScr.X:F1},{footScr.Y:F1},{headYDeltaPx:F2},{eyeYDeltaPx:F2},{distanceM:F2}");
            lock (_lock)
            {
                try { w.WriteLine(line); }
                catch (Exception ex)
                {
                    _playerFailed = true;
                    Log.Write(AppLogLevel.Warning,
                        $"[StabilityLog] Player write failed: {ex.Message}; player logging disabled.");
                }
            }
        }

        // Set of characters that force CSV quoting per RFC 4180.
        private static readonly System.Buffers.SearchValues<char> _csvSpecials =
            System.Buffers.SearchValues.Create(",\"\n\r");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool NeedsCsvQuoting(string s) =>
            s.AsSpan().IndexOfAny(_csvSpecials) >= 0;

        private static string CsvQuote(string s) =>
            "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
