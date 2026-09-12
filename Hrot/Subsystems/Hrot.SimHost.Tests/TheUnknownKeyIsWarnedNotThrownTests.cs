using System;
using System.Collections.Generic;
using System.Text;
using Fdp.Core;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Patching;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>Q59-N4</c> — an unsupported attribute key is WARNED and IGNORED, on every path. Never a throw.</b>
///
/// <para>🔒 <b>User ruling, <c>2026-08-26</c>:</b> *"if about unsupported attribute name (key), this should be
/// logged as warning and ignored, no throw."*</para>
///
/// <para>🔴 <b>What it replaces: total silence.</b> 📐 Measured <c>2026-08-26</c>:
/// <c>{"GeoPosition":{"Heading":90.0}}</c> — the path a reader of the old <c>AttributeIds.GeoHeading</c> would
/// naturally guess — applied <b>nothing, with no exception and no log</b>. ⇒ an operator had no way to discover
/// the key was wrong. ⭐ The binary path had the same hole, behind a comment reading *"Unknown IDs: silently
/// skipped (forward-compatibility)"*.</para>
///
/// <para>⭐⭐ <b>The TOLERANCE is kept; only the SILENCE is fixed.</b> Ignoring unknown keys is what lets a
/// newer sender talk to an older node across a mixed-version cluster ⇒ ⛔ throwing would turn a
/// forward-compatible patch into a failed request. ⚠ Which is why every rail here asserts BOTH halves: no
/// throw, AND the good keys in the same payload still land.</para>
/// </summary>
public class TheUnknownKeyIsWarnedNotThrownTests
{
    // ══ ① the JSON→ECS path ═══════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>An unknown key does not throw, and does not stop the known keys beside it.</b>
    ///
    /// <para>⭐ The second half is the one that matters in production: a patch carrying one bad key must not
    /// lose the good ones. ⛔ A throw — or an early return — would do exactly that.</para>
    /// </summary>
    [Theory]
    [InlineData("{\"GeoPosition\":{\"Heading\":90.0},\"Name\":\"Kept\"}")]   // the natural wrong guess
    [InlineData("{\"NoSuchAttribute\":1,\"Name\":\"Kept\"}")]
    [InlineData("{\"Name\":\"Kept\",\"Nested\":{\"Deep\":{\"Thing\":true}}}")]
    public void AnUnknownKeyIsIgnoredAndTheKnownOnesStillApply(string json)
    {
        var (repo, e) = OwnedEntity();
        var compiler = AttributeCompilerFactory.Build(geoTransform: null);

        var ex = Record.Exception(
            () => compiler.Compile(json, compiler.CreatePatchContext(repo, e)));

        Assert.Null(ex);
        Assert.Equal("Kept", repo.GetComponent<Fdp.Core.EntityInfo>(e).Name);
    }

    // ══ ② the JSON→record edge ════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>Same on the edge: no throw, and the known key still emits its record.</b>
    ///
    /// <para>📌 Both paths deliberately — the whole of <c>AX-018</c> was one path behaving differently from
    /// the other, so adding a diagnostic to only one would repeat that mistake.</para>
    /// </summary>
    [Fact]
    public void AnUnknownKeyAtTheEdgeIsIgnoredAndTheKnownOneStillEmits()
    {
        var compiler = AttributeCompilerFactory.BuildEdgeCompiler();
        var emitter  = new CountingEmitter();

        var ex = Record.Exception(() => compiler.Compile(
            Encoding.UTF8.GetBytes("{\"NoSuchAttribute\":1,\"Name\":\"Kept\"}"), emitter));

        Assert.Null(ex);
        Assert.Equal(1, emitter.Count);
        Assert.Equal(AttributeIds.Name, emitter.LastId);
    }

    // ══ ③ the binary path — an unregistered AttributeId ═══════════════════════════

    /// <summary>
    /// ⭐⭐ <b>And an unregistered <c>AttributeId</c> is the binary equivalent of an unsupported key.</b>
    ///
    /// <para>⭐ Asserted with a KNOWN record beside the unknown one, so the rail proves the unknown was
    /// skipped rather than the whole batch abandoned.</para>
    /// </summary>
    [Fact]
    public void AnUnregisteredAttributeIdIsIgnoredAndTheKnownOneStillApplies()
    {
        var (repo, e) = OwnedEntity();
        var interpreter = AttributeCompilerFactory.BuildBinaryInterpreter(geoTransform: null);
        var patchCtx    = EcsPatchContext.Create(repo, e);
        var ctx         = interpreter.CreateContext(patchCtx);
        ctx.Repo = repo; ctx.Entity = e;

        var ex = Record.Exception(() => interpreter.Apply(ctx, new[]
        {
            new EntityAttributeChange { AttributeId = 9999, Value = AttributeValue.FromInt(1) },
            new EntityAttributeChange
            {
                AttributeId = AttributeIds.Name,
                Value       = AttributeValue.FromString("Kept"),
            },
        }));

        Assert.Null(ex);
        Assert.Equal("Kept", repo.GetComponent<Fdp.Core.EntityInfo>(e).Name);
    }

