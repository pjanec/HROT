using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fdp.Core;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 94 (<c>94d</c>) — change detection compares BYTES, on BOTH arms.</b>
///
/// <para>📄 <b>The ruling, verbatim</b> *(<c>R-103</c> / <c>Q46</c> §2 rule 9, the user)*: <i>"we have
/// fast pre-compiled binary serializer mechanism for any component and i guess it can be used for any
/// class. it produces bytes. we compare these bytes. <b>No way comparing rendered text!</b>"</i></para>
///
/// <para>🔴🔴 <b>Two defects closed here, and the second was invisible.</b>
/// ① <c>VariableChangeMonitor.Observe</c> read <b>only</b> <c>row.ReadValue()</c> — the BYTE arm — so
/// Blueprint's already-decoded object values <b>could never highlight at all</b>.
/// ② Every production row passed <c>AssetTick: null</c> *(closed by <c>94b</c>)</c>, so the monitor
/// returned <c>None</c> on its first line regardless. ⇒ ⭐ <b>the highlight has never fired in
/// production on any host.</b></para>
/// </summary>
public sealed class TheChangeMonitorComparesBytesTests : IDisposable
{
    private static readonly Guid AssetId = new("dddddddd-0000-0000-0000-00000000000d");

    public TheChangeMonitorComparesBytesTests() => ManagedValueBytes.ResetFenceForTests();
    public void Dispose()                       => ManagedValueBytes.ResetFenceForTests();

    private static byte[] I32(int v) { var b = new byte[4]; MemoryMarshal.Write(b, in v); return b; }

    private static VariableRow ObjectRow(Func<object?> read, Func<uint> tick, string name = "Health")
        => new(
            Origin:          new VariableRowOrigin(AssetId, default, "s", name, "Alpha"),
            ShortName:       name, TypeText: "obj", ClrType: typeof(object),
            ReadValue:       () => Array.Empty<byte>(),
            AssetTick:       () => tick(),
            ReadValueObject: () => read());

    private static VariableRow ByteRow(Func<byte[]> read, Func<uint> tick, string name = "Health")
        => new(
            Origin:    new VariableRowOrigin(AssetId, default, "s", name, "Alpha"),
            ShortName: name, TypeText: "int", ClrType: typeof(int),
            ReadValue: () => read(),
            AssetTick: () => tick());

    // ══ the object arm, which could never highlight ══════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE rail: a MANAGED value change lights the highlight.</b>
    /// 🔴 Impossible before this batch — the monitor never looked at the object arm.
    /// </summary>
    [Fact]
    public void AManagedValueChangeIsDetectedThroughTheObjectArm()
    {
        var monitor = new VariableChangeMonitor();
        string value = "alpha";
        uint   tick  = 1;
        var    row   = ObjectRow(() => value, () => tick);

        Assert.False(monitor.Observe(row, VariableRunState.Running).Changed, "first sighting is a baseline");

        value = "omega";
        tick  = 2;

        Assert.True(monitor.Observe(row, VariableRunState.Running).Changed);
    }

    /// <summary>⭐ …and an UNCHANGED managed value does not light it — ⛔ else every row would be red.</summary>
    [Fact]
    public void AnUnchangedManagedValueDoesNotHighlight()
    {
        var monitor = new VariableChangeMonitor();
        uint tick   = 1;
        var  row    = ObjectRow(() => "same", () => tick);

        monitor.Observe(row, VariableRunState.Running);
        tick = 2;

        Assert.False(monitor.Observe(row, VariableRunState.Running).Changed);
    }

    /// <summary>
    /// ⭐⭐ <b>A CLASS, not just a string</b> — 📌 rule 8: <i>"a class or a string sitting in a managed
    /// component's field."</i> ⭐ <c>FdpAutoSerializer</c> recurses into it (CASE Z).
    /// </summary>
    [Fact]
    public void AChangeInsideAManagedClassIsDetected()
    {
        var monitor = new VariableChangeMonitor();
        var payload = new Payload { Hp = 10, Name = "scout" };
        uint tick   = 1;
        var  row    = ObjectRow(() => payload, () => tick);

        monitor.Observe(row, VariableRunState.Running);

        payload.Hp = 11;                       // one field, deep in the object
        tick = 2;

        Assert.True(monitor.Observe(row, VariableRunState.Running).Changed);
    }

