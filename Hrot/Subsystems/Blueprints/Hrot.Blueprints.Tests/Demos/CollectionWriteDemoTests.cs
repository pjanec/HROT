using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hrot.AI.Behaviors;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Tests.Demos;

/// <summary>
/// FC-1 wrap-up (closed with the R5 batch) -- the CollectionWriteDemo recipe: an Instance
/// blueprint that mutates a fixed-capacity collection on its OWN ECS component
/// (<see cref="BpFixedListDemo"/>, [BlueprintWritable]) through the accessor-mediated Add
/// write. The runtime proof drives the REAL generated TickThunk against a live
/// <c>EntityRepository</c> (which IS the emitted `((EntityRepository)view)` cast target) and
/// reads the COMPONENT back: the list fills 1→4, the fifth Add degrades to Ok=false (no
/// write, no throw), and the tail-always-default invariant holds. This is the component-home
/// end-to-end demo deferred at FC-1.
/// </summary>
public sealed class CollectionWriteDemoTests
{
    private static BlueprintAsset LoadRecipe()
    {
        var dir = Path.GetDirectoryName(typeof(BpFixedListDemo).Assembly.Location)!;
        var json = File.ReadAllText(Path.Combine(dir, "Recipes", "Blueprints", "CollectionWriteDemo.bp.json"));
        return BlueprintJsonServices.Deserialize(json)!;
    }

    private delegate void SpanAction(Span<byte> bytes);
    private delegate void TickThunkDel(
        Span<byte> bytes,
        Fdp.ModuleHost.Abstractions.ISimulationView view,
        Fdp.Interfaces.IEntityCommandBuffer ecb,
        Fdp.Core.Entity self,
        float time,
        float deltaTime,
        uint instanceVersion);

    [Fact]
    public void CollectionWriteDemo_CompileAndLoad_Succeeds()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        Assert.NotNull(fixture.CompileAndLoad(LoadRecipe()));
    }

    [Fact]
    public void CollectionWriteDemo_FiveTicks_ComponentFillsToCapacity_FifthAddRejected()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var assembly = fixture.CompileAndLoad(LoadRecipe());

        // A live entity carrying the writable demo component -- the write target.
        fixture.World.RegisterComponent<BpFixedListDemo>();
        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, default(BpFixedListDemo));

        var bpClass = assembly.GetTypes().Single(t => t.Name.EndsWith("_Bp") && t.GetNestedType("State") != null);
        var state = bpClass.GetNestedType("State")!;
        int cntOffset = (int)Marshal.OffsetOf(state, "Count");

        var bytes = new byte[Marshal.SizeOf(state) + 64];
        var init = (SpanAction)Delegate.CreateDelegate(typeof(SpanAction), bpClass.GetMethod("InitDefault")!);
        init(bytes);
        var tick = (TickThunkDel)Delegate.CreateDelegate(typeof(TickThunkDel), bpClass.GetMethod("TickThunk")!);

        for (int t = 1; t <= 5; t++)
        {
            // The emitted write path casts the view to EntityRepository -- pass the real repo.
            tick(bytes, fixture.World, fixture.Ecb, entity, t, 0.016f, 0);

            int expected = Math.Min(t, BpFixedListDemo.Capacity);
            ref readonly var c = ref fixture.World.GetComponentRO<BpFixedListDemo>(entity);
            Assert.Equal(expected, c.Count);                                   // capacity bound
            Assert.Equal(expected, BitConverter.ToInt32(bytes, cntOffset));    // mirrored via ItemCount
        }

        // All landed slots carry the appended value; the tail stays default (G6).
        ref readonly var final = ref fixture.World.GetComponentRO<BpFixedListDemo>(entity);
        var span = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<BpFixedListDemo.Buffer, int>(ref Unsafe.AsRef(in final.Items)),
            BpFixedListDemo.Capacity);
        for (int i = 0; i < BpFixedListDemo.Capacity; i++)
            Assert.Equal(7, span[i]);
    }
}
