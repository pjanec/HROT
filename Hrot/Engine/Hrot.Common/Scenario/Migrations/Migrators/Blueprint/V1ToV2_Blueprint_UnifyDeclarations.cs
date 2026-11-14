using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core.Logging;
using Fdp.Core.Serialization.Migrations;
using Hrot.Blueprints.Core;

namespace Hrot.Common.Scenario.Migrations.Migrators.Blueprint
{
    /// <summary>
    /// Migrates <c>Hrot.Blueprint</c> from v1 to v2 by replacing the three declaration lists with one
    /// tagged array.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Schema change:
    /// v1 — <c>Parameters[] · WorkingState[] · Variables[]</c>, each followed by its <c>*Order</c>.
    /// v2 — one <c>Declarations[]</c> in the first list's slot, every entry carrying <c>Kind</c>.
    /// The three <c>*Order</c> lists survive unchanged; they are display metadata.
    /// </para>
    /// <para>
    /// Round-trip: <b>lossless</b>. <c>v1 → v2 → v1</c> is byte-identical on all 58 shipped assets,
    /// and the down twin (<see cref="V2ToV1_Blueprint_SplitDeclarations"/>) is the revert — a migrated
    /// file stays migrated, so <c>git revert</c> cannot undo this for anything outside the repo.
    /// </para>
    /// <para>
    /// ⭐⭐ <b>The transform itself lives in <c>Hrot.Blueprints.Schema</c>, not here.</b> That assembly
    /// is a BCL-only leaf precisely so this file can reach it: the blueprint compiler already
    /// references <c>Hrot.Common</c>, so the transform could not live there without a cycle. See
    /// Architect Question #31.
    /// </para>
    /// <para>
    /// ⚠⚠ <b>Why this copies rather than mutating in place.</b> <c>BlueprintSchemaV2.Up</c> returns a
    /// <b>rebuilt</b> document, because v2's property ORDER is part of the contract — the union lands
    /// in the first list's slot so <c>Down</c> can restore byte-identical v1 without a hardcoded
    /// schema. ⛔ The pipeline's <b>invariant 1</b> requires the <c>$meta</c> OBJECT INSTANCE to
    /// survive <c>Apply</c>, so it is detached before the transform and re-attached afterwards — the
    /// same instance, not a clone. ⭐ Detaching it first also keeps <c>$meta</c> out of
    /// <c>Up</c>'s canonicality check, which is about declaration lists.
    /// </para>
    /// </remarks>
    internal sealed class V1ToV2_Blueprint_UnifyDeclarations : IJsonDocumentMigrator
    {
        public string DocType => HrotDocumentTypes.Blueprint;
        public int FromVersion => 1;
        public int ToVersion => 2;

        public void Apply(JsonObject root, MigrationContext ctx)
        {
            int count = BlueprintDeclarationRewrite.Apply(root, BlueprintSchemaV2.Up);

            ctx.Report.AddNote($"Unified {count} declaration(s) into the v2 tagged array.");
            FdpLog<V1ToV2_Blueprint_UnifyDeclarations>.Info(
                "Blueprint v1->v2: {0} declaration(s) unified", count);
        }
    }

    /// <summary>
    /// The half both blueprint migrators share: run a <c>BlueprintSchemaV2</c> transform over a
    /// document the pipeline owns, without violating the pipeline's <c>$meta</c> invariants.
    /// </summary>
    internal static class BlueprintDeclarationRewrite
    {
        /// <summary>
        /// Applies <paramref name="transform"/> to <paramref name="root"/> in place and returns how
        /// many declarations were carried across.
        /// </summary>
        internal static int Apply(JsonObject root, System.Func<JsonObject, JsonObject> transform)
        {
            // ⭐ The SAME instance goes back on, so pipeline invariant 1 (`$meta` identity) holds.
            JsonNode? meta = null;
            if (root.ContainsKey("$meta"))
            {
                meta = root["$meta"];
                root.Remove("$meta");   // also detaches it, so it can be re-parented below
            }

            var transformed = transform(root);

            // Snapshot before clearing — the values are cloned, so nothing is double-parented.
            var rewritten = new List<KeyValuePair<string, JsonNode?>>();
            foreach (var property in transformed)
                rewritten.Add(new KeyValuePair<string, JsonNode?>(
                    property.Key, property.Value?.DeepClone()));

            root.Clear();
            if (meta != null) root["$meta"] = meta;
            foreach (var property in rewritten) root[property.Key] = property.Value;

            return CountDeclarations(root);
        }

        private static int CountDeclarations(JsonObject root)
        {
            if (root[BlueprintSchemaV2.DeclarationsProperty] is JsonArray unified)
                return unified.Count;

            int n = 0;
            foreach (var list in new[] { "Parameters", "WorkingState", "Variables" })
                if (root[list] is JsonArray items) n += items.Count;
            return n;
        }
    }
}
