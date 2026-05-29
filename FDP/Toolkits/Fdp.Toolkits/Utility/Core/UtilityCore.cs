using System.Runtime.InteropServices;
using Fdp.Toolkit.Perception;

namespace Fdp.Toolkit.Utility
{
    // ── Curve kind ──────────────────────────────────────────────────────────────

    public enum CurveKind : byte
    {
        Linear,
        InverseLinear,
        Threshold,
        Bell,
        Step,
        Logistic,
        Quadratic,
        InverseQuadratic,
        PiecewiseLinear
    }

    // ── Scoring mode ────────────────────────────────────────────────────────────

    public enum ScoringMode : byte
    {
        /// <summary>Product-with-compensation (Dave Mark §4.3). Default.</summary>
        WeightedProduct,
        /// <summary>Normalised weighted sum (§4.4). Escape hatch for additive scoring.</summary>
        WeightedSum
    }

    // ── Input context ───────────────────────────────────────────────────────────

    public enum InputContext : byte
    {
        Self,
        Target,
        Leader,
        Candidate
    }

    // ── Decision kind ───────────────────────────────────────────────────────────

    public enum DecisionKind : byte
    {
        ThreatRanking,
        WeaponSelection,
        PostureSelect
    }

    // ── ResponseCurve — 16 bytes ─────────────────────────────────────────────────
    // Layout: Kind(1) + Padding0(1) + CurveId(2) + Slope(4) + Exponent(4) + XShift(4) = 16 bytes.
    // YShift (c) is omitted to hit 16 bytes; c=0 is the standard baseline for all Phase-1 curves.

    /// <summary>
    /// Describes a single response curve shape and its parameters.
    /// Immutable 16-byte value type — safe to store in unmanaged arrays.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly partial struct ResponseCurve
    {
        /// <summary>Which curve family to evaluate.</summary>
        public readonly CurveKind Kind;
        /// <summary>Reserved for alignment.</summary>
        public readonly byte      Padding0;
        /// <summary>PiecewiseLinear catalog key; 0 for all other kinds.</summary>
        public readonly short     CurveId;
        /// <summary>Slope / multiplier (m).</summary>
        public readonly float     Slope;
        /// <summary>Exponent / steepness (k).</summary>
        public readonly float     Exponent;
        /// <summary>Horizontal shift (b).</summary>
        public readonly float     XShift;

        public ResponseCurve(CurveKind kind, float slope = 1f, float exponent = 1f, float xShift = 0f, short curveId = 0)
        {
            Kind     = kind;
            Padding0 = 0;
            CurveId  = curveId;
            Slope    = slope;
            Exponent = exponent;
            XShift   = xShift;
        }
    }

    // ── InputParams — 16-byte union ──────────────────────────────────────────────

    /// <summary>
    /// Discriminated union of per-consideration parameters.
    /// 16 bytes reserved for future extension.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct InputParams
    {
        /// <summary>FNV-1a of asset GUID — used by EQS sensor readers.</summary>
        [FieldOffset(0)] public uint  BlueprintId;
        /// <summary>Maximum range in metres — used by DistanceToContext readers.</summary>
        [FieldOffset(0)] public float MaxRange;
        /// <summary>Zero-based weapon mount index — used by per-mount weapon readers.</summary>
        [FieldOffset(0)] public int   MountIndex;
    }

    // ── UtilityConsideration ────────────────────────────────────────────────────

    /// <summary>
    /// One consideration row: an input reader reference, its curve shaping, and its weight.
    /// Fully unmanaged — all fields are value types.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct UtilityConsideration
    {
        /// <summary>FNV-1a-16 of the [UtilityInput] reader attribute name.</summary>
        public readonly ushort        InputId;
        /// <summary>Which entity role supplies the input value.</summary>
        public readonly InputContext  Context;
        /// <summary>Reserved for alignment.</summary>
        public readonly byte          Padding0;
        /// <summary>Relative weight (used as exponent in WeightedProduct; multiplier in WeightedSum).</summary>
        public readonly float         Weight;
        /// <summary>Response curve that maps raw input to [0,1].</summary>
        public readonly ResponseCurve Curve;
        /// <summary>Extra per-consideration parameters (kind-specific).</summary>
        public readonly InputParams   Params;

        public UtilityConsideration(ushort inputId, InputContext context, float weight,
                                    ResponseCurve curve, InputParams @params = default)
        {
            InputId  = inputId;
            Context  = context;
            Padding0 = 0;
            Weight   = weight;
            Curve    = curve;
            Params   = @params;
        }
    }

    // ── UtilityOption ────────────────────────────────────────────────────────────

    /// <summary>
    /// Authored option definition for one choice inside a decision.
    /// Managed class — carries a managed array; not a component; not per-tick hot data.
    /// </summary>
    public sealed class UtilityOption
    {
        public ushort OptionId;
        public ScoringMode Mode;
        public UtilityConsideration[] Considerations = Array.Empty<UtilityConsideration>();
    }

    // ── UtilityDecisionDef ───────────────────────────────────────────────────────

    /// <summary>
    /// Authored definition of a utility-AI decision (e.g. ThreatRanking).
    /// Loaded once at startup; not a per-tick component.
    /// </summary>
    public sealed class UtilityDecisionDef
    {
        /// <summary>FNV-1a of the asset GUID.</summary>
        public int           BlueprintId;
        /// <summary>Hash of structural layout (option count, consideration count per option).</summary>
        public ulong         StructureHash;
        /// <summary>Hash of the authored parameter values (slopes, exponents, etc.).</summary>
        public ulong         ParamHash;
        /// <summary>Human-readable name for logs and profiler markers.</summary>
        public string        DebugName = string.Empty;
        public DecisionKind  Kind;
        public UtilityOption[] Options = Array.Empty<UtilityOption>();
    }

    // ── UtilityConstants ─────────────────────────────────────────────────────────

    /// <summary>
    /// Compile-time constants for the Utility-AI toolkit.
    /// </summary>
    public static class UtilityConstants
    {
        /// <summary>
        /// Maximum number of ranked candidate results returned by UtilityScorer.
        /// Must be >= <see cref="PerceptionConstants.MaxTrackedTargets"/> (cap invariant).
        /// </summary>
        public const int TopN = 16;

        // Cap-invariant assertion: perception's contact list must never exceed the ranking cap.
        static UtilityConstants()
        {
            System.Diagnostics.Debug.Assert(
                PerceptionConstants.MaxTrackedTargets <= TopN,
                $"Perception tracks {PerceptionConstants.MaxTrackedTargets} contacts " +
                $"but Utility ranks only {TopN}. Raise TopN or accept truncation.");
        }
    }
}
