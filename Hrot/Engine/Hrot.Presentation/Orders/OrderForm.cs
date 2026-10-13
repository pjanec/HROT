using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Editing;
using Hrot.Common.Orders;
using Hrot.Presentation.Behavior;
using Hrot.Presentation.Facades;
using Hrot.UI.Common.Facades;
using ImGuiNET;
using NodeEditor.UI.Picker;
using StructEdit.Core;

namespace Hrot.Presentation.Orders;

/// <summary>
/// ⭐⭐ <c>CE-1059</c> (P5b, F6) — THE order form: a modeless floating window per order the gesture could not complete (or that the
/// operator opened with "Order with options"). Numbers are typed, points and entities are picked on the 2-D / 3-D map or chosen
/// from a searchable list (<see cref="EntityListSource"/>), and Send goes through <see cref="OrderDraft.Send"/> — the one order
/// path. 📄 <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §13.12.
/// <para>⭐ O2 — built by <c>MapInteractionPack</c> on every live map host, it registers itself as the world's <see cref="IOrderForm"/>
/// on its FIRST <see cref="Draw"/>: the form is offered (the unit menu's <c>Name…</c> and "Order with options") exactly where a
/// host draws it, and a headless node — which never draws — never offers it.</para>
/// </summary>
public sealed class OrderForm : IOrderForm
{
    private readonly EntityRepository _world;
    private readonly MapPickBroker _picks;
    private readonly IComponentEditService _edit = ComponentEditServices.CreateBuilder().Build();
    private readonly List<OrderFormEntry> _entries = new();
    private OrderFormEntry? _drawing;   // the entry whose fields are being drawn — its units are left out of its entity list
    private int _next;

    /// <param name="world">The station's world (the form registers itself on it).</param>
    /// <param name="pickService">The host's map pick (<c>MenuOrders.PickService</c>), read at pick time.</param>
    /// <param name="pickers">The map's picker registry (<c>EntityAuthoring.Pickers</c>) for the entity list; null ⇒ no list.</param>
    public OrderForm(EntityRepository world, Func<IMapPickService?> pickService, Func<PickerRegistry?>? pickers = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _picks = new MapPickBroker(pickService ?? throw new ArgumentNullException(nameof(pickService)));
        if (pickers != null) _picks.EntityList = EntityListSource.Opener(() => world, pickers, () => _drawing?.Draft.Units.ToArray());
    }

    /// <summary>The open forms, oldest first.</summary>
    public IReadOnlyList<OrderFormEntry> Entries => _entries;

    /// <summary>The pick context the forms share (each form draws through its own scope of it).</summary>
    public IMapPickContext Picks => _picks;

    /// <summary>True once this form is the world's <see cref="IOrderForm"/>.</summary>
    public bool IsRegistered => _world.HasSingletonManaged<IOrderForm>() && ReferenceEquals(_world.GetSingletonManaged<IOrderForm>(), this);

    /// <inheritdoc/>
    public void Open(OrderDraft draft) => _entries.Add(new OrderFormEntry(++_next, draft, _edit, _world));

    /// <summary>Makes this the world's order form — what <see cref="Draw"/> does on its first call (a host that draws offers it).</summary>
    public void Register()
    {
        if (!IsRegistered) _world.SetSingletonManaged<IOrderForm>(this);
    }

    /// <summary>The scoped pick context of <paramref name="entry"/> (one broker, one pending pick, keyed per form).</summary>
    public IMapPickContext PicksFor(OrderFormEntry entry) => new ScopedMapPickContext(_picks, "$.orderForm" + entry.Id);

    /// <summary>Closes <paramref name="entry"/> without sending.</summary>
    public void Close(OrderFormEntry entry)
    {
        if (_entries.Remove(entry)) entry.Dispose();
    }

    /// <summary>Draws every open form; call once per ImGui frame (<c>MapInteraction.DrawOperatorUi</c>). Registers the form on the
    /// first call.</summary>
    public void Draw()
    {
        Register();
        foreach (var entry in _entries.ToArray())
        {
            bool open = true;
            _drawing = entry;
            try { DrawEntry(entry, ref open); }
            finally { _drawing = null; }
            if (!open || entry.IsDone) Close(entry);
        }
    }

    private void DrawEntry(OrderFormEntry e, ref bool open)
    {
        var units = e.Draft.Units;
        string who = units.Count == 1 ? "unit #" + units[0] : units.Count + " units";
        ImGui.SetNextWindowSize(new Vector2(440, 0), ImGuiCond.FirstUseEver);
        if (ImGui.Begin($"Order: {e.Draft.Order.Name} — {who}##orderForm{e.Id}", ref open))
        {
            BehaviorParamsForm.DrawFields(e.Session, PicksFor(e), "orderFormFields" + e.Id);
            if (e.CanRemember)
            {
                bool remember = e.Remember;
                if (ImGui.Checkbox("Remember as designations", ref remember)) e.Remember = remember;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Store the target / point / route / sector sent as each unit's designation.");
            }
            var missing = e.Missing;
            if (missing.Count > 0)
                ImGui.TextColored(new Vector4(1f, 0.75f, 0.2f, 1f), "Missing: " + string.Join(", ", missing.Select(f => f.Name)));
            ImGui.BeginDisabled(missing.Count > 0);
            if (ImGui.Button("Send")) e.Send();
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) open = false;
            if (e.Results is { } results)
                foreach (var r in results.Where(r => !r.Accepted))
                    ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "Refused: " + r.Detail);
            // ⭐ R-125 spirit — Esc closes the focused form (a modeless window, so only when it has focus).
            if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.IsKeyPressed(ImGuiKey.Escape)) open = false;
        }
        ImGui.End();
    }
}
