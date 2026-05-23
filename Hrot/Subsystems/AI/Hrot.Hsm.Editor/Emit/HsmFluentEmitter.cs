using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Hrot.Editor.AiShared.Emit;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Emit;

/// <summary>
/// Deterministic C# emitter for HSM assets.
/// Produces a .cs file with three static methods:
///   CreateBuilder() - fluent HSM definition via HsmBuilder
///   Compile()       - [HsmDefinition] thunk calling CreateBuilder().Build().Compile()
///   Layout()        - [HsmLayout] canvas positions snapshot
/// </summary>
public sealed class HsmFluentEmitter : IFluentCSharpEmitter<HsmAsset>
{
    private const string LayoutNamespace = "Hrot.Editor.AiShared.Layout";
    private const string Indent          = "    ";

    public string Emit(HsmAsset asset)
    {
        var sb = new StringBuilder();
        var usings = CollectUsings(asset);

        // Header
        sb.AppendLine(FluentCSharpEmitterBase.BuildHeader(asset.AssetId));

        // Usings
        foreach (var ns in usings)
        {
            if (ns.Length == 0)
                sb.AppendLine(); // blank line between system and non-system groups
            else
                sb.AppendLine($"using {ns};");
        }

        sb.AppendLine();

        string targetNs = string.IsNullOrEmpty(asset.TargetNamespace)
            ? "Hrot.AI.Behaviors.Machines"
            : asset.TargetNamespace;
        string className = SanitizeIdentifier(asset.Name);

        sb.AppendLine($"namespace {targetNs};");
        sb.AppendLine();
        sb.AppendLine($"public static class {className}");
        sb.AppendLine("{");

        EmitCreateBuilder(sb, asset);
        sb.AppendLine();
        EmitCompile(sb, asset);
        sb.AppendLine();
        EmitLayout(sb, asset);

        sb.AppendLine("}");

        return sb.ToString();
    }

    // ---- Using collection ----

    private static IReadOnlyList<string> CollectUsings(HsmAsset asset)
    {
        var set = new HashSet<string>
        {
            "System",
            "System.Numerics",
            "Fhsm.Compiler",
            "Fhsm.Kernel.Attributes",
            "Fhsm.Kernel.Data",
            LayoutNamespace,
        };

        // Collect namespaces from action/guard fully-qualified names.
        foreach (var s in asset.AllStates)
        {
            AddNsFromFqn(set, s.OnEntryAction);
            AddNsFromFqn(set, s.OnExitAction);
            AddNsFromFqn(set, s.ActivityAction);
            AddNsFromFqn(set, s.TimerAction);
        }
        foreach (var t in asset.AllTransitions)
        {
            AddNsFromFqn(set, t.GuardFunction);
            AddNsFromFqn(set, t.ActionFunction);
        }
        foreach (var gt in asset.AllGlobalTransitions)
        {
            AddNsFromFqn(set, gt.GuardFunction);
            AddNsFromFqn(set, gt.ActionFunction);
        }

        return FluentCSharpEmitterBase.SortUsings(set);
    }

    // "A.B.C.Method" -> add "A.B"  (strip class and method components)
    private static void AddNsFromFqn(HashSet<string> set, string? fqn)
    {
        if (string.IsNullOrEmpty(fqn)) return;
        int last   = fqn.LastIndexOf('.');
        if (last <= 0) return;
        int second = fqn.LastIndexOf('.', last - 1);
        if (second > 0)
            set.Add(fqn[..second]);
        else
            set.Add(fqn[..last]);
    }

    // ---- CreateBuilder ----

