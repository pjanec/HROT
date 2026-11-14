using System;
using System.Linq;
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
        _repo.RegisterComponent<TkbIdentity>();
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

    /// <summary>⭐ CE-3399 (Y-8) — a field's <c>[MapPickableEntity]</c> presets narrow the type-ahead's rows too (they only narrowed the MAP pick): a <c>tkb:&lt;type&gt;</c> preset lists only entities of that TKB type, a layer preset goes through the host's filter (the map pick's), the unfiltered list is
    /// unchanged, and the narrowed list is cached per preset set — 0 B on a steady frame. 🔴 Red-proof (compiles): <c>Rows</c> ignoring the presets ⇒ all three listed ⇒ red.</summary>
    [Fact]
    public void ThePresets_NarrowTheTypeAheadRows_PerPresetSet_AndStayCached()
    {
        var a = Spawn("Alpha road", ForceId.Friend, Vector3.Zero); _repo.AddComponent(a.E, new TkbIdentity { TkbType = new TkbTypeId(7) });
        var b = Spawn("Bravo point", ForceId.Friend, Vector3.Zero); _repo.AddComponent(b.E, new TkbIdentity { TkbType = new TkbTypeId(9) });
        var c = Spawn("Charlie unit", ForceId.Hostile, Vector3.Zero);                                                  // no TKB type
        var scope = new LiveRefScope(_repo, () => 0) { LayerFilter = (_, layers) => layers[0] == "roads" ? e => e == a.E : null };
        var rows = new EntityPickerRows();
        string[] onlyType7 = { "tkb:7" }, roads = { "roads" }, unknownLayer = { "no_such_layer_filter" };

        Assert.Equal(3, rows.Rows(scope, null).Count);                                                                  // no presets: every named entity
        Assert.Equal(new[] { "Alpha road" }, rows.Rows(scope, null, onlyType7).Select(r => r.Label));                  // the TKB type
        Assert.Equal(new[] { "Alpha road" }, rows.Rows(scope, null, roads).Select(r => r.Label));                      // a layer preset: the host's filter
        Assert.Equal(3, rows.Rows(scope, null, unknownLayer).Count);                                                    // a layer the host does not narrow by: not narrowed
        Assert.True(rows.VersionOf(onlyType7) > 0 && rows.VersionOf(roads) > 0);                                        // each preset set has its own list and version

        for (int i = 0; i < 20; i++) { rows.Rows(scope, null); rows.Rows(scope, null, onlyType7); rows.Rows(scope, null, roads); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++) { rows.Rows(scope, null); rows.Rows(scope, null, onlyType7); rows.Rows(scope, null, roads); }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());                                                   // R-2084
        Assert.True(scope.Admits(a.R.NetworkId, onlyType7) && !scope.Admits(b.R.NetworkId, onlyType7) && !scope.Admits(c.R.NetworkId, onlyType7));
    }

    /// <summary>⭐ CE-3399 follow-up (Y-8) — a field's preset lookup runs EVERY frame (an index loop, no LINQ: 0 B), and a document REBUILD (new attribute instances = new arrays with the SAME presets) does not grow the per-preset caches: N rebuilds ⇒ one admitted set, one row list.
    /// 🔴 Red-proof (compiles): the caches keyed by array reference again ⇒ the counts read N ⇒ red; <c>Presets</c> back on LINQ ⇒ the allocation assert fails.</summary>
    [Fact]
    public void ThePresetLookup_AllocatesNothing_AndDocumentRebuildsDoNotGrowThePerPresetCaches()
    {
        var a = Spawn("Alpha road", ForceId.Friend, Vector3.Zero); _repo.AddComponent(a.E, new TkbIdentity { TkbType = new TkbTypeId(7) });
        var scope = new LiveRefScope(_repo, () => 0);
        var rows = new EntityPickerRows();
        var node = new StructEdit.Core.EditNode(new StructEdit.Core.EditNodeId(1), "F", "$.F", StructEdit.Core.EditNodeKind.Scalar, typeof(EntityRef), null, null,
            new StructEdit.Core.EditNodeMetadata { CustomAttributes = new Attribute[] { new Fdp.Toolkit.Behavior.Attributes.MapPickableEntityAttribute("tkb:7") } });
        Assert.Equal(new[] { "tkb:7" }, ReferenceLeafDrawers.Presets(node));
        for (int i = 0; i < 20; i++) ReferenceLeafDrawers.Presets(node);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) ReferenceLeafDrawers.Presets(node);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());                      // R-2084: every frame, every chip

        for (int rebuild = 0; rebuild < 25; rebuild++)                                      // a document rebuild: a NEW attribute, a NEW array, the same presets
        {
            var fresh = new Fdp.Toolkit.Behavior.Attributes.MapPickableEntityAttribute("tkb:7").FilterPresets!;
            Assert.Single(rows.Rows(scope, null, fresh));
        }
        Assert.Equal(1, scope.AdmissionSetCount);
        Assert.Equal(1, rows.RowSetCount);
        var other = new[] { "tkb:9" };
        rows.Rows(scope, null, other);
        Assert.Equal(2, scope.AdmissionSetCount);                                           // a DIFFERENT preset set is a second one
        Assert.Equal(2, rows.RowSetCount);
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
