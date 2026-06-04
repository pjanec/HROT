using System.Numerics;

namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Pure, static, deterministic math helpers for Blueprint FunctionCall nodes.
/// Generated code calls these as global::Fdp.Toolkit.Blueprints.BlueprintMath.X(args).
/// All methods are side-effect-free; division/modulo by zero returns 0 (no throw —
/// these run in the sim hot path).
/// </summary>
public static class BlueprintMath
{
    // ── Float arithmetic ──────────────────────────────────────────────────────

    /// <summary>Add two floats.</summary>
    public static float Add(float a, float b) => a + b;

    /// <summary>Subtract b from a.</summary>
    public static float Subtract(float a, float b) => a - b;

    /// <summary>Multiply two floats.</summary>
    public static float Multiply(float a, float b) => a * b;

    /// <summary>Divide a by b; returns 0 if b is zero.</summary>
    public static float Divide(float a, float b) => b == 0f ? 0f : a / b;

    /// <summary>Remainder of a divided by b; returns 0 if b is zero.</summary>
    public static float Modulo(float a, float b) => b == 0f ? 0f : a % b;

    /// <summary>Absolute value of a float.</summary>
    public static float Abs(float value) => MathF.Abs(value);

    /// <summary>Negate a float.</summary>
    public static float Negate(float value) => -value;

    /// <summary>Minimum of two floats.</summary>
    public static float Min(float a, float b) => MathF.Min(a, b);

    /// <summary>Maximum of two floats.</summary>
    public static float Max(float a, float b) => MathF.Max(a, b);

    /// <summary>Clamp a value between min and max.</summary>
    public static float Clamp(float value, float min, float max)
        => MathF.Max(min, MathF.Min(max, value));

    /// <summary>Linear interpolation: a + (b - a) * alpha.</summary>
    public static float Lerp(float a, float b, float alpha)
        => a + (b - a) * alpha;

    /// <summary>Floor of a float.</summary>
    public static float Floor(float value) => MathF.Floor(value);

    /// <summary>Ceiling of a float.</summary>
    public static float Ceil(float value) => MathF.Ceiling(value);

    /// <summary>Round to nearest integer (midpoint rounds to even).</summary>
    public static float Round(float value) => MathF.Round(value);

    /// <summary>Square root; returns 0 for negative input.</summary>
    public static float Sqrt(float value) => value < 0f ? 0f : MathF.Sqrt(value);

    /// <summary>Raise base to the given power.</summary>
    public static float Pow(float @base, float exp) => MathF.Pow(@base, exp);

    /// <summary>Sine of angle (radians).</summary>
    public static float Sin(float angle) => MathF.Sin(angle);

    /// <summary>Cosine of angle (radians).</summary>
    public static float Cos(float angle) => MathF.Cos(angle);

    // ── Int arithmetic ────────────────────────────────────────────────────────

    /// <summary>Add two integers.</summary>
    public static int AddInt(int a, int b) => a + b;

    /// <summary>Subtract b from a (integer).</summary>
    public static int SubInt(int a, int b) => a - b;

    /// <summary>Multiply two integers.</summary>
    public static int MulInt(int a, int b) => a * b;

    /// <summary>Divide a by b (integer); returns 0 if b is zero.</summary>
    public static int DivInt(int a, int b) => b == 0 ? 0 : a / b;

    /// <summary>Remainder of a divided by b (integer); returns 0 if b is zero.</summary>
    public static int ModInt(int a, int b) => b == 0 ? 0 : a % b;

    /// <summary>Absolute value of an integer.</summary>
    public static int AbsInt(int value) => Math.Abs(value);

    /// <summary>Negate an integer.</summary>
    public static int NegateInt(int value) => -value;

    /// <summary>Minimum of two integers.</summary>
    public static int MinInt(int a, int b) => Math.Min(a, b);

    /// <summary>Maximum of two integers.</summary>
    public static int MaxInt(int a, int b) => Math.Max(a, b);

    /// <summary>Clamp an integer between min and max.</summary>
    public static int ClampInt(int value, int min, int max)
        => Math.Max(min, Math.Min(max, value));

    // ── Float comparisons → bool ──────────────────────────────────────────────

    /// <summary>True if a &gt; b.</summary>
    public static bool GreaterThan(float a, float b) => a > b;

    /// <summary>True if a &lt; b.</summary>
    public static bool LessThan(float a, float b) => a < b;

    /// <summary>True if a &gt;= b.</summary>
    public static bool GreaterOrEqual(float a, float b) => a >= b;

    /// <summary>True if a &lt;= b.</summary>
    public static bool LessOrEqual(float a, float b) => a <= b;

    /// <summary>True if |a - b| &lt;= epsilon.</summary>
    public static bool ApproxEquals(float a, float b, float epsilon = 1e-5f)
        => MathF.Abs(a - b) <= epsilon;

    // ── Int comparisons → bool ────────────────────────────────────────────────

    /// <summary>True if a == b (integer).</summary>
    public static bool EqualsInt(int a, int b) => a == b;

    /// <summary>True if a &gt; b (integer).</summary>
    public static bool GreaterThanInt(int a, int b) => a > b;

    /// <summary>True if a &lt; b (integer).</summary>
    public static bool LessThanInt(int a, int b) => a < b;

    // ── Bool logic ────────────────────────────────────────────────────────────

    /// <summary>Logical AND.</summary>
    public static bool And(bool a, bool b) => a && b;

    /// <summary>Logical OR.</summary>
    public static bool Or(bool a, bool b) => a || b;

    /// <summary>Logical NOT.</summary>
    public static bool Not(bool value) => !value;

    /// <summary>Logical XOR.</summary>
    public static bool Xor(bool a, bool b) => a ^ b;

    // ── Vector3 (System.Numerics.Vector3) ────────────────────────────────────

    /// <summary>Add two Vector3 values.</summary>
    public static Vector3 AddVec(Vector3 a, Vector3 b) => a + b;

    /// <summary>Subtract b from a (Vector3).</summary>
    public static Vector3 SubVec(Vector3 a, Vector3 b) => a - b;

    /// <summary>Multiply a Vector3 by a scalar.</summary>
    public static Vector3 MulVecScalar(Vector3 a, float scalar) => a * scalar;

    /// <summary>Dot product of two Vector3 values.</summary>
    public static float Dot(Vector3 a, Vector3 b) => Vector3.Dot(a, b);

    /// <summary>Cross product of two Vector3 values.</summary>
    public static Vector3 Cross(Vector3 a, Vector3 b) => Vector3.Cross(a, b);

    /// <summary>Normalize a Vector3; returns zero vector if length is zero.</summary>
    public static Vector3 Normalize(Vector3 a)
    {
        float len = a.Length();
        return len < 1e-10f ? Vector3.Zero : a / len;
    }

    /// <summary>Length (magnitude) of a Vector3.</summary>
    public static float Length(Vector3 a) => a.Length();

    /// <summary>Distance between two Vector3 points.</summary>
    public static float Distance(Vector3 a, Vector3 b) => Vector3.Distance(a, b);
}
