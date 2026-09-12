using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Time;
using Hrot.CGF;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐⭐⭐ <b>cgf==editor slice 4 — <c>DQ30-A</c>'s deadlock, railed.</b>
///
/// <para>📄 <c>docs/UX/Design_Question_30_Debug_Pause_Resume.md</c> §A + §3 *(risks)* ·
/// <c>docs/UX/UX_Feature_Cgf_Brain_Diagnostics.md</c> §3a — which names the first assertion below
/// as <b>"the single highest-risk check in the design"</b>.</para>
///
/// <para>⛔⛔ <b>Why it is worth a rail rather than a one-time look.</b> The debug halt disables CGF's
/// togglable groups. If anything that drains the master's mode switch ever ends up INSIDE one of
/// those groups, a paused node stops hearing the cluster and can never be resumed — and the symptom
/// (*"resume does nothing"*) points nowhere near the composition change that caused it. ⭐ A rail turns
/// that into a red at the moment someone adds the system.</para>
///
/// <para>⚠ <b>What these rails are NOT.</b> They assert on CONSTRUCTED objects — a real
/// <see cref="CgfLogicPack"/> in the real togglable groups, and the real time translators — ⛔ not on
/// the composition root's source, and ⛔ not on a live cluster. 📐 <c>CgfSubsystem.Initialize</c> blocks
/// on the DDS id allocator, so a whole-subsystem boot is an integration concern; the report says which
/// suite owns that and why it cannot gate here.</para>
/// </summary>
public sealed class TheHaltCannotStopTheClockTests
{
    private static CgfLogicPack BuildRealLogicPack()
        => new CgfLogicPack(
            behaviorRegistry: new Fdp.Toolkit.Behavior.BehaviorRegistry(),
            entityMap:        new NetworkEntityMap(),
            scenarioSource:   new Hrot.Core.Network.ScenarioEntityCreationRequestSource(),
            mapperRegistry:   new Fdp.Toolkit.Behavior.TacticalOrderMapper.TacticalIntentMapperRegistry());

