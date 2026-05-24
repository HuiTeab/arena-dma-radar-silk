using SDK;
using VmmSharpEx;
using VmmSharpEx.Options;
using VmmSharpEx.Scatter;

namespace eft_dma_radar.Arena.GameWorld
{
    /// <summary>
    /// Periodic alive/dead + health-status scatter.
    /// Reads <c>ObservedHealthController.IsAlive</c> and the
    /// <c>ObservedHealthController.HealthStatus</c> ETagStatus bitmask for every
    /// observed player whose OHC has been resolved, in a single scatter pass.
    ///
    /// <para>
    /// Cadence: kicked from <see cref="RefreshRegistration"/> every
    /// <see cref="HealthBatchIntervalMs"/> ms. The registration worker itself runs
    /// at ~100 ms, so the actual interval between scatter passes is the next
    /// registration tick after the cooldown elapses.
    /// </para>
    /// </summary>
    internal sealed partial class RegisteredPlayers
    {
        // ETagStatus bitmask values — match silk's constants. These are the
        // BSG-side [Flags] enum bits we recognise. Anything outside Healthy
        // tier is "alive but injured" — the player is still rendered.
        private const int ETagDying        = 0x2000; // 8192
        private const int ETagBadlyInjured = 0x1000; // 4096
        private const int ETagInjured      = 0x0800; // 2048

        // Scatter cadence. Health flips at most once per match per player (death),
        // so a 250 ms interval is plenty and keeps DMA traffic minimal.
        private const long HealthBatchIntervalMs = 250;

        // Back-off after a failed OHC resolve attempt. Keeps us from re-scattering
        // a stuck/uninitialised OPC every tick for a player who just spawned.
        private const long OhcResolveBackoffMs = 500;

        private long _nextHealthBatchTick;

