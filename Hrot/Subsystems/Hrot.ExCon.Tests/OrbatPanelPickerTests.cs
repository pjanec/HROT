using Fdp.Toolkit.Tkb;
using Hrot.ExCon.Logic;
using Hrot.ExCon.Panels;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;
using Hrot.UI.Common.AddEntity;
using Hrot.UI.Common.Panels;
using Moq;
using NodeEditor.UI.Picker;
using Xunit;

namespace Hrot.ExCon.Tests;

/// <summary>
/// ⭐ <c>CE-1017</c> D9 — the ORBAT panel's <i>New Unit…</i> opens the Add Entity picker rooted at the catalog's Units folder; a pick
/// selects the unit type and arms the placement tool. Without a picker it keeps its combo. 📄 docs/DESIGN_Add_Entity_Picker.md D9.
/// </summary>
public sealed class OrbatPanelPickerTests
{
    private static TkbDatabase Tkb()
    {
        var db = new TkbDatabase();
        NedTkbCatalog.RegisterAll(db);
        return db;
    }

    [Fact]
    public void CE1017_NewUnit_OpensThePickerAtUnits_AndAPickArmsThePlacement()
    {
        var tkb = Tkb();
        PickerRequest? req = null; Action<PickerResult>? cb = null;
        var panel = new OrbatPanel { Tkb = () => tkb, OpenPicker = () => (r, c) => { req = r; cb = c; } };
        var logic = new Mock<IExConLogic>();

        Assert.True(panel.HasPicker);
        Assert.True(panel.HandleChooseUnit(logic.Object));
        Assert.Equal("New Unit", req!.Title);
        var items = req.ItemsProvider();
        var leaves = items.Where(e => e.Tag is EntityTypeEntry).ToList();
        Assert.NotEmpty(leaves);
        Assert.All(leaves, e => Assert.StartsWith(EntityTypeCatalog.UnitsFolder, ((EntityTypeEntry)e.Tag!).Category));
        Assert.DoesNotContain(leaves, e => ((EntityTypeEntry)e.Tag!).TkbType == TkbEntityTypes.Truck_HMMWV);

        var pick = leaves.First();
        cb!(new PickerResult(new[] { pick }));

        long type = ((EntityTypeEntry)pick.Tag!).TkbType;
        Assert.Equal(type, panel.SelectedType);
        logic.Verify(l => l.StartPlacementMode(type, It.Is<string>(j => j.Contains("FORCE_FRIENDLY"))), Times.Once);
    }

    [Fact]
    public void CE1017_WithoutAPicker_NewUnitKeepsTheCombo()
    {
        var panel = new OrbatPanel(new[] { new TkbCatalogEntry(303, "Platoon Auto") });
        Assert.False(panel.HasPicker);
        Assert.False(panel.HandleChooseUnit(new Mock<IExConLogic>().Object));
        Assert.Equal(303, panel.SelectedType);
    }
}
