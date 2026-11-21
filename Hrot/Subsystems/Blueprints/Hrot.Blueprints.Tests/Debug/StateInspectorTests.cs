using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Tests for TASK-DBG-006: GetCurrentStateSnapshot and MarshalFromBytes round-trips (SC1-SC5).
/// </summary>
public sealed class StateInspectorTests
{
    private static readonly Guid AssetIdA = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GraphId1 = new Guid("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NodeId1  = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static Entity E1 => new Entity(1, 0);

    // ---- Helpers ---------------------------------------------------------------

    private static BlueprintDebugSession MakeSession()
        => new BlueprintDebugSession(
            new BlueprintRegistry(),
            new StubSimulationView(),
            new MockTimeController());

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
        public IReadOnlyList<T> ReadManagedEvents<T>() => throw new NotImplementedException();
        public IEntityCommandBuffer GetCommandBuffer() => throw new NotImplementedException();
    }

    // ---- SC1: snapshot when paused --------------------------------------------

    /// <summary>
    /// GetCurrentStateSnapshot must return a non-null snapshot with the paused entity
    /// when the session is paused at a breakpoint.
    /// </summary>
    [Fact]
    public void GetCurrentStateSnapshot_WhenPaused_ReturnsSnapshot()
    {
        var session = MakeSession();
        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);

        ((IBlueprintProbeSink)session).OnNodeEnter(E1, NodeId1.ToString("D"));

        Assert.True(session.IsPaused);
        var snapshot = session.GetCurrentStateSnapshot();
        Assert.NotNull(snapshot);
        Assert.Equal(E1, snapshot!.Self);
    }

    // ---- SC2: snapshot when not paused ----------------------------------------

    /// <summary>
    /// GetCurrentStateSnapshot must return null when the session is not paused.
    /// </summary>
    [Fact]
    public void GetCurrentStateSnapshot_WhenNotPaused_ReturnsNull()
    {
        var session = MakeSession();

        var snapshot = session.GetCurrentStateSnapshot();

        Assert.Null(snapshot);
    }

    // ---- SC3: MarshalFromBytes int --------------------------------------------

    /// <summary>
    /// MarshalFromBytes must decode a 4-byte buffer into the correct int value.
    /// </summary>
    [Fact]
    public void MarshalFromBytes_Int_RoundTrip()
    {
        var bytes  = BitConverter.GetBytes(42);
        var result = BlueprintDebugSession.MarshalFromBytes(bytes, typeof(int));
        Assert.Equal(42, (int)result!);
    }

    // ---- SC4: MarshalFromBytes float ------------------------------------------

    /// <summary>
    /// MarshalFromBytes must decode a 4-byte buffer into a float value within tolerance.
    /// </summary>
    [Fact]
    public void MarshalFromBytes_Float_RoundTrip()
    {
        var bytes  = BitConverter.GetBytes(3.14f);
        var result = BlueprintDebugSession.MarshalFromBytes(bytes, typeof(float));
        Assert.True(Math.Abs((float)result! - 3.14f) < 0.001f);
    }

    // ---- SC5: MarshalFromBytes unknown type returns byte[] --------------------

    /// <summary>
    /// MarshalFromBytes must return the raw byte[] unchanged when it cannot decode the bytes.
    ///
    /// <para>
    /// ⚠ <b>The reason changed with <c>S3</c>, and the assertion did not.</b> This used to hold because
    /// <c>DateTime</c> is <i>"not in the switch"</i>; the struct arm now decodes any unmanaged value
    /// type, so what keeps this red-able is the <b>exactness</b> bound — four bytes are not a
    /// <c>DateTime</c>'s eight. 📌 Left on <c>DateTime</c> deliberately: a type that <i>would</i> decode
    /// at the right length is a sharper witness for the bound than one that could never decode at all.
    /// </para>
    /// </summary>
    [Fact]
    public void MarshalFromBytes_UnknownType_ReturnsByteArray()
    {
        var bytes  = new byte[] { 1, 2, 3, 4 };
        var result = BlueprintDebugSession.MarshalFromBytes(bytes, typeof(DateTime));
        Assert.IsType<byte[]>(result);
    }
}
