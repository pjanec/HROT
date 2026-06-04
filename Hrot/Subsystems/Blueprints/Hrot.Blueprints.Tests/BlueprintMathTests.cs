using System.Numerics;
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Tests;

/// <summary>
/// Unit tests for BlueprintMath — validates representative results and all edge cases
/// mandated by BATCH-05: div-by-zero, vector ops, bool ops, int ops, float ops.
/// </summary>
public sealed class BlueprintMathTests
{
    // ── Float arithmetic ──────────────────────────────────────────────────────

    [Fact] public void Add_ReturnsSum()           => Assert.Equal(5f,  BlueprintMath.Add(2f, 3f));
    [Fact] public void Subtract_ReturnsDiff()      => Assert.Equal(-1f, BlueprintMath.Subtract(2f, 3f));
    [Fact] public void Multiply_ReturnsProduct()   => Assert.Equal(6f,  BlueprintMath.Multiply(2f, 3f));
    [Fact] public void Divide_ReturnsQuotient()    => Assert.Equal(2.5f,BlueprintMath.Divide(5f, 2f));
    [Fact] public void Divide_ByZero_ReturnsZero() => Assert.Equal(0f,  BlueprintMath.Divide(7f, 0f));
    [Fact] public void Modulo_ReturnsRemainder()   => Assert.Equal(1f,  BlueprintMath.Modulo(7f, 3f));
    [Fact] public void Modulo_ByZero_ReturnsZero() => Assert.Equal(0f,  BlueprintMath.Modulo(7f, 0f));
    [Fact] public void Abs_NegativeValue()         => Assert.Equal(3f,  BlueprintMath.Abs(-3f));
    [Fact] public void Negate_Float()              => Assert.Equal(-4f, BlueprintMath.Negate(4f));
    [Fact] public void Min_Float()                 => Assert.Equal(2f,  BlueprintMath.Min(2f, 5f));
    [Fact] public void Max_Float()                 => Assert.Equal(5f,  BlueprintMath.Max(2f, 5f));

    [Fact]
    public void Clamp_ValueBelowMin_ReturnsMin()
        => Assert.Equal(1f, BlueprintMath.Clamp(-5f, 1f, 10f));

    [Fact]
    public void Clamp_ValueAboveMax_ReturnsMax()
        => Assert.Equal(10f, BlueprintMath.Clamp(20f, 1f, 10f));

    [Fact]
    public void Clamp_ValueInRange_ReturnsValue()
        => Assert.Equal(5f, BlueprintMath.Clamp(5f, 1f, 10f));

    [Fact]
    public void Lerp_AtZero_ReturnsA()
        => Assert.Equal(0f, BlueprintMath.Lerp(0f, 10f, 0f));

    [Fact]
    public void Lerp_AtOne_ReturnsB()
        => Assert.Equal(10f, BlueprintMath.Lerp(0f, 10f, 1f));

    [Fact]
    public void Lerp_AtHalf_ReturnsMidpoint()
        => Assert.Equal(5f, BlueprintMath.Lerp(0f, 10f, 0.5f));

    [Fact] public void Floor_RoundsDown() => Assert.Equal(2f, BlueprintMath.Floor(2.9f));
    [Fact] public void Ceil_RoundsUp()    => Assert.Equal(3f, BlueprintMath.Ceil(2.1f));
    // MathF.Round uses banker's rounding (MidpointRounding.ToEven) by default: 2.5 → 2, 3.5 → 4.
    [Fact] public void Round_Midpoint_RoundsToEven() => Assert.Equal(2f, BlueprintMath.Round(2.5f));
    [Fact] public void Round_ClearlyRoundsUp()       => Assert.Equal(3f, BlueprintMath.Round(2.7f));

    [Fact]
    public void Sqrt_PositiveValue()
        => Assert.Equal(3f, BlueprintMath.Sqrt(9f), 5);

    [Fact]
    public void Sqrt_NegativeValue_ReturnsZero()
        => Assert.Equal(0f, BlueprintMath.Sqrt(-1f));

