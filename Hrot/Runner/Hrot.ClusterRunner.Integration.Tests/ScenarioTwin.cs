using Fdp.Testing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐ What every in-process demo twin does first: stage a shipped <c>scenarios/&lt;name&gt;</c>, boot the four hosts and take the cluster live,
/// then find people by name. (Was copied into <see cref="ClearingScenarioTests"/> and <see cref="PlatoonScenarioTests"/>.)
/// </summary>
internal static class ScenarioTwin
{

    public static Entity ByName(EntityRepository world, string name)
    {
        for (int i = 0; i <= world.MaxEntityIndex; i++)
        {
            var e = world.GetEntityByIndex(i);
            if (!world.IsAlive(e) || !world.HasComponent<EntityInfo>(e)) continue;
            if (world.GetComponent<EntityInfo>(e).Name.ToString() == name) return e;
        }
        return Entity.Null;
    }

    /// <summary>Stages <c>scenarios/&lt;name&gt;</c> (its id added to <paramref name="staged"/>, for the caller's Dispose to remove), boots
    /// the four hosts on <paramref name="domainId"/> and takes the cluster live.</summary>
    public static async Task<HrotRunnerHarness> Live(string scenario, List<string> staged, int domainId)
    {
        var id = scenario.Replace('-', '_') + "_" + Guid.NewGuid().ToString("N");
        staged.Add(id);
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(id));
        File.Copy(Path.Combine(TestRepo.Root, "scenarios", scenario, "scenario.json"),
            Path.Combine(NasScenarioStaging.DirectoryOf(id), "scenario.json"), overwrite: true);

        var harness = new HrotRunnerHarness("simhost,ig,cgf", domainId);
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;
        var rosterDeadline = DateTime.UtcNow.AddSeconds(10.0);
        while (master.NodeRoster.ActiveNodes.Count == 0 && DateTime.UtcNow < rosterDeadline)
        {
            harness.PumpFrames(1);
            Thread.Sleep(10);
        }
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = JsonSerializer.Serialize(new { TargetState = nameof(ClusterState.OperatingLive), ScenarioId = id }),
        }).ConfigureAwait(false);
        Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == 31, timeoutFrames: 4000),
            $"cluster must reach OperatingLive; at {(int)master.CurrentClusterState}");
        return harness;
    }
}