    // ══ the byte arm still wins where it has content ═════════════════════════

    /// <summary>
    /// ⭐⭐ <b>A struct of ANY size compares by its raw bytes</b> — 📌 rule 7: <i>"structures of ANY
    /// size, not limited to a fixed number of bytes."</i> ⛔ There was never a size limit here to
    /// remove; the 64-byte cap belongs to <c>AiWatchWindow</c>'s old value carrier, which pinned rows
    /// do not use.
    /// </summary>
    [Fact]
    public void ALargeStructComparesByItsRawBytes()
    {
        var monitor = new VariableChangeMonitor();
        var big     = new byte[4096];
        uint tick   = 1;
        var  row    = ByteRow(() => big, () => tick);

        monitor.Observe(row, VariableRunState.Running);

        big[4000] = 7;                          // one byte, far past any carrier limit
        tick = 2;

        Assert.True(monitor.Observe(row, VariableRunState.Running).Changed);
    }

    // ══ the FENCE — tooth ③, the one that can take the editor down ═══════════

    /// <summary>
    /// ⭐⭐⭐ <b>A CYCLIC graph does not crash the editor.</b> 📐 <c>FdpAutoSerializer</c> has <b>no
    /// cycle guard</b> *(<c>Q46</c> §3 tooth ③)* — a back-reference recurses until the stack dies.
    ///
    /// <para>⭐ Fenced by a SIZE CAP enforced <b>during</b> the write: a cycle emits bytes without
    /// bound and trips it long before the stack goes. ⛔ A <c>StackOverflowException</c> cannot be
    /// caught in .NET, so measuring the finished buffer would never happen.</para>
    /// </summary>
    [Fact]
    public void ACyclicValueIsFencedOffInsteadOfCrashing()
    {
        var a = new Node();
        var b = new Node();
        a.Next = b;
        b.Next = a;                              // ⛔ the cycle

        var monitor = new VariableChangeMonitor();
        uint tick   = 1;

        var highlight = monitor.Observe(ObjectRow(() => a, () => tick), VariableRunState.Running);

        Assert.False(highlight.Changed);
        Assert.True(ManagedValueBytes.IsNotComparable(typeof(Node)),
            "the TYPE is fenced, so the next frame does not pay for it again");
    }

    /// <summary>
    /// ⭐⭐ <b>The fence is per TYPE and permanent</b> — ⛔ the point is that a bad type costs ONE
    /// failed attempt per session, not one per row per frame.
    /// </summary>
    [Fact]
    public void AFencedTypeIsNeverSerializedAgain()
    {
        var bytes = new ManagedValueBytes();
        var a = new Node(); var b = new Node(); a.Next = b; b.Next = a;

        Assert.Null(bytes.TryGetBytes(a));
        Assert.True(ManagedValueBytes.IsNotComparable(typeof(Node)));
        Assert.Null(bytes.TryGetBytes(new Node()));   // a HEALTHY instance of a fenced type, still null
    }

    /// <summary>
    /// ⛔⛔ <b>A fenced row reports NO CHANGE, never "unchanged"</b> — and the difference matters: two
    /// unserialisable values must not compare equal and claim the sim did nothing.
    /// </summary>
    [Fact]
    public void AFencedRowNeverHighlightsEvenWhenItsValueChanges()
    {
        var a = new Node(); var b = new Node(); a.Next = b; b.Next = a;
        var c = new Node(); var d = new Node(); c.Next = d; d.Next = c;

        var monitor = new VariableChangeMonitor();
        object current = a;
        uint   tick    = 1;
        var    row     = ObjectRow(() => current, () => tick);

        monitor.Observe(row, VariableRunState.Running);
        current = c;                             // a genuinely different graph
        tick    = 2;

        Assert.False(monitor.Observe(row, VariableRunState.Running).Changed);
    }

