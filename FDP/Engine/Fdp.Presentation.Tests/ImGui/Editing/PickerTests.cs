using System;
using System.Numerics;
using System.Reflection;
using Fdp.Core;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Replication;
using Xunit;

#pragma warning disable CS0649

namespace Fdp.Presentation.Tests.ImGui.Editing;

// ---------------------------------------------------------------------------
// CE04 -- Picker Attributes
// ---------------------------------------------------------------------------

public class PickerAttributesTests
{
    // Helper struct with annotated fields used by the attribute reflection tests.
    private struct TestComponent
    {
        [MapPickableEntity("tanks", "infantry")]
        public EntityRef EntityWithPresets;

        [MapPickableEntity]
        public EntityRef EntityNoPresets;

    }

    // -- T-CE04a --------------------------------------------------------------
    // [MapPickableEntity("tanks", "infantry")] -> FilterPresets == ["tanks", "infantry"]
    [Fact]
    public void T_CE04a_MapPickableEntity_WithArgs_FilterPresetsArePreserved()
    {
        var field = typeof(TestComponent).GetField(nameof(TestComponent.EntityWithPresets))!;
        var attr = field.GetCustomAttribute<MapPickableEntityAttribute>()!;

        Assert.Equal(new[] { "tanks", "infantry" }, attr.FilterPresets);
    }

    // -- T-CE04b --------------------------------------------------------------
    // [MapPickableEntity] (no args) -> no filter (null)
    [Fact]
    public void T_CE04b_MapPickableEntity_NoArgs_FilterPresetsIsEmpty()
    {
        var field = typeof(TestComponent).GetField(nameof(TestComponent.EntityNoPresets))!;
        var attr = field.GetCustomAttribute<MapPickableEntityAttribute>()!;

        Assert.Null(attr.FilterPresets);
    }
}

// ---------------------------------------------------------------------------
// CE05 -- IComponentPickerContext
// ---------------------------------------------------------------------------

public class IComponentPickerContextTests
{
    // NOP implementation used by both tests.
    private sealed class NopPickerContext : IComponentPickerContext
    {
        public bool IsPickPendingFor(string jsonPath) => false;
        public void RequestEntityPick(string jsonPath, string[]? filterPresets) { }
        public void RequestLocationPick(string jsonPath) { }
        public bool TryConsumeEntityPick(string jsonPath, out EntityRef picked)
        {
            picked = default;
            return false;
        }
        public bool TryConsumeLocationPick(string jsonPath, out Fdp.Toolkit.Behavior.Params.PickableGeoPoint location)
        {
            location = default;
            return false;
        }
    }

    // -- T-CE05a --------------------------------------------------------------
    // A mock implementation compiles and all five methods can be invoked without error.
    [Fact]
    public void T_CE05a_NopPickerContext_AllMethodsInvokableWithoutError()
    {
        IComponentPickerContext ctx = new NopPickerContext();

        _ = ctx.IsPickPendingFor("$.Field");
        ctx.RequestEntityPick("$.Field", null);
        ctx.RequestEntityPick("$.Field", new[] { "tanks" });
        ctx.RequestLocationPick("$.Location");
        ctx.TryConsumeEntityPick("$.Field", out _);
        ctx.TryConsumeLocationPick("$.Location", out _);
    }

    // -- T-CE05b --------------------------------------------------------------
    // TryConsumeEntityPick for a path with no pending pick returns false and
    // the out reference is none.
    [Fact]
    public void T_CE05b_TryConsumeEntityPick_NoPendingPick_ReturnsFalseAndDefault()
    {
        IComponentPickerContext ctx = new NopPickerContext();

        bool result = ctx.TryConsumeEntityPick("$.Targets[0]", out EntityRef e);

        Assert.False(result);
        Assert.True(e.IsNone);
    }
}
