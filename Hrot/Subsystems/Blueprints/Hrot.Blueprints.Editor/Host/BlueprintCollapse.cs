using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Transform;
using NodeEditor.Core.Action;
using NodeEditor.Core.Commands;
using NodeEditor.Core.View;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// BP-74 / Q26 — the <b>one</b> place the editor turns a selection into a collapse.
///
/// <para>
/// ⭐ <b>Why this exists as a separate type.</b> Two callers need the same gesture and neither can
/// own it: <see cref="BlueprintCommandSink"/> applies <c>GraphCommand.CollapseToFunction</c> /
/// <c>CollapseToMacro</c> but must not record undo (a sink is the applier, the
/// <c>UndoStack</c> is the recorder), while <c>BlueprintDocumentFactory</c>'s registered
/// <c>editor.collapse-to-*</c> commands must record exactly <b>one</b> undo entry. Duplicating the
/// mechanics across the two is how <c>BP-60</c>'s family of defects starts.
/// </para>
///
/// <para>
/// ⛔ <b>No boundary logic lives here.</b> Batch 33 put it in <c>.Compiler</c>
/// (<see cref="CollapseAnalysis"/> / <see cref="CollapseEmitter"/>) deliberately, so it is testable
/// without an editor. This type's whole job is <i>ids → analyse → emit → apply or report</i>.
/// </para>
///
/// <para>
/// ⭐⭐ <b>The forward/inverse pair is a snapshot substitution, and that is the load-bearing
/// decision.</b> <see cref="CollapseEmitter"/> <i>clones</i> the lifted nodes into the extracted
/// graph rather than moving them, so the host's own <see cref="Node"/> objects are still intact when
/// the forward runs. The inverse therefore restores <b>the original objects</b> — same node ids,
/// same pin ids — rather than reconstructing equivalents.
/// </para>
///
/// <para>
/// ⛔⛔ <b>The tempting wrong inverse is "undo = expand the call node back."</b> Expansion mints
/// fresh ids, so undo→redo→undo would drift identity every cycle, and every pin GUID
/// (<c>SHA-256("pin:{nodeId}:{name}:{direction}")</c>) drifts with it — breakpoints, the debug map
/// and any saved reference would follow nodes that no longer exist. The plan and the emitted edit
/// are computed <b>once</b>, in <see cref="Prepare"/>, so replaying forward after an undo re-applies
/// the identical graphs instead of re-deriving them.
/// </para>
/// </summary>
internal static class BlueprintCollapse
{
    /// <summary>
    /// Analyses <paramref name="selection"/> and, when it is legal, builds the forward/inverse pair
    /// that performs the collapse. <b>Nothing is mutated</b> until <see cref="CollapsePreparation.Forward"/>
    /// runs.
    /// </summary>
    /// <param name="asset">Owning asset — the extracted graph is appended to (removed from) its graph list.</param>
    /// <param name="host">The graph the selection lives in.</param>
    /// <param name="selection">Selected node ids; ids not in <paramref name="host"/> are ignored by the analysis.</param>
    /// <param name="target">Macro or Function. The analysis refuses selections a Function cannot express.</param>
    /// <param name="requestedName">
    /// Optional name for the extracted graph. When null (or already taken) a free
    /// <c>NewMacro</c>/<c>NewFunction</c>-style name is generated, mirroring <c>editor.create-function</c>.
    /// </param>
    public static CollapsePreparation Prepare(
        BlueprintAsset            asset,
        Graph                     host,
        IReadOnlyCollection<Guid> selection,
        CollapseTarget            target,
        string?                   requestedName = null)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(selection);

