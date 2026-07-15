using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Hrot.AiEditor.Persistence.BTree;

namespace Hrot.AiEditor.Persistence.Emit;

// Alias to avoid repeating the long Func<> signature.
using SizeResolverDelegate = System.Func<string, int?>;

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

    // ---- Blackboard struct emit (S1-2) ----

    /// <summary>
    /// Emits a <c>[StructLayout(LayoutKind.Sequential)]</c> struct for a managed blackboard block.
    /// Returns the C# source string and fills <paramref name="packedFields"/> with the
    /// packing result (name → byte offset, packed order = declaration order for master vars).
    /// Returns <c>null</c> when <paramref name="dto"/> is not managed or has no variables.
    /// </summary>
    public static string? EmitBlackboardStructSource(
        BehaviorTreeAssetDto dto,
        out IReadOnlyList<BTreeBlackboardPackHelper.PackedField> packedFields)
        => EmitBlackboardStructSource(dto, sizeResolver: null, out packedFields);

    /// <summary>
    /// Emits a <c>[StructLayout(LayoutKind.Sequential)]</c> struct for a managed blackboard block,
    /// using an optional injected size resolver for struct-DTO types.
    /// Returns the C# source string and fills <paramref name="packedFields"/> with the packing result.
    /// Returns <c>null</c> when <paramref name="dto"/> is not managed or has no variables.
    /// </summary>
    public static string? EmitBlackboardStructSource(
        BehaviorTreeAssetDto dto,
        SizeResolverDelegate? sizeResolver,
        out IReadOnlyList<BTreeBlackboardPackHelper.PackedField> packedFields)
    {
        packedFields = Array.Empty<BTreeBlackboardPackHelper.PackedField>();

        if (!dto.Blackboard.Managed || dto.Blackboard.Variables.Count == 0)
            return null;

        IReadOnlyList<BTreeBlackboardPackHelper.PackedField> fields;
        try
        {
            fields = BTreeBlackboardPackHelper.Pack(dto.Blackboard.Variables, sizeResolver, out _);
        }
        catch (NotSupportedException)
        {
            // Unknown type — skip struct emit; caller decides how to handle.
            return null;
        }

        packedFields = fields;

        var targetNs = string.IsNullOrEmpty(dto.TargetNamespace)
            ? "Hrot.AI.Behaviors.Trees"
            : dto.TargetNamespace;

        // Always prefix with the asset name to ensure uniqueness across multiple managed assets
        // in the same namespace — multiple assets share the same BlackboardTypeName (e.g.
        // "Fdp.Toolkit.Behavior.Components.BrainBlackboard") which would produce identical
        // struct names (CS0101) if we used the TypeName alone.
        // Pattern: {AssetName}_{TypeNameSuffix} — e.g. "T10_MultiAction_BrainBlackboard".
        string assetPrefix   = SanitizeIdentifier(dto.Name);
        string typeSuffix    = string.IsNullOrWhiteSpace(dto.Blackboard.TypeName)
            ? "Blackboard"
            : SanitizeIdentifier(dto.Blackboard.TypeName);
        if (string.IsNullOrEmpty(typeSuffix)) typeSuffix = "Blackboard";
        string structName = assetPrefix + "_" + typeSuffix;

        var sb = new StringBuilder();
        sb.AppendLine(AiEmitCoreBase.BuildHeader(dto.AssetId));
        sb.AppendLine("using System.Runtime.InteropServices;");
        sb.AppendLine();
        sb.AppendLine($"namespace {targetNs};");
        sb.AppendLine();
        sb.AppendLine("[StructLayout(LayoutKind.Sequential)]");
        sb.AppendLine($"public struct {structName}");
        sb.AppendLine("{");

        foreach (var f in fields)
        {
            // S1-2b: bool fields require [MarshalAs(I1)] so managed size=1, not BOOL=4.
            // Struct-DTO fields use global::-qualified name with + → . separator conversion.
            if (f.TypeId == "System.Boolean" || f.TypeId == "bool")
            {
                sb.AppendLine($"{Indent}[MarshalAs(UnmanagedType.I1)]");
            }
            string csTypeName = ToCsTypeName(f.TypeId);
            sb.AppendLine($"{Indent}public {csTypeName} {f.Name};");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// Maps a CLR type FQN to the C# keyword or short name used in generated source.
    /// For struct-DTO types (not primitives/vectors), converts nested-type separator
    /// <c>+</c> → <c>.</c> and prefixes with <c>global::</c> so the field declaration
    /// is valid C# regardless of using-directive scope.
    /// E.g. "Hrot.AI.Behaviors.Brains.DemoCounterNodes+DemoCounterParams"
    ///     → "global::Hrot.AI.Behaviors.Brains.DemoCounterNodes.DemoCounterParams"
    /// </summary>
    private static string ToCsTypeName(string typeId) => typeId switch
    {
        "System.Boolean"  or "bool"   => "bool",
        "System.Byte"     or "byte"   => "byte",
        "System.SByte"    or "sbyte"  => "sbyte",
        "System.Char"     or "char"   => "char",
        "System.Int16"    or "short"  => "short",
        "System.UInt16"   or "ushort" => "ushort",
        "System.Int32"    or "int"    => "int",
        "System.UInt32"   or "uint"   => "uint",
        "System.Single"   or "float"  => "float",
        "System.Int64"    or "long"   => "long",
        "System.UInt64"   or "ulong"  => "ulong",
        "System.Double"   or "double" => "double",
        "System.Numerics.Vector2"    or "Vector2"    => "global::System.Numerics.Vector2",
        "System.Numerics.Vector3"    or "Vector3"    => "global::System.Numerics.Vector3",
        "System.Numerics.Vector4"    or "Vector4"    => "global::System.Numerics.Vector4",
        "System.Numerics.Quaternion" or "Quaternion" => "global::System.Numerics.Quaternion",
        "UnityEngine.Vector2"    => "global::UnityEngine.Vector2",
        "UnityEngine.Vector3"    => "global::UnityEngine.Vector3",
        "UnityEngine.Vector4"    => "global::UnityEngine.Vector4",
        "UnityEngine.Quaternion" => "global::UnityEngine.Quaternion",
        // S1-2b: struct-DTO types — convert CLR nested separator + → . and qualify with global::.
        _ => "global::" + typeId.Replace('+', '.'),
    };

    /// <summary>
    /// Emits the topology core (.cs file content) for the given BTree asset DTO,
    /// EXCLUDING the <c>[BTreeLayout]</c> method.
    /// Design §6.2: generated <c>.g.cs</c> = <c>CreateBuilder()</c> + <c>[BTreeDefinition]</c> thunk only.
    /// Layout lives in JSON; read by the future JSON loader (PU-301).
    /// </summary>
    public static string EmitTopologyCore(BehaviorTreeAssetDto dto)
        => EmitTopologyCore(dto, sizeResolver: null);

    /// <summary>
    /// Emits the topology core (.cs file content) for the given BTree asset DTO,
    /// EXCLUDING the <c>[BTreeLayout]</c> method, using an optional size resolver for struct-DTO types.
    /// </summary>
    public static string EmitTopologyCore(BehaviorTreeAssetDto dto, SizeResolverDelegate? sizeResolver)
    {
        return EmitInternal(dto, includeLayout: false, sizeResolver: sizeResolver);
    }

    /// <summary>Core emitter: shared implementation for both <see cref="Emit"/> and <see cref="EmitTopologyCore"/>.</summary>
    private static string EmitInternal(BehaviorTreeAssetDto dto, bool includeLayout,
        SizeResolverDelegate? sizeResolver = null)
    {
        var sb = new StringBuilder();
        var usings = includeLayout ? CollectUsings(dto) : CollectUsingsTopologyOnly(dto);

        // Pre-compute variable offsets for managed blackboard assets (S1-2 / S1-2b).
        // For unmanaged assets this is empty — EmitAction/EmitCondition fall back to the
        // legacy field-selector form which is byte-identical to the pre-BATCH-02 output.
        IReadOnlyDictionary<string, int> variableOffsets = BuildVariableOffsets(dto, sizeResolver);

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
        EmitCreateBuilder(sb, dto, variableOffsets);

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

    /// <summary>
    /// Pre-computes variable name → byte offset for a managed blackboard block.
    /// Returns an empty dictionary for non-managed assets (guard: §S1-2 / S1-2b).
    /// </summary>
    private static IReadOnlyDictionary<string, int> BuildVariableOffsets(
        BehaviorTreeAssetDto dto,
        SizeResolverDelegate? sizeResolver = null)
    {
        if (!dto.Blackboard.Managed || dto.Blackboard.Variables.Count == 0)
            return new Dictionary<string, int>();

        try
        {
            var fields = BTreeBlackboardPackHelper.Pack(dto.Blackboard.Variables, sizeResolver, out _);
            var map = new Dictionary<string, int>(fields.Count, StringComparer.Ordinal);
            foreach (var f in fields)
                map[f.Name] = f.ByteOffset;
            return map;
        }
        catch
        {
            // Unknown type — fall back to empty (unmanaged path)
            return new Dictionary<string, int>();
        }
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
        AddNamespaceFromTypeName(set, AiEmitCoreBase.EffectiveBlackboardTypeName(dto.BlackboardTypeName));
        AddNamespaceFromTypeName(set, AiEmitCoreBase.EffectiveContextTypeName(dto.ContextTypeName));

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
        AddNamespaceFromTypeName(set, AiEmitCoreBase.EffectiveBlackboardTypeName(dto.BlackboardTypeName));
        AddNamespaceFromTypeName(set, AiEmitCoreBase.EffectiveContextTypeName(dto.ContextTypeName));

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

    private static void EmitCreateBuilder(
        StringBuilder sb, BehaviorTreeAssetDto dto,
        IReadOnlyDictionary<string, int> variableOffsets)
    {
        var bbShort  = ShortTypeName(AiEmitCoreBase.EffectiveBlackboardTypeName(dto.BlackboardTypeName));
        var ctxShort = ShortTypeName(AiEmitCoreBase.EffectiveContextTypeName(dto.ContextTypeName));

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
                EmitNode(sb, dto, nodeById, entryChild, depth: 3, isLast: true, variableOffsets);
            }
            else
                // Root present but no children yet (a normal mid-authoring state): emit an empty
                // root Sequence so the builder has an entry and Compile() does not throw
                // "The builder has no root node".
                EmitEmptyRootSequence(sb);
        }
        else if (dto.Nodes.Count > 0)
        {
            // No explicit root — emit the first node directly (reflection-loaded blob pattern).
            // The generated CreateBuilder() chains: new BTreeBuilder<>().FirstNode(...)
            CheckNoCycles(dto, nodeById, dto.Nodes[0]);
            EmitNode(sb, dto, nodeById, dto.Nodes[0], depth: 3, isLast: true, variableOffsets);
        }
        else
        {
            // Empty tree (no nodes at all): emit an empty root Sequence so the generated builder is
            // a valid no-op instead of an empty builder that crashes Compile(). Keeps an incomplete
            // tree from breaking the whole editor build / behavior registration.
            EmitEmptyRootSequence(sb);
        }
    }

    /// <summary>
    /// Emits an empty root <c>Sequence</c> for a degenerate tree (no nodes, or a root with no
    /// children). A behavior tree must have at least one node or <c>BTreeBuilder.Compile</c> throws
    /// "The builder has no root node"; an empty Sequence is a valid harmless no-op
    /// (<c>AddComposite</c> always adds a builder entry). Chains onto the emitted
    /// <c>new BTreeBuilder&lt;...&gt;()</c> line.
    /// </summary>
    private static void EmitEmptyRootSequence(StringBuilder sb) =>
        sb.AppendLine($"{Indent}{Indent}.Sequence(_ => {{ }});");

    private static void EmitNode(
        StringBuilder sb, BehaviorTreeAssetDto dto,
        Dictionary<Guid, BTreeNodeDto> nodeById,
        BTreeNodeDto node, int depth, bool isLast,
        IReadOnlyDictionary<string, int> variableOffsets)
    {
        BuildNodeContent(sb, dto, nodeById, node, depth, isLast, variableOffsets: variableOffsets);
    }

    private static void BuildNodeContent(
        StringBuilder sb, BehaviorTreeAssetDto dto,
        Dictionary<Guid, BTreeNodeDto> nodeById,
        BTreeNodeDto node, int depth, bool isLast,
        string methodPrefix = ".",
        IReadOnlyDictionary<string, int>? variableOffsets = null)
    {
        string pad = string.Concat(Enumerable.Repeat(Indent, depth));

        switch (node)
        {
            case BTreeSequenceNodeDto:
                EmitComposite(sb, dto, nodeById, node, "Sequence", "seq", pad, depth, isLast, methodPrefix, variableOffsets);
                break;
            case BTreeSelectorNodeDto:
                EmitComposite(sb, dto, nodeById, node, "Selector", "sel", pad, depth, isLast, methodPrefix, variableOffsets);
                break;
            case BTreeParallelNodeDto:
                EmitComposite(sb, dto, nodeById, node, "Parallel", "par", pad, depth, isLast, methodPrefix, variableOffsets);
                break;
            case BTreeObserverSelectorNodeDto:
                EmitComposite(sb, dto, nodeById, node, "ObserverSelector", "obs", pad, depth, isLast, methodPrefix, variableOffsets);
                break;
            case BTreeActionNodeDto:
            case BTreeConditionNodeDto:
            case BTreeWaitNodeDto:
            case BTreeSubtreeNodeDto:
                EmitLeafWithPills(sb, dto, node, depth, isLast, methodPrefix, variableOffsets);
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
        string methodPrefix = ".",
        IReadOnlyDictionary<string, int>? variableOffsets = null)
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
                    EmitChildNode(sb, dto, nodeById, child, pillDepth + 1, isLast: true, methodPrefix: childPrefix, variableOffsets: variableOffsets);
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
        string methodPrefix = ".",
        IReadOnlyDictionary<string, int>? variableOffsets = null)
    {
        string pad = string.Concat(Enumerable.Repeat(Indent, depth));

        switch (node)
        {
            case BTreeSequenceNodeDto:
                EmitComposite(sb, dto, nodeById, node, "Sequence", "seq", pad, depth, isLast, methodPrefix, variableOffsets);
                break;
            case BTreeSelectorNodeDto:
                EmitComposite(sb, dto, nodeById, node, "Selector", "sel", pad, depth, isLast, methodPrefix, variableOffsets);
                break;
            case BTreeParallelNodeDto:
                EmitComposite(sb, dto, nodeById, node, "Parallel", "par", pad, depth, isLast, methodPrefix, variableOffsets);
                break;
            case BTreeObserverSelectorNodeDto:
                EmitComposite(sb, dto, nodeById, node, "ObserverSelector", "obs", pad, depth, isLast, methodPrefix, variableOffsets);
                break;
            case BTreeActionNodeDto:
            case BTreeConditionNodeDto:
            case BTreeWaitNodeDto:
            case BTreeSubtreeNodeDto:
                EmitLeafWithPills(sb, dto, node, depth, isLast, methodPrefix, variableOffsets);
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
        string methodPrefix = ".",
        IReadOnlyDictionary<string, int>? variableOffsets = null)
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
                EmitAction(sb, actNode, pad, innerIsLast, leafMethodPrefix, variableOffsets, dto);
                break;
            case BTreeConditionNodeDto condNode:
                EmitCondition(sb, condNode, pad, innerIsLast, leafMethodPrefix, variableOffsets);
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

    private static void EmitAction(
        StringBuilder sb, BTreeActionNodeDto node, string pad, bool isLast,
        string methodPrefix = ".",
        IReadOnlyDictionary<string, int>? variableOffsets = null,
        BehaviorTreeAssetDto? dto = null)
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
        string? actionTargetField = p.ExpressionTargetField;
        if (p.DelegateShape == BTreeDelegateShapeDto.ThreeParamReusable &&
            !string.IsNullOrEmpty(actionTargetField))
        {
            // S1-2: when variableOffsets is populated (managed blackboard), use the
            // offset-keyed string form: .Action("{MethodFqn}@{offset}", visualId: ...).
            // This keeps the blob key identical to the registry key (single source of truth).
            // When variableOffsets is empty (unmanaged/legacy), fall through to the field-selector form.
            if (variableOffsets != null && variableOffsets.Count > 0 &&
                variableOffsets.TryGetValue(actionTargetField!, out int offset))
            {
                string blobKey = $"{p.MethodFqn}@{offset}";
                sb.AppendLine($"{pad}{methodPrefix}Action(\"{blobKey}\",");
                sb.AppendLine($"{pad}{Indent}{visualId}){term}");
            }
            else
            {
                // Legacy unmanaged path — field-selector form (byte-identical to pre-BATCH-02).
                sb.AppendLine($"{pad}{methodPrefix}Action(dto => dto.{actionTargetField}, {methodRef},");
                sb.AppendLine($"{pad}{Indent}{visualId}){term}");
            }
        }
        else if ((p.DelegateShape == BTreeDelegateShapeDto.ThreeParamReusableStateful ||
                  p.DelegateShape == BTreeDelegateShapeDto.AiPrimitiveTickCore) &&
                 !string.IsNullOrEmpty(actionTargetField) &&
                 variableOffsets != null && variableOffsets.Count > 0 &&
                 variableOffsets.TryGetValue(actionTargetField!, out int statefulParamOffset))
        {
            // S2-1/S3-3: stateful thunk — emit string blob key "{MethodFqn}@{paramOffset}@{slotKey}".
            // The slot key is scope-aware (S3-3): Node → FNV-1a(assetId, nodeVisualId) unchanged;
            // Behavior → FNV-1a(assetId, variableId) so co-bound nodes share one slot. Baked at
            // code-gen time. Must match the bridge thunk's baked const (BTreeBridgeEmitCore
            // .EmitStatefulActionThunks) — both go through ResolveStatefulSlotKey, single source.
            // S3-G: scope governed by the working-state variable when distinct from params.
            int slotKey  = dto != null
                ? BTreeBridgeEmitCore.ResolveStatefulSlotKey(dto, BTreeBridgeEmitCore.StatefulScopeVariable(p), node.VisualId)
                : BTreeBridgeEmitCore.ComputeStatefulSlotKey(default, node.VisualId);
            string blobKey = $"{p.MethodFqn}@{statefulParamOffset}@{slotKey}";
            sb.AppendLine($"{pad}{methodPrefix}Action(\"{blobKey}\",");
            sb.AppendLine($"{pad}{Indent}{visualId}){term}");
        }
        else if (p.DelegateShape == BTreeDelegateShapeDto.AiPrimitiveTickCore)
        {
            // Defense-in-depth: an AiPrimitiveTickCore node must ALWAYS resolve to the offset-keyed
            // string-blob form above — its bound method is a blueprint's generated TickCore
            // (ref Params, ref WorkingState, Entity, EntityRepository, float), which is NOT a
            // NodeLogicDelegate<TBB,TCtx> method group. If we reach here the offset couldn't be
            // resolved (no ExpressionTargetField, no managed blackboard, or the variable wasn't
            // packed) — falling through to the method-group `.Action({methodRef}, ...)` form below
            // would silently emit `.Action(TickCore, ...)`, a guaranteed CS1503 (5-param method bound
            // where a 4-param NodeLogicDelegate is expected). Fail loud instead of emitting garbage;
            // the upstream size-resolver / compatibility-validator gaps this guards against are fixed
            // at generation time (see GeneratedBlueprintSchemaCatalog), so a real build should never
            // reach this branch — if it does, the asset itself is malformed.
            throw new InvalidOperationException(
                $"Action node {node.VisualId:D} binds '{p.MethodFqn}' with DelegateShape=AiPrimitiveTickCore " +
                "but its offset could not be resolved (missing ExpressionTargetField, non-managed blackboard, " +
                "or the target variable isn't packed) — refusing to emit an uncompilable method-group " +
                "`.Action(TickCore, ...)` bind. Fix the asset in the editor (bind ExpressionTargetField to a " +
                "managed blackboard variable typed as the blueprint's generated Params struct).");
        }
        else
        {
            sb.AppendLine($"{pad}{methodPrefix}Action({methodRef},");
            sb.AppendLine($"{pad}{Indent}{visualId}){term}");
        }
    }

    private static void EmitCondition(
        StringBuilder sb, BTreeConditionNodeDto node, string pad, bool isLast,
        string methodPrefix = ".",
        IReadOnlyDictionary<string, int>? variableOffsets = null)
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
        string? condTargetField = p.ExpressionTargetField;
        if (p.DelegateShape == BTreeDelegateShapeDto.ThreeParamReusable &&
            !string.IsNullOrEmpty(condTargetField))
        {
            // S1-2: same offset-key logic as EmitAction.
            if (variableOffsets != null && variableOffsets.Count > 0 &&
                variableOffsets.TryGetValue(condTargetField!, out int offset))
            {
                string blobKey = $"{p.MethodFqn}@{offset}";
                sb.AppendLine($"{pad}{methodPrefix}Condition(\"{blobKey}\",");
                sb.AppendLine($"{pad}{Indent}{visualId}){term}");
            }
            else
            {
                // Legacy unmanaged path — field-selector form (byte-identical to pre-BATCH-02).
                sb.AppendLine($"{pad}{methodPrefix}Condition(dto => dto.{condTargetField}, {methodRef},");
                sb.AppendLine($"{pad}{Indent}{visualId}){term}");
            }
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