        /// <summary>
        /// Two-phase scatter:
        ///   <list type="number">
        ///     <item><b>Phase 1</b> — for observed players without a resolved
        ///       <see cref="Player.ObservedHealthControllerAddr"/> and past their
        ///       back-off, walk <c>ObservedPlayerController.HealthController</c>,
        ///       verify by reading the back-ref <c>OHC._player</c>, and cache.</item>
        ///     <item><b>Phase 2</b> — for everyone with a resolved OHC, read
        ///       <c>IsAlive</c> (bool/byte at +0x14) and <c>HealthStatus</c>
        ///       (int32 at +0x10) in one scatter, then update the cached fields.</item>
        ///   </list>
        /// Local player is skipped — Arena's local player uses a different health
        /// chain (<c>EFT.Player._healthController</c>); not wired up here.
        /// </summary>
        private void BatchUpdateHealthStatuses()
        {
            long nowTick = Environment.TickCount64;
            if (nowTick < _nextHealthBatchTick) return;
            _nextHealthBatchTick = nowTick + HealthBatchIntervalMs;

            // Snapshot the values collection so we don't enumerate the live dictionary
            // while RefreshRegistration may be mutating it on the same thread.
            // (RegistrationWorker is single-threaded, but be defensive.)
            var snapshot = _players.Values.ToArray();
            if (snapshot.Length == 0) return;

            // ── Phase 1: resolve OHC for new observed players ────────────────
            // OHC lives at OPC + 0x120; we verify by reading the back-ref
            // (OHC._player at +0x18) and matching it against the player's base.
            var needResolve = new List<Player>(snapshot.Length);
            foreach (var p in snapshot)
            {
                if (p.IsLocalPlayer) continue;
                if (p.ObservedHealthControllerAddr != 0) continue;
                if (nowTick < p.NextOhcResolveTick) continue;
                needResolve.Add(p);
            }

            if (needResolve.Count > 0)
            {
                // 1a — read OPC pointer + OHC pointer per player.
                ulong[] opcAddrs = new ulong[needResolve.Count];
                ulong[] ohcAddrs = new ulong[needResolve.Count];
                using (var s = Memory.GetScatter(VmmFlags.NOCACHE))
                {
                    for (int i = 0; i < needResolve.Count; i++)
                        s.PrepareReadValue<ulong>(needResolve[i].Base + Offsets.ObservedPlayerView.ObservedPlayerController);
                    s.Execute();
                    for (int i = 0; i < needResolve.Count; i++)
                        s.ReadValue<ulong>(needResolve[i].Base + Offsets.ObservedPlayerView.ObservedPlayerController, out opcAddrs[i]);
                }
                using (var s = Memory.GetScatter(VmmFlags.NOCACHE))
                {
                    for (int i = 0; i < needResolve.Count; i++)
                        if (opcAddrs[i].IsValidVirtualAddress())
                            s.PrepareReadValue<ulong>(opcAddrs[i] + Offsets.ObservedPlayerController.HealthController);
                    s.Execute();
                    for (int i = 0; i < needResolve.Count; i++)
                        if (opcAddrs[i].IsValidVirtualAddress())
                            s.ReadValue<ulong>(opcAddrs[i] + Offsets.ObservedPlayerController.HealthController, out ohcAddrs[i]);
                }

                // 1b — verify each OHC by reading its back-ref to the player base.
                ulong[] backrefs = new ulong[needResolve.Count];
                using (var s = Memory.GetScatter(VmmFlags.NOCACHE))
                {
                    for (int i = 0; i < needResolve.Count; i++)
                        if (ohcAddrs[i].IsValidVirtualAddress())
                            s.PrepareReadValue<ulong>(ohcAddrs[i] + Offsets.ObservedHealthController.Player);
                    s.Execute();
                    for (int i = 0; i < needResolve.Count; i++)
                        if (ohcAddrs[i].IsValidVirtualAddress())
                            s.ReadValue<ulong>(ohcAddrs[i] + Offsets.ObservedHealthController.Player, out backrefs[i]);
                }

                for (int i = 0; i < needResolve.Count; i++)
                {
                    var p = needResolve[i];
                    if (ohcAddrs[i].IsValidVirtualAddress() && backrefs[i] == p.Base)
                    {
                        p.ObservedHealthControllerAddr = ohcAddrs[i];
                    }
                    else
                    {
                        // Either the OPC isn't ready yet, the HealthController hasn't
                        // been wired, or the back-ref doesn't match. Back off so we
                        // don't burn scatter slots on the same hopeless candidate.
                        p.NextOhcResolveTick = nowTick + OhcResolveBackoffMs;
                    }
                }
            }

            // ── Phase 2: read IsAlive + HealthStatus for all resolved OHCs ───
            int resolvedCount = 0;
            for (int i = 0; i < snapshot.Length; i++)
                if (!snapshot[i].IsLocalPlayer && snapshot[i].ObservedHealthControllerAddr != 0)
                    resolvedCount++;
            if (resolvedCount == 0) return;

            using (var s = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                foreach (var p in snapshot)
                {
                    if (p.IsLocalPlayer) continue;
                    if (p.ObservedHealthControllerAddr == 0) continue;
                    s.PrepareReadValue<byte>(p.ObservedHealthControllerAddr + Offsets.ObservedHealthController.IsAlive);
                    s.PrepareReadValue<int>(p.ObservedHealthControllerAddr + Offsets.ObservedHealthController.HealthStatus);
                }
                s.Execute();

                foreach (var p in snapshot)
                {
                    if (p.IsLocalPlayer) continue;
                    if (p.ObservedHealthControllerAddr == 0) continue;

                    if (s.ReadValue<byte>(p.ObservedHealthControllerAddr + Offsets.ObservedHealthController.IsAlive, out var aliveByte))
                    {
                        bool nowAlive = aliveByte != 0;
                        if (p.IsAlive && !nowAlive)
                            Log.WriteLine($"[RegisteredPlayers] '{p.Name}' died (OHC.IsAlive=false)");
                        p.IsAlive = nowAlive;
                    }

                    if (s.ReadValue<int>(p.ObservedHealthControllerAddr + Offsets.ObservedHealthController.HealthStatus, out var tag))
                    {
                        // [Flags] enum — check worst-first so the bucket reflects the
                        // most-severe currently-active tag.
                        if ((tag & ETagDying) != 0)
                            p.HealthStatus = EHealthStatus.Dying;
                        else if ((tag & ETagBadlyInjured) != 0)
                            p.HealthStatus = EHealthStatus.BadlyInjured;
                        else if ((tag & ETagInjured) != 0)
                            p.HealthStatus = EHealthStatus.Injured;
                        else
                            p.HealthStatus = EHealthStatus.Healthy;
                    }
                }
            }
        }
    }
}
