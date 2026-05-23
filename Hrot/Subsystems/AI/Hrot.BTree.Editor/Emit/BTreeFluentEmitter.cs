using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Fbt;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Emit;

namespace Hrot.BTree.Editor.Emit;

/// <summary>
/// Deterministic C# emitter for BTree assets.
/// Produces a .cs file with three methods:
///   CreateBuilder() — the fluent tree definition
///   Build()         — [BTreeDefinition] thunk
///   Layout()        — [BTreeLayout] canvas positions
///
/// BTH §4.
/// </summary>
public sealed class BTreeFluentEmitter : IFluentCSharpEmitter<BehaviorTreeAsset>
{
    // The layout-related namespace is a game-side type.
    private const string LayoutNamespace = "Hrot.AI.Behaviors.Trees.Layout";
    private const string FbtNamespace    = "Fbt";
    private const string Indent          = "    ";

    public string Emit(BehaviorTreeAsset asset)
    {
        var sb = new StringBuilder();
        var usings = CollectUsings(asset);

        // Header
        sb.AppendLine(FluentCSharpEmitterBase.BuildHeader(asset.AssetId));

        // Usings
        foreach (var ns in usings)
        {
            if (ns.Length == 0)
                sb.AppendLine(); // blank line separator between system and non-system
            else
                sb.AppendLine($"using {ns};");
        }

        sb.AppendLine();

        // Namespace + class
        var ns2 = string.IsNullOrEmpty(asset.TargetNamespace) ? "Hrot.AI.Behaviors.Trees" : asset.TargetNamespace;
        var className = SanitizeIdentifier(asset.Name);

        sb.AppendLine($"namespace {ns2};");
        sb.AppendLine();
        sb.AppendLine($"public static class {className}");
        sb.AppendLine("{");

        // Method 1: CreateBuilder
        EmitCreateBuilder(sb, asset);

        sb.AppendLine();

        // Method 2: Build
        EmitBuild(sb, asset);

        sb.AppendLine();

        // Method 3: Layout
        EmitLayout(sb, asset);

        sb.AppendLine("}");

        return sb.ToString();
    }

    // ---- Using collection ----

    private static IReadOnlyList<string> CollectUsings(BehaviorTreeAsset asset)
    {
        var set = new HashSet<string>
        {
            "System",
            "System.Numerics",
            FbtNamespace,
            "Fbt.Compiler",
            LayoutNamespace,
        };

        // Add namespaces from blackboard / context type names.
        AddNamespaceFromTypeName(set, asset.BlackboardTypeName);
        AddNamespaceFromTypeName(set, asset.ContextTypeName);

        // Scan nodes for action/condition FQNs.
        foreach (var node in asset.Nodes)
        {
            if (node.Action != null)
                AddNamespaceFromFqn(set, node.Action.MethodFqn);
            if (node.Condition != null)
                AddNamespaceFromFqn(set, node.Condition.MethodFqn);
        }

        return FluentCSharpEmitterBase.SortUsings(set);
    }

    private static void AddNamespaceFromTypeName(HashSet<string> set, string typeName)
    {
        // "Hrot.Game.Combat.CombatBlackboard" -> "Hrot.Game.Combat"
        int last = typeName.LastIndexOf('.');
        if (last > 0) set.Add(typeName[..last]);
    }

    private static void AddNamespaceFromFqn(HashSet<string> set, string fqn)
    {
        // "Hrot.Game.Combat.CombatActions.AimAndFire" -> "Hrot.Game.Combat"
        // Strip both the class and method components.
        int last = fqn.LastIndexOf('.');
        if (last <= 0) return;
        int second = fqn.LastIndexOf('.', last - 1);
        if (second > 0) set.Add(fqn[..second]);
        else set.Add(fqn[..last]);
    }

    // ---- CreateBuilder ----

