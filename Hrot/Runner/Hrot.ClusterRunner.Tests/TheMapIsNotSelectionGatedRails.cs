using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// 🔴🔴🔴 <b><c>UXI-23</c> <c>S2</c> / <c>CE-123</c> — the map may not be gated by selection.</b>
///
/// <para><b>The defect these pin, measured live on <c>--mode all</c>.</b> SimHost's map drew <b>3</b>
/// non-<c>Line</c> primitives against the Scenario perspective's <b>69</b>, over the same 8 entities.
/// <c>S1</c> proved the missing components were only half the story. The other half:
/// <c>SimHostApp.cs:440</c> passed an <c>isSelectedPredicate</c> to <see cref="StatelessGizmoSystem"/> —
/// the SAME lambda it passes to <c>DataDrivenGizmoSystem</c> 70 lines above.</para>
///
/// <para>⭐⭐⭐ <b>That is correct for HANDLES and catastrophic for the MAP.</b>
/// <see cref="DataDrivenGizmoSystem"/> gates drag handles, which genuinely belong on the selection.
/// <see cref="StatelessGizmoSystem"/> applies the predicate as ONE BLANKET GATE across every stateless
/// rule it owns — entity avatars, routes, tactical areas, the map overlay — and on the cluster node path
/// nothing ever sets <c>SelectionState.IsSelected</c>, so it was false for every entity, every frame.</para>
///
/// <para>🔒 <b><c>R-137</c>: the capability is not removed, only the wrong default.</b> The
/// <c>isSelectedPredicate</c> PARAMETER stays on the shared system — any host may still gate its map by
/// selection if it wants to.</para>
///
/// <para>⚠⚠ <b>UPDATED for <c>S2b</c>.</b> This file used to assert <i>"no host passes a predicate to
/// <c>StatelessGizmoSystem</c>"</i> by scanning the five host constructions. <c>S2b</c> moved those
/// constructions into <see cref="Hrot.ScenarioEditor.Map.MapInteractionPack"/>, so that rail's own
/// precondition (five construction sites) became false — and it went red, correctly, the moment the
/// composition moved. ⭐ The invariant is now <b>structural</b>: <c>MapInteractionContext</c> exposes no
/// stateless predicate to pass. What this file asserts instead is that no host has drifted BACK to
/// hand-building the machinery.</para>
///
/// <para>📄 <c>docs/UX/UX_Feature_Map_Parity.md</c> §3.9j.1.</para>
/// </summary>
public sealed class TheMapIsNotSelectionGatedRails
{
    /// <summary>
    /// The five hosts that compose a <see cref="StatelessGizmoSystem"/>, relative to the repo root.
    /// ⭐ Enumerated rather than globbed: a SIXTH host must be added here deliberately, which is a
    /// review moment, rather than silently escaping the rail.
    /// </summary>
    private static readonly string[] HostFiles =
    {
        "Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs",
        "Hrot/Subsystems/Hrot.IG/IgApplication.cs",
        "Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs",
        "Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs",
        "Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs",
    };

    // Walks up from the test binary directory until IOS-IG-SimHost.sln is found — the idiom already used
    // by ExConSubsystemClusterTests and CrossHostPanelKindRails.
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IOS-IG-SimHost.sln")))
            dir = dir.Parent;

