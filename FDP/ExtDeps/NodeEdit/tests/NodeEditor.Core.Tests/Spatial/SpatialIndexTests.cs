using System.Numerics;
using NodeEditor.Core.Spatial;
using NodeEditor.Primitives;
using FluentAssertions;
using Xunit;

namespace NodeEditor.Core.Tests.Spatial;

public class SpatialIndexTests
{
    [Fact]
    public void Insert_Then_QueryPoint_FindsNode()
    {
        var idx = new SpatialIndex();
        var id = NodeId.NewId();
        idx.Insert(id, new RectF(new Vector2(10, 10), new Vector2(50, 50)));
        idx.QueryPoint(new Vector2(30, 30)).Should().Contain(id);
    }

    [Fact]
    public void Insert_Then_QueryPoint_OutsideMisses()
    {
        var idx = new SpatialIndex();
        var id = NodeId.NewId();
        idx.Insert(id, new RectF(new Vector2(10, 10), new Vector2(50, 50)));
        idx.QueryPoint(new Vector2(100, 100)).Should().BeEmpty();
    }

    [Fact]
    public void Query_Area_FindsIntersecting()
    {
        var idx = new SpatialIndex();
        var id1 = NodeId.NewId();
        var id2 = NodeId.NewId();
        idx.Insert(id1, new RectF(new Vector2(0, 0), new Vector2(100, 100)));
        idx.Insert(id2, new RectF(new Vector2(200, 200), new Vector2(50, 50)));

        var found = idx.Query(new RectF(new Vector2(50, 50), new Vector2(100, 100))).ToList();
        found.Should().Contain(id1);
        found.Should().NotContain(id2);
    }

    [Fact]
    public void QueryFullyEnclosed_ExcludesPartial()
    {
        var idx = new SpatialIndex();
        var id = NodeId.NewId();
        idx.Insert(id, new RectF(new Vector2(0, 0), new Vector2(100, 100)));

        var fully = idx.QueryFullyEnclosed(new RectF(new Vector2(-10, -10), new Vector2(200, 200)));
        fully.Should().Contain(id);

        var partial = idx.QueryFullyEnclosed(new RectF(new Vector2(50, 50), new Vector2(100, 100)));
        partial.Should().NotContain(id);
    }

    [Fact]
    public void Remove_Works()
    {
        var idx = new SpatialIndex();
        var id = NodeId.NewId();
        idx.Insert(id, new RectF(new Vector2(0, 0), new Vector2(50, 50)));
        idx.Remove(id).Should().BeTrue();
        idx.QueryPoint(new Vector2(25, 25)).Should().BeEmpty();
    }

    [Fact]
    public void Insert_LargeRect_CoversManyCells()
    {
        var idx = new SpatialIndex(cellSize: 100);
        var id = NodeId.NewId();
        idx.Insert(id, new RectF(new Vector2(0, 0), new Vector2(500, 500)));

        // Point within the big rect, far corner.
        idx.QueryPoint(new Vector2(450, 450)).Should().Contain(id);
    }
}
