using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hrot.Blueprints.Core;

namespace Hrot.Blueprints.Tests.Golden;

/// <summary>
/// <b>U-15 — canonicalise the corpus, and prove the rewrite is a semantic no-op.</b>
///
/// <para>
/// ⭐ <b>Why it exists.</b> <c>U-10</c>'s strongest possible gate is <c>v1 → v2 → v1</c> byte identity,
/// and that is unwritable while the files on disk are not what <c>BlueprintJsonServices</c> writes.
/// ⇒ canonicalise once, deliberately, with the golden harness proving nothing moved.
/// </para>
///
/// <para>
/// ⛔⛔ <b>The risk this file exists to retire:</b> canonicalising means <b>round-tripping every
/// shipped asset through the model</b> — so anything present in the file that the model does not
/// carry is <b>deleted</b>, silently, in 58 files at once. <see cref="CanonicalisingDropsNothing"/>
/// is run <b>before</b> the rewrite, not after.
/// </para>
/// </summary>
public sealed class CorpusCanonicalisationTests
{
    /// <summary>The two roots: the compiled corpus (41) and the editor's recipe templates (16).</summary>
    public static IEnumerable<string> AllManagedFiles()
        => GoldenCorpus.EnumerateFiles().Concat(RecipeFiles());

    public static IReadOnlyList<string> RecipeFiles()
    {
        var dir = ResolveRecipesDir();
        return Directory.GetFiles(dir, "*.bp.json", SearchOption.AllDirectories)
                        .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal)
                        .ToList();
    }

    private static string ResolveRecipesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Blueprints");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Hrot.AI.Behaviors/Recipes/Blueprints not found.");
    }

    private static string Canonical(string json)
        => BlueprintJsonServices.Serialize(
            BlueprintJsonServices.Deserialize(json)
            ?? throw new InvalidDataException("Deserialized null."));

    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The gate that had to run before anything was rewritten.</b> Walks the original document
    /// against its canonical form and reports every property path that is <b>present in the file and
    /// absent from the canonical output</b> — i.e. everything the model does not carry and the rewrite
    /// would therefore delete.
    ///
    /// <para>
    /// ⚠ <b>A model round-trip cannot answer this.</b> <c>Deserialize→Serialize→Deserialize</c> is
    /// stable by construction for anything the model holds; the question here is about what it
    /// <b>does not</b> hold, which only a comparison against the raw document can see.
    /// </para>
    /// </summary>
    /// <summary>
    /// ⭐⭐ <b>The two paths the rewrite IS entitled to drop — enumerated, not waved through.</b>
    ///
    /// <para>
    /// ⛔ <b>Measured before the rewrite: these are the ONLY two, and they appear in 44 of the 58
    /// files.</b> Both were deliberately removed from the model — <c>Header</c> is now an empty class
    /// carrying the comment *"SubsystemType and SchemaVersion removed — <c>$meta</c> envelope carries
    /// this since Phase 2 (D-021)"*. ⭐ They are a **superseded duplicate** of information every one of
    /// the 58 files already carries in its envelope, which
    /// <see cref="EveryFileCarriesTheEnvelopeThatSupersedesThem"/> asserts rather than assumes.
    /// </para>
    ///
    /// <para>
    /// ⚠ Listing them here rather than loosening the comparison keeps the check sharp: a <b>different</b>
    /// path appearing in this report is a real deletion and still reddens.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> DeliberatelyDropped = new(StringComparer.Ordinal)
    {
        "$.Header.SubsystemType", "$.Header.SchemaVersion",
    };

    [Fact]
    public void CanonicalisingDropsNothing()
    {
        var losses = new List<string>();

        foreach (var file in AllManagedFiles())
        {
            var original  = JsonNode.Parse(File.ReadAllText(file), documentOptions: LenientDocument)!;
            var canonical = JsonNode.Parse(Canonical(File.ReadAllText(file)))!;

            var dropped = new List<string>();
            CollectDropped(original, canonical, "$", dropped);
            dropped.RemoveAll(DeliberatelyDropped.Contains);
            if (dropped.Count > 0)
                losses.Add($"{Path.GetFileName(file)}:\n    " + string.Join("\n    ", dropped));
        }

        Assert.True(losses.Count == 0,
            "canonicalising these files would DELETE data the model does not carry:\n  "
            + string.Join("\n  ", losses));
    }

    /// <summary>
    /// ⭐ <b>What makes the two deliberate drops safe, asserted.</b> Every managed file carries a
    /// <c>$meta</c> envelope naming the doc type and schema version — the thing <c>D-021</c> moved
    /// <c>Header.SubsystemType</c> / <c>Header.SchemaVersion</c> into. ⛔ Without this the drop would
    /// rest on a code comment.
    /// </summary>
    [Fact]
    public void EveryFileCarriesTheEnvelopeThatSupersedesThem()
    {
        var missing = new List<string>();
        foreach (var file in AllManagedFiles())
        {
            var root = JsonNode.Parse(File.ReadAllText(file), documentOptions: LenientDocument)!.AsObject();
            if (root.TryGetPropertyValue("$meta", out var meta) && meta is JsonObject m
                && m.ContainsKey("docType") && m.ContainsKey("schemaVersion")) continue;
            missing.Add(Path.GetFileName(file));
        }

        Assert.True(missing.Count == 0,
            "these files have no $meta envelope, so dropping Header.SubsystemType/SchemaVersion "
            + "would lose the information rather than deduplicate it:\n  " + string.Join("\n  ", missing));
    }

    private static readonly JsonDocumentOptions LenientDocument = new()
    {
        AllowTrailingCommas = true,
        CommentHandling     = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Property paths present in <paramref name="before"/> and missing from <paramref name="after"/>.
    /// ⚠ Deliberately one-directional: canonicalisation <b>adding</b> a default-valued property is
    /// fine; losing one is not.
    /// </summary>
    private static void CollectDropped(JsonNode? before, JsonNode? after, string path, List<string> into)
    {
        switch (before)
        {
            case JsonObject bo:
                if (after is not JsonObject ao) { into.Add($"{path} (object became {Describe(after)})"); return; }
                foreach (var (key, child) in bo)
                {
                    // ⭐ $meta is rewritten by the envelope writer on purpose — that is the one
                    //   property canonicalisation is entitled to replace rather than preserve.
                    if (key == "$meta") continue;
                    if (!ao.TryGetPropertyValue(key, out var mirror)) { into.Add($"{path}.{key}"); continue; }
                    CollectDropped(child, mirror, $"{path}.{key}", into);
                }
                return;

            case JsonArray ba:
                if (after is not JsonArray aa) { into.Add($"{path} (array became {Describe(after)})"); return; }
                if (aa.Count < ba.Count) { into.Add($"{path} (length {ba.Count} -> {aa.Count})"); return; }
                for (int i = 0; i < ba.Count; i++) CollectDropped(ba[i], aa[i], $"{path}[{i}]", into);
                return;

            case null:
                return;

            default:
                // ⚠ Scalars compare by MEANING, not by spelling: BP-227's whole point is that
                //   Dispatch: 1 and "AiPrimitive" are the same value written two ways, and
                //   normalising the spelling is what this task is FOR.
                if (after is null) { into.Add($"{path} (value dropped)"); return;}
                return;
        }
    }

    private static string Describe(JsonNode? n)
        => n is null ? "null" : n.GetType().Name;

    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>The durable gate:</b> every managed asset on disk already IS its canonical form, so the
    /// corpus cannot silently drift back. ⛔ Without this, <c>U-10</c>'s byte-identity gate decays the
    /// first time anyone hand-edits an asset.
    /// </summary>
    [Fact]
    public void EveryManagedAssetIsAlreadyCanonical()
    {
        var drifted = new List<string>();
        foreach (var file in AllManagedFiles())
        {
            var text = File.ReadAllText(file);
            if (!string.Equals(Normalize(text), Normalize(Canonical(text)), StringComparison.Ordinal))
                drifted.Add(Path.GetFileName(file));
        }

        Assert.True(drifted.Count == 0,
            $"{drifted.Count} asset(s) are not in canonical form:\n  " + string.Join("\n  ", drifted)
            + "\n\nRe-run the canonicalisation: BLUEPRINT_CANONICALISE=1 dotnet test --filter "
            + "FullyQualifiedName~Canonicalise_Rewrite");
    }

    /// <summary>Line endings are a checkout setting, not a canonical-form property.</summary>
    private static string Normalize(string s) => s.Replace("\r\n", "\n");

    /// <summary>
    /// ⭐⭐ <b>The rewrite itself — guarded, and it is the one-time operation `U-15` is.</b> Kept in
    /// the tree rather than run as a throwaway script so the exact transform that produced the tree is
    /// the one that checks it, and so a later format change can be replayed the same way.
    /// </summary>
    [Fact]
    public void Canonicalise_Rewrite()
    {
        if (Environment.GetEnvironmentVariable("BLUEPRINT_CANONICALISE") != "1")
            return;   // ⚠ inert unless asked — this writes to the source tree

        var rewritten = 0;
        foreach (var file in AllManagedFiles())
        {
            var text      = File.ReadAllText(file);
            var canonical = Canonical(text);
            if (string.Equals(Normalize(text), Normalize(canonical), StringComparison.Ordinal)) continue;
            File.WriteAllText(file, canonical);
            rewritten++;
        }

        Assert.True(rewritten >= 0);
    }

    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b><c>BP-227</c> settles here — and the count was wrong twice.</b>
    ///
    /// <para>
    /// The tracker says <b>7</b> files carry a numeric <c>Dispatch</c> (4 corpus + 3 recipes), itself a
    /// *"corrected"* figure from the Batch 40 review. ⛔ **Measured: 11** — 4 in the corpus and
    /// <b>7</b> in the recipes, because the recipes carry <b>both</b> <c>1</c> and <c>2</c> and only
    /// <c>1</c> was ever counted. ⚠ The undercount happened by the same mechanism as the defect: a
    /// search for one spelling of a field that has two.
    /// </para>
    ///
    /// <para>
    /// ⭐ After canonicalisation there is exactly one spelling, asserted here rather than described.
    /// </para>
    /// </summary>
    [Fact]
    public void NoManagedAssetSpellsDispatchAsANumber()
    {
        var numeric = new List<string>();
        foreach (var file in AllManagedFiles())
        {
            var root = JsonNode.Parse(File.ReadAllText(file), documentOptions: LenientDocument)!.AsObject();
            if (root.TryGetPropertyValue("Dispatch", out var d) && d is JsonValue v
                && v.TryGetValue<JsonElement>(out var e) && e.ValueKind == JsonValueKind.Number)
                numeric.Add(Path.GetFileName(file));
        }

        Assert.True(numeric.Count == 0,
            "BP-227: these assets still spell Dispatch as a number:\n  " + string.Join("\n  ", numeric));
    }

    /// <summary>
    /// The scope, asserted so it cannot quietly shrink: 41 compiled + 16 recipes.
    /// ⚠ Fixtures are deliberately excluded — several are malformed on purpose and a fixture's bytes
    /// are frequently the thing under test.
    /// </summary>
    [Fact]
    public void TheManagedScopeIsTheCorpusPlusTheRecipes()
    {
        // ⭐ 42 → 43 in Batch 60: `LayoutAlignmentWitness` (PA-14) — see EmittedStateLayoutTests.
        // ⭐ 43 → 44 in the Q43 build: `ParamResolverDemo`, the first Construction-graph asset.
        // ⭐ 44 → 45 in the R4 build: `ResolverWorldReachDemo`.
        // ⭐ 45 → 46 in E8a: `OwnParamResolverDemo`.
        // ⭐ 47 → 48 in CE-397: `HsmGuardDemo` — the FIRST AiPrimitive in the corpus hosted as an
        //    `HsmGuard`. Measured before it: 34 BTreeAction, 9 BTreeCondition, 2 HsmAction and ZERO
        //    HsmGuard, so the HSM GUARD path had no asset and acceptance rail 4 of
        //    DESIGN_Hsm_Blueprint_Behaviour_Authoring.md could not be closed by existing content.
        // ⭐ 48 → 50 for the Q74 §8.4 end-to-end subject: `HsmDriveActivity` and `HsmFireActivity`,
        //    the first AiPrimitives in the corpus hosted as `HsmAction` AND commanding a channel.
        //    Before them no HSM asset named a blueprint activity at all, so D-B1's auto-bind had
        //    no subject outside a test fixture — and the live run against them found CE-407.
        // ⛔ 50 → 44 (CE-436, 2026-09-29): the six HillAssault2I_* integrated blueprints were
        //    DELETED with PlatoonHillAttack2 (user: "not needed"). ⚠ The sixty HillAssault2_*
        //    TWINS are KEPT — the two families differ by ONE LETTER, so a sweep that matches
        //    `HillAssault2` matches both.
        // ⭐ 44 → 45 (CE-428, 2026-09-29): `T40Resolver` — the FIRST behaviour RESOLVER asset (shape ③, a Library
        //    with a ResolverSubject). Before it no .bp.json could be bound to a behaviour at all (Q76 §12.20).
        // ⛔ 45 → 43 (CE-440, 2026-09-30): `SharedStateRallyDemo` and `SharedStateCrossEntityDemo` were DELETED
        //    with the GetShared/SetShared node pair they demonstrated (decision A, Q76 §12.24).
        // ⛔ 43 → 42 (CE-445, 2026-09-30): `OwnParamResolverDemo` DELETED — an AiPrimitive's own resolver is
        //    retired (R-155: only behaviours have resolvers; DESIGN_Parameter_Model §P.4).
        // ⛔ 42 → 40 (CE-448, 2026-09-30): `ParamResolverDemo` and `ResolverWorldReachDemo` DELETED — reusable
        //    Library resolvers are retired (R-155: a resolver is the ONE stage a behaviour names).
        // ⭐ 40 → 41 (CE-446, 2026-09-30): `BlueprintBehaviourDemo` — the FIRST blueprint BEHAVIOUR (Dispatch = Behavior).
        // ⭐ 41 → 42 (CE-464, 2026-09-30): `PlatoonHillAttackBp` — the hill-attack commander as ONE blueprint behaviour.
        // ⛔ 42 → 28 (2026-10-01): the 15 `HillAssault2_*` twins retired (user: "retire the old superseded non elegant
        //    blueprint version"); 14 deleted, `_ReverseToBaseline` RENAMED `ChannelMoveAndWaitDemo` — the only production
        //    WaitForChannel asset left (R-137).
        // ⭐ 28 → 29 (CE-503, 2026-10-02): `HsmResolverDemoResolver` — the FIRST resolver asset bound by an HSM
        //    (Q76 §12.27g); before it only BTrees could bind one.
        // ⭐ 29 → 35 (CE-2021, 2026-10-02): the §7 demos of DESIGN_Unified_Behaviour_Run — `Demo_TaskChain` and
        //    `Demo_MissionPlan` (Behaviour Task chains, While Running abort, a task run ALONGSIDE) and their four step
        //    children `Demo_Advance` · `Demo_Engage` · `Demo_Retreat` · `Demo_TakeCover`.
        // ⭐ 35 → 36 (CE-3079 H7 / CE-3078, 2026-10-06): `DangerCrossingBp` — the FIRST asset built from the per-kind
        //    sensor nodes (SpawnSensor · When SensorResult · ReadSensorResult), the DangerCrossing BTree as a blueprint.
        // ⭐ 36 → 37 (CE-3083 G5, 2026-10-06): `CombatPostureBp`.
        Assert.Equal(37, GoldenCorpus.EnumerateFiles().Count);
        Assert.Equal(16, RecipeFiles().Count);
    }
}
