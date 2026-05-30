using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Fdp.Toolkit.Utility;
using Hrot.Editor.AiShared.Emit;
using Hrot.Utility.Editor.Model;

namespace Hrot.Utility.Editor.Emit;

// Deterministic C# emitter for UtilityDecisionAsset.
// Produces a .cs file with the [UtilityDecision] attribute and Build() method.
// Output format matches the starter-pack runtime decisions (file-scoped namespace style).
public sealed class UtilityFluentEmitter : IFluentCSharpEmitter<UtilityDecisionAsset>
{
    private readonly string _targetNamespace;
    private const string Indent = "    ";

    public UtilityFluentEmitter(string targetNamespace = "Fdp.Toolkit.Utility")
    {
        _targetNamespace = targetNamespace;
    }

    public string Emit(UtilityDecisionAsset asset)
    {
        var sb = new StringBuilder();

        // Header (marker + assetId comment, ends with newline)
        sb.AppendLine(FluentCSharpEmitterBase.BuildHeader(asset.AssetId));

        // Usings
        var usings = CollectUsings();
        foreach (var ns in usings)
        {
            if (ns.Length == 0)
                sb.AppendLine();
            else
                sb.AppendLine($"using {ns};");
        }
        sb.AppendLine();

        // Namespace declaration (file-scoped)
        sb.AppendLine($"namespace {_targetNamespace};");
        sb.AppendLine();

        // [UtilityDecision] attribute
        EmitAttribute(sb, asset);

        // Class declaration (no blank line between attribute and class)
        string className = DeriveClassName(asset.DisplayName);
        sb.AppendLine($"public sealed partial class {className} : IUtilityDecisionDefinition");
        sb.AppendLine("{");

        // Build method
        EmitBuildMethod(sb, asset);

        // [UtilityLayout] method placeholder (if non-default layout data present)
        if (HasLayoutData(asset.Layout))
        {
            sb.AppendLine();
            EmitLayoutPlaceholder(sb);
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    // ---- Attribute emission ----

    private static void EmitAttribute(StringBuilder sb, UtilityDecisionAsset asset)
    {
        sb.AppendLine("[UtilityDecision(");
        sb.AppendLine($"    assetId:     \"{asset.AssetId:D}\",");
        sb.AppendLine($"    displayName: \"{asset.DisplayName}\",");
        sb.AppendLine($"    kind:        DecisionKind.{asset.DecisionKind},");
        if (asset.HysteresisBonus != 0f)
        {
            sb.AppendLine($"    category:    \"{asset.Category}\",");
            sb.AppendLine($"    hysteresisBonus: {FloatLiteral(asset.HysteresisBonus)})]");
        }
        else
        {
            sb.AppendLine($"    category:    \"{asset.Category}\")]");
        }
    }

    // ---- Build method emission ----

    private static void EmitBuildMethod(StringBuilder sb, UtilityDecisionAsset asset)
    {
        sb.AppendLine($"{Indent}/// <summary>Builds the decision definition via the fluent builder.</summary>");
        sb.AppendLine($"{Indent}public static void Build(IUtilityDecisionBuilder b) => b");

        bool isCandidateDecision =
            asset.DecisionKind == DecisionKind.ThreatRanking ||
            asset.DecisionKind == DecisionKind.WeaponSelection;

        var sortedOptions = asset.Options
            .OrderBy(o => o.VisualId, StringComparer.Ordinal)
            .ToList();

        if (sortedOptions.Count == 0)
        {
            // No options; emit a terminator so the method body compiles.
            sb.AppendLine($"{Indent}{Indent};");
            return;
        }

        for (int oi = 0; oi < sortedOptions.Count; oi++)
        {
            var opt = sortedOptions[oi];
            bool isLastOption = oi == sortedOptions.Count - 1;

            // Option opening line
            if (isCandidateDecision)
                sb.AppendLine($"{Indent}{Indent}.CandidateOption({FormatMode(opt.Mode)}, o => o");
            else
                sb.AppendLine($"{Indent}{Indent}.Option({opt.OptionId}, {FormatMode(opt.Mode)}, o => o");

            var sortedCons = opt.Considerations
                .OrderBy(c => c.VisualId, StringComparer.Ordinal)
                .ToList();

            if (sortedCons.Count == 0)
            {
                // Empty option - close lambda and option call
                string empty = isLastOption ? ");" : ")";
                sb.AppendLine($"{Indent}{Indent}{empty}");
                continue;
            }

            for (int ci = 0; ci < sortedCons.Count; ci++)
            {
                var con = sortedCons[ci];
                bool isLastCon = ci == sortedCons.Count - 1;

                string inCall = $"In.{con.InputName}({BuildInCallArgs(con)})";
                string curve  = CurveExpression(con.Curve);
                string weight = FloatLiteral(con.Weight);

                // Determine closing suffix:
                // Last consideration of last option: close Consider, lambda, option, statement
                // Last consideration of non-last option: close Consider, lambda, option
                // Non-last consideration: close only Consider
                string suffix;
                if (isLastCon && isLastOption)
                    suffix = "));";
                else if (isLastCon)
                    suffix = "))";
                else
                    suffix = ")";

                sb.AppendLine($"{Indent}{Indent}{Indent}.Consider({inCall}, {weight}, {curve}{suffix}");
            }
        }
    }

    // ---- Layout placeholder ----

    private static void EmitLayoutPlaceholder(StringBuilder sb)
    {
        sb.AppendLine($"{Indent}[UtilityLayout]");
        sb.AppendLine($"{Indent}public static void Layout(IUtilityLayoutBuilder b)");
        sb.AppendLine($"{Indent}{{");
        sb.AppendLine($"{Indent}{Indent}// layout data - full wiring deferred to BATCH-16");
        sb.AppendLine($"{Indent}}}");
    }

    // ---- Usings ----

    private static IReadOnlyList<string> CollectUsings()
    {
        var set = new HashSet<string>
        {
            "Fdp.Toolkit.Utility",
        };
        return FluentCSharpEmitterBase.SortUsings(set);
    }

    // ---- Helpers ----

    private static string FormatMode(ScoringMode mode) =>
        $"ScoringMode.{mode}";

    private static string BuildInCallArgs(ConsiderationModel con)
    {
        var args = new List<string>();
        if (!string.IsNullOrEmpty(con.Params.TemplateName))
            args.Add($"\"{con.Params.TemplateName}\"");
        else if (con.Params.MaxRange != 0f)
            args.Add(FloatLiteral(con.Params.MaxRange));
        else if (con.Params.MountIndex != 0)
            args.Add(con.Params.MountIndex.ToString(CultureInfo.InvariantCulture));
        if (con.Context != InputContext.Self)
            args.Add($"InputContext.{con.Context}");
        return string.Join(", ", args);
    }

    // Returns the Curve.* preset name if the model matches, else a new ResponseCurve(...) expression.
    private static string CurveExpression(ResponseCurveModel curve)
    {
        return (curve.Kind, curve.M, curve.K, curve.B) switch
        {
            (CurveKind.Linear,           1f, 1f, 0f)   => "Curve.Linear",
            (CurveKind.InverseLinear,    1f, 1f, 0f)   => "Curve.InverseLinear",
            (CurveKind.Threshold,        1f, 1f, 0.5f) => "Curve.Threshold",
            (CurveKind.Bell,             1f, 8f, 1.0f) => "Curve.Bell",
            (CurveKind.Step,             1f, 1f, 0.5f) => "Curve.Step",
            (CurveKind.Logistic,         1f, 1f, 0f)   => "Curve.Logistic",
            (CurveKind.Quadratic,        1f, 1f, 0f)   => "Curve.Quadratic",
            (CurveKind.InverseQuadratic, 1f, 1f, 0f)   => "Curve.InverseQuadratic",
            _ when curve.Kind == CurveKind.PiecewiseLinear =>
                "new ResponseCurve(CurveKind.PiecewiseLinear)",
            _ => $"new ResponseCurve(CurveKind.{curve.Kind}, slope: {FloatLiteral(curve.M)}, exponent: {FloatLiteral(curve.K)}, xShift: {FloatLiteral(curve.B)})"
        };
    }

    private static string FloatLiteral(float f) =>
        f.ToString("R", CultureInfo.InvariantCulture) + "f";

    private static bool HasLayoutData(UtilityLayoutData layout) =>
        layout.OptionOrder.Count > 0 ||
        layout.Collapsed.Count > 0   ||
        !string.IsNullOrEmpty(layout.PinnedFixture);

    // Derives the class name from DisplayName: strips non-identifier chars, appends "Decision".
    private static string DeriveClassName(string displayName)
    {
        if (string.IsNullOrEmpty(displayName))
            return "UnnamedDecision";
        var sb = new StringBuilder();
        foreach (char c in displayName)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
        }
        if (sb.Length == 0) return "UnnamedDecision";
        if (char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString() + "Decision";
    }
}
