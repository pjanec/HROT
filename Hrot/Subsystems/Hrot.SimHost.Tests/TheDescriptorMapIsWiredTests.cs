using System;
using System.Linq;
using Fdp.Core;
using Fdp.Network.Cyclone.Systems;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Patching;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>Q59-E</c> — the guard that makes deleting FDP's descriptor vocabulary SAFE.</b>
///
/// <para>📄 <c>docs/blueprints/Architect_Question_59_…md</c> §7 · §9.3.</para>
///
/// <para>🔴🔴 <b>THE RISK THIS FILE EXISTS TO CONTAIN.</b> Dirty-marking used to come from a compile-time
/// constant in the JSON routing table. It now comes from a map populated at RUNTIME from the network layer's
/// translators. ⇒ ⛔ <b>if that population ever fails to happen, entity renames stop republishing —
/// silently, with no exception</b>, which is <c>AX-015</c> all over again. ⭐ So the wiring is railed, not
/// trusted.</para>
///
/// <para>⭐⭐ <b>Two independent claims:</b> ① the wiring happens WITHOUT any host remembering to call it
/// *(the <c>CycloneEgressSystem</c> hook)*; ② every component the attribute vocabulary writes is COVERED by
/// some translator — 📌 the invariant that replaces the design's too-broad *"every translator must
/// declare <c>TargetComponentIds</c>"*.</para>
/// </summary>
public class TheDescriptorMapIsWiredTests
{
    // ══ ① the wiring cannot be forgotten ══════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Executing a <see cref="CycloneEgressSystem"/> populates the world's map — no host call.</b>
    ///
    /// <para>⭐⭐ This is the "assert on the CONSTRUCTED OBJECT" rail. ⛔ Asserting that some registrar's
    /// SOURCE contains a call is the weak form this codebase has been burned by; ⭐ this constructs the real
    /// system, Executes it against a real world, and reads the map back.</para>
    ///
    /// <para>📌 <b>Why <c>CycloneEgressSystem</c> and not a module or a host.</b> Measured
    /// <c>2026-08-26</c>: <c>CycloneNetworkModule</c> is <b>never instantiated in production</b>, and the
    /// translator lists are assembled in 4+ host-side places *(a main pack plus a gizmo pack per host)*. ⇒ the
    /// egress system is the ONE type that already has both the translators and the world.</para>
    /// </summary>
    [Fact]
    public void ExecutingTheEgressSystemPopulatesTheWorldsMap()
    {
        using var repo = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(repo);

        // ⭐ Before: nothing declared for this world.
        Assert.Empty(AttributeInterpreterProvider.GetDescriptorMap(repo)
            .GetDescriptorsForComponentId(GlobalComponentIds.EntityInfo).ToArray());

        var system = new CycloneEgressSystem(new Fdp.Interfaces.INetworkTranslator[]
        {
            new FakeDescriptorTranslator(
                (long)Hrot.NED.Descriptors.EDescriptorType.dtEntityInfo,
                GlobalComponentIds.EntityInfo),
        });

        system.Execute(repo, 0.016f);

        // ⭐⭐ After: the pairing arrived, without any host being asked to remember anything.
        Assert.Equal(
            new[] { (long)Hrot.NED.Descriptors.EDescriptorType.dtEntityInfo },
            AttributeInterpreterProvider.GetDescriptorMap(repo)
                .GetDescriptorsForComponentId(GlobalComponentIds.EntityInfo).ToArray());
    }

