using ArenaUtils = eft_dma_radar.Arena.Misc.Utils;

namespace eft_dma_radar.Arena.Unity
{
    internal static class UnityOffsets
    {
        // ── GameObject / Component (Unity 6000.3.6f1, EFT Arena) ───────────
        // Confirmed against EFT silk6 (same Unity 6000.3.6f1 build) IDA refs:
        //   GO_Components: lea [rsi+38h] in TransferComponents
        //   GO_Name:       lea [rsi+68h] in GameObject::Transfer
        //   Comp_ObjectClass: Object::SetCachedScriptingObject [rbx+28h]
        //   Comp_GameObject: lea [rcx+38h] in Component::Transfer
        // NOTE: Unity 6 has NO GameObject→ObjectClass slot; the GCHandle
        // back-ref lives on the Object base (Component side) at +0x28.
        public const uint GO_Components    = 0x38;
        public const uint GO_Name          = 0x68;
        public const uint Comp_ObjectClass = 0x28;
        public const uint Comp_GameObject  = 0x38;
        public static readonly uint[] ObjClass_ToNamePtr = [0x0, 0x10];
        /// <summary>
        /// 6-element pointer chain: C# object → MonoBehaviour → GameObject → Components → Transform → ObjectClass → TransformInternal.
        /// </summary>
        public static readonly uint[] TransformChain =
        [
            0x10,               // ObjectClass → MonoBehaviour
            Comp_GameObject,    // 0x38 — Component → GameObject
            GO_Components,      // 0x38 — GameObject → ComponentArray
            0x08,               // First component entry ptr (Transform), stride 0x10
            Comp_ObjectClass,   // 0x28 — Transform → IL2CPP GCHandle / ObjectClass
            0x10,               // ObjectClass → TransformInternal
        ];
        public const uint GomFallback = 0x21A4450;
        public const uint AllCameras  = 0x19F3080;
        public static class ObjectClass { public const uint MonoBehaviourOffset = 0x10; }
        public static class Camera
        {
            // Arena Unity 6000.3.6.1f fallbacks (sig-scan still runs first):
            //   ViewMatrix: Camera::GetWorldToCameraMatrix body -> `lea rax, [rcx+88h]`
            //   FOV:        Camera_CUSTOM_GetGateFittedFieldOfView -> 2nd movss `[rcx+188h]`
            public static uint ViewMatrix = 0x88;
            public static uint FOV = 0x188;
            public static uint AspectRatio = 0x4F8;
            public const uint DerefIsAddedOffset = 0x35;
        }
        public static class List { public const uint ArrOffset = 0x10; public const uint ArrStartOffset = 0x20; }
        public static class ManagedList { public const uint ItemsPtr = 0x10; public const uint Count = 0x18; }
        public static class ManagedArray { public const uint FirstElement = 0x20; public const int ElementSize = 0x8; }
        public static class MongoID { public const uint TimeStamp = 0x00; public const uint Counter = 0x08; public const uint StringID = 0x10; }
        public static class IL2CPPHashSet { public const uint Entries = 0x18; public const uint Count = 0x1C; public const int EntrySize = 0x20; public const uint EntryValueOffset = 0x08; }
        public static class TransformAccess
        {
            // IDA: Arena UnityPlayer.dll — sub_1800CF5C0 (unnamed GetLocalPosition equivalent)
            //   ; rcx = TransformAccess* embedded inside TransformInternal
            //   mov  eax, [rcx+8]    → eax = m_Index  (int32 index into hierarchy arrays)
            //   mov  rax, [rcx]      → rax = m_Hierarchy ptr (TransformHierarchy*)
            //
            // TransformAccess struct layout (IDA-confirmed for Arena):
            //   +0x0: m_Hierarchy  (TransformHierarchy*)
            //   +0x8: m_Index      (int32)
            //
            // The struct is embedded inside the managed TransformInternal object.
            // Live dumps across Arena_Prison players locate it at TransformInternal+0x58:
            //   +0x58 → valid heap pointer  (m_Hierarchy — real hierarchy)
            //   +0x60 → small positive int  (m_Index — e.g. 35, 95, 111, 119)
            // EFT silk6 puts the same struct at +0x40/+0x48; Arena's managed TransformInternal
            // has extra fields that push the embedded TransformAccess 0x18 bytes higher.
            /// <summary>TransformInternal + 0x58 → TransformAccess.m_Hierarchy ptr.
            /// IDA sub_1800CF5C0: mov rax,[rcx+0]; struct embedded at TI+0x58 per live dump.</summary>
            public const uint HierarchyOffset = 0x40;
            /// <summary>TransformInternal + 0x60 → TransformAccess.m_Index (int32).
            /// IDA sub_1800CF5C0: mov eax,[rcx+8]; 0x58+0x8=0x60 relative to TransformInternal.</summary>
            public const uint IndexOffset = 0x48;
        }
        public static class TransformHierarchy
        {
            // IDA: Arena UnityPlayer.dll — sub_1800CF5C0 (GetLocalPosition equivalent)
            //   ; rcx = TransformAccess* — [rcx+0]=hierarchy, [rcx+8]=index
            //   mov  eax, [rcx+8]      → eax = m_Index
            //   lea  r8,  [rax+rax*2]  → r8  = index*3
            //   mov  rax, [rcx]        → rax = m_Hierarchy ptr
            //   shl  r8,  4            → r8  = index*0x30  (stride = 48 bytes)
            //   add  r8,  [rax+50h]    → r8  = &vertices[index]  ← VerticesOffset = 0x50
            //
            // IDA: Arena UnityPlayer.dll — sub_1800CF460 (GetRotation equivalent)
            //   ; rcx = TransformAccess* — same layout
            //   mov  r9,  [rcx]        → r9  = m_Hierarchy ptr
            //   mov  r8d, [rcx+8]      → r8d = m_Index
            //   mov  r10, [r9+50h]     → r10 = vertices array ptr  ← VerticesOffset = 0x50 (confirmed again)
            //   mov  r11, [r9+0A0h]    → r11 = indices array ptr   ← IndicesOffset  = 0xA0 (IDA confirmed)
            //   ; loop: mov ecx,[r11+rcx*4] → parentIndices[index] (int[] stride confirmed)
            //   ;        test ecx,ecx / jns → terminates when parent index < 0 (root sentinel = -1)
            //
            // Cross-check (GetLocalPosition, add r8,[rax+??h]):
            //   own-game Unity 6000.3.6f1:  [rax+18h] → VerticesOffset = 0x18, IndicesOffset = 0x20
            //   EFT silk6 Unity 6000.3.6f1: [rax+88h] → VerticesOffset = 0x88, IndicesOffset = 0x70
            //   Arena Unity 6000.3.6f1:     [rax+50h] → VerticesOffset = 0x50, IndicesOffset = 0xA0
            // All three share the same algorithm; only the field displacements differ per binary.
            //
            // The hierarchy's cached world-TRS slot is intentionally not exposed: Unity does
            // not refresh it every frame for every actor, causing multi-second position freezes.
            // World positions are computed from the live TrsX[] via TrsX.ComputeWorldPosition().
            /// <summary>TransformHierarchy + 0x50 → TrsX[] vertices array (stride 0x30).
            /// IDA: sub_1800CF5C0 → add r8,[rax+50h]; sub_1800CF460 → mov r10,[r9+50h].</summary>
            public const uint VerticesOffset = 0xA0;
            /// <summary>TransformHierarchy + 0xA0 → int[] parent indices array.
            /// IDA: sub_1800CF460 → mov r11,[r9+0A0h]; loop mov ecx,[r11+rcx*4] confirms int[].</summary>
            public const uint IndicesOffset  = 0x80;
        }
        public static class UnityAnimator { public const uint Speed = 0x4B0; }
    }

