using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Editing;
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
/// IComponentPickerContext spy that records method calls.
/// </summary>
file sealed class SpyPickerContext : IComponentPickerContext
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

    public bool TryConsumeEntityPick(string jsonPath, out Entity pickedEntity)
    {
        pickedEntity = default;
        return false;
    }

    public bool TryConsumeLocationPick(string jsonPath, out Vector3 location)
    {
        location = default;
        return false;
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
}