    private static void EmitCreateBuilder(StringBuilder sb, BehaviorTreeAsset asset)
    {
        var bbShort  = ShortTypeName(asset.BlackboardTypeName);
        var ctxShort = ShortTypeName(asset.ContextTypeName);

        sb.AppendLine($"{Indent}public static BTreeBuilder<{bbShort}, {ctxShort}> CreateBuilder() =>");
        sb.AppendLine($"{Indent}{Indent}new BTreeBuilder<{bbShort}, {ctxShort}>()");

        // Find root node.
        var root = asset.Nodes.FirstOrDefault(n => n.KernelType == NodeType.Root);
        if (root != null && root.ChildVisualIds.Count > 0)
        {
            // The root's single child is the entry-point composite.
            var entryChild = asset.FindNode(root.ChildVisualIds[0]);
            if (entryChild != null)
                EmitNode(sb, asset, entryChild, depth: 3, isLast: true);
            else
                sb.AppendLine($"{Indent}{Indent};");
        }
        else
        {
            sb.AppendLine($"{Indent}{Indent};");
        }
    }

    private static void EmitNode(
        StringBuilder sb, BehaviorTreeAsset asset,
        BTreeEditorNode node, int depth, bool isLast)
    {
        string pad = string.Concat(Enumerable.Repeat(Indent, depth));

        // Get pills sorted innermost-to-outermost (index 0 = closest to node = innermost).
        var pills = asset.Pills
            .Where(p => p.HostNodeVisualId == node.VisualId)
            .OrderBy(p => p.StackIndex)
            .ToList();

        // Emit pills outermost-first (= highest StackIndex first).
        pills.Reverse();

        // We build the node content recursively.
        string nodeText = BuildNodeContent(sb, asset, node, depth, pills, isLast);
        // (nodeText is built inline — EmitNode writes directly to sb)
    }

    private static string BuildNodeContent(
        StringBuilder sb, BehaviorTreeAsset asset,
        BTreeEditorNode node, int depth, List<BTreeEditorPill> pills, bool isLast)
    {
        string pad = string.Concat(Enumerable.Repeat(Indent, depth));

        bool isComposite = !node.IsLeaf && !node.IsDecorator;
        bool hasChildren  = node.ChildVisualIds.Count > 0;

        // Pill open wrappers
        foreach (var pill in pills)
        {
            string pillOpen = BuildPillOpen(pill, pad, depth);
            sb.Append(pillOpen);
            depth++;
            pad = string.Concat(Enumerable.Repeat(Indent, depth));
        }

        switch (node.KernelType)
        {
            case NodeType.Sequence:
                EmitComposite(sb, asset, node, "Sequence", "seq", pad, depth, isLast);
                break;
            case NodeType.Selector:
                EmitComposite(sb, asset, node, "Selector", "sel", pad, depth, isLast);
                break;
            case NodeType.Parallel:
                EmitComposite(sb, asset, node, "Parallel", "par", pad, depth, isLast);
                break;
            case NodeType.ObserverSelector:
                EmitComposite(sb, asset, node, "ObserverSelector", "obs", pad, depth, isLast);
                break;
            case NodeType.Action:
                EmitAction(sb, node, pad, isLast);
                break;
            case NodeType.Condition:
                EmitCondition(sb, node, pad, isLast);
                break;
            case NodeType.Wait:
                EmitWait(sb, node, pad, isLast);
                break;
            case NodeType.Subtree:
                EmitSubtree(sb, node, pad, isLast);
                break;
            default:
                sb.AppendLine($"{pad}// Unsupported node type: {node.KernelType}");
                break;
        }

        return string.Empty; // content written inline
    }

    private static string BuildPillOpen(BTreeEditorPill pill, string pad, int depth)
    {
        // Pills are emitted as opening calls; they are closed on the lines that follow.
        // This is a prefix; children follow on subsequent lines.
        // e.g. ".Inverter(visualId: new Guid(\"...\"),"
        // We build just the opening fragment here.
        return string.Empty; // Currently unused; pills handled inline in composites.
    }

