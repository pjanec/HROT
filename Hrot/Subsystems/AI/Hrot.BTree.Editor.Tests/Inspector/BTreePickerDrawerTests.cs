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
    /// production factory registers. ⭐ <c>CE-1167</c> — the BTree host's method list is the exporter's BTree methods of the slot's kind
    /// (it was the behaviour registry's NAMES, which are not methods).</summary>
    private static ActionBindingSources Sources(BehaviorRegistry registry, BehaviorTreeAsset? asset = null,
                                                IActionSchemaExporter? exporter = null)
        => ((ActionBindingDrawer)BTreePickerDrawerFactory.BuildDrawers(asset ?? MakeAsset(EmptyBlob()), registry, exporter)
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

    private static ActionSchemaEntry Entry(string fqn, ActionHosting hosting, bool condition = false)
        => new(fqn, typeof(int), hosting, BlackboardAccess.ReadWrite, IsCondition: condition);

    // ── the method list (was BehaviorHashPickerDrawer) ─────────────────────────

    /// <summary>⭐ <c>CE-1167</c> (AI_Editor_Shared_Infrastructure §7.1c G-5) — an action slot lists the exporter's BTree ACTIONS, a
    /// condition slot its BTree CONDITIONS; an HSM-only method and a behaviour's registered NAME are never offered (a stored binding is a
    /// C# method FQN — a behaviour name is a value the build cannot resolve).</summary>
    [Fact]
    public void CE1167_TheMethodList_IsTheExportersBTreeMethods_OfTheSlotsKind_NotBehaviourNames()
    {
        var registry = new BehaviorRegistry();
        RegisterName(registry, "SomeBehaviour", 1);
        var exporter = new PickerStubExporter(
            Entry("Ns.Class.RunAway", ActionHosting.BTree),
            Entry("Ns.Class.Patrol",  ActionHosting.BTree),
            Entry("Ns.Class.Ready",   ActionHosting.BTree, condition: true),
            Entry("Ns.Hsm.OnlyHsm",   ActionHosting.Hsm | ActionHosting.HsmActivity));

        var sources = Sources(registry, exporter: exporter);

        sources.GetMethods(BindingSlotKind.Action).Should().Equal("Ns.Class.Patrol", "Ns.Class.RunAway");
        sources.GetMethods(BindingSlotKind.Guard).Should().Equal("Ns.Class.Ready");
    }

    /// <summary>⭐ <c>CE-1167</c> — the asset's OWN bound names stay in the list even when the exporter no longer has them (a dangling
    /// binding is shown, not hidden) — the HSM's rule, now the one shared function.</summary>
    [Fact]
    public void CE1167_TheAssetsOwnMethods_StayListed_EvenWhenNoLongerExported()
    {
        var exporter = new PickerStubExporter(Entry("Ns.Class.Patrol", ActionHosting.BTree));
        var own = new Hrot.Editor.AiShared.BehaviorActionBinding?[] { new() { MethodFqn = "Gone.Class.Removed" }, null };

        ActionBindingMethods.For(exporter, ActionHosting.BTree, condition: false, own)
            .Should().BeEquivalentTo("Ns.Class.Patrol", "Gone.Class.Removed");
    }

    [Fact]
    public void FieldPicker_MethodList_NoExporter_ReturnsEmpty()
    {
        var registry = new BehaviorRegistry();
        RegisterName(registry, "Ns.Class.RunAway", 1);
        Sources(registry).GetMethods(BindingSlotKind.Guard).Should().BeEmpty();
        Sources(registry).GetMethods(BindingSlotKind.Action).Should().BeEmpty();
    }

    [Fact]
    public void FieldPicker_MethodList_ItemsSorted()
    {
        var exporter = new PickerStubExporter(
            Entry("Z.Method", ActionHosting.BTree), Entry("A.Method", ActionHosting.BTree), Entry("M.Method", ActionHosting.BTree));

        var items = Sources(new BehaviorRegistry(), exporter: exporter).GetMethods(BindingSlotKind.Action);

        items.Should().BeInAscendingOrder("items must be sorted alphabetically");
    }

    /// <summary>A C# method for the CE-1011 rail to go to (its source is this file, through the test assembly's PDB).</summary>
    internal static void ADefinedAction() { }

    /// <summary>⭐ <c>CE-1011</c> (AI_Editor_Shared_Infrastructure §7.1c G-2, G-3) — the bound method's source is found through the
    /// exporter's reflected method and opened at its line; an unknown method, or no exporter, has none and opens nothing.</summary>
    [Fact]
    public void CE1011_GoToDefinition_FindsTheBoundMethodsSource_AndOpensItAtItsLine()
    {
        string fqn = $"{typeof(BTreePickerDrawerTests).FullName}.{nameof(ADefinedAction)}";
        var exporter = new PickerStubExporter(Entry(fqn, ActionHosting.BTree) with
        {
            Method = typeof(BTreePickerDrawerTests).GetMethod(nameof(ADefinedAction),
                                                              System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic),
        });
        var sources = Sources(new BehaviorRegistry(), exporter: exporter);
        var opened = new List<(string File, int Line)>();
        sources.OpenSource = (file, line) => { opened.Add((file, line)); return true; };

        var at = sources.SourceOf(fqn);
        at.Should().NotBeNull();
        at!.Value.File.Should().EndWith("BTreePickerDrawerTests.cs");
        System.IO.File.ReadAllLines(at.Value.File)[at.Value.Line - 1].Should().Contain(nameof(ADefinedAction));
        sources.GoToDefinition(fqn).Should().BeTrue();
        opened.Should().Equal((at.Value.File, at.Value.Line));

        sources.SourceOf("No.Such.Method").Should().BeNull();
        sources.GoToDefinition("No.Such.Method").Should().BeFalse();
        Sources(new BehaviorRegistry()).SourceOf(fqn).Should().BeNull();   // no exporter
        opened.Should().HaveCount(1);
    }

    /// <summary>⭐ <c>CE-1011</c> (G-2) — the REAL exporter keeps the method it scanned on every entry, so every listed method can be
    /// gone to (no second FQN resolver).</summary>
    [Fact]
    public void CE1011_TheExporter_KeepsEachEntrysMethod()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        exporter.All.Should().NotBeEmpty();
        exporter.All.Should().OnlyContain(kv => kv.Value.Method != null
                                                && kv.Key.EndsWith("." + kv.Value.Method.Name, StringComparison.Ordinal));
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

    /// <summary>⭐ CE-1008 (AI_Editor_Shared_Infrastructure §7.1b) — the factory FORWARDS the host's picker to every choice field of the
    /// document (the silent-default rule: a caller that has it must pass it), one shared field; with none they stay plain combos.</summary>
    [Fact]
    public void CE1008_TheHostsPicker_ReachesTheBindingAndTheBehaviourPicker_OneSharedField()
    {
        Action<NodeEditor.UI.Picker.PickerRequest, Action<NodeEditor.UI.Picker.PickerResult>> open = (_, _) => { };
        var drawers = BTreePickerDrawerFactory.BuildDrawers(MakeAsset(EmptyBlob()), new BehaviorRegistry(), openPicker: open);
        var binding = (ActionBindingDrawer)drawers[typeof(BehaviorActionBindingFacet)];
        var behaviour = (Hrot.Editor.AiShared.Inspector.AiBehaviorPickerDrawer)((CompositeStringDrawer)drawers[typeof(string)])
            .Resolve(MakeNodeWithAttr(new Hrot.Editor.AiShared.Inspector.AiBehaviorPickerAttribute()))!;
        binding.Field.CanSearch.Should().BeTrue();
        behaviour.Field.Should().BeSameAs(binding.Field);

        ((ActionBindingDrawer)BTreePickerDrawerFactory.BuildDrawers(MakeAsset(EmptyBlob()), new BehaviorRegistry())[typeof(BehaviorActionBindingFacet)])
            .Field.CanSearch.Should().BeFalse();
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

/// <summary>⭐ CE-1167 — a fixed exporter for the method-list rails.</summary>
file sealed class PickerStubExporter : IActionSchemaExporter
{
    private readonly Dictionary<string, ActionSchemaEntry> _map;

    public IReadOnlyDictionary<string, ActionSchemaEntry> All => _map;
    public event Action? Changed { add { } remove { } }

    public PickerStubExporter(params ActionSchemaEntry[] entries)
    {
        _map = new Dictionary<string, ActionSchemaEntry>(StringComparer.Ordinal);
        foreach (var e in entries) _map[e.Fqn] = e;
    }

    public ActionSchemaEntry? Lookup(string fqn) => _map.GetValueOrDefault(fqn);
    public void Rebuild() { }
}
