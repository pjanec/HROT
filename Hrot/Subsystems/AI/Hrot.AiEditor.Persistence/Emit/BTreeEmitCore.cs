using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Hrot.AiEditor.Persistence.BTree;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// Deterministic C# emitter for BTree assets operating on the persisted DTO.
/// Design §6.1: netstandard2.0 emit core — no editor/net8/ImGui reference.
/// Takes a <see cref="BehaviorTreeAssetDto"/> and returns the C# string for
/// <c>CreateBuilder()</c> + the <c>[BTreeDefinition]</c> thunk + the <c>[BTreeLayout]</c> method.
/// Output is byte-identical to BTreeFluentEmitter.Emit(model) when given
/// mapper.ToDto(model).
/// </summary>
public static class BTreeEmitCore
{
    private const string LayoutNamespace = "Hrot.Editor.AiShared.Layout";
    private const string FbtNamespace    = "Fbt";
    private const string Indent          = "    ";

    /// <summary>Emits the complete .cs file content for the given BTree asset DTO.</summary>
    /// <remarks>
    /// Output includes the <c>[BTreeLayout]</c> method.
    /// Byte-identical to <c>BTreeFluentEmitter.Emit(model)</c> when given
    /// <c>mapper.ToDto(model)</c>.  Used by the editor adapter + BATCH-02 gate.
    /// </remarks>
    public static string Emit(BehaviorTreeAssetDto dto)
    {
        return EmitInternal(dto, includeLayout: true);
    }

    /// <summary>
    /// Emits the topology core (.cs file content) for the given BTree asset DTO,
    /// EXCLUDING the <c>[BTreeLayout]</c> method.
    /// Design §6.2: generated <c>.g.cs</c> = <c>CreateBuilder()</c> + <c>[BTreeDefinition]</c> thunk only.
    /// Layout lives in JSON; read by the future JSON loader (PU-301).
    /// </summary>
    public static string EmitTopologyCore(BehaviorTreeAssetDto dto)
    {
        return EmitInternal(dto, includeLayout: false);
    }

