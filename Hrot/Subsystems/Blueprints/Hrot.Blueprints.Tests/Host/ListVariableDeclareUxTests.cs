using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.Variables;
using Hrot.Blueprints.Editor.Windows;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// FC-2/LV-4 -- the Blueprint-local declare UX for fixed-list variables:
/// <list type="bullet">
///   <item><see cref="BlueprintDocumentFactory.CreateVariable"/> gains capacity/initialLength --
///   Capacity is the discriminator (never IsArray -- F7), InitialLength is clamped to
///   [0, Capacity], and a managed element (String) list is rejected at create time;</item>
///   <item>the created declaration JSON round-trips with Capacity/InitialLength intact (and a
///   scalar's JSON stays byte-compatible -- Capacity omitted when 0);</item>
///   <item>the My Blueprint tree badges a list variable with "[N]".</item>
/// </list>
/// The shared WorkingState panel's <c>BlackboardVariableEntry</c> is deliberately NOT widened
/// (F7) -- this UX is Blueprint-local (the create modal + factory path).
/// </summary>
public sealed class ListVariableDeclareUxTests
{
    private static BlueprintAsset MakeAsset() => new()
    {
        AssetId  = Guid.NewGuid(),
        Name     = "DeclareUx",
        Dispatch = BlueprintDispatchKind.Instance,
    };

    [Fact]
    public void CreateVariable_WithCapacity_DeclaresFixedList_CapacityDiscriminator()
    {
        var asset = MakeAsset();
        var decl = BlueprintDocumentFactory.CreateVariable(
            asset, "MyList", "System.Int32", null, capacity: 4, initialLength: 2);

        Assert.NotNull(decl);
        Assert.Equal(4, decl!.Type.Capacity);
        Assert.Equal(2, decl.Type.InitialLength);
        Assert.False(decl.Type.IsArray);                        // F7: Capacity, never IsArray
        Assert.Equal("System.Int32", decl.Type.TypeId);
    }

    [Fact]
    public void CreateVariable_InitialLength_ClampedIntoCapacityRange()
    {
        var asset = MakeAsset();
        var over  = BlueprintDocumentFactory.CreateVariable(
            asset, "Over", "System.Int32", null, capacity: 4, initialLength: 99);
        var under = BlueprintDocumentFactory.CreateVariable(
            asset, "Under", "System.Int32", null, capacity: 4, initialLength: -3);

        Assert.Equal(4, over!.Type.InitialLength);              // clamped to Capacity
        Assert.Equal(0, under!.Type.InitialLength);             // clamped to 0
    }

    [Fact]
    public void CreateVariable_ManagedElementList_Rejected_ScalarStringStillFine()
    {
        var asset = MakeAsset();

        Assert.Null(BlueprintDocumentFactory.CreateVariable(
            asset, "Bad", "System.String", null, capacity: 4));
        Assert.Empty(asset.Variables);

        // A SCALAR string variable remains legal -- only the list container is fenced.
        Assert.NotNull(BlueprintDocumentFactory.CreateVariable(asset, "Label", "System.String"));
    }

    [Fact]
    public void CreateVariable_ScalarPath_Unchanged_CapacityOmittedFromJson()
    {
        var asset = MakeAsset();
        BlueprintDocumentFactory.CreateVariable(asset, "Speed", "System.Single");
        BlueprintDocumentFactory.CreateVariable(
            asset, "MyList", "System.Int32", null, capacity: 4, initialLength: 2);

        var json = BlueprintJsonServices.Serialize(asset);
        // Scalar JSON stays byte-compatible: Capacity/InitialLength are WhenWritingDefault.
        var speedChunk = json.Substring(json.IndexOf("Speed"), 120);
        Assert.DoesNotContain("Capacity", speedChunk);

        var round = BlueprintJsonServices.Deserialize(json)!;
        var list = round.Variables.Single(v => v.Name == "MyList");
        Assert.Equal(4, list.Type.Capacity);
        Assert.Equal(2, list.Type.InitialLength);
    }

    [Fact]
    public void MyBlueprintTree_ListVariable_ShowsCapacityBadge()
    {
        var asset = MakeAsset();
        BlueprintDocumentFactory.CreateVariable(asset, "Speed", "System.Single");
        BlueprintDocumentFactory.CreateVariable(
            asset, "MyList", "System.Int32", null, capacity: 4, initialLength: 2);

        var model = new BlueprintMyBlueprintModel();
        model.Retarget(new BlueprintEditableAssetAdapter(asset), asset);
        var items = model.GetItems(BlueprintMyBlueprintModel.SectionVariables);

        Assert.Null(items.Single(i => i.DisplayName == "Speed").BadgeText);
        Assert.Equal("[4]", items.Single(i => i.DisplayName == "MyList").BadgeText);
    }

    [Fact]
    public void Modal_BudgetHelper_KnowsEverySelectableUnmanagedElementSize()
    {
        foreach (var typeId in BlueprintTypeSystem.SelectableTypeIds)
        {
            if (typeId == BlueprintTypeSystem.String) continue;  // managed -- fenced in the modal
            Assert.True(VariableCreateModal.ElementByteSize(typeId) > 0,
                $"budget line has no size for selectable element type '{typeId}'");
        }
    }
}
