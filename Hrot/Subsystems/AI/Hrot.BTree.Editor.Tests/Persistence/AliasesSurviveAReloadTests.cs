using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Fbt;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Hrot.Editor.AiShared.Blackboard;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Persistence;

/// <summary>
/// ⭐⭐⭐ <b>Batch 91 (<c>91b</c>) — Approach-A aliases survive a RELOAD.</b>
///
/// <para>🔴🔴 <b>The defect, measured.</b> The only writes to <c>_aliases</c> were rename
/// (<c>:406</c>), <c>AddAlias</c> (<c>:501</c>, the drag-drop) and prune (<c>:534</c>). ⛔ <b>Nothing on
/// the LOAD path touched them</b>, and the persistence assembly had no alias field at all. ⇒ ⭐⭐
/// <b>every alias a designer authored was gone when the asset reopened</b> — together with the badge,
/// the type-match decision and the cross-region refusal that guarded it.</para>
///
/// <para>📄 <b>The design already said it persists</b> —
/// <c>BTree_HSM_JSON_Persistence_Detailed_Design.md:132</c>, verbatim: <i>"subtree sync bindings,
/// <b>alias relationships</b>, conflict/unused suppressions … promoted to first-class JSON"</i>.
/// ⚠ Three things in one list; TWO were built. ⇒ ⭐ an OMISSION, not a decision.</para>
///
/// <para>⭐⭐⭐ <b>WHY THESE RAILS GO THROUGH REAL JSON.</b> ⛔ An in-process <c>AddAlias</c>-then-read
/// is <b>exactly the shape that let this defect live</b>: <c>BlackboardAliasingTests</c> does that and
/// has been green throughout. ⇒ every rail below <b>serialises the DTO to a JSON string and parses it
/// back</b> before asserting — the round trip a designer's save/reopen actually performs.</para>
/// </summary>
public sealed class AliasesSurviveAReloadTests
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private static BehaviorTreeBlob EmptyBlob() => new()
    {
        TreeName = "T", Nodes = Array.Empty<NodeDefinition>(),
        MethodNames = Array.Empty<string>(), FloatParams = Array.Empty<float>(),
        IntParams = Array.Empty<int>(), SubtreeAssetIds = Array.Empty<string>(),
    };

    /// <summary>⭐ The REAL round trip: model → DTO → <b>JSON text</b> → DTO → a FRESH model.</summary>
    private static BehaviorTreeAsset SaveAndReload(BehaviorTreeAsset asset)
    {
        var dto  = BehaviorTreeAssetMapper.ToDto(asset);
        var text = JsonSerializer.Serialize(dto, Json);
        var back = JsonSerializer.Deserialize<BehaviorTreeAssetDto>(text, Json)!;
        return BehaviorTreeAssetMapper.FromDto(back);
    }

    /// <summary>⭐ Built the way <c>BTreeSyncPersistenceTests</c> builds one — the closest existing
    /// precedent, so this harness cannot be the thing that differs.</summary>
    private static BehaviorTreeAsset MakeAsset(string name = "Alpha")
    {
        var asset = new BehaviorTreeAsset(
            Guid.NewGuid(), name, $"/trees/{name}.cs", true,
            "Hrot.Game.MasterBlackboard", "Hrot.Game.MasterContext",
            EmptyBlob(), "Hrot.AI.Behaviors.Trees");
        asset.AddVariable(new BlackboardVariableEntry("Health", typeof(float), null));
        return asset;
    }

    private static BlackboardAliasBinding Binding(Guid subAssetId, string subAssetName = "SubTree") =>
        new(subAssetId, Guid.NewGuid(), subAssetName, "Root/Move", typeof(int));

    // ══ THE rail ═════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE rail.</b> 🔴 RED before this batch on both hosts: the reloaded asset had no aliases
    /// at all, because the DTO had no field and the load path had no call.
    /// </summary>
    [Fact]
    public void AnAliasSurvivesSaveAndReload()
    {
        var asset = MakeAsset();
        var sub   = Guid.NewGuid();
        asset.AddAlias("Health", Binding(sub, "PatrolSubTree"));

        var reloaded = SaveAndReload(asset);

        var alias = Assert.Single(reloaded.GetAliasesFor("Health"));
        Assert.Equal(sub,             alias.RequiringAssetId);
        Assert.Equal("PatrolSubTree", alias.RequiringAssetName);
        Assert.Equal("Root/Move",     alias.RequiredByPath);
    }

    /// <summary>
    /// ⭐⭐ <b>The DTO TYPE survives too</b> — ⛔ not just the ids. 📌 The type is what the drag-drop's
    /// type-match decision was made ON, so an alias that reloads without it is an alias whose guard
    /// can no longer be re-checked. ⭐ Resolved through the mapper's EXISTING <c>ResolveClrType</c>.
    /// </summary>
    [Fact]
    public void TheAliasesDtoTypeSurvivesTheRoundTrip()
    {
        var asset = MakeAsset();
        asset.AddAlias("Health", Binding(Guid.NewGuid()));

        var alias = Assert.Single(SaveAndReload(asset).GetAliasesFor("Health"));

        Assert.Equal(typeof(int), alias.DtoType);
    }

    /// <summary>⭐ Several aliases on one variable, and several variables — the map shape, not just a
    /// single happy value.</summary>
    [Fact]
    public void ManyAliasesOnManyVariablesAllSurvive()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("Ammo", typeof(int), null));
        asset.AddAlias("Health", Binding(Guid.NewGuid(), "A"));
        asset.AddAlias("Health", Binding(Guid.NewGuid(), "B"));
        asset.AddAlias("Ammo",   Binding(Guid.NewGuid(), "C"));

        var reloaded = SaveAndReload(asset);

        Assert.Equal(new[] { "A", "B" },
            reloaded.GetAliasesFor("Health").Select(a => a.RequiringAssetName).OrderBy(n => n));
        Assert.Equal("C", Assert.Single(reloaded.GetAliasesFor("Ammo")).RequiringAssetName);
    }

    // ══ prune, AFTER a reload — the handoff's explicit ask ═══════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>A PERSISTED alias to a deleted sub-asset must still prune.</b>
    /// ⚠ <c>PruneStaleAliasBindings</c> is called from <c>BlackboardAuthoringWindow:404</c> and has
    /// only ever seen aliases created in the same session. ⇒ ⛔ railing it in-process would prove
    /// nothing about the case that now exists for the first time: an alias that arrived from DISK.
    /// </summary>
    [Fact]
    public void APersistedAliasToADeletedSubAssetIsPrunedAfterReload()
    {
        var asset = MakeAsset();
        var gone  = Guid.NewGuid();
        var kept  = Guid.NewGuid();
        asset.AddAlias("Health", Binding(gone, "Deleted"));
        asset.AddAlias("Health", Binding(kept, "StillHere"));

        var reloaded = SaveAndReload(asset);
        Assert.Equal(2, reloaded.GetAliasesFor("Health").Count);

        reloaded.PruneStaleAliasBindings(new[] { kept });

        var alias = Assert.Single(reloaded.GetAliasesFor("Health"));
        Assert.Equal("StillHere", alias.RequiringAssetName);
    }

    // ══ the GOLDEN safety property, asserted rather than assumed ═════════════

    /// <summary>
    /// ⭐⭐⭐ <b>An asset with NO alias emits NO <c>Aliases</c> key.</b>
    ///
    /// <para>⛔⛔ This is why the corpus stays byte-identical, and it is a rail rather than a hope.
    /// 📌 The DTO's own <c>ConcurrentWritesAllowed</c> comment states the rule: <i>"a new
    /// ALWAYS-EMITTED list changes the bytes of every asset"</i>, caught by
    /// <c>MigrationEquivalenceTests</c>. ⭐ No shipped asset can contain an alias — they never
    /// persisted — so with <c>WhenWritingNull</c> the field is absent everywhere.</para>
    /// </summary>
    [Fact]
    public void AnAssetWithNoAliasesEmitsNoAliasesKey()
    {
        var text = JsonSerializer.Serialize(BehaviorTreeAssetMapper.ToDto(MakeAsset()), Json);

        Assert.DoesNotContain("\"Aliases\"", text, StringComparison.Ordinal);
    }

    /// <summary>⭐ …and one WITH an alias does emit it — ⛔ without this the rail above would pass on a
    /// field that never serialises at all.</summary>
    [Fact]
    public void AnAssetWithAnAliasDoesEmitTheKey()
    {
        var asset = MakeAsset();
        asset.AddAlias("Health", Binding(Guid.NewGuid()));

        var text = JsonSerializer.Serialize(BehaviorTreeAssetMapper.ToDto(asset), Json);

        Assert.Contains("\"Aliases\"", text, StringComparison.Ordinal);
    }

    /// <summary>⚠ A variable whose alias list was emptied must not leave an empty map behind — ⛔ that
    /// would re-introduce the byte change the nullable field exists to avoid.</summary>
    [Fact]
    public void AnEmptiedAliasListEmitsNoKey()
    {
        var asset = MakeAsset();
        var sub   = Guid.NewGuid();
        asset.AddAlias("Health", Binding(sub));
        asset.PruneStaleAliasBindings(Array.Empty<Guid>());

        var text = JsonSerializer.Serialize(BehaviorTreeAssetMapper.ToDto(asset), Json);

        Assert.DoesNotContain("\"Aliases\"", text, StringComparison.Ordinal);
    }
}
