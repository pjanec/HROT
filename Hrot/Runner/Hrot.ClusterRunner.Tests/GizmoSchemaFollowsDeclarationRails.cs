using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// <b>ST-023 — schema follows declaration, per host</b> (Q52 §3).
///
/// <para><b>The invariant.</b> Every component type required by a projector family a host DECLARES must be
/// registered by that host's own component-registration path. <c>StatelessGizmoRegistry.Register</c> throws
/// when a required type is unknown, and it runs during bootstrap — so a host that declares a family it
/// cannot satisfy does not fail a frame, it <b>fails to start</b>. That is what killed <c>--mode ig</c>
/// (<c>ST-020</c>).</para>
///
/// <para><b>Why a rail and not a comment.</b> The omission was live for however long, and
/// <c>--mode all</c> masks it: SimHost's registries put the missing schema on the shared world, so a host
/// is satisfied by accident of co-tenancy. A per-host check is the only thing that sees it, and adding a
/// projector with a new component is exactly the change that would silently re-break a host.</para>
///
/// <para>⚠ <b>Per-WORLD, deliberately.</b> <c>ComponentTypeRegistry</c> is a process-global, monotonic
/// static — once any host in this process registers a type, <c>GetId</c> answers for all of them. Asserting
/// against it would make every case pass as soon as one host was checked. So each case builds a
/// <b>fresh <see cref="EntityRepository"/></b> and asserts against
/// <see cref="EntityRepository.GetRegisteredComponentTypes"/>, which is that world's own set.</para>
///
/// <para>⚠ <b>What this does NOT check:</b> that a gizmo ever DRAWS. Under Q52 §0 membership is uniform and
/// the draw decision is made at runtime from whether the entity carries the components — on IG that set is
/// <i>supposed</i> to be empty. Registering the type is not putting data on an entity.</para>
///
/// <para>⚠⚠ <b>AND IT DOES NOT CHECK THAT A HOST ACTUALLY WIRES ITS REGISTRATION — measured, not assumed.</b>
/// Each profile below calls the registries directly, so this asserts the SCHEMA SET is complete for what a
/// host declares. It is blind to the call site: commenting out IG's
/// <c>MapSchemaPack.RegisterAll(world)</c> and re-running left all cases <b>GREEN</b>, while
/// <c>ModeStartupRails</c>'s <c>ig</c> case reddened. ⇒ ⭐ <b>the two rails are complementary and neither
/// is sufficient alone</b>: this one catches a projector whose component nothing registers; the mode rail
/// catches a host that stops calling what it needs. Stated here so a green is not read as more than it is.
/// </para>
/// </summary>
public sealed class GizmoSchemaFollowsDeclarationRails
{
    /// <summary>
    /// A host: the projector families it declares, and the component registration it performs.
    ///
    /// <para>⚠ Both halves are MEASURED from the host's own code, not assumed — the declarations from its
    /// <c>GizmoRegistrar</c> call site, the registration from its bootstrap. A drift in either is the thing
    /// this rail exists to catch, so it must not be paraphrased.</para>
    ///
    /// <para>⚠⚠ <b>These profiles must be kept in step with the production call sites</b>, and a stale one
    /// is not harmless: an earlier version stopped short of <c>EditorSubsystem</c>'s inline registrations
    /// and reddened a host that was perfectly healthy (<c>ST-024</c>). <c>ST-027</c> added
    /// <c>MapSchemaPack</c> to simhost, cgf and the editor, so all four profiles carry it now.</para>
    /// </summary>
    private sealed record HostProfile(
        string Name,
        string[] DeclaredRegistrars,
        Action<EntityRepository> Register)
    {
        public string Declares => string.Join(" + ", DeclaredRegistrars);
    }

    // Assemblies are reached through a type, never a name string: a typo in a name would silently make a
    // family "declare nothing" and turn this rail green for the wrong reason.
    private static Assembly CommonAsm      => typeof(Hrot.Common.Diagnostics.Gizmos.MapSchemaPack).Assembly;
    private static Assembly PresentationAsm => typeof(Hrot.ScenarioEditor.Gizmos.MapOverlayGizmo).Assembly;
    private static Assembly IgAsm          => typeof(Hrot.IG.IgRoleComponentRegistry).Assembly;
    private static Assembly AiAsm          => typeof(Hrot.AI.Behaviors.Gizmos.GizmoRegistrar).Assembly;
    private static Assembly SimHostAsm     => typeof(Hrot.SimHost.SimHostComponentRegistry).Assembly;
    private static Assembly CgfAsm         => typeof(Hrot.CGF.CgfComponentRegistry).Assembly;

