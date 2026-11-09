using System.Linq;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Replication.Components;
using StructEdit.Core;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Presentation.Tests.Editing;

/// <summary>
/// ⭐ Next_2 A-3 (CE-4351 · CE-4352; 📄 docs/DESIGN_Struct_Editor.md — the ONE struct editor) — what the component inspector's double-click opens:
/// a FIXED-SIZE buffer lists its entries (<c>BrainJournal.Entries</c>, an <c>[InlineArray(24)]</c> of a struct), and a map whose element is a struct
/// (<c>EgressPublicationState.LastPublishedTickMap</c>, its <c>KeyValuePair</c>s) shows each element's own fields — never an empty form.
/// </summary>
public sealed class TheStructEditorOpensBuffersAndElementsTests
{
    private static IComponentEditService Service() => new ComponentEditServiceBuilder().Build();

    [Fact]
    public void CE4351_AFixedBufferOfStructs_ListsItsEntries_EachWithItsFields()
    {
        var j = new BrainJournal();
        j.Entries[0] = new BrainJournalEntry { Kind = (BrainJournalKind)1, A = 7 };
        j.Count = 1;
        using var session = Service().Open(j, typeof(BrainJournal));
        var entries = session.Document.Root.Children.First(c => c.Name == "Entries");
        Assert.True(entries.Children.Count == BrainJournal.Capacity, $"Entries lists its {BrainJournal.Capacity} slots (kind {entries.Kind}, {entries.Children.Count} children)");
        var first = entries.Children[0];
        Assert.True(first.Children.Count > 0, $"an entry shows its fields (kind {first.Kind}, {first.Children.Count} children)");
        Assert.Contains(first.Children, c => c.Name == "A");
    }

    /// <summary>⭐ CE-4351 — the inspector's double-click opens the field ALONE (<c>EditScope.ForField</c>) through the production builder: the buffer still lists its slots.</summary>
    [Fact]
    public void CE4351_DoubleClickingTheBuffer_OpensItWithItsEntries()
    {
        var j = new BrainJournal();
        j.Entries[0] = new BrainJournalEntry { A = 7 };
        j.Count = 1;
        var service = Fdp.Presentation.Editing.Types.TypeCatalogue.Standard.CreateBuilder().Build();
        using var session = service.Open(j, typeof(BrainJournal), EditScope.ForField(EditPath.Parse("$.Entries")), new EditContext());
        var entries = Find(session.Document.Root, "Entries");
        Assert.NotNull(entries);
        Assert.True(entries!.Children.Count == BrainJournal.Capacity, $"the field-scoped open lists the slots (kind {entries.Kind}, {entries.Children.Count} children)");
    }

    private static EditNode? Find(EditNode n, string name)
    {
        if (n.Name == name) return n;
        foreach (var c in n.Children) if (Find(c, name) is { } f) return f;
        return null;
    }

    [Fact]
    public void CE4352_AMapsStructElement_ShowsItsKeyAndValue()
    {
        var s = new EgressPublicationState();
        s.LastPublishedTickMap[11] = 100; s.LastPublishedTickMap[12] = 200;
        using var session = Service().Open(s, typeof(EgressPublicationState));
        var map = session.Document.Root.Children.First(c => c.Name == "LastPublishedTickMap");
        Assert.True(map.Children.Count == 2, $"the map lists its 2 entries (kind {map.Kind}, {map.Children.Count} children)");
        var e0 = map.Children[0];
        Assert.True(e0.Children.Count >= 2, $"an element shows its own fields — key and value (kind {e0.Kind}, {e0.Children.Count} children: {string.Join(",", e0.Children.Select(c => c.Name))})");
    }

    /// <summary>⭐ CE-4351 — another component of the same shape (a fixed buffer of structs): <c>ShotTraces.Slots</c>, opened alone, lists its slots with their fields.</summary>
    [Fact]
    public void CE4351_AnotherFixedBuffer_ShotTracesSlots_OpensWithItsSlots()
    {
        var t = new Fdp.Toolkit.Combat.ShotTraces();
        var service = Fdp.Presentation.Editing.Types.TypeCatalogue.Standard.CreateBuilder().Build();
        using var session = service.Open(t, typeof(Fdp.Toolkit.Combat.ShotTraces), EditScope.ForField(EditPath.Parse("$.Slots")), new EditContext());
        var slots = Find(session.Document.Root, "Slots");
        Assert.True(slots!.Children.Count == Fdp.Toolkit.Combat.ShotTraces.Capacity, $"{slots.Children.Count} slots");
        Assert.Contains(slots.Children[0].Children, c => c.Name == "End");
    }

    /// <summary>⭐ CE-4352 — the double-click on ONE entry (<c>$.LastPublishedTickMap[1]</c>, a field-scoped open) shows its key and value, read-only; and another
    /// component of the same shape (<c>OutgoingGrantsPending.Descriptors</c>) lists its entries.</summary>
    [Fact]
    public void CE4352_OneEntryOpensWithItsKeyAndValue_AndAnotherMapListsItsEntries()
    {
        var s = new EgressPublicationState();
        s.LastPublishedTickMap[11] = 100; s.LastPublishedTickMap[12] = 200;
        var service = Fdp.Presentation.Editing.Types.TypeCatalogue.Standard.CreateBuilder().Build();
        using (var session = service.Open(s, typeof(EgressPublicationState), EditScope.ForField(EditPath.Parse("$.LastPublishedTickMap[1]")), new EditContext()))
        {
            var e1 = Find(session.Document.Root, "[1]");
            Assert.NotNull(e1);
            Assert.Equal(new[] { "Key", "Value" }, e1!.Children.Select(c => c.Name).ToArray());
            Assert.Equal(12L, e1.Children[0].Binding!.GetBoxed());
            Assert.Equal(200u, e1.Children[1].Binding!.GetBoxed());
            Assert.True(e1.Children[1].IsReadOnly);
        }
        var g = new OutgoingGrantsPending();
        g.Descriptors[5] = 1; g.Descriptors[6] = 2; g.Descriptors[7] = 3;
        using var s2 = Service().Open(g, typeof(OutgoingGrantsPending));
        Assert.Equal(3, s2.Document.Root.Children.First(c => c.Name == "Descriptors").Children.Count);
    }
}
