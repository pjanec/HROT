using System.Runtime.InteropServices;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Tests.Demos;

/// <summary>
/// FC-2/LV-6 -- the ListVariableDemo recipe: an Instance blueprint with a fixed-list variable
/// (Waypoints: int × 4) that Appends 7 each tick and mirrors the logical length into a scalar
/// via Item Count. The runtime proof drives the REAL generated TickThunk five ticks and asserts
/// the capacity bound: the list fills to 4 and the fifth Add degrades to a no-write (Ok=false)
/// -- the list never overflows.
/// </summary>
public sealed class ListVariableDemoTests
{
    private static BlueprintAsset LoadRecipe()
    {
        // Same production-location resolution as RecipeIntegrityTests.LoadRecipe.
        var aiBehaviors = typeof(Hrot.AI.Behaviors.BpFixedListDemo).Assembly;
        var dir = Path.GetDirectoryName(aiBehaviors.Location)!;
        var json = File.ReadAllText(Path.Combine(dir, "Recipes", "Blueprints", "ListVariableDemo.bp.json"));
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
    public void ListVariableDemo_CompileAndLoad_Succeeds()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        Assert.NotNull(fixture.CompileAndLoad(LoadRecipe()));
    }

    [Fact]
    public void ListVariableDemo_FiveTicks_FillsToCapacity_FifthAddRejected()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var assembly = fixture.CompileAndLoad(LoadRecipe());

        var bpClass = assembly.GetTypes().Single(t => t.Name.EndsWith("_Bp") && t.GetNestedType("State") != null);
        var state = bpClass.GetNestedType("State")!;
        int listOffset = (int)Marshal.OffsetOf(state, "Waypoints");
        int cntOffset  = (int)Marshal.OffsetOf(state, "Count");

        var bytes = new byte[Marshal.SizeOf(state) + 64];
        var init = (SpanAction)Delegate.CreateDelegate(typeof(SpanAction), bpClass.GetMethod("InitDefault")!);
        init(bytes);
        var tick = (TickThunkDel)Delegate.CreateDelegate(typeof(TickThunkDel), bpClass.GetMethod("TickThunk")!);

        for (int t = 1; t <= 5; t++)
        {
            tick(bytes, fixture.View, fixture.Ecb, default, t, 0.016f, 0);
            int expected = Math.Min(t, 4);                          // capacity bound
            Assert.Equal(expected, BitConverter.ToInt32(bytes, listOffset));      // list Count
            Assert.Equal(expected, BitConverter.ToInt32(bytes, cntOffset));       // mirrored scalar
        }

        // All four landed slots carry the appended value; the fifth Add wrote nothing.
        for (int i = 0; i < 4; i++)
            Assert.Equal(7, BitConverter.ToInt32(bytes, listOffset + 4 + i * 4));
    }
}
