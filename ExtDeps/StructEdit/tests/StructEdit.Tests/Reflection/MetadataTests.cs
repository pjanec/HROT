using StructEdit.Core;
using StructEdit.Core.Attributes;
using StructEdit.Core.Memory;
using StructEdit.Reflection;
using System.Reflection;

namespace StructEdit.Tests.Reflection;

// ── Test fixtures ─────────────────────────────────────────────────────────────

// Custom domain attribute (opaque to StructEdit)
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
file sealed class MyCustomAttribute : Attribute { }

file class KnownOnlyClass
{
    [EditRange(0, 1)]
    public float Speed;
}

file class CustomOnlyClass
{
    [MyCustomAttribute]
    public int Target;
}

file class MixedClass
{
    [EditUnit("m/s")]
    [MyCustomAttribute]
    public float Velocity;
}

file class NoAttrClass
{
    public int Plain;
}

// ── TASK-CE02: EditNodeMetadata.CustomAttributes tests ────────────────────────

public class MetadataTests
{
    private static readonly ReflectionEditDocumentBuilder Builder = new();

    // T-CE02a: known-only attributes
    // Field decorated with [EditRange(0, 1)] only.
    // ReadMetadata returns Min==0, Max==1, CustomAttributes.Count==0.
    [Fact]
    public void T_CE02a_KnownAttributes_CustomAttributesIsEmpty()
    {
        var buffer = new ManagedObjectEditBuffer(typeof(KnownOnlyClass), new KnownOnlyClass());
        var doc = Builder.Build(buffer, typeof(KnownOnlyClass), EditScope.WholeComponent, null);

        var child = Assert.Single(doc.Root.Children);
        Assert.Equal(0.0, child.Metadata.Min);
        Assert.Equal(1.0, child.Metadata.Max);
        Assert.Equal(0, child.Metadata.CustomAttributes.Count);
    }

    // T-CE02b: custom attribute only.
    // Field decorated with [MyCustomAttribute].
    // ReadMetadata returns CustomAttributes.Count==1 and CustomAttributes[0] is MyCustomAttribute.
    [Fact]
    public void T_CE02b_CustomAttributeOnly_IsStoredInCustomAttributes()
    {
        var buffer = new ManagedObjectEditBuffer(typeof(CustomOnlyClass), new CustomOnlyClass());
        var doc = Builder.Build(buffer, typeof(CustomOnlyClass), EditScope.WholeComponent, null);

        var child = Assert.Single(doc.Root.Children);
        Assert.Equal(1, child.Metadata.CustomAttributes.Count);
        Assert.IsType<MyCustomAttribute>(child.Metadata.CustomAttributes[0]);
    }

    // T-CE02c: mixed attributes.
    // Field with [EditUnit("m/s")] and [MyCustomAttribute].
    // ReadMetadata returns Unit=="m/s", CustomAttributes.Count==1.
    [Fact]
    public void T_CE02c_MixedAttributes_KnownAndCustomBothPresent()
    {
        var buffer = new ManagedObjectEditBuffer(typeof(MixedClass), new MixedClass());
        var doc = Builder.Build(buffer, typeof(MixedClass), EditScope.WholeComponent, null);

        var child = Assert.Single(doc.Root.Children);
        Assert.Equal("m/s", child.Metadata.Unit);
        Assert.Equal(1, child.Metadata.CustomAttributes.Count);
        Assert.IsType<MyCustomAttribute>(child.Metadata.CustomAttributes[0]);
    }

    // T-CE02d: EditNodeMetadata.Empty.CustomAttributes is Array.Empty<Attribute>() (reference-equal).
    [Fact]
    public void T_CE02d_EmptySingleton_CustomAttributesIsArrayEmpty()
    {
        // Must be reference-equal (no new allocation for the common case)
        Assert.Same(Array.Empty<Attribute>(), EditNodeMetadata.Empty.CustomAttributes);
    }
}