    /// <summary>
    /// ⭐⭐⭐ <b>The halt actuator gates the BRAIN and nothing that carries time.</b>
    ///
    /// <para>Every system the two togglable groups would disable is inspected by type name. A name
    /// scan is the honest instrument here — the claim is about the composition of a list, and the
    /// forbidden types live in assemblies this one does not reference — ⚠ so the caveat is stated
    /// rather than hidden: it catches a system NAMED for the clock, and would miss one that drains
    /// mode events under an unrelated name.</para>
    /// </summary>
    [Fact]
    public void NoClockOrTimeSyncSystemSitsInsideCgfsTogglableGroups()
    {
        var pack = BuildRealLogicPack();

        var input = new TogglableInputGroup("CgfInput", pack.InputSystems);
        var sim   = new TogglableSimulationGroup("CgfSimulation", pack.SimulationSystems);

        var gated = input.GetSystems().Concat(sim.GetSystems())
                         .Select(s => s.GetType().Name)
                         .ToList();

        Assert.NotEmpty(gated);   // ⛔ an empty pack would pass every assertion below vacuously

        string[] forbidden =
        {
            "SlaveSyncController", "MasterSyncController", "SteppedSlaveController",
            "SlaveTimeModeListener", "DistributedTimeCoordinator",
        };

        var offenders = gated.Where(n => forbidden.Any(f => n.Contains(f, StringComparison.Ordinal)))
                             .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// ⚠ <b>And the same for the NETWORK ingress that carries the resume</b> — asserted by TYPE.
    ///
    /// <para>🔴 <b>This rail was first written as a name scan for <c>"Ingress"</c> and it went red on
    /// <c>BehaviorIngressSystem</c>.</b> 📐 Measured: that class lives in
    /// <c>Fdp.Toolkit.Behavior.Systems</c> and parses behaviour-assignment blackboards — it is BRAIN
    /// work, so being gated by the halt is exactly right. ⇒ the two classes share a WORD, not a
    /// meaning, and a name scan cannot tell them apart. ⭐ The type test can, so it is the one used.</para>
    /// </summary>
    [Fact]
    public void NoNetworkIngressSystemSitsInsideCgfsTogglableGroups()
    {
        var pack = BuildRealLogicPack();

        var gated = pack.InputSystems.Concat(pack.SimulationSystems)
                        .Where(s => s is Fdp.Network.Cyclone.Modules.CycloneNetworkIngressSystem)
                        .Select(s => s.GetType().Name)
                        .ToList();

        Assert.Empty(gated);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The three time translators declare themselves CONTROL PLANE — on the real objects.</b>
    ///
    /// <para>⛔⛔ This is the assertion that makes the freeze gate safe to apply to every ingress system
    /// on the node. The gate skips <c>WorldState</c> translators; these three ride in the ingress
    /// system that <c>SlaveTimeTranslatorRegistration</c> registers, and they carry the mode switch,
    /// the lockstep barrier and the clock handshake. ⇒ if any of them fell back to the
    /// <c>WorldState</c> default, a frozen CGF node would never hear its own resume.</para>
    ///
    /// <para>⭐ All three factories accept a <c>null</c> participant by documented contract, so this
    /// needs no DDS.</para>
    /// </summary>
    [Fact]
    public void TheThreeTimeTranslatorsAreControlPlane()
    {
        var bus = new FdpEventBus();

        var translators = new INetworkTranslator[]
        {
            TimeNetworkModule.CreateDescriptorTranslator(null, bus),
            TimeNetworkModule.CreateSlaveLockstepTranslator(null, bus, localNodeId: 7),
            TimeNetworkModule.CreateSlaveTimeSyncTranslator(null, bus, localNodeId: 7),
        };

        foreach (var t in translators)
            Assert.Equal(TranslatorClass.ControlPlane, t.Category);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>EXACTLY these three translators are control plane — and no fourth appears unnoticed.</b>
    ///
    /// <para>⛔⛔ The fail-safe default protects against a FORGOTTEN mark; nothing protects against a
    /// WRONG one. ⚠ A world-state translator marked <c>ControlPlane</c> would keep polling into a
    /// frozen snapshot <b>silently</b> — the exact failure the default was chosen to avoid, arriving
    /// through the other door. ⇒ the override set is enumerated, not trusted.</para>
    ///
    /// <para>⭐ A source scan is the honest instrument: *"these are ALL the overrides"* is a claim
    /// about the repository, and no constructed object can be asked it. ⛔ It does not stand in for
    /// <see cref="TheThreeTimeTranslatorsAreControlPlane"/>, which asserts the behaviour.</para>
    /// </summary>
    [Fact]
    public void OnlyTheThreeTimeTranslatorsDeclareThemselvesControlPlane()
    {
        var root = RepoRoot();
        Assert.NotNull(root);

        var declaring = new List<string>();

        foreach (var file in System.IO.Directory.EnumerateFiles(root!, "*.cs", System.IO.SearchOption.AllDirectories))
        {
            if (file.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}") ||
                file.Contains($"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}"))
                continue;

            var rel = System.IO.Path.GetRelativePath(root!, file).Replace('\\', '/');
            if (rel.Contains(".Tests/", StringComparison.Ordinal)) continue;

            foreach (var line in System.IO.File.ReadLines(file))
            {
                if (!line.Contains("TranslatorClass.ControlPlane", StringComparison.Ordinal)) continue;
                // ⚠ A DECLARATION, not a mention: the enum's own definition and the ingress system's
                //   doc comment both name the value without any translator claiming it.
                if (!line.Contains("Category", StringComparison.Ordinal)) continue;
                // ⚠⚠ And not a COMMENTED-OUT declaration. 📌 Found by this batch's own revert-goes-red
                //    proof: commenting the member out left the text intact, so the rail stayed green
                //    while the translator had silently fallen back to the WorldState default.
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                    trimmed.StartsWith("///", StringComparison.Ordinal) ||
                    trimmed.StartsWith("*", StringComparison.Ordinal)) continue;
                declaring.Add(rel);
                break;
            }
        }

        declaring.Sort();

        Assert.Equal(new[]
        {
            "FDP/Toolkits/Fdp.Toolkits/Time/SwitchTimeModeDescriptorTranslator.cs",
            "FDP/Toolkits/Fdp.Toolkits/Time/Translators/SlaveLockstepTranslator.cs",
            "FDP/Toolkits/Fdp.Toolkits/Time/Translators/SlaveTimeSyncTranslator.cs",
        }, declaring);
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
}
