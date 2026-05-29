using System;
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Utility
{
    // ── Attribute ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Marks a class as a Utility AI decision definition and supplies its catalog metadata.
    /// Apply to a class that also implements <see cref="IUtilityDecisionDefinition"/> and
    /// exposes a <c>public static void Build(IUtilityDecisionBuilder)</c> method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class UtilityDecisionAttribute : Attribute
    {
        /// <summary>A stable GUID-style string used to derive the decision's integer ID via FNV-1a-32.</summary>
        public string AssetId     { get; }
        /// <summary>Human-readable name shown in tooling.</summary>
        public string DisplayName { get; }
        /// <summary>Category used to drive the correct evaluation path in the scorer.</summary>
        public DecisionKind Kind  { get; }
        /// <summary>Optional design-time category tag for the editor.</summary>
        public string Category    { get; }
        /// <summary>Score bonus applied to the currently-active posture before re-ranking (PostureSelect only).</summary>
        public float HysteresisBonus { get; }

        public UtilityDecisionAttribute(string assetId, string displayName,
            DecisionKind kind, string category = "", float hysteresisBonus = 0f)
        {
            AssetId          = assetId;
            DisplayName      = displayName;
            Kind             = kind;
            Category         = category;
            HysteresisBonus  = hysteresisBonus;
        }
    }

    // ── Marker interface ───────────────────────────────────────────────────────────

    /// <summary>
    /// Marker interface; implementations must also carry <see cref="UtilityDecisionAttribute"/>
    /// and expose <c>public static void Build(IUtilityDecisionBuilder)</c>.
    /// </summary>
    public interface IUtilityDecisionDefinition { }

    // ── InputRef ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lightweight descriptor for one Utility AI input: which reader to invoke, which
    /// entity role to bind, and any per-consideration parameters.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct InputRef
    {
        /// <summary>Registered reader ID (FNV-1a-16 of the input name).</summary>
        public readonly ushort       InputId;
        /// <summary>Which entity role this input reads from.</summary>
        public readonly InputContext Context;
        /// <summary>Per-consideration parameters (sensor blueprint ID, max range, mount index, etc.).</summary>
        public readonly InputParams  Params;

        public InputRef(ushort inputId, InputContext context = default, InputParams @params = default)
        {
            InputId = inputId;
            Context = context;
            Params  = @params;
        }
    }

    // ── In factory class ───────────────────────────────────────────────────────────

    /// <summary>
    /// Factory methods for all 17 standard Utility AI inputs.
    /// Each method returns an <see cref="InputRef"/> that encodes which reader to invoke
    /// and which entity role provides the data.
    /// </summary>
    public static partial class In
    {
        // ── Group C: EQS ──────────────────────────────────────────────────────────

        /// <summary>
        /// Top EQS result score for the sensor with blueprint matching FNV-1a-32 of
        /// <paramref name="templateName"/>.
        /// </summary>
        public static InputRef EqsTopScore(string templateName, InputContext ctx = InputContext.Self)
            => new InputRef(StandardInputIds.EqsTopScore, ctx,
                new InputParams { BlueprintId = Fnv1a32(templateName) });

        /// <summary>
        /// Fraction of EQS result slots filled for the sensor with blueprint matching
        /// FNV-1a-32 of <paramref name="templateName"/>.
        /// </summary>
        public static InputRef EqsResultCount(string templateName, InputContext ctx = InputContext.Self)
            => new InputRef(StandardInputIds.EqsResultCount, ctx,
                new InputParams { BlueprintId = Fnv1a32(templateName) });

        // ── Group D: misc ─────────────────────────────────────────────────────────

        /// <summary>Injects a design-time constant via Params.MaxRange.</summary>
        public static InputRef Constant(float value, InputContext ctx = InputContext.Self)
            => new InputRef(StandardInputIds.Constant, ctx, new InputParams { MaxRange = value });

        // ── Hashing helper ────────────────────────────────────────────────────────

        /// <summary>
        /// FNV-1a-32 hash of <paramref name="name"/>.
        /// Used to derive blueprint IDs and decision asset IDs.
        /// </summary>
        public static uint Fnv1a32(string name)
        {
            uint hash = 2166136261u;
            foreach (char c in name)
            {
                hash ^= (byte)c;
                hash *= 16777619u;
            }
            return hash;
        }
    }

    // ── Curve presets ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Named <see cref="ResponseCurve"/> presets for use in decision builder expressions.
    /// </summary>
    public static class Curve
    {
        /// <summary>y = x</summary>
        public static readonly ResponseCurve Linear          = new ResponseCurve(CurveKind.Linear);
        /// <summary>y = 1 - x</summary>
        public static readonly ResponseCurve InverseLinear   = new ResponseCurve(CurveKind.InverseLinear);
        /// <summary>y = x >= XShift ? 1 : 0 (XShift = 0.5)</summary>
        public static readonly ResponseCurve Threshold       = new ResponseCurve(CurveKind.Threshold, xShift: 0.5f);
        /// <summary>Bell curve peaking at input = 1.0; exp(-8*(x-1)^2)</summary>
        public static readonly ResponseCurve Bell            = new ResponseCurve(CurveKind.Bell, slope: 1f, exponent: 8f, xShift: 1.0f);
        /// <summary>y = x >= XShift ? Slope : 0 (XShift = 0.5, Slope = 1)</summary>
        public static readonly ResponseCurve Step            = new ResponseCurve(CurveKind.Step, slope: 1f, xShift: 0.5f);
        /// <summary>Standard S-curve logistic function.</summary>
        public static readonly ResponseCurve Logistic        = new ResponseCurve(CurveKind.Logistic);
        /// <summary>y = x^2</summary>
        public static readonly ResponseCurve Quadratic       = new ResponseCurve(CurveKind.Quadratic);
        /// <summary>y = 1 - x^2</summary>
        public static readonly ResponseCurve InverseQuadratic = new ResponseCurve(CurveKind.InverseQuadratic);

        /// <summary>Returns a linear curve scaled by <paramref name="slope"/>.</summary>
        public static ResponseCurve WithSlope(CurveKind kind, float slope)
            => new ResponseCurve(kind, slope: slope);
    }

    // ── Context aliases ────────────────────────────────────────────────────────────

    /// <summary>
    /// Compile-time aliases for <see cref="InputContext"/> enum values.
    /// </summary>
    public static class Ctx
    {
        public const InputContext Self      = InputContext.Self;
        public const InputContext Target    = InputContext.Target;
        public const InputContext Leader    = InputContext.Leader;
        public const InputContext Candidate = InputContext.Candidate;
    }

    // ── Builder interfaces ─────────────────────────────────────────────────────────

    /// <summary>Fluent builder for one option's consideration list.</summary>
    public interface IUtilityOptionBuilder
    {
        /// <summary>
        /// Appends a consideration (input reader + weight + response curve) to this option.
        /// </summary>
        IUtilityOptionBuilder Consider(InputRef input, float weight, ResponseCurve curve);
    }

    /// <summary>Fluent builder for a <see cref="UtilityDecisionDef"/>.</summary>
    public interface IUtilityDecisionBuilder
    {
        /// <summary>
        /// Adds a named option (e.g. a posture or weapon variant).
        /// <paramref name="optionId"/> must fit in a byte (D-05).
        /// </summary>
        IUtilityDecisionBuilder Option(ushort optionId, ScoringMode mode,
            Action<IUtilityOptionBuilder> configure);

        /// <summary>
        /// Adds the single candidate option used by ThreatRanking and WeaponSelection decisions
        /// (OptionId = 0; called once per candidate during evaluation).
        /// </summary>
        IUtilityDecisionBuilder CandidateOption(ScoringMode mode,
            Action<IUtilityOptionBuilder> configure);
    }

    // ── Concrete builder ───────────────────────────────────────────────────────────

    /// <summary>
    /// Concrete implementation of the fluent decision builder.
    /// Accumulates options/considerations and emits a <see cref="UtilityDecisionDef"/> on
    /// request. Not thread-safe; intended for single-threaded startup registration only.
    /// </summary>
    public sealed class UtilityDecisionBuilder : IUtilityDecisionBuilder, IUtilityOptionBuilder
    {
        private readonly System.Collections.Generic.List<UtilityOption> _options = new();
        private System.Collections.Generic.List<UtilityConsideration>? _current;

        /// <inheritdoc/>
        public IUtilityDecisionBuilder Option(ushort optionId, ScoringMode mode,
            Action<IUtilityOptionBuilder> configure)
        {
            System.Diagnostics.Debug.Assert(optionId <= byte.MaxValue,
                $"OptionId {optionId} exceeds byte.MaxValue; trace records use a byte field.");
            _current = new System.Collections.Generic.List<UtilityConsideration>();
            configure(this);
            _options.Add(new UtilityOption
            {
                OptionId       = optionId,
                Mode           = mode,
                Considerations = _current.ToArray()
            });
            _current = null;
            return this;
        }

        /// <inheritdoc/>
        public IUtilityDecisionBuilder CandidateOption(ScoringMode mode,
            Action<IUtilityOptionBuilder> configure)
            => Option(0, mode, configure);

        /// <inheritdoc/>
        public IUtilityOptionBuilder Consider(InputRef input, float weight, ResponseCurve curve)
        {
            _current!.Add(new UtilityConsideration(
                inputId:  input.InputId,
                context:  input.Context,
                weight:   weight,
                curve:    curve,
                @params:  input.Params));
            return this;
        }

        /// <summary>
        /// Produces a <see cref="UtilityDecisionDef"/> from the accumulated options and the
        /// supplied <paramref name="attr"/> metadata.
        /// </summary>
        public UtilityDecisionDef Build(UtilityDecisionAttribute attr)
        {
            return new UtilityDecisionDef
            {
                BlueprintId = ComputeId(attr.AssetId),
                Kind        = attr.Kind,
                Options     = _options.ToArray(),
                DebugName   = attr.DisplayName
            };
        }

        /// <summary>
        /// Derives the integer decision ID from <paramref name="assetId"/> via FNV-1a-32,
        /// matching <see cref="UtilityDecisionCatalog.ComputeId"/>.
        /// </summary>
        public static int ComputeId(string assetId) => (int)In.Fnv1a32(assetId);
    }
}
