using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler.Graph;
using Fbt.Runtime;
using Fbt.Serialization;

namespace Fbt.Compiler
{
    /// <summary>
    /// Fluent builder for constructing BehaviorTreeBlob instances programmatically
    /// without JSON, with full type safety for action delegates.
    /// </summary>
    /// <typeparam name="TBlackboard">Blackboard struct type.</typeparam>
    /// <typeparam name="TContext">Context struct type.</typeparam>
    public sealed class BTreeBuilder<TBlackboard, TContext>
        where TBlackboard : struct
        where TContext : struct, IAIContext
    {
        // ---- Internal entry: pairs a BuilderNode with its debug metadata and child entries ----
        private sealed class BuilderEntry
        {
            public readonly BuilderNode Node;
            public readonly NodeDebugMetadata Meta;
            public readonly List<BuilderEntry> ChildEntries = new List<BuilderEntry>();
            // For expression-bound leaves (FBT-003); null for regular delegate leaves.
            public string? TargetFieldName;
            public string? TargetDtoType;

            public BuilderEntry(BuilderNode node, NodeDebugMetadata meta)
            {
                Node = node;
                Meta = meta;
            }
        }

        // ---- Fields ----

        private readonly List<BuilderEntry> _entries = new List<BuilderEntry>();
        private readonly ActionRegistry<TBlackboard, TContext> _registry;

        // ---- Constructors ----

        /// <summary>Creates a new BTreeBuilder with its own fresh ActionRegistry.</summary>
        public BTreeBuilder()
        {
            _registry = new ActionRegistry<TBlackboard, TContext>();
        }

        // Private constructor used by child builder contexts (shares the same registry)
        private BTreeBuilder(ActionRegistry<TBlackboard, TContext> registry)
        {
            _registry = registry;
        }

        // ---- Composites ----

        /// <summary>Adds a Sequence node whose children are populated by <paramref name="children"/>.</summary>
        public BTreeBuilder<TBlackboard, TContext> Sequence(
            Action<BTreeBuilder<TBlackboard, TContext>> children,
            Guid visualId = default,
            [CallerFilePath] string sourceFile = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            return AddComposite(NodeType.Sequence, "Sequence", -1, children, visualId, sourceFile, lineNumber);
        }

        /// <summary>Adds a Selector node whose children are populated by <paramref name="children"/>.</summary>
        public BTreeBuilder<TBlackboard, TContext> Selector(
            Action<BTreeBuilder<TBlackboard, TContext>> children,
            Guid visualId = default,
            [CallerFilePath] string sourceFile = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            return AddComposite(NodeType.Selector, "Selector", -1, children, visualId, sourceFile, lineNumber);
        }

        /// <summary>Adds a Parallel node whose children are populated by <paramref name="children"/>.</summary>
        public BTreeBuilder<TBlackboard, TContext> Parallel(
            int policy,
            Action<BTreeBuilder<TBlackboard, TContext>> children,
            Guid visualId = default,
            [CallerFilePath] string sourceFile = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            return AddComposite(
                NodeType.Parallel,
                $"Parallel(policy={policy})",
                policy,
                children,
                visualId,
                sourceFile,
                lineNumber);
        }

        // ---- Decorators ----

        /// <summary>Adds an Inverter decorator node wrapping a single child.</summary>
        public BTreeBuilder<TBlackboard, TContext> Inverter(
            Action<BTreeBuilder<TBlackboard, TContext>> child,
            Guid visualId = default,
            [CallerFilePath] string sourceFile = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            return AddDecorator(NodeType.Inverter, "Inverter", child, visualId, sourceFile, lineNumber);
        }

        /// <summary>Adds a Repeater decorator that runs its child <paramref name="count"/> times.</summary>
        public BTreeBuilder<TBlackboard, TContext> Repeater(
            int count,
            Action<BTreeBuilder<TBlackboard, TContext>> child,
            Guid visualId = default,
            [CallerFilePath] string sourceFile = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            var node = new BuilderNode { Type = NodeType.Repeater, RepeatCount = count };
            return AddDecoratorWithNode(node, $"Repeater({count}x)", child, visualId, sourceFile, lineNumber);
        }

        /// <summary>Adds a Wait leaf node that blocks for <paramref name="duration"/> seconds.</summary>
        public BTreeBuilder<TBlackboard, TContext> Wait(
            float duration,
            Guid visualId = default,
            [CallerFilePath] string sourceFile = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            var node = new BuilderNode { Type = NodeType.Wait, WaitTime = duration };
            string durationStr = duration.ToString("G", CultureInfo.InvariantCulture);
            var meta = BuildMeta($"Wait({durationStr}s)", sourceFile, lineNumber, visualId);
            _entries.Add(new BuilderEntry(node, meta));
            return this;
        }

        /// <summary>Adds a Cooldown decorator that gates its child behind a cooldown period.</summary>
        public BTreeBuilder<TBlackboard, TContext> Cooldown(
            float duration,
            Action<BTreeBuilder<TBlackboard, TContext>> child,
            Guid visualId = default,
            [CallerFilePath] string sourceFile = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            var node = new BuilderNode { Type = NodeType.Cooldown, CooldownTime = duration };
            string durationStr = duration.ToString("G", CultureInfo.InvariantCulture);
            return AddDecoratorWithNode(node, $"Cooldown({durationStr}s)", child, visualId, sourceFile, lineNumber);
        }

        // ---- Leaves ----

        /// <summary>Adds an Action leaf node backed by <paramref name="action"/>.</summary>
        public BTreeBuilder<TBlackboard, TContext> Action(
            NodeLogicDelegate<TBlackboard, TContext> action,
            Guid visualId = default,
            [CallerFilePath] string sourceFile = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            return AddLeaf(NodeType.Action, action, visualId, sourceFile, lineNumber);
        }

        /// <summary>Adds a Condition leaf node backed by <paramref name="condition"/>.</summary>
        public BTreeBuilder<TBlackboard, TContext> Condition(
            NodeLogicDelegate<TBlackboard, TContext> condition,
            Guid visualId = default,
            [CallerFilePath] string sourceFile = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            return AddLeaf(NodeType.Condition, condition, visualId, sourceFile, lineNumber);
        }

        /// <summary>
        /// Adds a Condition leaf node that projects the blackboard to the field selected by
        /// <paramref name="fieldSelector"/> and evaluates <paramref name="logic"/> against it.
        /// The byte offset is computed once at tree-build time via Marshal.OffsetOf.
        /// </summary>
        /// <typeparam name="TValue">Field type. Must be unmanaged.</typeparam>
        /// <remarks>
        /// TBlackboard should be decorated with [StructLayout(LayoutKind.Sequential)] for
        /// Marshal.OffsetOf to produce reliable offsets.
        /// </remarks>
        public BTreeBuilder<TBlackboard, TContext> Condition<TValue>(
            Expression<Func<TBlackboard, TValue>> fieldSelector,
            ReusableConditionDelegate<TValue, TContext> logic,
            Guid visualId = default,
            [CallerFilePath] string sourceFile = "",
            [CallerLineNumber] int lineNumber = 0)
            where TValue : unmanaged
        {
            var (memberName, offset) = ExtractFieldInfo(fieldSelector, nameof(fieldSelector));
            string key = $"{logic.Method.DeclaringType!.FullName}.{logic.Method.Name}@{offset}";

            NodeLogicDelegate<TBlackboard, TContext> curried =
                (ref TBlackboard bb, ref BehaviorTreeState st, ref TContext ctx, int _) =>
                {
                    ref TValue projected = ref Unsafe.As<TBlackboard, TValue>(
                        ref Unsafe.AddByteOffset(ref bb, offset));
                    return logic(ref projected, ref st, ref ctx);
                };
            _registry.Register(key, curried);

            var node = new BuilderNode { Type = NodeType.Condition, MethodName = key };
            var meta = BuildMeta(logic.Method.Name, sourceFile, lineNumber, visualId);
            var entry = new BuilderEntry(node, meta)
            {
                TargetFieldName = memberName,
                TargetDtoType = typeof(TBlackboard).FullName ?? string.Empty
            };
            _entries.Add(entry);
            return this;
        }

        /// <summary>
        /// Adds an Action leaf node that projects the blackboard to the field selected by
        /// <paramref name="fieldSelector"/> and executes <paramref name="logic"/> against it.
        /// The byte offset is computed once at tree-build time via Marshal.OffsetOf.
        /// </summary>
        /// <typeparam name="TValue">Field type. Must be unmanaged.</typeparam>
        /// <remarks>
        /// TBlackboard should be decorated with [StructLayout(LayoutKind.Sequential)] for
        /// Marshal.OffsetOf to produce reliable offsets.
        /// </remarks>
        public BTreeBuilder<TBlackboard, TContext> Action<TValue>(
            Expression<Func<TBlackboard, TValue>> fieldSelector,
            ReusableActionDelegate<TValue, TContext> logic,
            Guid visualId = default,
            [CallerFilePath] string sourceFile = "",
            [CallerLineNumber] int lineNumber = 0)
            where TValue : unmanaged
        {
            var (memberName, offset) = ExtractFieldInfo(fieldSelector, nameof(fieldSelector));
            string key = $"{logic.Method.DeclaringType!.FullName}.{logic.Method.Name}@{offset}";

            NodeLogicDelegate<TBlackboard, TContext> curried =
                (ref TBlackboard bb, ref BehaviorTreeState st, ref TContext ctx, int _) =>
                {
                    ref TValue projected = ref Unsafe.As<TBlackboard, TValue>(
                        ref Unsafe.AddByteOffset(ref bb, offset));
                    return logic(ref projected, ref st, ref ctx);
                };
            _registry.Register(key, curried);

            var node = new BuilderNode { Type = NodeType.Action, MethodName = key };
            var meta = BuildMeta(logic.Method.Name, sourceFile, lineNumber, visualId);
            var entry = new BuilderEntry(node, meta)
            {
                TargetFieldName = memberName,
                TargetDtoType = typeof(TBlackboard).FullName ?? string.Empty
            };
            _entries.Add(entry);
            return this;
        }

        // ---- Terminal calls ----

        /// <summary>
        /// Compiles the tree into a <see cref="BehaviorTreeBlob"/> and populates
        /// <see cref="BehaviorTreeBlob.DebugMetadata"/>.
        /// Throws <see cref="BehaviorTreeBuildException"/> if validation fails.
        /// </summary>
        public BehaviorTreeBlob Compile(string treeName)
        {
            if (_entries.Count == 0)
                throw new InvalidOperationException("The builder has no root node.");
            if (_entries.Count > 1)
                throw new InvalidOperationException(
                    "The builder has multiple root nodes. A behavior tree must have exactly one root.");

            var root = _entries[0];
            var blob = TreeCompiler.FlattenToBlob(root.Node, treeName);

            // Populate DebugMetadata in depth-first order (mirrors FlattenToBlob ordering)
            var metaList = new List<NodeDebugMetadata>();
            FlattenMetadata(root, metaList);
            blob.DebugMetadata = metaList.ToArray();

            return blob;
        }

        /// <summary>Returns the accumulated ActionRegistry containing all registered delegates.</summary>
        public ActionRegistry<TBlackboard, TContext> GetRegistry() => _registry;

        /// <summary>
        /// Converts the current builder state to a <see cref="BehaviorTreeGraph"/>.
        /// May be called before or after <see cref="Compile"/>.
        /// </summary>
        public BehaviorTreeGraph ToGraph(string treeName)
        {
            if (_entries.Count == 0)
                throw new InvalidOperationException("The builder has no root node.");
            if (_entries.Count > 1)
                throw new InvalidOperationException(
                    "The builder has multiple root nodes. A behavior tree must have exactly one root.");

            var graph = new BehaviorTreeGraph { TreeName = treeName };
            graph.RootNode = ConvertToGraphNode(_entries[0], null);
            return graph;
        }

        // ---- Private helpers ----

        private BTreeBuilder<TBlackboard, TContext> AddComposite(
            NodeType type,
            string label,
            int policy,
            Action<BTreeBuilder<TBlackboard, TContext>> children,
            Guid visualId,
            string sourceFile,
            int lineNumber)
        {
            var node = new BuilderNode { Type = type };
            if (policy >= 0) node.Policy = policy;

            var meta = BuildMeta(label, sourceFile, lineNumber, visualId);
            var entry = new BuilderEntry(node, meta);

            var childBuilder = new BTreeBuilder<TBlackboard, TContext>(_registry);
            children(childBuilder);

            foreach (var childEntry in childBuilder._entries)
            {
                node.Children.Add(childEntry.Node);
                entry.ChildEntries.Add(childEntry);
            }

            _entries.Add(entry);
            return this;
        }

        private BTreeBuilder<TBlackboard, TContext> AddDecorator(
            NodeType type,
            string label,
            Action<BTreeBuilder<TBlackboard, TContext>> child,
            Guid visualId,
            string sourceFile,
            int lineNumber)
        {
            return AddDecoratorWithNode(new BuilderNode { Type = type }, label, child, visualId, sourceFile, lineNumber);
        }

        private BTreeBuilder<TBlackboard, TContext> AddDecoratorWithNode(
            BuilderNode node,
            string label,
            Action<BTreeBuilder<TBlackboard, TContext>> child,
            Guid visualId,
            string sourceFile,
            int lineNumber)
        {
            var meta = BuildMeta(label, sourceFile, lineNumber, visualId);
            var entry = new BuilderEntry(node, meta);

            var childBuilder = new BTreeBuilder<TBlackboard, TContext>(_registry);
            child(childBuilder);

            foreach (var childEntry in childBuilder._entries)
            {
                node.Children.Add(childEntry.Node);
                entry.ChildEntries.Add(childEntry);
            }

            _entries.Add(entry);
            return this;
        }

        private BTreeBuilder<TBlackboard, TContext> AddLeaf(
            NodeType type,
            NodeLogicDelegate<TBlackboard, TContext> del,
            Guid visualId,
            string sourceFile,
            int lineNumber)
        {
            string key = GetDelegateKey(del);
            _registry.Register(key, del);

            var node = new BuilderNode { Type = type, MethodName = key };
            var meta = BuildMeta(del.Method.Name, sourceFile, lineNumber, visualId);
            _entries.Add(new BuilderEntry(node, meta));
            return this;
        }

        private static void FlattenMetadata(BuilderEntry entry, List<NodeDebugMetadata> list)
        {
            list.Add(entry.Meta);
            foreach (var child in entry.ChildEntries)
                FlattenMetadata(child, list);
        }

        private static NodeDebugMetadata BuildMeta(string label, string sourceFile, int lineNumber, Guid visualId)
        {
            return new NodeDebugMetadata
            {
                Label = label,
                SourceFile = Path.GetFileName(sourceFile),
                LineNumber = lineNumber,
                VisualId = (visualId == default ? Guid.NewGuid() : visualId).ToString()
            };
        }

        private static string GetDelegateKey(NodeLogicDelegate<TBlackboard, TContext> del)
        {
            return $"{del.Method.DeclaringType!.FullName}.{del.Method.Name}";
        }

        /// <summary>
        /// Extracts the field/property name and its byte offset in TBlackboard from a lambda expression.
        /// </summary>
        private static (string memberName, nint offset) ExtractFieldInfo<TValue>(
            Expression<Func<TBlackboard, TValue>> fieldSelector,
            string parameterName)
            where TValue : unmanaged
        {
            MemberExpression? memberExpr = fieldSelector.Body as MemberExpression;
            if (memberExpr == null && fieldSelector.Body is UnaryExpression unary)
                memberExpr = unary.Operand as MemberExpression;
            if (memberExpr == null)
                throw new ArgumentException(
                    "fieldSelector must be a direct field or property access (e.g. dto => dto.FieldName).",
                    parameterName);
            string memberName = memberExpr.Member.Name;
            // Note: TBlackboard should have [StructLayout(LayoutKind.Sequential)] for reliable offsets.
            nint offset = (nint)Marshal.OffsetOf<TBlackboard>(memberName);
            return (memberName, offset);
        }

        /// <summary>
        /// Recursively converts a BuilderEntry tree into the BehaviorTreeNode graph hierarchy.
        /// </summary>
        private static BehaviorTreeNode ConvertToGraphNode(BuilderEntry entry, BehaviorTreeNode? parent)
        {
            switch (entry.Node.Type)
            {
                case NodeType.Sequence:
                case NodeType.Selector:
                case NodeType.Parallel:
                {
                    var composite = new CompositeNode
                    {
                        Type = entry.Node.Type,
                        ParallelPolicy = entry.Node.Policy,
                        VisualId = Guid.Parse(entry.Meta.VisualId),
                        CustomComment = entry.Meta.CustomComment,
                        Parent = parent
                    };
                    foreach (var childEntry in entry.ChildEntries)
                        composite.Children.Add(ConvertToGraphNode(childEntry, composite));
                    return composite;
                }
                case NodeType.Inverter:
                case NodeType.Repeater:
                case NodeType.Wait:
                case NodeType.Cooldown:
                {
                    float duration = entry.Node.WaitTime > 0f
                        ? entry.Node.WaitTime
                        : entry.Node.CooldownTime;
                    var decorator = new DecoratorNode
                    {
                        Type = entry.Node.Type,
                        Duration = duration,
                        RepeatCount = entry.Node.RepeatCount,
                        VisualId = Guid.Parse(entry.Meta.VisualId),
                        CustomComment = entry.Meta.CustomComment,
                        Parent = parent
                    };
                    if (entry.ChildEntries.Count > 0)
                        decorator.Child = ConvertToGraphNode(entry.ChildEntries[0], decorator);
                    return decorator;
                }
                default: // Action, Condition
                    return new LogicNode
                    {
                        Type = entry.Node.Type,
                        DelegateName = entry.Node.MethodName,
                        TargetDtoType = entry.TargetDtoType ?? string.Empty,
                        TargetFieldName = entry.TargetFieldName ?? string.Empty,
                        VisualId = Guid.Parse(entry.Meta.VisualId),
                        CustomComment = entry.Meta.CustomComment,
                        Parent = parent
                    };
            }
        }
    }
}
