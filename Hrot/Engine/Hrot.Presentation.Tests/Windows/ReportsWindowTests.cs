using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Toolkit.Squad.Command;
using Hrot.Presentation.Windows;
using Xunit;

namespace Hrot.Presentation.Tests.Windows;

/// <summary>
/// ⭐⭐ <c>CE-1191</c> (UI-6, <c>docs/DESIGN_Command_Display.md</c> §6 D3) — the Reports window's feature suite: the pure model
/// (<see cref="ReportsWindowModel"/>) over a <see cref="SquadReportLog"/> of 20 reports, and the window's dump.
/// ⚠ The ImGui half (the table, the combos) is thin and not driven here; what the window draws is <c>BuildViewModel</c>, which these rails read.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class ReportsWindowTests : IDisposable
{
    private const float Now = 500f;
    private readonly EntityRepository _world = new();
    private readonly SquadReportLog _log = new();
    private readonly Entity[] _units = new Entity[4];
    private static readonly string[] Names = { "Alpha", "Bravo", "Charlie", "Delta" };

    public ReportsWindowTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
        _world.RegisterComponent<EntityInfo>();
        for (int i = 0; i < _units.Length; i++)
        {
            _units[i] = _world.CreateEntity();
            _world.AddComponent(_units[i], new EntityInfo { Name = new FixedString64(Names[i]) });
        }
        // 20 reports, serial 1..20: sender i%4 → addressee (i+1)%4, the kind cycles through five, observed (i+1)*10 s before Now-ish.
        var kinds = new[] { ReportKind.Contact, ReportKind.Spot, ReportKind.Sitrep, ReportKind.Progress, ReportKind.Request };
        for (int i = 0; i < 20; i++)
        {
            var kind = kinds[i % kinds.Length];
            var report = new CommandReport
            {
                Kind = kind, ObservedAt = Now - (i + 1) * 10f, At = new Vector3(100f + i, 200f + i, 0f),
                Subtype = kind == ReportKind.Progress ? (byte)ProgressKind.Set : kind == ReportKind.Request ? (byte)RequestKind.Casevac : (byte)0,
                Count = 6, Size = ElementSize.SquadSize, Activity = ElementActivity.Moving, Equipment = ElementEquipment.MachineGun,
                Synthetic = (byte)(i == 7 ? 1 : 0),
            };
            _log.Add(new SquadReportLogEntry
            {
                SeenAt = 100.0 + i, FromUnit = _units[i % 4], ToUnit = _units[(i + 1) % 4], Report = report, Verdict = i % 2 == 0 ? "folded" : "stale",
            });
        }
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private System.Collections.Generic.IReadOnlyList<ReportRow> Rows(Entity? unit = null, ReportKind? kind = null)
        => ReportsWindowModel.Rows(_world, _log, Now, unit, kind);

    [Fact]
    public void TheLogYieldsEveryReport_NewestFirst()
    {
        var rows = Rows();

        Assert.Equal(20, rows.Count);
        Assert.Equal(Enumerable.Range(1, 20).Reverse().Select(i => (long)i), rows.Select(r => r.Serial));
        Assert.True(rows.Zip(rows.Skip(1), (a, b) => a.SeenAt > b.SeenAt).All(x => x));
    }

    [Fact]
    public void TheUnitFilterKeepsOnlyTheReportsFromOrToThatUnit()
    {
        var bravo = _units[1];

        var rows = Rows(unit: bravo);

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.True(r.From == "Bravo" || r.To == "Bravo"));
        // sender i%4==1 or addressee (i+1)%4==1 (i%4==0) ⇒ 10 of the 20 — and none of the others leaks in
        Assert.Equal(10, rows.Count);
        Assert.Equal(20, Rows(unit: null).Count);
    }

    [Fact]
    public void TheKindFilterKeepsOnlyThatKind()
    {
        var rows = Rows(kind: ReportKind.Spot);

        Assert.Equal(4, rows.Count);
        Assert.All(rows, r => Assert.Equal("Spot", r.Kind));
        // both filters together: Contacts are serials 1, 6, 11, 16 — only 1 (Alpha → Bravo) and 6 (Bravo → Charlie) touch Bravo
        Assert.Equal(new long[] { 6, 1 }, Rows(unit: _units[1], kind: ReportKind.Contact).Select(r => r.Serial));
    }

    [Fact]
    public void ASyntheticReportIsMarked_AndAgeIsNowMinusObservedAt()
    {
        var rows = Rows();

        var synthetic = Assert.Single(rows, r => r.Synthetic);
        Assert.Equal(8, synthetic.Serial);                       // i == 7
        Assert.Equal(80f, synthetic.AgeSeconds);                 // observed 80 s before Now
        foreach (var r in rows) Assert.Equal(r.Serial * 10f, r.AgeSeconds);   // ObservedAt = Now − serial × 10
    }

    [Fact]
    public void ARowCarriesNamesSubtypeAndTheSpotFieldsOnlyForSpotAndContact()
    {
        var contact = Rows(kind: ReportKind.Contact).Last();     // serial 1: Alpha → Bravo
        var progress = Rows(kind: ReportKind.Progress).Last();   // serial 4
        var sitrep = Rows(kind: ReportKind.Sitrep).Last();

        Assert.Equal(("Alpha", "Bravo"), (contact.From, contact.To));
        Assert.NotNull(contact.Spot);
        Assert.Equal((6, "SquadSize", "Moving", "MachineGun"), (contact.Spot!.Count, contact.Spot.Size, contact.Spot.Activity, contact.Spot.Equipment));
        Assert.Contains("SquadSize x6 Moving MachineGun @ (100, 200)", contact.SpotText);
        Assert.Equal("Progress / Set", progress.KindText);
        Assert.Null(progress.Spot);
        Assert.Null(sitrep.Spot);
        Assert.Equal("Sitrep", sitrep.KindText);
    }

    [Fact]
    public void ANullOrEmptyLog_YieldsNoRows()
    {
        Assert.Empty(ReportsWindowModel.Rows(_world, null, Now, null, null));
        Assert.Empty(ReportsWindowModel.Rows(_world, new SquadReportLog(), Now, null, null));
        Assert.Empty(ReportsWindowModel.Units(_world, null));
    }

    [Fact]
    public void TheUnitFilterOffersEveryUnitTheLogMentions_ByName()
    {
        var units = ReportsWindowModel.Units(_world, _log);

        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie", "Delta" }, units.Select(u => u.Name));
    }

    [Fact]
    public void ANamelessUnitShowsAsItsIndex()
    {
        var anon = _world.CreateEntity();

        Assert.Equal($"#{anon.Index}", ReportsWindowModel.NameOf(_world, anon));
        Assert.Equal("—", ReportsWindowModel.NameOf(_world, default));
    }

    [Fact]
    public void TheEmptyLineIsTheOneTheDesignGives()
    {
        Assert.Equal(
            "No report has reached this node yet — reports are logged where the commander's staff runs (brain nodes); see DESIGN_Command_Display §6 D4.",
            ReportsWindowModel.EmptyText);
    }

    // ── the window: what it builds and dumps ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheWindowOverAWorldWithNoLog_BuildsTheEmptyLine()
    {
        var window = new ReportsWindow(() => _world, "reports_test", "Scenario", Vector4.One);   // ⛔ _world has no log yet (Peek never creates)

        var vm = window.BuildViewModel();

        Assert.Empty(vm.Rows);
        Assert.Equal(ReportsWindowModel.EmptyText, vm.EmptyText);
        Assert.Equal(ReportsWindowModel.EmptyText, new ReportsWindow(() => null, "reports_none", "ExCon", Vector4.One).BuildViewModel().EmptyText);
    }

    [Fact]
    public void TheWindowDumpsItsRows_ThroughTheLogOfItsWorld()
    {
        PanelSnapshot.CaptureEnabled = true;
        var log = SquadReportLog.For(_world);
        foreach (var e in _log.Recent(20).Reverse()) log.Add(new SquadReportLogEntry
            { SeenAt = e.SeenAt, FromUnit = e.FromUnit, ToUnit = e.ToUnit, Report = e.Report, Verdict = e.Verdict });
        _world.SetSingletonUnmanaged(new GlobalTime { TotalTime = Now });
        var window = new ReportsWindow(() => _world, "reports_test", "Scenario", Vector4.One);
        Assert.Contains("reports_test", PanelSnapshot.RegisteredPanels);

        window.SimulateDrawClientArea();
        window.UnitFilter = _units[1];
        window.KindFilter = ReportKind.Contact;
        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("reports_test");
        Assert.NotNull(vm);
        Assert.Equal(ReportsWindow.Kind, vm!.PanelKind);
        var dump = vm.Dump();
        Assert.Equal(20, dump["totalLogged"]!.GetValue<long>());
        Assert.Equal("Bravo", dump["unitFilter"]!.GetValue<string>());
        Assert.Equal("Contact", dump["kindFilter"]!.GetValue<string>());
        var rows = dump["rows"]!.AsArray();
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("Contact", r!["kind"]!.GetValue<string>()));
        Assert.Equal(new long[] { 6, 1 }, rows.Select(r => r!["serial"]!.GetValue<long>()));
        Assert.Equal(new float[] { 60f, 10f }, rows.Select(r => r!["ageSeconds"]!.GetValue<float>()));   // the age comes off the world's clock
    }
}