    [Fact]
    public void Pow_TwoToThree()
        => Assert.Equal(8f, BlueprintMath.Pow(2f, 3f), 5);

    [Fact]
    public void Sin_Zero_ReturnsZero()
        => Assert.Equal(0f, BlueprintMath.Sin(0f), 5);

    [Fact]
    public void Cos_Zero_ReturnsOne()
        => Assert.Equal(1f, BlueprintMath.Cos(0f), 5);

    // ── Int arithmetic ────────────────────────────────────────────────────────

    [Fact] public void AddInt_TwoPlusThree()           => Assert.Equal(5,   BlueprintMath.AddInt(2, 3));
    [Fact] public void SubInt_ReturnsDiff()            => Assert.Equal(-1,  BlueprintMath.SubInt(2, 3));
    [Fact] public void MulInt_ReturnsProduct()         => Assert.Equal(6,   BlueprintMath.MulInt(2, 3));
    [Fact] public void DivInt_ReturnsQuotient()        => Assert.Equal(3,   BlueprintMath.DivInt(9, 3));
    [Fact] public void DivInt_ByZero_ReturnsZero()     => Assert.Equal(0,   BlueprintMath.DivInt(9, 0));
    [Fact] public void ModInt_ReturnsRemainder()       => Assert.Equal(1,   BlueprintMath.ModInt(7, 3));
    [Fact] public void ModInt_ByZero_ReturnsZero()     => Assert.Equal(0,   BlueprintMath.ModInt(7, 0));
    [Fact] public void AbsInt_NegativeValue()          => Assert.Equal(5,   BlueprintMath.AbsInt(-5));
    [Fact] public void NegateInt_ReturnsNegative()     => Assert.Equal(-4,  BlueprintMath.NegateInt(4));
    [Fact] public void MinInt_ReturnsSmaller()         => Assert.Equal(2,   BlueprintMath.MinInt(2, 5));
    [Fact] public void MaxInt_ReturnsLarger()          => Assert.Equal(5,   BlueprintMath.MaxInt(2, 5));
    [Fact] public void ClampInt_BelowMin_ReturnsMin()  => Assert.Equal(1,   BlueprintMath.ClampInt(-5, 1, 10));
    [Fact] public void ClampInt_AboveMax_ReturnsMax()  => Assert.Equal(10,  BlueprintMath.ClampInt(20, 1, 10));
    [Fact] public void ClampInt_InRange_ReturnsValue() => Assert.Equal(5,   BlueprintMath.ClampInt(5, 1, 10));

    // ── Float comparisons ─────────────────────────────────────────────────────

    [Fact] public void GreaterThan_True()          => Assert.True(BlueprintMath.GreaterThan(5f, 3f));
    [Fact] public void GreaterThan_False()         => Assert.False(BlueprintMath.GreaterThan(3f, 5f));
    [Fact] public void LessThan_True()             => Assert.True(BlueprintMath.LessThan(3f, 5f));
    [Fact] public void GreaterOrEqual_Equal()      => Assert.True(BlueprintMath.GreaterOrEqual(5f, 5f));
    [Fact] public void LessOrEqual_Less()          => Assert.True(BlueprintMath.LessOrEqual(3f, 5f));

    [Fact]
    public void ApproxEquals_WithinEpsilon_ReturnsTrue()
        => Assert.True(BlueprintMath.ApproxEquals(1.0f, 1.000001f, 1e-4f));

    [Fact]
    public void ApproxEquals_OutsideEpsilon_ReturnsFalse()
        => Assert.False(BlueprintMath.ApproxEquals(1.0f, 1.1f, 1e-4f));

    // ── Int comparisons ───────────────────────────────────────────────────────

    [Fact] public void EqualsInt_Equal()              => Assert.True(BlueprintMath.EqualsInt(3, 3));
    [Fact] public void EqualsInt_NotEqual()           => Assert.False(BlueprintMath.EqualsInt(3, 4));
    [Fact] public void GreaterThanInt_True()          => Assert.True(BlueprintMath.GreaterThanInt(5, 3));
    [Fact] public void LessThanInt_True()             => Assert.True(BlueprintMath.LessThanInt(3, 5));

