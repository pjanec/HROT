using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// <b>ST-033 / ST-034 — the rails for uniform gizmo membership by reflection</b>
/// (<c>DESIGN_Uniform_Gizmo_Membership.md</c> §8.2 ④, §8.4).
///
/// <para><b>⚠ What happened to invariant A.</b> This file used to assert, per host, that every component
/// a host's <i>curated</i> family list required was registered by that host — <c>ST-023</c>, written when
/// each host named its own subset of registrars. <c>ST-031</c> dissolved that premise: there are no
/// curated lists any more. <c>GizmoReflectionRegistrar</c> discovers every projector and resolves each
/// one's component ids <b>immediately before registering it</b>, so "declared but unsatisfied" is no
/// longer a state the code can be in — it is closed by construction, not by a test.</para>
///
/// <para>⇒ The per-host profile table is retired rather than maintained as a fiction, and is replaced by
/// the two invariants that reflection actually needs:</para>
/// <list type="bullet">
///   <item><b>completeness</b> — everything in source is registered at runtime. This is the ONE real risk
///   reflection introduces: it sees only assemblies already LOADED, so a mode that never loads a
///   projector's assembly would silently declare less than the source contains, and no compile error
///   would say so.</item>
///   <item><b>non-bloat</b> — a gizmo-only component id must not reach the recorder schema. This is what
///   proves the <c>ST-027</c> correction: ids, not tables, and not recordable.</item>
/// </list>
/// </summary>
public sealed class GizmoSchemaFollowsDeclarationRails
{
    /// <summary>
    /// ⭐⭐ <b>COMPLETENESS (invariant B).</b> Every <c>[GizmoProjector]</c> the source declares is
    /// registered at runtime, and every host gets the same set.
    ///
    /// <para>⚠ Asserted against the <b>enumerated</b> set, never a hardcoded count: a seventh family — or a
    /// single new projector — must move this number, not be silently excluded. That is why the count comes
    /// from reflection over the attribute rather than a literal.</para>
    /// </summary>
    [Fact]
    public void EveryProjectorInSource_IsRegisteredAtRuntime()
    {
        var discovered = GizmoReflectionRegistrar.DiscoverProjectorTypes();

        Assert.True(discovered.Count > 0,
            "GizmoReflectionRegistrar found no [GizmoProjector] types at all. Either the attribute moved "
            + "or no gizmo assembly is loaded in this test process — both make every other assertion here "
            + "vacuous.");

        var statelessRegistry = new StatelessGizmoRegistry();
        var registered = GizmoReflectionRegistrar.RegisterAll(
            new GizmoRegistry(), statelessRegistry, new GizmoSettingsRegistry());

        var missing = discovered.Except(registered).ToArray();

        Assert.True(missing.Length == 0,
            $"{missing.Length} projector(s) were discovered but not registered:\n  "
            + string.Join("\n  ", missing.Select(t => t.FullName))
            + "\n⭐ Every discovered projector must end up registered — a projector that is found and then "
            + "dropped is worse than one that was never found, because nothing else will report it.");
    }

    /// <summary>
    /// ⭐⭐ <b>The families are uniform.</b> Reflection cannot produce a per-host subset by construction, so
    /// this pins the property the ruling actually asked for: every projector namespace present in the
    /// process is covered by the one registrar every host calls.
    ///
    /// <para>📌 This is the successor to the old per-host table. It says the same thing the ruling does —
    /// <i>"support all"</i> — without encoding a list that can drift out of step with the hosts.</para>
    /// </summary>
    [Fact]
    public void EveryProjectorNamespace_IsCoveredByTheOneRegistrar()
    {
        var discovered = GizmoReflectionRegistrar.DiscoverProjectorTypes();
        var namespaces = discovered
            .Select(t => t.Namespace ?? "<none>")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        // The count is reported, never asserted against a literal: a new family must show up here as a
        // changed message, not as a red that tempts someone to bump a number.
        Assert.True(namespaces.Length > 0, "No projector namespaces found.");

        var statelessRegistry = new StatelessGizmoRegistry();
        var registered = GizmoReflectionRegistrar.RegisterAll(
            new GizmoRegistry(), statelessRegistry, new GizmoSettingsRegistry());

        var covered = registered
            .Select(t => t.Namespace ?? "<none>")
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var uncovered = namespaces.Where(n => !covered.Contains(n)).ToArray();

        Assert.True(uncovered.Length == 0,
            $"These projector namespaces are present but not covered by GizmoReflectionRegistrar:\n  "
            + string.Join("\n  ", uncovered)
            + $"\n(families seen: {namespaces.Length} — {string.Join(", ", namespaces)})");
    }

