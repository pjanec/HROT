using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Modules.Geographic.Systems;
using Fdp.Toolkit.Replication.Patching;
using Hrot.NED.Messages;
using Fdp.Toolkit.Replication.Attributes;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐⭐⭐ <b>Axis-B first cut — <c>UXI-30</c>'s authority gate, the heading attribute, and the
/// owned-vs-unowned write router.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §2 *(the routing model)* · §6 ①②③ · §7.</para>
///
/// <para>🔴🔴 <b><c>UXI-30</c>'s stated premise needed correcting, and measuring it is what found that.</b>
/// The design says *"<c>BinaryInterpreter.Apply</c> — <b>no authority gate</b> … dispatches every record
/// to its handler with no <c>CanWrite</c>"*. 📐 Measured <c>2026-08-25</c>: <b>both</b> production
/// installers — <c>SimTransformAttributeInstaller</c> and <c>EntityDataAttributeInstaller</c> — ALREADY
/// opened every handler with <c>if (!ctx.PatchContext.CanWrite&lt;T&gt;()) return;</c>. ⇒ ⭐ the binary
/// path WAS gated, per handler, exactly as the JSON path is *(whose gate also lives in the typed
/// <c>ValueInvoker&lt;T&gt;</c>, not in the router)*.</para>
///
/// <para>⭐⭐⭐ <b>So the real defect is that the gate was PER-INSTALLER and therefore FORGETTABLE</b> — and
/// this slice adds a third installer, which is exactly when that bites. ⇒ the gate moved into
/// <c>RegisterHandler&lt;TComponent&gt;</c>, and the rails below assert it on a handler that contains
/// <b>no guard of its own</b>. ⛔ That is the distinction: they prove the REGISTRATION gates, not that an
/// author remembered to.</para>
/// </summary>
public sealed class TheGateCannotBeForgottenTests
{
    // ── the harness ─────────────────────────────────────────────────────────────

    /// <summary>An <see cref="IEntityPatchContext"/> whose authority answer is a constructor argument.</summary>
    private sealed class AuthorityPatchContext : IEntityPatchContext
    {
        private readonly bool _canWrite;
        private readonly Dictionary<Type, object> _managed = new();

        public AuthorityPatchContext(bool canWrite) => _canWrite = canWrite;

        public int UnmanagedFetches { get; private set; }

        public ref T GetUnmanagedComponent<T>() where T : struct
        {
            UnmanagedFetches++;
            return ref Holder<T>.Value;
        }

        public T GetManagedComponent<T>() where T : class
        {
            if (!_managed.TryGetValue(typeof(T), out var raw))
            {
                raw = Activator.CreateInstance<T>()!;
                _managed[typeof(T)] = raw;
            }
            return (T)raw;
        }

        public void FlushDirtyMarks() { }
        public bool CanWrite<T>() where T : struct => _canWrite;
        public bool CanWriteManaged<T>() where T : class => _canWrite;

        private static class Holder<T> where T : struct { public static T Value; }
    }

    // ⭐⭐ AX-005a / R-134 — the FDP-INTERNAL record. ⛔ It used to be a DDS `AttributeRecord`; the
    //    installers no longer speak that type, so neither does this rail.
    private static EntityAttributeChange HeadingRecord(double deg)
        => EntityAttributeChange.Double(AttributeIds.Heading, deg);

    // ══ ① the gate, asserted on a handler with NO guard of its own ══════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE <c>UXI-30</c> RAIL.</b> The handler body is a bare counter — it contains no
    /// <c>CanWrite</c> check whatsoever. ⇒ if it does not run for an unowned component, the GATE did that,
    /// not the handler.
    ///
    /// <para>⛔ Red by registering through the untyped <c>RegisterHandler</c> overload — which is precisely
    /// the mistake a future installer would make.</para>
    /// </summary>
    [Fact]
    public void ATypedHandlerNeverRunsForAnUnownedComponent()
    {
        int ran = 0;

        var interpreter = new BinaryInterpreterBuilder<EntityAttributeChange>(r => r.AttributeId)
            .RegisterHandler<SimTransform>(AttributeIds.Heading, (_, _) => ran++)
            .Build();

        // ── unowned ⇒ the handler must not run ──
        var denied = new AuthorityPatchContext(canWrite: false);
        interpreter.Apply(interpreter.CreateContext(denied), new[] { HeadingRecord(90) });
        Assert.Equal(0, ran);

        // ── owned ⇒ it must ──
        var allowed = new AuthorityPatchContext(canWrite: true);
        interpreter.Apply(interpreter.CreateContext(allowed), new[] { HeadingRecord(90) });
        Assert.Equal(1, ran);
    }

