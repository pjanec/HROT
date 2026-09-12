using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Fdp.Core;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 94 (<c>94c</c>) — the accessor is called ONCE PER BEHAVIOUR FRAME; every UI frame in
/// between draws from the cache.</b>
///
/// <para>📄 <c>Q46</c> §2 — the user's own specification: <b>rule 2</b> <i>"the accessor is called once
/// per brain frame"</i> · <b>rule 3</b> <i>"cached … and rendered every UI frame from the cache, without
/// calling the accessor"</i> · <b>rule 4a</b> <i>"pin while running-but-PAUSED ⇒ call the accessor
/// immediately"</i> · <b>rule 4b</b> <i>"pin while PLANNING ⇒ do not call it."</i></para>
///
/// <para>⭐⭐ <b>Every rail below COUNTS accessor invocations</b> — ⛔ a rail that merely checks the
/// rendered value would pass against a sampler that reads on every repaint, which is exactly the
/// behaviour rule 2 replaces.</para>
/// </summary>
public sealed class TheRowSamplesOnThePulseTests
{
    private static readonly Guid AssetId = new("cccccccc-0000-0000-0000-00000000000c");

    /// <summary>A row whose arms COUNT their calls and read a mutable source.</summary>
    private sealed class CountingRow
    {
        public int    ByteReads;
        public int    ObjectReads;
        public int    Value = 1;
        public uint   Pulse;

        public VariableRow Row(string name = "Health") => new(
            Origin:          new VariableRowOrigin(AssetId, default, "s", name, "Alpha"),
            ShortName:       name,
            TypeText:        "int",
            ClrType:         typeof(int),
            ReadValue:       () => { ByteReads++;   return I32(Value); },
            AssetTick:       () => Pulse,
            ReadValueObject: () => { ObjectReads++; return Value; });
    }

    private static byte[] I32(int v) { var b = new byte[4]; MemoryMarshal.Write(b, in v); return b; }
    private static int ReadI32(VariableRow r) => MemoryMarshal.Read<int>(r.ReadValue());

    // ══ rule 2 + 3 — one sample per pulse ════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE rule-2 rail.</b> Five repaints on ONE pulse ⇒ the accessor is called <b>once</b>,
    /// and every repaint still renders the value.
    /// </summary>
    [Fact]
    public void ManyRepaintsOnOnePulseCallTheAccessorOnce()
    {
        var src     = new CountingRow();
        var sampler = new VariableRowSampler();
        var rows    = new[] { src.Row() };

        for (int i = 0; i < 5; i++)
        {
            var sampled = sampler.Sample(rows, VariableRunState.Running);
            Assert.Equal(1, ReadI32(sampled[0]));            // drawn every frame…
        }

        Assert.Equal(1, src.ByteReads);                       // …read once
        Assert.Equal(1, src.ObjectReads);
    }

    // ══ Batch 97 (97d) — the SECOND clock ════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b><c>R-76</c>'s BINDING clock: a selection change re-samples, WITH THE PULSE STOPPED.</b>
    ///
    /// <para>🔴 Before <c>97d</c> the sampler had ONE clock, so under a breakpoint — where the pulse
    /// never moves — selecting another entity re-evaluated <b>nothing</b>. 📌 The user: <i>"the watch
    /// row must update (accessor evaluated) even if time currently stopped."</i></para>
    /// </summary>
    [Fact]
    public void ASelectionChangeReSamplesEvenThoughThePulseHasNotMoved()
    {
        var src     = new CountingRow();
        var sampler = new VariableRowSampler();
        var rows    = new[] { src.Row() };

        sampler.Sample(rows, VariableRunState.Paused);
        Assert.Equal(1, src.ObjectReads);

        sampler.Sample(rows, VariableRunState.Paused);
        Assert.Equal(1, src.ObjectReads);                     // ⭐ still one — rule 2 holds

        EntityBindingFrame.Advance();                          // ⭐⭐ the designer picks another entity

        src.Value = 99;
        var afterRebind = sampler.Sample(rows, VariableRunState.Paused);

        Assert.Equal(2, src.ObjectReads);
        Assert.Equal(99, ReadI32(afterRebind[0]));
    }