        Assert.True(dir != null, "Could not locate workspace root (IOS-IG-SimHost.sln not found).");
        return dir!;
    }

    /// <summary>
    /// 🔴🔴 <b>The <c>CE-123</c> regression guard.</b> No host may construct
    /// <see cref="StatelessGizmoSystem"/> with a third argument.
    ///
    /// <para><b>Why this is asserted over SOURCE and not over behaviour.</b> The defect lives in a host's
    /// COMPOSITION — a lambda handed to a constructor deep inside a bootstrap callback. There is no
    /// constructed object to interrogate without standing up a whole host, and the field is private. A
    /// unit test over <see cref="StatelessGizmoSystem"/> proves what the predicate DOES (see the sibling
    /// rail below); only this one proves that no host PASSES one.</para>
    /// </summary>
    [Fact]
    public void NoHost_ConstructsTheGizmoSystemsItself()
    {
        var root = RepoRoot();
        var offenders = new List<string>();

        foreach (string relative in HostFiles)
        {
            string path = Path.Combine(root.FullName, relative);
            Assert.True(File.Exists(path), $"Host file not found: {relative}");

            foreach (string line in File.ReadAllLines(path))
            {
                string t = line.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal)) continue;   // explanatory comments

                if (Regex.IsMatch(t, @"new\s+(?:[\w\.]*\.)?StatelessGizmoSystem\s*\(")
                 || Regex.IsMatch(t, @"new\s+(?:[\w\.]*\.)?DataDrivenGizmoSystem\s*\(")
                 || Regex.IsMatch(t, @"new\s+(?:[\w\.]*\.)?GizmoExecutionController\s*\(")
                 || Regex.IsMatch(t, @"new\s+(?:[\w\.]*\.)?TogglablePostSimulationGroup\s*\(\s*""GizmoExecution"))
                {
                    offenders.Add($"{relative}: {Collapse(t)}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "A host constructs the map's gizmo machinery itself:\n  "
          + string.Join("\n  ", offenders)
          + "\n\n🔒 UXI-23 S2b: MapInteractionPack.Build(ctx) constructs it, once, and the HOST SCHEDULES "
          + "what it returns. Five hand-written compositions were five chances to get a constructor "
          + "argument wrong, and CE-123 was exactly that — SimHost handed StatelessGizmoSystem the "
          + "selection predicate meant for the drag handles, and its map went dark with nothing reported.\n"
          + "⭐ The stateless-gate invariant is now structural rather than checked: MapInteractionContext "
          + "has no stateless predicate to pass. See MapInteractionPackTests.");
    }

    /// <summary>
    /// ⭐⭐ <b>The behaviour half — what the predicate actually does.</b> Documents WHY the rail above
    /// matters: one predicate suppresses an ordinary presentation projector completely.
    ///
    /// <para>Without this, the source rail looks like style policing. With it, the cost is explicit.</para>
    /// </summary>
    [Fact]
    public void ASelectionPredicate_SuppressesAnEntireProjector_NotJustItsHandles()
    {
        // ⚠ Uses its OWN probe component, deliberately. An earlier version of this rail registered
        // GizmoSchemaFollowsDeclarationRails' GizmoOnlyProbeComponent, which reddened that file's
        // non-bloat rail: ComponentTypeRegistry is a process-global static and RegisterComponent
        // RE-APPLIES SetRecordable/SetSaveable from the DataPolicy, undoing the flag clearing that rail
        // asserts. Sharing a probe across rail files couples them through process-global state.
        using var repo = new EntityRepository();
        repo.RegisterComponent<MapGateProbeComponent>();

        var registry = new StatelessGizmoRegistry();
        var probe = new CountingProbeGizmo();
        registry.Register(probe, new[] { typeof(MapGateProbeComponent) });

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new MapGateProbeComponent { Value = 1 });

        // ── null predicate: draws (this is what all five hosts do after S2) ──
        var open = new StatelessGizmoSystem(registry, new DebugPrimitiveBuffer());
        open.Execute(repo, 0.016f);
        Assert.True(probe.DrawCount > 0,
            "A null isSelectedPredicate must mean ALWAYS DRAW — StatelessGizmoSystem:81.");

        // ── a predicate nothing satisfies: draws nothing at all ──
        probe.DrawCount = 0;
        var gated = new StatelessGizmoSystem(registry, new DebugPrimitiveBuffer(), (_, _) => false);
        gated.Execute(repo, 0.016f);
        Assert.Equal(0, probe.DrawCount);
    }

    /// <summary>Returns the substring from <paramref name="start"/> to the matching close paren.</summary>
    private static string ArgumentListAt(string source, int start)
    {
        int depth = 1;
        for (int i = start; i < source.Length; i++)
        {
            if (source[i] == '(') depth++;
            else if (source[i] == ')')
            {
                depth--;
                if (depth == 0) return source.Substring(start, i - start);
            }
        }
        return source.Substring(start);
    }

    /// <summary>Commas at nesting depth zero — so a lambda body's own commas are not counted.</summary>
    private static int TopLevelCommas(string args)
    {
        int depth = 0, count = 0;
        foreach (char c in args)
        {
            if (c is '(' or '[' or '<') depth++;
            else if (c is ')' or ']' or '>') depth--;
            else if (c == ',' && depth == 0) count++;
        }
        return count;
    }

    private static string Collapse(string s)
    {
        string one = Regex.Replace(s, @"\s+", " ").Trim();
        return one.Length <= 160 ? one : one.Substring(0, 160) + "…";
    }

    /// <summary>A projector that only counts, so the rail measures dispatch and not drawing.</summary>
    private sealed class CountingProbeGizmo : IStatelessGizmo
    {
        public int DrawCount;
        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder drawBuilder) => DrawCount++;
    }
}

/// <summary>
/// A component owned by THIS rail file, on an id above anything <c>GlobalComponentIds</c> declares
/// (highest production id measured: 264; <c>GizmoOnlyProbeComponent</c> holds 500).
///
/// <para>⚠ It exists so this file does not share a probe with <c>GizmoSchemaFollowsDeclarationRails</c>.
/// <c>ComponentTypeRegistry</c> is a process-global static, so two rail files registering one component
/// couple through it — and that file's non-bloat rail asserts flags that <c>RegisterComponent</c>
/// re-applies.</para>
///
/// <para>⛔ Deliberately carries NO <c>[GizmoProjector]</c> partner type, so
/// <c>GizmoReflectionRegistrar.DiscoverProjectorTypes</c> never sees it and the discovery-based rails
/// elsewhere keep their counts.</para>
/// </summary>
[Fdp.Core.ComponentId(501)]
public struct MapGateProbeComponent { public int Value; }
