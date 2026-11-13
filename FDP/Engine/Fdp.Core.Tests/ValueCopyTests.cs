using System.Collections.Generic;
using Fdp.Core;
using Xunit;

namespace Fdp.Core.Tests;

/// <summary>
/// ⭐ N-9 (CE-4471) — a managed singleton the writer refills in place (<see cref="IValueCopy{T}"/> + <see cref="DataPolicy.SnapshotViaClone"/>)
/// is never SHARED across worlds by a table copy: the destination gets its own instance, holding the same value, refilled in place on the
/// next copy (no allocation) — so the drawing thread never reads the object the sim thread refills (the traffic frame).
/// 🔴 Red-proof: the value copy removed from <c>ManagedComponentTable.CopyRowFrom</c> ⇒ the destination holds the source's instance.
/// </summary>
public sealed class ValueCopyTests
{
    [ComponentId(501)]
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay | DataPolicy.SnapshotViaClone)]
    public sealed class Frame : IValueCopy<Frame>
    {
        public readonly List<int> Rows = new();
        public Frame CloneValue() { var f = new Frame(); f.CopyFrom(this); return f; }
        public void CopyFrom(Frame source) { Rows.Clear(); Rows.AddRange(source.Rows); }
    }

    [Fact]
    public void N9_AValueCopiedSingleton_GetsItsOwnInstance_RefilledInPlace_NeverTheSourcesObject()
    {
        using var sim = new EntityRepository();
        using var ui = new EntityRepository();
        sim.RegisterManagedComponent<Frame>();
        var frame = new Frame();
        frame.Rows.AddRange(new[] { 1, 2, 3 });
        sim.SetSingletonManaged(frame);

        ui.CopySingletonsFrom(sim, default);
        var mine = ui.GetSingletonManaged<Frame>()!;
        Assert.NotSame(frame, mine);
        Assert.Equal(new[] { 1, 2, 3 }, mine.Rows);

        frame.Rows.Clear(); frame.Rows.Add(7);   // the writer refills its own instance
        sim.SetSingletonManaged(frame);
        Assert.Equal(new[] { 1, 2, 3 }, mine.Rows);   // the copy is untouched until the next copy

        for (int i = 0; i < 3; i++) ui.CopySingletonsFrom(sim, default);   // warm
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        ui.CopySingletonsFrom(sim, default);
        long after = System.GC.GetAllocatedBytesForCurrentThread();
        Assert.Same(mine, ui.GetSingletonManaged<Frame>());   // the same destination instance, refilled
        Assert.Equal(new[] { 7 }, mine.Rows);
        Assert.Equal(0, after - before);
    }
}
