using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Hrot.Common.Orders;
using Hrot.Presentation.Behavior;
using StructEdit.Core;

namespace Hrot.Presentation.Orders;

/// <summary>
/// ⭐ <c>CE-1059</c> (P5b) — ONE open order form, without ImGui: the draft, an edit session over the order's DTO (THE params form,
/// <see cref="BehaviorParamsForm"/>) seeded from <see cref="OrderDraft.Params"/>, what Send would send, what is still missing,
/// the remember tick and the last send's results. 📄 <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §13.12 O1, O7.
/// </summary>
public sealed class OrderFormEntry : IDisposable
{
    private readonly EntityRepository _world;
    private readonly JsonObject _opened;   // the DTO as the session opened it — a field differing from it was CHANGED

    public OrderFormEntry(int id, OrderDraft draft, IComponentEditService editService, EntityRepository world)
    {
        Id = id;
        Draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        var type = draft.Order.Dto;
        string seed = draft.Params.ToJsonString();
        Session = BehaviorParamsForm.Open(editService ?? throw new ArgumentNullException(nameof(editService)), type, seed);
        _opened = Parse(BehaviorParamsForm.ToJson(BehaviorParamsForm.Hydrate(type, seed), type));
        CanRemember = draft.Order.Fields.Any(f => OrderDesignations.KindOf(f) != null);
    }

    /// <summary>A form id, unique on this station (window id and pick scope).</summary>
    public int Id { get; }

    public OrderDraft Draft { get; }

    /// <summary>The edit session the window draws (<see cref="BehaviorParamsForm.DrawFields"/>).</summary>
    public IEditSession Session { get; }

    /// <summary>⭐ U6 / O7 — on Send, store every designatable field sent as the units' designation (§13.3).</summary>
    public bool Remember { get; set; }

    /// <summary>True when the order has a field a designation can hold (the tick is shown only then).</summary>
    public bool CanRemember { get; }

    /// <summary>The last Send's results, one per unit (null before the first Send).</summary>
    public IReadOnlyList<OrderResult>? Results { get; private set; }

    /// <summary>True once a Send was accepted for every unit — the window closes.</summary>
    public bool IsDone => Results is { Count: > 0 } r && r.All(x => x.Accepted);

    /// <summary>
    /// ⭐ O1 — what Send sends: the draft's params (the gesture, the designations, explicit JSON) plus ONLY the fields the operator
    /// changed. ⛔ Not the whole DTO: a never-picked <c>[Required]</c> point serialises as <c>[0,0]</c>, which
    /// <see cref="OrderMenu.MissingIn"/> would count as set.
    /// </summary>
    public JsonObject Json()
    {
        var json = (JsonObject)Draft.Params.DeepClone();
        var now = Parse(BehaviorParamsForm.ToJson(Session.Commit(), Draft.Order.Dto));
        foreach (var kv in now)
        {
            _opened.TryGetPropertyValue(kv.Key, out var was);
            if (JsonNode.DeepEquals(kv.Value, was)) continue;
            foreach (var key in json.Select(p => p.Key).Where(k => string.Equals(k, kv.Key, StringComparison.OrdinalIgnoreCase)).ToArray())
                json.Remove(key);
            json[kv.Key] = kv.Value?.DeepClone();
        }
        return json;
    }

    /// <summary>The <c>[Required]</c> fields still unset — THE one rule (<see cref="OrderMenu.MissingIn"/>); Send waits for none.</summary>
    public IReadOnlyList<OrderField> Missing => OrderMenu.MissingIn(Draft.Order, Json());

    /// <summary>Sends through <see cref="OrderDraft.Send"/> (the ONE order path, once per unit) when nothing is missing; with
    /// <see cref="Remember"/>, then stores the sent designatable fields for every unit. Returns false when it could not send.</summary>
    public bool Send()
    {
        var json = Json();
        if (OrderMenu.MissingIn(Draft.Order, json).Count > 0) return false;
        Results = Draft.Send(json.ToJsonString());
        if (Remember) RememberDesignations(json);
        return true;
    }

    private void RememberDesignations(JsonObject json)
    {
        var store = OrderDesignations.For(_world);
        foreach (var field in Draft.Order.Fields)
        {
            if (OrderDesignations.KindOf(field) is not { } kind) continue;
            var value = json.FirstOrDefault(p => string.Equals(p.Key, field.Name, StringComparison.OrdinalIgnoreCase)).Value;
            if (value is null || (field.Kind == OrderFieldKind.MapEntity && value.ToJsonString() == "0")) continue;
            foreach (long unit in Draft.Units) store.Set(unit, kind, value);
        }
    }

    public void Dispose() => Session.Dispose();

    private static JsonObject Parse(string json) => JsonNode.Parse(json) as JsonObject ?? new JsonObject();
}
