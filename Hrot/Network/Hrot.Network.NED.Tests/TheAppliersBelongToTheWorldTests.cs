using System.Reflection;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Attributes;
using Xunit;

namespace Hrot.Network.NED.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>AX-016</c> — the attribute appliers are ONE PER WORLD and NOT bound to any network.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §15. 🔒 User ruling: *"is instantiating the same
/// interpreter in every network factory the right solution? the interpreter should not be bound to any
/// network."*</para>
///
/// <para>⚠⚠ <b>SUPERSEDES <c>TheBinaryArmIsWiredInProductionTests</c>, and why is worth keeping.</b> Those
/// rails asserted that the request system's <b>constructor</b> produced a non-null interpreter — the right
/// control for the defect of that day *(<c>AX-012</c>: a production caller omitting an argument)*. ⛔ But they
/// pinned the wrong thing: they made a per-network-stack instance the asserted contract. ⭐ The appliers now
/// belong to the WORLD, so these rails assert that — and <c>AX-012</c>'s silent-null arm becomes
/// <b>unrepresentable</b> rather than merely detected.</para>
/// </summary>
public class TheAppliersBelongToTheWorldTests
{
    /// <summary>⚠ Domains kept low and distinct — CycloneDDS ports are `7400 + 250 × domainId`, ceiling ≈ 232.</summary>
    private const int TestDomain = 171;

    private static EntityRepository WorldWithGeo()
    {
        var repo = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(repo);

        var geo = new WGS84Transform();
        geo.SetOrigin(52.52, 13.405, 0.0);
        repo.SetSingletonManaged<IGeographicTransform>(geo);
        return repo;
    }

    // ══ the world owns them ═══════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Repeated resolution yields the SAME instance.</b>
    ///
    /// <para>🔴 <c>EntityWriteRouter.For(repo)</c> is called at EVERY gizmo construction. Before
    /// <c>AX-016</c> that built a fresh interpreter each time — one per gizmo, each with its own scratchpad,
    /// and N chances for two to be built from different geographic transforms and convert the same attribute
    /// differently.</para>
    /// </summary>
    [Fact]
    public void TheWorldHasExactlyOneInterpreterHoweverOftenItIsAskedFor()
    {
        using var repo = WorldWithGeo();

        var a = AttributeInterpreterProvider.GetOrCreate(repo);
        var b = AttributeInterpreterProvider.GetOrCreate(repo);

        Assert.Same(a, b);
        Assert.Same(a, AttributeInterpreterProvider.GetOrCreate(repo));
    }