        // Macro call nodes inside the selection are resolved through this so the analysis can see a
        // called macro's latency (Q26 / BP1661's shared MacroLatency predicate).
        var macrosById = asset.Graphs
            .Where(g => g.Kind == GraphKind.Macro)
            .GroupBy(g => g.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var analysis = CollapseAnalysis.Analyse(host, selection, target, macrosById);
        if (analysis.IsRefused)
            return CollapsePreparation.Refused(analysis.Refusals);

        var name = UniqueGraphName(asset, requestedName, target);
        var edit = CollapseEmitter.Emit(host, analysis.Plan!, target, name);

        // ⭐ Captured BEFORE the forward runs, and never recaptured — see the class docs.
        var beforeNodes = host.Nodes.ToList();
        var beforeLinks = host.Links.ToList();
        var afterNodes  = edit.RewrittenHost.Nodes.ToList();
        var afterLinks  = edit.RewrittenHost.Links.ToList();

        // ⚠ The host graph OBJECT is mutated in place rather than substituted. BlueprintGraphModel,
        // BlueprintCommandSink and the graph switcher all hold live references to it (BP-24's
        // Retarget contract), so swapping asset.Graphs' entry for CollapseEmitter's new instance
        // would leave the canvas rendering a graph nothing writes to any more.
        void Forward()
        {
            host.Nodes = afterNodes.ToList();
            host.Links = afterLinks.ToList();
            CollapseEmitter.RebuildLinkedToIds(host);
            if (!asset.Graphs.Contains(edit.Extracted))
                asset.Graphs.Add(edit.Extracted);
        }

        void Inverse()
        {
            host.Nodes = beforeNodes.ToList();
            host.Links = beforeLinks.ToList();
            // ⚠ Mandatory, not cosmetic: Pin.LinkedToIds is a denormalised mirror of the link list
            // and the forward rewrote it on the very node objects being restored here. Restoring the
            // lists without rebuilding leaves every surviving host node advertising links to a call
            // node that no longer exists.
            CollapseEmitter.RebuildLinkedToIds(host);
            asset.Graphs.Remove(edit.Extracted);
        }

        return CollapsePreparation.Ready(edit.Extracted, edit.CallNode, Forward, Inverse);
    }

    /// <summary>
    /// Prepares and performs a collapse, reporting a refusal to the designer rather than failing
    /// silently (Q26-B2 — legality is decided <b>on invoke</b>, never by greying the menu item out).
    /// </summary>
    /// <param name="view">
    /// When supplied, the whole gesture is recorded as <b>one</b> undo entry through
    /// <c>GraphView.Execute</c>. When null the forward is applied directly (the sink's path — the
    /// stack has already recorded the pair by the time a sink sees anything).
    /// </param>
    /// <param name="indicators">Refusal surface. Null in headless callers that inspect the result instead.</param>
    /// <returns>The preparation, so callers can assert on the refusal without re-analysing.</returns>
    public static CollapsePreparation Run(
        GraphView?                view,
        BlueprintAsset            asset,
        Graph                     host,
        IReadOnlyCollection<Guid> selection,
        CollapseTarget            target,
        Action?                   markDirty     = null,
        IEditorIndicators?        indicators    = null,
        string?                   requestedName = null)
    {
        var prep = Prepare(asset, host, selection, target, requestedName);

        if (prep.IsRefused)
        {
            indicators?.Notify(prep.ToNotification(host, target));
            return prep;
        }

        var label = target == CollapseTarget.Macro ? "Collapse to Macro" : "Collapse to Function";

        if (view is not null)
            view.Execute(new BlueprintEditCommand(label, prep.Forward!),
                         new BlueprintEditCommand(label, prep.Inverse!),
                         label);
        else
            prep.Forward!();

        markDirty?.Invoke();
        return prep;
    }