    /// <summary>
    /// 🔴 <b>NON-BLOAT (ST-032's proof).</b> A component id that the GIZMO PATH brought into existence must
    /// not be recordable or saveable.
    ///
    /// <para><b>Why this rail exists.</b> <c>ST-027</c> called <c>repo.RegisterComponent&lt;T&gt;()</c> for
    /// 15 components on every host. That created real TABLES, with two measured consequences — it made
    /// <c>IsComponentTypeRegistered</c> true, and five translators are guarded on exactly that
    /// (<c>BehaviorTkbTranslator:52,100</c> · <c>PerceptionTkbTranslator:29,39</c> ·
    /// <c>VehicleKinematicsTkbTranslator:56</c>), so spawned entities would have GAINED brain/perception
    /// components; and it put those components in the recorder schema. Ids alone fix the first. The second
    /// needs the flags cleared, because <c>GetOrRegisterManaged</c> defaults them to <c>true</c>
    /// (<c>ComponentType.cs:157-158</c>) and <c>AsyncRecorder.BuildSchemaManifest</c> iterates
    /// <c>GetRecordableTypeIds()</c> — by ID, not by table.</para>
    ///
    /// <para>⚠⚠ <b>Formulated as "ids the registrar CREATED", deliberately.</b> <c>ComponentTypeRegistry</c>
    /// is a process-global, monotonic static, so whether a given component is already present depends on
    /// what ran earlier in this process. Snapshotting first makes the assertion deterministic regardless of
    /// test order — and it mirrors the production rule exactly: the registrar only clears the flags for ids
    /// it brings into existence, because clearing them for one a co-tenant genuinely simulates would drop
    /// that host's real data from the recording.</para>
    /// </summary>
    [Fact]
    public void ComponentIdsCreatedByTheGizmoPath_AreNeitherRecordableNorSaveable()
    {
        // ⭐ Driven by a TEST-ONLY projector requiring a TEST-ONLY component, so the assertion is
        // deterministic. The first version of this rail asserted over the PRODUCTION components and
        // stayed GREEN with the flag-clearing removed, because by the time it ran every one of them was
        // already registered by something else in this process -- the set it checked was empty. A probe
        // nothing else can touch removes that whole class of vacuity, and it still exercises the real
        // production path: reflection discovers this projector exactly as it discovers the others.
        // ⚠ No "not yet registered" precondition: a sibling case in this class may have run RegisterAll
        // already, and asserting -1 made this fail in the suite while passing in isolation. The OUTCOME is
        // what matters and it is order-independent -- nothing but the registrar knows about this probe, so
        // whoever ran it first must have cleared the flags.
        GizmoReflectionRegistrar.RegisterAll(
            new GizmoRegistry(), new StatelessGizmoRegistry(), new GizmoSettingsRegistry());

        int id = ComponentTypeRegistry.GetId(typeof(GizmoOnlyProbeComponent));
        Assert.True(id >= 0,
            "The registrar did not resolve the probe projector's component id, so it never registered the "
            + "probe projector -- check DiscoverProjectorTypes sees this assembly.");

        Assert.False(ComponentTypeRegistry.IsRecordable(id),
            "A component id created by the GIZMO path must not be recordable: "
            + "AsyncRecorder.BuildSchemaManifest walks GetRecordableTypeIds() by ID, so a recordable id "
            + "lands in the .fdp schema even with no table behind it. This is the half of the ST-027 "
            + "correction that id-only does NOT give you.");
        Assert.False(ComponentTypeRegistry.IsSaveable(id));
        Assert.DoesNotContain(id, ComponentTypeRegistry.GetRecordableTypeIds());
    }

    /// <summary>
    /// 🔴 <b>The other half of ST-027's correction:</b> the gizmo path must not create component TABLES.
    ///
    /// <para>A table is what made <c>EntityRepository.IsComponentTypeRegistered</c> — literally
    /// <c>_componentTables.ContainsKey(...)</c> — return true, and the translators above read that as
    /// licence to ADD the component to a spawned entity. Registering an id leaves a world's table set
    /// untouched, so those guards stay false.</para>
    /// </summary>
    [Fact]
    public void TheGizmoPath_CreatesNoComponentTableInAWorld()
    {
        using var world = new EntityRepository();
        int before = world.GetRegisteredComponentTypes().Count;

        GizmoReflectionRegistrar.RegisterAll(
            new GizmoRegistry(), new StatelessGizmoRegistry(), new GizmoSettingsRegistry());

        Assert.Equal(before, world.GetRegisteredComponentTypes().Count);
    }
}

/// <summary>
/// A component that exists ONLY in this test assembly, on an id far above anything
/// <c>GlobalComponentIds</c> declares (highest production id measured: 264). ⭐ Nothing in production can
/// register it, which is what makes the non-bloat rail deterministic instead of order-dependent.
/// </summary>
[Fdp.Core.ComponentId(500)]
public struct GizmoOnlyProbeComponent { public int Value; }

/// <summary>
/// A projector that exists ONLY in this test assembly, so <c>GizmoReflectionRegistrar</c> discovers it by
/// exactly the same path it discovers the real ones — the rail therefore exercises production code rather
/// than a re-implementation of its rule.
/// </summary>
[GizmoProjector(typeof(GizmoOnlyProbeComponent))]
public sealed class GizmoOnlyProbeGizmo : IStatelessGizmo
{
    public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder drawBuilder) { }
}
