using ImGuiNET;
using System.Globalization;
using eft_dma_radar.Arena;

namespace eft_dma_radar.Arena.Unity.PhysX
{
    /// <summary>
    /// Shared ImGui widget that renders the <see cref="VisibilityClassifier"/> rule editor.
    /// Both <see cref="VisCheckDebugWindow"/> and <see cref="CacheViewWindow"/> call this so
    /// the controls are always identical and in sync.
    /// <para>
    /// Any change is applied to the live runtime immediately <em>and</em> persisted to
    /// <see cref="ArenaConfig"/> (disk) so settings survive restart.
    /// </para>
    /// </summary>
    internal static class ClassifierRulesWidget
    {
        // ── Layer mask editor ────────────────────────────────────────────────

        private static string _maskHex      = $"{Raycaster.SeeThroughLayerMask:X8}";
        private static bool   _maskHexFocus; // true while the InputText has keyboard focus

        // ── Pattern add inputs ───────────────────────────────────────────────

        private static string _newGlobalPat = "";
        private static string _newMapPat    = "";

        // ── Status feedback (shown for 4 s after any change) ─────────────────

        private static string _statusMsg = "";
        private static long   _statusMsgMs;

        // ── Bit-grid colours (AABBGGRR little-endian) ────────────────────────
        // See-through bit = orange/red  |  Blocker bit = dark gray

        private const uint ColSeeThru      = 0xFF2060E0u;  // R=E0 G=60 B=20 — orange
        private const uint ColSeeThruHover = 0xFF4080FFu;
        private const uint ColBlocker      = 0xFF333333u;
        private const uint ColBlockerHover = 0xFF555555u;

        // ── Entry point ──────────────────────────────────────────────────────

        /// <summary>
        /// Draws the full rule editor at the current cursor position.
        /// Safe to call from any ImGui window or child.
        /// </summary>
        public static void Draw()
        {
            DrawLayerMaskSection();
            ImGui.Spacing();
            DrawGlobalPatternsSection();
            ImGui.Spacing();
            DrawMapPatternsSection();
            ImGui.Spacing();
            DrawReclassifyRow();
        }

        // ── Layer mask ───────────────────────────────────────────────────────

        private static void DrawLayerMaskSection()
        {
            uint curMask = Raycaster.SeeThroughLayerMask;

            ImGui.TextDisabled("See-through layer mask:");

            // Keep the hex string in sync with the live property — but only when the
            // text field is NOT focused, so we don't fight the user's typing.
            if (!_maskHexFocus)
                _maskHex = $"{curMask:X8}";

            ImGui.SetNextItemWidth(90f);
            bool hexChanged = ImGui.InputText("##stm", ref _maskHex, 12,
                ImGuiInputTextFlags.CharsHexadecimal);
            _maskHexFocus = ImGui.IsItemActive();
            if (hexChanged && uint.TryParse(_maskHex, NumberStyles.HexNumber, null, out uint parsed))
            {
                Raycaster.SeeThroughLayerMask = parsed;
                curMask = parsed;
                Persist(); // save mask change immediately; no reclassify needed for mask-only edits
                           // (mask is read per-ray, not baked into IsSeeThrough for mask-based actors)
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Hex bitmask — each set bit marks that Unity layer as see-through.\nSame encoding as ShapeLayerMask: bit N = layer N.");

            // Actor hit count for the current mask
            var snap = SceneCache.Snapshot;
            if (snap.Actors.Length > 0)
            {
                int hits = 0;
                for (int i = 0; i < snap.Actors.Length; i++)
                    if ((snap.Actors[i].ShapeLayerMask & curMask) != 0) hits++;
                ImGui.SameLine();
                ImGui.TextDisabled($"({hits}/{snap.Actors.Length} actors)");
            }

            // 4 × 8 bit grid — click to toggle a layer bit.
            // Orange = see-through, dark = blocks rays.
            ImGui.TextDisabled("Layers 0–31 (orange = see-through, dark = blocks):");
            bool gridChanged = false;
            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 8; col++)
                {
                    int bit = row * 8 + col;
                    bool isSet = (curMask & (1u << bit)) != 0;
                    if (col > 0) ImGui.SameLine(0, 2f);
                    ImGui.PushID($"stl_{bit}");
                    ImGui.PushStyleColor(ImGuiCol.Button,        isSet ? ColSeeThru      : ColBlocker);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, isSet ? ColSeeThruHover : ColBlockerHover);
                    if (ImGui.Button($"{bit}", new Vector2(26f, 18f)))
                    {
                        curMask ^= 1u << bit;
                        Raycaster.SeeThroughLayerMask = curMask;
                        _maskHex = $"{curMask:X8}";
                        gridChanged = true;
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(
                            $"Layer {bit} (mask 0x{1u << bit:X})\n" +
                            (isSet ? "Currently: see-through (rays pass through)"
                                   : "Currently: blocks rays"));
                    ImGui.PopStyleColor(2);
                    ImGui.PopID();
                }
            }
            if (gridChanged) Persist();
        }

        // ── Global name patterns ─────────────────────────────────────────────