    private static void EmitComposite(
        StringBuilder sb, BehaviorTreeAsset asset,
        BTreeEditorNode node, string methodName, string lambdaArg,
        string pad, int depth, bool isLast)
    {
        // Build pill prefix/suffix lines.
        var pills = asset.Pills
            .Where(p => p.HostNodeVisualId == node.VisualId)
            .OrderByDescending(p => p.StackIndex)   // outermost first
            .ToList();

        // Opening lines for each pill (from outermost to innermost).
        string innerPad = pad;
        var pillDepth = depth;
        foreach (var pill in pills)
        {
            string pillCall = BuildDecoratorOpen(pill, innerPad);
            sb.Append(pillCall);
            pillDepth++;
            innerPad = string.Concat(Enumerable.Repeat(Indent, pillDepth));
        }

        string visualIdArg = $"visualId: new Guid(\"{node.VisualId:D}\")";

        int childCount = node.ChildVisualIds.Count;
        if (childCount == 0)
        {
            // Empty composite.
            sb.AppendLine($"{innerPad}.{methodName}({lambdaArg} => {{ }},");
            sb.AppendLine($"{innerPad}{Indent}{visualIdArg}){(isLast ? ";" : ",")}");
        }
        else
        {
            sb.AppendLine($"{innerPad}.{methodName}({lambdaArg} => {lambdaArg}");
            for (int i = 0; i < childCount; i++)
            {
                var childId = node.ChildVisualIds[i];
                var child = asset.FindNode(childId);
                if (child != null)
                {
                    bool lastChild = i == childCount - 1;
                    EmitChildNode(sb, asset, child, pillDepth + 1, lastChild);
                }
            }
            bool hasPills = pills.Count > 0;
            string closeSuffix = (hasPills || !isLast) ? "," : ";";
            sb.AppendLine($"{innerPad}{Indent},");
            sb.AppendLine($"{innerPad}{Indent}{visualIdArg}){closeSuffix}");
        }

        // Close pill wrappers (innermost first when closing).
        var closingPills = pills.AsEnumerable().Reverse().ToList();
        foreach (var pill in closingPills)
        {
            string pillClose = BuildDecoratorClose(pill, innerPad, isLast && pill == closingPills.Last());
            sb.Append(pillClose);
        }
    }

    private static void EmitChildNode(
        StringBuilder sb, BehaviorTreeAsset asset,
        BTreeEditorNode node, int depth, bool isLast)
    {
        string pad = string.Concat(Enumerable.Repeat(Indent, depth));

        switch (node.KernelType)
        {
            case NodeType.Sequence:
                EmitComposite(sb, asset, node, "Sequence", "seq", pad, depth, isLast);
                break;
            case NodeType.Selector:
                EmitComposite(sb, asset, node, "Selector", "sel", pad, depth, isLast);
                break;
            case NodeType.Parallel:
                EmitComposite(sb, asset, node, "Parallel", "par", pad, depth, isLast);
                break;
            case NodeType.ObserverSelector:
                EmitComposite(sb, asset, node, "ObserverSelector", "obs", pad, depth, isLast);
                break;
            case NodeType.Action:
                EmitAction(sb, node, pad, isLast);
                break;
            case NodeType.Condition:
                EmitCondition(sb, node, pad, isLast);
                break;
            case NodeType.Wait:
                EmitWait(sb, node, pad, isLast);
                break;
            case NodeType.Subtree:
                EmitSubtree(sb, node, pad, isLast);
                break;
            default:
                sb.AppendLine($"{pad}// Unknown node type: {node.KernelType}");
                break;
        }
    }

    private static string BuildDecoratorOpen(BTreeEditorPill pill, string pad)
    {
        // Decorator wraps its child: the open is the start of the fluent call.
        return string.Empty; // Simplified for Slice 1; decorators rendered inline with host.
    }

    private static string BuildDecoratorClose(BTreeEditorPill pill, string pad, bool isLast)
    {
        return string.Empty;
    }

