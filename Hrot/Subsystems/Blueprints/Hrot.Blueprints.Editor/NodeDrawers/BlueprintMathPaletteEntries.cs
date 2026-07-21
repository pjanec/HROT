using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Palette <see cref="NodeKindDescriptor"/> factories for
/// <c>Fdp.Toolkit.Blueprints.BlueprintMath</c> static methods.
/// <para>
/// Each descriptor creates a <see cref="FunctionCallNode"/> pre-configured with
/// <c>TargetTypeId = "Fdp.Toolkit.Blueprints.BlueprintMath"</c>, the corresponding
/// <c>MethodName</c>, and <c>IsPure = true</c>.  Pin projection is handled at render
/// time by <c>NodePinSchema.FunctionCallPins</c> via reflection — no hand-authored
/// pins are needed here.
/// </para>
/// <para>
/// BlueprintMath uses distinct method names (no overloads), so
/// <c>NodePinSchema.ResolveMethod</c>'s first-match is always unambiguous.
/// </para>
/// </summary>
public static class BlueprintMathPaletteEntries
{
    private const string TargetTypeId = "Fdp.Toolkit.Blueprints.BlueprintMath";

    /// <summary>Category names for math node grouping in the picker.</summary>
    public static class Categories
    {
        public const string Math        = "Math";
        public const string MathInt     = "Math/Int";
        public const string MathCompare = "Math/Compare";
        public const string MathBool    = "Math/Bool";
        public const string MathVector  = "Math/Vector";
    }

