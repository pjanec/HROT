using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Host;

/// <summary>
/// Node catalog for the BTree canvas.
/// Provides composite, leaf, and decorator palette entries.
/// When constructed with an <see cref="IActionSchemaExporter"/>, also emits
/// dynamic entries for registered BTree actions and conditions.
/// </summary>
public sealed class BTreeNodeCatalog : INodeCatalog
{
    // ---- Single exec pin signatures (reversed convention) ----
    // Output pin: used by children to connect to parent's input
    private static readonly PinSignature ExecOut =
        new("exec", PinKind.Exec, null, false);
    // Input pin: used by composites/root to receive from multiple children
    private static readonly PinSignature ExecIn =
        new("exec", PinKind.Exec, null, false);

    // ---- Category paths ----
    private const string CatComposite = "Composite";
    private const string CatLeaf      = "Leaf";
    private const string CatDecorator = "Decorator";
    // I4: blueprint-compiled AiPrimitive actions get their own palette group so they read as
    // distinct from hand-written leaves.
    private const string CatBlueprintAction = "Blueprint";
    private static readonly string CatReactiveGuard = ReactiveGuardVocabulary.CategoryName;

    // ---- Entries ----
    private readonly IReadOnlyList<NodeCatalogEntry> _staticEntries;
    private IReadOnlyList<NodeCatalogEntry> _dynamicEntries;
    private IReadOnlyList<NodeCatalogEntry> _all;
    private readonly IActionSchemaExporter? _actionSchema;
    private readonly string? _blackboardTypeName;

    public BTreeNodeCatalog() : this(null) { }

    public BTreeNodeCatalog(
        IActionSchemaExporter? actionSchema,
        string? blackboardTypeName = null)
    {
        _staticEntries = BuildStaticEntries();
        _actionSchema = actionSchema;
        _blackboardTypeName = blackboardTypeName;

        if (actionSchema != null)
        {
            _dynamicEntries = BuildDynamicEntries(actionSchema);
            actionSchema.Changed += OnSchemaChanged;
        }
        else
        {
            _dynamicEntries = Array.Empty<NodeCatalogEntry>();
        }

        _all = Concat(_staticEntries, _dynamicEntries);
    }

    private void OnSchemaChanged()
    {
        if (_actionSchema == null) return;
        _dynamicEntries = BuildDynamicEntries(_actionSchema);
        _all = Concat(_staticEntries, _dynamicEntries);
    }

    private static IReadOnlyList<NodeCatalogEntry> Concat(
        IReadOnlyList<NodeCatalogEntry> a,
        IReadOnlyList<NodeCatalogEntry> b)
    {
        if (b.Count == 0) return a;
        var result = new List<NodeCatalogEntry>(a.Count + b.Count);
        result.AddRange(a);
        result.AddRange(b);
        return result.AsReadOnly();
    }

    private IReadOnlyList<NodeCatalogEntry> BuildDynamicEntries(IActionSchemaExporter schema)
    {
        var entries = new List<NodeCatalogEntry>();
        foreach (var kv in schema.All)
        {
            var entry = kv.Value;
            if (!entry.Hosting.HasFlag(ActionHosting.BTree))
                continue;

            // When a blackboard type name is known, filter hand-written actions/conditions to those
            // whose DtoType matches the asset's blackboard so the codegen can bind. Blueprint
            // AiPrimitives (I4) are exempt: their DtoType is the generated Params struct, which is
            // bin-packed into the asset blackboard at a baked offset (they compose as host-BTree
            // nodes), so a blackboard-type match does not apply.
            if (!entry.IsAiPrimitive
                && !string.IsNullOrEmpty(_blackboardTypeName)
                && entry.DtoType?.FullName != _blackboardTypeName)
                continue;

            var shortName = kv.Key.Substring(kv.Key.LastIndexOf('.') + 1);
            var kindId = entry.IsCondition
                ? BTreeKinds.ConditionPrefix + kv.Key
                : BTreeKinds.ActionPrefix + kv.Key;

            // A generated AiPrimitive's method FQN is always "{Blueprint}_{id:X8}_Bp.TickCore", so the
            // bare method name ("TickCore") is a useless label. Show the authored blueprint name.
            var displayName = entry.IsAiPrimitive ? AiPrimitiveDisplayName(kv.Key) : shortName;

            // I4: present blueprint AiPrimitives under their own category with a blueprint icon and
            // a descriptive tooltip/keyword, so they are visually distinct from curated leaves.
            var category = entry.IsAiPrimitive ? CatBlueprintAction : CatLeaf;
            var iconKey  = entry.IsAiPrimitive
                ? (entry.IsCondition ? "bt/blueprint_condition" : "bt/blueprint_action")
                : (entry.IsCondition ? "bt/condition" : "bt/action");
            var description = entry.IsAiPrimitive
                ? "Blueprint-authored AiPrimitive composed as a BTree node."
                : (string?)null;
            var keywords = entry.IsAiPrimitive
                ? new[] { kv.Key, displayName, "blueprint", "aiprimitive" }
                : new[] { kv.Key, shortName };

            entries.Add(new NodeCatalogEntry(
                new NodeKindKey(kindId),
                displayName,
                description,
                category,
                keywords,
                iconKey,
                entry.IsCondition,
                false,
                false,
                Array.Empty<PinSignature>(),
                new[] { ExecOut }));
        }
        return entries.AsReadOnly();
    }

