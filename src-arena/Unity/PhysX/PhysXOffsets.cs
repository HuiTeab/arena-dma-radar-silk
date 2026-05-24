namespace eft_dma_radar.Arena.Unity.PhysX
{
    /// <summary>
    /// Field offsets inside PhysX 4.1 / Unity 6 objects.
    /// <para>
    /// All values here are <b>struct-relative</b>, not module-relative. They are
    /// stable as long as the PhysX SDK version stays at 4.1.x — Unity engine
    /// patches that recompile <c>UnityPlayer.dll</c> shift code addresses
    /// (handled by the <see cref="PhysXProbe"/> sig-scan) but the in-struct
    /// layout is owned by NVIDIA's SDK and does not move.
    /// </para>
    /// </summary>
    internal static class PhysXOffsets
    {
        // ── SDK entry point ─────────────────────────────────────────────────
        // RVA inside UnityPlayer.dll where the NpPhysics singleton pointer is
        // stored. Found by PhysXProbe's sig-scan; re-verified every attach.
        public const uint PhysXSdkRva = 0x01EE1028; // ✅ arena (Unity 6000.3.6)

        // ── NpPhysics layout ───────────────────────────────────────────────
        // The SDK singleton owns a scene array. We only need the first two
        // fields to enumerate scenes.
        public const uint NpPhysics_SceneArrayData = 0x08; // ✅ ptr to NpScene*[N]
        public const uint NpPhysics_SceneArraySize = 0x10; // ✅ uint32 — confirmed 2..3 on Arena

        // ── NpScene layout ─────────────────────────────────────────────────
        // Each NpScene owns a rigid-actor array. The array contains both
        // RigidStatic and RigidDynamic; the actor kind is discriminated by the
        // PxBase concrete type at +0x8 of the actor.
        public const uint NpScene_RigidActorsData = 0x23C8; // ✅ ptr to PxActor*[N]
        public const uint NpScene_RigidActorsSize = 0x23D0; // ✅ uint32 — confirmed = 7842..10732 on Arena

        // ── PxBase header (every PhysX object starts with this) ─────────────
        // First 16 bytes of any PxRigidActor / NpShape / mesh / heightfield.
        public const uint PxBase_ConcreteType = 0x08; // ✅ PxConcreteType (u16)

        // ── PxRigidActor (NpRigidStatic / NpRigidDynamic share this prefix) ──
        // Some EFT/Unity builds split shape-manager location across two offsets;
        // SceneCache walks all three and uses whichever yields a sane shape count.
        public const uint PxRigidActor_ShapeManager        = 0x28; // ✅ NpShapeManager — arena

        // ── NpShapeManager (a ptr_table_t: small inline + heap overflow) ────
        // count == 0 ⇒ no shapes
        // count == 1 ⇒ "ShapesSingle" holds the shape pointer directly
        // count >  1 ⇒ "ShapesSingle" is a pointer to a ptr-array of length count
        public const uint NpShapeManager_ShapesSingle = 0x00; // ✅ NpShape* or NpShape*[]
        public const uint NpShapeManager_ShapesCount  = 0x08; // ✅ uint16

        // ── NpShape (Sc::ShapeCore embedded at +0x50) ───────────────────────
        // Layout fully verified by IDA disassembly of:
        //   • NpShape::setSimulationFilterData   — writes at this+0x60
        //   • NpShape::setQueryFilterData        — writes at this+0x50
        //   • NpShape::setFlag                   — direct path reads at this+0x90
        //   • Sc::ShapeCore::ShapeCore           — full canonical field layout
        // The NpShape header occupies +0x00..+0x4F (vtable / actor ptr /
        // buffered-write bookkeeping). The embedded Sc::ShapeCore starts at
        // +0x50; field reads compose NpShape_PxShapeCoreOffset + PxShapeCore_*.
        // ShapeFlags is the one exception — read as an absolute offset because
        // the trigger / scene-query flag check runs in the hot per-actor loop.
        public const uint NpShape_PxShapeCoreOffset = 0x50; // ✅ inline Sc::ShapeCore
        public const uint NpShape_ShapeFlags        = 0x90; // ✅ = PxShapeCoreOffset + 0x40

        // ── Sc::ShapeCore (a.k.a. PxShapeCore) — offsets relative to its start ─
        // From the Sc::ShapeCore::ShapeCore constructor IDA decompile:
        //   +0x00..+0x0F : queryFilterData       (4 × u32 — initialised by setQueryFilterData)
        //   +0x10..+0x1F : simulationFilterData  (4 × u32 — initialised by setSimulationFilterData)
        //   +0x20..+0x3B : transform             (PxTransform = quat(4f) + Vec3(3f) = 28 bytes;
        //                                         constructor writes 1.0f at +0x2C giving qw=1.0)
        //   +0x3C..+0x3F : contactOffset         (float — initialised to scale × 0.02)
        //   +0x40        : shapeFlags            (PxShapeFlag byte — eSIM/eSQ/eTRIGGER/eVis)
        //   +0x41        : mOwnsMaterialIndices  (byte)
        //   +0x42..+0x43 : materialIndex         (u16)
        //   +0x48..+0x87 : PxGeometryUnion       (64 bytes — type tag (i32) first, then data)
        // We only read the three fields the visibility filter needs at runtime;
        // the rest of the canonical layout is documented in the comment above
        // for future work (Phase 2 / 3 readers).
        public const uint PxShapeCore_QueryFilterData = 0x00; // ✅
        public const uint PxShapeCore_LocalPose       = 0x20; // ✅ PxTransform (28 bytes — q,p)
        public const uint PxShapeCore_Geometry        = 0x48; // ✅ PxGeometryUnion (64 bytes)

        // ── NpRigidDynamic-specific: cached buffered body-to-world transform ─
        // The "live" pose of a dynamic actor is its bufferedBody2World, written
        // by PhysX once per simulation step. Phase 2 work will read this for
        // moving colliders (doors, vehicles); Phase 1 only handles statics.
        public const uint NpRigidDynamic_BufferedBody2World = 0x140; // 🟡 PxTransform

        // ── NpRigidStatic-specific ─────────────────────────────────────────
        // ✅ Verified by live hex dump: RigidStatic actor is 0xB0 bytes total,
        // three back-to-back actors all carry their world transform at +0x90
        // within the struct (quaternion magnitude check passes, position matches
        // a sane Arena world coordinate).
        public const uint NpRigidStatic_BodyToWorld = 0x90; // ✅ PxTransform — single static pose

        // ── PxTriangleMesh (the cooked mesh referenced by triangle-mesh geom) ─
        // The mesh holds vertices (always Vec3 float) and triangle indices
        // (either u16 or u32 depending on PxTriangleMeshFlag.Has16BitIndices).
        public const uint TriangleMesh_NbVertices     = 0x20; // ✅ u32
        public const uint TriangleMesh_NbTriangles    = 0x24; // ✅ u32
        public const uint TriangleMesh_Vertices       = 0x28; // ✅ Vec3* — packed contiguous
        public const uint TriangleMesh_Triangles      = 0x30; // ✅ u16*/u32* — 3 per triangle
        public const uint TriangleMesh_LocalBoundsMin = 0x38; // ✅ Vec3 — for fast AABB cull
        public const uint TriangleMesh_LocalBoundsMax = 0x44; // ✅ Vec3
        public const uint TriangleMesh_Flags          = 0x5C; // ✅ u8 (PxTriangleMeshFlag)

        // ── PxHeightField (cooked heightmap) ────────────────────────────────
        // Samples are int16 heights on a 2D grid. World space:
        //   localX = column * columnScale
        //   localZ = row    * rowScale
        //   localY = sample * heightScale
        // Arena maps are all triangle-mesh; these offsets are exercised only
        // when the heightfield path is touched (not yet live-tested in Arena).
        public const uint HeightField_Rows    = 0x38; // 🟡 u32
        public const uint HeightField_Columns = 0x3C; // 🟡 u32
        public const uint HeightField_Samples = 0x50; // 🟡 PxHfSample* (i16 + 2 bytes flags each)

        // ── PxGeometry header (first int of any geometry struct) ────────────
        // Each geometry kind below starts with this 4-byte type tag.
        public const uint PxGeometry_TypeTag = 0x00; // ✅ PxGeometryType (i32)

        // ── PxSphereGeometry / PxCapsuleGeometry / PxBoxGeometry ────────────
        public const uint Sphere_Radius      = 0x04; // ✅ float
        public const uint Capsule_Radius     = 0x04; // ✅ float
        public const uint Capsule_HalfHeight = 0x08; // ✅ float
        public const uint Box_HalfExtents    = 0x04; // ✅ Vec3

        // ── PxTriangleMeshGeometry / PxHeightFieldGeometry / PxConvexMeshGeometry ──
        // We only need the pointer to the cooked mesh + the per-axis scales.
        // PxMeshScale (Vec3 scale + Quat rot, 28 bytes) at TriMeshGeom +0x04
        // is not consumed by the radar — Arena maps don't ship per-instance
        // mesh scales — so reading it would just complicate the ingest.
        public const uint TriangleMeshGeom_MeshPtr       = 0x28; // ✅ PxTriangleMesh*
        // PxConvexMeshGeometry's field order differs from PxTriangleMeshGeometry:
        // in ConvexMesh the pointer comes BEFORE the flags (so it sits right
        // after the 28-byte PxMeshScale + 4-byte type prefix = +0x20). In
        // TriangleMesh the flags come first and push the pointer to +0x28.
        // Confirmed from live ConvexGeomUnion dumps on arena 0.5.0.3.45073 —
        // all three sampled actors had a clean 8-byte-aligned heap pointer
        // at +0x20 and the (wrong) +0x28 read gave a low-bit-set Frankenstein
        // value (low 32 bits of flags joined with high 32 bits of the next field).
        public const uint ConvexMeshGeom_MeshPtr         = 0x20; // ✅ PxConvexMesh* (verified live)
        public const uint HeightFieldGeom_HeightFieldPtr = 0x08; // 🟡 PxHeightField*
        public const uint HeightFieldGeom_HeightScale    = 0x10; // 🟡 float

        // ── PxConvexMesh (the cooked convex hull referenced by ConvexMesh geom) ─
        // ⚠ These offsets are BEST-GUESS by structural analogy with our
        // verified PxTriangleMesh layout (data starts at +0x20 inside the SDK
        // object). They have NOT been verified against arena's binary yet —
        // SceneCache logs a hex dump of the first ConvexMesh actor whose
        // counts fail validation so we can correct these from concrete data
        // on the next pass.
        //
        // The underlying layout in PhysX 4.1.2 is Gu::ConvexHullData:
        //   +0x00  PxBounds3 mAABB         (24 bytes: Vec3 min + Vec3 max)
        //   +0x18  PxVec3    mCenterOfMass (12 bytes)
        //   +0x24  pad to 8-byte align
        //   +0x28  HullPolygonData* mPolygons
        //   +0x30  PxU8* mBigConvexRawData (only for hulls with > 64 verts)
        //   +0x38  PxU8* mVertexData8     (index buffer)
        //   +0x40  PxU8* mFacesByEdges8
        //   +0x48  PxU8* mFacesByVertices8
        //   +0x50  PxVec3* mHullVertices  (vertex array)
        //   +0x58  PxU16 mNbEdges
        //   +0x5A  PxU8  mNbHullVertices
        //   +0x5B  PxU8  mNbPolygons
        // Offsets below assume the same +0x20 prefix as PxTriangleMesh.
        public const uint ConvexMesh_AabbMin       = 0x20; // 🟡 Vec3
        public const uint ConvexMesh_AabbMax       = 0x2C; // 🟡 Vec3
        public const uint ConvexMesh_Polygons      = 0x48; // 🟡 HullPolygonData*
        public const uint ConvexMesh_Vertices      = 0x70; // 🟡 PxVec3*
        public const uint ConvexMesh_NbVertices    = 0x7A; // 🟡 u8
        public const uint ConvexMesh_NbPolygons    = 0x7B; // 🟡 u8

        // HullPolygonData layout (per-polygon descriptor referenced by mPolygons):
        //   +0x00  PxPlane mPlane (Vec3 normal + float d) = 16 bytes
        //   +0x10  PxU16   mVRef8
        //   +0x12  PxU8    mNbVerts
        //   +0x13  PxU8    mMinIndex
        // Total: 20 bytes per polygon descriptor.
        public const uint HullPolygonData_Plane    = 0x00; // PxPlane = Vec4
        public const uint HullPolygonData_Stride   = 0x14; // 🟡 20 bytes per descriptor — verify alignment on real hull
        public const uint HeightFieldGeom_RowScale       = 0x14; // 🟡 float
        public const uint HeightFieldGeom_ColumnScale    = 0x18; // 🟡 float

        // ── PxFilterData ────────────────────────────────────────────────────
        // Four u32s. Unity packs the layer index into word1 as a one-hot bit
        // (`1 << layerIndex`); word0 is the collision-group mask. word2 / word3
        // are unused by the radar (the SHAPE-COMPARE diagnostic showed word3 is
        // uniformly 0x0000FFFF and word2 is zero).
        public const uint FilterData_Word0 = 0x00; // ✅ group bitmask
        public const uint FilterData_Word1 = 0x04; // ✅ Unity layer one-hot

        // Unity's std::string variant (libc++-style short string optimization):
        //   bytes 0..15  : either heap data pointer (long mode) OR start of in-place SSO buffer
        //   bytes 16..23 : size_t length (long mode only)
        //   byte  31     : SSO discriminator (the high byte of the struct)
        //     • value >= 0x40  ⇒  long mode: read data ptr at +0, length at +16
        //     • value <  0x40  ⇒  SSO mode:  length = 31 - flag, data starts at +0
        public const uint StdString_DataOrSsoBuf = 0x00;
        public const uint StdString_Length       = 0x10;
        public const uint StdString_SsoFlag      = 0x1F;

        // The layer offset isn't documented in Unity.cs. We probe several
        // candidates at build time (SceneCache layer-offset probe) and pick
        // the one whose value matches log2(ShapeLayerMask) most often.
        public const uint NpShape_NativeCollider     = 0x10;
        public const uint NativeCollider_GameObject  = 0x38;
        public const uint NativeGameObject_NamePtr   = 0x68;
        // Layer offset confirmed by SceneCache probe (256/256 samples at +0x58
        // on arena 0.5.0.3.45073 / Unity 6000.3.6f1). The probe still runs on
        // every build so a future patch's shift is caught immediately — just
        // change this constant to whatever the new winner is.
        public const uint NativeGameObject_Layer     = 0x58;
    }
}
