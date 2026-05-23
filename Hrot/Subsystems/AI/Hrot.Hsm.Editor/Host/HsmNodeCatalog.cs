using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Host;

/// <summary>
/// Static node catalog for the HSM canvas.
/// Provides one entry per state kind; states have no typed pins.
/// </summary>
internal sealed class HsmNodeCatalog : INodeCatalog
{
    private const string CatStates = "States";

    private static readonly IReadOnlyList<NodeCatalogEntry> _all = BuildEntries();

    private static readonly IReadOnlyList<NodeCategoryDescriptor> _categories =
        new[] { new NodeCategoryDescriptor(CatStates, "States", null) };

    public IReadOnlyList<NodeCatalogEntry> All       => _all;
    public IReadOnlyList<NodeCategoryDescriptor> Categories => _categories;

    public IReadOnlyList<NodeCatalogEntry> Query(NodeSearchQuery q)
    {
        IEnumerable<NodeCatalogEntry> results = _all;

        if (!string.IsNullOrEmpty(q.CategoryFilter))
            results = results.Where(e => e.CategoryPath == q.CategoryFilter);

        if (!string.IsNullOrEmpty(q.Text))
        {
            string text = q.Text.ToLowerInvariant();
            results = results.Where(e =>
                e.DisplayName.ToLowerInvariant().Contains(text) ||
                e.Keywords.Any(k => k.ToLowerInvariant().Contains(text)));
        }

        if (!q.IncludeDeprecated)
            results = results.Where(e => !e.IsDeprecated);

        return results.ToList();
    }

    public IReadOnlyList<NodeCatalogEntry> QueryForPinContext(PinContextQuery q)
        => Array.Empty<NodeCatalogEntry>();

    // ---- Static catalog construction ----

    private static IReadOnlyList<NodeCatalogEntry> BuildEntries() => new[]
    {
        Make(HsmKinds.Simple,      "Simple State",       "A leaf state with no children.",
             new[] { "state", "simple", "leaf" },                   "hsm/state_simple"),
        Make(HsmKinds.Composite,   "Composite State",    "A state that can contain child states.",
             new[] { "state", "composite", "compound" },            "hsm/state_composite"),
        Make(HsmKinds.Parallel,    "Parallel State",     "A state with orthogonal sub-regions.",
             new[] { "state", "parallel", "orthogonal", "fork" },   "hsm/state_parallel"),
        Make(HsmKinds.Final,       "Final State",        "A terminal state; no outgoing transitions allowed.",
             new[] { "state", "final", "terminal", "end" },         "hsm/state_final"),
        Make(HsmKinds.History,     "History State",      "Shallow history pseudo-state.",
             new[] { "state", "history", "shallow" },               "hsm/state_history"),
        Make(HsmKinds.DeepHistory, "Deep History State", "Deep history pseudo-state.",
             new[] { "state", "history", "deep" },                  "hsm/state_deep_history"),
    };

    private static NodeCatalogEntry Make(
        string kindId,
        string displayName,
        string? description,
        string[] keywords,
        string? iconKey) => new(
            new NodeKindKey(kindId),
            displayName,
            description,
            CatStates,
            keywords,
            iconKey,
            IsPure: false,
            IsLatent: false,
            IsDeprecated: false,
            Inputs:  Array.Empty<PinSignature>(),
            Outputs: Array.Empty<PinSignature>());
}
