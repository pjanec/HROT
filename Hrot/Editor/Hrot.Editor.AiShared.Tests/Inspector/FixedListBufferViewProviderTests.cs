using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hrot.Editor.AiShared.Inspector;
using StructEdit.Core;
using StructEdit.Reflection;

namespace Hrot.Editor.AiShared.Tests.Inspector;

// ── Fixture types ─────────────────────────────────────────────────────────────

[InlineArray(4)]
public struct PvIntBuf4 { private int _e0; }
public struct PvIntList4 { public int Count; public PvIntBuf4 Items; }

/// <summary>An action working-state DTO hosting a fixed list beside scalars.</summary>
public struct PatrolWorkingState
{
    public float Cooldown;
    public PvIntList4 Stops;
}

/// <summary>A DTO with a plain [InlineArray] buffer NOT wrapped in the list shape.</summary>
public struct RawBufferState
{
    public int Tag;
    public PvIntBuf4 Raw;
}

/// <summary>
/// FC-3c (Q#21-D3/D-e/D-p P1) — the host-side <see cref="FixedListBufferViewProvider"/> over a
/// REAL StructEdit session:
/// <list type="bullet">
///   <item>the wrapper's buffer node is replaced by a Count-bounded element window (F2-clamped);</item>
///   <item>the collapsed row title is the SHARED <see cref="Fdp.Core.FixedListFormatter"/>
///   summary — identical to the Blueprints debugger watch's string;</item>
///   <item>a bare [InlineArray] buffer outside the wrapper shape is NOT claimed (keeps the
///   default all-capacity expansion);</item>
///   <item>display-only v1: the window offers exactly the elements, no resize surface.</item>
/// </list>
/// </summary>
public sealed class FixedListBufferViewProviderTests
{
    private static IComponentEditService BuildService()
        => new ComponentEditServiceBuilder()
            .RegisterBufferViewProvider(new FixedListBufferViewProvider())
            .Build();

    private static PatrolWorkingState MakeState(int count, params int[] items)
    {
        var s = new PatrolWorkingState { Cooldown = 1.5f, Stops = { Count = count } };
        var span = MemoryMarshal.CreateSpan(ref Unsafe.As<PvIntBuf4, int>(ref s.Stops.Items), 4);
        for (int i = 0; i < items.Length; i++) span[i] = items[i];
        return s;
    }

    [Fact]
    public void WrapperBuffer_Claimed_WindowIsCountBounded_TitleIsSharedSummary()
    {
        var service = BuildService();
        using var session = service.Open(MakeState(2, 10, 20, 99), typeof(PatrolWorkingState));

        var stops = session.Document.Root.Children.Single(c => c.Name == "Stops");
        var view = stops.Children.Single(c => c.Kind == EditNodeKind.BufferView);

        Assert.Equal("List<Int32>[4] Count=2 {10, 20}", view.Name);   // THE shared summary string
        Assert.Equal(2, view.Children.Count);                          // window, not capacity
        Assert.Equal(10, view.Children[0].Binding!.GetBoxed());
        Assert.Equal(20, view.Children[1].Binding!.GetBoxed());

        // The wrapper's Count row stays visible beside the view (plain int).
        Assert.Contains(stops.Children, c => c.Name == "Count");
    }

    [Fact]
    public void GarbageCount_WindowClampsToCapacity_NeverBeyond()
    {
        var service = BuildService();
        using var session = service.Open(MakeState(99, 1, 2, 3, 4), typeof(PatrolWorkingState));

        var view = session.Document.Root.Children.Single(c => c.Name == "Stops")
            .Children.Single(c => c.Kind == EditNodeKind.BufferView);
        Assert.Equal(4, view.Children.Count);                          // F2 clamp

        using var neg = service.Open(MakeState(-3), typeof(PatrolWorkingState));
        var negView = neg.Document.Root.Children.Single(c => c.Name == "Stops")
            .Children.Single(c => c.Kind == EditNodeKind.BufferView);
        Assert.Empty(negView.Children);
    }

    [Fact]
    public void BareInlineArrayBuffer_NotClaimed_KeepsDefaultExpansion()
    {
        var service = BuildService();
        using var session = service.Open(new RawBufferState(), typeof(RawBufferState));

        var raw = session.Document.Root.Children.Single(c => c.Name == "Raw");
        Assert.Equal(EditNodeKind.InlineArray, raw.Kind);              // untouched
        Assert.Equal(4, raw.Children.Count);                           // full capacity as before
    }
}
