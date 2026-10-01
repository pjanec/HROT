using System;
using System.Collections.Generic;
using System.Linq;
using Fbt;
using Fdp.Toolkit.Behavior;
using FluentAssertions;
using Hrot.BTree.Editor.Inspector;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Inspector.ActionBinding;
using StructEdit.Core;
using StructEdit.Core.Attributes;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Inspector;

/// <summary>
/// AIE-024 tests for BTree field picker drawers.
/// All logic-level: no ImGui context required.
/// </summary>
public sealed class BTreePickerDrawerTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName        = "T",
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeAsset(BehaviorTreeBlob blob) =>
        BehaviorTreeAssetProjector.Project(
            blob, null, null,
            Guid.NewGuid(), blob.TreeName, "/t.cs", false, "", "");

    /// <summary>⭐ <c>CE-417</c> slice 4b — the method list and the variable list now come from the ONE binding drawer the
    /// production factory registers; the BTree host supplies only its method list (the behavior registry).</summary>
    private static ActionBindingSources Sources(BehaviorRegistry registry, BehaviorTreeAsset? asset = null)
        => ((ActionBindingDrawer)BTreePickerDrawerFactory.BuildDrawers(asset ?? MakeAsset(EmptyBlob()), registry)
               [typeof(BehaviorActionBindingFacet)]).Sources;

    private static void RegisterName(BehaviorRegistry registry, string name, int id)
    {
        var def = new BehaviorDefinition
        {
            Name      = name,
            BrainTier = 0,
        };
        registry.Register(id, name, def);
    }

    // ── the method list (was BehaviorHashPickerDrawer) ─────────────────────────

    [Fact]
    public void FieldPicker_BehaviorHash_ListsRegistryNames()
    {
        var registry = new BehaviorRegistry();
        RegisterName(registry, "Ns.Class.RunAway", 1);
        RegisterName(registry, "Ns.Class.Patrol",  2);

        var items    = Sources(registry).GetMethods(BindingSlotKind.Action);

        items.Should().Contain("Ns.Class.RunAway");
        items.Should().Contain("Ns.Class.Patrol");
        items.Should().HaveCount(2);
    }

    [Fact]
    public void FieldPicker_BehaviorHash_EmptyRegistry_ReturnsEmpty()
    {
        var registry = new BehaviorRegistry();
        Sources(registry).GetMethods(BindingSlotKind.Guard).Should().BeEmpty();
    }

    [Fact]
    public void FieldPicker_BehaviorHash_ItemsSorted()
    {
        var registry = new BehaviorRegistry();
        RegisterName(registry, "Z.Method", 3);
        RegisterName(registry, "A.Method", 1);
        RegisterName(registry, "M.Method", 2);

        var items    = Sources(registry).GetMethods(BindingSlotKind.Action);

        items.Should().BeInAscendingOrder("items must be sorted alphabetically");
    }

    // ── the variable list (was BlackboardFieldPickerDrawer) ────────────────────

    [Fact]
    public void FieldPicker_BlackboardField_ListsActiveAssetFields()
    {
        var asset = MakeAsset(EmptyBlob());
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("Health",    typeof(float), null),
            new BlackboardVariableEntry("HasTarget", typeof(bool),  null),
        });
        var items  = Sources(new BehaviorRegistry(), asset).GetVariables(default);

        items.Should().Contain("Health");
        items.Should().Contain("HasTarget");
        items.Should().HaveCount(2);
    }

    [Fact]
    public void FieldPicker_BlackboardField_EmptyAsset_ReturnsEmpty()
    {
        Sources(new BehaviorRegistry()).GetVariables(default).Should().BeEmpty();
    }

    // ── CompositeStringDrawer tests ───────────────────────────────────────────

    [Fact]
    public void CompositeStringDrawer_DispatchesByAttribute()
    {
        var pickerDrawer = new AiAssetPickerDrawer(new EmptyCatalog(), Hrot.Editor.AiShared.AssetKind.BTree);
        var composite    = new CompositeStringDrawer()
            .Register<AiAssetPickerAttribute>(pickerDrawer);

        var node     = MakeNodeWithAttr(new AiAssetPickerAttribute(Hrot.Editor.AiShared.AssetKind.BTree));
        var resolved = composite.Resolve(node);

        resolved.Should().BeSameAs(pickerDrawer,
            "composite drawer must dispatch to the drawer registered for the field's attribute");
    }

    [Fact]
    public void CompositeStringDrawer_NoAttribute_ReturnsNull()
    {
        var composite = new CompositeStringDrawer()
            .Register<AiAssetPickerAttribute>(new AiAssetPickerDrawer(new EmptyCatalog(), Hrot.Editor.AiShared.AssetKind.BTree));

        // Node with no custom attributes.
        var resolved = composite.Resolve(MakeNodeWithAttr());

        resolved.Should().BeNull("no registered attribute means no dispatch");
    }

    /// <summary>⭐ <c>CE-417</c> slice 4b — the binding facet is drawn by the ONE binding drawer, keyed by its type.</summary>
    [Fact]
    public void TheFactory_RegistersTheBindingDrawer_ForTheBindingFacet()
    {
        var drawers = BTreePickerDrawerFactory.BuildDrawers(MakeAsset(EmptyBlob()), new BehaviorRegistry());

        drawers.Should().ContainKey(typeof(BehaviorActionBindingFacet));
        drawers[typeof(BehaviorActionBindingFacet)].Should().BeOfType<ActionBindingDrawer>();
    }

    private sealed class EmptyCatalog : Hrot.Editor.AiShared.Catalog.IAssetCatalog
    {
        public IReadOnlyList<Hrot.Editor.AiShared.IEditableAsset> All => Array.Empty<Hrot.Editor.AiShared.IEditableAsset>();
        public Hrot.Editor.AiShared.IEditableAsset? FindByAssetId(Guid assetId) => null;
        public Hrot.Editor.AiShared.IEditableAsset? FindByName(string name) => null;
        public IReadOnlyList<Hrot.Editor.AiShared.IEditableAsset> WhereDependsOn(Guid assetId) => Array.Empty<Hrot.Editor.AiShared.IEditableAsset>();
        public event Action<Hrot.Editor.AiShared.AssetKind>? Changed { add { } remove { } }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EditNode MakeNodeWithAttr(params Attribute[] attrs)
    {
        var meta = new EditNodeMetadata { CustomAttributes = attrs };
        return new EditNode(
            id:       new EditNodeId(0),
            name:     "Field",
            jsonPath: "$.Field",
            kind:     EditNodeKind.String,
            clrType:  typeof(string),
            metadata: meta);
    }
}
