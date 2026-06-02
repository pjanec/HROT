using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Fbt;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
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
    // The layout-contract namespace — matches HsmFluentEmitter and the types in Hrot.Editor.AiContracts.
    private const string LayoutNamespace = "Hrot.Editor.AiShared.Layout";
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
        BuildNodeContent(sb, asset, node, depth, isLast);
    }

    private static void BuildNodeContent(
        StringBuilder sb, BehaviorTreeAsset asset,
        BTreeEditorNode node, int depth, bool isLast,
        string methodPrefix = ".")
    {
        string pad = string.Concat(Enumerable.Repeat(Indent, depth));

        switch (node.KernelType)
        {
            case NodeType.Sequence:
                EmitComposite(sb, asset, node, "Sequence", "seq", pad, depth, isLast, methodPrefix);
                break;
            case NodeType.Selector:
                EmitComposite(sb, asset, node, "Selector", "sel", pad, depth, isLast, methodPrefix);
                break;
            case NodeType.Parallel:
                EmitComposite(sb, asset, node, "Parallel", "par", pad, depth, isLast, methodPrefix);
                break;
            case NodeType.ObserverSelector:
                EmitComposite(sb, asset, node, "ObserverSelector", "obs", pad, depth, isLast, methodPrefix);
                break;
            case NodeType.Action:
            case NodeType.Condition:
            case NodeType.Wait:
            case NodeType.Subtree:
                EmitLeafWithPills(sb, asset, node, depth, isLast, methodPrefix);
                break;
            default:
                sb.AppendLine($"{pad}// Unsupported node type: {node.KernelType}");
                break;
        }
    }

    private static void EmitComposite(
        StringBuilder sb, BehaviorTreeAsset asset,
        BTreeEditorNode node, string methodName, string lambdaArg,
        string pad, int depth, bool isLast,
        string methodPrefix = ".")
    {
        // Build pill prefix/suffix lines.
        var pills = asset.Pills
            .Where(p => p.HostNodeVisualId == node.VisualId)
            .OrderByDescending(p => p.StackIndex)   // outermost first
            .ToList();

        // Opening lines for each pill (from outermost to innermost).
        string innerPad = pad;
        var pillDepth = depth;
        // For the first pill, use the incoming methodPrefix so nested composites chain correctly.
        string nextPillPrefix = methodPrefix;
        foreach (var pill in pills)
        {
            sb.Append(BuildDecoratorOpen(pill, innerPad, pillDepth, nextPillPrefix));
            nextPillPrefix = ".";
            pillDepth++;
            innerPad = string.Concat(Enumerable.Repeat(Indent, pillDepth));
        }

        string visualIdArg = $"visualId: new Guid(\"{node.VisualId:D}\")";
        // After pills, the composite's own method call chains on the last pill's lambda var.
        string compositeMethodPrefix = pills.Count > 0 ? "." : methodPrefix;
        string childPrefix = $"{lambdaArg}.";

        int childCount = node.ChildVisualIds.Count;
        if (childCount == 0)
        {
            // Empty composite.
            sb.AppendLine($"{innerPad}{compositeMethodPrefix}{methodName}({lambdaArg} => {{ }},");
            sb.AppendLine($"{innerPad}{Indent}{visualIdArg}){(isLast ? ";" : ",")}");
        }
        else
        {
            // Statement lambda form avoids the invalid separator problem that arises with
            // expression chaining when multiple children need to be emitted.
            sb.AppendLine($"{innerPad}{compositeMethodPrefix}{methodName}({lambdaArg} =>");
            sb.AppendLine($"{innerPad}{{");
            for (int i = 0; i < childCount; i++)
            {
                var childId = node.ChildVisualIds[i];
                var child = asset.FindNode(childId);
                if (child != null)
                    EmitChildNode(sb, asset, child, pillDepth + 1, isLast: true, methodPrefix: childPrefix);
            }
            bool hasPills = pills.Count > 0;
            string closeSuffix = (hasPills || !isLast) ? "," : ";";
            sb.AppendLine($"{innerPad}}},");
            sb.AppendLine($"{innerPad}{Indent}{visualIdArg}){closeSuffix}");
        }

        // Close pill wrappers (innermost first when closing).
        var closingPills = pills.AsEnumerable().Reverse().ToList();
        for (int i = 0; i < closingPills.Count; i++)
        {
            pillDepth--;
            innerPad = string.Concat(Enumerable.Repeat(Indent, pillDepth));
            bool isThisLast = isLast && i == closingPills.Count - 1;
            sb.Append(BuildDecoratorClose(closingPills[i], innerPad, isThisLast));
        }
    }

    private static void EmitChildNode(
        StringBuilder sb, BehaviorTreeAsset asset,
        BTreeEditorNode node, int depth, bool isLast,
        string methodPrefix = ".")
    {
        string pad = string.Concat(Enumerable.Repeat(Indent, depth));

        switch (node.KernelType)
        {
            case NodeType.Sequence:
                EmitComposite(sb, asset, node, "Sequence", "seq", pad, depth, isLast, methodPrefix);
                break;
            case NodeType.Selector:
                EmitComposite(sb, asset, node, "Selector", "sel", pad, depth, isLast, methodPrefix);
                break;
            case NodeType.Parallel:
                EmitComposite(sb, asset, node, "Parallel", "par", pad, depth, isLast, methodPrefix);
                break;
            case NodeType.ObserverSelector:
                EmitComposite(sb, asset, node, "ObserverSelector", "obs", pad, depth, isLast, methodPrefix);
                break;
            case NodeType.Action:
            case NodeType.Condition:
            case NodeType.Wait:
            case NodeType.Subtree:
                EmitLeafWithPills(sb, asset, node, depth, isLast, methodPrefix);
                break;
            default:
                sb.AppendLine($"{pad}// Unknown node type: {node.KernelType}");
                break;
        }
    }

    private static string BuildDecoratorOpen(BTreeEditorPill pill, string pad, int pillDepth, string methodPrefix = ".")
    {
        string v = $"d{pillDepth}";
        return pill.DecoratorType switch
        {
            NodeType.Inverter     => $"{pad}{methodPrefix}Inverter({v} => {v}\n",
            NodeType.Repeater     => $"{pad}{methodPrefix}Repeater({pill.IntParam ?? 1}, {v} => {v}\n",
            NodeType.Cooldown     => $"{pad}{methodPrefix}Cooldown({FloatLiteral(pill.FloatParam ?? 0f)}, {v} => {v}\n",
            NodeType.ForceSuccess => $"{pad}{methodPrefix}ForceSuccess({v} => {v}\n",
            NodeType.ForceFailure => $"{pad}{methodPrefix}ForceFailure({v} => {v}\n",
            NodeType.UntilSuccess => $"{pad}{methodPrefix}UntilSuccess({v} => {v}\n",
            NodeType.UntilFailure => $"{pad}{methodPrefix}UntilFailure({v} => {v}\n",
            _                     => string.Empty,
        };
    }

    private static string BuildDecoratorClose(BTreeEditorPill pill, string pad, bool isLast)
    {
        string term = isLast ? ";" : ",";
        string visualId = $"visualId: new Guid(\"{pill.VisualId:D}\")"; 
        return $"{pad}{Indent}{visualId}){term}\n";
    }

    // Emits a leaf node wrapped with any decorator pills it owns.
    private static void EmitLeafWithPills(
        StringBuilder sb, BehaviorTreeAsset asset,
        BTreeEditorNode node, int depth, bool isLast,
        string methodPrefix = ".")
    {
        var pills = asset.Pills
            .Where(p => p.HostNodeVisualId == node.VisualId)
            .OrderByDescending(p => p.StackIndex)
            .ToList();

        string pad = string.Concat(Enumerable.Repeat(Indent, depth));
        int pillDepth = depth;
        // For the first pill, use the incoming methodPrefix so nested leaves chain correctly.
        string nextPillPrefix = methodPrefix;

        foreach (var pill in pills)
        {
            sb.Append(BuildDecoratorOpen(pill, pad, pillDepth, nextPillPrefix));
            nextPillPrefix = ".";
            pillDepth++;
            pad = string.Concat(Enumerable.Repeat(Indent, pillDepth));
        }

        // When wrapped by pills, the leaf itself chains on the pill's lambda var via ".".
        bool innerIsLast = pills.Count == 0 && isLast;
        string leafMethodPrefix = pills.Count > 0 ? "." : methodPrefix;

        switch (node.KernelType)
        {
            case NodeType.Action:    EmitAction(sb, node, pad, innerIsLast, leafMethodPrefix);    break;
            case NodeType.Condition: EmitCondition(sb, node, pad, innerIsLast, leafMethodPrefix); break;
            case NodeType.Wait:      EmitWait(sb, node, pad, innerIsLast, leafMethodPrefix);      break;
            case NodeType.Subtree:   EmitSubtree(sb, node, pad, innerIsLast, leafMethodPrefix);   break;
            default:
                sb.AppendLine($"{pad}// Unknown leaf type: {node.KernelType}");
                break;
        }

        var closingPills = pills.AsEnumerable().Reverse().ToList();
        for (int i = 0; i < closingPills.Count; i++)
        {
            pillDepth--;
            pad = string.Concat(Enumerable.Repeat(Indent, pillDepth));
            bool isThisLast = isLast && i == closingPills.Count - 1;
            sb.Append(BuildDecoratorClose(closingPills[i], pad, isThisLast));
        }
    }

    private static string FloatLiteral(float f) =>
        f.ToString("R", CultureInfo.InvariantCulture) + "f";

    private static void EmitAction(StringBuilder sb, BTreeEditorNode node, string pad, bool isLast, string methodPrefix = ".")
    {
        var p = node.Action;
        var visualId = $"visualId: new Guid(\"{node.VisualId:D}\")";
        string term = isLast ? ";" : ",";

        if (p == null)
        {
            sb.AppendLine($"{pad}{methodPrefix}Action({visualId}){term}");
            return;
        }

        string methodRef = ShortMethodRef(p.MethodFqn);
        if (p.DelegateShape == BTreeActionDelegateShape.ThreeParamReusable &&
            !string.IsNullOrEmpty(p.ExpressionTargetField))
        {
            sb.AppendLine($"{pad}{methodPrefix}Action(dto => dto.{p.ExpressionTargetField}, {methodRef},");
            sb.AppendLine($"{pad}{Indent}{visualId}){term}");
        }
        else
        {
            sb.AppendLine($"{pad}{methodPrefix}Action({methodRef},");
            sb.AppendLine($"{pad}{Indent}{visualId}){term}");
        }
    }

    private static void EmitCondition(StringBuilder sb, BTreeEditorNode node, string pad, bool isLast, string methodPrefix = ".")
    {
        var p = node.Condition;
        var visualId = $"visualId: new Guid(\"{node.VisualId:D}\")";
        string term = isLast ? ";" : ",";

        if (p == null)
        {
            sb.AppendLine($"{pad}{methodPrefix}Condition({visualId}){term}");
            return;
        }

        string methodRef = ShortMethodRef(p.MethodFqn);
        if (p.DelegateShape == BTreeActionDelegateShape.ThreeParamReusable &&
            !string.IsNullOrEmpty(p.ExpressionTargetField))
        {
            sb.AppendLine($"{pad}{methodPrefix}Condition(dto => dto.{p.ExpressionTargetField}, {methodRef},");
            sb.AppendLine($"{pad}{Indent}{visualId}){term}");
        }
        else
        {
            sb.AppendLine($"{pad}{methodPrefix}Condition({methodRef},");
            sb.AppendLine($"{pad}{Indent}{visualId}){term}");
        }
    }

    private static void EmitWait(StringBuilder sb, BTreeEditorNode node, string pad, bool isLast, string methodPrefix = ".")
    {
        var p = node.Wait;
        var visualId = $"visualId: new Guid(\"{node.VisualId:D}\")";
        string term = isLast ? ";" : ",";

        float duration = p?.Duration ?? 0f;
        sb.AppendLine($"{pad}{methodPrefix}Wait({duration.ToString("R", CultureInfo.InvariantCulture)}f,");
        sb.AppendLine($"{pad}{Indent}{visualId}){term}");
    }

    private static void EmitSubtree(StringBuilder sb, BTreeEditorNode node, string pad, bool isLast, string methodPrefix = ".")
    {
        var p = node.Subtree;
        var visualId = $"visualId: new Guid(\"{node.VisualId:D}\")";
        string term = isLast ? ";" : ",";

        // BPF-018: emit the tree name (not the Guid) because BTreeBuilder.Subtree expects a name string.
        string subtreeRef = p != null ? $"\"{p.SubtreeName}\"" : "\"\"";
        sb.AppendLine($"{pad}{methodPrefix}Subtree({subtreeRef},");
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

        // Emit subtree sync field entries sorted by nodeVisualId then fieldName for determinism.
        var allSyncBindings = asset.GetAllSyncBindings();
        var syncFields = allSyncBindings
            .OrderBy(kv => kv.Key.ToString("D"), StringComparer.Ordinal)
            .SelectMany(kv => kv.Value
                .Where(b => b.SyncIn || b.SyncOut || b.MasterVariableName != null)
                .OrderBy(b => b.FieldName, StringComparer.Ordinal)
                .Select(b => (NodeId: kv.Key, Binding: b)))
            .Where(x => x.Binding.SyncIn || x.Binding.SyncOut || x.Binding.MasterVariableName != null)
            // Omit completely empty bindings (no-op to persist)
            .Where(x => x.Binding.SyncIn || x.Binding.SyncOut || x.Binding.MasterVariableName != null)
            .ToList();

        // Filter out no-op entries (all false and no master var)
        syncFields = syncFields
            .Where(x => x.Binding.SyncIn || x.Binding.SyncOut || x.Binding.MasterVariableName != null)
            .Where(x => !(x.Binding.SyncIn == false && x.Binding.SyncOut == false && x.Binding.MasterVariableName == null))
            .ToList();

        // Emit pill entries.
        var pillEntries = asset.Pills
            .OrderBy(p => p.VisualId.ToString("D"), StringComparer.Ordinal)
            .ToList();

        for (int i = 0; i < nodeEntries.Count; i++)
        {
            var node = nodeEntries[i];
            bool lastEntry = false;
            EmitLayoutNodeEntry(sb, node, lastEntry);
        }

        for (int i = 0; i < pillEntries.Count; i++)
        {
            var pill = pillEntries[i];
            bool lastEntry = false;
            EmitLayoutPillEntry(sb, pill, lastEntry);
        }

        for (int i = 0; i < syncFields.Count; i++)
        {
            var (nodeId, binding) = syncFields[i];
            bool lastEntry = false;
            EmitSyncFieldEntry(sb, nodeId.ToString("D"), binding, lastEntry);
        }

        var conflictSuppressions = asset.GetConflictSuppressions().OrderBy(s => s.VariableName).ThenBy(s => s.WriterPairKey).ToList();
        foreach (var sup in conflictSuppressions)
        {
            sb.AppendLine($"{Indent}{Indent}.SuppressBlackboardConflict(\"{sup.VariableName}\", \"{sup.WriterPairKey}\")");
        }

        var unusedSuppressions = asset.GetUnusedSuppressions().OrderBy(s => s).ToList();
        foreach (var sup in unusedSuppressions)
        {
            sb.AppendLine($"{Indent}{Indent}.SuppressUnusedWarning(\"{sup}\")");
        }

        sb.AppendLine($"{Indent}{Indent}.Build();");
    }

    private static void EmitSyncFieldEntry(StringBuilder sb, string visualId, SubtreeSyncBinding b, bool isLast)
    {
        string masterVarExpr = b.MasterVariableName != null
            ? $"masterVar: \"{b.MasterVariableName}\""
            : "masterVar: null";
        string syncInStr  = b.SyncIn  ? "true" : "false";
        string syncOutStr = b.SyncOut ? "true" : "false";
        string suffix = isLast ? ".Build();" : "";
        sb.AppendLine($"{Indent}{Indent}.SubtreeSyncField(\"{visualId}\", \"{b.FieldName}\", {masterVarExpr}, syncIn: {syncInStr}, syncOut: {syncOutStr}){suffix}");
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

