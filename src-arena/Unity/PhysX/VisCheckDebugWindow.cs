using ImGuiNET;
using eft_dma_radar.Arena;

namespace eft_dma_radar.Arena.Unity.PhysX
{
    /// <summary>
    /// Comprehensive visibility-check debug overlay.
    /// <para>
    /// Left column: live monitoring — cache state, snapshot stats, worker stats,
    /// per-player check results with filtering and optional bone-mask / blocker columns.
    /// </para>
    /// <para>
    /// Right column: tuning controls — per-bone ray toggles, max distance,
    /// the full <see cref="ClassifierRulesWidget"/> (layer mask + name patterns),
    /// and action buttons.
    /// </para>
    /// <para>Toggled with <b>F11</b>.</para>
    /// </summary>
    internal static class VisCheckDebugWindow
    {
        public static bool IsVisible { get; set; }
        public static void Toggle() => IsVisible = !IsVisible;

        // ── Per-player table options ─────────────────────────────────────────

        private static int  _playerFilter = 0;   // 0=All  1=Visible  2=Blocked
        private static bool _colBones     = true;
        private static bool _colBlocker   = true;

        // ── Frame entry point ────────────────────────────────────────────────

        public static void Draw()
        {
            if (!IsVisible) return;

            var io = ImGui.GetIO();
            ImGui.SetNextWindowSizeConstraints(new Vector2(640f, 440f), io.DisplaySize);
            ImGui.SetNextWindowSize(new Vector2(900f, 640f), ImGuiCond.FirstUseEver);

            bool open = IsVisible;
            if (!ImGui.Begin("VisCheck Debug", ref open,
                ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse))
            {
                IsVisible = open;
                ImGui.End();
                return;
            }
            IsVisible = open;

            try
            {
                // Two-column layout: monitor (flex) | settings (330 px fixed).
                const float SettingsWidth = 330f;
                float regionW   = ImGui.GetContentRegionAvail().X;
                float monitorW  = MathF.Max(200f, regionW - SettingsWidth - 8f);

                if (ImGui.BeginChild("##vcd_mon", new Vector2(monitorW, 0), ImGuiChildFlags.Borders))
                    DrawMonitorColumn();
                ImGui.EndChild();

                ImGui.SameLine();

                if (ImGui.BeginChild("##vcd_set", new Vector2(0, 0), ImGuiChildFlags.Borders))
                    DrawSettingsColumn();
                ImGui.EndChild();
            }
            finally
            {
                ImGui.End();
            }
        }

        // ── Monitor column ────────────────────────────────────────────────────

        private static void DrawMonitorColumn()
        {
            DrawCacheBlock();
            ImGui.Separator();
            DrawSnapshotBlock();
            ImGui.Separator();
            DrawWorkerStatsBlock();
            ImGui.Separator();
            DrawPerPlayerBlock();
        }

        private static void DrawCacheBlock()
        {
            var (lbl, col) = SceneCache.State switch
            {
                SceneCacheState.Ready    => ("READY",    new Vector4(0.30f, 0.85f, 0.30f, 1f)),
                SceneCacheState.Building => ("BUILDING", new Vector4(0.95f, 0.85f, 0.20f, 1f)),
                SceneCacheState.Failed   => ("FAILED",   new Vector4(0.95f, 0.30f, 0.25f, 1f)),
                _                        => ("IDLE",     new Vector4(0.65f, 0.65f, 0.65f, 1f)),
            };
            ImGui.Text("Cache:"); ImGui.SameLine();
            ImGui.TextColored(col, lbl);
            ImGui.SameLine(0, 12);
            ImGui.TextDisabled(
                $"NpPhysics=0x{SceneCache.LastNpPhysics:X}  SDK RVA=0x{SceneCache.LastSdkRva:X8}");

            if (SceneCache.LastBuildStartedUtc != default)
            {
                double age = (DateTime.UtcNow - SceneCache.LastBuildStartedUtc).TotalSeconds;
                ImGui.Text(
                    $"Build: {SceneCache.LastBuildDuration.TotalMilliseconds:F0}ms  " +
                    $"{age:F0}s ago  " +
                    $"ok={SceneCache.BuildSuccessCount}  fail={SceneCache.BuildFailureCount}");
            }
            else
            {
                ImGui.TextDisabled("Build: (never)");
            }

            int totalSkip = SceneCache.LastSkippedNonRigid
                          + SceneCache.LastSkippedReadError
                          + SceneCache.LastSkippedBadGeometry;
            if (totalSkip > 0)
            {
                var sc = SceneCache.Snapshot.Actors.Length == 0
                    ? new Vector4(0.95f, 0.40f, 0.40f, 1f)
                    : new Vector4(0.75f, 0.70f, 0.40f, 1f);
                ImGui.TextColored(sc,
                    $"Skipped: non-rigid={SceneCache.LastSkippedNonRigid}  " +
                    $"read-err={SceneCache.LastSkippedReadError}  " +
                    $"bad-geom={SceneCache.LastSkippedBadGeometry}");
            }

            if (!string.IsNullOrEmpty(SceneCache.LastError))
                ImGui.TextColored(new Vector4(0.95f, 0.40f, 0.40f, 1f),
                    $"Error: {SceneCache.LastError}");
        }

