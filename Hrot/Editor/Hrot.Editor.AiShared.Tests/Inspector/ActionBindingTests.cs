using System;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Compiler;
using FluentAssertions;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Inspector.ActionBinding;
using Hrot.Hsm.Editor.Model;
using StructEdit.Core;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Inspector;

/// <summary>
/// ⭐⭐⭐ <c>CE-417</c> slice 4b — the ONE binding facet, drawer and applier, both hosts.
/// 📄 <c>docs/blueprints/DESIGN_Behavior_Action_Binding.md</c> §5.4.
///
/// <para>The rules here were spelled once per slot in <c>HsmFacetDispatcher</c> (pick→Guid, CE-414 compose) and twice
/// in the BTree/HSM variable pickers (the compatibility rule); each is now one implementation and pinned once.</para>
/// </summary>
public sealed class ActionBindingTests
{
    // ── fixtures ──────────────────────────────────────────────────────────────

    /// <summary>A stand-in for a blueprint's generated <c>Params</c> struct.</summary>
    public struct PatrolParams { public float Speed; public int Laps; }

    private const string PatrolTickCore = "Hrot.Generated.Patrol_1A2B3C4D_Bp.TickCore";

    private sealed class Exporter : IActionSchemaExporter
    {
        private readonly Dictionary<string, ActionSchemaEntry> _map = new(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, ActionSchemaEntry> All => _map;
        public event Action? Changed { add { } remove { } }
        public ActionSchemaEntry? Lookup(string fqn) => _map.GetValueOrDefault(fqn);
        public void Rebuild() { }

        public Exporter Method(string fqn, Type dto)
        {
            _map[fqn] = new ActionSchemaEntry(fqn, dto, ActionHosting.Hsm | ActionHosting.HsmActivity, BlackboardAccess.ReadWrite);
            return this;
        }

        public Exporter Blueprint(string tickCoreFqn, Type paramsType)
        {
            _map[tickCoreFqn] = new ActionSchemaEntry(tickCoreFqn, paramsType,
                ActionHosting.Hsm | ActionHosting.HsmActivity, BlackboardAccess.ReadWrite, IsAiPrimitive: true);
            return this;
        }
    }

    private sealed class CatalogAsset : IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "";
        public AssetKind Kind { get; init; } = AssetKind.Blueprint;
        public string SourceFilePath => "/x.bp.json";
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    private sealed class Catalog : IAssetCatalog
    {
        private readonly List<IEditableAsset> _a;
        public Catalog(params IEditableAsset[] assets) => _a = assets.ToList();
        public IReadOnlyList<IEditableAsset> All => _a;
        public IEditableAsset? FindByAssetId(Guid id) => _a.FirstOrDefault(x => x.AssetId == id);
        public IEditableAsset? FindByName(string n) => _a.FirstOrDefault(x => x.Name == n);
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid id) => Array.Empty<IEditableAsset>();
#pragma warning disable 67
        public event Action<AssetKind>? Changed;
#pragma warning restore 67
    }

    private static HsmAsset MakeAsset(params BlackboardVariableEntry[] vars)
    {
        var b = new HsmBuilder("T");
        b.State("Idle").Initial().Final();
        var graph = b.Build();
        HsmNormalizer.Normalize(graph);
        var flat  = HsmFlattener.Flatten(graph);
        var asset = HsmAssetProjector.Project(
            HsmEmitter.Emit(flat), HsmEmitter.BuildMachineMetadata(graph), null, Guid.NewGuid(), "T", "", false, "");
        if (vars.Length > 0) asset.SetBlackboardVariables(vars);
        return asset;
    }

    private static BlackboardVariableEntry Var(string name, Type t) => new(name, t, null);

    // ── the compatibility rule ────────────────────────────────────────────────

    [Fact]
    public void ParameterType_IsTheMethodsDto_OrTheBlueprintsParams_OrUnknown()
    {
        var exporter = new Exporter().Method("Ns.Float", typeof(float)).Blueprint(PatrolTickCore, typeof(PatrolParams));

        ActionBindingCompatibility.ParameterType(exporter, "Ns.Float", null).Should().Be(typeof(float));
        ActionBindingCompatibility.ParameterType(exporter, null, "Patrol").Should().Be(typeof(PatrolParams),
            "a blueprint binding's variable is the blueprint's generated Params — the CE-414 compose type");
        ActionBindingCompatibility.ParameterType(exporter, "Ns.Unknown", null).Should().BeNull();
        ActionBindingCompatibility.ParameterType(exporter, null, null).Should().BeNull();
        ActionBindingCompatibility.ParameterType(null, "Ns.Float", null).Should().BeNull("no exporter, no type");
    }

