using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared.Inspector;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;
using StructEdit.Core;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Inspector;

/// <summary>
/// ⭐⭐ CE-1008 (docs/blueprints/AI_Editor_Shared_Infrastructure.md §7.1b) — the AI inspectors' ONE choice field: it opens the searchable
/// picker with every choice and a "(none)", and the pick (which the picker returns LATER, by callback) lands on the node it was made for, on
/// the drawer's next frame — never on another node selected meanwhile. 🔒 User: "want the general picker window used for opening assets,
/// with incremental search; the C# method picker ideally as an FQN-segment tree."
/// </summary>
public sealed class PickerFieldTests
{
    private sealed class FakeRegistry
    {
        public PickerRequest? Request;
        public Action<PickerResult>? Answer;
        public void Open(PickerRequest request, Action<PickerResult> answer) { Request = request; Answer = answer; }
        public PickerEntry Entry(string id) => Request!.ItemsProvider().Single(e => e.Id == id);
    }

    private static EditNode Node(string path) => new(new EditNodeId(0), "Field", path, EditNodeKind.String, typeof(string));

    [Fact]
    public void CE1008_ThePickerOffersNoneAndEveryChoice_AndThePickLandsOnItsNode_OnceOnly()
    {
        var registry = new FakeRegistry();
        var field = new PickerField(registry.Open);
        var nodeA = Node("$.Action");
        var nodeB = Node("$.Action");   // the same field of ANOTHER selection (a new EditSession, a new node)
        var choices = PickerChoices.Flat(new[] { "Run", "Hide" });

        field.Open("method##abm", nodeA, "Run", choices, "Method");
        Assert.True(field.CanSearch);
        Assert.Equal(new[] { PickerField.NoneId, "Run", "Hide" }, registry.Request!.ItemsProvider().Select(e => e.Id).ToArray());
        Assert.Equal("Run", registry.Request.InitialSelectionId);
        Assert.False(field.TryConsume(nodeA, "method##abm", out _));      // nothing until the operator picks

        registry.Answer!(new PickerResult(new[] { registry.Entry("Hide") }));
        Assert.False(field.TryConsume(nodeB, "method##abm", out _));      // never another node's
        Assert.False(field.TryConsume(nodeA, "blueprint##abb", out _));   // nor another field's
        Assert.True(field.TryConsume(nodeA, "method##abm", out var picked));
        Assert.Equal("Hide", picked);
        Assert.False(field.TryConsume(nodeA, "method##abm", out _));      // taken once
    }

    [Fact]
    public void CE1008_NoneClearsTheField_ACancelLeavesItAlone_AndNoRegistryMeansNoSearch()
    {
        var registry = new FakeRegistry();
        var field = new PickerField(registry.Open);
        var node = Node("$.Action");

        field.Open("variable##abv", node, "Speed", PickerChoices.Flat(new[] { "Speed" }), "Variable");
        registry.Answer!(new PickerResult(Array.Empty<PickerEntry>()));   // cancelled
        Assert.False(field.TryConsume(node, "variable##abv", out _));
        field.Open("variable##abv", node, "Speed", PickerChoices.Flat(new[] { "Speed" }), "Variable");
        registry.Answer!(new PickerResult(new[] { registry.Entry(PickerField.NoneId) }));
        Assert.True(field.TryConsume(node, "variable##abv", out var cleared));
        Assert.Equal(string.Empty, cleared);

        Assert.False(new PickerField(null).CanSearch);   // a test or lightweight host: the plain combo
    }

    /// <summary>⭐ P-4 — C# methods open as an FQN TREE (namespace and type folders), the full FQN a keyword so any fragment finds it.</summary>
    [Fact]
    public void CE1008_MethodsAreAnFqnTree_TheFqnIsSearchable_AndTheTagIsTheDescription()
    {
        var methods = PickerChoices.Methods(new[] { "Hrot.AI.Doors.OpenDoor", "Bare" }, fqn => fqn + " [C#]");
        Assert.Equal(("OpenDoor", "Hrot/AI/Doors", "Hrot.AI.Doors.OpenDoor [C#]"), (methods[0].Label, methods[0].Category, methods[0].Description));
        Assert.Contains("Hrot.AI.Doors.OpenDoor", methods[0].Keywords!);
        Assert.Null(methods[1].Category);

        var registry = new FakeRegistry();
        new PickerField(registry.Open).Open("method##abm", Node("$.Action"), "", methods, "Method");
        Assert.Equal((PickerLayout.Tree, true, PickerField.NoneId), (registry.Request!.Layout, registry.Request.FoldSingleChildFolders, registry.Request.InitialSelectionId));
        Assert.Equal("Hrot/AI/Doors", registry.Entry("Hrot.AI.Doors.OpenDoor").Category);
    }
}