    /// <summary>
    /// Derives a friendly palette label for a generated AiPrimitive from its <c>TickCore</c> FQN.
    /// The Blueprint compiler emits <c>{Namespace}.{SanitizedName}_{BlueprintId:X8}_Bp.TickCore</c>
    /// (see <c>AiPrimitiveEmitter</c>), so strip <c>.TickCore</c>, take the declaring type's short
    /// name, then drop the trailing <c>_{8 hex}_Bp</c> to recover the authored blueprint name.
    /// Falls back to the declaring type's short name if the pattern does not match.
    /// </summary>
    private static string AiPrimitiveDisplayName(string tickCoreFqn)
    {
        int lastDot = tickCoreFqn.LastIndexOf('.');
        string declFqn = lastDot > 0 ? tickCoreFqn.Substring(0, lastDot) : tickCoreFqn;
        int declDot = declFqn.LastIndexOf('.');
        string declShort = declDot >= 0 ? declFqn.Substring(declDot + 1) : declFqn;

        string name = declShort;
        if (name.EndsWith("_Bp", StringComparison.Ordinal))
            name = name.Substring(0, name.Length - 3);

        int us = name.LastIndexOf('_');
        if (us > 0 && name.Length - us - 1 == 8 && IsHex(name.AsSpan(us + 1)))
            name = name.Substring(0, us);

        return string.IsNullOrEmpty(name) ? declShort : name;

        static bool IsHex(ReadOnlySpan<char> s)
        {
            foreach (var c in s)
                if (!Uri.IsHexDigit(c)) return false;
            return true;
        }
    }

    private static IReadOnlyList<NodeCatalogEntry> BuildStaticEntries()
    {
        var entries = new List<NodeCatalogEntry>();

        // Composites — both input and output pins.
        entries.Add(Make(BTreeKinds.Sequence, "Sequence", CatComposite,
            "Runs children left-to-right; fails on first failure.",
            new[] { "sequence", "and" }, "bt/sequence", false, false, false,
            inputs: new[] { ExecIn }, outputs: new[] { ExecOut }));

        entries.Add(Make(BTreeKinds.Selector, "Selector", CatComposite,
            "Runs children left-to-right; succeeds on first success.",
            new[] { "selector", "or", "fallback" }, "bt/selector", false, false, false,
            inputs: new[] { ExecIn }, outputs: new[] { ExecOut }));

        entries.Add(Make(BTreeKinds.ObserverSelector, "Observer Selector", CatReactiveGuard,
            ReactiveGuardVocabulary.BTreeObserverSelectorTooltip + "\n\n" + ReactiveGuardVocabulary.CrossSubsystemHintBTree,
            new[] { "observer", "selector", "reactive", "guard" }, "bt/observer_selector", false, false, false,
            inputs: new[] { ExecIn }, outputs: new[] { ExecOut }));

        entries.Add(Make(BTreeKinds.Parallel, "Parallel", CatComposite,
            "Runs all children simultaneously.",
            new[] { "parallel", "concurrent" }, "bt/parallel", false, false, false,
            inputs: new[] { ExecIn }, outputs: new[] { ExecOut }));

        entries.Add(Make(BTreeKinds.Root, "Root", CatComposite,
            "The root of the behavior tree.",
            new[] { "root", "entry" }, "bt/root", false, false, false,
            inputs: new[] { ExecIn }, outputs: Array.Empty<PinSignature>()));

        // Leaves — output only.
        entries.Add(Make(BTreeKinds.Action, "Action", CatLeaf,
            "Runs a user-defined action delegate.",
            new[] { "action", "do", "execute" }, "bt/action", false, false, false,
            inputs: Array.Empty<PinSignature>(), outputs: new[] { ExecOut }));

        entries.Add(Make(BTreeKinds.Condition, "Condition", CatLeaf,
            "Evaluates a user-defined condition delegate.",
            new[] { "condition", "check", "test" }, "bt/condition", true, false, false,
            inputs: Array.Empty<PinSignature>(), outputs: new[] { ExecOut }));

        entries.Add(Make(BTreeKinds.Wait, "Wait", CatLeaf,
            "Waits for a fixed duration in seconds.",
            new[] { "wait", "delay", "sleep" }, "bt/wait", false, false, false,
            inputs: Array.Empty<PinSignature>(), outputs: new[] { ExecOut }));

        entries.Add(Make(BTreeKinds.Subtree, "Subtree", CatLeaf,
            "Calls another behavior tree asset.",
            new[] { "subtree", "call", "reference" }, "bt/subtree", false, false, false,
            inputs: Array.Empty<PinSignature>(), outputs: new[] { ExecOut }));

        // Decorator pills — no pins; palette action is AttachToSelected.
        entries.Add(MakeDecorator(BTreeKinds.Inverter,    "Inverter",      "Inverts the result of its child."));
        entries.Add(MakeDecorator(BTreeKinds.Repeater,    "Repeater",      "Repeats the child N times."));
        entries.Add(MakeDecorator(BTreeKinds.Cooldown,    "Cooldown",      "Blocks the child for a cooldown period after it runs."));
        entries.Add(MakeDecorator(BTreeKinds.ForceSuccess,"ForceSuccess",  "Forces child result to Success."));
        entries.Add(MakeDecorator(BTreeKinds.ForceFailure,"ForceFailure",  "Forces child result to Failure."));
        entries.Add(MakeDecorator(BTreeKinds.UntilSuccess,"UntilSuccess",  "Repeats until child succeeds."));
        entries.Add(MakeDecorator(BTreeKinds.UntilFailure,"UntilFailure",  "Repeats until child fails."));

        return entries.AsReadOnly();
    }