    /// <summary>
    /// ⭐⭐ <b>…and it does NOT fire on a mere repaint.</b> ⛔ 📌 <c>R-76</c>: <i>"re-resolving the
    /// binding per tick would churn the row's identity under the cursor."</i> ⚠ Without this, the
    /// binding clock would silently become "sample every frame" and undo <c>94c</c>.
    /// </summary>
    [Fact]
    public void TheBindingClockDoesNotFireOnARepaint()
    {
        var src     = new CountingRow();
        var sampler = new VariableRowSampler();
        var rows    = new[] { src.Row() };

        EntityBindingFrame.Advance();
        sampler.Sample(rows, VariableRunState.Paused);
        int afterFirst = src.ObjectReads;

        for (int i = 0; i < 5; i++) sampler.Sample(rows, VariableRunState.Paused);

        Assert.Equal(afterFirst, src.ObjectReads);
    }

    /// <summary>⭐⭐ …and when the pulse MOVES, exactly one more sample is taken.</summary>
    [Fact]
    public void MovingThePulseTakesExactlyOneMoreSample()
    {
        var src     = new CountingRow();
        var sampler = new VariableRowSampler();
        var rows    = new[] { src.Row() };

        sampler.Sample(rows, VariableRunState.Running);
        sampler.Sample(rows, VariableRunState.Running);
        Assert.Equal(1, src.ByteReads);

        src.Value = 99;
        src.Pulse++;                                          // a brain frame ran

        var sampled = sampler.Sample(rows, VariableRunState.Running);

        Assert.Equal(2,  src.ByteReads);
        Assert.Equal(99, ReadI32(sampled[0]));
    }

    /// <summary>
    /// ⛔⛔ <b>A value that changes WITHOUT a pulse is not shown</b> — and that is the specification,
    /// not a limitation: rule 2 says the accessor runs <i>"only when the frame's <c>dt &gt; 0</c>"</i>.
    /// ⭐ It is what keeps a value stable under a breakpoint.
    /// </summary>
    [Fact]
    public void AValueChangingWithoutAPulseIsNotResampled()
    {
        var src     = new CountingRow();
        var sampler = new VariableRowSampler();
        var rows    = new[] { src.Row() };

        sampler.Sample(rows, VariableRunState.Running);
        src.Value = 99;                                       // the world "moved" but the sim did not

        Assert.Equal(1, ReadI32(sampler.Sample(rows, VariableRunState.Running)[0]));
        Assert.Equal(1, src.ByteReads);
    }

