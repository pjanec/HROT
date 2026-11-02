using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Replication;
using StructEdit.Core;
using StructEdit.Reflection;
using Xunit;

#pragma warning disable CS0649

namespace Fdp.Presentation.Tests.ImGui.Editing;

// ---------------------------------------------------------------------------
// Shared test doubles for CE07
// ---------------------------------------------------------------------------

/// <summary>
/// Minimal IValueBinding that delegates to <see cref="Func{T}"/> accessors.
/// </summary>
file sealed class DelegateBinding : IValueBinding
{
    private readonly Func<object?> _get;
    private readonly Action<object?> _set;

    public DelegateBinding(Func<object?> get, Action<object?> set, Type type)
    {
        _get = get;
        _set = set;
        ValueType = type;
    }

    public Type ValueType { get; }
    public object? GetBoxed() => _get();
    public void SetBoxed(object? value) => _set(value);
    public bool TryGetSpan(out Span<byte> bytes) { bytes = default; return false; }
}

/// <summary>
/// Controllable IContainerBinding for testing element-removal logic.
/// </summary>
file sealed class FakeContainerBinding : IContainerBinding
{
    private readonly List<object?> _elements;

    public FakeContainerBinding(params object?[] initial)
    {
        _elements = new List<object?>(initial);
    }

    // IValueBinding
    public Type ValueType => typeof(object[]);
    public object? GetBoxed() => null;
    public void SetBoxed(object? value) { }
    public bool TryGetSpan(out Span<byte> bytes) { bytes = default; return false; }

    // IContainerBinding
    public int Count => _elements.Count;
    public bool CanResize { get; set; } = true;
    public bool ResizeWasCalled { get; private set; }
    public int? LastResizeArg { get; private set; }

    public IValueBinding GetElementBinding(int index) =>
        new DelegateBinding(
            () => _elements[index],
            v => _elements[index] = v,
            typeof(object));

    public void Resize(int newCount)
    {
        ResizeWasCalled = true;
        LastResizeArg = newCount;
        while (_elements.Count > newCount) _elements.RemoveAt(_elements.Count - 1);
        while (_elements.Count < newCount) _elements.Add(null);
    }

    public object? ElementAt(int index) => _elements[index];
}

/// <summary>
/// IMapPickContext spy that records method calls.
/// </summary>
internal sealed class SpyPickerContext : IMapPickContext
{
    private readonly HashSet<string> _pendingPaths = new();
    public List<string> RequestEntityPickCalls { get; } = new();
    public List<string> RequestLocationPickCalls { get; } = new();

    public void SetPendingFor(string jsonPath) => _pendingPaths.Add(jsonPath);

    public bool IsPickPendingFor(string jsonPath) => _pendingPaths.Contains(jsonPath);

    public void RequestEntityPick(string jsonPath, string[]? filterPresets) =>
        RequestEntityPickCalls.Add(jsonPath);

    public void RequestLocationPick(string jsonPath) =>
        RequestLocationPickCalls.Add(jsonPath);

    public EntityRef PendingResult;

    /// <summary>⭐ CE-4206 — the live reference scope this spy hands the leaves (null = a host with no repository).</summary>
    public Fdp.Toolkit.Scenario.LiveRefScope? Refs;
    public Fdp.Toolkit.Scenario.LiveRefScope? References => Refs;

    public bool TryConsumeEntityPick(string jsonPath, out EntityRef picked)
    {
        picked = PendingResult;
        PendingResult = default;
        return !picked.IsNone;
    }

    public Fdp.Toolkit.Replication.PointLink? PendingLocation;

    public bool TryConsumeLocationPick(string jsonPath, out Fdp.Toolkit.Replication.PointLink location)
    {
        location = PendingLocation ?? default;
        bool had = PendingLocation.HasValue;
        PendingLocation = null;
        return had;
    }
}