        private static void DrawSnapshotBlock()
        {
            var snap = SceneCache.Snapshot;
            ImGui.Text(
                $"Snapshot '{snap.MapId}':  actors={snap.Actors.Length}  " +
                $"meshes={snap.Meshes.Length}  convex={snap.ConvexMeshes.Length}  " +
                $"hf={snap.HeightFields.Length}  built {AgeText(snap.BuildTickMs)}");

            if (snap.Actors.Length > 0)
            {
                int nS=0,nPl=0,nCap=0,nBox=0,nC=0,nTri=0,nHf=0,nOth=0,nST=0;
                for (int i = 0; i < snap.Actors.Length; i++)
                {
                    switch (snap.Actors[i].GeometryType)
                    {
                        case PxGeometryType.Sphere:       nS++;   break;
                        case PxGeometryType.Plane:        nPl++;  break;
                        case PxGeometryType.Capsule:      nCap++; break;
                        case PxGeometryType.Box:          nBox++; break;
                        case PxGeometryType.ConvexMesh:   nC++;   break;
                        case PxGeometryType.TriangleMesh: nTri++; break;
                        case PxGeometryType.HeightField:  nHf++;  break;
                        default:                          nOth++; break;
                    }
                    if (snap.Actors[i].IsSeeThrough) nST++;
                }
                ImGui.TextDisabled(
                    $"  sph={nS} plane={nPl} cap={nCap} box={nBox} " +
                    $"convex={nC} tri={nTri} hf={nHf} other={nOth}  " +
                    $"see-thru={nST}/{snap.Actors.Length}");
            }
        }

        private static void DrawWorkerStatsBlock()
        {
            var (wlbl, wcol) = VisibilityWorker.Enabled
                ? ("RUNNING", new Vector4(0.30f, 0.85f, 0.30f, 1f))
                : ("STOPPED", new Vector4(0.65f, 0.65f, 0.65f, 1f));
            ImGui.Text("Worker:"); ImGui.SameLine();
            ImGui.TextColored(wcol, wlbl);

            var stats = VisibilityWorker.LastTickStats;
            long tickAge = stats.TickMs == 0 ? -1 : Environment.TickCount64 - stats.TickMs;
            string ageStr = tickAge < 0 ? "" : $"  (tick {tickAge}ms ago)";
            string pct    = stats.Checks > 0
                ? $" ({100.0 * stats.Blocked / stats.Checks:F0}%)"
                : "";
            ImGui.Text(
                $"checks={stats.Checks}  blocked={stats.Blocked}{pct}  " +
                $"avg={stats.AvgUs:F1}μs  max={stats.MaxUs:F1}μs{ageStr}");
            ImGui.Text(
                $"eye: ({stats.EyePos.X:F1}, {stats.EyePos.Y:F1}, {stats.EyePos.Z:F1})  " +
                $"max-dist={VisibilityWorker.MaxRayDistance:F0}m");
        }

