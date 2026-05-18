namespace eft_dma_radar.Arena.GameWorld
{
    using eft_dma_radar.Arena.GameWorld.Players;
    using eft_dma_radar.Arena.Unity;
    using SDK;

    /// <summary>
    /// Player type classification (mirrors Silk's PlayerType).
    /// </summary>
    internal enum PlayerType
    {
        Default = 0,
        LocalPlayer,
        Teammate,
        USEC,
        BEAR,
        PScav,
        AIScav,
        AIRaider,
        AIBoss,
        AIGuard,
    }

    /// <summary>
    /// Health-status bucket derived from the <c>ObservedHealthController.HealthStatus</c>
    /// bitmask. The bitmask is a [Flags] enum on the BSG side (ETagStatus) — we collapse
    /// it to the worst-active tier for radar rendering.
    /// </summary>
    internal enum EHealthStatus
    {
        Healthy = 0,
        Injured,
        BadlyInjured,
        Dying,
    }

    /// <summary>
    /// Represents a single player tracked in the current Arena match.
    /// Written by the registration worker (identity) and realtime worker (position/rotation).
    /// </summary>
    internal sealed class Player
    {
        // ── Identity (written once during discovery) ──────────────────────

        /// <summary>Raw memory address of the player object (ObservedPlayerView or ClientPlayer).</summary>
        public ulong Base;

        /// <summary>Display name (nickname or role label for AI).</summary>
        public string Name = string.Empty;

        /// <summary>Account ID — not sent by Arena's server to other clients; always null.</summary>
        public string? AccountId;

        /// <summary>Profile ID string — may be set for AI too.</summary>
        public string? ProfileId;

        /// <summary>Player classification.</summary>
        public PlayerType Type;

        /// <summary>True if this is the local (MainPlayer) instance.</summary>
        public bool IsLocalPlayer;

        /// <summary>True if this player is AI-controlled.</summary>
        public bool IsAI;

        /// <summary>Armband-based Arena team id (-1 if unknown / no armband).</summary>
        public int TeamID = -1;

        /// <summary>True once the nickname was successfully read from memory (vs. the PMC/PScav fallback).</summary>
        internal bool NameResolved;
        /// <summary>True once the faction/side was successfully resolved from memory (vs. Default).</summary>
        internal bool TypeResolved;

        // ── State (updated each registration tick) ────────────────────────

        public bool IsActive;

        /// <summary>
        /// Live alive/dead state read from <c>ObservedHealthController.IsAlive</c> by the
        /// periodic health scatter (<see cref="RegisteredPlayers.BatchUpdateHealthStatuses"/>).
        /// Starts <c>true</c> at registration; flips to <c>false</c> the moment the
        /// observed-side health controller reports death — does NOT require the player
        /// to leave the registered list (so corpses on the floor render as dead).
        /// </summary>
        public bool IsAlive;

        /// <summary>
        /// Bucketed health status derived from <c>ObservedHealthController.HealthStatus</c>'s
        /// ETagStatus bitmask. <see cref="EHealthStatus.Healthy"/> until the periodic
        /// health scatter resolves a real value. Independent of <see cref="IsAlive"/>:
        /// a "Dying" player is still alive.
        /// </summary>
        public EHealthStatus HealthStatus = EHealthStatus.Healthy;

        /// <summary>True when the position has been successfully computed at least once.</summary>
        public bool HasValidPosition;

        // ── Realtime data (written by realtime scatter thread) ────────────

        /// <summary>World position in Unity space.</summary>
        public Vector3 Position;

        /// <summary>Yaw angle in degrees [0, 360).</summary>
        public float RotationYaw;

        /// <summary>Pitch angle in degrees.</summary>
        public float RotationPitch;

        /// <summary>
        /// Stable world position for top-down rendering (radar map dot, distance/height
        /// labels). Prefers the <c>HumanPelvis</c> bone from the skeleton scatter (body
        /// center — doesn't swing with stance/ADS like <see cref="Position"/> does,
        /// because that field tracks <c>_playerLookRaycastTransform</c> which moves to
        /// the scope/hand/eye depending on weapon state). Falls back to
        /// <see cref="Position"/> when the skeleton hasn't been resolved yet (early
        /// frames, distant players) so existing behaviour is preserved.
        /// </summary>
        public Vector3 MapPosition
        {
            get
            {
                var pelvis = Skeleton?.GetBonePosition(Bones.HumanPelvis);
                return pelvis ?? Position;
            }
        }

        // ── Transform cache (managed by RegisteredPlayers) ────────────────

        internal ulong TransformInternal;
        internal ulong VerticesAddr;
        internal int TransformIndex;
        internal volatile bool TransformReady;
        internal int[]? CachedIndices;

        internal ulong RotationAddr;
        internal volatile bool RotationReady;

        internal int ConsecutiveErrors;
        internal bool RealtimeEstablished;

        /// <summary>
        /// TickCount64 timestamp of the last time <see cref="Position"/> actually changed value.
        /// Retained for telemetry; freeze-detection was removed once world position became
        /// computed from the live TrsX[] every tick.
        /// </summary>
        internal long LastPositionChangeMs;

        /// <summary>
        /// Consecutive registration ticks this player has been absent from the RegisteredPlayers
        /// list. Used as a grace period so transient list-read flickers / invalid pointer hiccups
        /// don't immediately wipe a player who is still alive in the match.
        /// </summary>
        internal int MissingTicks;

        // ── Back-off timers (Environment.TickCount64) ─────────────────────
        // When non-zero, skip the corresponding init/retry until TickCount64 reaches this value.
        internal long NextTransformInitTick;
        internal long NextRotationInitTick;
        internal long NextTeamIdTick;
        internal long NextNameResolveTick;
        internal int  NameResolveFailStreak;
        internal int  TransformInitFailStreak;
        internal int  RotationInitFailStreak;
        internal int  TeamIdFailStreak;

        /// <summary>Cached ArmBand slot pointer — avoids re-scanning the equipment slots array on every TeamID read.</summary>
        internal ulong ArmBandSlotAddr;

        /// <summary>
        /// Cached pointer to the player's <c>ObservedHealthController</c>. Resolved once
        /// during discovery (or on the next periodic refresh) and reused by the periodic
        /// IsAlive / HealthStatus scatter. Zero until resolved — periodic batch skips
        /// players whose OHC is still unresolved.
        /// </summary>
        internal ulong ObservedHealthControllerAddr;

        /// <summary>
        /// Back-off tick for re-resolving <see cref="ObservedHealthControllerAddr"/> after
        /// a transient failure (e.g. controller still null during respawn).
        /// </summary>
        internal long NextOhcResolveTick;

        // ── Skeleton / bone data (written by camera worker) ───────────────

        /// <summary>Per-player skeleton (null until resolved; null for LocalPlayer).</summary>
        internal Skeleton? Skeleton;

        /// <summary>Back-off timer for skeleton init retries.</summary>
        internal long NextSkeletonInitTick;

        /// <summary>Consecutive skeleton-init failures (for exponential back-off).</summary>
        internal int SkeletonInitFailStreak;

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Name).Append(" (").Append(Type).Append(')');
            sb.Append(" @ ").Append(Position);
            sb.Append(" yaw=").Append(RotationYaw.ToString("F1")).Append('°');
            // AccountId omitted — Arena server never sends it to other clients
            if (ProfileId is not null)
                sb.Append(" prof=").Append(ProfileId);
            if (TeamID >= 0)
                sb.Append(" team=").Append((ArmbandColorType)TeamID);
            return sb.ToString();
        }
    }
}
