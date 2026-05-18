#pragma warning disable IDE0130
#pragma warning disable CA2211

// ──────────────────────────────────────────────────────────────────────────────
// Arena SDK offsets — fallback values overwritten at runtime by the IL2CPP dumper.
// The dumper resolves offsets via reflection on typeof(Offsets).
// All values below are placeholder stubs; populate after first successful dump.
// ──────────────────────────────────────────────────────────────────────────────

namespace SDK
{
    public readonly partial struct Offsets
    {
        // ── Il2CppClass layout (stable across EFT builds) ──
        public readonly partial struct Il2CppClass
        {
            public const uint Name        = 0x10;
            public const uint Namespace   = 0x18;
            public const uint Parent      = 0x58;
            public const uint Fields      = 0x80;
            public const uint StaticFields = 0xB8;
            public const uint Methods     = 0x98;
            public const uint MethodCount = 0x120;
            public const uint FieldCount  = 0x124;
        }

        // ── TypeInfoTable RVA within GameAssembly.dll ──
        // Populated by TypeInfoTableResolver sig-scan at startup.
        public readonly partial struct Special
        {
            public static ulong TypeInfoTableRva = 0x0;
            public static uint GamePlayerOwner_TypeIndex = 0;
        }

        // ── GameWorld / LocalGameWorld ──────────────────────────────────────
        public readonly partial struct ClientLocalGameWorld
        {
            public static uint RegisteredPlayers = 0x1B8;
            public static uint MainPlayer        = 0x218;
            public static uint LocationId        = 0xD0;
        }

        // ── ObservedPlayerView (EFT.NextObservedPlayer.ObservedPlayerView) ──
        // Field offsets verified against C:\Temp\il2cpp_full_dump.txt [14996]
        public readonly partial struct ObservedPlayerView
        {
            public static uint ObservedPlayerController = 0x28;   // <ObservedPlayerController>k__BackingField
            public static uint Voice                    = 0x40;   // <Voice>k__BackingField
            public static uint Id                       = 0x7C;   // <Id>k__BackingField
            public static uint Side                     = 0x9C;   // <Side>k__BackingField (EPlayerSide valuetype)
            public static uint IsAI                     = 0xA8;   // <IsAI>k__BackingField
            public static uint ProfileId                = 0xB0;   // <ProfileId>k__BackingField
            public static uint NickName                 = 0xC0;   // <NickName>k__BackingField
            // AccountId (0xC8): field exists in EFT.NextObservedPlayer.ObservedPlayerView but Arena's
            // server never sends it to other clients — always null in practice; not read.
            public static uint PlayerBody               = 0xE0;   // <PlayerBody>k__BackingField
            public static uint _playerLookRaycastTransform = 0x110; // _playerLookRaycastTransform
        }

        // ── ObservedPlayerController (EFT.NextObservedPlayer.ObservedPlayerController) ──
        // Field offsets verified against dump [14856]
        public readonly partial struct ObservedPlayerController
        {
            public static uint MovementController = 0x110; // <MovementController>k__BackingField
            public static uint HealthController   = 0x120; // <HealthController>k__BackingField
            public static uint InventoryController = 0x10; // <InventoryController>k__BackingField
            public static uint Culling            = 0x100; // <Culling>k__BackingField
        }

        // ── Inventory chain (for armband-based TeamID detection) ─────────────
        public readonly partial struct InventoryController
        {
            public static uint Inventory = 0x120; // <Inventory>k__BackingField
        }

        public readonly partial struct Inventory
        {
            public static uint Equipment = 0x18; // InventoryLogic.Inventory.Equipment (plain field)
        }

        public readonly partial struct CompoundItem
        {
            public static uint Slots = 0x98; // EFT.InventoryLogic.CompoundItem.Slots — Equipment extends this
        }

        public readonly partial struct Slot
        {
            public static uint ContainedItem = 0x58; // <ContainedItem>k__BackingField
            public static uint ID            = 0x68; // <ID>k__BackingField
        }

        public readonly partial struct LootItem
        {
            public static uint Template = 0x78; // EFT.InventoryLogic.Item <Template>k__BackingField
        }

        public readonly partial struct ItemTemplate
        {
            public static uint _id = 0x110; // <_id>k__BackingField (MongoID valuetype)
        }

        // ── ObservedMovementController rotation ──────────────────────────────
        // Verified from live Arena dumps:
        // EFT.NextObservedPlayer.ObservedPlayerMovementController:
        //   0x10 <Model>k__BackingField (valuetype — NOT used for rotation in Arena)
        //   0xB0 <ObservedPlayerStateContext>k__BackingField (class ptr)
        // EFT.NextObservedPlayer.ObservedPlayerStateContext:
        //   0x20 <Rotation>k__BackingField (Vector2: yaw X, pitch Y)
        public readonly partial struct ObservedMovementController
        {
            public static uint StateContext = 0xB0; // ptr → ObservedPlayerStateContext
        }

        public readonly partial struct ObservedPlayerStateContext
        {
            public static uint Rotation         = 0x20; // valuetype Vector2 (yaw X, pitch Y)
            public static uint _playerTransform = 0x98; // BifacialTransform — alternative stable transform
        }

        // ── GamePlayerOwner (IL2CPP singleton static class) ────────────────────
        public readonly partial struct GamePlayerOwner
        {
            public static uint _myPlayer = 0x8;
        }

        // ── Player / FirstPersonCamera ──────────────────────────────────────
        public readonly partial struct Player
        {
            public static uint GameWorld        = 0x640;  // EFT.Player [12002] → GameWorld ptr
            public static uint MovementContext  = 0x70;   // <MovementContext>k__BackingField
            public static uint _playerLookRaycastTransform = 0xA88; // EFT.Player._playerLookRaycastTransform
            // Arena-verified via runtime offset auto-probe (see RegisteredPlayers.ProbeLocalInventoryControllerOffset).
            // EFT-mainline value here is 0x980; Arena's Player layout has it at 0x9E0.
            public static uint _inventoryController = 0x9E0;
            /// <summary>EFT.Player → Arena.ArenaNetworkGame (the "_game" back-reference).</summary>
            public static uint _game = 0xC80;
        }

        // ── EFT.NetworkGame`1 → EFT.ProfileStatus ────────────────────────────
        // _profileStatus is the authoritative source for the running match's
        // raid mode, server, location, and lobby short-id.  The pre-IL2CPP
        // arena-dma-radar read this via NetworkGame→NetworkGameData→raidMode;
        // the new build inlines the field on NetworkGame and the runtime data
        // lives on the linked ProfileStatus object.
        public readonly partial struct NetworkGame
        {
            public static uint _profileStatus = 0x120;
        }

        // ── EFT.ProfileStatus (TypeIndex 12431) ──────────────────────────────
        // Field layout per the live IL2CPP class dump.
        public readonly partial struct ProfileStatus
        {
            public static uint profileid      = 0x10; // string
            public static uint profileToken   = 0x18; // string
            public static uint status         = 0x20; // int
            public static uint raidMode       = 0x24; // int → ERaidMode
            public static uint ip             = 0x28; // string
            public static uint port           = 0x30; // int
            public static uint location       = 0x38; // string  (e.g. "Arena_Bay5")
            public static uint sid            = 0x40; // string  (full session id)
            public static uint gameMode       = 0x48; // string  (e.g. "deathmatch")
            public static uint shortId        = 0x50; // string  (6-char lobby code)
            public static uint utcPenaltyTime = 0x58; // double
            public static uint rankingMode    = 0x60; // int
        }

        // ── MovementContext (EFT.MovementContext [12338]) ────────────────────
        public readonly partial struct MovementContext
        {
            public static uint _rotation = 0xD4; // valuetype Vector2 (yaw X, pitch Y)
        }

        public readonly partial struct PlayerBody
        {
            public static uint PlayerBones      = 0x28; // PlayerBones named-bone references
            public static uint SkeletonRootJoint = 0x30; // DizSkinningSkeleton — bone transform list
            public static uint SkeletonHands     = 0x38; // DizSkinningSkeleton for hands
        }

        // ── Diz.Skinning.Skeleton ──────────────────────────────────────────
        // EFT-side: <_values>k__BackingField at 0x30 — verified against silk reference.
        public readonly partial struct DizSkinningSkeleton
        {
            public static uint _values = 0x30;
        }

        // ── Unity TrsX (Transform) ──────────────────────────────────────────
        public readonly partial struct TrsX
        {
            public const uint Position = 0x90;
        }

        // ── GOM (GameObjectManager) entry ───────────────────────────────────
        public readonly partial struct GameObjectManager
        {
            public const uint LastTaggedNode      = 0x0;
            public const uint TaggedNodes         = 0x8;
            public const uint LastMainCameraTaggedNode = 0x38;
            public const uint MainCameraTaggedNodes   = 0x40;
        }

        public readonly partial struct TaggedObject
        {
            public const uint Object = 0x10;
            public const uint Next   = 0x28;
        }

        // ── Unity string ────────────────────────────────────────────────────
        public readonly partial struct UnityString
        {
            public const uint Length = 0x10;
            public const uint Data   = 0x14;
        }

        // ── AssemblyCSharp ──────────────────────────────────────────────────
        public readonly partial struct AssemblyCSharp
        {
            public static uint TypeStart = 0;
            public static uint TypeCount = 0;
        }

        // ── EFT.CameraControl.CameraManager (singleton) ─────────────────────
        // Field offsets from Arena IL2CPP dump (overwritten at runtime by dumper).
        // Layout matches EFT-silk: <OpticCameraManager>k__BackingField @ 0x10,
        // <Camera>k__BackingField @ 0x68. get_Instance RVA from dump.
        public readonly partial struct EFTCameraManager
        {
            public static uint OpticCameraManager = 0x10;
            public static uint Camera             = 0x68;
            public static uint GetInstance_RVA    = 0x1BCA160;
        }

        // ── EFT.CameraControl.OpticCameraManager ────────────────────────────
        public readonly partial struct OpticCameraManager
        {
            public static uint Camera = 0x70;
        }
    }

