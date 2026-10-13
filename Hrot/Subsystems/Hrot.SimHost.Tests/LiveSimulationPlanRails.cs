using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Systems;
using Hrot.Common.Infrastructure;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐⭐ <c>CE-2144</c> (R-2008) — damage is applied by ONE shared capability at the HEAD of Simulation on every live-simulation node.
/// 🔒 User, <c>2026-10-13</c>: <i>"D approved (provided it runs unified on any ECS enabled node running live simulation - shared code,
/// shared bootstrap)"</i>. 📄 docs/DESIGN_Orders_Params_Entity_Classes.md §11.2.
/// </summary>
/// <remarks>The host plans are built inline in their composition roots, so — like the other root rails here — the "every host"
/// half is a SOURCE rail; the behaviour half runs the factory's plan on a world.</remarks>
public sealed class LiveSimulationPlanRails
{
    /// <summary>The six production host plans (design §11.2 INVENTORY).</summary>
    private static readonly string[] HostRoots =
    {
        "Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs",
        "Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs",
        "Hrot/Subsystems/Hrot.IG/IgNodeBootstrapper.cs",
        "Hrot/Subsystems/Hrot.Editor/EditorCapabilities.cs",
        "Stride/HrotStrideApp.Game/StrideCapabilities.cs",
    };

    /// <summary>
    /// ⛔ The shared bootstrap cannot be bypassed: no production source starts a plan with <c>new NodeCompositionPlan()</c> except the
    /// factory itself, and every host root calls the factory (the editor twice — one per arm).
    /// </summary>
    [Fact]
    public void EveryProductionPlanStartsFromTheSharedHead_CE2144()
    {
        string root = RepoRoot() ?? throw new InvalidOperationException("checkout root (HROT.sln) not found");
        var bare = new Regex(@"new\s+(Hrot\.Common\.Infrastructure\.)?NodeCompositionPlan\s*\(", RegexOptions.Compiled);

        var offenders = new List<string>();
        int scanned = 0;
        foreach (string top in new[] { "Hrot", "Stride", "FDP" })
        {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(root, top), "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (rel.Contains("/obj/") || rel.Contains("/bin/") || rel.Contains("Tests/") || rel.Contains(".Tests/")) continue;
                scanned++;
                if (rel.EndsWith("Infrastructure/CoreInfrastructureCapabilities.cs", StringComparison.Ordinal)) continue;
                if (bare.IsMatch(File.ReadAllText(file))) offenders.Add(rel);
            }
        }

        Assert.True(scanned > 100, $"scanned only {scanned} production files — the walk is broken, not clean");
        Assert.True(offenders.Count == 0,
            "CE-2144: a host started a plan without the shared head (CoreInfrastructureCapabilities.LiveSimulationPlan): " + string.Join(", ", offenders));

        foreach (string host in HostRoots)
        {
            int calls = Regex.Matches(File.ReadAllText(Path.Combine(root, host)), @"LiveSimulationPlan\s*\(").Count;
            Assert.True(calls >= (host.EndsWith("EditorCapabilities.cs") ? 2 : 1), $"{host} does not start its plan(s) from LiveSimulationPlan ({calls} calls)");
        }
    }

    /// <summary>
    /// ⭐ The head is FIRST on any role, de-duplicates by Key, and its system applies damage on a node with no Brain at all (a SimHost-
    /// or Stride-shaped plan): Health drops and the capabilities are stripped at 0 HP — the owner side every live node now has.
    /// </summary>
    [Fact]
    public void TheSharedHeadAppliesDamageOnANodeWithNoBrain_CE2144()
    {
        var plan = CoreInfrastructureCapabilities.LiveSimulationPlan(NodeRole.MuscleGround)
            .Capability(NodeRole.MuscleGround, new CoreInfrastructureCapabilities.Interactions())
            .Capability(NodeRole.MuscleGround, new CoreInfrastructureCapabilities.HealthApplication());   // a second declaration loses (first wins)

        var resolved = plan.Resolve(NodeRole.MuscleGround);
        Assert.Equal(CapabilityKeys.HealthApplication, resolved[0].Key);
        Assert.Single(resolved, c => c.Key == CapabilityKeys.HealthApplication);

        var input = new List<IEcsModuleSystem>(); var sim = new List<IEcsModuleSystem>(); var post = new List<IEcsModuleSystem>();
        foreach (var c in resolved) c.PopulateSystems(context: null!, input, sim, post);
        Assert.IsType<HealthApplicationSystem>(sim[0]);
        Assert.IsType<Fdp.Toolkit.Combat.Systems.BleedOutSystem>(sim[1]);   // ⭐ CE-2155 (R-2011 I3) — every live node bleeds the casualties it owns

        using var world = new EntityRepository();
        world.RegisterComponent<Health>();
        world.RegisterComponent<ActorCapabilityState>();
        world.RegisterComponent<MobilityKill>();
        world.RegisterEvent<DamageAssessedEvent>();
        var soldier = world.CreateEntity();
        world.AddComponent(soldier, new Health { Current = 30f, Max = 100f });
        world.AddComponent(soldier, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot });

        world.Bus.Publish(new DamageAssessedEvent { HitEntity = soldier, TotalDamage = 50f });
        world.Bus.SwapBuffers();
        sim[0].Execute(world, 0.016f);

        Assert.Equal(0f, world.GetComponentRO<Health>(soldier).Current);
        var caps = world.GetComponentRO<ActorCapabilityState>(soldier).Capabilities;
        Assert.False(caps.HasFlag(ActorCapabilities.CanMove));
        Assert.False(caps.HasFlag(ActorCapabilities.CanShoot));
    }

    /// <summary>⭐ Inert where nothing assesses damage: a node that never registered the event (IG today) runs it without effect or throw.</summary>
    [Fact]
    public void TheSharedHeadIsInertWhereNoDamageEventIsRegistered_CE2144()
    {
        // ⭐ CE-2155 — every system of the capability (the bleed-out joined it): inert where nothing it reads is registered.
        using var world = new EntityRepository();
        world.RegisterComponent<Health>();
        foreach (var system in new CoreInfrastructureCapabilities.HealthApplication().Systems)
            system.Execute(world, 0.016f);
    }

    private static string? RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            if (File.Exists(Path.Combine(dir, "HROT.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}
