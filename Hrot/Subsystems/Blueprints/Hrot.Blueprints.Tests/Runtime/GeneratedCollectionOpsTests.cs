using Fdp.Core;
using Hrot.AI.Behaviors;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// FC-1b end-to-end -- the GENERATED ops class (<c>BpGenListDemoItemsOps</c>, emitted by
/// <c>CollectionOpsGenerator</c> from the single <c>[BlueprintCollectionField]</c> attribute on
/// <see cref="BpGenListDemo.Items"/>) must pass the SAME gates as the hand-written FC-0 reference
/// (<c>FixedCollectionOpsTests</c>): the InlineArray write round-trip through a
/// <c>GetComponentRW</c> ref, the G6 tail-always-default invariant, the overflow/bounds contract,
/// and the F2 garbage-Count clamp -- plus the editor-side proof that discovery cannot tell
/// generated from hand-written (read pair via <c>TryReflectCollections</c>, write set via
/// <c>TryReflectWriteAccessors</c>, gate 1 via <c>[BlueprintWritable]</c>).
/// </summary>
public sealed class GeneratedCollectionOpsTests : IDisposable
{
    private readonly EntityRepository _repo;
    private readonly Entity _entity;

    public GeneratedCollectionOpsTests()
    {
        _repo = new EntityRepository();
        _repo.RegisterComponent<BpGenListDemo>();
        _entity = _repo.CreateEntity();
        _repo.AddComponent(_entity, new BpGenListDemo());
    }

    public void Dispose() => _repo.Dispose();

    private ref BpGenListDemo Rw() => ref _repo.GetComponentRW<BpGenListDemo>(_entity);
    private BpGenListDemo ReRead() => _repo.GetComponentRO<BpGenListDemo>(_entity);

    private int RawSlot(int i)
    {
        var c = ReRead();
        ReadOnlySpan<int> s = c.Items;
        return s[i];
    }

    // ---- the FC-0 round-trip gate, against generated code -------------------

    [Fact]
    public void GeneratedOps_WriteThroughRwRef_RoundTrips()
    {
        ref var c = ref Rw();
        Assert.True(BpGenListDemoItemsOps.Add(ref c, 10));
        Assert.True(BpGenListDemoItemsOps.Add(ref c, 20));
        Assert.True(BpGenListDemoItemsOps.InsertAt(ref c, 1, 15));
        Assert.True(BpGenListDemoItemsOps.SetAt(ref c, 0, 5));

        var read = ReRead();
        Assert.Equal(3, BpGenListDemoItemsOps.Count(in read));
        Assert.Equal(5,  BpGenListDemoItemsOps.Item(in read, 0));
        Assert.Equal(15, BpGenListDemoItemsOps.Item(in read, 1));
        Assert.Equal(20, BpGenListDemoItemsOps.Item(in read, 2));
    }

    [Fact]
    public void GeneratedOps_G6Invariant_RemoveClearResizeZeroVacatedSlots()
    {
        ref var c = ref Rw();
        for (int i = 0; i < BpGenListDemo.Capacity; i++) BpGenListDemoItemsOps.Add(ref c, 100 + i);

        Assert.True(BpGenListDemoItemsOps.RemoveAt(ref c, 0));
        Assert.Equal(0, RawSlot(3));                              // vacated tail slot zeroed

        Assert.True(BpGenListDemoItemsOps.Resize(ref c, 1));
        for (int i = 1; i < BpGenListDemo.Capacity; i++)
            Assert.Equal(0, RawSlot(i));                          // dropped tail zeroed

        BpGenListDemoItemsOps.Clear(ref c);
        Assert.Equal(0, ReRead().Count);
        Assert.Equal(0, RawSlot(0));
    }

    [Fact]
    public void GeneratedOps_OverflowBoundsAndClampContract()
    {
        ref var c = ref Rw();
        for (int i = 0; i < BpGenListDemo.Capacity; i++)
            Assert.True(BpGenListDemoItemsOps.Add(ref c, i));
        Assert.False(BpGenListDemoItemsOps.Add(ref c, 999));                        // full
        Assert.False(BpGenListDemoItemsOps.SetAt(ref c, BpGenListDemo.Capacity, 9)); // OOB
        Assert.False(BpGenListDemoItemsOps.Resize(ref c, BpGenListDemo.Capacity + 1));

        c.Count = 999_999;                                        // F2: garbage Count
        var read = ReRead();
        Assert.Equal(BpGenListDemo.Capacity, BpGenListDemoItemsOps.Count(in read));
        ref var c2 = ref Rw();
        Assert.False(BpGenListDemoItemsOps.Add(ref c2, 1));       // clamped => full, no OOB
    }

    // ---- editor discovery: generated is indistinguishable from hand-written --

    [Fact]
    public void EditorDiscovery_FindsGeneratedReadPairAndWriteSet()
    {
        var collections = ComponentFieldReflector.TryReflectCollections("Hrot.AI.Behaviors.BpGenListDemo");
        var items = Assert.Single(collections);
        Assert.Equal("Items", items.Name);
        Assert.Equal("System.Int32", items.ElementTypeId);
        Assert.Equal("Hrot.AI.Behaviors.BpGenListDemoItemsOps.Count", items.CountAccessorFqn);
        Assert.Equal("Hrot.AI.Behaviors.BpGenListDemoItemsOps.Item",  items.ItemAccessorFqn);

        var writeOps = ComponentFieldReflector.TryReflectWriteAccessors("Hrot.AI.Behaviors.BpGenListDemo", "Items");
        Assert.Equal(6, writeOps.Count);
        foreach (var op in Enum.GetValues<CollectionWriteOp>())
            Assert.Equal($"Hrot.AI.Behaviors.BpGenListDemoItemsOps.{op}", writeOps[op]);

        Assert.True(ComponentFieldReflector.IsWritableComponent("Hrot.AI.Behaviors.BpGenListDemo"));
    }
}
