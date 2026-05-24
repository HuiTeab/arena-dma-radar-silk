using System.Collections.Generic;

namespace eft_dma_radar.Arena.Unity.PhysX
{
    /// <summary>
    /// Decides which cached actors are "see-through" — i.e. ignored by the
    /// visibility raycaster. Three rule sources combine via OR:
    /// <list type="bullet">
    ///   <item><b>Layer mask</b> (<see cref="Raycaster.SeeThroughLayerMask"/>):
    ///     historical Phase 1 V0 rule. Any actor whose one-hot
    ///     <c>ShapeLayerMask</c> bit intersects the mask is see-through.
    ///     Defaults to layer 16 (player colliders) so a player's own body
    ///     doesn't block sightlines to teammates standing behind them.</item>
    ///   <item><b>Global name substrings</b> (<see cref="GlobalNamePatterns"/>):
    ///     applied to every map. Substring match against
    ///     <see cref="CachedActor.Name"/> (ordinal case-insensitive).
    ///     Default: <c>"Glass"</c> — windows are transparent regardless of
    ///     which scene loaded them.</item>
    ///   <item><b>Map-scoped name substrings</b> (<see cref="GetMapPatterns"/>
    ///     / <see cref="SetMapPatterns"/>): same substring semantics as the
    ///     global list but only consulted when the snapshot's
    ///     <see cref="SceneSnapshot.MapId"/> matches the dictionary key.
    ///     Lets us add patterns that are correct on one arena scene but
    ///     would mis-filter on another (e.g. a Sandbag mesh that's
    ///     translucent on one scene but opaque on another).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why pre-compute, not check per-ray:</b> name-substring match on every
    /// ray × actor pair would cost ~25 k extra string scans per visibility
    /// frame. Instead we classify once at snapshot build / load time and
    /// store the verdict as <see cref="CachedActor.IsSeeThrough"/>; the
    /// raycaster's Gate 0 then becomes a single bool read per actor.
    /// </para>
    /// <para>
    /// <b>Tuning:</b> all rule sources are writable at runtime. After mutating
    /// any of them, call <see cref="Reclassify"/> to refresh
    /// <see cref="CachedActor.IsSeeThrough"/> on the current snapshot.
    /// </para>
    /// <para>
    /// <b>Not persisted:</b> <see cref="CachedActor.IsSeeThrough"/> is a
    /// derived value — both fresh builds and disk-loaded snapshots compute
    /// it from the current rules. Rule changes invalidate the runtime
    /// classification but not the on-disk snapshot file, so the
    /// 30 s rebuild cost is amortised across rule iteration.
    /// </para>
    /// </remarks>
    internal static class VisibilityClassifier
    {
        // Patterns that apply to every map. Each entry is a case-insensitive
        // substring match — pick patterns narrow enough that you won't match
        // legitimate blockers by accident.
        //
        //   "Glass" — windows / panes / ballistic glass. Visually transparent
        //   even when the material stops bullets, so the visibility raycast
        //   should pass through.
        //
        //   "Cube (" — Unity's auto-generated primitive name with a numeric
        //   suffix (e.g. "Cube (15)", "Cube (270)"). Live match logs on
        //   Arena_AutoService showed a network of ~361 such actors on layer
        //   29 — invisible game-logic cubes (spawn protection / detection
        //   volumes) that block sight without any visible geometry. The
        //   trailing "(" anchors the match to the auto-named instances and
        //   leaves any legitimate "Cube" prefix alone.
        public static string[] GlobalNamePatterns { get; set; } =
        [
            "Glass",
            "Cube (",
        ];