    /// <summary>⭐⭐ …and the JSON arm follows the same rule, so the two cannot drift apart *(<c>AX-014</c>)*.</summary>
    [Fact]
    public void TheJsonCompilerIsWorldScopedToo()
    {
        using var repo = WorldWithGeo();

        Assert.Same(AttributeInterpreterProvider.GetOrCreateJson(repo),
                    AttributeInterpreterProvider.GetOrCreateJson(repo));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>NOT BOUND TO ANY NETWORK: no participant, no factory, no translators — both appliers resolve.</b>
    ///
    /// <para>🔴 This is the property that was asked for, and the one the old design could not have.
    /// 📐 Measured <c>2026-08-26</c>: <c>OfflineNetworkFactory.CreateSimHostAttributeUpdateSystems()</c>
    /// returns an EMPTY array ⇒ before <c>AX-016</c> an offline host had no attribute applier at all.
    /// ⛔ Nothing in this rail touches CycloneDDS.</para>
    /// </summary>
    [Fact]
    public void AWorldWithNoNetworkAtAllStillHasBothAppliers()
    {
        using var repo = WorldWithGeo();

        Assert.NotNull(AttributeInterpreterProvider.GetOrCreate(repo));
        Assert.NotNull(AttributeInterpreterProvider.GetOrCreateJson(repo));
    }

    /// <summary>
    /// ⚠ <b>A world with NO geodetic frame still gets appliers</b> — they simply carry no <c>Geo*</c>
    /// handlers *(<c>AX-010</c>)*. ⛔ It must not throw: <c>GetSingletonManaged</c> does when unset, which is
    /// the trap that reddened the <c>AX-005</c> cluster rail on the IG.
    /// </summary>
    [Fact]
    public void AWorldWithNoGeographicTransformStillGetsAppliers()
    {
        using var repo = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(repo);

        Assert.NotNull(AttributeInterpreterProvider.GetOrCreate(repo));
        Assert.NotNull(AttributeInterpreterProvider.GetOrCreateJson(repo));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The write router SHARES the world's instance — it does not build its own.</b>
    /// ⭐ Asserted on the CONSTRUCTED OBJECT, which is the control that caught <c>AX-012</c>.
    /// </summary>
    [Fact]
    public void TheWriteRouterSharesTheWorldsInterpreter()
    {
        using var repo = WorldWithGeo();

        var expected = AttributeInterpreterProvider.GetOrCreate(repo);
        Assert.Same(expected, Held(EntityWriteRouter.For(repo)));
    }

    /// <summary>
    /// ⭐⭐ <b>Two routers built independently share ONE interpreter</b> — the per-gizmo duplication railed as
    /// gone rather than described as gone.
    /// </summary>
    [Fact]
    public void TwoRoutersOnTheSameWorldShareOneInterpreter()
    {
        using var repo = WorldWithGeo();

        Assert.Same(Held(EntityWriteRouter.For(repo)), Held(EntityWriteRouter.For(repo)));
    }

    // ══ the DDS system resolves, rather than being handed one ══════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The request system resolves BOTH arms from the world on its first <c>Execute</c>.</b>
    ///
    /// <para>⭐ This replaces *"the constructor produced one"*. ⛔ The interpreter is no longer the network
    /// stack's to provide, so asserting it at construction would pin the very coupling <c>AX-016</c> removes.
    /// ⚠ The property that matters is unchanged: once the system has run, neither arm is null — so
    /// <c>AX-012</c>'s silent drop cannot recur.</para>
    /// </summary>
    [Fact]
    public void TheRequestSystemResolvesBothArmsFromTheWorldOnFirstExecute()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var repo        = WorldWithGeo();

        var system = new Hrot.Map.Common.Systems.UpdateEntityAttributeRequestSystem(
            participant, new NetworkEntityMap(), repo.GetSingletonManaged<IGeographicTransform>());

        system.Execute(repo, 0.016f);

        Assert.Same(AttributeInterpreterProvider.GetOrCreate(repo),     Field(system, "_binaryInterpreter"));
        Assert.Same(AttributeInterpreterProvider.GetOrCreateJson(repo), Field(system, "_jsonCompiler"));
    }

    /// <summary>
    /// ⭐⭐ <b>…and an explicit override still wins</b> — ⛔ the world default must not have removed the seam.
    /// ⚠ Not decoration: <c>SimHostAppTests</c> passes its own JSON compiler.
    /// </summary>
    [Fact]
    public void AnExplicitOverrideStillWins()
    {
        using var participant = new DdsParticipant(TestDomain + 1);
        using var repo        = WorldWithGeo();

        var geo       = repo.GetSingletonManaged<IGeographicTransform>();
        var ownJson   = Fdp.Toolkit.Replication.Attributes.AttributeCompilerFactory.Build(geo);
        var ownBinary = Fdp.Toolkit.Replication.Attributes.AttributeCompilerFactory.BuildBinaryInterpreter(geo);

        var system = new Hrot.Map.Common.Systems.UpdateEntityAttributeRequestSystem(
            participant, new NetworkEntityMap(), geo, ownJson, default, ownBinary);

        system.Execute(repo, 0.016f);

        Assert.Same(ownJson,   Field(system, "_jsonCompiler"));
        Assert.Same(ownBinary, Field(system, "_binaryInterpreter"));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static object? Held(IEntityComponentWriter writer) => Field(writer, "_interpreter");

    private static object? Field(object target, string name)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                 ?.GetValue(target);
}