    private static void EmitAction(StringBuilder sb, BTreeEditorNode node, string pad, bool isLast)
    {
        var p = node.Action;
        var visualId = $"visualId: new Guid(\"{node.VisualId:D}\")";
        string term = isLast ? ";" : ",";

        if (p == null)
        {
            sb.AppendLine($"{pad}.Action({visualId}){term}");
            return;
        }

        string methodRef = ShortMethodRef(p.MethodFqn);
        if (p.DelegateShape == BTreeActionDelegateShape.ThreeParamReusable &&
            !string.IsNullOrEmpty(p.ExpressionTargetField))
        {
            sb.AppendLine($"{pad}.Action(dto => dto.{p.ExpressionTargetField}, {methodRef},");
            sb.AppendLine($"{pad}{Indent}{visualId}){term}");
        }
        else
        {
            sb.AppendLine($"{pad}.Action({methodRef},");
            sb.AppendLine($"{pad}{Indent}{visualId}){term}");
        }
    }

    private static void EmitCondition(StringBuilder sb, BTreeEditorNode node, string pad, bool isLast)
    {
        var p = node.Condition;
        var visualId = $"visualId: new Guid(\"{node.VisualId:D}\")";
        string term = isLast ? ";" : ",";

        if (p == null)
        {
            sb.AppendLine($"{pad}.Condition({visualId}){term}");
            return;
        }

        string methodRef = ShortMethodRef(p.MethodFqn);
        if (p.DelegateShape == BTreeActionDelegateShape.ThreeParamReusable &&
            !string.IsNullOrEmpty(p.ExpressionTargetField))
        {
            sb.AppendLine($"{pad}.Condition(dto => dto.{p.ExpressionTargetField}, {methodRef},");
            sb.AppendLine($"{pad}{Indent}{visualId}){term}");
        }
        else
        {
            sb.AppendLine($"{pad}.Condition({methodRef},");
            sb.AppendLine($"{pad}{Indent}{visualId}){term}");
        }
    }

    private static void EmitWait(StringBuilder sb, BTreeEditorNode node, string pad, bool isLast)
    {
        var p = node.Wait;
        var visualId = $"visualId: new Guid(\"{node.VisualId:D}\")";
        string term = isLast ? ";" : ",";

        float duration = p?.Duration ?? 0f;
        sb.AppendLine($"{pad}.Wait({duration.ToString("R", CultureInfo.InvariantCulture)}f,");
        sb.AppendLine($"{pad}{Indent}{visualId}){term}");
    }

    private static void EmitSubtree(StringBuilder sb, BTreeEditorNode node, string pad, bool isLast)
    {
        var p = node.Subtree;
        var visualId = $"visualId: new Guid(\"{node.VisualId:D}\")";
        string term = isLast ? ";" : ",";

        string subtreeRef = p != null ? $"\"{p.SubtreeAssetId:D}\"" : "\"\"";
        sb.AppendLine($"{pad}.Subtree({subtreeRef},");
        sb.AppendLine($"{pad}{Indent}{visualId}){term}");
    }

    // ---- Build ----

    private static void EmitBuild(StringBuilder sb, BehaviorTreeAsset asset)
    {
        sb.AppendLine($"{Indent}[BTreeDefinition(\"{asset.Name}\", AssetId = \"{asset.AssetId:D}\")]");
        sb.AppendLine($"{Indent}public static BehaviorTreeBlob Build() =>");
        sb.AppendLine($"{Indent}{Indent}CreateBuilder().Compile(\"{asset.Name}\");");
    }

    // ---- Layout ----

    private static void EmitLayout(StringBuilder sb, BehaviorTreeAsset asset)
    {
        sb.AppendLine($"{Indent}[BTreeLayout(\"{asset.AssetId:D}\")]");
        sb.AppendLine($"{Indent}public static BTreeEditorLayout Layout() => new BTreeEditorLayoutBuilder()");
        sb.AppendLine($"{Indent}{Indent}.Canvas(panOffset: new Vector2({asset.CanvasPanOffset.X:R}f, {asset.CanvasPanOffset.Y:R}f), zoomLevel: {asset.CanvasZoomLevel:R}f)");

        // Emit node entries sorted by VisualId (lexicographic) for determinism.
        var nodeEntries = asset.Nodes
            .OrderBy(n => n.VisualId.ToString("D"), StringComparer.Ordinal)
            .ToList();

        for (int i = 0; i < nodeEntries.Count; i++)
        {
            var node = nodeEntries[i];
            bool lastEntry = i == nodeEntries.Count - 1 && asset.Pills.Count == 0;
            EmitLayoutNodeEntry(sb, node, lastEntry);
        }

        // Emit pill entries.
        var pillEntries = asset.Pills
            .OrderBy(p => p.VisualId.ToString("D"), StringComparer.Ordinal)
            .ToList();

        for (int i = 0; i < pillEntries.Count; i++)
        {
            var pill = pillEntries[i];
            bool lastEntry = i == pillEntries.Count - 1;
            EmitLayoutPillEntry(sb, pill, lastEntry);
        }

        if (nodeEntries.Count == 0 && pillEntries.Count == 0)
            sb.AppendLine($"{Indent}{Indent}.Build();");
    }

