using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ N-5 (📄 docs/DESIGN_Struct_Editor.md §5, CE-3382) — the ONE entity chip's model: the picker's rows (<see cref="EntityPickerRows"/>: by name, side colour, distance
/// from the selection) and the gone mark (<see cref="ReferenceLeafDrawers.GoneText"/>). Red-proof: <c>GoneText</c> answering null for a missing id ⇒ the gone assert fails.
/// </summary>
[Collection(Hrot.Presentation.Tests.ComponentTypeRegistryMutatorCollection.Name)]
public sealed class EntityChipTests : IDisposable
{
    private readonly EntityRepository _repo;
    private int _nextId = 40;

    public EntityChipTests()
    {
        ComponentTypeRegistry.Clear();
        _repo = new EntityRepository();
        _repo.RegisterComponent<NetworkIdentity>();
        _repo.RegisterComponent<EntityInfo>();
        _repo.RegisterComponent<SimTransform>();
    }

    public void Dispose() => _repo.Dispose();

    private (Entity E, EntityRef R) Spawn(string name, ForceId side, Vector3 at)
    {
        var e = _repo.CreateEntity();
        int id = _nextId++;
        _repo.AddComponent(e, new NetworkIdentity(id));
        _repo.AddComponent(e, new EntityInfo { Name = new FixedString64(name), ForceId = side });
        _repo.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
        return (e, new EntityRef(id));
    }

    [Fact]
    public void ThePickerRows_AreByName_WithSideAndDistance_AndCachedUntilTheScopeOrTheSelectionMoves()
    {
        Spawn("Zulu supply", ForceId.Friend, new Vector3(1500, 0, 0));
        Spawn("Bravo 2-3", ForceId.Hostile, new Vector3(0, 210, 0));
        var scope = new LiveRefScope(_repo, () => 0);
        var rows = new EntityPickerRows();

        var r = rows.Rows(scope, new WorldPoint(0, 0, 0));
        Assert.Equal(2, r.Count);
        Assert.Equal("Bravo 2-3", r[0].Label);
        Assert.Equal("Hostile", r[0].Side);
        Assert.Equal(EntityPickerRows.SideColor(ForceId.Hostile), r[0].SideColor);
        Assert.Equal("210 m", r[0].Distance);
        Assert.Equal("1.5 km", r[1].Distance);

        int v = rows.Version;
        rows.Rows(scope, new WorldPoint(0.5f, 0, 0));                         // under a metre: the same rows
        Assert.Equal(v, rows.Version);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20; i++) rows.Rows(scope, new WorldPoint(0, 0, 0));
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());          // R-2084: a steady frame allocates nothing
        Assert.Equal("", rows.Rows(scope, null)[0].Distance);                 // no selection ⇒ no distance column
        Assert.NotEqual(v, rows.Version);
    }

    [Fact]
    public void AGoneEntity_ReadsGoneWithItsId_ALiveOneAndNoneDoNot()
    {
        var (e, alive) = Spawn("Alpha 1-1", ForceId.Friend, Vector3.Zero);
        var scope = new LiveRefScope(_repo, () => 0);

        Assert.Null(ReferenceLeafDrawers.GoneText(alive, scope));
        Assert.Null(ReferenceLeafDrawers.GoneText(EntityRef.None, scope));
        Assert.Null(ReferenceLeafDrawers.GoneText(new EntityRef(9999), null)); // no scope ⇒ cannot tell, never claims gone
        Assert.Equal("gone · #9999", ReferenceLeafDrawers.GoneText(new EntityRef(9999), scope));
        Assert.Same(ReferenceLeafDrawers.GoneText(new EntityRef(9999), scope), ReferenceLeafDrawers.GoneText(new EntityRef(9999), scope)); // cached text

        _repo.DestroyEntity(e);
        scope.Invalidate();
        Assert.Equal("gone · #" + alive.NetworkId, ReferenceLeafDrawers.GoneText(alive, scope));
    }
}
