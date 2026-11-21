using System;
using Hrot.Editor.AiShared;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector.ActionBinding;
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
///   1. dispatcher.GetFacet  (each binding facet carries its site id — ⭐ CE-417 slice 4b, no side channel)
///   2. sources.Promote(facet.Action)  → returns newName
///   3. Build edited facet with Action.ExpressionTargetField = newName
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
            .FirstOrDefault(t => (t.Action?.MethodFqn) == actionFqn)
            ?? asset.AllTransitions.First();

        return (asset, transition.VisualId);
    }

    // ── Promote creates variable and sets ExpressionTargetField ──────────────

    [Fact]
    public void Promote_CreatesVar_AndFacetApply_SetsExpressionTargetField_Hsm()
    {
        const string fqn = "Ns.FloatAction";
        var (asset, transitionVisualId) = MakeAssetWithTransition(fqn);
        var entry      = new ActionSchemaEntry(fqn, typeof(float), ActionHosting.Hsm, BlackboardAccess.ReadWrite);
        var exporter   = new StubExporter(entry);
        var dispatcher = new HsmFacetDispatcher(asset);
        var sources    = new ActionBindingSources(asset, _ => Array.Empty<string>(), exporter);

        // Step 1: Get facet (its action binding carries the transition id).
        var sel   = new HsmTransitionSelection(transitionVisualId);
        var facet = (TransitionFacet)dispatcher.GetFacet(sel)!;

        // Step 2: Simulate DrawInput clicking "Promote".
        facet.Action.SiteId.Should().Be(transitionVisualId.ToString(), "the mapper must carry the transition id");
        var newName = sources.Promote(facet.Action);
        newName.Should().NotBeNull("Promote must succeed for a known FQN");

        // Step 3: Apply the facet with the new name bound.
        facet.Action.ExpressionTargetField = newName;
        dispatcher.ApplyFacet(sel, facet);

        // Assert: auto-variable created in asset.
        var created = asset.BlackboardVariables.Should().ContainSingle().Subject;
        created.Name.Should().Be(newName);
        created.FieldType.Should().Be(typeof(float));
        created.IsAutoManaged.Should().BeTrue();

        // Assert: ExpressionTargetField persisted on the transition.
        var transition = asset.FindTransitionByVisualId(transitionVisualId)!;
        (transition.Action?.ExpressionTargetField).Should().Be(newName,
            "ApplyFacet must persist ExpressionTargetField from the edited transition facet");
    }

    [Fact]
    public void Promote_AndApplyFacet_BindingSurvivesRoundTrip_Hsm()
    {
        const string fqn = "Ns.IntAction";
        var (asset, transitionVisualId) = MakeAssetWithTransition(fqn);
        var entry      = new ActionSchemaEntry(fqn, typeof(int), ActionHosting.Hsm, BlackboardAccess.ReadWrite);
        var exporter   = new StubExporter(entry);
        var dispatcher = new HsmFacetDispatcher(asset);
        var sources    = new ActionBindingSources(asset, _ => Array.Empty<string>(), exporter);

        // Simulate promote + bind.
        var sel   = new HsmTransitionSelection(transitionVisualId);
        var facet = (TransitionFacet)dispatcher.GetFacet(sel)!;
        var name  = sources.Promote(facet.Action)!;
        facet.Action.ExpressionTargetField = name;
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
        (restoredTransition.Action?.ExpressionTargetField).Should().Be(name,
            "ExpressionTargetField must survive HSM model→DTO→model round-trip");
    }

    [Fact]
    public void Promote_SecondCallSameId_IsIdempotent_Hsm()
    {
        const string fqn = "Ns.FloatAction";
        var (asset, transitionVisualId) = MakeAssetWithTransition(fqn);
        var entry      = new ActionSchemaEntry(fqn, typeof(float), ActionHosting.Hsm, BlackboardAccess.ReadWrite);
        var exporter   = new StubExporter(entry);
        var dispatcher = new HsmFacetDispatcher(asset);
        var sources    = new ActionBindingSources(asset, _ => Array.Empty<string>(), exporter);

        var sel   = new HsmTransitionSelection(transitionVisualId);
        var facet = (TransitionFacet)dispatcher.GetFacet(sel)!;
        var name1 = sources.Promote(facet.Action)!;
        var name2 = sources.Promote(facet.Action)!;

        name1.Should().Be(name2, "same visualId must always produce the same auto-name");
        asset.BlackboardVariables.Should().HaveCount(1, "second promote is idempotent — no duplicate");
    }

    /// <summary>⭐ <c>CE-417</c> slice 4b — every binding facet carries its site; a node's secondary binding carries its slot,
    /// so its promoted variable never collides with the primary one (B-2).</summary>
    [Fact]
    public void EveryBindingFacet_CarriesItsSiteAndSlot_Hsm()
    {
        var (asset, transitionVisualId) = MakeAssetWithTransition("Ns.BoolAction");
        var dispatcher = new HsmFacetDispatcher(asset);

        var tf = (TransitionFacet)dispatcher.GetFacet(new HsmTransitionSelection(transitionVisualId))!;
        tf.Action.SiteId.Should().Be(transitionVisualId.ToString());
        tf.Action.SiteSlot.Should().BeNull("the action is the transition's primary binding");
        tf.Guard.SiteId.Should().Be(transitionVisualId.ToString());
        tf.Guard.SiteSlot.Should().Be("guard");

        var idle = asset.AllStates.First(s => s.Name == "Idle");
        var sf   = (StateFacet)dispatcher.GetFacet(new HsmStateSelection(idle.StableId))!;
        sf.Activity.SiteId.Should().Be(idle.StableId.ToString());
        sf.Activity.SiteSlot.Should().BeNull("the activity is the state's primary binding");
        // ⛔ HSM-012 — the Timer slot left the facet (the kernel never arms a timer), so only the two
        //    secondary slots that an author can still reach are asserted here.
        new[] { sf.OnEntry.SiteSlot, sf.OnExit.SiteSlot }
            .Should().Equal("entry", "exit");
    }
}