    // ══ rule 4a / 4b — pinning while paused vs planning ══════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Rule 4a.</b> Paused means the pulse never moves — so without the "never sampled ⇒ sample
    /// NOW" clause a row pinned while paused would wait for a resume that may never come.
    /// </summary>
    [Fact]
    public void ARowFirstSeenWhilePausedSamplesImmediately()
    {
        var src     = new CountingRow { Value = 7 };
        var sampler = new VariableRowSampler();

        var sampled = sampler.Sample(new[] { src.Row() }, VariableRunState.Paused);

        Assert.Equal(1, src.ByteReads);
        Assert.Equal(7, ReadI32(sampled[0]));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Rule 4b.</b> Planning does not call the accessor <b>at all</b> — ⛔ not "calls it and
    /// discards". ⚠ The rows pass through untouched, so the Value column's INITIAL arm still renders
    /// the authored default.
    /// </summary>
    [Fact]
    public void PlanningNeverCallsTheAccessor()
    {
        var src     = new CountingRow();
        var sampler = new VariableRowSampler();
        var rows    = new[] { src.Row() };

        var sampled = sampler.Sample(rows, VariableRunState.Planning);

        Assert.Equal(0, src.ByteReads);
        Assert.Equal(0, src.ObjectReads);
        Assert.Same(rows, sampled);
    }

    // ══ independence — the user's ruling ═════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Two panels, two samplers, no coupling.</b> 📌 The user, verbatim: <i>"watch panel rows
    /// are not identical instances to details panel rows… each completely independent on each other
    /// knowing nothing about each other."</i>
    ///
    /// <para>⛔ A process-wide cache keyed by <c>(AssetId, Entity, VariablePath)</c> would make the
    /// second panel reuse the first's sample — this rail fails against that design.</para>
    /// </summary>
    [Fact]
    public void TwoPanelsSampleIndependentlyEvenForTheSameRowIdentity()
    {
        var src     = new CountingRow();
        var details = new VariableRowSampler();
        var watch   = new VariableRowSampler();
        var rows    = new[] { src.Row() };

        details.Sample(rows, VariableRunState.Running);
        Assert.Equal(1, src.ByteReads);

        watch.Sample(rows, VariableRunState.Running);          // same identity, other panel
        Assert.Equal(2, src.ByteReads);                        // ⛔ 1 would mean a shared cache
    }

    // ══ what the rewrite preserves ═══════════════════════════════════════════

    /// <summary>
    /// ⚠⚠ <b>The sampled row is a DIFFERENT record instance</b> — its arms were rewritten to read the
    /// cache. ⭐ <b>Identity is preserved</b>, and identity is <c>Origin.Key</c>, which is what every
    /// lookup in this namespace uses *(the row type's own doc says so)*. ⛔ Record equality also
    /// compares the delegates and was never identity.
    /// </summary>
    [Fact]
    public void TheSampledRowKeepsItsIdentityAndItsNonValueFacets()
    {
        var src = new CountingRow();
        var row = src.Row() with { IsStale = true, RowKind = VariableRowKind.NodeOwned };

        var sampled = new VariableRowSampler().Sample(new[] { row }, VariableRunState.Running)[0];

        Assert.Equal(row.Origin.Key, sampled.Origin.Key);
        Assert.True(sampled.IsStale);
        Assert.Equal(VariableRowKind.NodeOwned, sampled.RowKind);
        Assert.NotNull(sampled.AssetTick);
    }

    /// <summary>
    /// ⭐ A row with <b>no</b> object arm does not gain one — ⛔ otherwise the formatter would prefer
    /// an object arm that only ever returns <c>null</c>, and every byte-arm cell would read
    /// <c>&lt;unreadable&gt;</c>.
    /// </summary>
    [Fact]
    public void ARowWithNoObjectArmDoesNotGainOne()
    {
        var row = new VariableRow(
            Origin:    new VariableRowOrigin(AssetId, default, "s", "Health", "Alpha"),
            ShortName: "Health", TypeText: "int", ClrType: typeof(int),
            ReadValue: () => I32(5),
            AssetTick: () => 0u);

        var sampled = new VariableRowSampler().Sample(new[] { row }, VariableRunState.Running)[0];

        Assert.Null(sampled.ReadValueObject);
        Assert.Equal(5, ReadI32(sampled));
    }

    /// <summary>⭐ A throwing accessor is absorbed — ⛔ a sampler never takes the window down.</summary>
    [Fact]
    public void AThrowingAccessorDoesNotEscape()
    {
        var row = new VariableRow(
            Origin:    new VariableRowOrigin(AssetId, default, "s", "Boom", "Alpha"),
            ShortName: "Boom", TypeText: "int", ClrType: typeof(int),
            ReadValue: () => throw new InvalidOperationException("boom"),
            AssetTick: () => 0u,
            ReadValueObject: () => throw new InvalidOperationException("boom"));

        var sampled = new VariableRowSampler().Sample(new[] { row }, VariableRunState.Running)[0];

        Assert.Empty(sampled.ReadValue().ToArray());
        Assert.Null(sampled.ReadValueObject!.Invoke());
    }

    // ══ the model wires it ═══════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Asked of the MODEL, not of the sampler</b> — 📌 <c>R-67</c>: a rail that drives the
    /// sampler directly cannot see whether <c>VariableTableModel.Build()</c> actually calls it.
    /// </summary>
    [Fact]
    public void TheModelSamplesThroughItsOwnSamplerOncePerPulse()
    {
        var src   = new CountingRow();
        var model = new VariableTableModel(
            new FixedVariableRowSource(new[] { src.Row() }), VariableTableColumns.Details)
        { RunState = VariableRunState.Running };

        model.Build();
        model.Build();
        model.Build();

        Assert.Equal(1, src.ByteReads);   // ⛔ 3 would mean the model reads per repaint, as it used to
    }

    /// <summary>⭐ …and two models do not share a cache, which is the per-panel ruling again.</summary>
    [Fact]
    public void TwoModelsDoNotShareASampler()
    {
        var src    = new CountingRow();
        var source = new FixedVariableRowSource(new[] { src.Row() });

        new VariableTableModel(source, VariableTableColumns.Details)
            { RunState = VariableRunState.Running }.Build();
        new VariableTableModel(source, VariableTableColumns.Details)
            { RunState = VariableRunState.Running }.Build();

        Assert.Equal(2, src.ByteReads);
    }
}
