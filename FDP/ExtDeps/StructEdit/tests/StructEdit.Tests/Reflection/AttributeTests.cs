using StructEdit.Core.Attributes;
using System.Reflection;

namespace StructEdit.Tests.Reflection;

// ─── test fixtures ────────────────────────────────────────────────────────────

file class AnnotatedType
{
    [EditRange(0, 100)]
    public int X;

    [EditUnit("m/s")]
    public float Speed { get; set; }

    [FixedBufferHint(typeof(byte), 128)]
    public int Buffer;
}

// ─── TASK-R001: Attribute tests ───────────────────────────────────────────────

public class AttributeTests
{
    [Fact]
    public void EditRange_AppliesToField_CarriesMinMax()
    {
        var attr = typeof(AnnotatedType)
            .GetField("X")!
            .GetCustomAttribute<EditRangeAttribute>()!;

        Assert.NotNull(attr);
        Assert.Equal(0.0, attr.Min);
        Assert.Equal(100.0, attr.Max);
    }

    [Fact]
    public void EditUnit_AppliesToProperty_CarriesUnit()
    {
        var attr = typeof(AnnotatedType)
            .GetProperty("Speed")!
            .GetCustomAttribute<EditUnitAttribute>()!;

        Assert.NotNull(attr);
        Assert.Equal("m/s", attr.Unit);
    }

    [Fact]
    public void FixedBufferHint_CarriesElementTypeAndLength()
    {
        var attr = typeof(AnnotatedType)
            .GetField("Buffer")!
            .GetCustomAttribute<FixedBufferHintAttribute>()!;

        Assert.NotNull(attr);
        Assert.Equal(typeof(byte), attr.ElementType);
        Assert.Equal(128, attr.Length);
    }
}