    [Fact]
    public void CompatibleVariables_FilterByType_AndListEverything_WhenTheTypeIsUnknown()
    {
        var vars = new[] { Var("z", typeof(float)), Var("a", typeof(int)), Var("m", typeof(float)) };

        ActionBindingCompatibility.CompatibleVariables(typeof(float), vars).Should().Equal("m", "z");
        ActionBindingCompatibility.CompatibleVariables(null, vars).Should().Equal("a", "m", "z");
        ActionBindingCompatibility.CompatibleVariables(typeof(double), vars).Should().BeEmpty();
    }

    [Fact]
    public void ABlueprintBinding_ListsTheVariablesOfItsParamsType()
    {
        var asset   = MakeAsset(Var("patrol", typeof(PatrolParams)), Var("speed", typeof(float)));
        var sources = new ActionBindingSources(asset, _ => Array.Empty<string>(),
                                               new Exporter().Blueprint(PatrolTickCore, typeof(PatrolParams)));

        sources.GetVariables(new BehaviorActionBindingFacet { BlueprintName = "Patrol" }).Should().Equal("patrol");
    }

    // ── the drawer's pick rules ───────────────────────────────────────────────

    [Fact]
    public void PickingAMethod_ClearsTheBlueprint_AndViceVersa()
    {
        var b = new BehaviorActionBindingFacet { BlueprintName = "Patrol", ExpressionTargetField = "v" };

        ActionBindingDrawer.PickMethod(ref b, "Ns.Chase");
        b.MethodFqn.Should().Be("Ns.Chase");
        b.BlueprintName.Should().BeNull("method XOR blueprint (design §9 ③)");
        b.ExpressionTargetField.Should().Be("v", "the target variable is not part of that choice");

        ActionBindingDrawer.PickBlueprint(ref b, "Patrol");
        b.BlueprintName.Should().Be("Patrol");
        b.MethodFqn.Should().BeNull();

        ActionBindingDrawer.PickBlueprint(ref b, "");
        b.BlueprintName.Should().BeNull("(none) clears");
        ActionBindingDrawer.PickVariable(ref b, "");
        b.ExpressionTargetField.Should().BeNull();
    }

    [Fact]
    public void AFieldWithNoSlotAttribute_IsAnActionSlot_WithoutBlueprints()
    {
        var slot = ActionBindingDrawer.SlotOf(null);

        slot.Kind.Should().Be(BindingSlotKind.Action);
        slot.AllowsBlueprint.Should().BeFalse();
    }

    // ── the field editor ──────────────────────────────────────────────────────

    [Fact]
    public void TheFieldEditor_MakesTheBindingOneCustomLeaf()
    {
        var node = new BehaviorActionBindingFieldEditor().CreateNode(
            new EditNodeId(7), "Activity", "$.Activity", null!, new EditNodeMetadata());

        node!.Kind.Should().Be(EditNodeKind.Custom);
        node.ClrType.Should().Be(typeof(BehaviorActionBindingFacet));
    }

    // ── the applier ───────────────────────────────────────────────────────────

    private static ActionBindingApplyContext Ctx(HsmAsset asset, IAssetCatalog? catalog = null,
                                                 IActionSchemaExporter? exporter = null,
                                                 string? compose = "bpActivityParams", bool keep = false)
        => new(asset, catalog, exporter, compose, keep);

    /// <summary>⭐ §11.1a — the Guid is captured AT PICK TIME, while the catalogue entry is in hand.</summary>
    [Fact]
    public void ABlueprintPick_CapturesItsGuid_FromTheCatalogue()
    {
        var bp = new CatalogAsset { Name = "Patrol" };

        var b = BehaviorActionBindingEditor.Apply(
            null, new BehaviorActionBindingFacet { BlueprintName = "Patrol" }, Ctx(MakeAsset(), new Catalog(bp)));

        b!.BlueprintName.Should().Be("Patrol");
        b.BlueprintAssetId.Should().Be(bp.AssetId);
    }