    // TrsX entry stride = 0x30 (48 bytes).
    // IDA: Arena sub_1800CF5C0 — lea r8,[rax+rax*2]; shl r8,4 → index*0x30 (stride confirmed).
    //   movups xmm0, [r8]       → T at vertex+0x00 (Vector3, 12 bytes + 4-byte pad)
    //   (silk6 adds: movaps xmm1,[r8+10h] → Q at vertex+0x10; xmm2,[r8+20h] → S at vertex+0x20)
    // Using LayoutKind.Explicit with Size=0x30: Vector3(12)+pad(4)+Quaternion(16)+Vector3(12)+pad(4)=48.
    [StructLayout(LayoutKind.Explicit, Size = 0x30)]
    internal readonly struct TrsX
    {
        [FieldOffset(0x00)] public readonly Vector3 T;
        [FieldOffset(0x10)] public readonly Quaternion Q;
        [FieldOffset(0x20)] public readonly Vector3 S;

        internal static Vector3 ComputeWorldPosition(
            ReadOnlySpan<TrsX> vertices,
            ReadOnlySpan<int> parentIndices,
            int index,
            int maxIterations = 4096)
        {
            // Arena respawns can leave a cached TransformIndex that points past a freshly-
            // reallocated (smaller) hierarchy. Guard explicitly so we surface a sentinel
            // instead of throwing IndexOutOfRangeException that the caller would have to
            // catch as a first-chance exception on every realtime tick.
            if ((uint)index >= (uint)vertices.Length || (uint)index >= (uint)parentIndices.Length)
                return new Vector3(float.NaN, float.NaN, float.NaN);

            var pos = vertices[index].T;
            int parent = parentIndices[index];
            int iter = 0;
            int maxParent = Math.Min(vertices.Length, parentIndices.Length);
            while ((uint)parent < (uint)maxParent && iter++ < maxIterations)
            {
                ref readonly var p = ref vertices[parent];
                pos = Vector3.Transform(pos, p.Q);
                pos *= p.S;
                pos += p.T;
                parent = parentIndices[parent];
            }
            return pos;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal readonly struct LinkedListObject
    {
        public readonly ulong PreviousObjectLink;
        public readonly ulong NextObjectLink;
        public readonly ulong ThisObject;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct ComponentArray
    {
        public readonly ulong ArrayBase;
        public readonly ulong MemLabelId;
        public readonly ulong Size;
        public readonly ulong Capacity;

        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        internal readonly struct Entry
        {
            [FieldOffset(0x8)]
            public readonly ulong Component;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    internal readonly struct GameObject
    {
        // Unity 6000.3.6f1 has no GameObject→ObjectClass slot; the IL2CPP
        // scripting back-ref lives on the Component side (Comp_ObjectClass).
        [FieldOffset(0x08)]
        public readonly int InstanceID;
        [FieldOffset((int)UnityOffsets.GO_Components)]
        public readonly ComponentArray Components;
        [FieldOffset((int)UnityOffsets.GO_Name)]
        public readonly ulong NamePtr;
    }

    internal readonly struct GOM
    {
        private const int MaxWalkNodes = 100_000;

        public readonly ulong LastActiveNode;
        public readonly ulong ActiveNodes;

        private GOM(ulong lastActiveNode, ulong activeNodes)
        {
            LastActiveNode = lastActiveNode;
            ActiveNodes    = activeNodes;
        }

        private static readonly Dictionary<string, ulong> _nameCache = new();
        private static readonly Lock _cacheLock = new();

        public static void ClearCache() { lock (_cacheLock) _nameCache.Clear(); }

        private static ulong _cachedGomAddr;
        // 0 = unknown, 1 = (0x20,0x28), 2 = (0x18,0x20)
        private static int _cachedLayout;

        // GOM head layout differs across Arena builds — probe at runtime so the
        // walk uses the build's real LastActiveNode / ActiveNodes pair instead of
        // hard-coded offsets that may produce a structurally-valid-but-wrong list.
        public static GOM Get(ulong gomAddress)
        {
            if (!ArenaUtils.IsValidVirtualAddress(gomAddress))
                return default;

            int layout = _cachedLayout;
            if (layout == 1 && TryReadLayout(gomAddress, 0x20, 0x28, out var g1)) return g1;
            if (layout == 2 && TryReadLayout(gomAddress, 0x18, 0x20, out var g2)) return g2;

            if (TryReadLayout(gomAddress, 0x20, 0x28, out var probed1))
            {
                _cachedLayout = 1;
                return probed1;
            }
            if (TryReadLayout(gomAddress, 0x18, 0x20, out var probed2))
            {
                _cachedLayout = 2;
                return probed2;
            }
            return default;
        }

        private static bool TryReadLayout(ulong gomAddress, uint lastOff, uint activeOff, out GOM gom)
        {
            gom = default;
            if (!Memory.TryReadValue<ulong>(gomAddress + lastOff,   out var last,   false)) return false;
            if (!Memory.TryReadValue<ulong>(gomAddress + activeOff, out var active, false)) return false;
            if (!ArenaUtils.IsValidVirtualAddress(last) || !ArenaUtils.IsValidVirtualAddress(active))
                return false;
            // Sanity: ActiveNodes must be a LinkedListObject whose ThisObject is a valid VA.
            if (!Memory.TryReadValue<ulong>(active + 0x10, out var firstThis, false)) return false;
            if (!ArenaUtils.IsValidVirtualAddress(firstThis)) return false;
            gom = new GOM(last, active);
            return true;
        }

        private static readonly (string Sig, int RelOff, int InstrLen, string Desc)[] GomDirectSigs =
        [
            ("48 8B 0D ? ? ? ? 8B 41 ? 48 83 C0 ? ? ? ? ? ? ? ? 83 79", 3, 7, "mov [rip+rel32],rax (GOM init store)"),
        ];

        private static readonly (string Sig, int RelOff, int InstrLen, string Desc)[] GomCallSiteSigs =
        [
            ("E8 ? ? ? ? 4C 8D 45 ? 89 5D ? 48 8D 55", 1, 5, "call GomGetter (variant 1)"),
            ("E8 ? ? ? ? 8B 48 ? ? ? ? ? ? ? ? 48 8D 77", 1, 5, "call GomGetter (variant 2)"),
            ("E8 ? ? ? ? 48 8B 58 ? 48 8D 78 ? 48 3B DF 74 ? ? ? ? 48 8B 53", 1, 5, "call GomGetter (variant 3)"),
            ("E8 ? ? ? ? 8B 48 ? ? ? ? ? ? ? ? 48 8D 6F", 1, 5, "call GomGetter (variant 4)"),
            ("E8 ? ? ? ? 48 8B 58 ? 48 8D 78 ? 48 3B DF 74 ? 66 66 66 0F 1F 84 00", 1, 5, "call GomGetter (variant 5)"),
            ("E8 ? ? ? ? 4C 8D 44 24 ? C7 44 24 ? ? ? ? ? 48 8D 54 24 ? 48 8B C8", 1, 5, "call GomGetter (variant 6)"),
            ("E8 ? ? ? ? 4C 8D 44 24 ? 89 5C 24 ? 48 8D 54 24", 1, 5, "call GomGetter (variant 7)"),
        ];

        private static readonly (string Sig, int RelOff, int InstrLen, string Desc)[] GomBroadSigs =
        [
            ("48 89 05 ? ? ? ? 48 83 C8", 3, 7, "mov [rip+rel32],rax; add rsp (broad)"),
        ];

        private const int BroadSigMaxMatches = 256;

        public static ulong GetAddr(ulong unityBase)
        {
            if (ArenaUtils.IsValidVirtualAddress(_cachedGomAddr))
                return _cachedGomAddr;

            foreach (var (sig, relOff, instrLen, desc) in GomDirectSigs)
            {
                try
                {
                    ulong addr = Memory.FindSignature(sig, "UnityPlayer.dll");
                    if (!ArenaUtils.IsValidVirtualAddress(addr)) continue;
                    int rva = Memory.ReadValue<int>(addr + (ulong)relOff, false);
                    ulong ptr = Memory.ReadPtr(addr + (ulong)instrLen + (ulong)rva, false);
                    if (ArenaUtils.IsValidVirtualAddress(ptr))
                    {
                        Log.WriteLine($"[GOM] Located via direct sig: {desc}");
                        _cachedGomAddr = ptr;
                        return ptr;
                    }
                }
                catch { }
            }

            foreach (var (sig, relOff, instrLen, desc) in GomCallSiteSigs)
            {
                try
                {
                    ulong callAddr = Memory.FindSignature(sig, "UnityPlayer.dll");
                    if (!ArenaUtils.IsValidVirtualAddress(callAddr)) continue;
                    int callRel = Memory.ReadValue<int>(callAddr + (ulong)relOff, false);
                    ulong targetFunc = callAddr + (ulong)instrLen + (ulong)callRel;
                    if (!ArenaUtils.IsValidVirtualAddress(targetFunc)) continue;
                    if (TryResolveGetterGlobal(targetFunc, out var globalPtr))
                    {
                        Log.WriteLine($"[GOM] Located via call-site sig: {desc}");
                        _cachedGomAddr = globalPtr;
                        return globalPtr;
                    }
                }
                catch { }
            }

            foreach (var (sig, relOff, instrLen, desc) in GomBroadSigs)
            {
                try
                {
                    var matches = Memory.FindSignatures(sig, "UnityPlayer.dll", BroadSigMaxMatches);
                    foreach (var addr in matches)
                    {
                        if (!ArenaUtils.IsValidVirtualAddress(addr)) continue;
                        int rva = Memory.ReadValue<int>(addr + (ulong)relOff, false);
                        ulong ptr = addr + (ulong)instrLen + (ulong)rva;
                        if (!Memory.TryReadPtr(ptr, out var gomAddr, false)) continue;
                        if (IsValidGomPtr(gomAddr))
                        {
                            Log.WriteLine($"[GOM] Located via broad sig: {desc} (matched {matches.Length} sites)");
                            _cachedGomAddr = gomAddr;
                            return gomAddr;
                        }
                    }
                }
                catch { }
            }

            try
            {
                ulong fallback = Memory.ReadPtr(unityBase + UnityOffsets.GomFallback, false);
                if (ArenaUtils.IsValidVirtualAddress(fallback))
                {
                    Log.WriteLine("[GOM] Located via hardcoded offset");
                    _cachedGomAddr = fallback;
                    return fallback;
                }
            }
            catch { }

            throw new InvalidOperationException("Failed to locate GameObjectManager");
        }

        private static bool TryResolveGetterGlobal(ulong funcAddr, out ulong result)
        {
            result = 0;
            Span<byte> header = stackalloc byte[7];
            if (!Memory.TryReadBuffer(funcAddr, header, false)) return false;
            if (header[0] != 0x48 || header[1] != 0x8B || header[2] != 0x05) return false;
            int innerRel = BitConverter.ToInt32(header[3..]);
            ulong globalAddr = funcAddr + 7 + (ulong)innerRel;
            if (!Memory.TryReadPtr(globalAddr, out result, false)) return false;
            return ArenaUtils.IsValidVirtualAddress(result);
        }

        private static bool IsValidGomPtr(ulong ptr)
        {
            if (!ArenaUtils.IsValidVirtualAddress(ptr))
                return false;

            // Try Silk standard offsets first (0x20, 0x28)
            if (Memory.TryReadValue<ulong>(ptr + 0x20, out var lastActive, false) &&
                Memory.TryReadValue<ulong>(ptr + 0x28, out var activeNodes, false) &&
                ArenaUtils.IsValidVirtualAddress(lastActive) &&
                ArenaUtils.IsValidVirtualAddress(activeNodes) &&
                Memory.TryReadValue<LinkedListObject>(activeNodes, out var firstNode, false) &&
                ArenaUtils.IsValidVirtualAddress(firstNode.ThisObject))
            {
                return true;
            }

            // Try alternate offsets (0x18, 0x20)
            if (Memory.TryReadValue<ulong>(ptr + 0x18, out var altLastActive, false) &&
                Memory.TryReadValue<ulong>(ptr + 0x20, out var altActiveNodes, false) &&
                ArenaUtils.IsValidVirtualAddress(altLastActive) &&
                ArenaUtils.IsValidVirtualAddress(altActiveNodes) &&
                Memory.TryReadValue<LinkedListObject>(altActiveNodes, out var altFirstNode, false) &&
                ArenaUtils.IsValidVirtualAddress(altFirstNode.ThisObject))
            {
                return true;
            }

            return false;
        }

        internal static void ResetCachedAddresses()
        {
            _cachedGomAddr = 0;
            _cachedLayout  = 0;
            ClearCache();
        }

        public static ulong GetGameObjectByName(string name, bool ignoreCase = true, bool useCache = true, bool logCount = false)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            if (useCache)
            {
                lock (_cacheLock)
                {
                    if (_nameCache.TryGetValue(name, out var cached) && ArenaUtils.IsValidVirtualAddress(cached))
                        return cached;
                }
            }
            var gom = Get(Memory.GOM);
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!Memory.TryReadValue<LinkedListObject>(gom.ActiveNodes, out var first, false)) return 0;
            if (!Memory.TryReadValue<LinkedListObject>(gom.LastActiveNode, out var last, false)) return 0;
            ulong result = WalkList(first, last, forward: true,
                (node) => MatchName(node.ThisObject, name, comparison) ? node.ThisObject : 0);
            if (result == 0)
                result = WalkList(last, first, forward: false,
                    (node) => MatchName(node.ThisObject, name, comparison) ? node.ThisObject : 0);
            if (ArenaUtils.IsValidVirtualAddress(result) && useCache)
            {
                lock (_cacheLock)
                    _nameCache[name] = result;
            }
            return result;
        }

        public static ulong FindBehaviourByKlassPtr(ulong klassPtr)
        {
            if (!ArenaUtils.IsValidVirtualAddress(klassPtr)) return 0;
            var gom = Get(Memory.GOM);
            if (!Memory.TryReadValue<LinkedListObject>(gom.ActiveNodes, out var first, true)) return 0;
            if (!Memory.TryReadValue<LinkedListObject>(gom.LastActiveNode, out var last, true)) return 0;
            ulong result = WalkList(first, last, forward: true,
                node => GetComponentByKlassPtr(node.ThisObject, klassPtr), useCache: true);
            if (result == 0)
                result = WalkList(last, first, forward: false,
                    node => GetComponentByKlassPtr(node.ThisObject, klassPtr), useCache: true);
            return result;
        }

        public static ulong FindBehaviourByClassName(string className, bool logCount = false)
        {
            if (string.IsNullOrEmpty(className)) return 0;
            var gom = Get(Memory.GOM);
            if (!Memory.TryReadValue<LinkedListObject>(gom.ActiveNodes, out var first, true)) return 0;
            if (!Memory.TryReadValue<LinkedListObject>(gom.LastActiveNode, out var last, true)) return 0;
            ulong result = WalkList(first, last, forward: true,
                node => GetComponentByClassName(node.ThisObject, className), useCache: true);
            if (result == 0)
                result = WalkList(last, first, forward: false,
                    node => GetComponentByClassName(node.ThisObject, className), useCache: true);
            return result;
        }

        private static ulong GetComponentByKlassPtr(ulong gameObject, ulong klassPtr)
        {
            if (!Memory.TryReadValue<GameObject>(gameObject, out var go, true)) return 0;
            ref readonly var compArr = ref go.Components;
            if (!ArenaUtils.IsValidVirtualAddress(compArr.ArrayBase) || compArr.Size == 0) return 0;
            int count = (int)Math.Min(compArr.Size, 0x400);
            Span<ComponentArray.Entry> entries = count <= 64
                ? stackalloc ComponentArray.Entry[count]
                : new ComponentArray.Entry[count];
            if (!Memory.TryReadBuffer(compArr.ArrayBase, entries, true)) return 0;
            for (int i = 0; i < count; i++)
            {
                var compPtr = entries[i].Component;
                if (!ArenaUtils.IsValidVirtualAddress(compPtr)) continue;
                if (!Memory.TryReadPtr(compPtr + UnityOffsets.Comp_ObjectClass, out var objectClass, true)
                    || !ArenaUtils.IsValidVirtualAddress(objectClass)) continue;
                if (!Memory.TryReadPtr(objectClass, out var klass, true)) continue;
                if (klass == klassPtr) return objectClass;
            }
            return 0;
        }

        private static ulong GetComponentByClassName(ulong gameObject, string className)
        {
            if (!Memory.TryReadValue<GameObject>(gameObject, out var go, true)) return 0;
            ref readonly var compArr = ref go.Components;
            if (!ArenaUtils.IsValidVirtualAddress(compArr.ArrayBase) || compArr.Size == 0) return 0;
            int count = (int)Math.Min(compArr.Size, 0x400);
            Span<ComponentArray.Entry> entries = count <= 64
                ? stackalloc ComponentArray.Entry[count]
                : new ComponentArray.Entry[count];
            if (!Memory.TryReadBuffer(compArr.ArrayBase, entries, true)) return 0;
            for (int i = 0; i < count; i++)
            {
                var compPtr = entries[i].Component;
                if (!ArenaUtils.IsValidVirtualAddress(compPtr)) continue;
                if (!Memory.TryReadPtr(compPtr + UnityOffsets.Comp_ObjectClass, out var objectClass, true)
                    || !ArenaUtils.IsValidVirtualAddress(objectClass)) continue;
                var name = Il2CppClass.ReadName(objectClass, useCache: true);
                if (name is not null && name.Equals(className, StringComparison.Ordinal))
                    return objectClass;
            }
            return 0;
        }

        public static ulong GetComponentFromBehaviour(ulong behaviour, string className)
        {
            if (!ArenaUtils.IsValidVirtualAddress(behaviour)) return 0;
            if (!Memory.TryReadPtr(behaviour + UnityOffsets.Comp_GameObject, out var gameObject, false)
                || !ArenaUtils.IsValidVirtualAddress(gameObject)) return 0;
            return GetComponentByClassName(gameObject, className);
        }

        /// <summary>
        /// DEBUG: Dumps all GameObjects found in the GOM for debugging purposes.
        /// </summary>
        public static void DebugDumpAllGameObjects()
        {
            // Intentionally empty — kept as a stub for ad-hoc debugging. Previous implementation
            // was removed because the verbose dumps were no longer needed in normal operation.
        }

        private static ulong WalkList(
            LinkedListObject start,
            LinkedListObject end,
            bool forward,
            Func<LinkedListObject, ulong> visitor,
            bool useCache = false)
        {
            var current = start;
            for (int i = 0; i < MaxWalkNodes; i++)
            {
                if (!ArenaUtils.IsValidVirtualAddress(current.ThisObject)) break;
                var hit = visitor(current);
                if (ArenaUtils.IsValidVirtualAddress(hit)) return hit;
                if (current.ThisObject == end.ThisObject) break;
                var nextLink = forward ? current.NextObjectLink : current.PreviousObjectLink;
                if (!Memory.TryReadValue<LinkedListObject>(nextLink, out current, useCache)) break;
            }
            return 0;
        }

        private static bool MatchName(ulong gameObject, string name, StringComparison comparison)
        {
            if (!Memory.TryReadValue<ulong>(gameObject + UnityOffsets.GO_Name, out var namePtr, false)) return false;
            if (!ArenaUtils.IsValidVirtualAddress(namePtr)) return false;
            return Memory.TryReadString(namePtr, out var goName, 64, false)
                && goName is not null
                && goName.Contains(name, comparison);
        }
    }

    internal static class Il2CppClass
    {
        public static string? ReadName(ulong objectClass, int maxLength = 64, bool useCache = false)
        {
            if (!Memory.TryReadPtrChain(objectClass, UnityOffsets.ObjClass_ToNamePtr, out ulong namePtr, useCache))
                return null;
            if (!ArenaUtils.IsValidVirtualAddress(namePtr)) return null;
            return Memory.TryReadString(namePtr, out var name, maxLength, useCache) ? name : null;
        }
    }
}