        private static void DrawPerPlayerBlock()
        {
            // ── Filter + column toggles ───────────────────────────────────────
            ImGui.Text("Players:");
            ImGui.SameLine();
            ImGui.RadioButton("All",     ref _playerFilter, 0); ImGui.SameLine();
            ImGui.RadioButton("Visible", ref _playerFilter, 1); ImGui.SameLine();
            ImGui.RadioButton("Blocked", ref _playerFilter, 2);
            ImGui.SameLine(0, 14f);
            ImGui.TextDisabled("cols:");
            ImGui.SameLine();
            ImGui.Checkbox("Bones##col",   ref _colBones);
            ImGui.SameLine();
            ImGui.Checkbox("Blocker##col", ref _colBlocker);

            var rows = VisibilityWorker.LastPerPlayer;
            if (rows.Count == 0)
            {
                ImGui.TextDisabled("  (no checks yet — start the worker and build the cache)");
                return;
            }

            // Dynamic column count based on active toggles.
            int nCols = 4; // Name | Dist | Status | μs — always visible
            if (_colBones)   nCols++;
            if (_colBlocker) nCols++;

            var  snap   = SceneCache.Snapshot;
            float tableH = ImGui.GetContentRegionAvail().Y - 4f;

            if (ImGui.BeginTable("##vcdpl", nCols,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders |
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY,
                new Vector2(0, tableH)))
            {
                ImGui.TableSetupColumn("Name",    ImGuiTableColumnFlags.WidthStretch, 1.0f);
                ImGui.TableSetupColumn("Dist",    ImGuiTableColumnFlags.WidthFixed,   52f);
                ImGui.TableSetupColumn("Status",  ImGuiTableColumnFlags.WidthFixed,   64f);
                if (_colBones)
                    ImGui.TableSetupColumn("Bones",   ImGuiTableColumnFlags.WidthFixed, 50f);
                if (_colBlocker)
                    ImGui.TableSetupColumn("Blocker", ImGuiTableColumnFlags.WidthStretch, 1.0f);
                ImGui.TableSetupColumn("μs",      ImGuiTableColumnFlags.WidthFixed,   64f);
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();

                var visCol = new Vector4(0.30f, 0.85f, 0.30f, 1f);
                var blkCol = new Vector4(0.85f, 0.45f, 0.25f, 1f);

                foreach (var r in rows)
                {
                    if (_playerFilter == 1 && !r.Visible) continue;
                    if (_playerFilter == 2 &&  r.Visible) continue;

                    ImGui.TableNextRow();
                    int col = 0;

                    // Name
                    ImGui.TableSetColumnIndex(col++);
                    ImGui.TextUnformatted(string.IsNullOrEmpty(r.Name) ? "(unnamed)" : r.Name);

                    // Distance
                    ImGui.TableSetColumnIndex(col++);
                    ImGui.Text($"{r.Distance:F0}m");

                    // Status
                    ImGui.TableSetColumnIndex(col++);
                    ImGui.TextColored(r.Visible ? visCol : blkCol,
                        r.Visible ? "visible" : "blocked");

                    // Bone mask: uppercase = bone visible, lowercase = blocked / disabled
                    if (_colBones)
                    {
                        ImGui.TableSetColumnIndex(col++);
                        bool hv = (r.BoneMask & (1u << 0)) != 0;
                        bool cv = (r.BoneMask & (1u << 1)) != 0;
                        bool pv = (r.BoneMask & (1u << 2)) != 0;
                        ImGui.TextUnformatted(
                            $"{(hv ? 'H' : 'h')}{(cv ? 'C' : 'c')}{(pv ? 'P' : 'p')}");
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip(
                                "H=head  C=chest  P=pelvis\n" +
                                "Uppercase = bone is visible from your eye.\n" +
                                "Lowercase = bone is blocked or bone check is disabled.");
                    }

                    // Blocker actor name
                    if (_colBlocker)
                    {
                        ImGui.TableSetColumnIndex(col++);
                        if (!r.Visible
                            && r.BlockerActorIdx >= 0
                            && r.BlockerActorIdx < snap.Actors.Length)
                        {
                            var bl = snap.Actors[r.BlockerActorIdx];
                            string nm = string.IsNullOrEmpty(bl.Name)
                                ? $"({bl.GeometryType})"
                                : bl.Name;
                            if (nm.Length > 38) nm = nm.Substring(0, 37) + "…";
                            ImGui.TextUnformatted(nm);
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip(
                                    $"Layer {bl.UnityLayer} (mask 0x{bl.ShapeLayerMask:X})\n" +
                                    $"Type: {bl.GeometryType}\n" +
                                    $"Actor: 0x{bl.ActorBase:X}");
                        }
                        else
                        {
                            ImGui.TextDisabled("—");
                        }
                    }

                    // Ray cast time
                    ImGui.TableSetColumnIndex(col);
                    ImGui.Text($"{r.TimeUs:F1}");
                }
                ImGui.EndTable();
            }
        }