    /// <summary>
    /// ⚠⚠ <b>The characterization that makes the rail above meaningful: the UNTYPED overload does NOT
    /// gate.</b> ⭐ Kept deliberately — the untyped registration is still the right tool for a handler that
    /// touches no ECS component *(a pure scratchpad accumulator, say)*, so this is a documented boundary,
    /// ⛔ not a hole. 📌 It is also what makes the typed overload's red-proof possible.
    /// </summary>
    [Fact]
    public void TheUntypedOverloadIsDeliberatelyUngated()
    {
        int ran = 0;

        var interpreter = new BinaryInterpreterBuilder<EntityAttributeChange>(r => r.AttributeId)
            .RegisterHandler(AttributeIds.Heading, (_, _) => ran++)
            .Build();

        interpreter.Apply(interpreter.CreateContext(new AuthorityPatchContext(canWrite: false)),
                          new[] { HeadingRecord(90) });

        Assert.Equal(1, ran);
    }

    /// <summary>
    /// ⭐⭐ <b>And an unowned record touches NO ECS memory</b> — the gate must skip before
    /// <c>GetUnmanagedComponent</c>, not after. ⛔ A gate that fetched the component first would already
    /// have taken a write ref on a chunk this node does not own.
    /// </summary>
    [Fact]
    public void AnUnownedRecordNeverFetchesTheComponent()
    {
        var denied = new AuthorityPatchContext(canWrite: false);

        var interpreter = AttributeCompilerFactory.BuildBinaryInterpreter(geoTransform: null);
        interpreter.Apply(interpreter.CreateContext(denied), new[] { HeadingRecord(123) });

        Assert.Equal(0, denied.UnmanagedFetches);
    }