        private static void DrawGlobalPatternsSection()
        {
            ImGui.TextDisabled("Global patterns — applied to every map:");
            var pats = VisibilityClassifier.GlobalNamePatterns;

            for (int i = 0; i < pats.Length; i++)
            {
                ImGui.PushID($"gp_{i}");
                ImGui.TextUnformatted($"  \"{pats[i]}\"");
                ImGui.SameLine();
                if (ImGui.SmallButton("×"))
                {
                    var next = new string[pats.Length - 1];
                    int ni = 0;
                    for (int k = 0; k < pats.Length; k++)
                        if (k != i) next[ni++] = pats[k];
                    VisibilityClassifier.GlobalNamePatterns = next;
                    PersistAndReclassify();
                    ImGui.PopID();
                    return; // pats array changed; bail and re-render next frame
                }
                ImGui.PopID();
            }
            if (pats.Length == 0) ImGui.TextDisabled("  (none)");

            float avail = ImGui.GetContentRegionAvail().X;
            float addW  = ImGui.CalcTextSize("Add##gp").X + ImGui.GetStyle().FramePadding.X * 2 + 6f;
            ImGui.SetNextItemWidth(avail - addW - ImGui.GetStyle().ItemSpacing.X);
            ImGui.InputTextWithHint("##ngp", "new pattern…", ref _newGlobalPat, 128);
            ImGui.SameLine();
            if (ImGui.SmallButton("Add##gp") && !string.IsNullOrWhiteSpace(_newGlobalPat))
            {
                var next = new string[pats.Length + 1];
                pats.CopyTo(next, 0);
                next[pats.Length] = _newGlobalPat.Trim();
                VisibilityClassifier.GlobalNamePatterns = next;
                _newGlobalPat = "";
                PersistAndReclassify();
            }
        }

        // ── Per-map name patterns ─────────────────────────────────────────────

        private static void DrawMapPatternsSection()
        {
            var snap  = SceneCache.Snapshot;
            string mapId = !string.IsNullOrEmpty(snap.MapId)
                ? snap.MapId
                : Memory.CurrentGameWorld?.MapID ?? "";

            if (string.IsNullOrEmpty(mapId))
            {
                ImGui.TextDisabled("Map patterns: (no active map — join a match first)");
                return;
            }

            ImGui.TextDisabled($"Map patterns — {mapId}:");
            var pats = VisibilityClassifier.GetMapPatterns(mapId);

            for (int i = 0; i < pats.Length; i++)
            {
                ImGui.PushID($"mp_{i}");
                ImGui.TextUnformatted($"  \"{pats[i]}\"");
                ImGui.SameLine();
                if (ImGui.SmallButton("×"))
                {
                    var next = new string[pats.Length - 1];
                    int ni = 0;
                    for (int k = 0; k < pats.Length; k++)
                        if (k != i) next[ni++] = pats[k];
                    VisibilityClassifier.SetMapPatterns(mapId, next);
                    PersistAndReclassify();
                    ImGui.PopID();
                    return;
                }
                ImGui.PopID();
            }
            if (pats.Length == 0) ImGui.TextDisabled("  (none)");

            float avail = ImGui.GetContentRegionAvail().X;
            float addW  = ImGui.CalcTextSize("Add##mp").X + ImGui.GetStyle().FramePadding.X * 2 + 6f;
            ImGui.SetNextItemWidth(avail - addW - ImGui.GetStyle().ItemSpacing.X);
            ImGui.InputTextWithHint("##nmp", "new pattern…", ref _newMapPat, 128);
            ImGui.SameLine();
            if (ImGui.SmallButton("Add##mp") && !string.IsNullOrWhiteSpace(_newMapPat))
            {
                var existing = VisibilityClassifier.GetMapPatterns(mapId);
                var next = new string[existing.Length + 1];
                existing.CopyTo(next, 0);
                next[existing.Length] = _newMapPat.Trim();
                VisibilityClassifier.SetMapPatterns(mapId, next);
                _newMapPat = "";
                PersistAndReclassify();
            }
        }

        // ── Reclassify button + feedback ─────────────────────────────────────

        private static void DrawReclassifyRow()
        {
            if (ImGui.Button("Reclassify now"))
                PersistAndReclassify();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(
                    "Refresh IsSeeThrough on every cached actor using the current rules.\n" +
                    "Happens automatically when you add/remove patterns.");

            if (!string.IsNullOrEmpty(_statusMsg)
                && Environment.TickCount64 - _statusMsgMs < 4000)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.40f, 0.85f, 0.40f, 1f), _statusMsg);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static void Persist()
        {
            VisibilityClassifier.SaveToConfig(ArenaProgram.Config);
            ArenaProgram.Config.Save();
        }

        private static void PersistAndReclassify()
        {
            VisibilityClassifier.Reclassify(SceneCache.Snapshot);
            Persist();
            int n = SceneCache.Snapshot.Actors.Length;
            _statusMsg   = $"Reclassified {n} actors — saved";
            _statusMsgMs = Environment.TickCount64;
        }
    }
}