        // Per-map additional patterns. Keyed by SceneSnapshot.MapId
        // (case-insensitive). Empty by default; the arena scenes we know
        // about (Arena_Bowl, Arena_AutoService, Arena_Prison, Arena_Iceberg,
        // Arena_saw, Arena_RailwayStation, Arena_equator_TDM_02, Arena_Yard,
        // Arena_Bay5, Arena_AirPit) all start with no entries. They get
        // populated iteratively as the user observes which colliders are
        // wrongly blocking sightlines on each scene and adds the relevant
        // substring via SetMapPatterns.
        //
        // No locking — the readers (Classify, Reclassify) run on a single
        // worker / build thread; the writers (UI button presses, config
        // load) are infrequent and atomic at the dictionary-replace level.
        private static readonly Dictionary<string, string[]> _mapPatterns
            = new(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Replaces the per-map pattern list for <paramref name="mapId"/>.
        /// Passing <c>null</c> or an empty array clears the entry.
        /// </summary>
        public static void SetMapPatterns(string mapId, params string[] patterns)
        {
            if (string.IsNullOrEmpty(mapId)) return;
            if (patterns is null || patterns.Length == 0)
                _mapPatterns.Remove(mapId);
            else
                _mapPatterns[mapId] = (string[])patterns.Clone();
        }

        /// <summary>
        /// Returns the per-map pattern list for <paramref name="mapId"/>, or
        /// an empty array when no entry exists. The returned array is a
        /// snapshot — mutating it does not affect the stored entry; use
        /// <see cref="SetMapPatterns"/> for that.
        /// </summary>
        public static string[] GetMapPatterns(string mapId)
        {
            if (string.IsNullOrEmpty(mapId)) return [];
            return _mapPatterns.TryGetValue(mapId, out var v) ? (string[])v.Clone() : [];
        }

        /// <summary>
        /// Snapshot of every map's pattern list. Cache View / debug UI can
        /// enumerate this to render the current per-map filter state.
        /// </summary>
        public static IReadOnlyDictionary<string, string[]> AllMapPatterns => _mapPatterns;

        // ── Config persistence ────────────────────────────────────────────────

        /// <summary>
        /// Applies all classifier settings stored in <paramref name="cfg"/> to the
        /// live runtime state. Called once at startup after config load.
        /// </summary>
        public static void LoadFromConfig(ArenaConfig cfg)
        {
            if (cfg.VisCheckGlobalNamePatterns is { Length: > 0 })
                GlobalNamePatterns = (string[])cfg.VisCheckGlobalNamePatterns.Clone();

            Raycaster.SeeThroughLayerMask = cfg.VisCheckSeeThroughLayerMask;

            _mapPatterns.Clear();
            if (cfg.VisCheckMapNamePatterns is not null)
            {
                foreach (var kv in cfg.VisCheckMapNamePatterns)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Value?.Length > 0)
                        _mapPatterns[kv.Key] = (string[])kv.Value.Clone();
                }
            }
        }

        /// <summary>
        /// Writes the current runtime classifier settings back into
        /// <paramref name="cfg"/> so the caller can save them to disk.
        /// </summary>
        public static void SaveToConfig(ArenaConfig cfg)
        {
            cfg.VisCheckSeeThroughLayerMask = Raycaster.SeeThroughLayerMask;
            cfg.VisCheckGlobalNamePatterns  = (string[])GlobalNamePatterns.Clone();
            var dict = new Dictionary<string, string[]>();
            foreach (var kv in _mapPatterns)
                dict[kv.Key] = (string[])kv.Value.Clone();
            cfg.VisCheckMapNamePatterns = dict;
        }

        /// <summary>
        /// Classifies a single actor against the current rules in the context
        /// of a specific map. Used during fresh-build ingest
        /// (<see cref="SceneCache"/>) and disk-load
        /// (<see cref="SnapshotSerializer"/>).
        /// </summary>
        public static bool Classify(string mapId, uint shapeLayerMask, string? name)
        {
            // Layer rule (Phase 1 V0) — cheapest, fires before any string work.
            uint layerMask = Raycaster.SeeThroughLayerMask;
            if (layerMask != 0 && (shapeLayerMask & layerMask) != 0)
                return true;

            if (string.IsNullOrEmpty(name))
                return false;

            // Phase 1 V1 — global name patterns (apply to every map).
            if (MatchesAny(name, GlobalNamePatterns))
                return true;

            // Phase 1 V2 — map-scoped name patterns (this scene only).
            if (!string.IsNullOrEmpty(mapId)
                && _mapPatterns.TryGetValue(mapId, out var perMap)
                && MatchesAny(name, perMap))
                return true;

            return false;
        }