    // ══ ② the heading attribute — routed to the EXISTING conversion ═════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The production interpreter applies <c>Heading</c> to <c>SimTransform.Rotation</c>, and it
    /// does so through the conversion that already existed.</b>
    ///
    /// <para>⭐ Asserted against <see cref="SimTransformBridgeSystem.HeadingDegToRotation"/> directly: if
    /// the installer ever grew its own compass math, this rail would catch the divergence — which is the
    /// point of routing rather than copying.</para>
    /// </summary>
    [Theory]
    [InlineData(0.0)]     // North
    [InlineData(90.0)]    // East
    [InlineData(180.0)]   // South
    [InlineData(270.0)]   // West
    [InlineData(45.0)]
    public void HeadingAppliesTheExistingCompassConversion(double headingDeg)
    {
        var ctx = new AuthorityPatchContext(canWrite: true);
        var interpreter = AttributeCompilerFactory.BuildBinaryInterpreter(geoTransform: null);

        interpreter.Apply(interpreter.CreateContext(ctx), new[] { HeadingRecord(headingDeg) });

        var expected = SimTransformBridgeSystem.HeadingDegToRotation((float)headingDeg);
        ref var st = ref ctx.GetUnmanagedComponent<SimTransform>();

        Assert.Equal(expected.X, st.Rotation.X, 5);
        Assert.Equal(expected.Y, st.Rotation.Y, 5);
        Assert.Equal(expected.Z, st.Rotation.Z, 5);
        Assert.Equal(expected.W, st.Rotation.W, 5);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The gizmo's yaw→compass step and the installer's compass→rotation step are INVERSES</b> —
    /// so a rotation committed through the attribute path lands on the same quaternion the old direct
    /// write produced.
    ///
    /// <para>⛔⛔ This is the rail for the failure that would be SILENT: a sign or offset error here
    /// rotates entities the wrong way with no error anywhere. 📐 Algebraically
    /// <c>HeadingDegToRotation(YawRadToCompassDeg(y)) = FromYaw(y)</c>; asserted rather than trusted.</para>
    /// </summary>
    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.7853982f)]     // 45°
    [InlineData(1.5707964f)]     // 90°
    [InlineData(3.1415927f)]     // 180°
    [InlineData(-1.5707964f)]    // -90°
    public void TheGizmosCompassStepIsTheInstallersInverse(float yawRad)
    {
        float compassDeg = SimMath.YawRadToCompassDeg(yawRad);

        var viaAttribute = SimTransformBridgeSystem.HeadingDegToRotation(compassDeg);
        var viaDirect    = SimMath.FromYaw(yawRad);

        // ⚠ Compared as a FACING VECTOR, not component-wise: q and -q are the same rotation, so a
        //   component-wise compare can fail on a sign convention that is physically identical.
        var a = Vector3.Transform(Vector3.UnitX, viaAttribute);
        var b = Vector3.Transform(Vector3.UnitX, viaDirect);

        Assert.Equal(b.X, a.X, 4);
        Assert.Equal(b.Y, a.Y, 4);
        Assert.Equal(b.Z, a.Z, 4);
    }

    // ══ the INVERSE — the anti-regression the design calls the trap ════════════

    /// <summary>
    /// ⭐⭐⭐ <b>REPLICATION INGRESS MUST NOT BE GATED — and it structurally cannot be.</b>
    ///
    /// <para>🔒 <c>HROT-PROGRAMMERS-GUIDE</c> Part 0 rule 8 and the design both name this as THE trap:
    /// replication ingress writes unowned components <b>by design</b> — that is how a ghost receives its
    /// owner's state — so applying <c>UXI-30</c>'s gate there would stop every ghost updating,
    /// repo-wide.</para>
    ///
    /// <para>📐 Measured <c>2026-08-25</c>: <c>GeoSpatialIngressTranslator</c> writes through a
    /// <b>command buffer</b> (<c>cmd.SetComponent</c>) and does so only when
    /// <c>!repo.HasAuthority&lt;SimTransform&gt;(entity)</c> — i.e. it writes exactly the unowned case, and
    /// it never touches <c>BinaryInterpreter</c> at all. ⇒ ⭐ this slice could not have gated it even by
    /// mistake.</para>
    ///
    /// <para>⭐⭐ <b>So this rail guards the FUTURE, not the present:</b> it fails the day someone routes a
    /// replication ingress translator through the change-request builder — the plausible *"let us unify
    /// the two write paths"* refactor. ⚠ A name/text scan is the honest instrument for that structural
    /// claim; the behavioural half is the shipped <c>GeoSpatialIngressTranslatorTests</c>, which asserts a
    /// ghost still receives owner state.</para>
    /// </summary>
    [Fact]
    public void NoReplicationIngressTranslatorRoutesThroughTheGatedBuilder()
    {
        var root = RepoRoot();
        Assert.NotNull(root);

        var ingressDir = System.IO.Path.Combine(
            root!, "Hrot", "Network", "Hrot.Network.NED", "Replication");
        Assert.True(System.IO.Directory.Exists(ingressDir), ingressDir);

        var offenders = new List<string>();

        foreach (var file in System.IO.Directory.EnumerateFiles(
                     ingressDir, "*Ingress*.cs", System.IO.SearchOption.AllDirectories))
        {
            var text = System.IO.File.ReadAllText(file);
            if (text.Contains("BinaryInterpreterBuilder", StringComparison.Ordinal) ||
                text.Contains("RegisterHandler", StringComparison.Ordinal))
                offenders.Add(System.IO.Path.GetRelativePath(root!, file).Replace('\\', '/'));
        }

        Assert.Empty(offenders);
    }

    /// <summary>⭐ Walks up to the checkout root, the same probe the other structural rails use.</summary>
    private static string? RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(dir, "IOS-IG-SimHost.sln"))) return dir;
            dir = System.IO.Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    // ══ ③ the write router ═════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Owned ⇒ the write lands DIRECTLY, and the value is right.</b>
    /// ⭐ Uses a real <see cref="EntityRepository"/> so the authority answer is the kernel's own, not a stub's.
    /// </summary>
    [Fact]
    public void AnOwnedWriteGoesStraightIntoEcs()
    {
        using var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();

        var e = repo.CreateEntity();
        repo.SetComponent(e, new SimTransform { Rotation = Quaternion.Identity });

        // ⭐⭐⭐ AUTHORITY IS EXPLICIT, and this line is the production shape, not test scaffolding.
        //    📐 Measured `2026-08-25`: `HasAuthority` reads `EntityHeader.AuthorityMask`, which nothing
        //    sets by default. The spawner grants it — `SimHostNodeBootstrapper:287` does exactly this for
        //    an entity it owns — and replication grants/revokes it via DeferredTakeoverSystem /
        //    OwnershipUpdateTranslator. ⇒ "owned" means "someone set the bit", never "I created it".
        //    ⚠ See TheWriterTreatsAnUngrantedComponentAsUnowned for why that matters beyond this fixture.
        repo.SetAuthority<SimTransform>(e, true);

        var writer = new AttributeEntityComponentWriter(
            repo, AttributeCompilerFactory.BuildBinaryInterpreter(geoTransform: null));

        var route = writer.Write(e, AttributeIds.Heading, 90.0);

        Assert.Equal(EntityWriteRoute.Direct, route);

        var expected = SimTransformBridgeSystem.HeadingDegToRotation(90f);
        ref readonly var st = ref repo.GetComponentRO<SimTransform>(e);
        Assert.Equal(expected.W, st.Rotation.W, 4);
    }

    /// <summary>
    /// 🔴🔴 <b>THE FINDING THIS SLICE SHOULD NOT SHIP WITHOUT SAYING: an entity that HAS the component but
    /// was never GRANTED authority routes as UNOWNED.</b>
    ///
    /// <para>📐 Measured <c>2026-08-25</c>: <c>SetAuthority</c> has production callers in
    /// <b><c>Hrot.SimHost</c></b> *(its bootstrapper, for entities it spawns)* and in the <b>NED
    /// replication path</b> *(<c>DeferredTakeoverSystem</c>, <c>OwnershipUpdateTranslator</c>)</b> — and
    /// <b>nowhere else</b>. ⛔ <c>Hrot.Editor</c> never calls it.</para>
    ///
    /// <para>⚠⚠ <b>So the design's routing model rests on a bit that not every host sets.</b> On a host
    /// that creates entities without granting authority, every attribute write looks unowned and becomes a
    /// change-request — or, with no request sink, a refusal. ⇒ ⭐ this is why the gizmo's writer is
    /// OPT-IN and the existing SimHost call site keeps its direct write: switching it wholesale would
    /// change behaviour on hosts whose entities carry no authority bits.</para>
    ///
    /// <para>⭐ Railed as a CHARACTERIZATION, not a defect: it is the authority model working as built. ⛔ But
    /// it is a premise the design did not state, and a later slice that wires the writer everywhere must
    /// grant authority on the creating host first.</para>
    /// </summary>
    [Fact]
    public void TheWriterTreatsAnUngrantedComponentAsUnowned()
    {
        using var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();

        var e = repo.CreateEntity();
        repo.SetComponent(e, new SimTransform { Rotation = Quaternion.Identity });
        // ⛔ deliberately NO SetAuthority — the shape a host that never grants it produces.

        var published = new List<(Entity, ushort, double)>();
        var writer = new AttributeEntityComponentWriter(
            repo,
            AttributeCompilerFactory.BuildBinaryInterpreter(geoTransform: null),
            publishRequest: (ent, recs) => published.Add((ent, recs[0].AttributeId, recs[0].Value.DoubleValue)));

        Assert.Equal(EntityWriteRoute.Requested, writer.Write(e, AttributeIds.Heading, 90.0));
        Assert.Single(published);
    }

    /// <summary>
    /// ⚠ <b>A dead entity is REFUSED, not silently dropped</b> — the caller gets to say so.
    /// </summary>
    [Fact]
    public void ADeadEntityIsRefused()
    {
        using var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();

        var e = repo.CreateEntity();
        repo.DestroyEntity(e);

        var writer = new AttributeEntityComponentWriter(
            repo, AttributeCompilerFactory.BuildBinaryInterpreter(geoTransform: null));

        Assert.Equal(EntityWriteRoute.Refused, writer.Write(e, AttributeIds.Heading, 90.0));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>An entity WITHOUT the component is not owned here ⇒ the write becomes a REQUEST</b>, carrying
    /// the attribute id and value for the owner to apply.
    ///
    /// <para>⭐ This is the distributed case in miniature: on a real cluster a ghost the local node does
    /// not own produces exactly this branch. ⚠ Modelled here by absence rather than by an authority mask
    /// so the rail needs no cluster — and the branch under test is the same one.</para>
    /// </summary>
    [Fact]
    public void AnUnownedWriteBecomesAChangeRequest()
    {
        using var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();

        var e = repo.CreateEntity();      // ⚠ no SimTransform ⇒ nothing local to write

        var published = new List<(Entity Entity, ushort Id, double Value)>();

        var writer = new AttributeEntityComponentWriter(
            repo,
            AttributeCompilerFactory.BuildBinaryInterpreter(geoTransform: null),
            publishRequest: (ent, recs) => published.Add((ent, recs[0].AttributeId, recs[0].Value.DoubleValue)));

        var route = writer.Write(e, AttributeIds.Heading, 137.5);

        Assert.Equal(EntityWriteRoute.Requested, route);
        Assert.Single(published);
        Assert.Equal(e, published[0].Entity);
        Assert.Equal(AttributeIds.Heading, published[0].Id);
        Assert.Equal(137.5, published[0].Value, 6);
    }

    /// <summary>
    /// ⚠⚠ <b>With no request sink, an unowned write is REFUSED — it does not silently succeed.</b>
    /// ⭐ This is the honest answer for a host with no request egress, and it is why the route is a
    /// three-valued result rather than a <c>bool</c>: ⛔ *"written"* and *"nobody to ask"* must not
    /// collapse into one answer.
    /// </summary>
    [Fact]
    public void AnUnownedWriteWithNoSinkIsRefusedNotSilentlyDropped()
    {
        using var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();

        var e = repo.CreateEntity();      // no SimTransform

        var writer = new AttributeEntityComponentWriter(
            repo, AttributeCompilerFactory.BuildBinaryInterpreter(geoTransform: null));

        Assert.Equal(EntityWriteRoute.Refused, writer.Write(e, AttributeIds.Heading, 90.0));
    }
}
