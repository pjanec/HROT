using System;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Selection;
using Hrot.Hsm.Editor.Inspector;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Inspector;

/// <summary>
/// Corrective Task 0: headless tests proving that the Promote gesture creates an
/// auto-variable AND binds ExpressionTargetField via the HSM ApplyFacet path.
///
/// The ImGui button click is replaced by the equivalent headless sequence:
///   1. dispatcher.GetFacet  (populates fqnContext.CurrentVisualId)
///   2. drawer.Promote(visualId)  → returns newName
///   3. Build edited facet with ExpressionTargetField = newName
///   4. dispatcher.ApplyFacet  → persists into asset
/// </summary>
public sealed class HsmPromoteBindTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private sealed class StubExporter : IActionSchemaExporter
    {
        private readonly Dictionary<string, ActionSchemaEntry> _map;
        public IReadOnlyDictionary<string, ActionSchemaEntry> All => _map;
        public event Action? Changed { add { } remove { } }
        public StubExporter(params ActionSchemaEntry[] entries)
        {
            _map = new Dictionary<string, ActionSchemaEntry>(StringComparer.Ordinal);
            foreach (var e in entries) _map[e.Fqn] = e;
        }
        public ActionSchemaEntry? Lookup(string fqn) => _map.GetValueOrDefault(fqn);
        public void Rebuild() { }
    }

    private static (HsmDefinitionBlob blob, MachineMetadata meta) Compile(HsmBuilder b)
    {
        var graph = b.Build();
        HsmNormalizer.Normalize(graph);
        var flat = HsmFlattener.Flatten(graph);
        return (HsmEmitter.Emit(flat), HsmEmitter.BuildMachineMetadata(graph));
    }

    /// <summary>
    /// Build an HSM asset with a transition that has an action function,
    /// and return the asset and the transition's VisualId.
    /// </summary>
    private static (HsmAsset asset, Guid transitionVisualId) MakeAssetWithTransition(
        string actionFqn = "Ns.FloatAction")
    {
        var b = new HsmBuilder("T");
        b.Event("Fire", 1);
        b.State("Active").Final();
        b.State("Idle").Initial()
            .On("Fire").GoTo("Active").Action(actionFqn);
        var (blob, meta) = Compile(b);
        var asset = HsmAssetProjector.Project(blob, meta, null, Guid.NewGuid(), "T", "", false, "");

        var transition = asset.AllTransitions
            .FirstOrDefault(t => t.ActionFunction == actionFqn)
            ?? asset.AllTransitions.First();

        return (asset, transition.VisualId);
    }

    // ── Promote creates variable and sets ExpressionTargetField ──────────────

    [Fact]
    public void Promote_CreatesVar_AndFacetApply_SetsExpressionTargetField_Hsm()
    {
        const string fqn = "Ns.FloatAction";
        var (asset, transitionVisualId) = MakeAssetWithTransition(fqn);
        var entry      = new ActionSchemaEntry(fqn, typeof(float), ActionHosting.Hsm, BlackboardAccess.ReadWrite, null);
        var exporter   = new StubExporter(entry);
        var ctx        = new HsmFacetFqnContext { CurrentActionFqn = fqn };
        var dispatcher = new HsmFacetDispatcher(asset, ctx);
        var drawer     = new HsmBlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn, ctx);

        // Step 1: Get facet (populates CurrentVisualId via mapper).
        var sel   = new HsmTransitionSelection(transitionVisualId);
        var facet = (TransitionFacet)dispatcher.GetFacet(sel)!;

        // Step 2: Simulate DrawInput clicking "Promote".
        var visualId = ctx.CurrentVisualId;
        visualId.Should().Be(transitionVisualId.ToString(), "mapper must populate CurrentVisualId");
        var newName = drawer.Promote(visualId!);
        newName.Should().NotBeNull("Promote must succeed for a known FQN");

        // Step 3: Apply the facet with the new name bound.
        facet.ExpressionTargetField = newName;
        dispatcher.ApplyFacet(sel, facet);

        // Assert: auto-variable created in asset.
        var created = asset.BlackboardVariables.Should().ContainSingle().Subject;
        created.Name.Should().Be(newName);
        created.FieldType.Should().Be(typeof(float));
        created.IsAutoManaged.Should().BeTrue();

        // Assert: ExpressionTargetField persisted on the transition.
        var transition = asset.FindTransitionByVisualId(transitionVisualId)!;
        transition.ExpressionTargetField.Should().Be(newName,
            "ApplyFacet must persist ExpressionTargetField from the edited transition facet");
    }

    [Fact]
    public void Promote_AndApplyFacet_BindingSurvivesRoundTrip_Hsm()
    {
        const string fqn = "Ns.IntAction";
        var (asset, transitionVisualId) = MakeAssetWithTransition(fqn);
        var entry      = new ActionSchemaEntry(fqn, typeof(int), ActionHosting.Hsm, BlackboardAccess.ReadWrite, null);
        var exporter   = new StubExporter(entry);
        var ctx        = new HsmFacetFqnContext { CurrentActionFqn = fqn };
        var dispatcher = new HsmFacetDispatcher(asset, ctx);
        var drawer     = new HsmBlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn, ctx);

        // Simulate promote + bind.
        var sel   = new HsmTransitionSelection(transitionVisualId);
        var facet = (TransitionFacet)dispatcher.GetFacet(sel)!;
        var name  = drawer.Promote(ctx.CurrentVisualId!)!;
        facet.ExpressionTargetField = name;
        dispatcher.ApplyFacet(sel, facet);

        // Round-trip through DTO.
        var restored = HsmAssetMapper.FromDto(HsmAssetMapper.ToDto(asset));

        // Auto-variable survived.
        var restoredVar = restored.BlackboardVariables.Should().ContainSingle().Subject;
        restoredVar.Name.Should().Be(name, "auto-variable must survive DTO round-trip");
        restoredVar.IsAutoManaged.Should().BeTrue();

        // ExpressionTargetField preserved on transition.
        var restoredTransition = restored.FindTransitionByVisualId(transitionVisualId)!;
        restoredTransition.Should().NotBeNull("transition must exist in restored asset");
        restoredTransition.ExpressionTargetField.Should().Be(name,
            "ExpressionTargetField must survive HSM model→DTO→model round-trip");
    }

    [Fact]
    public void Promote_SecondCallSameId_IsIdempotent_Hsm()
    {
        const string fqn = "Ns.FloatAction";
        var (asset, transitionVisualId) = MakeAssetWithTransition(fqn);
        var entry      = new ActionSchemaEntry(fqn, typeof(float), ActionHosting.Hsm, BlackboardAccess.ReadWrite, null);
        var exporter   = new StubExporter(entry);
        var ctx        = new HsmFacetFqnContext { CurrentActionFqn = fqn };
        var dispatcher = new HsmFacetDispatcher(asset, ctx);
        var drawer     = new HsmBlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn, ctx);

        var sel = new HsmTransitionSelection(transitionVisualId);
        dispatcher.GetFacet(sel);
        var name1 = drawer.Promote(ctx.CurrentVisualId!)!;
        var name2 = drawer.Promote(ctx.CurrentVisualId!)!;

        name1.Should().Be(name2, "same visualId must always produce the same auto-name");
        asset.BlackboardVariables.Should().HaveCount(1, "second promote is idempotent — no duplicate");
    }

    [Fact]
    public void FqnContext_CurrentVisualId_IsSetByMapper_Hsm()
    {
        const string fqn = "Ns.BoolAction";
        var (asset, transitionVisualId) = MakeAssetWithTransition(fqn);
        var ctx        = new HsmFacetFqnContext { CurrentActionFqn = fqn };
        var dispatcher = new HsmFacetDispatcher(asset, ctx);

        ctx.CurrentVisualId.Should().BeNull("not set yet before GetFacet");

        dispatcher.GetFacet(new HsmTransitionSelection(transitionVisualId));

        ctx.CurrentVisualId.Should().Be(transitionVisualId.ToString(),
            "mapper.GetFacet must write CurrentVisualId to the shared context");
    }

    [Fact]
    public void FqnContext_CurrentVisualId_ClearedOnNonTransitionSelection_Hsm()
    {
        const string fqn = "Ns.FloatAction";
        var (asset, transitionVisualId) = MakeAssetWithTransition(fqn);
        var ctx        = new HsmFacetFqnContext { CurrentActionFqn = fqn };
        var dispatcher = new HsmFacetDispatcher(asset, ctx);

        // Prime the context with the transition.
        dispatcher.GetFacet(new HsmTransitionSelection(transitionVisualId));
        ctx.CurrentVisualId.Should().NotBeNull("set after transition GetFacet");

        // Select a state instead.
        var idleState = asset.AllStates.First(s => s.Name == "Idle");
        dispatcher.GetFacet(new HsmStateSelection(idleState.StableId));

        ctx.CurrentVisualId.Should().BeNull("CurrentVisualId must be cleared on non-transition selection");
        ctx.CurrentActionFqn.Should().BeNull("CurrentActionFqn must also be cleared");
    }
}