    // ══ ④ the diagnostic must not cost anything when nothing is unknown ═══════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE RAIL THAT KEEPS THE FIX HONEST: the quiet path stays allocation-free.</b>
    ///
    /// <para>⭐⭐ The warning builds a <c>string</c> from the key — necessarily. ⛔ So the risk is that the
    /// diagnostic leaks onto the hot path: capturing the property name eagerly, or materialising it before
    /// knowing whether the key is routed. ⇒ this asserts <b>zero bytes on this thread</b> for a fully-known
    /// payload, which is the steady state.</para>
    ///
    /// <para>⚠ Measured with <c>GC.GetAllocatedBytesForCurrentThread</c>, not <c>GetTotalMemory</c> — the
    /// latter counts every thread in the process, which is why two allocation rails in this repo were coin
    /// flips until they were corrected.</para>
    ///
    /// <para>⚠⚠ <b>A NUMERIC-only payload, and the first cut of this rail got that wrong.</b> 📐 It included
    /// <c>"Name":"A"</c> and measured <b>688 bytes</b> — because <c>reader.GetString()</c> legitimately
    /// allocates a string for a string attribute. ⇒ ⛔ the zero-allocation mandate has only ever applied to
    /// non-string paths *(the sibling rail <c>Compile_NonStringPath_ZeroAllocation</c> says so in its very
    /// name)*, so asserting zero over a string attribute was measuring the wrong thing.</para>
    ///
    /// <para>⚠⚠ <b>And the context is WARMED, which the second cut got wrong too.</b> 📐 A fresh
    /// <c>EcsPatchContext</c> measured <b>216 bytes</b> — its <c>HashSet</c> buckets, allocated on first
    /// insert. ⇒ ⛔ that is the cost of CREATING a context, not of the diagnostic, and conflating the two
    /// would make this rail assert something untrue about the code.</para>
    ///
    /// <para>📌 <b>It earned its keep on the way:</b> at 416 bytes it caught a REAL regression I had just
    /// introduced — <c>DescriptorOwnershipMap.GetDescriptorsForComponentId</c> returned <c>set.ToArray()</c>,
    /// allocating on <b>every component access</b>. ⭐ Fixed by storing <c>long[]</c> and merging at
    /// registration instead.</para>
    /// </summary>
    [Fact]
    public void TheDiagnosticCostsNothingWhenEveryKeyIsKnown()
    {
        var (repo, e) = OwnedEntity();
        var compiler = AttributeCompilerFactory.Build(geoTransform: null);
        var utf8 = Encoding.UTF8.GetBytes("{\"Heading\":90.0}");

        // warm-up so JIT and the patch context are outside the measured window
        for (int i = 0; i < 3; i++)
            compiler.Compile(utf8, compiler.CreatePatchContext(repo, e));

        // ⭐⭐ The context is warmed too, deliberately — see the remarks: a FRESH EcsPatchContext allocates
        //    its HashSet buckets on first use, which is inherent to creating one and has nothing to do with
        //    the diagnostic this rail is about.
        var ctx = compiler.CreatePatchContext(repo, e);
        compiler.Compile(utf8, ctx);

        long before = GC.GetAllocatedBytesForCurrentThread();
        compiler.Compile(utf8, ctx);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// ⭐⭐ <b>And the same key repeated many times warns ONCE.</b>
    ///
    /// <para>⚠ A sender repeating a bad key at 60 Hz would otherwise bury the log, and a buried warning is
    /// the same as no warning. ⭐ Asserted through allocation: the first unknown key builds a string, the
    /// rest hit the seen-set and must not. ⛔ Asserting on log output would need a logging harness this
    /// project does not have; ⚠ stated rather than glossed — this is an indirect proof of the dedup.</para>
    /// </summary>
    [Fact]
    public void ARepeatedUnknownKeyIsOnlyReportedOnce()
    {
        var (repo, e) = OwnedEntity();
        var compiler = AttributeCompilerFactory.Build(geoTransform: null);
        var utf8 = Encoding.UTF8.GetBytes("{\"NoSuchAttribute\":1}");

        // first call reports it (and allocates the key string + the log message)
        compiler.Compile(utf8, compiler.CreatePatchContext(repo, e));
        for (int i = 0; i < 3; i++)
            compiler.Compile(utf8, compiler.CreatePatchContext(repo, e));

        var ctx = compiler.CreatePatchContext(repo, e);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            compiler.Compile(utf8, ctx);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // ⭐ 100 repeats of an already-reported key: only the seen-set probe's key string, no log messages.
        //   ⚠ Not asserted as zero — the probe must build the string to look it up. Bounded instead.
        Assert.True(allocated < 100 * 128,
            $"a repeated unknown key allocated {allocated} bytes over 100 compiles — the warn-once " +
            "dedup is not holding, so the log will be flooded.");
    }

    // ══ helpers ══════════════════════════════════════════════════════════════════

    private static (EntityRepository, Entity) OwnedEntity()
    {
        var repo = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(repo);
        repo.RegisterComponent<Fdp.Core.EntityInfo>();
        repo.RegisterComponent<EgressPublicationState>();
        FakeDescriptorTranslator.ContributeProductionPairings(repo);

        var e = repo.CreateEntity();
        repo.AddComponent(e, default(Fdp.Core.EntityInfo));
        repo.AddComponent(e, default(SimTransform));
        repo.SetAuthority<Fdp.Core.EntityInfo>(e, true);
        repo.SetAuthority<SimTransform>(e, true);
        return (repo, e);
    }

    private sealed class CountingEmitter : IAttributeRecordEmitter
    {
        public int Count { get; private set; }
        public ushort LastId { get; private set; }

        private void Seen(ushort id) { Count++; LastId = id; }

        public void EmitInt32(ushort id, int v, short s1 = 0, short s2 = 0)      => Seen(id);
        public void EmitInt64(ushort id, long v, short s1 = 0, short s2 = 0)     => Seen(id);
        public void EmitFloat32(ushort id, float v, short s1 = 0, short s2 = 0)  => Seen(id);
        public void EmitFloat64(ushort id, double v, short s1 = 0, short s2 = 0) => Seen(id);
        public void EmitBool(ushort id, bool v, short s1 = 0, short s2 = 0)      => Seen(id);
        public void EmitString(ushort id, string? v, short s1 = 0, short s2 = 0) => Seen(id);
    }
}