        // ── Settings column ───────────────────────────────────────────────────

        private static void DrawSettingsColumn()
        {
            DrawWorkerSettingsSection();
            ImGui.Separator();

            if (ImGui.BeginChild("##vcd_classifier_scroll",
                    new Vector2(0, ImGui.GetContentRegionAvail().Y - 80f), ImGuiChildFlags.None))
            {
                ClassifierRulesWidget.Draw();
            }
            ImGui.EndChild();

            ImGui.Separator();
            DrawActionsSection();
        }

        private static void DrawWorkerSettingsSection()
        {
            ImGui.TextDisabled("Worker Settings");

            bool wEnabled = VisibilityWorker.Enabled;
            if (ImGui.Checkbox("Enabled##wk", ref wEnabled))
            {
                if (wEnabled) VisibilityWorker.Start();
                else          VisibilityWorker.Stop();
            }

            float maxDist = VisibilityWorker.MaxRayDistance;
            if (ImGui.SliderFloat("Max dist##wk", ref maxDist, 10f, 500f, "%.0f m"))
            {
                VisibilityWorker.MaxRayDistance = maxDist;
                VisibilityWorker.SaveToConfig(ArenaProgram.Config);
                ArenaProgram.Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Players beyond this distance default to visible (no ray cast).");

            ImGui.Text("Check bones:");
            ImGui.SameLine();

            bool bH = VisibilityWorker.CheckHead;
            if (ImGui.Checkbox("H##b", ref bH)) { VisibilityWorker.CheckHead = bH; SaveWorkerCfg(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Cast a ray toward the head bone.");
            ImGui.SameLine();

            bool bC = VisibilityWorker.CheckChest;
            if (ImGui.Checkbox("C##b", ref bC)) { VisibilityWorker.CheckChest = bC; SaveWorkerCfg(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Cast a ray toward the chest (spine3) bone.");
            ImGui.SameLine();

            bool bP = VisibilityWorker.CheckPelvis;
            if (ImGui.Checkbox("P##b", ref bP)) { VisibilityWorker.CheckPelvis = bP; SaveWorkerCfg(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Cast a ray toward the pelvis bone.");
        }

        private static void DrawActionsSection()
        {
            ImGui.TextDisabled("Actions");
            string mapId = Memory.CurrentGameWorld?.MapID ?? string.Empty;
            bool hasmatch = !string.IsNullOrEmpty(mapId);
            bool busy     = SceneCache.State == SceneCacheState.Building;

            ImGui.BeginDisabled(!hasmatch || busy);
            if (ImGui.Button("Rebuild"))
                SceneCache.TriggerBuild(mapId);
            ImGui.SameLine();
            if (ImGui.Button("Invalidate + Rebuild"))
            {
                SnapshotSerializer.TryDelete(mapId);
                SceneCache.TriggerBuild(mapId);
            }
            ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button(VisibilityWorker.Enabled ? "Stop Worker" : "Start Worker"))
            {
                if (VisibilityWorker.Enabled) VisibilityWorker.Stop();
                else                          VisibilityWorker.Start();
            }

            if (ImGui.Button("Reset Cache")) SceneCache.Reset();

            if (!hasmatch) ImGui.TextDisabled("(no active match)");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void SaveWorkerCfg()
        {
            VisibilityWorker.SaveToConfig(ArenaProgram.Config);
            ArenaProgram.Config.Save();
        }

        private static string AgeText(long tickMs)
        {
            if (tickMs == 0) return "(never)";
            var ageSec = (Environment.TickCount64 - tickMs) / 1000.0;
            return ageSec < 60 ? $"{ageSec:F0}s ago" : $"{ageSec / 60.0:F1}min ago";
        }
    }
}
