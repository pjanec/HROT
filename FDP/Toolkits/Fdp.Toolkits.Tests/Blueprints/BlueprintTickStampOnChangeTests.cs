using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Blueprints.Systems;
using Xunit;

namespace Fdp.Toolkits.Tests.Blueprints;

/// <summary>
/// ⭐ Q-1 (POC 2 of the parallel simulation; DESIGN_Parallel_Simulation §10.2) — with <see cref="BlueprintTickSystem.StampOnChange"/> a
/// blueprint slot that runs but writes back the SAME bytes (the generated tick's every-pass state and edge-memory writes) leaves its chunk's
/// version alone; a real change stamps it, in the same frame. 🔴 Red-proof: the knob off (the old "a slot ran ⇒ stamp") — the second,
/// unchanged frame reads as a change.
/// </summary>
[Collection("BlueprintStampOnChange")]
public sealed unsafe class BlueprintTickStampOnChangeTests
{
    private const int Id = 4242;
    private static byte _value;

    private static IComponentTable TableOf(EntityRepository w, BlueprintTierSpec spec)
        => (IComponentTable)typeof(EntityRepository).GetMethod(nameof(EntityRepository.GetComponentTable))!
            .MakeGenericMethod(spec.ComponentType).Invoke(w, null)!;

    private static (EntityRepository World, BlueprintTickSystem System, IComponentTable Table) Build()
    {
        var w = new EntityRepository();
        BlueprintTierTable.RegisterAll(w);
        var reg = new BlueprintRegistry();
        reg.RegisterInstance(Id, new BlueprintDefinition
        {
            Name = "stamp-probe", Kind = BlueprintDispatchKind.Instance, StructureHash = 1, StateSize = 8,
            Tick = static (s, view, ecb, self, t, dt, iv) => { s[0] = _value; s[1] = 0; },   // writes every pass, as generated code does
        });
        var e = w.CreateEntity();
        Assert.True(OccurrenceStoreAccess.TryAttachSlot(w, e, Id, 8, 1, OccurrenceKind.Blueprint, out _, out _));
        return (w, new BlueprintTickSystem(reg), TableOf(w, BlueprintTierTable.Of(w, e)!));
    }

    [Fact]
    public void ASlotThatWritesTheSameBytes_LeavesItsChunkUnstamped_ARealChangeStampsIt()
    {
        bool was = BlueprintTickSystem.StampOnChange;
        BlueprintTickSystem.StampOnChange = true;
        try
        {
            var (w, sys, table) = Build();
            using var _ = w;
            _value = 7;
            w.Tick(); sys.Execute(w, 0.016f);                    // 0 → 7: a change
            uint first = w.GlobalVersion;
            w.Tick(); sys.Execute(w, 0.016f);                    // 7 → 7: ran, wrote nothing new
            Assert.False(table.HasChanges(first));
            uint second = w.GlobalVersion;
            _value = 9;
            w.Tick(); sys.Execute(w, 0.016f);                    // 7 → 9: a change, stamped this frame
            Assert.True(table.HasChanges(second));
        }
        finally { BlueprintTickSystem.StampOnChange = was; }
    }

    [Fact]
    public void WithTheKnobOff_ASlotThatRan_StampsItsChunk_AsBefore()
    {
        bool was = BlueprintTickSystem.StampOnChange;
        BlueprintTickSystem.StampOnChange = false;
        try
        {
            var (w, sys, table) = Build();
            using var _ = w;
            _value = 7;
            w.Tick(); sys.Execute(w, 0.016f);
            uint first = w.GlobalVersion;
            w.Tick(); sys.Execute(w, 0.016f);
            Assert.True(table.HasChanges(first));
        }
        finally { BlueprintTickSystem.StampOnChange = was; }
    }

    [Fact]
    public void ASteadyTick_AllocatesNothing()
    {
        bool was = BlueprintTickSystem.StampOnChange;
        BlueprintTickSystem.StampOnChange = true;
        try
        {
            var (w, sys, _) = Build();
            using var __ = w;
            _value = 3;
            for (int i = 0; i < 5; i++) { w.Tick(); sys.Execute(w, 0.016f); }   // warm
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) { w.Tick(); sys.Execute(w, 0.016f); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        finally { BlueprintTickSystem.StampOnChange = was; }
    }
}
