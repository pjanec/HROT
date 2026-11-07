using FluentAssertions;
using StructEdit.Core.Text;

namespace StructEdit.Tests.Foundation;

[Flags] public enum VtColours { None = 0, Red = 1, Green = 2, Blue = 4 }
public enum VtMood { Calm, Alert }

/// <summary>
/// ⭐ S6 / S9 (📄 docs/DESIGN_Struct_Editor.md §6, CE-3380) — the built-in Parse / Format: a refusal is a MESSAGE, <c>long</c> is a <c>long</c>, flags read and write their names
/// joined, a nullable reads empty as null, numbers are invariant and round-trip.
/// 🔴 Red-proof (compiles): the integer branch's range check removed ⇒ <c>300</c> as a byte overflows instead of refusing ⇒ red.
/// </summary>
public class ValueTextTests
{
    [Theory]
    [InlineData(typeof(int), "3x", "'3x' is not a whole number")]
    [InlineData(typeof(int), "1.5", "'1.5' is not a whole number")]
    [InlineData(typeof(byte), "300", "300 is out of range (0 … 255)")]
    [InlineData(typeof(float), "abc", "'abc' is not a number")]
    [InlineData(typeof(VtMood), "Sleepy", "'Sleepy' is not one of Calm, Alert")]
    [InlineData(typeof(VtMood), "1", "'1' is not one of Calm, Alert")]
    [InlineData(typeof(Guid), "nope", "'nope' is not an id (32 hex digits)")]
    public void ARefusal_SaysWhy(Type type, string text, string problem)
    {
        ValueText.TryParse(type, text, out var v, out var p).Should().BeFalse();
        v.Should().BeNull();
        p.Should().Be(problem);
    }

    [Fact]
    public void ALong_IsALong_PastIntRange()
    {
        ValueText.TryParse(typeof(long), "5000000000", out var v, out _).Should().BeTrue();
        v.Should().BeOfType<long>().And.Be(5_000_000_000L);
    }

    [Fact]
    public void Flags_ReadAndWriteTheirNamesJoined()
    {
        ValueText.TryParse(typeof(VtColours), "red | Blue", out var v, out _).Should().BeTrue();
        v.Should().Be(VtColours.Red | VtColours.Blue);
        ValueText.Format(v).Should().Be("Red, Blue");
        ValueText.TryParse(typeof(VtColours), ValueText.Format(v), out var back, out _).Should().BeTrue();
        back.Should().Be(v);
        ValueText.TryParse(typeof(VtColours), "", out var none, out _).Should().BeTrue();
        none.Should().Be(VtColours.None);
    }

    [Fact]
    public void ANullable_ReadsEmptyAsNull_AndAValueAsItsType()
    {
        ValueText.TryParse(typeof(int?), " ", out var v, out var p).Should().BeTrue();
        v.Should().BeNull(); p.Should().BeNull();
        ValueText.TryParse(typeof(int?), "7", out var seven, out _).Should().BeTrue();
        seven.Should().Be(7);
    }

    [Fact]
    public void Numbers_AreInvariant_AndRoundTrip()
    {
        ValueText.Format(0.1f).Should().Be("0.1");
        ValueText.TryParse(typeof(float), ValueText.Format(1.0f / 3f), out var f, out _).Should().BeTrue();
        f.Should().Be(1.0f / 3f);
        ValueText.TryParse(typeof(double), "1e3", out var d, out _).Should().BeTrue();
        d.Should().Be(1000d);
        ValueText.TryParse(typeof(bool), "yes", out var b, out _).Should().BeTrue();
        b.Should().Be(true);
    }
}