    /// <summary>Core emitter: shared implementation for both <see cref="Emit"/> and <see cref="EmitTopologyCore"/>.</summary>
    private static string EmitInternal(BehaviorTreeAssetDto dto, bool includeLayout)
    {
        var sb = new StringBuilder();
        var usings = includeLayout ? CollectUsings(dto) : CollectUsingsTopologyOnly(dto);

        // Header
        sb.AppendLine(AiEmitCoreBase.BuildHeader(dto.AssetId));

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
        var targetNs = string.IsNullOrEmpty(dto.TargetNamespace)
            ? "Hrot.AI.Behaviors.Trees"
            : dto.TargetNamespace;
        var className = SanitizeIdentifier(dto.Name);

        sb.AppendLine($"namespace {targetNs};");
        sb.AppendLine();
        sb.AppendLine($"public static class {className}");
        sb.AppendLine("{");

        // Method 1: CreateBuilder
        EmitCreateBuilder(sb, dto);

        sb.AppendLine();

        // Method 2: Build (the [BTreeDefinition] thunk)
        EmitBuild(sb, dto);

        if (includeLayout)
        {
            sb.AppendLine();

            // Method 3: Layout
            EmitLayout(sb, dto);
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    // ---- Using collection ----

    /// <summary>
    /// Collects usings for the full file (includes <c>Hrot.Editor.AiShared.Layout</c> for the
    /// <c>[BTreeLayout]</c> method).  Used by <see cref="Emit"/>.
    /// </summary>
    private static IReadOnlyList<string> CollectUsings(BehaviorTreeAssetDto dto)
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
        AddNamespaceFromTypeName(set, dto.BlackboardTypeName);
        AddNamespaceFromTypeName(set, dto.ContextTypeName);

        // Scan nodes for action/condition FQNs.
        foreach (var node in dto.Nodes)
        {
            if (node is BTreeActionNodeDto actNode && actNode.Action != null)
                AddNamespaceFromFqn(set, actNode.Action.MethodFqn);
            if (node is BTreeConditionNodeDto condNode && condNode.Condition != null)
                AddNamespaceFromFqn(set, condNode.Condition.MethodFqn);
        }

        return AiEmitCoreBase.SortUsings(set);
    }

    /// <summary>
    /// Collects usings for the topology-core-only file (excludes
    /// <c>Hrot.Editor.AiShared.Layout</c> — no <c>[BTreeLayout]</c> method).
    /// Used by <see cref="EmitTopologyCore"/>.
    /// </summary>
    private static IReadOnlyList<string> CollectUsingsTopologyOnly(BehaviorTreeAssetDto dto)
    {
        var set = new HashSet<string>
        {
            "System",
            "System.Numerics",
            FbtNamespace,
            "Fbt.Compiler",
            // NOTE: LayoutNamespace intentionally excluded — no [BTreeLayout] in topology core.
        };

        // Add namespaces from blackboard / context type names.
        AddNamespaceFromTypeName(set, dto.BlackboardTypeName);
        AddNamespaceFromTypeName(set, dto.ContextTypeName);

        // Scan nodes for action/condition FQNs.
        foreach (var node in dto.Nodes)
        {
            if (node is BTreeActionNodeDto actNode && actNode.Action != null)
                AddNamespaceFromFqn(set, actNode.Action.MethodFqn);
            if (node is BTreeConditionNodeDto condNode && condNode.Condition != null)
                AddNamespaceFromFqn(set, condNode.Condition.MethodFqn);
        }

        return AiEmitCoreBase.SortUsings(set);
    }

    private static void AddNamespaceFromTypeName(HashSet<string> set, string typeName)
    {
        int last = typeName.LastIndexOf('.');
        if (last > 0) set.Add(typeName.Substring(0, last));
    }

    private static void AddNamespaceFromFqn(HashSet<string> set, string fqn)
    {
        int last = fqn.LastIndexOf('.');
        if (last <= 0) return;
        int second = fqn.LastIndexOf('.', last - 1);
        if (second > 0) set.Add(fqn.Substring(0, second));
        else set.Add(fqn.Substring(0, last));
    }

    // ---- Cycle guard (BATCH-14) ----

    /// <summary>
    /// Pre-pass cycle detection via DFS over <see cref="BTreeNodeDto.ChildVisualIds"/>
    /// starting at <paramref name="entry"/>. Uses a path-visited set to detect back-edges.
    /// Throws <see cref="InvalidOperationException"/> (a normal, catchable exception)
    /// on the first back-edge — BEFORE the recursive emit walk so a
    /// <see cref="StackOverflowException"/> never occurs.
    /// </summary>
    private static void CheckNoCycles(BehaviorTreeAssetDto dto, Dictionary<Guid, BTreeNodeDto> nodeById, BTreeNodeDto entry)
    {
        var pathSet = new HashSet<Guid>();
        DfsCheckNoCycles(entry, nodeById, pathSet);
    }

    private static void DfsCheckNoCycles(BTreeNodeDto node, Dictionary<Guid, BTreeNodeDto> nodeById, HashSet<Guid> pathSet)
    {
        if (!pathSet.Add(node.VisualId))
        {
            throw new InvalidOperationException(
                $"Cycle detected in BTree topology at node {node.VisualId:D} — a node cannot be its own ancestor. Fix the wiring in the editor.");
        }

        foreach (var childId in node.ChildVisualIds)
        {
            if (nodeById.TryGetValue(childId, out var child))
                DfsCheckNoCycles(child, nodeById, pathSet);
            // Missing child ids in nodeById are simply skipped (matching emit walk behavior).
        }

        pathSet.Remove(node.VisualId);
    }

    // ---- CreateBuilder ----

    private static void EmitCreateBuilder(StringBuilder sb, BehaviorTreeAssetDto dto)
    {
        var bbShort  = ShortTypeName(dto.BlackboardTypeName);
        var ctxShort = ShortTypeName(dto.ContextTypeName);

        sb.AppendLine($"{Indent}public static BTreeBuilder<{bbShort}, {ctxShort}> CreateBuilder() =>");
        sb.AppendLine($"{Indent}{Indent}new BTreeBuilder<{bbShort}, {ctxShort}>()");

        // Build lookup: VisualId → node
        var nodeById = dto.Nodes.ToDictionary(n => n.VisualId);

        // Find the entry node:
        // 1. If the DTO has an explicit BTreeRootNodeDto (from a hand-authored model with an explicit root),
        //    use its first child as the entry.
        // 2. Otherwise (reflection-loaded blob: builder emits Sequence directly, no implicit root wrapper),
        //    use the first node in the DTO as the entry — matching the .Sequence(...)/.Wait(...) chain pattern.
        var root = dto.Nodes.FirstOrDefault(n => n is BTreeRootNodeDto);
        if (root != null)
        {
            if (root.ChildVisualIds.Count > 0 && nodeById.TryGetValue(root.ChildVisualIds[0], out var entryChild))
            {
                CheckNoCycles(dto, nodeById, entryChild);
                EmitNode(sb, dto, nodeById, entryChild, depth: 3, isLast: true);
            }
            else
                sb.AppendLine($"{Indent}{Indent};");
        }
        else if (dto.Nodes.Count > 0)
        {
            // No explicit root — emit the first node directly (reflection-loaded blob pattern).
            // The generated CreateBuilder() chains: new BTreeBuilder<>().FirstNode(...)
            CheckNoCycles(dto, nodeById, dto.Nodes[0]);
            EmitNode(sb, dto, nodeById, dto.Nodes[0], depth: 3, isLast: true);
        }
        else
        {
            sb.AppendLine($"{Indent}{Indent};");
        }
    }

    private static void EmitNode(
        StringBuilder sb, BehaviorTreeAssetDto dto,
        Dictionary<Guid, BTreeNodeDto> nodeById,
        BTreeNodeDto node, int depth, bool isLast)
    {
        BuildNodeContent(sb, dto, nodeById, node, depth, isLast);
    }

    private static void BuildNodeContent(
        StringBuilder sb, BehaviorTreeAssetDto dto,
        Dictionary<Guid, BTreeNodeDto> nodeById,
        BTreeNodeDto node, int depth, bool isLast,
        string methodPrefix = ".")
    {
        string pad = string.Concat(Enumerable.Repeat(Indent, depth));

        switch (node)
        {
            case BTreeSequenceNodeDto:
                EmitComposite(sb, dto, nodeById, node, "Sequence", "seq", pad, depth, isLast, methodPrefix);
                break;
            case BTreeSelectorNodeDto:
                EmitComposite(sb, dto, nodeById, node, "Selector", "sel", pad, depth, isLast, methodPrefix);
                break;
            case BTreeParallelNodeDto:
                EmitComposite(sb, dto, nodeById, node, "Parallel", "par", pad, depth, isLast, methodPrefix);
                break;
            case BTreeObserverSelectorNodeDto:
                EmitComposite(sb, dto, nodeById, node, "ObserverSelector", "obs", pad, depth, isLast, methodPrefix);
                break;
            case BTreeActionNodeDto:
            case BTreeConditionNodeDto:
            case BTreeWaitNodeDto:
            case BTreeSubtreeNodeDto:
                EmitLeafWithPills(sb, dto, node, depth, isLast, methodPrefix);
                break;
            default:
                sb.AppendLine($"{pad}// Unsupported node type: {node.GetType().Name}");
                break;
        }
    }

    private static void EmitComposite(
        StringBuilder sb, BehaviorTreeAssetDto dto,
        Dictionary<Guid, BTreeNodeDto> nodeById,
        BTreeNodeDto node, string methodName, string lambdaArg,
        string pad, int depth, bool isLast,
        string methodPrefix = ".")
    {
        var pills = dto.Pills
            .Where(p => p.HostNodeVisualId == node.VisualId)
            .OrderByDescending(p => p.StackIndex)
            .ToList();

        string innerPad = pad;
        var pillDepth = depth;
        string nextPillPrefix = methodPrefix;
        foreach (var pill in pills)
        {
            sb.Append(BuildDecoratorOpen(pill, innerPad, pillDepth, nextPillPrefix));
            nextPillPrefix = ".";
            pillDepth++;
            innerPad = string.Concat(Enumerable.Repeat(Indent, pillDepth));
        }

        string visualIdArg = $"visualId: new Guid(\"{node.VisualId:D}\")";
        string compositeMethodPrefix = pills.Count > 0 ? "." : methodPrefix;
        string childPrefix = $"{lambdaArg}.";

        // Parallel's builder signature is Parallel(int policy, Action<...> children, ...);
        // every other composite (Sequence/Selector/ObserverSelector) takes only the children
        // lambda. Without this leading arg the emitted code fails to compile (CS7036).
        // Policy 0 = RequireAll (kernel default); authoring a per-node policy is DEC follow-up.
        string leadingArgs = methodName == "Parallel" ? "0, " : "";

        int childCount = node.ChildVisualIds.Count;
        if (childCount == 0)
        {
            sb.AppendLine($"{innerPad}{compositeMethodPrefix}{methodName}({leadingArgs}{lambdaArg} => {{ }},");
            sb.AppendLine($"{innerPad}{Indent}{visualIdArg}){(isLast ? ";" : ",")}");
        }
        else
        {
            sb.AppendLine($"{innerPad}{compositeMethodPrefix}{methodName}({leadingArgs}{lambdaArg} =>");
            sb.AppendLine($"{innerPad}{{");
            for (int i = 0; i < childCount; i++)
            {
                var childId = node.ChildVisualIds[i];
                if (nodeById.TryGetValue(childId, out var child))
                    EmitChildNode(sb, dto, nodeById, child, pillDepth + 1, isLast: true, methodPrefix: childPrefix);
            }
            bool hasPills = pills.Count > 0;
            string closeSuffix = (hasPills || !isLast) ? "," : ";";
            sb.AppendLine($"{innerPad}}},");
            sb.AppendLine($"{innerPad}{Indent}{visualIdArg}){closeSuffix}");
        }

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
        StringBuilder sb, BehaviorTreeAssetDto dto,
        Dictionary<Guid, BTreeNodeDto> nodeById,
        BTreeNodeDto node, int depth, bool isLast,
        string methodPrefix = ".")
    {
        string pad = string.Concat(Enumerable.Repeat(Indent, depth));

        switch (node)
        {
            case BTreeSequenceNodeDto:
                EmitComposite(sb, dto, nodeById, node, "Sequence", "seq", pad, depth, isLast, methodPrefix);
                break;
            case BTreeSelectorNodeDto:
                EmitComposite(sb, dto, nodeById, node, "Selector", "sel", pad, depth, isLast, methodPrefix);
                break;
            case BTreeParallelNodeDto:
                EmitComposite(sb, dto, nodeById, node, "Parallel", "par", pad, depth, isLast, methodPrefix);
                break;
            case BTreeObserverSelectorNodeDto:
                EmitComposite(sb, dto, nodeById, node, "ObserverSelector", "obs", pad, depth, isLast, methodPrefix);
                break;
            case BTreeActionNodeDto:
            case BTreeConditionNodeDto:
            case BTreeWaitNodeDto:
            case BTreeSubtreeNodeDto:
                EmitLeafWithPills(sb, dto, node, depth, isLast, methodPrefix);
                break;
            default:
                sb.AppendLine($"{pad}// Unknown node type: {node.GetType().Name}");
                break;
        }
    }

    private static string BuildDecoratorOpen(BTreePillDto pill, string pad, int pillDepth, string methodPrefix = ".")
    {
        string v = $"d{pillDepth}";
        string decoratorType = pill.DecoratorType;
        return decoratorType switch
        {
            "Inverter"     => $"{pad}{methodPrefix}Inverter({v} => {v}\n",
            "Repeater"     => $"{pad}{methodPrefix}Repeater({pill.IntParam ?? 1}, {v} => {v}\n",
            "Cooldown"     => $"{pad}{methodPrefix}Cooldown({FloatLiteral(pill.FloatParam ?? 0f)}, {v} => {v}\n",
            "ForceSuccess" => $"{pad}{methodPrefix}ForceSuccess({v} => {v}\n",
            "ForceFailure" => $"{pad}{methodPrefix}ForceFailure({v} => {v}\n",
            "UntilSuccess" => $"{pad}{methodPrefix}UntilSuccess({v} => {v}\n",
            "UntilFailure" => $"{pad}{methodPrefix}UntilFailure({v} => {v}\n",
            _              => string.Empty,
        };
    }

    private static string BuildDecoratorClose(BTreePillDto pill, string pad, bool isLast)
    {
        string term = isLast ? ";" : ",";
        string visualId = $"visualId: new Guid(\"{pill.VisualId:D}\")";
        return $"{pad}{Indent}{visualId}){term}\n";
    }

    private static void EmitLeafWithPills(
        StringBuilder sb, BehaviorTreeAssetDto dto,
        BTreeNodeDto node, int depth, bool isLast,
        string methodPrefix = ".")
    {
        var pills = dto.Pills
            .Where(p => p.HostNodeVisualId == node.VisualId)
            .OrderByDescending(p => p.StackIndex)
            .ToList();

        string pad = string.Concat(Enumerable.Repeat(Indent, depth));
        int pillDepth = depth;
        string nextPillPrefix = methodPrefix;

        foreach (var pill in pills)
        {
            sb.Append(BuildDecoratorOpen(pill, pad, pillDepth, nextPillPrefix));
            nextPillPrefix = ".";
            pillDepth++;
            pad = string.Concat(Enumerable.Repeat(Indent, pillDepth));
        }

        bool innerIsLast = pills.Count == 0 && isLast;
        string leafMethodPrefix = pills.Count > 0 ? "." : methodPrefix;

        switch (node)
        {
            case BTreeActionNodeDto actNode:
                EmitAction(sb, actNode, pad, innerIsLast, leafMethodPrefix);
                break;
            case BTreeConditionNodeDto condNode:
                EmitCondition(sb, condNode, pad, innerIsLast, leafMethodPrefix);
                break;
            case BTreeWaitNodeDto waitNode:
                EmitWait(sb, waitNode, pad, innerIsLast, leafMethodPrefix);
                break;
            case BTreeSubtreeNodeDto subtreeNode:
                EmitSubtree(sb, subtreeNode, pad, innerIsLast, leafMethodPrefix);
                break;
            default:
                sb.AppendLine($"{pad}// Unknown leaf type: {node.GetType().Name}");
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

    private static void EmitAction(StringBuilder sb, BTreeActionNodeDto node, string pad, bool isLast, string methodPrefix = ".")
    {
        var p = node.Action;
        var visualId = $"visualId: new Guid(\"{node.VisualId:D}\")";
        string term = isLast ? ";" : ",";

        if (p == null || string.IsNullOrEmpty(p.MethodFqn))
        {
            throw new InvalidOperationException(
                $"Action node {node.VisualId:D} is unbound (no method) — bind a method in the editor.");
        }

        string methodRef = ShortMethodRef(p.MethodFqn);
        if (p.DelegateShape == BTreeDelegateShapeDto.ThreeParamReusable &&
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

    private static void EmitCondition(StringBuilder sb, BTreeConditionNodeDto node, string pad, bool isLast, string methodPrefix = ".")
    {
        var p = node.Condition;
        var visualId = $"visualId: new Guid(\"{node.VisualId:D}\")";
        string term = isLast ? ";" : ",";

        if (p == null || string.IsNullOrEmpty(p.MethodFqn))
        {
            throw new InvalidOperationException(
                $"Condition node {node.VisualId:D} is unbound (no method) — bind a method in the editor.");
        }

        string methodRef = ShortMethodRef(p.MethodFqn);
        if (p.DelegateShape == BTreeDelegateShapeDto.ThreeParamReusable &&
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

    private static void EmitWait(StringBuilder sb, BTreeWaitNodeDto node, string pad, bool isLast, string methodPrefix = ".")
    {
        var p = node.Wait;
        var visualId = $"visualId: new Guid(\"{node.VisualId:D}\")";
        string term = isLast ? ";" : ",";

        float duration = p?.Duration ?? 0f;
        sb.AppendLine($"{pad}{methodPrefix}Wait({duration.ToString("R", CultureInfo.InvariantCulture)}f,");
        sb.AppendLine($"{pad}{Indent}{visualId}){term}");
    }

    private static void EmitSubtree(StringBuilder sb, BTreeSubtreeNodeDto node, string pad, bool isLast, string methodPrefix = ".")
    {
        var p = node.Subtree;
        var visualId = $"visualId: new Guid(\"{node.VisualId:D}\")";
        string term = isLast ? ";" : ",";

        string subtreeRef = p != null ? $"\"{p.SubtreeName}\"" : "\"\"";
        sb.AppendLine($"{pad}{methodPrefix}Subtree({subtreeRef},");
        sb.AppendLine($"{pad}{Indent}{visualId}){term}");
    }

    // ---- Build ----

    private static void EmitBuild(StringBuilder sb, BehaviorTreeAssetDto dto)
    {
        sb.AppendLine($"{Indent}[BTreeDefinition({QuoteStr(dto.Name)}, AssetId = {QuoteStr(dto.AssetId.ToString("D"))})]");
        sb.AppendLine($"{Indent}public static BehaviorTreeBlob Build() =>");
        sb.AppendLine($"{Indent}{Indent}CreateBuilder().Compile(\"{dto.Name}\");");
    }

    // ---- Layout ----

    private static void EmitLayout(StringBuilder sb, BehaviorTreeAssetDto dto)
    {
        sb.AppendLine($"{Indent}[BTreeLayout(\"{dto.AssetId:D}\")]");
        sb.AppendLine($"{Indent}public static BTreeEditorLayout Layout() => new BTreeEditorLayoutBuilder()");
        sb.AppendLine($"{Indent}{Indent}.Canvas(panOffset: new Vector2({dto.Canvas.PanX:R}f, {dto.Canvas.PanY:R}f), zoomLevel: {dto.Canvas.Zoom:R}f)");

        // Node entries sorted by VisualId (lexicographic)
        var nodeEntries = dto.Nodes
            .OrderBy(n => n.VisualId.ToString("D"), StringComparer.Ordinal)
            .ToList();

        // Sync field entries sorted by nodeVisualId then fieldName
        var allSyncBindings = dto.SubtreeSyncBindings;
        var syncFields = allSyncBindings
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .SelectMany(kv => kv.Value
                .Where(b => b.SyncIn || b.SyncOut || b.MasterVariableName != null)
                .OrderBy(b => b.FieldName, StringComparer.Ordinal)
                .Select(b => (NodeId: kv.Key, Binding: b)))
            .Where(x => x.Binding.SyncIn || x.Binding.SyncOut || x.Binding.MasterVariableName != null)
            .Where(x => x.Binding.SyncIn || x.Binding.SyncOut || x.Binding.MasterVariableName != null)
            .ToList();

        syncFields = syncFields
            .Where(x => x.Binding.SyncIn || x.Binding.SyncOut || x.Binding.MasterVariableName != null)
            .Where(x => !(x.Binding.SyncIn == false && x.Binding.SyncOut == false && x.Binding.MasterVariableName == null))
            .ToList();

        var pillEntries = dto.Pills
            .OrderBy(p => p.VisualId.ToString("D"), StringComparer.Ordinal)
            .ToList();

        for (int i = 0; i < nodeEntries.Count; i++)
        {
            var node = nodeEntries[i];
            bool lastEntry = false;
            EmitLayoutNodeEntry(sb, node, lastEntry);
        }

        // Emit per-child waypoints — only for nodes that have any.
        var waypointEntries = nodeEntries
            .Where(n => n.EditorMetadata.Waypoints != null && n.EditorMetadata.Waypoints.Count > 0)
            .OrderBy(n => n.VisualId.ToString("D"), StringComparer.Ordinal)
            .ToList();
        foreach (var node in waypointEntries)
            EmitLayoutLinkWaypoints(sb, node);

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
            EmitSyncFieldEntry(sb, nodeId, binding, lastEntry);
        }

        var conflictSuppressions = dto.Suppressions.Conflict
            .OrderBy(s => s.VariableName)
            .ThenBy(s => s.WriterPairKey)
            .ToList();
        foreach (var sup in conflictSuppressions)
        {
            sb.AppendLine($"{Indent}{Indent}.SuppressBlackboardConflict(\"{sup.VariableName}\", \"{sup.WriterPairKey}\")");
        }

        var unusedSuppressions = dto.Suppressions.Unused.OrderBy(s => s).ToList();
        foreach (var sup in unusedSuppressions)
        {
            sb.AppendLine($"{Indent}{Indent}.SuppressUnusedWarning(\"{sup}\")");
        }

        sb.AppendLine($"{Indent}{Indent}.Build();");
    }

    private static void EmitSyncFieldEntry(StringBuilder sb, string visualId, SubtreeSyncBindingDto b, bool isLast)
    {
        string masterVarExpr = b.MasterVariableName != null
            ? $"masterVar: \"{b.MasterVariableName}\""
            : "masterVar: null";
        string syncInStr  = b.SyncIn  ? "true" : "false";
        string syncOutStr = b.SyncOut ? "true" : "false";
        string suffix = isLast ? ".Build();" : "";
        sb.AppendLine($"{Indent}{Indent}.SubtreeSyncField(\"{visualId}\", \"{b.FieldName}\", {masterVarExpr}, syncIn: {syncInStr}, syncOut: {syncOutStr}){suffix}");
    }

    private static void EmitLayoutNodeEntry(StringBuilder sb, BTreeNodeDto node, bool isLast)
    {
        string guidStr = $"\"{node.VisualId:D}\"";
        var parts = new List<string>();

        float x = node.EditorMetadata.X;
        float y = node.EditorMetadata.Y;
        if (x != 0f || y != 0f)
            parts.Add($"position: new Vector2({x:R}f, {y:R}f)");

        // Expression target field
        string? exprTarget = null;
        if (node is BTreeActionNodeDto actNode)
            exprTarget = actNode.Action?.ExpressionTargetField;
        else if (node is BTreeConditionNodeDto condNode)
            exprTarget = condNode.Condition?.ExpressionTargetField;

        if (!string.IsNullOrEmpty(exprTarget))
            parts.Add($"expressionTarget: \"{exprTarget}\"");

        if (!string.IsNullOrEmpty(node.EditorMetadata.Comment))
            parts.Add($"comment: \"{EscapeString(node.EditorMetadata.Comment!)}\"");

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

    private static void EmitLayoutPillEntry(StringBuilder sb, BTreePillDto pill, bool isLast)
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

    private static void EmitLayoutLinkWaypoints(StringBuilder sb, BTreeNodeDto node)
    {
        var waypoints = node.EditorMetadata.Waypoints!;
        string guidStr = $"\"{node.VisualId:D}\"";
        var pts = string.Join(", ",
            waypoints.Select(wp => $"new Vector2({wp.X.ToString("R", CultureInfo.InvariantCulture)}f, {wp.Y.ToString("R", CultureInfo.InvariantCulture)}f)"));
        sb.AppendLine($"{Indent}{Indent}.LinkWaypoints({guidStr}, new Vector2[] {{ {pts} }})");
    }

    // ---- Helpers ----

    private static string ShortTypeName(string fqn)
    {
        int last = fqn.LastIndexOf('.');
        return last >= 0 ? fqn.Substring(last + 1) : fqn;
    }

    private static string ShortMethodRef(string fqn)
    {
        int last   = fqn.LastIndexOf('.');
        if (last <= 0) return fqn;
        int second = fqn.LastIndexOf('.', last - 1);
        return second >= 0 ? fqn.Substring(second + 1) : fqn.Substring(last);
    }

    private static string SanitizeIdentifier(string name)
    {
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

    private static string QuoteStr(string s) => $"\"{s}\"";

    private static string EscapeString(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
