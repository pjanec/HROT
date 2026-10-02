using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Panels;
using Fdp.Toolkit.DER;
using Fdp.Toolkit.Vis2D.Abstractions;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Panels;

/// <summary>
/// ⭐⭐⭐ <b><c>UXI-11</c> slice <c>S-4</c> — RIGHT-CLICK SELECTS, on both inspector panels.</b>
/// 📄 <c>docs/UX/UX_Feature_Selection.md</c> §2.3 (ruled <c>2026-08-12</c>) and §2.7.13 (this slice's
/// as-built).
///
/// <para>§2.3's rows, verbatim: right-click on an <b>already-selected</b> entity leaves the selection
/// <b>unchanged</b>; right-click on an <b>unselected</b> one <b>clears and selects that one</b>.</para>
///
/// <para>⚠ <b>Why a new class rather than an addition to the panels' existing suites</b> (T-1 ④):
/// <c>EntityInspectorPanelDumpsItsSelectionTests</c> and
/// <c>DerEntityInspectorPanelBuildsItsModelTests</c> are <c>PanelSnapshot</c>/projection suites — they
/// own "what the dump carries", not "what a gesture means". §2.3's click semantics had no suite on
/// either panel before this slice; this is it, and it covers both so the two surfaces cannot drift
/// apart unnoticed.</para>
///
/// <para>⛔ <b>What these rails do NOT cover:</b> that the ImGui draw calls <c>RightClick</c> at all.
/// That line is one <c>IsMouseClicked(Right)</c> guard inside a draw and is not reachable headlessly —
/// it is covered by the operator pass, not here. ⭐ Everything the gesture MEANS is below.</para>
/// </summary>
public sealed class TheInspectorPanelsRightClickSelectsTests
{
    // ══ The ECS panel (EntityInspectorPanel) ══════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ §2.3 row 2 — right-clicking an UNSELECTED entity replaces the selection with it. ⚠ A
    /// host-bound panel does not mutate: it publishes a <c>Replace</c> request, because
    /// <c>SelectionRequestSystem</c> is the only writer (§2.7.3 rule 1).
    /// </summary>
    [Fact]
    public void RightClickingAnUnselectedEntity_RequestsThatOneEntity()
    {
        var (e0, e1) = TwoEntities();
        var requests = new List<SelectionChangeRequest>();
        var panel    = BoundPanel(new FakeSelection(e0), requests);
        panel.ProjectHostSelection();

        panel.RightClick(e1);

        var req = Assert.Single(requests);
        Assert.Equal(SelectionChangeMode.Replace, req.Mode);
        Assert.Equal(new[] { e1 }, req.Entities.ToArray());
        Assert.Equal("Inspector.RightClick", req.Reason);
    }

