using System;
using System.Collections.Generic;
using FluentAssertions;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Host;

/// <summary>
/// B-4 lifecycle tests: removing an HSM transition (link) that owns an auto-managed
/// variable removes that variable from the asset.
/// </summary>
public sealed class HsmTransitionAutoVarDeleteTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static (HsmAsset asset, HsmCommandSink sink, TransitionNode transition) BuildTestAsset(
        string? expressionTargetField = null,
        bool autoManaged = false)
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var transVisualId = Guid.NewGuid();

        var rootNode = new StateNode("__root__");
        var source   = new StateNode("Idle")   { StableId = sourceId,  Parent = rootNode };
        var target   = new StateNode("Active") { StableId = targetId,  Parent = rootNode };
        rootNode.Children.Add(source);
        rootNode.Children.Add(target);

        var transition = new TransitionNode
        {
            VisualId              = transVisualId,
            Source                = source,
            Target                = target,
            ExpressionTargetField = expressionTargetField,
        };
        source.OutgoingTransitions.Add(transition);

        var asset = new HsmAsset(
            Guid.NewGuid(), "Test", "", false, "",
            new HsmDefinitionBlob(), new MachineMetadata(),
            rootNode,
            new List<StateNode> { source, target },
            new List<TransitionNode> { transition },
            new List<GlobalTransitionNode>(),
            new List<RegionNode>(),
            new List<EventDefinition>());

        if (expressionTargetField != null)
        {
            asset.AddVariable(new BlackboardVariableEntry(
                expressionTargetField,
                typeof(float),
                null,
                IsAutoManaged: autoManaged));
        }

        var sink = new HsmCommandSink(asset);
        return (asset, sink, transition);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void RemoveTransitionLink_WithAutoManagedVar_RemovesVar()
    {
        string varName = "_auto_trans1";
        var (asset, sink, transition) = BuildTestAsset(varName, autoManaged: true);

        asset.BlackboardVariables.Should().ContainSingle(v => v.Name == varName);

        sink.Apply(new GraphCommand.RemoveLinks(new[] { new LinkId(transition.VisualId) }));

        asset.BlackboardVariables.Should().BeEmpty(
            "removing the owning transition must remove its auto-managed variable");
    }

    [Fact]
    public void RemoveTransitionLink_SharedVar_DoesNotRemoveVar()
    {
        string varName = "sharedVar";
        var (asset, sink, transition) = BuildTestAsset(varName, autoManaged: false);

        asset.BlackboardVariables.Should().ContainSingle(v => v.Name == varName);

        sink.Apply(new GraphCommand.RemoveLinks(new[] { new LinkId(transition.VisualId) }));

        asset.BlackboardVariables.Should().ContainSingle(v => v.Name == varName,
            "a shared (non-auto-managed) variable must NOT be deleted when its transition is removed");
    }

    [Fact]
    public void RemoveTransitionLink_NoExpressionTargetField_NoVarRemoved()
    {
        var (asset, sink, transition) = BuildTestAsset(expressionTargetField: null, autoManaged: false);
        asset.AddVariable(new BlackboardVariableEntry("unrelated", typeof(int), null));

        sink.Apply(new GraphCommand.RemoveLinks(new[] { new LinkId(transition.VisualId) }));

        asset.BlackboardVariables.Should().ContainSingle(v => v.Name == "unrelated",
            "transitions without ExpressionTargetField must not affect other variables");
    }

    [Fact]
    public void RemoveTransitionLink_AutoManagedVar_AssetIsMarkedDirty()
    {
        string varName = "_auto_t1";
        var (asset, sink, transition) = BuildTestAsset(varName, autoManaged: true);
        asset.ClearDirty();

        sink.Apply(new GraphCommand.RemoveLinks(new[] { new LinkId(transition.VisualId) }));

        asset.IsDirty.Should().BeTrue(
            "removing an auto-managed variable must mark the asset dirty (triggers re-pack on next BuildViewModel)");
    }

    [Fact]
    public void RemoveTransitionLink_AutoManagedVar_TransitionRemovedFromSource()
    {
        string varName = "_auto_t2";
        var (asset, sink, transition) = BuildTestAsset(varName, autoManaged: true);
        var source = transition.Source;

        source.OutgoingTransitions.Should().ContainSingle();

        sink.Apply(new GraphCommand.RemoveLinks(new[] { new LinkId(transition.VisualId) }));

        source.OutgoingTransitions.Should().BeEmpty(
            "the transition must be removed from the source state's outgoing list");
    }
}
