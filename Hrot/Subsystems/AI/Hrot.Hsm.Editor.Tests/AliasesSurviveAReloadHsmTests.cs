using System;
using System.Collections.Generic;
using System.Text.Json;
using Fhsm.Kernel.Data;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

/// <summary>
/// ⭐⭐⭐ <b>Batch 91 (<c>91b</c>) — the HSM half: Approach-A aliases survive a RELOAD.</b>
///
/// <para>⭐⭐ <b>Both hosts, because both carried the defect.</b> 📐 <c>HsmAsset</c> has the same
/// <c>_aliases</c> dictionary, the same <c>AddAlias</c>/<c>PruneStaleAliasBindings</c>, and the same
/// missing load path as <c>BehaviorTreeAsset</c> ⇒ ⛔ railing only BTree would leave half the fix
/// unproven. ⭐ The BTree file carries the full diagnosis; this one asserts the same properties on the
/// other host.</para>
///
/// <para>⭐⭐⭐ <b>Through REAL JSON</b>, for the reason stated there: an in-process
/// <c>AddAlias</c>-then-read is the shape that let the defect live.</para>
/// </summary>
public sealed class AliasesSurviveAReloadHsmTests
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private static HsmAsset MakeAsset()
    {
        var root   = new StateNode("__root__");
        var simple = new StateNode("Idle") { IsInitial = true, Parent = root };
        root.Children.Add(simple);

        var asset = new HsmAsset(
            Guid.NewGuid(), "AlphaHsm", "", false, "",
            new HsmDefinitionBlob(),
            new MachineMetadata(),
            root,
            new List<StateNode> { simple },
            new List<TransitionNode>(),
            new List<GlobalTransitionNode>(),
            new List<RegionNode>(),
            new List<EventDefinition>());

        asset.AddVariable(new BlackboardVariableEntry("Health", typeof(float), null));
        return asset;
    }

    /// <summary>⭐ model → DTO → <b>JSON text</b> → DTO → a FRESH model.</summary>
    private static HsmAsset SaveAndReload(HsmAsset asset)
    {
        var dto  = HsmAssetMapper.ToDto(asset);
        var text = JsonSerializer.Serialize(dto, Json);
        var back = JsonSerializer.Deserialize<HsmAssetDto>(text, Json)!;
        return HsmAssetMapper.FromDto(back);
    }

    private static BlackboardAliasBinding Binding(Guid subAssetId, string name = "SubMachine") =>
        new(subAssetId, Guid.NewGuid(), name, "Idle/OnEnter", typeof(int));

    /// <summary>⭐⭐⭐ THE rail, on HSM. 🔴 RED before this batch.</summary>
    [Fact]
    public void AnAliasSurvivesSaveAndReload()
    {
        var asset = MakeAsset();
        var sub   = Guid.NewGuid();
        asset.AddAlias("Health", Binding(sub, "GuardSubMachine"));

        var alias = Assert.Single(SaveAndReload(asset).GetAliasesFor("Health"));

        Assert.Equal(sub,                alias.RequiringAssetId);
        Assert.Equal("GuardSubMachine",  alias.RequiringAssetName);
        Assert.Equal("Idle/OnEnter",     alias.RequiredByPath);
        Assert.Equal(typeof(int),        alias.DtoType);
    }

    /// <summary>⭐⭐ A PERSISTED alias to a deleted sub-asset still prunes — the case that only exists
    /// now that aliases can arrive from disk.</summary>
    [Fact]
    public void APersistedAliasToADeletedSubAssetIsPrunedAfterReload()
    {
        var asset = MakeAsset();
        var kept  = Guid.NewGuid();
        asset.AddAlias("Health", Binding(Guid.NewGuid(), "Deleted"));
        asset.AddAlias("Health", Binding(kept, "StillHere"));

        var reloaded = SaveAndReload(asset);
        Assert.Equal(2, reloaded.GetAliasesFor("Health").Count);

        reloaded.PruneStaleAliasBindings(new[] { kept });

        Assert.Equal("StillHere", Assert.Single(reloaded.GetAliasesFor("Health")).RequiringAssetName);
    }

    /// <summary>⭐⭐⭐ The GOLDEN safety property: no alias ⇒ no key ⇒ the corpus is byte-identical.</summary>
    [Fact]
    public void AnAssetWithNoAliasesEmitsNoAliasesKey()
    {
        var text = JsonSerializer.Serialize(HsmAssetMapper.ToDto(MakeAsset()), Json);

        Assert.DoesNotContain("\"Aliases\"", text, StringComparison.Ordinal);
    }
}