    /// <summary>
    /// A free graph name. Honours <paramref name="requested"/> when it is usable and not taken;
    /// otherwise counts up from <c>NewMacro</c>/<c>NewFunction</c>, the same convention
    /// <c>editor.create-function</c>'s quick-add uses.
    /// </summary>
    private static string UniqueGraphName(BlueprintAsset asset, string? requested, CollapseTarget target)
    {
        var existing = new HashSet<string>(
            asset.Graphs.Select(g => g.Name ?? string.Empty), StringComparer.OrdinalIgnoreCase);

        var trimmed = requested?.Trim();
        if (!string.IsNullOrEmpty(trimmed) && !existing.Contains(trimmed!)) return trimmed!;

        var baseName = trimmed;
        if (string.IsNullOrEmpty(baseName))
            baseName = target == CollapseTarget.Macro ? "NewMacro" : "NewFunction";

        for (int i = 1; ; i++)
        {
            var candidate = $"{baseName}{i}";
            if (!existing.Contains(candidate)) return candidate;
        }
    }
}

/// <summary>
/// The outcome of <see cref="BlueprintCollapse.Prepare"/>: either the refusals, or the pair of
/// actions that perform the collapse and undo it.
/// </summary>
internal sealed class CollapsePreparation
{
    private CollapsePreparation(
        IReadOnlyList<CollapseRefusalReason> refusals,
        Graph? created, Node? callNode, Action? forward, Action? inverse)
    {
        Refusals = refusals;
        Created  = created;
        CallNode = callNode;
        Forward  = forward;
        Inverse  = inverse;
    }

    public static CollapsePreparation Refused(IReadOnlyList<CollapseRefusalReason> refusals)
        => new(refusals, null, null, null, null);

    public static CollapsePreparation Ready(Graph created, Node callNode, Action forward, Action inverse)
        => new(Array.Empty<CollapseRefusalReason>(), created, callNode, forward, inverse);

    /// <summary>Empty when the collapse is legal.</summary>
    public IReadOnlyList<CollapseRefusalReason> Refusals { get; }

    /// <summary>The graph the selection would move into. Null when refused.</summary>
    public Graph? Created { get; }

    /// <summary>The call node that replaces the selection in the host graph. Null when refused.</summary>
    public Node? CallNode { get; }

    /// <summary>Applies the collapse. Null when refused. Safe to replay (redo) — see the class docs on <see cref="BlueprintCollapse"/>.</summary>
    public Action? Forward { get; }

    /// <summary>Restores the host graph and drops the created graph. Null when refused.</summary>
    public Action? Inverse { get; }

    public bool IsRefused => Forward is null;

    /// <summary>Every refusal message, joined — what a failed <c>GraphCommandResult</c> carries.</summary>
    public string RefusalMessage => string.Join(" ", Refusals.Select(r => r.Message));

    /// <summary>
    /// The refusal as a toast. ⭐ <b>The body names the offending nodes</b> — a message that only
    /// says "cannot collapse" teaches the designer nothing, which is the whole complaint behind
    /// <c>BP-76</c>.
    /// </summary>
    public EditorNotification ToNotification(Graph host, CollapseTarget target)
    {
        var titles = Refusals
            .SelectMany(r => r.NodeIds)
            .Distinct()
            .Select(id => host.Nodes.FirstOrDefault(n => n.Id == id))
            .Where(n => n is not null)
            .Select(n => DescribeNode(n!))
            .ToList();

        var body = RefusalMessage;
        if (titles.Count > 0)
            body += "  (" + string.Join(", ", titles) + ")";

        return new EditorNotification(
            Id:          Refusals.FirstOrDefault()?.Code ?? "collapse.refused",
            Severity:    NotificationSeverity.Warning,
            Title:       target == CollapseTarget.Macro
                             ? "Cannot collapse to a macro"
                             : "Cannot collapse to a function",
            Body:        body,
            AutoDismiss: TimeSpan.FromSeconds(8),
            Actions:     null);
    }

    /// <summary>
    /// ⚠ Type name, never <c>Title</c>. <c>BP-76</c> is the live example of what keying off a display
    /// title costs — and a short id suffix disambiguates two nodes of the same kind.
    /// </summary>
    private static string DescribeNode(Node node)
        => $"{node.GetType().Name} {node.Id.ToString("N")[..8]}";
}