    /// <summary>
    /// Returns the full set of descriptors for all <c>BlueprintMath</c> methods,
    /// grouped by category.  Ordering is deterministic (declaration order).
    /// </summary>
    public static IEnumerable<NodeKindDescriptor> All()
    {
        // ── Float arithmetic ───────────────────────────────────────────────────
        yield return MakeMath("Math.Add",      "Float + Float",  Categories.Math,    "Add two floats.",                          "Add");
        yield return MakeMath("Math.Subtract", "Float - Float",  Categories.Math,    "Subtract b from a (float).",               "Subtract");
        yield return MakeMath("Math.Multiply", "Float × Float",  Categories.Math,    "Multiply two floats.",                     "Multiply");
        yield return MakeMath("Math.Divide",   "Float ÷ Float",  Categories.Math,    "Divide a by b; returns 0 if b is zero.",   "Divide");
        yield return MakeMath("Math.Modulo",   "Float % Float",  Categories.Math,    "Remainder; returns 0 if b is zero.",       "Modulo");
        yield return MakeMath("Math.Abs",      "Abs (Float)",    Categories.Math,    "Absolute value of a float.",               "Abs");
        yield return MakeMath("Math.Negate",   "Negate (Float)", Categories.Math,    "Negate a float.",                          "Negate");
        yield return MakeMath("Math.Min",      "Min (Float)",    Categories.Math,    "Minimum of two floats.",                   "Min");
        yield return MakeMath("Math.Max",      "Max (Float)",    Categories.Math,    "Maximum of two floats.",                   "Max");
        yield return MakeMath("Math.Clamp",    "Clamp (Float)",  Categories.Math,    "Clamp a float between min and max.",       "Clamp");
        yield return MakeMath("Math.Lerp",     "Lerp (Float)",   Categories.Math,    "Linear interpolation: a + (b-a)*alpha.",   "Lerp");
        yield return MakeMath("Math.Floor",    "Floor",          Categories.Math,    "Floor of a float.",                        "Floor");
        yield return MakeMath("Math.Ceil",     "Ceil",           Categories.Math,    "Ceiling of a float.",                      "Ceil");
        yield return MakeMath("Math.Round",    "Round",          Categories.Math,    "Round to nearest integer.",                "Round");
        yield return MakeMath("Math.Sqrt",     "Sqrt",           Categories.Math,    "Square root; returns 0 for negative.",     "Sqrt");
        yield return MakeMath("Math.Pow",      "Power",          Categories.Math,    "Raise base to the given power.",           "Pow");
        yield return MakeMath("Math.Sin",      "Sin",            Categories.Math,    "Sine of angle (radians).",                 "Sin");
        yield return MakeMath("Math.Cos",      "Cos",            Categories.Math,    "Cosine of angle (radians).",               "Cos");

        // ── Int arithmetic ─────────────────────────────────────────────────────
        yield return MakeMath("Math.AddInt",    "Int + Int",      Categories.MathInt, "Add two integers.",                        "AddInt");
        yield return MakeMath("Math.SubInt",    "Int - Int",      Categories.MathInt, "Subtract b from a (integer).",             "SubInt");
        yield return MakeMath("Math.MulInt",    "Int × Int",      Categories.MathInt, "Multiply two integers.",                   "MulInt");
        yield return MakeMath("Math.DivInt",    "Int ÷ Int",      Categories.MathInt, "Divide a by b (integer); 0 if b is zero.", "DivInt");
        yield return MakeMath("Math.ModInt",    "Int % Int",      Categories.MathInt, "Remainder (integer); 0 if b is zero.",     "ModInt");
        yield return MakeMath("Math.AbsInt",    "Abs (Int)",      Categories.MathInt, "Absolute value of an integer.",            "AbsInt");
        yield return MakeMath("Math.NegateInt", "Negate (Int)",   Categories.MathInt, "Negate an integer.",                       "NegateInt");
        yield return MakeMath("Math.MinInt",    "Min (Int)",      Categories.MathInt, "Minimum of two integers.",                 "MinInt");
        yield return MakeMath("Math.MaxInt",    "Max (Int)",      Categories.MathInt, "Maximum of two integers.",                 "MaxInt");
        yield return MakeMath("Math.ClampInt",  "Clamp (Int)",    Categories.MathInt, "Clamp an integer between min and max.",    "ClampInt");

        // ── Float comparisons → bool ───────────────────────────────────────────
        yield return MakeMath("Math.GreaterThan",    "> (Float)",     Categories.MathCompare, "True if a > b.",              "GreaterThan");
        yield return MakeMath("Math.LessThan",       "< (Float)",     Categories.MathCompare, "True if a < b.",              "LessThan");
        yield return MakeMath("Math.GreaterOrEqual", ">= (Float)",    Categories.MathCompare, "True if a >= b.",             "GreaterOrEqual");
        yield return MakeMath("Math.LessOrEqual",    "<= (Float)",    Categories.MathCompare, "True if a <= b.",             "LessOrEqual");
        yield return MakeMath("Math.ApproxEquals",   "≈ (Float)",     Categories.MathCompare, "True if |a-b| <= epsilon.",   "ApproxEquals");
        yield return MakeMath("Math.EqualsInt",      "= (Int)",       Categories.MathCompare, "True if a == b (integer).",   "EqualsInt");
        yield return MakeMath("Math.GreaterThanInt", "> (Int)",       Categories.MathCompare, "True if a > b (integer).",    "GreaterThanInt");
        yield return MakeMath("Math.LessThanInt",    "< (Int)",       Categories.MathCompare, "True if a < b (integer).",    "LessThanInt");

        // ── Bool logic ─────────────────────────────────────────────────────────
        yield return MakeMath("Math.And", "Boolean AND", Categories.MathBool, "Logical AND.",  "And");
        yield return MakeMath("Math.Or",  "Boolean OR",  Categories.MathBool, "Logical OR.",   "Or");
        yield return MakeMath("Math.Not", "Boolean NOT", Categories.MathBool, "Logical NOT.",  "Not");
        yield return MakeMath("Math.Xor", "Boolean XOR", Categories.MathBool, "Logical XOR.",  "Xor");

        // ── Vector3 ────────────────────────────────────────────────────────────
        yield return MakeMath("Math.AddVec",       "Vec3 + Vec3",    Categories.MathVector, "Add two Vector3 values.",                 "AddVec");
        yield return MakeMath("Math.SubVec",       "Vec3 - Vec3",    Categories.MathVector, "Subtract b from a (Vector3).",            "SubVec");
        yield return MakeMath("Math.MulVecScalar", "Vec3 × Float",   Categories.MathVector, "Multiply a Vector3 by a scalar.",         "MulVecScalar");
        yield return MakeMath("Math.Dot",          "Dot Product",    Categories.MathVector, "Dot product of two Vector3 values.",       "Dot");
        yield return MakeMath("Math.Cross",        "Cross Product",  Categories.MathVector, "Cross product of two Vector3 values.",     "Cross");
        yield return MakeMath("Math.Normalize",    "Normalize",      Categories.MathVector, "Normalize a Vector3.",                     "Normalize");
        yield return MakeMath("Math.Length",       "Vector Length",  Categories.MathVector, "Length (magnitude) of a Vector3.",         "Length");
        yield return MakeMath("Math.Distance",     "Distance",       Categories.MathVector, "Distance between two Vector3 points.",     "Distance");
    }

    /// <summary>
    /// Creates a descriptor whose <c>CreateInstance</c> returns a
    /// <see cref="FunctionCallNode"/> targeting the given <c>BlueprintMath</c> method.
    /// </summary>
    private static NodeKindDescriptor MakeMath(
        string kind, string displayName, string category, string tooltip, string methodName)
        => new()
        {
            Kind        = kind,
            DisplayName = displayName,
            Category    = category,
            Tooltip     = tooltip,
            Icon        = "bp/pure",
            CreateInstance = () => new FunctionCallNode
            {
                Id           = Guid.NewGuid(),
                TargetTypeId = TargetTypeId,
                MethodName   = methodName,
                IsPure       = true,
            },
        };
}
