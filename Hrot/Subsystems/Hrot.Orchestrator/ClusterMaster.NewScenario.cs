using System;
using System.Text.Json.Nodes;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐ New scenario N2 (📄 docs/UX/DESIGN_New_Scenario_And_Recipe.md §2.2, R-4135) — a BLANK scenario on a terrain is a header-only file the MASTER
/// writes where it resolves scenario names (its NAS, <see cref="StorageProcessManager.WriteBlankScenario"/>); the caller then loads it by name through
/// the ordinary door. ⭐ Run BEFORE the frame's load transitions (<c>ProcessBlankScenarioIntents</c>), so the create and the load sent together are
/// ordered by construction — on one process and across the wire (the egress sends the create first too).
/// </summary>
public sealed partial class ClusterMaster
{
    /// <summary>⭐ N2 — writes the blank scenario: (name, terrain) → <see langword="null"/> when written, else the refusal. Set by the host that owns the
    /// NAS root (<c>OrchestratorCore</c>, like <see cref="ExerciseDeleter"/>); <see langword="null"/> ⇒ this master refuses the create.</summary>
    public Func<string, string, string?>? BlankScenarioWriter { get; set; }

    private void ProcessBlankScenarioIntents()
    {
        foreach (var intent in _eventBus.ReadManaged<ExecuteStorageOpIntent>())
            if (intent.Operation == StorageOpType.CreateBlankScenario)
                ProcessCreateBlankScenario(intent);
    }

    private void ProcessCreateBlankScenario(ExecuteStorageOpIntent intent)
    {
        var name = intent.ScenarioName;
        string? terrain = null;
        try { terrain = (JsonNode.Parse(intent.HeaderJson ?? "{}") as JsonObject)?["terrainName"]?.GetValue<string>(); }
        catch (System.Text.Json.JsonException) { }
        catch (InvalidOperationException) { }

        string? refusal =
              string.IsNullOrWhiteSpace(name)    ? "a scenario name is required"
            : string.IsNullOrWhiteSpace(terrain) ? "a terrain is required (a blank scenario is created on a terrain, which never changes afterwards)"
            : BlankScenarioWriter is null        ? "this master cannot create scenarios"
            : BlankScenarioWriter(name!, terrain!);

        if (refusal != null)
            FdpLog<ClusterMaster>.Warn("[Orchestrator] N2: blank scenario '{0}' on '{1}' REFUSED — {2}.", name ?? "", terrain ?? "", refusal);
        else
            FdpLog<ClusterMaster>.Info("[Orchestrator] N2: blank scenario '{0}' written on terrain '{1}'.", name!, terrain!);
        PublishOpStatus(intent.RequestId, refusal == null ? OrchestrationStatusCode.Success : OrchestrationStatusCode.Rejected, refusal);
    }
}