    private static NodeCatalogEntry Make(
        string kindId, string name, string cat, string? desc,
        IReadOnlyList<string> keywords, string? iconKey,
        bool isPure, bool isLatent, bool isDeprecated,
        IReadOnlyList<PinSignature> inputs,
        IReadOnlyList<PinSignature> outputs) =>
        new(
            new NodeKindKey(kindId), name, desc, cat,
            keywords, iconKey, isPure, isLatent, isDeprecated,
            inputs, outputs);

    private static NodeCatalogEntry MakeDecorator(string kindId, string name, string? desc) =>
        new(
            new NodeKindKey(kindId), name, desc, CatDecorator,
            new[] { name.ToLowerInvariant(), "decorator", "pill" },
            "bt/decorator",
            false, false, false,
            Array.Empty<PinSignature>(),
            Array.Empty<PinSignature>(),
            PaletteAction: NodePaletteAction.AttachToSelected,
            AttachmentCategory: AttachmentCategory.Decorator);

    // ---- INodeCatalog ----

    public IReadOnlyList<NodeCatalogEntry> All => _all;

    public IReadOnlyList<NodeCategoryDescriptor> Categories { get; } = new[]
    {
        new NodeCategoryDescriptor(CatComposite, "Composites", "bt/composite"),
        new NodeCategoryDescriptor(CatLeaf,      "Leaves",     "bt/leaf"),
        new NodeCategoryDescriptor(CatBlueprintAction, "Blueprint Actions", "bt/blueprint"),
        new NodeCategoryDescriptor(CatDecorator, "Decorators", "bt/decorator"),
        new NodeCategoryDescriptor(CatReactiveGuard, ReactiveGuardVocabulary.CategoryName, null),
    };

    public IReadOnlyList<NodeCatalogEntry> Query(NodeSearchQuery q)
    {
        var text = q.Text;
        var results = _all.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(q.CategoryFilter))
            results = results.Where(e => e.CategoryPath == q.CategoryFilter);

        if (!q.IncludeDeprecated)
            results = results.Where(e => !e.IsDeprecated);

        if (!string.IsNullOrWhiteSpace(text))
        {
            var lower = text.ToLowerInvariant();
            results = results.Where(e =>
                e.DisplayName.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
                e.Keywords.Any(k => k.Contains(lower, StringComparison.OrdinalIgnoreCase)) ||
                (e.Description?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return results.ToList();
    }

    public IReadOnlyList<NodeCatalogEntry> QueryForPinContext(PinContextQuery q)
    {
        // All BTree nodes share the single exec type; any non-decorator entry is compatible.
        var results = _all.Where(e => e.CategoryPath != CatDecorator);

        if (!string.IsNullOrWhiteSpace(q.Text))
        {
            var lower = q.Text.ToLowerInvariant();
            results = results.Where(e =>
                e.DisplayName.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
                e.Keywords.Any(k => k.Contains(lower, StringComparison.OrdinalIgnoreCase)));
        }

        return results.ToList();
    }
}
