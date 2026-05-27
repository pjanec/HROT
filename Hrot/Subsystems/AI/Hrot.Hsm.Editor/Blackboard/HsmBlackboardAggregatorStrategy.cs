using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Blackboard;

public sealed class HsmBlackboardAggregatorStrategy : IBlackboardAggregatorStrategy
{
    private readonly BlackboardAggregatorService _service;

    public HsmBlackboardAggregatorStrategy(BlackboardAggregatorService service)
        => _service = service;

    public bool CanHandle(IEditableAsset asset)
        => asset is HsmAsset;

    public AggregationResult Aggregate(
        IEditableAsset        asset,
        IActionSchemaExporter schema,
        IAssetCatalog         catalog,
        HashSet<Guid>         visited)
    {
        var hsmAsset = (HsmAsset)asset;
        if (!visited.Add(hsmAsset.AssetId))
            return AggregationResult.Empty;

        var requirements = new List<DtoRequirement>();
        var warnings     = new List<AggregationWarning>();

        // States
        foreach (var state in hsmAsset.AllStates)
        {
            EmitIfFound(state.OnEntryAction,  $"{hsmAsset.Name} > State '{state.Name}' OnEntry",  hsmAsset, state.StableId, schema, requirements, warnings);
            EmitIfFound(state.OnExitAction,   $"{hsmAsset.Name} > State '{state.Name}' OnExit",   hsmAsset, state.StableId, schema, requirements, warnings);
            EmitIfFound(state.ActivityAction, $"{hsmAsset.Name} > State '{state.Name}' Activity", hsmAsset, state.StableId, schema, requirements, warnings);
            EmitIfFound(state.TimerAction,    $"{hsmAsset.Name} > State '{state.Name}' Timer",    hsmAsset, state.StableId, schema, requirements, warnings);
        }

        // Transitions
        foreach (var t in hsmAsset.AllTransitions)
        {
            string label = $"{hsmAsset.Name} > Transition '{t.Source?.Name}' -> '{t.Target?.Name}'";
            EmitIfFound(t.GuardFunction,  label + " Guard",  hsmAsset, t.VisualId, schema, requirements, warnings);
            EmitIfFound(t.ActionFunction, label + " Action", hsmAsset, t.VisualId, schema, requirements, warnings);
        }

        // Global transitions
        foreach (var g in hsmAsset.AllGlobalTransitions)
        {
            string label = $"{hsmAsset.Name} > GlobalTransition -> '{g.Target?.Name}'";
            EmitIfFound(g.GuardFunction,  label + " Guard",  hsmAsset, g.VisualId, schema, requirements, warnings);
            EmitIfFound(g.ActionFunction, label + " Action", hsmAsset, g.VisualId, schema, requirements, warnings);
        }

        return new AggregationResult(requirements, warnings);
    }

    private static void EmitIfFound(
        string?               fqn,
        string                path,
        HsmAsset              asset,
        Guid                  elementId,
        IActionSchemaExporter schema,
        List<DtoRequirement>  reqs,
        List<AggregationWarning> warns)
    {
        if (fqn == null) return;
        var entry = schema.Lookup(fqn);
        if (entry != null)
            reqs.Add(new DtoRequirement(entry.DtoType, path, asset.AssetId, elementId));
        else
            warns.Add(new AggregationWarning(
                AggregationWarningKind.SchemaEntryNotFound,
                $"Schema entry not found for FQN '{fqn}' in asset '{asset.Name}'.",
                asset.AssetId));
    }
}