    /// <summary>
    /// ⭐⭐ <b>Several egress systems per world UNION their contributions.</b>
    ///
    /// <para>📐 Measured: a host registers a main translator pack AND a gizmo pack, at different points. ⇒ ⛔ a
    /// "set the map" API would have the second silently erase the first, so this pins the additive
    /// behaviour.</para>
    /// </summary>
    [Fact]
    public void SeveralEgressSystemsUnionTheirContributions()
    {
        using var repo = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(repo);

        new CycloneEgressSystem(new Fdp.Interfaces.INetworkTranslator[]
        {
            new FakeDescriptorTranslator(11L, GlobalComponentIds.EntityInfo),
        }).Execute(repo, 0.016f);

        new CycloneEgressSystem(new Fdp.Interfaces.INetworkTranslator[]
        {
            new FakeDescriptorTranslator(22L, GlobalComponentIds.EntityInfo),
        }).Execute(repo, 0.016f);

        Assert.Equal(
            new[] { 11L, 22L },
            AttributeInterpreterProvider.GetDescriptorMap(repo)
                .GetDescriptorsForComponentId(GlobalComponentIds.EntityInfo).ToArray());
    }

    // ══ ①c AX-022 — the property that makes multi-source contribution SAFE ════════

    /// <summary>
    /// ⭐⭐⭐ <b><c>AX-022</c>'s load-bearing property: registration is IDEMPOTENT per (ordinal, component).</b>
    ///
    /// <para>⭐⭐ <c>AX-022</c> has <c>NedReplicationModule</c> publish its pairings into the same world map
    /// that <c>CycloneEgressSystem</c> feeds — so the SAME pair arrives from two sources, and on every tick
    /// the guard could in principle miss. ⇒ ⛔ if registration duplicated, a component would accumulate the
    /// same ordinal repeatedly and <c>SmartEgressUtil.MarkDirty</c> would be called N times for one change.
    /// ⭐ This pins the dedup, which is what makes contribution order and count irrelevant.</para>
    /// </summary>
    [Fact]
    public void RegisteringTheSamePairingTwiceIsIdempotent()
    {
        using var repo = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(repo);

        var map = AttributeInterpreterProvider.GetDescriptorMap(repo);

        for (int i = 0; i < 5; i++)
            map.RegisterFromTranslator(77L, new[] { GlobalComponentIds.EntityInfo });

        Assert.Equal(
            new[] { 77L },
            map.GetDescriptorsForComponentId(GlobalComponentIds.EntityInfo).ToArray());
    }

    /// <summary>
    /// ⭐⭐ <b>And two DIFFERENT ordinals on one component both survive — the multi-valued case.</b>
    ///
    /// <para>📐 Measured <c>2026-08-26</c>: real production data needs this — <c>SimTransform</c> is declared
    /// by <b>both</b> <c>BdcWorldPosTranslator</c> and <c>GeoSpatialEgressTranslator</c>. ⛔ A single-valued
    /// map would mark the wrong descriptor for one of them.</para>
    /// </summary>
    [Fact]
    public void TwoOrdinalsOnOneComponentBothSurvive()
    {
        using var repo = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(repo);

        var map = AttributeInterpreterProvider.GetDescriptorMap(repo);
        map.RegisterFromTranslator(2L,  new[] { GlobalComponentIds.SimTransform });
        map.RegisterFromTranslator(99L, new[] { GlobalComponentIds.SimTransform });
        map.RegisterFromTranslator(2L,  new[] { GlobalComponentIds.SimTransform });   // repeat

        Assert.Equal(
            new[] { 2L, 99L },
            map.GetDescriptorsForComponentId(GlobalComponentIds.SimTransform).ToArray());
    }

