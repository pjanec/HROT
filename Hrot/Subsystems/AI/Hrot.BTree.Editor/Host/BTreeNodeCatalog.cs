using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Host;

/// <summary>
/// Static node catalog for the BTree canvas.
/// Provides composite, leaf, and decorator palette entries.
/// Dynamic action/condition entries require BehaviorRegistry injection (added in Slice 2).
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

    // ---- Static entries ----
    private readonly IReadOnlyList<NodeCatalogEntry> _all;

    public BTreeNodeCatalog()
    {
        _all = BuildStaticEntries();
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

        entries.Add(Make(BTreeKinds.ObserverSelector, "Observer Selector", CatComposite,
            "Selector with reactive re-evaluation of observer children.",
            new[] { "observer", "selector", "reactive" }, "bt/observer_selector", false, false, false,
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
            Array.Empty<PinSignature>());

    // ---- INodeCatalog ----

    public IReadOnlyList<NodeCatalogEntry> All => _all;

    public IReadOnlyList<NodeCategoryDescriptor> Categories { get; } = new[]
    {
        new NodeCategoryDescriptor(CatComposite, "Composites", "bt/composite"),
        new NodeCategoryDescriptor(CatLeaf,      "Leaves",     "bt/leaf"),
        new NodeCategoryDescriptor(CatDecorator, "Decorators", "bt/decorator"),
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