    public static TheoryData<string> HostNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var h in Hosts) data.Add(h.Name);
            return data;
        }
    }

    private static readonly HostProfile[] Hosts =
    [
        // Hrot/Subsystems/Hrot.IG/Gizmos/GizmoRegistrar.cs:16-21 + IgApplication.cs:742-744
        // registration: IgNodeBootstrapper.RegisterDomainComponents (Phase 2)
        new("ig",
            ["Hrot.Common.Diagnostics.Gizmos", "Hrot.AI.Behaviors.Gizmos", "Hrot.IG.Gizmos",
             "Hrot.ScenarioEditor.Gizmos", "Hrot.Presentation.Gizmos"],
            w =>
            {
                Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(w);
                Hrot.IG.IgRoleComponentRegistry.RegisterAll(w);
                Hrot.Common.Diagnostics.Gizmos.MapSchemaPack.RegisterAll(w);
            }),

        // SimHostApp.cs:337-342 · registration: SimHostNodeBootstrapper.RegisterDomainComponents
        new("simhost",
            ["Hrot.SimHost.Gizmos", "Hrot.Presentation.Gizmos"],
            w =>
            {
                Hrot.SimHost.SimHostComponentRegistry.RegisterAll(w);
                Hrot.Common.Diagnostics.Gizmos.MapSchemaPack.RegisterAll(w);
            }),

        // CgfSubsystem.cs:526-528 · registration: CgfApplication.cs:125 / CgfSubsystem.cs:266
        new("cgf",
            ["Hrot.CGF.Gizmos", "Hrot.Presentation.Gizmos"],
            w =>
            {
                Hrot.CGF.CgfComponentRegistry.RegisterAll(w);
                Hrot.Common.Diagnostics.Gizmos.MapSchemaPack.RegisterAll(w);
            }),

        // EditorSubsystem.cs:1431-1445 · registration: EditorSubsystem.cs:857-858
        new("editor",
            ["Hrot.ScenarioEditor.Gizmos", "Hrot.SimHost.Gizmos", "Hrot.Common.Diagnostics.Gizmos",
             "Hrot.IG.Gizmos", "Hrot.Presentation.Gizmos", "Hrot.AI.Behaviors.Gizmos"],
            w =>
            {
                // ⚠ EditorSubsystem.cs:857-869 -- and the tail matters. An earlier version of this
                // profile stopped at 858 and reddened the editor for CullingState/VisualEffectState,
                // which it registers INLINE at 864/868. The rail was wrong, not the host: --mode editor
                // boots, and the whole Hrot.SystemTests suite runs one.
                //
                // ⭐ Worth noting rather than fixing here: the editor hand-picks IG components instead
                // of calling IgRoleComponentRegistry, so this list is a second place that can drift.
                Hrot.SimHost.SimHostComponentRegistry.RegisterAll(w);
                Hrot.CGF.CgfComponentRegistry.RegisterAll(w);
                Hrot.Common.Diagnostics.Gizmos.MapSchemaPack.RegisterAll(w);
                w.RegisterManagedComponent<Hrot.Map.Common.Components.ZoneMembership>();
                w.RegisterComponent<Fdp.Toolkit.Vis2D.Components.MapDisplayComponent>();
                w.RegisterComponent<Hrot.IG.Components.CullingState>();
                w.RegisterComponent<Hrot.IG.Components.ResolvedStyle>();
                w.RegisterManagedComponent<Hrot.IG.Components.IgSymbolOverride>();
                w.RegisterComponent<Hrot.IG.Components.VisualEffectState>();
                w.RegisterComponent<Hrot.IG.Components.TracerTarget>();
            }),
    ];

    /// <summary>
    /// ⚠ <b><c>replaybrowser</c> is absent, and that is a REPORTED GAP, not an oversight.</b>
    /// <c>ReplayBrowserSubsystem.cs:165-171</c> declares FOUR families (<c>Hrot.SimHost</c>,
    /// <c>Hrot.Common</c>, <c>Hrot.Presentation</c>, <c>Hrot.AI.Behaviors</c>) and 📐 a grep for
    /// <c>RegisterComponent&lt;</c>/<c>ComponentRegistry</c> across that subsystem returns <b>nothing</b> —
    /// so there is no registration path for this rail to call. Either it inherits a world registered
    /// elsewhere (in which case the profile needs that entry point, not a guess), or it has IG's omission
    /// and cannot start standalone. ⛔ Not guessed at here; filed as <c>ST-024</c>, and
    /// <c>ModeStartupRails</c> covers whether it actually boots (it does).
    /// </summary>
    private const string ReplayBrowserGap = "ST-024";

    [Theory]
    [MemberData(nameof(HostNames))]
    public void EveryDeclaredProjectorFamily_HasItsSchemaRegistered(string hostName)
    {
        var host = Hosts.Single(h => h.Name == hostName);

        var required = RequiredComponents(host.DeclaredRegistrars);
        Assert.NotEmpty(required); // a host whose families require nothing would make this vacuous

        using var world = new EntityRepository();
        host.Register(world);
        var registered = world.GetRegisteredComponentTypes().Keys.ToHashSet();

        var missing = required.Where(t => !registered.Contains(t.Type)).ToArray();

        Assert.True(missing.Length == 0,
            $"Host '{host.Name}' declares projector families it cannot satisfy, so it would THROW during "
            + $"bootstrap (StatelessGizmoRegistry.Register), not merely draw nothing.\n"
            + $"  declares : {host.Declares}\n"
            + $"  missing  : {string.Join(", ", missing.Select(m => $"{m.Type.Name} (required by {m.Projector})"))}\n"
            + $"⭐ The fix is to register the TYPE, not to drop the family: Q52 §0 — support all, decide on "
            + $"the current presence of the component. Hrot.Common.Diagnostics.Gizmos.MapSchemaPack is the "
            + $"place for projector-required schema.");
    }

    /// <summary>
    /// ⭐ <b>No ORPHAN projector family.</b> A projector whose namespace no host declares is validated by
    /// nothing above and drawn by nobody — it is dead weight that looks live.
    ///
    /// <para>⚠ This replaces a first attempt at "the pack covers every requirement", which was wrong by
    /// construction: it treated anything outside <c>HrotSharedComponentRegistry</c> as the pack's job,
    /// when host ROLE registries legitimately supply most of it (<c>IgHealthState</c>,
    /// <c>SelectionState</c>, <c>PerceptionReceptor</c> …). It reddened on seven types that were never
    /// the pack's business. The per-host cases above already catch a new projector whose component
    /// nobody registers — so completeness needed no second assertion, but ORPHANS did.</para>
    /// </summary>
    [Fact]
    public void EveryProjectorNamespace_IsDeclaredByAtLeastOneHost()
    {
        var declared = Hosts.SelectMany(h => h.DeclaredRegistrars).ToHashSet(StringComparer.Ordinal);

        var orphans = new List<string>();
        foreach (var asm in new[] { CommonAsm, PresentationAsm, IgAsm, AiAsm, SimHostAsm, CgfAsm }.Distinct())
        {
            foreach (var type in asm.GetTypes())
            {
                if (type.GetCustomAttribute<GizmoProjectorAttribute>() is null) continue;
                if (type.Namespace is { } ns && !declared.Contains(ns))
                    orphans.Add($"{type.FullName} (namespace '{ns}')");
            }
        }

        Assert.True(orphans.Count == 0,
            $"These projectors live in a namespace no host declares, so their generated registrar is "
            + $"never called and they can never draw:\n  {string.Join("\n  ", orphans)}\n"
            + $"⭐ Either a host should declare that registrar, or the projector's namespace is wrong. "
            + $"⚠ If a host DOES declare it, this rail's Hosts table is out of date -- fix the table, "
            + $"which is the same failure mode that first reddened 'editor' here.");
    }

    // ── reflection over the declarations ──────────────────────────────────────────────────────────

    private sealed record Requirement(Type Type, string Projector);

    /// <summary>
    /// The union of every <c>[GizmoProjector]</c>'s required components across the given assemblies, each
    /// carrying the projector that asked for it so a failure names the culprit rather than just the type.
    /// </summary>
    private static Requirement[] RequiredComponents(string[] registrarNamespaces)
    {
        var wanted = registrarNamespaces.ToHashSet(StringComparer.Ordinal);
        var seen = new Dictionary<Type, string>();

        // ⚠ Scanned per NAMESPACE, not per assembly, because that is how GizmoRegistrarGenerator groups:
        // it emits "one source file per namespace group" (GizmoRegistrarGenerator.cs:136). The
        // Hrot.Presentation assembly therefore carries TWO registrars -- Hrot.Presentation.Gizmos and
        // Hrot.ScenarioEditor.Gizmos -- and a host that declares one of them does NOT get the other's
        // projectors. Grouping by assembly would over-state what a host declared and redden it wrongly.
        foreach (var asm in new[] { CommonAsm, PresentationAsm, IgAsm, AiAsm, SimHostAsm, CgfAsm }.Distinct())
        {
            foreach (var type in asm.GetTypes())
            {
                if (type.Namespace is null || !wanted.Contains(type.Namespace)) continue;

                var attr = type.GetCustomAttribute<GizmoProjectorAttribute>();
                if (attr is null) continue;

                foreach (var required in attr.RequiredComponents)
                {
                    if (!seen.ContainsKey(required))
                        seen[required] = $"{type.Name} in {type.Namespace}";
                }
            }
        }

        return seen.Select(kv => new Requirement(kv.Key, kv.Value)).ToArray();
    }
}