    /// <summary>
    /// ⭐⭐⭐ §2.3 row 1 — right-clicking an entity that is ALREADY SELECTED changes nothing. ⛔ Not
    /// "re-selects it harmlessly": a request would clear the other members of a multi-selection, which
    /// is precisely the behaviour the ruling forbids.
    /// </summary>
    [Fact]
    public void RightClickingASelectedEntity_RequestsNothing()
    {
        var (e0, e1) = TwoEntities();
        var requests = new List<SelectionChangeRequest>();
        var panel    = BoundPanel(new FakeSelection(e0, e1), requests);
        panel.ProjectHostSelection();

        panel.RightClick(e1);

        Assert.Empty(requests);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The rail this slice exists for.</b> The menu's subject is fixed BY THE GESTURE, at open
    /// time — so a right-click on an unselected entity gets a ONE-entity menu even though the host
    /// selection still holds the old two and will not change until next frame.
    ///
    /// <para>⛔ <b>Red-proof:</b> restore <c>selCount</c> to <c>_selectedEntities.Count</c> and this
    /// reads 2 — the "wrong exactly once" menu §2.3 warns about, which S-3e's deferral turned from a
    /// same-frame ordering race into something sequencing could not fix at all.</para>
    /// </summary>
    [Fact]
    public void AfterRightClickingAnUnselectedEntity_TheMenuIsAboutThatOneEntity_NotTheStaleSelection()
    {
        var (e0, e1) = TwoEntities();
        var requests = new List<SelectionChangeRequest>();
        var panel    = BoundPanel(new FakeSelection(e0, e1), requests);
        panel.ProjectHostSelection();
        Assert.Equal(2, panel._selectedEntities.Count);     // ⛔ anti-vacuity: the stale set really is 2

        panel.RightClick(Third());

        Assert.Equal(1, panel.ContextMenuSubjectCount);
        Assert.Equal(2, panel._selectedEntities.Count);     // ⭐ and the view has NOT been written locally
    }

    /// <summary>⭐⭐ Right-clicking inside the selection keeps the MULTI menu.</summary>
    [Fact]
    public void AfterRightClickingASelectedEntity_TheMenuIsAboutTheWholeSelection()
    {
        var (e0, e1) = TwoEntities();
        var panel    = BoundPanel(new FakeSelection(e0, e1), new List<SelectionChangeRequest>());
        panel.ProjectHostSelection();

        panel.RightClick(e0);

        Assert.Equal(2, panel.ContextMenuSubjectCount);
    }

    /// <summary>
    /// ⭐⭐ <b>UNBOUND is still a supported host</b> (§2.7.8: ReplayBrowser inspects a recording, there is
    /// no map to agree with). With no host selection the panel mutates its own set, and §2.3 holds
    /// there too.
    /// </summary>
    [Fact]
    public void WithNoHostSelection_TheRightClickMutatesTheLocalSet()
    {
        var (e0, e1) = TwoEntities();
        var panel = new EntityInspectorPanel();             // ⛔ no Selection, no RequestSelectionChange
        panel._selectedEntities.Add(e0);

        panel.RightClick(e1);

        Assert.Equal(new[] { e1 }, panel._selectedEntities.ToArray());
        Assert.Equal(1, panel.ContextMenuSubjectCount);
    }

    // ══ The DER panel (DerEntityInspectorPanel) ═══════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ §2.3 on the DER surface. ⚠ Addressed by NETWORK id, not <c>Entity</c> — <c>IDerEntity.EntityId</c>
    /// is the <c>EntityMaster</c> id, which is what <c>SelectEntityCommand</c> already carries, so this
    /// seam needed no new event (§2.7.13).
    /// </summary>
    [Fact]
    public void TheDerPanel_RightClickingAnUnselectedRow_RequestsIt_AndASelectedOneRequestsNothing()
    {
        var asked = new List<int>();
        int hostSelected = 7;
        var panel = new DerEntityInspectorPanel
        {
            RequestSelectEntity   = id => asked.Add(id),
            HostSelectedNetworkId = () => hostSelected,
        };

        panel.RightClick(7);                                // ⭐ §2.3 row 1 — already selected
        Assert.Empty(asked);

        panel.RightClick(9);                                // ⭐ §2.3 row 2 — not selected
        Assert.Equal(new[] { 9 }, asked.ToArray());
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Bound, the DER panel is a VIEW</b>: the id it paints and dumps comes from the host, not
    /// from its own field. ⛔ Red-proof: revert <c>EffectiveSelectedId</c> to <c>_selectedEntityId</c>
    /// and this reads <c>NoSelection</c> — the split that <c>UXI-11</c> exists to remove.
    /// </summary>
    [Fact]
    public void TheDerPanel_WhenBound_ReportsTheHostsSelection_NotItsOwn()
    {
        var repo = new DerRepo();
        repo.CreateEntity(1, tkbType: 42);
        repo.CreateEntity(2, tkbType: 42);

        int hostSelected = 2;
        var panel = new DerEntityInspectorPanel
        {
            RequestSelectEntity   = _ => { },
            HostSelectedNetworkId = () => hostSelected,
        };

        // ⛔ anti-vacuity: nothing was ever written to the panel's own field.
        var vm = panel.BuildViewModel(repo, "der-test", "der-entity-inspector");
        Assert.Equal(2, vm.SelectedEntityId);

        hostSelected = 1;
        Assert.Equal(1, panel.BuildViewModel(repo, "der-test", "der-entity-inspector").SelectedEntityId);
    }

    /// <summary>
    /// ⭐⭐ <b>UNBOUND, it still owns its id</b> — a DER viewer over a recording has no global selection,
    /// and that is correct behaviour rather than a fallback (§2.7.8).
    /// </summary>
    [Fact]
    public void TheDerPanel_WhenUnbound_KeepsItsOwnSelection()
    {
        var repo = new DerRepo();
        repo.CreateEntity(1, tkbType: 42);
        var panel = new DerEntityInspectorPanel();

        panel.RightClick(1);

        Assert.Equal(1, panel.BuildViewModel(repo, "der-test", "der-entity-inspector").SelectedEntityId);
    }

    // ══ Helpers ═══════════════════════════════════════════════════════════════

    private static EntityRepository? _repoKeepAlive;

    private static (Entity, Entity) TwoEntities()
    {
        _repoKeepAlive = new EntityRepository();
        return (_repoKeepAlive.CreateEntity(), _repoKeepAlive.CreateEntity());
    }

    private static Entity Third() => _repoKeepAlive!.CreateEntity();

    private static EntityInspectorPanel BoundPanel(ISelectionState selection, List<SelectionChangeRequest> sink)
        => new EntityInspectorPanel
        {
            Selection             = selection,
            RequestSelectionChange = sink.Add,
        };

    /// <summary>A minimal host selection — the truth the panel projects from.</summary>
    private sealed class FakeSelection : ISelectionState
    {
        private readonly List<Entity> _selected;

        public FakeSelection(params Entity[] selected) => _selected = selected.ToList();

        public bool IsSelected(Entity entity) => _selected.Contains(entity);
        public IReadOnlyCollection<Entity> SelectedEntities => _selected;
        public Entity? PrimarySelected
        {
            get => _selected.Count > 0 ? _selected[0] : (Entity?)null;
            set { _selected.Clear(); if (value.HasValue) _selected.Add(value.Value); Version++; }
        }
        public Entity? HoveredEntity { get; set; }
        public int Version { get; private set; }
        public void Add(Entity entity) { if (!_selected.Contains(entity)) _selected.Add(entity); Version++; }
        public void Remove(Entity entity) { _selected.Remove(entity); Version++; }
        public void SetMultiple(IReadOnlyCollection<Entity> entities)
        { _selected.Clear(); _selected.AddRange(entities); Version++; }
        public void Clear() { _selected.Clear(); Version++; }
    }
}
