using System;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 2) — <c>VariablesDetailsView</c> converted to the <c>PanelSnapshot</c>
/// contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example; <c>BP-462</c>.
///
/// <para>⭐ The ADDRESS is composed from the caller's <c>idScope</c> + this view's own
/// <see cref="VariablesDetailsViewDescriptor.ViewId"/> — see the class remarks on
/// <see cref="VariablesDetailsViewPanelViewModel"/> for why.</para>
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class VariablesDetailsViewDumpsItsStateTests : IDisposable
{
    public VariablesDetailsViewDumpsItsStateTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private static VariableDetailsSection MakeSection(bool withContent)
    {
        var section = new VariableDetailsSection(new VariableValueFormatter(RawValueDecoder.Instance));
        if (withContent)
        {
            section.Show("Inputs", new FixedVariableRowSource(new[]
            {
                new VariableRow(
                    Origin:    new VariableRowOrigin(Guid.NewGuid(), new Fdp.Core.Entity(1, 0), "s", "Ammo", "Asset"),
                    ShortName: "Ammo",
                    TypeText:  "Int32",
                    ClrType:   typeof(int),
                    ReadValue: () => new byte[4]),
            }));
        }
        return section;
    }

    [Fact]
    public void BeforeAnyDraw_TheViewIsNotYetInstrumented()
    {
        var view = new VariablesDetailsView(MakeSection(withContent: true));
        Assert.DoesNotContain($"host1/{VariablesDetailsViewDescriptor.ViewId}", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity
        Assert.NotNull(view);
    }

    [Fact]
    public void FirstDraw_DeclaresItInstrumented_AtTheComposedAddress()
    {
        var view = new VariablesDetailsView(MakeSection(withContent: true));
        var addr = $"host1/{VariablesDetailsViewDescriptor.ViewId}";
        Assert.DoesNotContain(addr, PanelSnapshot.RegisteredPanels);

        view.SimulateDraw("host1");

        Assert.Contains(addr, PanelSnapshot.RegisteredPanels);
    }

    [Fact]
    public void AfterABuild_TheDumpCarriesARealField()
    {
        PanelSnapshot.CaptureEnabled = true;
        var view = new VariablesDetailsView(MakeSection(withContent: true));

        var addr = $"host1/{VariablesDetailsViewDescriptor.ViewId}";
        view.SimulateDraw("host1");

        // ⛔ Through PanelSnapshot.TryGet, not the returned local — this is what a disabled Register
        //   call would actually redden (the local vm is always populated regardless).
        var stored = PanelSnapshot.TryGet(addr);
        Assert.NotNull(stored);
        Assert.Equal(addr, stored!.PanelId);
        Assert.Equal(VariablesDetailsViewDescriptor.ViewId, stored.PanelKind);

        var dump = stored.Dump();
        Assert.True(dump["hasContent"]!.GetValue<bool>());
        Assert.Equal("Inputs", dump["heading"]!.GetValue<string>());
    }

    [Fact]
    public void TwoHostsOfTheSameView_StayIndividuallyAddressable()
    {
        PanelSnapshot.CaptureEnabled = true;
        var docked = new VariablesDetailsView(MakeSection(withContent: true));
        var pinned = new VariablesDetailsView(MakeSection(withContent: false));

        docked.SimulateDraw("ai_details_btree_variables");
        pinned.SimulateDraw("details_pin_details.variables_x");

        Assert.True(PanelSnapshot.TryGet($"ai_details_btree_variables/{VariablesDetailsViewDescriptor.ViewId}")!.Dump()["hasContent"]!.GetValue<bool>());
        Assert.False(PanelSnapshot.TryGet($"details_pin_details.variables_x/{VariablesDetailsViewDescriptor.ViewId}")!.Dump()["hasContent"]!.GetValue<bool>());
    }

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing_ButStaysRegistered()
    {
        var view = new VariablesDetailsView(MakeSection(withContent: true));   // CaptureEnabled stays false

        var vm = view.SimulateDraw("host1");

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains($"host1/{VariablesDetailsViewDescriptor.ViewId}", PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);
        Assert.True(vm.HasContent);
    }
}
