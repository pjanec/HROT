using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Tests for TASK-DBG-004: watch expressions, pin-value snapshotting, and MarshalFromBytes (SC1-SC6).
/// </summary>
public sealed class WatchTests
{
    private static readonly Guid AssetIdA = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GraphId1 = new Guid("11111111-1111-1111-1111-111111111111");

    private static Entity E1 => new Entity(1, 0);

    // ---- Helpers ---------------------------------------------------------------

    private static BlueprintDebugSession MakeSession()
        => new BlueprintDebugSession(
            new BlueprintRegistry(),
            new StubSimulationView(),
            new MockTimeController());

    private static DebugMap MakeMap(Guid assetId, ulong structureHash)
        => new DebugMap
        {
            AssetId       = assetId,
            BlueprintId   = 1,
            StructureHash = structureHash,
            Entries       = Array.Empty<DebugMapEntry>(),
        };

    private sealed class StubSimulationView : ISimulationView
    {
        public uint  Tick => 0;
        public float Time => 0f;
        public ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged
            => throw new NotImplementedException();
        public T GetManagedComponentRO<T>(Entity e) where T : class
            => throw new NotImplementedException();
        public bool IsAlive(Entity e) => throw new NotImplementedException();
        public bool HasComponent<T>(Entity e) where T : unmanaged => throw new NotImplementedException();
        public bool HasManagedComponent<T>(Entity e) where T : class => throw new NotImplementedException();
        public ReadOnlySpan<T> ReadEvents<T>() where T : unmanaged => throw new NotImplementedException();
        public QueryBuilder Query() => throw new NotImplementedException();
        public System.Collections.Generic.IReadOnlyList<T> ReadManagedEvents<T>()
            => throw new NotImplementedException();
        public IEntityCommandBuffer GetCommandBuffer() => throw new NotImplementedException();
    }

    // NoInlining helper: measures only the OnPinValueChanged call, not construction.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CallOnPinValueChanged(BlueprintDebugSession session, Entity self, string pinId, int value)
        => ((IBlueprintProbeSink)session).OnPinValueChanged(self, pinId, value);

    // ---- SC1: zero-alloc no-listener path ------------------------------------

    /// <summary>
    /// OnPinValueChanged with a watch registered but no event listener must allocate 0 bytes.
    /// </summary>
    [Fact]
    public void AddWatch_OnPinValueChanged_NoListener_ZeroAllocation()
    {
        var session  = MakeSession();
        var pinId    = Guid.NewGuid();
        session.AddWatch(AssetIdA, GraphId1, pinId, "val", typeof(int));
        var pinIdStr = pinId.ToString("D");

        // Warm up to let JIT settle.
        for (int i = 0; i < 10; i++)
            CallOnPinValueChanged(session, E1, pinIdStr, 42);

        long before = GC.GetAllocatedBytesForCurrentThread();
        CallOnPinValueChanged(session, E1, pinIdStr, 42);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0L, after - before);
    }

    // ---- SC2: one-alloc with-listener path -----------------------------------

    /// <summary>
    /// OnPinValueChanged with a watch and an event listener must allocate managed memory
    /// (at least the byte[] from ToArray) and fire the event with correct data.
    /// </summary>
    [Fact]
    public void AddWatch_OnPinValueChanged_WithListener_AllocatesAndFiresEvent()
    {
        var session  = MakeSession();
        var pinId    = Guid.NewGuid();
        session.AddWatch(AssetIdA, GraphId1, pinId, "val", typeof(int));
        var pinIdStr = pinId.ToString("D");

        PinValueChanged? captured = null;
        ((IBlueprintDebugSession)session).OnPinValueChangedEvent += evt => captured = evt;

        // Warm up.
        for (int i = 0; i < 10; i++)
            CallOnPinValueChanged(session, E1, pinIdStr, 99);

        long before = GC.GetAllocatedBytesForCurrentThread();
        CallOnPinValueChanged(session, E1, pinIdStr, 42);
        long after = GC.GetAllocatedBytesForCurrentThread();

        // Allocation must be > 0 (the byte[] from ToArray() is on the heap).
        Assert.True(after - before > 0, "Expected managed allocation when listener is present.");
        Assert.NotNull(captured);
        Assert.Equal(pinIdStr, captured!.PinId);
        Assert.Equal(typeof(int), captured.ValueType);
    }

    // ---- SC3: Matrix4x4 WriteValue stores correct bytes ----------------------

    /// <summary>
    /// WriteValue with System.Numerics.Matrix4x4 (64 bytes) stores all bytes
    /// and updates HasEverBeenWritten / UpdateCount correctly.
    /// </summary>
    [Fact]
    public void Watch_WriteValue_Matrix4x4_StoresCorrectBytes()
    {
        var id    = new WatchId(1);
        var watch = new Watch(id, AssetIdA, GraphId1, Guid.NewGuid(), "matrix", typeof(Matrix4x4));

        var matrix = new Matrix4x4(
            1,  2,  3,  4,
            5,  6,  7,  8,
            9,  10, 11, 12,
            13, 14, 15, 16);

        watch.WriteValue(matrix, E1, 0u);

        Assert.Equal(64, watch.LastValueBytes.Length);
        Assert.True(watch.HasEverBeenWritten);
        Assert.Equal(1, watch.UpdateCount);
    }

    // ---- SC4: Oversized struct throws ----------------------------------------

    /// <summary>
    /// WriteValue with a struct larger than 64 bytes must throw InvalidOperationException.
    /// </summary>
    [Fact]
    public void Watch_WriteValue_OversizedStruct_ThrowsInvalidOperationException()
    {
        var id    = new WatchId(1);
        var watch = new Watch(id, AssetIdA, GraphId1, Guid.NewGuid(), "oversized", typeof(OversizedStruct));

        Assert.Throws<InvalidOperationException>(
            () => watch.WriteValue(new OversizedStruct(), E1, 0u));
    }

    // Dummy struct > 64 bytes.
    private struct OversizedStruct
    {
#pragma warning disable CS0169
        private long _a, _b, _c, _d, _e, _f, _g, _h, _i;  // 9 * 8 = 72 bytes
#pragma warning restore CS0169
    }

    // ---- SC5: MarshalFromBytes decodes int correctly -------------------------

    /// <summary>
    /// MarshalFromBytes(bytes, typeof(int)) must decode the integer value correctly.
    /// </summary>
    [Fact]
    public void MarshalFromBytes_Int_DecodeCorrectly()
    {
        var bytes  = BitConverter.GetBytes(12345);
        var result = BlueprintDebugSession.MarshalFromBytes(bytes, typeof(int));

        Assert.Equal(12345, (int)result!);
    }

    // ---- SC6: IsStale set on structure-hash mismatch -------------------------

    /// <summary>
    /// Registering a map with a different structure hash must mark watches for
    /// that asset as stale.
    /// </summary>
    [Fact]
    public void Watch_IsStale_SetOnHashMismatch()
    {
        var session = MakeSession();
        var pinId   = Guid.NewGuid();
        session.AddWatch(AssetIdA, GraphId1, pinId, "val", typeof(int));

        // Register map v1.
        session.RegisterDebugMap(MakeMap(AssetIdA, 0x1111));
        var watch = session.GetWatches()[0];
        Assert.False(watch.IsStale);

        // Register map v2 (hash mismatch).
        session.RegisterDebugMap(MakeMap(AssetIdA, 0x2222));

        Assert.True(watch.IsStale);
    }
}