    private static void EmitCreateBuilder(StringBuilder sb, HsmAsset asset)
    {
        sb.AppendLine($"{Indent}public static HsmBuilder CreateBuilder()");
        sb.AppendLine($"{Indent}{{");

        string pad = Indent + Indent;

        sb.AppendLine($"{pad}var builder = new HsmBuilder({QuoteStr(asset.Name)});");

        // Events sorted by EventId
        if (asset.AllEvents.Count > 0)
        {
            sb.AppendLine();
            foreach (var ev in asset.AllEvents.OrderBy(e => e.EventId))
            {
                sb.AppendLine(
                    $"{pad}builder.Event({QuoteStr(ev.Name)}, {ev.EventId}, {ev.PayloadSize}," +
                    $" {BoolStr(ev.IsIndirect)}, {BoolStr(ev.IsDeferrable)});");
            }
        }

        // RegisterAction calls (alphabetical set)
        var allActions = CollectActions(asset);
        if (allActions.Count > 0)
        {
            sb.AppendLine();
            foreach (var a in allActions)
                sb.AppendLine($"{pad}builder.RegisterAction({QuoteStr(a)});");
        }

        // RegisterGuard calls (alphabetical set)
        var allGuards = CollectGuards(asset);
        if (allGuards.Count > 0)
        {
            sb.AppendLine();
            foreach (var g in allGuards)
                sb.AppendLine($"{pad}builder.RegisterGuard({QuoteStr(g)});");
        }

        // asset.RootState is the projector's synthetic root; its single child is the
        // compiler graph root (__Root, FlatIndex 0). User top-level states are the
        // children of that compiler root.
        var compilerRoot = asset.RootState.Children.Count > 0
            ? asset.RootState.Children[0] : null;
        var userTopLevel = compilerRoot?.Children;
        if (userTopLevel != null && userTopLevel.Count > 0)
        {
            sb.AppendLine();
            foreach (var topState in userTopLevel)
                EmitTopLevelState(sb, topState, pad);
        }

        // Global transitions sorted by EventId
        if (asset.AllGlobalTransitions.Count > 0)
        {
            sb.AppendLine();
            foreach (var gt in asset.AllGlobalTransitions.OrderBy(g => g.EventId))
            {
                string evRef     = gt.EventName ?? gt.EventId.ToString(CultureInfo.InvariantCulture);
                string targetName = gt.Target?.Name ?? "???";
                sb.AppendLine(
                    $"{pad}builder.GlobalTransition({QuoteStr(evRef)}, {QuoteStr(targetName)}," +
                    $" visualId: new Guid({QuoteStr(gt.VisualId.ToString("D"))}));");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"{pad}return builder;");
        sb.AppendLine($"{Indent}}}");
    }

    private static void EmitTopLevelState(StringBuilder sb, StateNode state, string pad)
    {
        bool needsVar = state.Children.Count > 0 || state.OutgoingTransitions.Count > 0;
        string varName = MakeVarName(state.Name);

        string decl = $"builder.State({QuoteStr(state.Name)}, stableId: new Guid({QuoteStr(state.StableId.ToString("D"))}))";
        var config   = BuildStateConfig(state);

        if (needsVar)
            sb.Append($"{pad}var {varName} = {decl}");
        else
            sb.Append($"{pad}{decl}");

        EmitConfigChain(sb, config, pad + "    ");
        sb.AppendLine(";");

        foreach (var child in state.Children)
            EmitChildCall(sb, child, varName, pad, depth: 2);

        foreach (var t in state.OutgoingTransitions)
            EmitTransitionCall(sb, varName, t, pad);
    }

    private static void EmitChildCall(StringBuilder sb, StateNode child, string parentVar, string pad, int depth)
    {
        string stableGuid  = QuoteStr(child.StableId.ToString("D"));
        string lambdaParam = $"sb{depth}";
        string innerPad    = pad + "    ";
        var config = BuildStateConfig(child);
        bool hasBody = config.Count > 0 || child.Children.Count > 0 || child.OutgoingTransitions.Count > 0;

        if (!hasBody)
        {
            sb.AppendLine($"{pad}{parentVar}.Child({QuoteStr(child.Name)}, {lambdaParam} => {{ }}, stableId: new Guid({stableGuid}));");
        }
        else
        {
            sb.AppendLine($"{pad}{parentVar}.Child({QuoteStr(child.Name)}, {lambdaParam} =>");
            sb.AppendLine($"{pad}{{");

            if (config.Count > 0)
            {
                sb.Append($"{innerPad}{lambdaParam}");
                foreach (var c in config)
                    sb.Append(c);
                sb.AppendLine(";");
            }

            foreach (var grandchild in child.Children)
                EmitChildCall(sb, grandchild, lambdaParam, innerPad, depth + 1);

            foreach (var t in child.OutgoingTransitions)
                EmitTransitionCall(sb, lambdaParam, t, innerPad);

            sb.AppendLine($"{pad}}}, stableId: new Guid({stableGuid}));");
        }
    }

    private static void EmitTransitionCall(StringBuilder sb, string stateVar, TransitionNode t, string pad)
    {
        string onCall = t.EventName != null
            ? $"{stateVar}.On({QuoteStr(t.EventName)})"
            : $"{stateVar}.On({t.EventId})";

        string targetName = t.Target?.Name ?? "???";
        string chain =
            $".GoTo({QuoteStr(targetName)}, visualId: new Guid({QuoteStr(t.VisualId.ToString("D"))}))";

        if (!string.IsNullOrEmpty(t.GuardFunction))
            chain += $".Guard({QuoteStr(t.GuardFunction)})";
        if (!string.IsNullOrEmpty(t.ActionFunction))
            chain += $".Action({QuoteStr(t.ActionFunction)})";
        if (t.Priority != 0)
            chain += $".Priority({t.Priority})";

        sb.AppendLine($"{pad}{onCall}{chain};");
    }

    private static void EmitConfigChain(StringBuilder sb, List<string> config, string continuationPad)
    {
        foreach (var c in config)
        {
            sb.AppendLine();
            sb.Append($"{continuationPad}{c}");
        }
    }

    private static List<string> BuildStateConfig(StateNode s)
    {
        var parts = new List<string>();
        if (s.IsInitial)     parts.Add(".Initial()");
        if (s.IsHistory)     parts.Add(".History()");
        if (s.IsDeepHistory) parts.Add(".DeepHistory()");
        if (s.IsParallel)    parts.Add(".Parallel()");
        if (s.IsFinal)       parts.Add(".Final()");
        if (s.OnEntryAction  != null) parts.Add($".OnEntry({QuoteStr(s.OnEntryAction)})");
        if (s.OnExitAction   != null) parts.Add($".OnExit({QuoteStr(s.OnExitAction)})");
        if (s.ActivityAction != null) parts.Add($".Activity({QuoteStr(s.ActivityAction)})");
        if (s.TimerAction    != null) parts.Add($".TimerAction({QuoteStr(s.TimerAction)})");
        return parts;
    }

    private static List<string> CollectActions(HsmAsset asset)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var s in asset.AllStates)
        {
            if (s.OnEntryAction  != null) set.Add(s.OnEntryAction);
            if (s.OnExitAction   != null) set.Add(s.OnExitAction);
            if (s.ActivityAction != null) set.Add(s.ActivityAction);
            if (s.TimerAction    != null) set.Add(s.TimerAction);
        }
        foreach (var t in asset.AllTransitions)
            if (t.ActionFunction != null) set.Add(t.ActionFunction);
        foreach (var gt in asset.AllGlobalTransitions)
            if (gt.ActionFunction != null) set.Add(gt.ActionFunction);
        return new List<string>(set);
    }

    private static List<string> CollectGuards(HsmAsset asset)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var t in asset.AllTransitions)
            if (t.GuardFunction != null) set.Add(t.GuardFunction);
        foreach (var gt in asset.AllGlobalTransitions)
            if (gt.GuardFunction != null) set.Add(gt.GuardFunction);
        return new List<string>(set);
    }

    // ---- Compile ----

    private static void EmitCompile(StringBuilder sb, HsmAsset asset)
    {
        sb.AppendLine($"{Indent}[HsmDefinition({QuoteStr(asset.Name)}, AssetId = {QuoteStr(asset.AssetId.ToString("D"))})]");
        sb.AppendLine($"{Indent}public static HsmDefinitionBlob Compile() => CreateBuilder().Build().Compile();");
    }

    // ---- Layout ----

    private static void EmitLayout(StringBuilder sb, HsmAsset asset)
    {
        sb.AppendLine($"{Indent}[HsmLayout({QuoteStr(asset.AssetId.ToString("D"))})]");
        sb.AppendLine($"{Indent}public static HsmEditorLayout Layout() => new HsmEditorLayoutBuilder()");
        sb.AppendLine(
            $"{Indent}{Indent}.Canvas(new Vector2({FormatFloat(asset.CanvasPanOffset.X)}," +
            $" {FormatFloat(asset.CanvasPanOffset.Y)}), {FormatFloat(asset.CanvasZoomLevel)})");

        // States sorted by StableId (D-format) lexicographic
        foreach (var s in asset.AllStates.OrderBy(x => x.StableId.ToString("D"), StringComparer.Ordinal))
        {
            string guidStr = QuoteStr(s.StableId.ToString("D"));
            string pos     = $"new Vector2({FormatFloat(s.Position.X)}, {FormatFloat(s.Position.Y)})";

            sb.Append($"{Indent}{Indent}.State({guidStr}, {pos}");
            if (s.Size.HasValue)
                sb.Append($", sizeOverride: new Vector2({FormatFloat(s.Size.Value.X)}, {FormatFloat(s.Size.Value.Y)})");
            if (!string.IsNullOrEmpty(s.Comment))
                sb.Append($", comment: {QuoteStr(s.Comment)}");
            if (s.IsCollapsed)
                sb.Append(", collapsed: true");
            if (!string.IsNullOrEmpty(s.ColorOverride))
                sb.Append($", color: {QuoteStr(s.ColorOverride)}");
            sb.AppendLine(")");
        }

        // Transitions sorted by VisualId (D-format) lexicographic
        foreach (var t in asset.AllTransitions.OrderBy(x => x.VisualId.ToString("D"), StringComparer.Ordinal))
        {
            string guidStr = QuoteStr(t.VisualId.ToString("D"));
            string waypoints;
            if (t.Waypoints.Count == 0)
            {
                waypoints = "Array.Empty<System.Numerics.Vector2>()";
            }
            else
            {
                var pts = string.Join(", ",
                    t.Waypoints.Select(wp => $"new Vector2({FormatFloat(wp.X)}, {FormatFloat(wp.Y)})"));
                waypoints = $"new Vector2[] {{ {pts} }}";
            }

            sb.Append($"{Indent}{Indent}.Transition({guidStr}, {waypoints}");
            if (!string.IsNullOrEmpty(t.Comment))
                sb.Append($", comment: {QuoteStr(t.Comment)}");
            sb.AppendLine(")");
        }

        // Regions sorted by StableId (D-format) lexicographic
        // RegionNode has no Position; use zero as placeholder.
        foreach (var r in asset.AllRegions.OrderBy(x => x.StableId.ToString("D"), StringComparer.Ordinal))
        {
            string guidStr = QuoteStr(r.StableId.ToString("D"));
            const string zeroPos = "new Vector2(0f, 0f)";

            sb.Append($"{Indent}{Indent}.Region({guidStr}, {r.RegionIndex}, {zeroPos}");
            if (!string.IsNullOrEmpty(r.Comment))
                sb.Append($", comment: {QuoteStr(r.Comment)}");
            sb.AppendLine(")");
        }

        sb.AppendLine($"{Indent}{Indent}.Build();");
    }

    // ---- Helpers ----

    private static string QuoteStr(string s) => $"\"{s}\"";
    private static string BoolStr(bool b) => b ? "true" : "false";

    private static string FormatFloat(float f) =>
        f.ToString("R", CultureInfo.InvariantCulture) + "f";

    // Converts a state name to a camelCase local variable identifier.
    private static string MakeVarName(string stateName)
    {
        var sb   = new StringBuilder(stateName.Length);
        bool cap = false;
        foreach (char c in stateName)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                cap = true;
                continue;
            }
            if (sb.Length == 0)
                sb.Append(char.ToLowerInvariant(c));
            else if (cap)
                sb.Append(char.ToUpperInvariant(c));
            else
                sb.Append(c);
            cap = false;
        }
        string name = sb.ToString();
        if (name.Length == 0 || char.IsDigit(name[0]))
            name = "_" + name;
        return name;
    }

    // Replaces non-alphanumeric/underscore chars with '_', prefixes '_' if starts with digit.
    private static string SanitizeIdentifier(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
            else
                sb.Append('_');
        }
        string result = sb.ToString();
        if (result.Length == 0 || char.IsDigit(result[0]))
            result = "_" + result;
        return result;
    }
}