    // ── Bool logic ────────────────────────────────────────────────────────────

    [Fact] public void And_TrueTrue_ReturnsTrue()    => Assert.True(BlueprintMath.And(true, true));
    [Fact] public void And_TrueFalse_ReturnsFalse()  => Assert.False(BlueprintMath.And(true, false));
    [Fact] public void Or_FalseFalse_ReturnsFalse()  => Assert.False(BlueprintMath.Or(false, false));
    [Fact] public void Or_FalseTrue_ReturnsTrue()    => Assert.True(BlueprintMath.Or(false, true));
    [Fact] public void Not_True_ReturnsFalse()       => Assert.False(BlueprintMath.Not(true));
    [Fact] public void Not_False_ReturnsTrue()       => Assert.True(BlueprintMath.Not(false));
    [Fact] public void Xor_SameValues_ReturnsFalse() => Assert.False(BlueprintMath.Xor(true, true));
    [Fact] public void Xor_DiffValues_ReturnsTrue()  => Assert.True(BlueprintMath.Xor(true, false));

    // ── Vector3 ops ───────────────────────────────────────────────────────────

    [Fact]
    public void AddVec_ReturnsSum()
    {
        var a = new Vector3(1f, 2f, 3f);
        var b = new Vector3(4f, 5f, 6f);
        var r = BlueprintMath.AddVec(a, b);
        Assert.Equal(new Vector3(5f, 7f, 9f), r);
    }

    [Fact]
    public void SubVec_ReturnsDiff()
    {
        var a = new Vector3(4f, 5f, 6f);
        var b = new Vector3(1f, 2f, 3f);
        var r = BlueprintMath.SubVec(a, b);
        Assert.Equal(new Vector3(3f, 3f, 3f), r);
    }

    [Fact]
    public void MulVecScalar_ReturnsScaled()
    {
        var a = new Vector3(1f, 2f, 3f);
        var r = BlueprintMath.MulVecScalar(a, 2f);
        Assert.Equal(new Vector3(2f, 4f, 6f), r);
    }

    [Fact]
    public void Dot_PerpendicularVectors_ReturnsZero()
    {
        var a = new Vector3(1f, 0f, 0f);
        var b = new Vector3(0f, 1f, 0f);
        Assert.Equal(0f, BlueprintMath.Dot(a, b), 5);
    }

    [Fact]
    public void Dot_ParallelVectors_ReturnsProduct()
    {
        var a = new Vector3(2f, 0f, 0f);
        var b = new Vector3(3f, 0f, 0f);
        Assert.Equal(6f, BlueprintMath.Dot(a, b), 5);
    }

    [Fact]
    public void Cross_XaxisYaxis_ReturnsZaxis()
    {
        var a = new Vector3(1f, 0f, 0f);
        var b = new Vector3(0f, 1f, 0f);
        var r = BlueprintMath.Cross(a, b);
        Assert.Equal(0f, r.X, 5);
        Assert.Equal(0f, r.Y, 5);
        Assert.Equal(1f, r.Z, 5);
    }

    [Fact]
    public void Normalize_UnitVector_LengthIsOne()
    {
        var a = new Vector3(3f, 4f, 0f);
        var r = BlueprintMath.Normalize(a);
        Assert.Equal(1f, r.Length(), 5);
    }

    [Fact]
    public void Normalize_ZeroVector_ReturnsZero()
    {
        var r = BlueprintMath.Normalize(Vector3.Zero);
        Assert.Equal(Vector3.Zero, r);
    }

    [Fact]
    public void Length_KnownVector_ReturnsCorrectLength()
    {
        var a = new Vector3(3f, 4f, 0f);
        Assert.Equal(5f, BlueprintMath.Length(a), 5);
    }

    [Fact]
    public void Distance_TwoPoints_ReturnsCorrectDistance()
    {
        var a = new Vector3(0f, 0f, 0f);
        var b = new Vector3(3f, 4f, 0f);
        Assert.Equal(5f, BlueprintMath.Distance(a, b), 5);
    }
}
