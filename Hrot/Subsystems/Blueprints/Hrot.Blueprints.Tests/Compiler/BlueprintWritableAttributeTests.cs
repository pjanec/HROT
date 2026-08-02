using System.Reflection;
using Fdp.Core;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// CA-03 (Slice W1) -- smoke coverage for <see cref="BlueprintWritableAttribute"/> (co-located with
/// <see cref="ComponentIdAttribute"/> in Fdp.Core, per the design). This is EDITOR-primary (CA-04's
/// write picker) -- the compiler never checks it (see <c>V_ComponentAccessRules</c>'s doc comment) --
/// so this only proves the attribute exists, is reflectable, and can coexist with
/// <see cref="ComponentIdAttribute"/> on the same type (the shape a real writable component takes).
/// </summary>
public sealed class BlueprintWritableAttributeTests
{
    [ComponentId(499)]
    [BlueprintWritable]
    private struct WritableTestComponent
    {
        public int Health;
        public float Speed;
    }

    [ComponentId(498)]
    private struct NonWritableTestComponent
    {
        public int Value;
    }

    [Fact]
    public void BlueprintWritableAttribute_IsPresent_OnMarkedComponent()
    {
        var attr = typeof(WritableTestComponent).GetCustomAttribute<BlueprintWritableAttribute>();
        Assert.NotNull(attr);
    }

    [Fact]
    public void BlueprintWritableAttribute_CoexistsWith_ComponentIdAttribute()
    {
        var componentIdAttr = typeof(WritableTestComponent).GetCustomAttribute<ComponentIdAttribute>();
        Assert.NotNull(componentIdAttr);
        Assert.Equal(499, componentIdAttr!.Id);
    }

    [Fact]
    public void BlueprintWritableAttribute_AbsentByDefault_OnUnmarkedComponent()
    {
        var attr = typeof(NonWritableTestComponent).GetCustomAttribute<BlueprintWritableAttribute>();
        Assert.Null(attr);
    }
}