    private static void EmitLayoutNodeEntry(StringBuilder sb, BTreeEditorNode node, bool isLast)
    {
        string guidStr = $"\"{node.VisualId:D}\"";
        var parts = new List<string>();

        if (node.Position.X != 0f || node.Position.Y != 0f)
            parts.Add($"position: new Vector2({node.Position.X:R}f, {node.Position.Y:R}f)");

        // Expression target field.
        string? exprTarget = node.Action?.ExpressionTargetField ?? node.Condition?.ExpressionTargetField;
        if (!string.IsNullOrEmpty(exprTarget))
            parts.Add($"expressionTarget: \"{exprTarget}\"");

        if (!string.IsNullOrEmpty(node.Comment))
            parts.Add($"comment: \"{EscapeString(node.Comment!)}\"");

        string suffix = isLast ? ".Build();" : "";
        if (parts.Count == 0)
        {
            sb.AppendLine($"{Indent}{Indent}.Node({guidStr}){(isLast ? "" : "")}");
            if (isLast) sb.AppendLine($"{Indent}{Indent}.Build();");
        }
        else
        {
            sb.AppendLine($"{Indent}{Indent}.Node({guidStr},");
            for (int j = 0; j < parts.Count; j++)
            {
                bool last = j == parts.Count - 1;
                sb.AppendLine($"{Indent}{Indent}{Indent}{parts[j]}{(last ? ")" : ",")}");
            }
            if (isLast) sb.AppendLine($"{Indent}{Indent}.Build();");
        }
    }

    private static void EmitLayoutPillEntry(StringBuilder sb, BTreeEditorPill pill, bool isLast)
    {
        string guidStr = $"\"{pill.VisualId:D}\"";
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(pill.Comment))
            parts.Add($"comment: \"{EscapeString(pill.Comment!)}\"");

        if (parts.Count == 0)
        {
            sb.AppendLine($"{Indent}{Indent}.Node({guidStr})");
        }
        else
        {
            sb.AppendLine($"{Indent}{Indent}.Node({guidStr},");
            for (int j = 0; j < parts.Count; j++)
            {
                bool last = j == parts.Count - 1;
                sb.AppendLine($"{Indent}{Indent}{Indent}{parts[j]}{(last ? ")" : ",")}");
            }
        }

        if (isLast) sb.AppendLine($"{Indent}{Indent}.Build();");
    }

    // ---- Helpers ----

    /// <summary>Extracts a short (unqualified) type name from a FQN.</summary>
    private static string ShortTypeName(string fqn)
    {
        int last = fqn.LastIndexOf('.');
        return last >= 0 ? fqn[(last + 1)..] : fqn;
    }

    /// <summary>Extracts "ClassName.MethodName" from a fully qualified method name.</summary>
    private static string ShortMethodRef(string fqn)
    {
        // "Hrot.Game.Combat.CombatActions.AimAndFire" -> "CombatActions.AimAndFire"
        int last   = fqn.LastIndexOf('.');
        if (last <= 0) return fqn;
        int second = fqn.LastIndexOf('.', last - 1);
        return second >= 0 ? fqn[(second + 1)..] : fqn[last..];
    }

    private static string SanitizeIdentifier(string name)
    {
        // Remove non-identifier characters; ensure it starts with a letter.
        var sb = new StringBuilder();
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
        }
        if (sb.Length == 0) return "BTreeAsset";
        if (char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString();
    }

    private static string EscapeString(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
