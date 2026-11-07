using FluentAssertions;
using StructEdit.Core;
using StructEdit.Reflection;

namespace StructEdit.Tests.Session;

public class CoWaypoint
{
    public string Name { get; set; } = "";
    public List<int> Tags { get; set; } = new();
}

public struct CoLevel { public float Alt; public float Speed; }

public class CoHolder
{
    public List<CoWaypoint> Points { get; set; } = new();
    public CoLevel[] Levels { get; set; } = Array.Empty<CoLevel>();
    public List<string> Words { get; set; } = new();
}

/// <summary>
/// ⭐ S4 (📄 docs/DESIGN_Struct_Editor.md §6 S4, §10.1 / §10.5, CE-3379) — the list operations: a MOVE is in place (no rebuild, edits through the moved row land on
/// the moved element, Commit keeps the order); insert / duplicate / remove / clear change the count and rebuild once; a new element is never null; a duplicate is DEEP.
/// 🔴 Red-proof (compiles): <c>ContainerOps.Move</c> with <c>to</c> and <c>from</c> swapped in the last SetBoxed ⇒ the moved element is lost ⇒ red.
/// </summary>
public class ContainerOpsTests
{
    private static (IEditSession s, IContainerOps ops) Open(CoHolder h, string member)
    {
        var session = new ComponentEditServiceBuilder().Build().Open(h, typeof(CoHolder));
        var node = session.Document.Root.Children.First(c => c.Name == member);
        return (session, (IContainerOps)node.Binding!);
    }

    private static EditNode Member(IEditSession s, string member) => s.Document.Root.Children.First(c => c.Name == member);

    [Fact]
    public void AMove_IsInPlace_NoRebuild_AndAnEditThroughTheMovedRow_LandsOnTheMovedElement()
    {
        var (s, ops) = Open(new CoHolder { Points = { new() { Name = "a" }, new() { Name = "b" }, new() { Name = "c" } } }, "Points");
        using (s)
        {
            ops.Move(0, 2);
            s.RebuildState.Should().Be(EditRebuildState.Stable);
            var rows = Member(s, "Points").Children;
            rows.Select(r => ((CoWaypoint)r.Binding!.GetBoxed()!).Name).Should().Equal("b", "c", "a");
            rows[2].Children.First(c => c.Name == "Name").Binding!.SetBoxed("a2");   // the row the user now sees as item 3
            var done = (CoHolder)s.Commit();
            done.Points.Select(p => p.Name).Should().Equal("b", "c", "a2");
        }
    }

    [Fact]
    public void AMove_OfStructsInAnArray_ShiftsTheOnesBetween()
    {
        var (s, ops) = Open(new CoHolder { Levels = new[] { new CoLevel { Alt = 1 }, new CoLevel { Alt = 2 }, new CoLevel { Alt = 3 }, new CoLevel { Alt = 4 } } }, "Levels");
        using (s)
        {
            ops.Move(3, 1);
            ((CoHolder)s.Commit()).Levels.Select(l => l.Alt).Should().Equal(1f, 4f, 2f, 3f);
        }
    }

    [Fact]
    public void InsertDuplicateRemoveClear_ChangeTheCount_AndTheRebuiltDocumentShowsIt()
    {
        var (s, ops) = Open(new CoHolder { Words = { "x", "y" } }, "Words");
        using (s)
        {
            ops.Insert(1, ops.NewElement());
            ops.Count.Should().Be(3);
            s.MarkStructuralChange();
            s.RebuildDocument();
            Member(s, "Words").Children.Should().HaveCount(3);
            var words = (IContainerOps)Member(s, "Words").Binding!;
            words.Duplicate(0);
            words.RemoveAt(3);
            ((CoHolder)s.Commit()).Words.Should().Equal("x", "x", "");
        }
    }

    [Fact]
    public void ANewElement_IsNeverNull_ForAStringAStructOrAClass()
    {
        var (s, ops) = Open(new CoHolder(), "Points");
        using (s)
        {
            ops.NewElement().Should().BeOfType<CoWaypoint>();
            ContainerOps.DefaultOf(typeof(string)).Should().Be("");
            ContainerOps.DefaultOf(typeof(CoLevel)).Should().Be(default(CoLevel));
            ContainerOps.DefaultOf(typeof(int[])).Should().BeOfType<int[]>();
        }
    }

    [Fact]
    public void ADuplicate_IsDeep_EditingTheCopyLeavesTheOriginal()
    {
        var (s, ops) = Open(new CoHolder { Points = { new() { Name = "a", Tags = { 1, 2 } } } }, "Points");
        using (s)
        {
            ops.Duplicate(0);
            var arr = (System.Collections.IList)ops.GetBoxed()!;
            ((CoWaypoint)arr[1]!).Tags.Add(9);
            ((CoWaypoint)arr[1]!).Name = "copy";
            var done = (CoHolder)s.Commit();
            done.Points.Select(p => p.Name).Should().Equal("a", "copy");
            done.Points[0].Tags.Should().Equal(1, 2);
        }
    }

    [Fact]
    public void ClearEmpties_AndTheLiveObjectIsUntouchedUntilCommit()
    {
        var live = new CoHolder { Words = { "x" } };
        var (s, ops) = Open(live, "Words");
        using (s)
        {
            ops.Clear();
            ops.Count.Should().Be(0);
            live.Words.Should().Equal("x");
            ((CoHolder)s.Commit()).Words.Should().BeEmpty();
        }
    }
}
