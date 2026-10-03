using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration.Assets;

namespace Hrot.Map.Common.ClusterLoad;

/// <summary>
/// ⭐ <b>Which kinds this node AUTHORS</b> — the CONFIGURED half of the asset tokens (docs/DESIGN_Asset_Management.md
/// §7.3b clause ②, §10 D3). ⛔ Never derived from the host type.
/// <para>⭐ The default is <see cref="AllRooted"/>: a host that has a root for a behaviour kind treats its copy as its own
/// and receives NAS updates ADD-ONLY. That makes a missing setting cost staleness (which the probe reports), never
/// unpublished work. A runtime-only Brain is configured with <see cref="None"/> and receives the full mirror.</para>
/// </summary>
public sealed class AssetAuthoring
{
    private readonly IReadOnlySet<string>? _kinds;   // null = every rooted kind

    private AssetAuthoring(IReadOnlySet<string>? kinds) => _kinds = kinds;

    /// <summary>Authors every behaviour kind it has a root for — the default.</summary>
    public static AssetAuthoring AllRooted { get; } = new(null);

    /// <summary>Authors nothing: receives every needed kind as a mirror (a runtime brain).</summary>
    public static AssetAuthoring None { get; } = new(new HashSet<string>());

    /// <summary>Authors exactly <paramref name="kinds"/> (kind ids, see <see cref="AssetTokens.Kinds"/>).</summary>
    public static AssetAuthoring Of(IEnumerable<string> kinds) => new(new HashSet<string>(kinds, StringComparer.Ordinal));

    /// <summary>Parses the <c>--asset-authoring</c> setting: <c>all</c> (default), <c>none</c>, or a comma list of kinds.</summary>
    public static AssetAuthoring Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)) return AllRooted;
        if (value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) return None;
        return Of(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Select(k => k.ToLowerInvariant()));
    }

    public bool Authors(string kind) => _kinds == null || _kinds.Contains(kind);
}

/// <summary>
/// ⭐⭐ <b>B1/B2 — the asset tokens a node advertises, DERIVED</b> (docs/DESIGN_Asset_Management.md §7.3, §7.3a, §7.3b).
/// <list type="number">
///   <item>⭐ The load-part kinds come from <see cref="RoleLoadRequirements.PartsFor"/> through the 3-row adapter
///     (KnowledgeBase → <c>tkb</c>, Terrain → <c>terrain</c>, ScenarioEntities → <c>scenario</c>) — never a second table.
///     ⛔ An authorship claim NEVER subtracts these (§7.3b, BOUNDED): a load part fails the load without its bytes.</item>
///   <item>⭐ A <c>Brain</c> node also gets every behaviour kind that ANY of its catalog contributors roots on disk
///     (non-null <c>BaseFolder</c> — §7.3a, the ANY aggregation rule). A rootless kind yields NO token.</item>
///   <item>⭐ For each such kind the node advertises its root (§10 D4), and EITHER <c>needs</c> (mirror) OR <c>authors</c>
///     (add-only), by the configured <see cref="AssetAuthoring"/> (§7.3b ①–③).</item>
/// </list>
/// </summary>
public static class AssetNeeds
{
    /// <summary>The §7.3a adapter — the whole of it.</summary>
    public static string KindFor(LoadPart part) => part switch
    {
        LoadPart.KnowledgeBase    => AssetTokens.Kinds.KnowledgeBase,
        LoadPart.Terrain          => AssetTokens.Kinds.Terrain,
        LoadPart.ScenarioEntities => AssetTokens.Kinds.Scenario,
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, "no asset kind for this load part"),
    };

    /// <summary>
    /// ⭐ The feature tokens every ECS host hands its <c>ClusterSlave</c>: <c>fdp.reliable-init</c> plus the load-part asset
    /// tokens for its roles. A Brain host with a behaviour-asset catalog appends <see cref="Tokens"/>' behaviour half
    /// later (<c>ClusterSlave.AppendCapabilities</c>), once the catalog exists.
    /// </summary>
    public static string[] HostCapabilities(NodeRole roles)
        => new[] { Fdp.Toolkit.Replication.CapabilityTokens.ReliableInit }
           .Concat(Tokens(roles, Array.Empty<(string, string?)>(), AssetAuthoring.AllRooted))
           .ToArray();

    /// <param name="roles">The node's roles.</param>
    /// <param name="contributors">Every behaviour-asset catalog contributor on the node: its kind id and its
    /// <c>BaseFolder</c> (null when it has none — an assembly-backed contributor).</param>
    /// <param name="authoring">The configured authorship (§7.3b ②).</param>
    public static IReadOnlyList<string> Tokens(
        NodeRole roles,
        IEnumerable<(string Kind, string? BaseFolder)> contributors,
        AssetAuthoring authoring)
    {
        var tokens = new List<string>();
        foreach (var part in RoleLoadRequirements.PartsFor(roles))
            tokens.Add(AssetTokens.Needs(KindFor(part)));

        if (!roles.HasFlag(NodeRole.Brain)) return tokens;

        // ⭐ ANY non-null BaseFolder roots the kind; the FIRST non-null one is its root. ⛔ Never "all", never "first
        //   registered" — both measured wrong in §7.3a.
        foreach (var group in contributors.GroupBy(c => c.Kind, StringComparer.Ordinal))
        {
            var root = group.Select(c => c.BaseFolder).FirstOrDefault(f => !string.IsNullOrEmpty(f));
            if (root == null) continue;                            // a rootless kind: nothing on disk to sync
            if (AssetTokens.Kinds.LoadPartKinds.Contains(group.Key)) continue;   // load parts are not behaviour kinds

            tokens.Add(AssetTokens.Root(group.Key, root));
            tokens.Add(authoring.Authors(group.Key) ? AssetTokens.Authors(group.Key) : AssetTokens.Needs(group.Key));
        }
        return tokens;
    }
}
