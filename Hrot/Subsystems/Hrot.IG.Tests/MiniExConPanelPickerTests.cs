using Fdp.Core;
using Fdp.Toolkit.Tkb;
using Hrot.IG.UI;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;
using Hrot.UI.Common.AddEntity;
using NodeEditor.UI.Picker;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// ⭐ <c>CE-1017</c> S4 — IG's Mini ExCon chooses its type with the grouped Add Entity picker instead of a raw TKB
/// number. 📄 docs/DESIGN_Add_Entity_Picker.md S4.
/// </summary>
public sealed class MiniExConPanelPickerTests
{
    [Fact]
    public void CE1017_ThePick_BecomesTheTypeTheSpawnButtonsUse_ForThePanelsSide()
    {
        var tkb = new TkbDatabase();
        NedTkbCatalog.RegisterAll(tkb);
        var state = new MiniExConPanelState(1) { Affiliation = ForceId.Hostile };
        var panel = new MiniExConPanel(state, new FdpEventBus());
        PickerRequest? req = null; Action<PickerResult>? cb = null;
        panel.SetPicker(() => tkb, () => (r, c) => { req = r; cb = c; });

        Assert.True(panel.HandleChooseType());
        Assert.Equal("Add Hostile Entity", req!.Title);

        cb!(new PickerResult(new[] { req.ItemsProvider().Single(e => e.Name == "T-72") }));

        Assert.Equal(TkbEntityTypes.Tank_T72, state.TkbType);
    }

    [Fact]
    public void CE1017_WithoutAPicker_NothingOpens()
    {
        var panel = new MiniExConPanel(new MiniExConPanelState(1), new FdpEventBus());

        Assert.False(panel.HandleChooseType());
    }
}