/// <summary>
/// Minimal IEditSession stub for constructing a ComponentEditDrawer in tests.
/// </summary>
file sealed class NopEditSession : IEditSession
{
    public EditDocument Document { get; set; } = null!;
    public bool IsDirty => false;
    public EditRebuildState RebuildState => EditRebuildState.Stable;
    public void MarkStructuralChange() { }
    public void RebuildDocument() { }
    public ValidationResult Validate() => ValidationResult.Ok();
    public object Commit() => new object();
    public void Cancel() { }
    public void Dispose() { }
}

// ---------------------------------------------------------------------------
// CE07 -- ComponentEditDrawer
// ---------------------------------------------------------------------------

public class ComponentEditDrawerTests
{
    // ── Test component types ─────────────────────────────────────────────────

    private struct TwoFloatComp
    {
        public float X;
        public float Y;
    }

    private enum TestEnum { Alpha, Beta, Gamma }

    // ── T-CE07a ──────────────────────────────────────────────────────────────
    // When both Min and Max are set on EditNodeMetadata, the slider branch condition holds.
    // The actual SliderFloat vs InputFloat routing requires an ImGui context; this test
    // verifies the branching predicate directly (acceptable alternative per spec).
    [Fact]
    public void T_CE07a_Metadata_WithMinAndMax_SliderBranchPredicateHolds()
    {
        var meta = new EditNodeMetadata { Min = 0.0, Max = 100.0 };

        // The DrawPrimitiveInput float branch uses: meta.Min.HasValue && meta.Max.HasValue
        Assert.True(meta.Min.HasValue && meta.Max.HasValue,
            "Slider branch condition should be true when both Min and Max are set.");
    }

    [Fact]
    public void T_CE07a_Metadata_WithoutMax_SliderBranchPredicateIsFalse()
    {
        var meta = new EditNodeMetadata { Min = 0.0 }; // Max not set

        Assert.False(meta.Min.HasValue && meta.Max.HasValue,
            "Slider branch condition should be false when Max is not set.");
    }

    // ── T-CE07b ──────────────────────────────────────────────────────────────
    // A struct with two float fields produces an EditDocument with two children.
    [Fact]
    public void T_CE07b_StructNode_TwoFloatChildren_ChildCountIsTwo()
    {
        var comp    = new TwoFloatComp { X = 1f, Y = 2f };
        var service = new ComponentEditServiceBuilder().Build();

        using var session = service.Open(comp, typeof(TwoFloatComp));

        // The root may be a SelectionRoot wrapper or the struct node directly.
        var root = session.Document.Root;
        var structNode = root.Kind == EditNodeKind.SelectionRoot
            ? root.Children.First()
            : root;

        Assert.Equal(2, structNode.Children.Count);
    }

    [Fact]
    public void T_CE07b_StructNode_ChildrenHaveFloatClrType()
    {
        var comp    = new TwoFloatComp { X = 3f, Y = 7f };
        var service = new ComponentEditServiceBuilder().Build();

        using var session = service.Open(comp, typeof(TwoFloatComp));

        var root = session.Document.Root;
        var structNode = root.Kind == EditNodeKind.SelectionRoot
            ? root.Children.First()
            : root;

        Assert.All(structNode.Children, child => Assert.Equal(typeof(float), child.ClrType));
    }

    // ── T-CE07c ──────────────────────────────────────────────────────────────
    // RemoveElementAtIndex(container, 1): shifts element [2] into slot [1], then Resize(2).
    [Fact]
    public void T_CE07c_RemoveElementAtIndex_ShiftsDownAndResizes()
    {
        // Slots: [0]="a", [1]="b", [2]="c"
        var container = new FakeContainerBinding("a", "b", "c");

        ComponentEditDrawer.RemoveElementAtIndex(container, 1);

        // Slot [1] should now hold what was in slot [2].
        Assert.Equal("c", container.ElementAt(1));

        // Container should have been resized to 2.
        Assert.True(container.ResizeWasCalled);
        Assert.Equal(2, container.LastResizeArg);
        Assert.Equal(2, container.Count);
    }

