using System;
using System.Collections.Generic;
using System.Linq;
using Fbt;
using Fdp.Toolkit.Behavior;
using FluentAssertions;
using Hrot.BTree.Editor.Inspector;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
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

    private static void RegisterName(BehaviorRegistry registry, string name, int id)
    {
        var def = new BehaviorDefinition
        {
            Name      = name,
            BrainTier = 0,
        };
        registry.Register(id, name, def);
    }

    // ── BehaviorHashPickerDrawer tests ────────────────────────────────────────

    [Fact]
    public void FieldPicker_BehaviorHash_ListsRegistryNames()
    {
        var registry = new BehaviorRegistry();
        RegisterName(registry, "Ns.Class.RunAway", 1);
        RegisterName(registry, "Ns.Class.Patrol",  2);

        var drawer = new BehaviorHashPickerDrawer(registry);
        var items  = drawer.GetItems();

        items.Should().Contain("Ns.Class.RunAway");
        items.Should().Contain("Ns.Class.Patrol");
        items.Should().HaveCount(2);
    }

    [Fact]
    public void FieldPicker_BehaviorHash_EmptyRegistry_ReturnsEmpty()
    {
        var registry = new BehaviorRegistry();
        var drawer   = new BehaviorHashPickerDrawer(registry);

        drawer.GetItems().Should().BeEmpty();
    }

    [Fact]
    public void FieldPicker_BehaviorHash_ItemsSorted()
    {
        var registry = new BehaviorRegistry();
        RegisterName(registry, "Z.Method", 3);
        RegisterName(registry, "A.Method", 1);
        RegisterName(registry, "M.Method", 2);

        var drawer = new BehaviorHashPickerDrawer(registry);
        var items  = drawer.GetItems();

        items.Should().BeInAscendingOrder("items must be sorted alphabetically");
    }

    // ── BlackboardFieldPickerDrawer tests ─────────────────────────────────────

    [Fact]
    public void FieldPicker_BlackboardField_ListsActiveAssetFields()
    {
        var asset = MakeAsset(EmptyBlob());
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("Health",    typeof(float), null),
            new BlackboardVariableEntry("HasTarget", typeof(bool),  null),
        });
        var drawer = new BlackboardFieldPickerDrawer(asset);
        var items  = drawer.GetItems();

        items.Should().Contain("Health");
        items.Should().Contain("HasTarget");
        items.Should().HaveCount(2);
    }

    [Fact]
    public void FieldPicker_BlackboardField_EmptyAsset_ReturnsEmpty()
    {
        var asset  = MakeAsset(EmptyBlob());
        var drawer = new BlackboardFieldPickerDrawer(asset);

        drawer.GetItems().Should().BeEmpty();
    }

    // ── CompositeStringDrawer tests ───────────────────────────────────────────

    [Fact]
    public void CompositeStringDrawer_DispatchesByAttribute_BehaviorHash()
    {
        var registry  = new BehaviorRegistry();
        RegisterName(registry, "Ns.Method", 1);
        var bhDrawer  = new BehaviorHashPickerDrawer(registry);
        var composite = new CompositeStringDrawer()
            .Register<BehaviorHashPickerAttribute>(bhDrawer);

        // Create a node with BehaviorHashPicker metadata.
        var node = MakeNodeWithAttr(new BehaviorHashPickerAttribute());
        var resolved = composite.Resolve(node);

        resolved.Should().BeSameAs(bhDrawer,
            "composite drawer must dispatch to BehaviorHashPickerDrawer when attribute present");
    }

    [Fact]
    public void CompositeStringDrawer_NoAttribute_ReturnsNull()
    {
        var registry  = new BehaviorRegistry();
        var bhDrawer  = new BehaviorHashPickerDrawer(registry);
        var composite = new CompositeStringDrawer()
            .Register<BehaviorHashPickerAttribute>(bhDrawer);

        // Node with no custom attributes.
        var node     = MakeNodeWithAttr();
        var resolved = composite.Resolve(node);

        resolved.Should().BeNull("no registered attribute means no dispatch");
    }

    [Fact]
    public void CompositeStringDrawer_DispatchesByAttribute_BlackboardField()
    {
        var asset      = MakeAsset(EmptyBlob());
        var bbDrawer   = new BlackboardFieldPickerDrawer(asset);
        var composite  = new CompositeStringDrawer()
            .Register<BlackboardFieldPickerAttribute>(bbDrawer);

        var node     = MakeNodeWithAttr(new BlackboardFieldPickerAttribute());
        var resolved = composite.Resolve(node);

        resolved.Should().BeSameAs(bbDrawer);
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
