using FluentAssertions;
using StructEdit.Core;
using StructEdit.Reflection;

namespace StructEdit.Tests.Session;

// ── Test fixtures ─────────────────────────────────────────────────────────────

file class ComponentWithList
{
    public List<int> Items { get; set; } = new List<int> { 1, 2, 3 };
}

file class ComponentWithArray
{
    public int[] Data { get; set; } = new int[] { 10, 20 };
}

// ── Helper ────────────────────────────────────────────────────────────────────

file static class DynHelper
{
    private static IComponentEditService Service()
        => new ComponentEditServiceBuilder().Build();

    public static (IEditSession session, IContainerBinding cb) OpenList(List<int> items)
    {
        var session = Service().Open(
            new ComponentWithList { Items = items }, typeof(ComponentWithList));
        var node = session.Document.Root.Children.First(c => c.Name == "Items");
        var cb   = (IContainerBinding)node.Binding!;
        return (session, cb);
    }

    public static (IEditSession session, IContainerBinding cb) OpenArray(int[] data)
    {
        var session = Service().Open(
            new ComponentWithArray { Data = data }, typeof(ComponentWithArray));
        var node = session.Document.Root.Children.First(c => c.Name == "Data");
        var cb   = (IContainerBinding)node.Binding!;
        return (session, cb);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// TASK-A001: Dynamic array resize — session-level tests
// ══════════════════════════════════════════════════════════════════════════════

public class DynamicArraySessionTests
{
    // A001-T1: Resize List<T> from 3 → 5; committed component has 5 elements
    [Fact]
    public void ResizeList_Up_CommittedComponentHas5Elements()
    {
        var (session, cb) = DynHelper.OpenList(new List<int> { 1, 2, 3 });
        using (session)
        {
            cb.Resize(5);
            session.MarkStructuralChange();
            session.RebuildDocument();

            var result = (ComponentWithList)session.Commit();
            result.Items.Should().HaveCount(5);
        }
    }

    // A001-T2: Resize List<T> from 5 → 2; committed component has 2 elements
    [Fact]
    public void ResizeList_Down_CommittedComponentHas2Elements()
    {
        var (session, cb) = DynHelper.OpenList(new List<int> { 1, 2, 3, 4, 5 });
        using (session)
        {
            cb.Resize(2);
            session.MarkStructuralChange();
            session.RebuildDocument();

            var result = (ComponentWithList)session.Commit();
            result.Items.Should().HaveCount(2);
        }
    }

    // A001-T3: Resize T[] from 2 → 4; committed component has 4 elements
    [Fact]
    public void ResizeArray_From2To4_CommittedComponentHas4Elements()
    {
        var (session, cb) = DynHelper.OpenArray(new int[] { 10, 20 });
        using (session)
        {
            cb.Resize(4);
            session.MarkStructuralChange();
            session.RebuildDocument();

            var result = (ComponentWithArray)session.Commit();
            result.Data.Should().HaveCount(4);
        }
    }

    // A001-T4: After resize up, elements at existing indices retain their values
    [Fact]
    public void ResizeList_Up_ExistingElementValuesPreserved()
    {
        var (session, cb) = DynHelper.OpenList(new List<int> { 7, 8, 9 });
        using (session)
        {
            cb.Resize(5);

            cb.GetElementBinding(0).GetBoxed().Should().Be(7);
            cb.GetElementBinding(1).GetBoxed().Should().Be(8);
            cb.GetElementBinding(2).GetBoxed().Should().Be(9);
        }
    }

    // A001-T5: After resize and Commit(), returned component has the new size (no RebuildDocument needed)
    [Fact]
    public void ResizeArray_Commit_ReturnsCorrectNewLength()
    {
        var (session, cb) = DynHelper.OpenArray(new int[] { 1, 2, 3 });
        using (session)
        {
            cb.Resize(6);

            var result = (ComponentWithArray)session.Commit();
            result.Data.Should().HaveCount(6);
        }
    }

    // A001-T6: MarkStructuralChange() after resize sets RebuildState to RebuildRequired
    [Fact]
    public void ResizeList_MarkStructuralChange_StateIsRebuildRequired()
    {
        var (session, cb) = DynHelper.OpenList(new List<int> { 1, 2, 3 });
        using (session)
        {
            cb.Resize(5);
            session.MarkStructuralChange();

            session.RebuildState.Should().Be(EditRebuildState.RebuildRequired);
        }
    }
}