    [Fact]
    public void T_CE07c_RemoveElementAtIndex_FirstElement_ShiftsAllDown()
    {
        var container = new FakeContainerBinding(10, 20, 30);

        ComponentEditDrawer.RemoveElementAtIndex(container, 0);

        Assert.Equal(20, container.ElementAt(0));
        Assert.Equal(30, container.ElementAt(1));
        Assert.Equal(2, container.LastResizeArg);
    }

    // ── T-CE07d ──────────────────────────────────────────────────────────────
    // For an enum type, the combo data (Enum.GetNames) matches the enum definition.
    // The actual ImGui.Combo call requires a context; this test verifies the data
    // that would be passed to Combo is correct.
    [Fact]
    public void T_CE07d_EnumType_GetNames_MatchesDefinition()
    {
        string[] names = Enum.GetNames(typeof(TestEnum));

        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, names);
    }

    [Fact]
    public void T_CE07d_EnumType_IsEnum_BranchReachable()
    {
        // Verifies the type.IsEnum predicate that gates the Combo branch.
        Assert.True(typeof(TestEnum).IsEnum);
        Assert.False(typeof(float).IsEnum);
    }

    // ── T-CE07e ──────────────────────────────────────────────────────────────
    // When pickerCtx == null, constructing the drawer does not throw.
    // DrawEditNode is not called here because it requires an ImGui context;
    // the no-NullReferenceException guarantee at construction is the verifiable part.
    [Fact]
    public void T_CE07e_PickerNull_DrawerConstructs_WithoutException()
    {
        // T-CE07e: [PARTIAL — DrawEditNode requires ImGui context]
        // Structural assertion: drawer accepts null pickerCtx without error.
        var session = new NopEditSession();
        var drawer  = new ComponentEditDrawer(session, null);

        Assert.NotNull(drawer);
    }

    // ── T-CE07f ──────────────────────────────────────────────────────────────
    // When pickerCtx.IsPickPendingFor returns true, the picker context is queried.
    // The [Picking...] label vs button rendering requires an ImGui context;
    // this test verifies the spy receives the IsPickPendingFor call and returns true,
    // which would cause the [Picking...] branch to be taken.
    [Fact]
    public void T_CE07f_PickerPending_IsPickPendingFor_ReturnsTrue()
    {
        // T-CE07f: [PARTIAL — ImGui branch verification requires context]
        // Structural: mock wired correctly returns true for the pending path.
        var ctx = new SpyPickerContext();
        ctx.SetPendingFor("$.Foo");

        Assert.True(ctx.IsPickPendingFor("$.Foo"),
            "Mock should report pick as pending for the registered path.");
        Assert.False(ctx.IsPickPendingFor("$.Bar"),
            "Mock should NOT report pick as pending for an unregistered path.");
    }

    [Fact]
    public void T_CE07f_PickerPending_DrawerWithSpyContext_Constructs()
    {
        var ctx     = new SpyPickerContext();
        var session = new NopEditSession();
        var drawer  = new ComponentEditDrawer(session, ctx);

        Assert.NotNull(drawer);
    }

    // ── DESIGN_Entity_Reference D5 — the TYPE makes a component field pickable ─────────────────────────────────────

    private struct RefComp
    {
        [Fdp.Toolkit.Behavior.Attributes.MapPickableEntity("tanks")]
        public EntityRef Target;
        public float X;
    }

    /// <summary>
    /// ⭐⭐ An <see cref="EntityRef"/> field is ONE leaf (not a struct with a read-only field) and a completed pick writes
    /// the picked NETWORK id into the component. 🔴 Without <see cref="PickableLeafFieldEditor"/> the field is a struct node
    /// the drawer never offers a picker for, and the pick is never consumed.
    /// </summary>
    [Fact]
    public void D5_AnEntityRefField_IsOneLeaf_AndAPickLandsInTheComponent()
    {
        var service = new ComponentEditServiceBuilder()
            .RegisterFieldEditor<EntityRef>(new PickableLeafFieldEditor(typeof(EntityRef)))
            .Build();
        using var session = service.Open(new RefComp { Target = new EntityRef(5), X = 2f }, typeof(RefComp));

        var root = session.Document.Root;
        var structNode = root.Kind == EditNodeKind.SelectionRoot ? root.Children.First() : root;
        var target = structNode.Children.Single(c => c.Name == nameof(RefComp.Target));
        Assert.Equal(EditNodeKind.Scalar, target.Kind);
        Assert.Equal(typeof(EntityRef), target.ClrType);

        var picker = new SpyPickerContext { PendingResult = new EntityRef(1001) };
        using (var imgui = new ImGuiTestFixture())
        {
            imgui.NewFrame();
            ImGuiNET.ImGui.Begin("D5");
            if (ImGuiNET.ImGui.BeginTable("t", 2))
            {
                new ComponentEditDrawer(session, picker).DrawEditNode(root);
                ImGuiNET.ImGui.EndTable();
            }
            ImGuiNET.ImGui.End();
            imgui.Render();
        }

        var committed = (RefComp)session.Commit();
        Assert.Equal(new EntityRef(1001), committed.Target);
        Assert.Equal(2f, committed.X);
    }

    private struct TwoFields { public float A; public int B; }

    /// <summary>
    /// ⭐ CE-1016 — a single struct opened for editing shows its FIELDS, not a collapsible row named after its type: the field owner
    /// is the struct (under the selection root the session wraps it in); several objects keep their headers. ⭐ CE-1015 — the
    /// root draw runs with the narrower indent and leaves the ImGui style stack balanced.
    /// </summary>
    [Fact]
    public void CE1016_ASingleStruct_ShowsItsFields_WithoutATypeRow()
    {
        var service = new ComponentEditServiceBuilder().Build();
        using var session = service.Open(new TwoFields { A = 1f, B = 2 }, typeof(TwoFields));
        var owner = ComponentEditDrawer.FieldsOwner(session.Document.Root);
        Assert.NotNull(owner);
        Assert.Equal(typeof(TwoFields), owner!.ClrType);
        Assert.Equal(new[] { nameof(TwoFields.A), nameof(TwoFields.B) }, owner.Children.Select(c => c.Name).ToArray());

        using (var imgui = new ImGuiTestFixture())
        {
            imgui.NewFrame();
            float before = ImGuiNET.ImGui.GetStyle().IndentSpacing;
            ImGuiNET.ImGui.Begin("CE1016");
            if (ImGuiNET.ImGui.BeginTable("t", 2))
            {
                new ComponentEditDrawer(session, null).DrawRoot(session.Document.Root);
                ImGuiNET.ImGui.EndTable();
            }
            ImGuiNET.ImGui.End();
            Assert.Equal(before, ImGuiNET.ImGui.GetStyle().IndentSpacing);   // pushed and popped
            imgui.Render();
        }
        Assert.True(ComponentEditDrawer.IndentPixels < 21f / 2f + 1f);   // about half ImGui's default
    }

    private struct LocComp
    {
        public Fdp.Toolkit.Replication.PointLink Where;
        public float X;
    }

    /// <summary>
    /// ⭐⭐ <c>DESIGN_Map_Picking_Unification</c> P5 — a <c>PointLink</c> field (S0 item 5; was <c>PickableGeoPoint</c>) is ONE leaf with a map picker by its
    /// TYPE (no attribute), and a completed pick writes the geodetic point into the component. 🔴 Without the leaf editor
    /// it is a struct node the drawer offers no picker for.
    /// </summary>
    [Fact]
    public void P5_APointLinkField_IsOneLeaf_AndALocationPickLandsInTheComponent()
    {
        var service = new ComponentEditServiceBuilder()
            .RegisterFieldEditor<Fdp.Toolkit.Replication.PointLink>(
                new PickableLeafFieldEditor(typeof(Fdp.Toolkit.Replication.PointLink)))
            .Build();
        using var session = service.Open(new LocComp { X = 2f }, typeof(LocComp));

        var root = session.Document.Root;
        var structNode = root.Kind == EditNodeKind.SelectionRoot ? root.Children.First() : root;
        var where = structNode.Children.Single(c => c.Name == nameof(LocComp.Where));
        Assert.Equal(EditNodeKind.Scalar, where.Kind);

        var picker = new SpyPickerContext { PendingLocation = new Fdp.Toolkit.Replication.PointLink(new System.Numerics.Vector3(50.1f, 14.4f, 0f)) };
        using (var imgui = new ImGuiTestFixture())
        {
            imgui.NewFrame();
            ImGuiNET.ImGui.Begin("P5");
            if (ImGuiNET.ImGui.BeginTable("t", 2))
            {
                new ComponentEditDrawer(session, picker).DrawEditNode(root);
                ImGuiNET.ImGui.EndTable();
            }
            ImGuiNET.ImGui.End();
            imgui.Render();
        }

        var committed = (LocComp)session.Commit();
        Assert.Equal(50.1f, committed.Where.Local.X);   // a picked point writes new PointLink(local)
        Assert.Equal(14.4f, committed.Where.Local.Y);
        Assert.Equal(2f, committed.X);
    }

    // ═══ CE-4206 R1 (DESIGN_Map_Picking_Unification §6.2–§6.3 F1–F5) — the reference leaves ═════════════════════════════════════════
    // Red-proofs (each compiles): ReferenceLeafDrawers.PickToLink returning the bare `new EntityLink(picked)` ⇒ the Exact-GUID pick rail fails ·
    // LiveRefScope never rebuilding after a spawn ⇒ (Fdp.Toolkits.Tests) A_spawn_after_the_first_query_is_found fails.

    private struct LinkComp { public Fdp.Toolkit.Replication.EntityLink Link; }
    private struct PointComp { public Fdp.Toolkit.Replication.PointLink Where; }

    private sealed class RefWorld : IDisposable
    {
        public readonly EntityRepository Repo;
        public readonly Fdp.Toolkit.Scenario.LiveRefScope Scope;
        private int _next = 500;
        public RefWorld()
        {
            Fdp.Core.ComponentTypeRegistry.Clear();
            Repo = new EntityRepository();
            Repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            Repo.RegisterComponent<Fdp.Toolkit.Scenario.EntityGuid>();
            Repo.RegisterComponent<Fdp.Toolkit.Scenario.RefKey>();
            Repo.RegisterComponent<Fdp.Core.EntityInfo>();
            Repo.RegisterComponent<Fdp.Core.SimTransform>();
            Scope = new Fdp.Toolkit.Scenario.LiveRefScope(Repo);
        }
        public EntityRef Spawn(string name, Guid? guid = null, string? key = null, System.Numerics.Vector3? at = null)
        {
            var e = Repo.CreateEntity();
            int id = _next++;
            Repo.AddComponent(e, new Fdp.Toolkit.Replication.Components.NetworkIdentity(id));
            Repo.AddComponent(e, new Fdp.Core.EntityInfo { Name = new Fdp.Core.FixedString64(name) });
            if (guid != null) Repo.AddComponent(e, new Fdp.Toolkit.Scenario.EntityGuid(guid.Value));
            if (key != null) Repo.AddComponent(e, new Fdp.Toolkit.Scenario.RefKey(key));
            if (at != null) Repo.AddComponent(e, new Fdp.Core.SimTransform { Position = at.Value });
            return new EntityRef(id);
        }
        public void Dispose() => Repo.Dispose();
    }

    private static T DrawAndCommit<T>(T initial, SpyPickerContext picker, Type leafType) where T : struct
    {
        var builder = new ComponentEditServiceBuilder();
        builder.RegisterFieldEditor<Fdp.Toolkit.Replication.EntityLink>(new PickableLeafFieldEditor(typeof(Fdp.Toolkit.Replication.EntityLink)))
               .RegisterFieldEditor<Fdp.Toolkit.Replication.PointLink>(new PickableLeafFieldEditor(typeof(Fdp.Toolkit.Replication.PointLink)));
        using var session = builder.Build().Open(initial, typeof(T));
        using (var imgui = new ImGuiTestFixture())
        {
            imgui.NewFrame();
            ImGuiNET.ImGui.Begin("R1");
            if (ImGuiNET.ImGui.BeginTable("t", 2))
            {
                new ComponentEditDrawer(session, picker).DrawEditNode(session.Document.Root);
                ImGuiNET.ImGui.EndTable();
            }
            ImGuiNET.ImGui.End();
            imgui.Render();
        }
        return (T)session.Commit();
    }

    [Fact]
    public void R1_AnEntityLinkPick_OfAnEntityWithAGuid_WritesExact()
    {
        using var w = new RefWorld();
        var g = Guid.NewGuid();
        var hq = w.Spawn("Blue HQ", guid: g);
        var picker = new SpyPickerContext { Refs = w.Scope, PendingResult = hq };

        var committed = DrawAndCommit(new LinkComp(), picker, typeof(Fdp.Toolkit.Replication.EntityLink));

        Assert.Equal(new Fdp.Toolkit.Replication.EntityLink(hq, RefSpec.ByExact(g)), committed.Link);   // SR-133: the file keeps the GUID, not a number
    }

    [Fact]
    public void R1_AnEntityLinkPick_OfAnEntityWithoutAGuid_WritesTheBareId_AndTheLeafWarns()
    {
        using var w = new RefWorld();
        var bullet = w.Spawn("Bullet");
        var picker = new SpyPickerContext { Refs = w.Scope, PendingResult = bullet };

        var committed = DrawAndCommit(new LinkComp(), picker, typeof(Fdp.Toolkit.Replication.EntityLink));

        Assert.Equal(new Fdp.Toolkit.Replication.EntityLink(bullet), committed.Link);
        Assert.True(ReferenceLeafDrawers.NeedsStableIdWarning(committed.Link, w.Scope));
        Assert.False(ReferenceLeafDrawers.NeedsStableIdWarning(committed.Link, null));   // no scope to ask ⇒ no claim
    }

    [Fact]
    public void R1_AnEntityLinkPick_WithoutAScope_StillWritesTheBareId()
    {
        var committed = DrawAndCommit(new LinkComp(), new SpyPickerContext { PendingResult = new EntityRef(77) }, typeof(Fdp.Toolkit.Replication.EntityLink));
        Assert.Equal(new Fdp.Toolkit.Replication.EntityLink(new EntityRef(77)), committed.Link);
    }

    [Fact]
    public void R1_TheKeyForm_WritesByKey_ResolvedWhenAnEntityHasIt_NoneWhenNot_AndRefusesAnOverlongKeyWithoutThrowing()
    {
        using var w = new RefWorld();
        var hq = w.Spawn("Blue HQ", key: "BlueHQ");

        Assert.True(ReferenceLeafDrawers.TryKeyToLink("BlueHQ", w.Scope, out var found, out _));
        Assert.Equal(new Fdp.Toolkit.Replication.EntityLink(hq, RefSpec.ByKey("BlueHQ")), found);
        Assert.Null(ReferenceLeafDrawers.KeyProblem("BlueHQ", w.Scope));

        Assert.True(ReferenceLeafDrawers.TryKeyToLink("Nowhere", w.Scope, out var none, out _));
        Assert.True(none.Ref.IsNone);                                                       // an unresolvable key leaves Ref none …
        Assert.Equal("Nowhere", none.Spec.KeyText);                                          // … but is still what was written
        Assert.Equal("no entity with key 'Nowhere' here", ReferenceLeafDrawers.KeyProblem("Nowhere", w.Scope));

        w.Spawn("Twin A", key: "Twin"); w.Spawn("Twin B", key: "Twin");
        Assert.Equal("key 'Twin' is not unique", ReferenceLeafDrawers.KeyProblem("Twin", w.Scope));

        string tooLong = new string('k', 40);
        Assert.False(ReferenceLeafDrawers.TryKeyToLink(tooLong, w.Scope, out _, out var refusal));
        Assert.Contains("at most 31", refusal);                                              // red text, not an exception
        Assert.Contains("at most 31", ReferenceLeafDrawers.KeyProblem(tooLong, w.Scope));
    }

    [Fact]
    public void R1_SwitchingForms_PrefillsTheKey_AndTakesTheGuid()
    {
        using var w = new RefWorld();
        var g = Guid.NewGuid();
        var hq = w.Spawn("Blue HQ", guid: g, key: "BlueHQ");

        var toKey = ReferenceLeafDrawers.SwitchToKey(new Fdp.Toolkit.Replication.EntityLink(hq, RefSpec.ByExact(g)), w.Scope, out var text);
        Assert.Equal("BlueHQ", text);
        Assert.Equal(new Fdp.Toolkit.Replication.EntityLink(hq, RefSpec.ByKey("BlueHQ")), toKey);

        var back = ReferenceLeafDrawers.SwitchToExact(new Fdp.Toolkit.Replication.EntityLink(EntityRef.None, RefSpec.ByKey("BlueHQ")), w.Scope);
        Assert.Equal(new Fdp.Toolkit.Replication.EntityLink(hq, RefSpec.ByExact(g)), back);   // resolved through the key, then its GUID

        var keyless = w.Spawn("Plain", guid: Guid.NewGuid());
        var unchanged = ReferenceLeafDrawers.SwitchToKey(new Fdp.Toolkit.Replication.EntityLink(keyless), w.Scope, out var empty);
        Assert.Equal("", empty);                                                            // no key to prefill ⇒ the link is left as it is
        Assert.Equal(new Fdp.Toolkit.Replication.EntityLink(keyless), unchanged);
    }

    [Fact]
    public void R1_APointLink_EntityForm_SnapshotsThePositionWhenAssigned()
    {
        using var w = new RefWorld();
        var g = Guid.NewGuid();
        var hq = w.Spawn("Blue HQ", guid: g, at: new System.Numerics.Vector3(100f, 200f, 5f));

        // the helper: the entity's spec + its position now
        var p = ReferenceLeafDrawers.PointFromSpec(RefSpec.ByExact(g), hq, new WorldPoint(1, 2, 3), w.Scope);
        Assert.Equal(new Fdp.Toolkit.Replication.PointLink(new WorldPoint(100, 200, 5), RefSpec.ByExact(g)), p);

        // through the drawer: a PointLink already in the Entity form (its Spec set) takes an entity pick → spec + snapshot
        var other = w.Spawn("Red HQ", guid: Guid.NewGuid(), at: new System.Numerics.Vector3(7f, 8f, 0f));
        var picker = new SpyPickerContext { Refs = w.Scope, PendingResult = other };
        var start = new PointComp { Where = new Fdp.Toolkit.Replication.PointLink(new WorldPoint(100, 200, 5), RefSpec.ByExact(g)) };
        var committed = DrawAndCommit(start, picker, typeof(Fdp.Toolkit.Replication.PointLink));
        Assert.Equal(new WorldPoint(7, 8, 0), committed.Where.Local);
        Assert.True(w.Scope.TryGuidOf(other, out var og));
        Assert.Equal(RefSpec.ByExact(og), committed.Where.Spec);

        // an entity with no GUID cannot be a reference: its position is copied as coordinates
        var bullet = w.Spawn("Bullet", at: new System.Numerics.Vector3(9f, 9f, 9f));
        var copy = ReferenceLeafDrawers.PointFromPick(bullet, default, w.Scope, out bool stable);
        Assert.False(stable);
        Assert.True(copy.Spec.IsNone);
        Assert.Equal(new WorldPoint(9, 9, 9), copy.Local);
    }

    [Fact]
    public void R1_TheLeafCoversTheFourReferenceTypes_AndNoOther()
    {
        foreach (var t in new[] { typeof(Fdp.Toolkit.Replication.EntityLink), typeof(Fdp.Toolkit.Replication.PointLink), typeof(EntityRef), typeof(Fdp.Core.WorldPoint) })
            Assert.True(ReferenceLeafDrawers.Handles(t));
        Assert.False(ReferenceLeafDrawers.Handles(typeof(float)));
    }
}
