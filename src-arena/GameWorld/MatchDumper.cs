using System.IO;
using eft_dma_radar.Arena.Unity.IL2CPP;
using SDK;

namespace eft_dma_radar.Arena.GameWorld
{
    /// <summary>
    /// Serializes the full live Arena radar snapshot to a timestamped JSON file under
    /// <c>&lt;exe&gt;\dumps\&lt;mapId&gt;_&lt;timestamp&gt;.json</c> and an accompanying
    /// IL2CPP class-hierarchy dump to <c>..._il2cpp.txt</c>.
    ///
    /// <para><b>Toggle:</b> <see cref="ArenaConfig.EnableMatchDump"/> at runtime
    /// (defaults to true); the <c>EnabledByDefault</c> constant in this file can
    /// be flipped to bypass the config for development builds. Nothing runs when
    /// disabled — zero overhead.</para>
    ///
    /// <para>Arena is much sparser than EFT main, so the snapshot only covers:
    /// <list type="bullet">
    ///   <item><b>match</b> — map id/name, GameWorld base, dump timestamp.</item>
    ///   <item><b>localPlayer</b> — base, position, rotation, team id.</item>
    ///   <item><b>players</b> — every tracked observed player plus their cached
    ///     identity, side/type, position, rotation, profile id, team id.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal static class MatchDumper
    {
        // ── Toggle ───────────────────────────────────────────────────────────────
        // Flip this to bypass the runtime config flag for development builds.
        private const bool EnabledByDefault = false;

        // Flip this to also write an IL2CPP class hierarchy dump (offsets, parents,
        // values) alongside the JSON snapshot. Runs on its own background thread so
        // it never delays the JSON write. Can take 10–30s during a live match due
        // to DMA read volume.
        private const bool EnableIl2CppDump = true;

        // ── Paths ────────────────────────────────────────────────────────────────
        private static readonly string DumpsDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dumps");

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        // ── Guard ─────────────────────────────────────────────────────────────────
        private static DateTime _lastDumpTime = DateTime.MinValue;
        private static readonly TimeSpan MinDumpInterval = TimeSpan.FromSeconds(5);

        // ── Active IL2CPP dump task ───────────────────────────────────────────────
        // Kept so Memory.Close() can drain it before VMM is disposed, preventing
        // a mid-write truncation when the user closes the app or exits the match.
        private static volatile Task? _activeIl2CppTask;

        // ── Entry points ─────────────────────────────────────────────────────────

        /// <summary>
        /// Dumps the full match state to disk if the feature is enabled.
        /// Safe to call from any thread — JSON serialization and IO run on a
        /// background <see cref="ThreadPool"/> thread to avoid blocking callers.
        /// </summary>
        public static void DumpAsync(LocalGameWorld game)
        {
            try
            {
                if (game is null) return;
                if (!ArenaProgram.Config.EnableMatchDump && !EnabledByDefault)
                    return;

                var now = DateTime.UtcNow;
                if (now - _lastDumpTime < MinDumpInterval)
                {
                    Log.WriteLine("[MatchDumper] Skipped — too soon since last dump.");
                    return;
                }
                _lastDumpTime = now;

                // Move EVERYTHING to a background worker — including the snapshot build.
                // BuildSnapshot performs DMA reads that can throw; doing it on the calling
                // (UI) thread would turn a single bad read into a crash.
                var gameRef = game;
                string safeMap = string.Concat(game.MapID
                    .Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_'));
                string ts = now.ToString("yyyyMMdd_HHmmss");

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        var snapshot = BuildSnapshot(gameRef, now);
                        WriteSnapshot(snapshot);
                    }
                    catch (Exception ex)
                    {
                        Log.WriteLine($"[MatchDumper] Background dump failed: {ex}");
                    }
                });

                if (EnableIl2CppDump)
                {
                    _activeIl2CppTask = Task.Run(() =>
                    {
                        try { WriteIl2CppDump(gameRef, safeMap, ts); }
                        catch (Exception ex) { Log.WriteLine($"[MatchDumper] IL2CPP dump failed: {ex.Message}"); }
                    });
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[MatchDumper] DumpAsync failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Waits for any in-progress IL2CPP dump to finish, up to
        /// <paramref name="timeout"/>. Call this from <c>Memory.Close()</c>
        /// before VMM is disposed so the file is never truncated mid-write.
        /// </summary>
        public static void Drain(TimeSpan timeout)
        {
            var task = _activeIl2CppTask;
            if (task is null || task.IsCompleted) return;
            Log.WriteLine("[MatchDumper] Waiting for IL2CPP dump to finish before shutdown...");
            if (!task.Wait(timeout))
                Log.WriteLine("[MatchDumper] IL2CPP dump did not finish in time — file may be incomplete.");
        }

        // ── Snapshot builder ─────────────────────────────────────────────────────

        private static MatchSnapshot BuildSnapshot(LocalGameWorld game, DateTime timestamp)
        {
            var players = new List<DumpPlayer>();
            foreach (var p in game.Players)
            {
                try
                {
                    // Only use already-cached Player properties — zero live DMA reads here.
                    // Any live read in a background thread contends with the main DMA loop
                    // and increases the odds of stalling the realtime worker.
                    players.Add(new DumpPlayer
                    {
                        Name          = p.Name,
                        Type          = p.Type.ToString(),
                        IsLocalPlayer = p.IsLocalPlayer,
                        IsAI          = p.IsAI,
                        IsActive      = p.IsActive,
                        IsAlive       = p.IsAlive,
                        Position      = ToDumpVec(p.Position),
                        HasValidPosition = p.HasValidPosition,
                        RotationYaw   = p.RotationYaw,
                        RotationPitch = p.RotationPitch,
                        ProfileId     = p.ProfileId,
                        AccountId     = p.AccountId,
                        TeamID        = p.TeamID,
                        TeamColor     = p.TeamID >= 0 ? ((ArmbandColorType)p.TeamID).ToString() : null,
                        Base          = $"0x{p.Base:X}",
                    });
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[MatchDumper] Player snapshot failed ({p?.Name}): {ex.Message}");
                }
            }

            DumpLocalPlayer? localPlayer = null;
            try
            {
                if (game.LocalPlayer is Player lp)
                {
                    localPlayer = new DumpLocalPlayer
                    {
                        Name          = lp.Name,
                        Base          = $"0x{lp.Base:X}",
                        ProfileId     = lp.ProfileId,
                        AccountId     = lp.AccountId,
                        IsAlive       = lp.IsAlive,
                        IsActive      = lp.IsActive,
                        Position      = ToDumpVec(lp.Position),
                        HasValidPosition = lp.HasValidPosition,
                        RotationYaw   = lp.RotationYaw,
                        RotationPitch = lp.RotationPitch,
                        TeamID        = lp.TeamID,
                        TeamColor     = lp.TeamID >= 0 ? ((ArmbandColorType)lp.TeamID).ToString() : null,
                    };
                }
            }
            catch (Exception ex) { Log.WriteLine($"[MatchDumper] LocalPlayer snapshot failed: {ex.Message}"); }

            return new MatchSnapshot
            {
                DumpedAt      = timestamp,
                MapId         = game.MapID,
                MapName       = game.MapName,
                GameWorldBase = $"0x{game.Base:X}",
                LocalPlayer   = localPlayer,
                Players       = players,
            };
        }

        // ── Writers ──────────────────────────────────────────────────────────────

        private static void WriteSnapshot(MatchSnapshot snapshot)
        {
            try
            {
                Directory.CreateDirectory(DumpsDir);

                string safeMap = string.Concat(snapshot.MapId
                    .Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_'));
                string ts = snapshot.DumpedAt.ToString("yyyyMMdd_HHmmss");
                string path = Path.Combine(DumpsDir, $"{safeMap}_{ts}.json");

                string json = JsonSerializer.Serialize(snapshot, _jsonOptions);
                File.WriteAllText(path, json, Encoding.UTF8);

                Log.WriteLine($"[MatchDumper] Dump written: {path} " +
                    $"({snapshot.Players.Count} players" +
                    (snapshot.LocalPlayer is not null ? ", localPlayer" : "") + ")");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[MatchDumper] Write failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Writes a full IL2CPP class hierarchy dump for every addressable Arena
        /// object (GameWorld, CameraManager, players) to
        /// <c>dumps\&lt;map&gt;_&lt;ts&gt;_il2cpp.txt</c>.
        /// </summary>
        private static void WriteIl2CppDump(LocalGameWorld game, string safeMap, string ts)
        {
            string path = Path.Combine(DumpsDir, $"{safeMap}_{ts}_il2cpp.txt");
            Log.WriteLine($"[MatchDumper] Writing IL2CPP dump to: {path}");

            try
            {
                Directory.CreateDirectory(DumpsDir);
                // Manual Flush after each section so the file is usable even if
                // the app is killed mid-dump.
                using var sw = new StreamWriter(path, false, Encoding.UTF8, 4096);
                var dumpStart = DateTime.UtcNow;

                sw.WriteLine($"// IL2CPP Match Dump — {dumpStart:u}");
                sw.WriteLine($"// Map: {game.MapID} ({game.MapName})");
                sw.WriteLine($"// GameWorld @ 0x{game.Base:X}");
                sw.WriteLine();
                sw.Flush();

                // ── GameWorld ────────────────────────────────────────────────────
                if (!game.InMatch) { sw.WriteLine("// Match ended before dump could complete."); return; }
                sw.WriteLine("═══════════════════════════════════════");
                sw.WriteLine("SECTION: GameWorld");
                sw.WriteLine("═══════════════════════════════════════");
                Il2CppDumper.DumpClassFieldsToWriter(game.Base, sw,
                    $"ClientLocalGameWorld @ 0x{game.Base:X} (map={game.MapID})");
                sw.Flush();
                Log.WriteLine($"[MatchDumper] GameWorld done ({(DateTime.UtcNow - dumpStart).TotalSeconds:F1}s)");

                // ── World (GameWorld._world @ +0x220) ────────────────────────────
                // Arena's GameWorld holds a _world class ptr that fronts the loot
                // sync packets, interactables, and other world-level state. Dumping
                // it surfaces the live ClientWorld layout for offset discovery.
                if (game.InMatch)
                {
                    sw.WriteLine("═══════════════════════════════════════");
                    sw.WriteLine("SECTION: World (GameWorld._world @ +0x220)");
                    sw.WriteLine("═══════════════════════════════════════");
                    try
                    {
                        const uint OffWorld = 0x220;
                        if (Memory.TryReadPtr(game.Base + OffWorld, out var world, false)
                            && world.IsValidVirtualAddress())
                        {
                            Il2CppDumper.DumpClassFieldsToWriter(world, sw,
                                $"ClientWorld (_world) @ 0x{world:X}");
                        }
                        else
                        {
                            sw.WriteLine($"// _world @ 0x{game.Base + OffWorld:X}: pointer null or invalid");
                        }
                    }
                    catch (Exception ex) { sw.WriteLine($"// World dump failed: {ex.Message}"); }
                    sw.Flush();
                    Log.WriteLine($"[MatchDumper] World done ({(DateTime.UtcNow - dumpStart).TotalSeconds:F1}s)");
                }

                // ── CameraManager ────────────────────────────────────────────────
                if (game.CameraManager is { } cm)
                {
                    sw.WriteLine("═══════════════════════════════════════");
                    sw.WriteLine("SECTION: CameraManager");
                    sw.WriteLine("═══════════════════════════════════════");
                    try
                    {
                        if (cm.FPSCamera.IsValidVirtualAddress())
                            Il2CppDumper.DumpClassFieldsToWriter(cm.FPSCamera, sw, $"FPSCamera @ 0x{cm.FPSCamera:X}");
                        else
                            sw.WriteLine("// FPSCamera: not resolved");

                        if (cm.OpticCamera.IsValidVirtualAddress())
                            Il2CppDumper.DumpClassFieldsToWriter(cm.OpticCamera, sw, $"OpticCamera @ 0x{cm.OpticCamera:X}");
                        else
                            sw.WriteLine("// OpticCamera: not resolved (player not currently ADS through scope)");
                    }
                    catch (Exception ex) { sw.WriteLine($"// CameraManager dump failed: {ex.Message}"); }
                    sw.Flush();
                    Log.WriteLine($"[MatchDumper] CameraManager done ({(DateTime.UtcNow - dumpStart).TotalSeconds:F1}s)");
                }
                else
                {
                    sw.WriteLine("// CameraManager: not yet initialized");
                    sw.WriteLine();
                }

                // ── LocalPlayer ──────────────────────────────────────────────────
                if (!game.InMatch) { sw.WriteLine("// Match ended — stopping IL2CPP dump."); return; }
                if (game.LocalPlayer is { } lp && lp.Base.IsValidVirtualAddress())
                {
                    sw.WriteLine("═══════════════════════════════════════");
                    sw.WriteLine("SECTION: LocalPlayer");
                    sw.WriteLine("═══════════════════════════════════════");
                    try
                    {
                        sw.WriteLine($"// {lp.Name} [{lp.Type}] @ 0x{lp.Base:X} (LOCAL)" +
                            $"  pos=({lp.Position.X:F1},{lp.Position.Y:F1},{lp.Position.Z:F1})" +
                            $"  yaw={lp.RotationYaw:F1}° pitch={lp.RotationPitch:F1}°" +
                            $"  team={(lp.TeamID >= 0 ? ((ArmbandColorType)lp.TeamID).ToString() : "?")}");

                        Il2CppDumper.DumpClassFieldsToWriter(lp.Base, sw, $"LocalPlayer (EFT.Player) @ 0x{lp.Base:X}");

                        // MovementContext — yaw/pitch source for the local player
                        if (Memory.TryReadPtr(lp.Base + Offsets.Player.MovementContext, out var mc, false) && mc.IsValidVirtualAddress())
                            Il2CppDumper.DumpClassFieldsToWriter(mc, sw, $"  MovementContext @ 0x{mc:X}");

                        // InventoryController — Arena uses 0x9E0 (auto-probed at runtime if shifted)
                        if (Memory.TryReadPtr(lp.Base + Offsets.Player._inventoryController, out var invCtrl, false) && invCtrl.IsValidVirtualAddress())
                        {
                            Il2CppDumper.DumpClassFieldsToWriter(invCtrl, sw, $"  InventoryController @ 0x{invCtrl:X}");
                            if (Memory.TryReadPtr(invCtrl + Offsets.InventoryController.Inventory, out var inv, false) && inv.IsValidVirtualAddress())
                            {
                                Il2CppDumper.DumpClassFieldsToWriter(inv, sw, $"    Inventory @ 0x{inv:X}");
                                if (Memory.TryReadPtr(inv + Offsets.Inventory.Equipment, out var eq, false) && eq.IsValidVirtualAddress())
                                    Il2CppDumper.DumpClassFieldsToWriter(eq, sw, $"      Equipment @ 0x{eq:X}");
                            }
                        }

                        // LocalPlayer._game @ +0xC80 — back-reference from the local Player
                        // to the Arena.ArenaNetworkGame instance. Holds match-scoped state
                        // (round timer, mode, match info) not reachable from GameWorld.
                        const uint OffLocalPlayerGame = 0xC80;
                        if (Memory.TryReadPtr(lp.Base + OffLocalPlayerGame, out var lpGame, false)
                            && lpGame.IsValidVirtualAddress())
                        {
                            Il2CppDumper.DumpClassFieldsToWriter(lpGame, sw,
                                $"  LocalPlayer._game (ArenaNetworkGame) @ 0x{lpGame:X}");

                            // _game._matchInfo @ +0x228 — Arena.ArenaMatchInfo.
                            // Holds the chosen preset, modes, side, and locations.
                            // We also walk into the generic collections that hold the
                            // ACTIVE game mode (TeamFight / Duel / ShootOut / etc.) so
                            // their internal layout can be reverse-engineered.
                            const uint OffArenaNetworkGameMatchInfo = 0x228;
                            if (Memory.TryReadPtr(lpGame + OffArenaNetworkGameMatchInfo, out var matchInfo, false)
                                && matchInfo.IsValidVirtualAddress())
                            {
                                Il2CppDumper.DumpClassFieldsToWriter(matchInfo, sw,
                                    $"    _game._matchInfo @ 0x{matchInfo:X}");

                                // _matchInfo._selectedModes @ +0x38 — List<ERaidMode>.
                                // Empty in observed dumps; we still walk it for completeness.
                                const uint OffMatchInfoSelectedModes = 0x38;
                                if (Memory.TryReadPtr(matchInfo + OffMatchInfoSelectedModes, out var selectedModes, false)
                                    && selectedModes.IsValidVirtualAddress())
                                {
                                    Il2CppDumper.DumpClassFieldsToWriter(selectedModes, sw,
                                        $"      _matchInfo._selectedModes @ 0x{selectedModes:X}");
                                    try
                                    {
                                        using var modes = MemList<int>.Get(selectedModes, false);
                                        sw.WriteLine($"//       _selectedModes entries: count={modes.Count}");
                                        for (int i = 0; i < modes.Count; i++)
                                            sw.WriteLine($"//         [{i}] ERaidMode = {(ERaidMode)modes[i]} ({modes[i]})");
                                    }
                                    catch (Exception ex) { sw.WriteLine($"//       _selectedModes walk failed: {ex.Message}"); }
                                }
                                else
                                {
                                    sw.WriteLine($"//     _matchInfo._selectedModes @ 0x{matchInfo + OffMatchInfoSelectedModes:X}: null");
                                }

                                // _matchInfo.SelectedSubModeList @ +0x60 — HashSet<EGameMode>.
                                // .NET HashSet<T>.Slot layout (from reference source):
                                //   struct Slot { int hashCode; int next; T value; }
                                // For HashSet<int> that's 12 bytes per slot, packed without
                                // padding. Read the first `_lastIndex` slots so we surface
                                // every live entry (the dump shows _lastIndex==_count==1).
                                const uint OffMatchInfoSelectedSubModeList = 0x60;
                                if (Memory.TryReadPtr(matchInfo + OffMatchInfoSelectedSubModeList, out var subModeList, false)
                                    && subModeList.IsValidVirtualAddress())
                                {
                                    Il2CppDumper.DumpClassFieldsToWriter(subModeList, sw,
                                        $"      _matchInfo.SelectedSubModeList @ 0x{subModeList:X}");
                                    try
                                    {
                                        // HashSet`1 field offsets from the live dump:
                                        const uint HashSetSlotsOff     = 0x18;
                                        const uint HashSetCountOff     = 0x20;
                                        const uint HashSetLastIndexOff = 0x24;
                                        const uint HashSetComparerOff  = 0x30;
                                        const uint SlotsArrayStartOff  = 0x20; // managed array header
                                        const int  SlotStrideBytes     = 12;   // hashCode(4)+next(4)+value(4)

                                        if (Memory.TryReadValue<int>(subModeList + HashSetCountOff,     out var hsCount,     false)
                                            && Memory.TryReadValue<int>(subModeList + HashSetLastIndexOff, out var hsLastIdx, false)
                                            && Memory.TryReadPtr(subModeList + HashSetSlotsOff,         out var hsSlots,     false)
                                            && hsSlots.IsValidVirtualAddress())
                                        {
                                            sw.WriteLine($"//       SelectedSubModeList entries: count={hsCount}, lastIndex={hsLastIdx}");
                                            int scanCount = Math.Min(hsLastIdx, 16);
                                            for (int i = 0; i < scanCount; i++)
                                            {
                                                ulong slot = hsSlots + SlotsArrayStartOff + (ulong)(i * SlotStrideBytes);
                                                // .NET HashSet<T>.Slot field order is hashCode, next, value:
                                                Memory.TryReadValue<int>(slot + 0, out var slotHash, false);
                                                Memory.TryReadValue<int>(slot + 4, out var slotNext, false);
                                                Memory.TryReadValue<int>(slot + 8, out var slotVal,  false);
                                                // Empty/free slots use hashCode == 0; skip them.
                                                if (slotHash == 0 && slotVal == 0 && slotNext == 0) continue;
                                                sw.WriteLine($"//         [{i}] ERaidMode = {(ERaidMode)slotVal} ({slotVal})  (hash=0x{slotHash:X8}, next={slotNext})");
                                            }
                                        }

                                        // Walk _comparer so we can read its IL2CPP class name —
                                        // for HashSet<EGameMode> it will be EqualityComparer of
                                        // the right enum and confirms what type T actually is.
                                        if (Memory.TryReadPtr(subModeList + HashSetComparerOff, out var hsComparer, false)
                                            && hsComparer.IsValidVirtualAddress())
                                        {
                                            Il2CppDumper.DumpClassFieldsToWriter(hsComparer, sw,
                                                $"        SelectedSubModeList._comparer @ 0x{hsComparer:X}");
                                        }
                                    }
                                    catch (Exception ex) { sw.WriteLine($"//       SelectedSubModeList walk failed: {ex.Message}"); }
                                }
                                else
                                {
                                    sw.WriteLine($"//     _matchInfo.SelectedSubModeList @ 0x{matchInfo + OffMatchInfoSelectedSubModeList:X}: null");
                                }

                                // _matchInfo.LocationsByGameMode @ +0x70 — Dictionary<EGameMode, ...>.
                                // Each key is an active EGameMode. Use the existing
                                // MemDictionary wrapper (same one RegisteredPlayers uses
                                // for List<ulong>) — entries are pad(8) + key + value.
                                const uint OffMatchInfoLocationsByGameMode = 0x70;
                                if (Memory.TryReadPtr(matchInfo + OffMatchInfoLocationsByGameMode, out var locByMode, false)
                                    && locByMode.IsValidVirtualAddress())
                                {
                                    Il2CppDumper.DumpClassFieldsToWriter(locByMode, sw,
                                        $"      _matchInfo.LocationsByGameMode @ 0x{locByMode:X}");
                                    try
                                    {
                                        using var dict = MemDictionary<int, ulong>.Get(locByMode, false);
                                        sw.WriteLine($"//       LocationsByGameMode entries: count={dict.Count}");
                                        int idx = 0;
                                        foreach (var entry in dict)
                                        {
                                            var modeName = (ERaidMode)entry.Key;
                                            sw.WriteLine($"//         [{idx++}] ERaidMode = {modeName} ({entry.Key})  value=0x{entry.Value:X}");
                                            // Walk the value pointer (it's a List<Location>) so
                                            // the location data for this mode is visible.
                                            if (entry.Value.IsValidVirtualAddress())
                                            {
                                                Il2CppDumper.DumpClassFieldsToWriter(entry.Value, sw,
                                                    $"          LocationsByGameMode[{modeName}].value @ 0x{entry.Value:X}");
                                            }
                                        }
                                    }
                                    catch (Exception ex) { sw.WriteLine($"//       LocationsByGameMode walk failed: {ex.Message}"); }
                                }
                                else
                                {
                                    sw.WriteLine($"//     _matchInfo.LocationsByGameMode @ 0x{matchInfo + OffMatchInfoLocationsByGameMode:X}: null");
                                }
                            }
                            else
                            {
                                sw.WriteLine($"//   _game._matchInfo @ 0x{lpGame + OffArenaNetworkGameMatchInfo:X}: pointer null or invalid");
                            }

                            // _game._raidMode @ +0x11C — EFT.ERaidMode (inherited from
                            // EFT.NetworkGame`1). Value types are stored inline, so the
                            // int is at the field offset directly — same pattern silk uses
                            // for every enum read (Side, HealthStatus, Pose, etc.).
                            // NOTE: in the current Arena build this field reads 0 in live
                            // matches; the authoritative source is _profileStatus below.
                            const uint OffNetworkGameRaidMode = 0x11C;
                            ulong raidModeAddr = lpGame + OffNetworkGameRaidMode;
                            if (Memory.TryReadValue<int>(raidModeAddr, out var raidModeInt, false))
                                sw.WriteLine($"//   _game._raidMode @ 0x{raidModeAddr:X} = {(ERaidMode)raidModeInt} ({raidModeInt})");
                            else
                                sw.WriteLine($"//   _game._raidMode @ 0x{raidModeAddr:X}: read failed");

                            // _game._profileStatus @ +0x120 — EFT.ProfileStatus. The new
                            // home of the match mode after the NetworkGameData refactor.
                            // Layout (TypeIndex 12431):
                            //   +0x10 profileid    : string
                            //   +0x18 profileToken : string
                            //   +0x20 status       : valuetype (int)
                            //   +0x24 raidMode     : valuetype (int — EFT.ERaidMode)
                            //   +0x28 ip           : string
                            //   +0x30 port         : int
                            //   +0x38 location     : string
                            //   +0x40 sid          : string
                            //   +0x48 gameMode     : string  (mode short name)
                            //   +0x50 shortId      : string  (match short id)
                            //   +0x60 rankingMode  : valuetype (int)
                            const uint OffNetworkGameProfileStatus = 0x120;
                            const uint OffProfileStatusStatus      = 0x20;
                            const uint OffProfileStatusRaidMode    = 0x24;
                            const uint OffProfileStatusIp          = 0x28;
                            const uint OffProfileStatusPort        = 0x30;
                            const uint OffProfileStatusLocation    = 0x38;
                            const uint OffProfileStatusSid         = 0x40;
                            const uint OffProfileStatusGameMode    = 0x48;
                            const uint OffProfileStatusShortId     = 0x50;
                            const uint OffProfileStatusRankingMode = 0x60;
                            if (Memory.TryReadPtr(lpGame + OffNetworkGameProfileStatus, out var profileStatus, false)
                                && profileStatus.IsValidVirtualAddress())
                            {
                                Il2CppDumper.DumpClassFieldsToWriter(profileStatus, sw,
                                    $"  _game._profileStatus @ 0x{profileStatus:X}");

                                if (Memory.TryReadValue<int>(profileStatus + OffProfileStatusRaidMode, out var psRaidMode, false))
                                    sw.WriteLine($"//     _profileStatus.raidMode    = {(ERaidMode)psRaidMode} ({psRaidMode})");
                                if (Memory.TryReadValue<int>(profileStatus + OffProfileStatusStatus, out var psStatus, false))
                                    sw.WriteLine($"//     _profileStatus.status      = {psStatus}");
                                if (Memory.TryReadValue<int>(profileStatus + OffProfileStatusRankingMode, out var psRanking, false))
                                    sw.WriteLine($"//     _profileStatus.rankingMode = {psRanking}");
                                if (Memory.TryReadValue<int>(profileStatus + OffProfileStatusPort, out var psPort, false))
                                    sw.WriteLine($"//     _profileStatus.port        = {psPort}");

                                // String fields — Unity managed strings, decoded with the
                                // standard "ptr → ReadUnityString" pattern silk uses.
                                static string ReadProfileStr(ulong owner, uint off)
                                {
                                    if (!Memory.TryReadPtr(owner + off, out var sp, false) || !sp.IsValidVirtualAddress())
                                        return "null";
                                    try { return $"\"{Memory.ReadUnityString(sp, 128, false)}\""; }
                                    catch { return $"0x{sp:X}"; }
                                }
                                sw.WriteLine($"//     _profileStatus.gameMode    = {ReadProfileStr(profileStatus, OffProfileStatusGameMode)}");
                                sw.WriteLine($"//     _profileStatus.location    = {ReadProfileStr(profileStatus, OffProfileStatusLocation)}");
                                sw.WriteLine($"//     _profileStatus.shortId     = {ReadProfileStr(profileStatus, OffProfileStatusShortId)}");
                                sw.WriteLine($"//     _profileStatus.sid         = {ReadProfileStr(profileStatus, OffProfileStatusSid)}");
                                sw.WriteLine($"//     _profileStatus.ip          = {ReadProfileStr(profileStatus, OffProfileStatusIp)}");
                            }
                            else
                            {
                                sw.WriteLine($"//   _game._profileStatus @ 0x{lpGame + OffNetworkGameProfileStatus:X}: pointer null or invalid");
                            }
                        }
                        else
                        {
                            sw.WriteLine($"// LocalPlayer._game @ 0x{lp.Base + OffLocalPlayerGame:X}: pointer null or invalid");
                        }
                    }
                    catch (Exception ex) { sw.WriteLine($"// LocalPlayer dump failed: {ex.Message}"); }
                    sw.Flush();
                    Log.WriteLine($"[MatchDumper] LocalPlayer done ({(DateTime.UtcNow - dumpStart).TotalSeconds:F1}s)");
                }

                // ── Observed players ─────────────────────────────────────────────
                // Cap to one representative per PlayerType so the file stays manageable
                // — Arena rounds can have many AI guards / teammates with identical
                // layouts.
                if (!game.InMatch) { sw.WriteLine("// Match ended — stopping IL2CPP dump."); return; }
                sw.WriteLine("═══════════════════════════════════════");
                sw.WriteLine("SECTION: ObservedPlayers (one per PlayerType)");
                sw.WriteLine("═══════════════════════════════════════");
                var seenTypes = new HashSet<PlayerType>();
                foreach (var p in game.Players)
                {
                    if (p.IsLocalPlayer) continue;
                    if (!seenTypes.Add(p.Type)) continue;
                    if (!game.InMatch) { sw.WriteLine("// Match ended mid-player-dump — stopping."); break; }

                    try
                    {
                        if (!p.Base.IsValidVirtualAddress()) continue;

                        sw.WriteLine($"// {p.Name} [{p.Type}] @ 0x{p.Base:X}" +
                            $"  pos=({p.Position.X:F1},{p.Position.Y:F1},{p.Position.Z:F1})" +
                            $"  yaw={p.RotationYaw:F1}° pitch={p.RotationPitch:F1}°" +
                            $"  team={(p.TeamID >= 0 ? ((ArmbandColorType)p.TeamID).ToString() : "?")}" +
                            (p.IsAI ? "  AI" : ""));

                        DumpObservedPlayerHierarchy(p, sw);
                    }
                    catch (Exception ex)
                    {
                        try { sw.WriteLine($"// Player dump failed: {ex.Message}"); } catch { }
                    }
                    sw.Flush();
                }
                Log.WriteLine($"[MatchDumper] Players done ({(DateTime.UtcNow - dumpStart).TotalSeconds:F1}s)");

                sw.Flush();
                Log.WriteLine($"[MatchDumper] IL2CPP dump complete in {(DateTime.UtcNow - dumpStart).TotalSeconds:F1}s: {path}");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[MatchDumper] IL2CPP dump write failed: {ex.Message}");
            }
        }

        // ── Observed-player sub-object dump ──────────────────────────────────────

        /// <summary>
        /// Dumps the relevant sub-object chain for a single observed player:
        /// OPV → OPC → InventoryController/HealthController/MovementController/
        /// StateContext/PlayerBody (skeleton + bones).
        /// </summary>
        private static void DumpObservedPlayerHierarchy(Player p, StreamWriter sw)
        {
            ulong playerBase = p.Base;
            string name = p.Name;

            // ObservedPlayerView root
            try
            {
                Il2CppDumper.DumpClassFieldsToWriter(playerBase, sw,
                    $"  ObservedPlayerView [{name}] @ 0x{playerBase:X}");
            }
            catch { /* root failure — skip */ }

            // ObservedPlayerController
            if (!Memory.TryReadPtr(playerBase + Offsets.ObservedPlayerView.ObservedPlayerController, out var opc, false)
                || !opc.IsValidVirtualAddress())
                return;

            try
            {
                Il2CppDumper.DumpClassFieldsToWriter(opc, sw,
                    $"    ObservedPlayerController [{name}] @ 0x{opc:X}");
            }
            catch { }

            // HealthController
            try
            {
                if (Memory.TryReadPtr(opc + Offsets.ObservedPlayerController.HealthController, out var hc, false)
                    && hc.IsValidVirtualAddress())
                {
                    Il2CppDumper.DumpClassFieldsToWriter(hc, sw,
                        $"      ObservedHealthController [{name}] @ 0x{hc:X}");
                }
            }
            catch { }

            // InventoryController → Inventory → Equipment
            try
            {
                if (Memory.TryReadPtr(opc + Offsets.ObservedPlayerController.InventoryController, out var invCtrl, false)
                    && invCtrl.IsValidVirtualAddress())
                {
                    Il2CppDumper.DumpClassFieldsToWriter(invCtrl, sw,
                        $"      ObservedInventoryController [{name}] @ 0x{invCtrl:X}");

                    if (Memory.TryReadPtr(invCtrl + Offsets.InventoryController.Inventory, out var inv, false)
                        && inv.IsValidVirtualAddress())
                    {
                        Il2CppDumper.DumpClassFieldsToWriter(inv, sw,
                            $"        Inventory [{name}] @ 0x{inv:X}");

                        if (Memory.TryReadPtr(inv + Offsets.Inventory.Equipment, out var eq, false)
                            && eq.IsValidVirtualAddress())
                        {
                            Il2CppDumper.DumpClassFieldsToWriter(eq, sw,
                                $"          Equipment [{name}] @ 0x{eq:X}");
                        }
                    }
                }
            }
            catch { }

            // MovementController → StateContext
            try
            {
                if (Memory.TryReadPtr(opc + Offsets.ObservedPlayerController.MovementController, out var mc, false)
                    && mc.IsValidVirtualAddress())
                {
                    Il2CppDumper.DumpClassFieldsToWriter(mc, sw,
                        $"      ObservedMovementController [{name}] @ 0x{mc:X}");

                    if (Memory.TryReadPtr(mc + Offsets.ObservedMovementController.StateContext, out var sc, false)
                        && sc.IsValidVirtualAddress())
                    {
                        Il2CppDumper.DumpClassFieldsToWriter(sc, sw,
                            $"        ObservedPlayerStateContext [{name}] @ 0x{sc:X}");

                        if (Memory.TryReadPtr(sc + Offsets.ObservedPlayerStateContext._playerTransform, out var bif, false)
                            && bif.IsValidVirtualAddress())
                        {
                            Il2CppDumper.DumpClassFieldsToWriter(bif, sw,
                                $"          BifacialTransform [{name}] @ 0x{bif:X}");
                        }
                    }
                }
            }
            catch { }

            // Culling — useful for diagnosing freeze-on-cull issues
            try
            {
                if (Memory.TryReadPtr(opc + Offsets.ObservedPlayerController.Culling, out var culling, false)
                    && culling.IsValidVirtualAddress())
                {
                    Il2CppDumper.DumpClassFieldsToWriter(culling, sw,
                        $"      Culling [{name}] @ 0x{culling:X}");
                }
            }
            catch { }

            // PlayerBody chain (skeleton + bones)
            try
            {
                if (Memory.TryReadPtr(playerBase + Offsets.ObservedPlayerView.PlayerBody, out var body, false)
                    && body.IsValidVirtualAddress())
                {
                    Il2CppDumper.DumpClassFieldsToWriter(body, sw,
                        $"    PlayerBody [{name}] @ 0x{body:X}");

                    if (Memory.TryReadPtr(body + Offsets.PlayerBody.PlayerBones, out var bones, false)
                        && bones.IsValidVirtualAddress())
                    {
                        Il2CppDumper.DumpClassFieldsToWriter(bones, sw,
                            $"      PlayerBones [{name}] @ 0x{bones:X}");
                    }

                    if (Memory.TryReadPtr(body + Offsets.PlayerBody.SkeletonRootJoint, out var skel, false)
                        && skel.IsValidVirtualAddress())
                    {
                        Il2CppDumper.DumpClassFieldsToWriter(skel, sw,
                            $"      DizSkinningSkeleton (root) [{name}] @ 0x{skel:X}");
                    }

                    if (Memory.TryReadPtr(body + Offsets.PlayerBody.SkeletonHands, out var skelHands, false)
                        && skelHands.IsValidVirtualAddress())
                    {
                        Il2CppDumper.DumpClassFieldsToWriter(skelHands, sw,
                            $"      DizSkinningSkeleton (hands) [{name}] @ 0x{skelHands:X}");
                    }
                }
            }
            catch { }

            // Managed look-raycast transform — same one the realtime worker reads
            try
            {
                if (Memory.TryReadPtr(playerBase + Offsets.ObservedPlayerView._playerLookRaycastTransform, out var lookT, false)
                    && lookT.IsValidVirtualAddress())
                {
                    Il2CppDumper.DumpClassFieldsToWriter(lookT, sw,
                        $"    LookTransform (managed) [{name}] @ 0x{lookT:X}");
                }
            }
            catch { }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static DumpVec3 ToDumpVec(Vector3 v) => new() { X = v.X, Y = v.Y, Z = v.Z };

        // ── DTOs ─────────────────────────────────────────────────────────────────

        private sealed class MatchSnapshot
        {
            [JsonPropertyName("dumpedAt")]      public DateTime DumpedAt { get; init; }
            [JsonPropertyName("mapId")]         public string MapId { get; init; } = "";
            [JsonPropertyName("mapName")]       public string MapName { get; init; } = "";
            [JsonPropertyName("gameWorldBase")] public string GameWorldBase { get; init; } = "";
            [JsonPropertyName("localPlayer")]   public DumpLocalPlayer? LocalPlayer { get; init; }
            [JsonPropertyName("players")]       public List<DumpPlayer> Players { get; init; } = [];
        }

        private sealed class DumpVec3
        {
            [JsonPropertyName("x")] public float X { get; init; }
            [JsonPropertyName("y")] public float Y { get; init; }
            [JsonPropertyName("z")] public float Z { get; init; }
        }

        private sealed class DumpLocalPlayer
        {
            [JsonPropertyName("name")]             public string Name { get; init; } = "";
            [JsonPropertyName("base")]             public string Base { get; init; } = "";
            [JsonPropertyName("profileId")]        public string? ProfileId { get; init; }
            [JsonPropertyName("accountId")]        public string? AccountId { get; init; }
            [JsonPropertyName("isAlive")]          public bool IsAlive { get; init; }
            [JsonPropertyName("isActive")]         public bool IsActive { get; init; }
            [JsonPropertyName("position")]         public DumpVec3? Position { get; init; }
            [JsonPropertyName("hasValidPosition")] public bool HasValidPosition { get; init; }
            [JsonPropertyName("rotationYaw")]      public float RotationYaw { get; init; }
            [JsonPropertyName("rotationPitch")]    public float RotationPitch { get; init; }
            [JsonPropertyName("teamId")]           public int TeamID { get; init; }
            [JsonPropertyName("teamColor")]        public string? TeamColor { get; init; }
        }

        private sealed class DumpPlayer
        {
            [JsonPropertyName("name")]             public string Name { get; init; } = "";
            [JsonPropertyName("type")]             public string Type { get; init; } = "";
            [JsonPropertyName("isLocalPlayer")]    public bool IsLocalPlayer { get; init; }
            [JsonPropertyName("isAI")]             public bool IsAI { get; init; }
            [JsonPropertyName("isActive")]         public bool IsActive { get; init; }
            [JsonPropertyName("isAlive")]          public bool IsAlive { get; init; }
            [JsonPropertyName("position")]         public DumpVec3? Position { get; init; }
            [JsonPropertyName("hasValidPosition")] public bool HasValidPosition { get; init; }
            [JsonPropertyName("rotationYaw")]      public float RotationYaw { get; init; }
            [JsonPropertyName("rotationPitch")]    public float RotationPitch { get; init; }
            [JsonPropertyName("profileId")]        public string? ProfileId { get; init; }
            [JsonPropertyName("accountId")]        public string? AccountId { get; init; }
            [JsonPropertyName("teamId")]           public int TeamID { get; init; }
            [JsonPropertyName("teamColor")]        public string? TeamColor { get; init; }
            [JsonPropertyName("base")]             public string Base { get; init; } = "";
        }
    }
}