        /// <summary>
        /// Returns the first rule that matches the given actor, formatted as a
        /// short human-readable string ("layer 0x10000", "name pattern 'Glass'",
        /// "map pattern 'BALLISTIC_Fabric'"), or "no rule matched" when the
        /// actor isn't classified as see-through. Used by the Cache View
        /// tooltip and the SceneCache build-time sample log so the user can
        /// see why a specific actor got filtered.
        /// </summary>
        public static string Explain(string mapId, uint shapeLayerMask, string? name)
        {
            uint layerMask = Raycaster.SeeThroughLayerMask;
            if (layerMask != 0 && (shapeLayerMask & layerMask) != 0)
                return $"layer mask 0x{layerMask:X}";

            if (!string.IsNullOrEmpty(name))
            {
                string? hit = FirstMatch(name, GlobalNamePatterns);
                if (hit is not null) return $"global pattern \"{hit}\"";

                if (!string.IsNullOrEmpty(mapId)
                    && _mapPatterns.TryGetValue(mapId, out var perMap))
                {
                    hit = FirstMatch(name, perMap);
                    if (hit is not null) return $"map(\"{mapId}\") pattern \"{hit}\"";
                }
            }
            return "no rule matched";
        }

        /// <summary>
        /// Walks every actor in <paramref name="snapshot"/> and refreshes
        /// <see cref="CachedActor.IsSeeThrough"/> in place — for when the user
        /// edits the rule lists at runtime and wants the new rules to take
        /// effect without rebuilding the cache.
        /// </summary>
        public static void Reclassify(SceneSnapshot snapshot)
        {
            if (snapshot is null || snapshot.IsEmpty) return;

            // Track flips so the diagnostic hook can report "your new pattern
            // moved N actors to see-through, M back to blocker" — much more
            // useful than just "rules changed".
            int flipsToSee = 0, flipsToBlock = 0;
            foreach (var a in snapshot.Actors)
            {
                bool prev = a.IsSeeThrough;
                bool now  = Classify(snapshot.MapId, a.ShapeLayerMask, a.Name);
                if (prev != now)
                {
                    if (now) flipsToSee++;
                    else     flipsToBlock++;
                }
                a.IsSeeThrough = now;
            }

            VisCheckDiagnostics.OnClassifierChanged(
                trigger:           "Reclassify",
                newLayerMask:      Raycaster.SeeThroughLayerMask,
                newGlobalPatterns: GlobalNamePatterns,
                flipsToSeeThrough: flipsToSee,
                flipsToBlocker:    flipsToBlock);
        }

        /// <summary>
        /// Substring scan helper — case-insensitive ordinal Contains over a
        /// flat array. Empty / null entries in <paramref name="patterns"/>
        /// are skipped so callers don't have to pre-clean the list.
        /// </summary>
        private static bool MatchesAny(string name, string[] patterns)
        {
            if (patterns is null) return false;
            for (int i = 0; i < patterns.Length; i++)
            {
                if (!string.IsNullOrEmpty(patterns[i])
                    && name.Contains(patterns[i], System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Like <see cref="MatchesAny"/> but returns the first matching pattern
        /// itself (useful for diagnostic / explanation output). Null when no
        /// pattern matches.
        /// </summary>
        private static string? FirstMatch(string name, string[] patterns)
        {
            if (patterns is null) return null;
            for (int i = 0; i < patterns.Length; i++)
            {
                if (!string.IsNullOrEmpty(patterns[i])
                    && name.Contains(patterns[i], System.StringComparison.OrdinalIgnoreCase))
                    return patterns[i];
            }
            return null;
        }
    }
}