    /// <summary>
    /// ⭐⭐ <b><c>AX-022</c> — a <c>NedReplicationModule</c> tick against a real world is safe, and a
    /// participant-less module contributes nothing.</b>
    ///
    /// <para>⚠ <b>What this can and cannot prove, stated plainly.</b> With <c>participant: null</c> the
    /// module builds EMPTY translator packs, so its own ownership map is empty and there is nothing to
    /// publish — ⇒ this rail proves the tick is SAFE and correctly contributes nothing, ⛔ it does NOT prove
    /// a populated module publishes. ⭐ That needs a live DDS participant and is covered by the integration
    /// suite, not here.</para>
    /// </summary>
    [Fact]
    public void AParticipantLessReplicationModuleTicksSafelyAndContributesNothing()
    {
        using var repo = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(repo);

        var module = new Hrot.Network.Replication.NedReplicationModule(
            participant:          null,
            role:                 Hrot.Common.NodeRole.MuscleGround,
            entityMap:            new Fdp.Toolkit.Replication.Services.NetworkEntityMap(),
            geoTransform:         Hrot.Map.Common.HrotEnvironment.CreateGeoTransform(),
            eventBus:             new FdpEventBus(),
            localNodeId:          1,
            domainId:             0,
            tkbEntityTranslators: new System.Collections.Generic.List<Fdp.Interfaces.ITkbEntityTranslator>().AsReadOnly());

        var ex = Record.Exception(() => module.Tick(repo, 0.016f));

        Assert.Null(ex);
        Assert.Empty(AttributeInterpreterProvider.GetDescriptorMap(repo).CoveredComponentIds);
    }

    // ══ ② every written component is covered ══════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE INVARIANT: every component the attribute vocabulary writes is covered by a translator.</b>
    ///
    /// <para>⭐⭐ This is what replaces the design's first, too-broad idea *("every <c>ShouldPublish</c>-gating
    /// translator must declare <c>TargetComponentIds</c>")*. 📐 Measured: most of those translators get their
    /// marks from domain systems calling <c>MarkDirty</c> with an explicit ordinal, so a blanket rule would be
    /// noise. ⇒ ⭐ the claim that MATTERS is this one — an attribute whose component nothing covers can be
    /// applied and will never be republished.</para>
    ///
    /// <para>⚠ Checked against the PRODUCTION pairings *(the ones <c>EntityInfoEgressTranslator</c> and
    /// <c>GeoSpatialEgressTranslator</c> declare)*, mirrored by <see cref="FakeDescriptorTranslator"/>, because
    /// constructing the real DDS translators needs a live participant.</para>
    /// </summary>
    [Fact]
    public void EveryComponentTheVocabularyWritesIsCoveredByATranslator()
    {
        using var repo = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(repo);
        FakeDescriptorTranslator.ContributeProductionPairings(repo);

        var map = AttributeInterpreterProvider.GetDescriptorMap(repo);

        var uncovered = AttributeVocabulary.WrittenComponents
            .Where(t => map.GetDescriptorsForComponentId(
                            ComponentTypeRegistry.GetOrRegisterManaged(t)).IsEmpty)
            .Select(t => t.Name)
            .ToList();

        Assert.Empty(uncovered);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>And an EMPTY map is a legitimate state — the networkless host must still work.</b>
    ///
    /// <para>🔒 Standing constraint: the editor is *"a networkless, all-in-one in-process solution by
    /// design"*. ⇒ ⛔ an apply on a world with no translators must NOT throw and must simply mark nothing.
    /// ⚠ Railed because the obvious "guard against an empty map" instinct would break that host.</para>
    /// </summary>
    [Fact]
    public void AnApplyOnANetworklessWorldMarksNothingAndDoesNotThrow()
    {
        using var repo = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(repo);
        repo.RegisterComponent<EgressPublicationState>();
        // ⛔ deliberately NO translators contributed

        var e = repo.CreateEntity();
        repo.AddComponent(e, default(SimTransform));
        repo.SetAuthority<SimTransform>(e, true);

        var compiler = AttributeCompilerFactory.Build(geoTransform: null);
        compiler.Compile("{\"Heading\":90.0}", compiler.CreatePatchContext(repo, e));

        // ⭐ The write LANDED — a networkless host still applies attributes.
        Assert.NotEqual(default, repo.GetComponent<SimTransform>(e).Rotation);

        // ⭐⭐ And nothing was marked for republication, because there is nothing to republish.
        Assert.False(repo.HasManagedComponent<EgressPublicationState>(e));
    }
}