    /// <summary>
    /// ⚠ <b>Tooth ②, recorded as a rail rather than fixed:</b> <c>FdpAutoSerializer</c> skips get-only
    /// properties *(<c>CanRead &amp;&amp; CanWrite</c>)*, so a class exposing state ONLY through
    /// computed getters serialises to nothing and its changes are invisible.
    /// ⛔ <b>Do not "fix" the serializer</b> — it is flight-recorder infrastructure with its own rails.
    /// ⭐ Such a row simply never highlights, and this pins that so nobody rediscovers it as a bug.
    /// </summary>
    [Fact]
    public void AGetOnlyPropertyIsInvisibleToTheComparison()
    {
        var monitor = new VariableChangeMonitor();
        var value   = new GetOnly(1);
        uint tick   = 1;
        var  row    = ObjectRow(() => value, () => tick);

        monitor.Observe(row, VariableRunState.Running);

        value = new GetOnly(999);                // a different value…
        tick  = 2;

        Assert.False(monitor.Observe(row, VariableRunState.Running).Changed,   // …and no highlight
            "get-only members are not serialised, so this row can never highlight — Q46 §3 tooth ②");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The cycle fence is STATIC, and this rail is the reason.</b>
    ///
    /// <para>🔴🔴 <b>Measured, Batch 94:</b> the first fence was a size cap checked <em>during</em> the
    /// write. ⛔ It <b>aborted the whole test host</b> — a self-referencing node with a single
    /// reference member recurses <b>without writing a byte per level</b>, so the stack dies before any
    /// cap can be consulted, and a <c>StackOverflowException</c> cannot be caught in .NET.</para>
    ///
    /// <para>⇒ ⭐ <c>Node</c> below emits <b>nothing</b> per level by construction, so this rail passes
    /// only if the type was refused <b>before</b> the serializer ran.</para>
    /// </summary>
    [Fact]
    public void ACyclicTypeIsRefusedBeforeTheSerializerRuns()
    {
        var bytes = new ManagedValueBytes();
        var lone  = new Node();                  // ⛔ not even cyclic as an INSTANCE…

        Assert.Null(bytes.TryGetBytes(lone));    // …but its TYPE can reach itself
        Assert.True(ManagedValueBytes.IsNotComparable(typeof(Node)));
    }

    /// <summary>
    /// ⚠⚠ <b>The conservatism is real and is reported rather than hidden:</b> a TREE-shaped type is
    /// fenced even when the instance in hand is perfectly acyclic. ⭐ The fence must be a property of
    /// the TYPE — the serializer is compiled per type, and an instance check would have to run the
    /// dangerous code to find out. ⇒ ⛔ such a row never highlights.
    /// </summary>
    [Fact]
    public void AnAcyclicTreeInstanceIsStillFencedBecauseItsTypeCouldRecurse()
    {
        var bytes = new ManagedValueBytes();
        var leaf  = new Node { Next = null };    // ⭐ genuinely acyclic

        Assert.Null(bytes.TryGetBytes(leaf));
    }

    /// <summary>
    /// ⭐ <b>The SIZE cap survives as a second, independent fence</b> — for a value that is huge but
    /// acyclic, where a dynamic check does work. ⛔ It is no longer the cycle fence.
    /// </summary>
    [Fact]
    public void AHugeAcyclicValueTripsTheSizeCap()
    {
        var bytes = new ManagedValueBytes();
        var huge  = new Bulk { Blob = new byte[ManagedValueBytes.MaxBytes + 1024] };

        Assert.Null(bytes.TryGetBytes(huge));
        Assert.True(ManagedValueBytes.IsNotComparable(typeof(Bulk)));
    }

    /// <summary>⭐ A null value is comparable and stable — ⛔ not an error, and not a fence trip.</summary>
    [Fact]
    public void ANullManagedValueIsComparable()
    {
        var bytes = new ManagedValueBytes();

        Assert.NotNull(bytes.TryGetBytes(null));
        Assert.Empty(bytes.TryGetBytes(null)!);
    }

    // ── fixtures ────────────────────────────────────────────────────────────

    private sealed class Payload
    {
        public int    Hp   { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class Node
    {
        public Node? Next { get; set; }
    }

    private sealed class Bulk
    {
        public byte[] Blob { get; set; } = Array.Empty<byte>();
    }

    private sealed class GetOnly
    {
        private readonly int _v;
        public GetOnly(int v) => _v = v;
        public int Computed => _v * 2;           // ⛔ get-only ⇒ skipped by the serializer
    }
}