    /// <summary>
    /// ⭐ §7.1a ③ — never erase. A name the catalogue cannot see keeps the binding's current Guid, and with no catalogue at
    /// all the picked name is still kept (Guid empty) — it names what the author picked.
    /// </summary>
    [Fact]
    public void AnUnresolvedPick_NeverErases()
    {
        var kept    = Guid.NewGuid();
        var current = new BehaviorActionBinding { BlueprintName = "Patrol", BlueprintAssetId = kept };

        BehaviorActionBindingEditor.Apply(current, new BehaviorActionBindingFacet { BlueprintName = "Patrol" },
                                          Ctx(MakeAsset(), new Catalog()))!
            .BlueprintAssetId.Should().Be(kept);

        var noCatalogue = BehaviorActionBindingEditor.Apply(
            null, new BehaviorActionBindingFacet { BlueprintName = "Patrol" }, Ctx(MakeAsset()));
        noCatalogue.Should().NotBeNull("a picked name with no catalogue is still a pick");
        noCatalogue!.BlueprintName.Should().Be("Patrol");
        noCatalogue.BlueprintAssetId.Should().Be(Guid.Empty);
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-414</c> — a CHANGED blueprint pick composes ONE variable typed by the blueprint's Params and binds it
    /// as this binding's target; re-applying the same pick composes nothing; clearing drops the editor-owned variable.
    /// </summary>
    [Fact]
    public void ABlueprintPick_ComposesItsParamsVariable_OnlyOnAChange()
    {
        var asset    = MakeAsset();
        var exporter = new Exporter().Blueprint(PatrolTickCore, typeof(PatrolParams));
        var ctx      = Ctx(asset, exporter: exporter);

        var b = BehaviorActionBindingEditor.Apply(null, new BehaviorActionBindingFacet { BlueprintName = "Patrol" }, ctx)!;

        var composed = asset.BlackboardVariables.Should().ContainSingle().Subject;
        composed.FieldType.Should().Be(typeof(PatrolParams));
        composed.IsAutoManaged.Should().BeTrue();
        b.ExpressionTargetField.Should().Be(composed.Name);

        // the inspector round-trips the facet every edit — an unchanged pick must not compose again: the same entry
        // survives, and a variable the author DELETED is not resurrected by the next unrelated edit
        b = BehaviorActionBindingEditor.Apply(b, BehaviorActionBindingEditor.ToFacet(b, null), ctx)!;
        asset.BlackboardVariables.Should().ContainSingle().Which.Should().BeSameAs(composed);
        b.ExpressionTargetField.Should().Be(composed.Name);
        asset.RemoveVariable(composed.Name);
        b = BehaviorActionBindingEditor.Apply(b, BehaviorActionBindingEditor.ToFacet(b, null), ctx)!;
        asset.BlackboardVariables.Should().BeEmpty("re-composing an unchanged pick would resurrect a deleted variable");
        asset.AddVariable(composed);   // restore for the clear step below

        // clearing the pick (the drawer's "(none)": the blueprint goes, the facet still names the composed variable)
        // drops the variable the EDITOR owns, and unbinds
        var facet = BehaviorActionBindingEditor.ToFacet(b, null);
        ActionBindingDrawer.PickBlueprint(ref facet, null);
        var cleared = BehaviorActionBindingEditor.Apply(b, facet, ctx);
        asset.BlackboardVariables.Should().BeEmpty();
        cleared.Should().BeNull("an HSM slot holding nothing is unbound");
    }

    [Fact]
    public void ASlotThatCannotHostABlueprint_NeverComposes()
    {
        var asset = MakeAsset();

        BehaviorActionBindingEditor.Apply(null, new BehaviorActionBindingFacet { BlueprintName = "Patrol" },
            Ctx(asset, exporter: new Exporter().Blueprint(PatrolTickCore, typeof(PatrolParams)), compose: null));

        asset.BlackboardVariables.Should().BeEmpty();
    }

    /// <summary>⛔ The compose never removes a variable the AUTHOR owns, even when it was the binding's target.</summary>
    [Fact]
    public void ARepick_KeepsAnAuthorOwnedVariable()
    {
        var asset = MakeAsset(Var("mine", typeof(PatrolParams)));
        var b     = new BehaviorActionBinding { BlueprintName = "Old", ExpressionTargetField = "mine" };

        BehaviorActionBindingEditor.Apply(b, new BehaviorActionBindingFacet { BlueprintName = "Patrol", ExpressionTargetField = "mine" },
                                          Ctx(asset, exporter: new Exporter()));

        asset.BlackboardVariables.Select(v => v.Name).Should().Contain("mine");
    }

    [Fact]
    public void AnEmptyFacet_UnbindsAnHsmSlot_ButKeepsABTreeNodesBinding()
    {
        var asset = MakeAsset();

        BehaviorActionBindingEditor.Apply(new BehaviorActionBinding { MethodFqn = "Ns.X" }, default, Ctx(asset, compose: null))
            .Should().BeNull();
        BehaviorActionBindingEditor.Apply(new BehaviorActionBinding { MethodFqn = "Ns.X" }, default, Ctx(asset, compose: null, keep: true))
            .Should().NotBeNull("a BTree action node always carries its binding");
    }

    /// <summary>⭐ A target variable authored BEFORE any method is picked is kept (the CE-401 seed-first case).</summary>
    [Fact]
    public void AVariableAuthoredBeforeAMethod_IsKept()
    {
        var b = BehaviorActionBindingEditor.Apply(
            null, new BehaviorActionBindingFacet { ExpressionTargetField = "Alpha" }, Ctx(MakeAsset(), compose: null));

        b!.ExpressionTargetField.Should().Be("Alpha");
        b.NamesNothing.Should().BeTrue();
    }

    /// <summary>The facet does not carry the working-state fields, so the apply must not lose them.</summary>
    [Fact]
    public void Apply_PreservesTheWorkingStateFields()
    {
        var current = new BehaviorActionBinding
        {
            MethodFqn = "Ns.X", WorkingStateTypeId = "Ns.Ws", WorkingStateTargetField = "ws",
        };

        var b = BehaviorActionBindingEditor.Apply(current, new BehaviorActionBindingFacet { MethodFqn = "Ns.Y" },
                                                  Ctx(MakeAsset(), compose: null))!;

        b.Should().BeSameAs(current, "the binding is edited in place");
        b.MethodFqn.Should().Be("Ns.Y");
        b.WorkingStateTypeId.Should().Be("Ns.Ws");
        b.WorkingStateTargetField.Should().Be("ws");
    }

    [Fact]
    public void ToFacet_CarriesTheBindingAndTheSite()
    {
        var site = Guid.NewGuid().ToString();
        var f = BehaviorActionBindingEditor.ToFacet(
            new BehaviorActionBinding { MethodFqn = "Ns.X", BlueprintName = "P", ExpressionTargetField = "v" },
            site, "guard", targetsWholeBlackboard: true);

        f.MethodFqn.Should().Be("Ns.X");
        f.BlueprintName.Should().Be("P");
        f.ExpressionTargetField.Should().Be("v");
        f.SiteId.Should().Be(site);
        f.SiteSlot.Should().Be("guard");
        f.TargetsWholeBlackboard.Should().BeTrue();
        BehaviorActionBindingEditor.ToFacet(null, site).MethodFqn.Should().BeNull();
    }

    // ── CE-417 §6 "one carrier" (Q75 §6) ─────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-417</c> rail "one carrier" — no type but the binding record (DTO, model, facet) carries a
    /// <c>MethodFqn</c> + <c>ExpressionTargetField</c> pair.</b> 📄 <c>DESIGN_Behavior_Action_Binding.md</c> §6;
    /// <c>Architect_Question_75_…</c> §6 (decision B).
    ///
    /// <para>🔴 <b>What it guards.</b> Before slice 2 the pair was spelled on four payload DTOs, the flat HSM DTO fields, both
    /// editor models and every facet — each with its own copy of the migration and naming rules. ⛔ A new class that grows
    /// both members is a second carrier again, however well-meant.</para>
    ///
    /// <para>⚠ <b>Scope is explicit</b>: every assembly that authors, persists or emits a binding is named by an anchor type,
    /// so a missing reference fails the compile rather than silently shrinking the scan. <c>Hrot.Utility.Editor</c> carries
    /// no binding (measured: no <c>MethodFqn</c> member) and this project does not reference it.</para>
    ///
    /// <para>✅ <b>Red-proof</b>: add <c>public string? MethodFqn;</c> to <c>StateNode</c> ⇒ this rail reddens.</para>
    /// </summary>
    [Fact]
    public void OneCarrier_OnlyTheBindingRecordCarriesAMethodAndATargetField()
    {
        var anchors = new[]
        {
            typeof(BehaviorActionBinding),                                         // Hrot.Editor.AiShared
            typeof(Hrot.AiEditor.Persistence.BehaviorActionBindingDto),            // Hrot.AiEditor.Persistence
            typeof(StateNode),                                                     // Hrot.Hsm.Editor
            typeof(Hrot.BTree.Editor.Model.BehaviorTreeAsset),                     // Hrot.BTree.Editor
            typeof(Hrot.Blueprints.Editor.Catalog.BlueprintAssetContributor),      // Hrot.Blueprints.Editor
        };
        var allowed = new HashSet<Type>
        {
            typeof(BehaviorActionBinding),
            typeof(Hrot.AiEditor.Persistence.BehaviorActionBindingDto),
            typeof(BehaviorActionBindingFacet),
        };

        const System.Reflection.BindingFlags Members =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly;
        static bool Has(Type t, string name, System.Reflection.BindingFlags f)
            => t.GetField(name, f) != null || t.GetProperty(name, f) != null;

        var carriers = anchors.Select(a => a.Assembly).Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => Has(t, "MethodFqn", Members) && Has(t, "ExpressionTargetField", Members))
            .ToList();

        carriers.Should().BeEquivalentTo(allowed,
            "Q75 §6: one binding carrier — a type that holds both a method and its target field is a second carrier");
    }
}
