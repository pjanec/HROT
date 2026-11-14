using System;
using System.Collections.Generic;

namespace Fdp.Toolkit.Orchestration.Assets
{
    /// <summary>
    /// ⭐⭐ <b>The asset-distribution capability tokens</b> (docs/DESIGN_Asset_Management.md §7.3a/§7.3b, <c>Q72-B</c>/<c>Q72-J</c>)
    /// — advertised once at join on the node's capability descriptor, beside <c>fdp.role.*</c> (AQ-70: unknown tokens are
    /// ignored, absence = unsupported, so an older node simply receives nothing new).
    /// <list type="bullet">
    ///   <item><c>hrot.asset.needs.&lt;kind&gt;</c> — stage this kind to me as a MIRROR of NAS.</item>
    ///   <item><c>hrot.asset.authors.&lt;kind&gt;</c> — I author this kind: never mirror it over me, only ADD files I lack (§7.3b ③).</item>
    ///   <item><c>hrot.asset.root.&lt;kind&gt;=&lt;path&gt;</c> — where this kind lives on my disk (§10 D4) — only for the
    ///     behaviour kinds; the load-part kinds land in the node's staging root, which the orchestrator already knows.</item>
    /// </list>
    /// </summary>
    public static class AssetTokens
    {
        public const string NeedsPrefix = "hrot.asset.needs.";
        public const string AuthorsPrefix = "hrot.asset.authors.";
        public const string RootPrefix = "hrot.asset.root.";

        /// <summary>The kind ids. Load-part kinds (§7.3a adapter) and the behaviour kinds (lower-case <c>AssetKind</c>).</summary>
        public static class Kinds
        {
            public const string KnowledgeBase = "tkb";
            public const string Terrain = "terrain";
            public const string Scenario = "scenario";
            public const string Blueprint = "blueprint";
            public const string BTree = "btree";
            public const string Hsm = "hsm";

            /// <summary>The kinds that arrive through a load part — ⛔ an authorship claim never subtracts these (§7.3b, BOUNDED).</summary>
            public static readonly IReadOnlyCollection<string> LoadPartKinds = new[] { KnowledgeBase, Terrain, Scenario };

            /// <summary>The kind id of an <c>AssetKind</c> enum name (<c>Blueprint</c> → <c>blueprint</c>).</summary>
            public static string FromAssetKindName(string name) => name.ToLowerInvariant();
        }

        public static string Needs(string kind) => NeedsPrefix + kind;
        public static string Authors(string kind) => AuthorsPrefix + kind;
        public static string Root(string kind, string path) => RootPrefix + kind + "=" + path;

        /// <summary>Reads a node's advertised tokens into what the sync needs to know about it.</summary>
        public static NodeAssetProfile Parse(IEnumerable<string>? tokens)
        {
            var needs = new HashSet<string>(StringComparer.Ordinal);
            var authors = new HashSet<string>(StringComparer.Ordinal);
            var roots = new Dictionary<string, string>(StringComparer.Ordinal);
            if (tokens != null)
            {
                foreach (var t in tokens)
                {
                    if (string.IsNullOrEmpty(t)) continue;
                    if (t.StartsWith(NeedsPrefix, StringComparison.Ordinal)) needs.Add(t.Substring(NeedsPrefix.Length));
                    else if (t.StartsWith(AuthorsPrefix, StringComparison.Ordinal)) authors.Add(t.Substring(AuthorsPrefix.Length));
                    else if (t.StartsWith(RootPrefix, StringComparison.Ordinal))
                    {
                        var rest = t.Substring(RootPrefix.Length);
                        int eq = rest.IndexOf('=');
                        if (eq > 0 && eq < rest.Length - 1) roots[rest.Substring(0, eq)] = rest.Substring(eq + 1);
                    }
                }
            }
            return new NodeAssetProfile(needs, authors, roots);
        }
    }

    /// <summary>One node's asset tokens, parsed.</summary>
    public sealed record NodeAssetProfile(
        IReadOnlySet<string> Needs, IReadOnlySet<string> Authors, IReadOnlyDictionary<string, string> Roots)
    {
        /// <summary>
        /// How the NAS→node sync treats <paramref name="kind"/> on this node: ⭐ a needed kind is MIRRORED; an authored kind
        /// is ADD-ONLY (§7.3b ③ — even if a needs token were also present, authorship wins for a non-load-part kind);
        /// anything else is not sent. ⛔ A load-part kind is never degraded by an authorship claim (BOUNDED).
        /// </summary>
        public AssetSyncMode? ModeFor(string kind)
        {
            bool loadPart = ((ICollection<string>)AssetTokens.Kinds.LoadPartKinds).Contains(kind);
            if (!loadPart && Authors.Contains(kind)) return AssetSyncMode.AddOnly;
            if (Needs.Contains(kind)) return AssetSyncMode.Mirror;
            return null;
        }
    }
}