    // ── Memory layout structs ────────────────────────────────────────────────
    public readonly struct Types
    {
        [StructLayout(LayoutKind.Explicit)]
        public readonly struct Vector3
        {
            [FieldOffset(0x00)] public readonly float X;
            [FieldOffset(0x04)] public readonly float Y;
            [FieldOffset(0x08)] public readonly float Z;

            public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
        }

        /// <summary>
        /// EFT.MongoID Struct
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Pack = 8)]
        public readonly struct MongoID
        {
            [FieldOffset(0x0)]
            private readonly uint _timeStamp;
            [FieldOffset(0x8)]
            private readonly ulong _counter;
            [FieldOffset(0x10)]
            private readonly ulong _stringID;

            public readonly ulong StringID => _stringID;
        }
    }

    // ── Arena Armband -> TeamID mapping ──────────────────────────────────────
    // Mirrors the reference example; values are stable template GUIDs.
    public enum ArmbandColorType
    {
        red     = 0,
        fuchsia = 1,
        yellow  = 2,
        green   = 3,
        azure   = 4,
        white   = 5,
        blue    = 6,
    }

    // ── EFT.ERaidMode ────────────────────────────────────────────────────────
    // Active match mode. Read from EFT.NetworkGame`1._raidMode at +0x11C
    // (the pre-IL2CPP layout had this on a separate NetworkGameData object;
    // Arena's current build inlines it on the NetworkGame parent). Cast the
    // int and use ToString() to get the name — same pattern silk uses for
    // every other enum. Values mirror the old arena-dma-radar SDK that this
    // codebase replaces.
    public enum ERaidMode
    {
        Online       = 0,
        Local        = 1,
        Coop         = 2,
        OverRun      = 3,
        TeamFight    = 4,
        LastHero     = 5,
        FinalRun     = 6,
        OneManArmy   = 7,
        Duel         = 8,
        ShootOut     = 9,
        ShootOutSolo = 10,
        ShootOutDuo  = 11,
        ShootOutTrio = 12,
        BlastGang    = 13,
        CheckPoint   = 14,
        Narrate      = 15,
    }
}
