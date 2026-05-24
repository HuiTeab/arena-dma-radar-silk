using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace eft_dma_radar.Arena.Unity.PhysX
{
    /// <summary>
    /// Rolling-window aggregator of which actors blocked sightlines, fed by
    /// <see cref="VisibilityWorker"/> at the end of every tick. Exists so the
    /// debug UI can answer the one question that actually drives rule
    /// engineering — "which actor is causing the most problems right now?" —
    /// without re-aggregating from scratch every UI frame.
    /// <para>
    /// Why a rolling window vs. an all-time counter: vischeck quality is
    /// situational. A perma-counter gets dominated by whatever wall you
    /// stared at for the first 30 seconds of the match and stops surfacing
    /// new offenders. A 30 s window keeps the list responsive to whatever's
    /// happening *now* — which is the moment the user reaches for the debug
    /// overlay in the first place.
    /// </para>
    /// <para>
    /// Storage model: append-only ring of hits with timestamps. Pruning
    /// happens lazily on every record + on every read, so the buffer never
    /// grows unbounded even if nothing's reading. 60 Hz × 8 players ×
    /// 30 s = 14 400 max entries — a few hundred KB of memory, trivial.
    /// </para>
    /// </summary>
    internal static class BlockerHistory
    {
        // ── Configuration ────────────────────────────────────────────────────

        /// <summary>How far back the rolling window reaches. 30 s = long
        /// enough that a transient blocker spike survives a few ticks of
        /// looking elsewhere, short enough to feel "live".</summary>
        public const int WindowMs = 30_000;

        // ── Internal state ───────────────────────────────────────────────────

        private readonly struct Hit
        {
            public Hit(int actorIdx, long tickMs, ulong playerBase)
            {
                ActorIdx   = actorIdx;
                TickMs     = tickMs;
                PlayerBase = playerBase;
            }
            public int   ActorIdx   { get; }
            public long  TickMs     { get; }
            public ulong PlayerBase { get; }
        }

        // ReaderWriterLockSlim would be overkill — the UI reads ~60 Hz and
        // the worker writes at the same rate; a plain Monitor lock keeps
        // things simple and the critical sections are < 50 µs.
        private static readonly Lock _lock = new();
        private static readonly List<Hit> _hits = new(2048);

        // ── Hot path: per-tick recording (called from VisibilityWorker) ──────

        /// <summary>
        /// Records every blocker hit in <paramref name="results"/> against the
        /// rolling window. Safe to call from the worker thread on every tick;
        /// no allocation in the steady state. Auto-prunes anything older than
        /// <see cref="WindowMs"/> on each call.
        /// </summary>
        public static void RecordTick(IReadOnlyList<VisibilityWorker.PlayerCheckResult> results, long tickMs)
        {
            if (results is null || results.Count == 0) return;
            lock (_lock)
            {
                for (int i = 0; i < results.Count; i++)
                {
                    var r = results[i];
                    if (r.BlockerActorIdx >= 0)
                        _hits.Add(new Hit(r.BlockerActorIdx, tickMs, r.PlayerBase));
                }
                PruneLocked(tickMs - WindowMs);
            }
        }

        /// <summary>Wipes the window — called on match end / scene swap.</summary>
        public static void Clear()
        {
            lock (_lock) _hits.Clear();
        }

        // ── Read path: aggregated view for the UI ────────────────────────────

        /// <summary>
        /// Aggregated row for the Top Blockers table: actor identity + how
        /// often / how recently it caused a block + how many distinct players
        /// it affected. UniquePlayers helps the user spot whether one actor
        /// is hosing the entire lobby vs. just being noisy on one player.
        /// </summary>
        public readonly struct Aggregate
        {
            public Aggregate(int actorIdx, int count, int uniquePlayers, long lastSeenMs)
            {
                ActorIdx      = actorIdx;
                Count         = count;
                UniquePlayers = uniquePlayers;
                LastSeenMs    = lastSeenMs;
            }
            public int  ActorIdx      { get; }
            public int  Count         { get; }
            public int  UniquePlayers { get; }
            public long LastSeenMs    { get; }
        }

        /// <summary>
        /// Returns up to <paramref name="max"/> rows, sorted by hit count
        /// descending. Pruned and re-aggregated each call (cheap — the
        /// window holds at most a few thousand hits and the dictionary
        /// pass is O(N)).
        /// </summary>
        public static List<Aggregate> GetTop(int max = 50)
        {
            long nowMs  = Environment.TickCount64;
            long cutoff = nowMs - WindowMs;

            var byActor = new Dictionary<int, (int Count, HashSet<ulong> Players, long LastSeen)>();

            lock (_lock)
            {
                PruneLocked(cutoff);
                for (int i = 0; i < _hits.Count; i++)
                {
                    var h = _hits[i];
                    if (!byActor.TryGetValue(h.ActorIdx, out var agg))
                        agg = (0, new HashSet<ulong>(), 0);
                    agg.Count++;
                    agg.Players.Add(h.PlayerBase);
                    if (h.TickMs > agg.LastSeen) agg.LastSeen = h.TickMs;
                    byActor[h.ActorIdx] = agg;
                }
            }

            return byActor
                .Select(kv => new Aggregate(kv.Key, kv.Value.Count, kv.Value.Players.Count, kv.Value.LastSeen))
                .OrderByDescending(a => a.Count)
                .ThenByDescending(a => a.LastSeenMs)
                .Take(max)
                .ToList();
        }

        /// <summary>Total hits currently in the window — for the panel header.</summary>
        public static int TotalHits
        {
            get { lock (_lock) return _hits.Count; }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static void PruneLocked(long cutoff)
        {
            // Hits are appended in monotonic-time order — find the first kept
            // index and remove the prefix in one shot instead of N RemoveAll
            // calls. O(log N) binary search to find the boundary.
            int lo = 0, hi = _hits.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_hits[mid].TickMs < cutoff) lo = mid + 1;
                else                            hi = mid;
            }
            if (lo > 0) _hits.RemoveRange(0, lo);
        }
    }
}
