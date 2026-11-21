using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Presentation.Fonts;
using Fdp.Presentation.Forms;
using Fdp.Toolkit.Aggregate;
using ImGuiNET;

namespace Hrot.Presentation.Forms;

/// <summary>
/// ⭐ <c>CE-8612</c> (docs/designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md §4 UI-7, mockup ② in <c>agg-ui-visuals.svg</c>) — the Details ▸ SLOTS tab of a
/// STOCHASTIC unit: live and read-only. Three gauges — the suppressed share, the morale (the men still standing) and the prepared level — then the slot table:
/// one row per slot (its role's icon, the role, its weapon, health / ammo / stress bars, a flag: suppressed · broken · down) and one per vehicle of its holdings
/// (operational, M-kill, K-kill). A down slot stays listed (its wounds survive a split, C-3). ⛔ Nothing here writes: losses come only from combat.
/// The bars are draw-list rectangles and every text is a cached constant — 0 B per frame.
/// </summary>
public static class SlotsForm
{
    public static readonly FormDecl Decl = new()
    {
        Id = "form.slots", Title = "Slots", Order = 11,
        Requires = new[] { typeof(UnitSlotTable), typeof(AggregateUnit) },
        Offers = IsStochastic,
        Fields = new[]
        {
            new FormField { Label = "Suppressed", Kind = FieldKind.Gauge, Has = IsStochastic, ReadText = (w, e) => SuppressedGauge(w, e).Text, ReadGauge = SuppressedGauge },
            new FormField { Label = "Morale", Kind = FieldKind.Gauge, Has = IsStochastic, ReadText = (w, e) => MoraleGauge(w, e).Text, ReadGauge = MoraleGauge },
            new FormField { Label = "Prepared", Kind = FieldKind.Gauge, Has = IsStochastic, ReadText = (w, e) => PreparedGauge(w, e).Text, ReadGauge = PreparedGauge },
        },
        After = DrawTable,
    };

    public static bool IsStochastic(EntityRepository w, Entity e)
        => w.IsComponentTypeRegistered<UnitSlotTable>() && w.HasComponent<UnitSlotTable>(e) && Hrot.Common.Diagnostics.Gizmos.UnitCardFacts.IsStochastic(w, e);

    public static readonly Vector4 Info = new(0.416f, 0.690f, 1f, 1f), Frame = new(0.23f, 0.23f, 0.25f, 1f), Dim = new(0.55f, 0.55f, 0.58f, 1f);

    // ── the gauges ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static readonly FixedString64[] SuppressedTexts = new FixedString64[101], BrokenTexts = new FixedString64[UnitSlotTable.Capacity + 1];
    private static readonly FixedString64[] PreparedTexts =
    {
        new("Unprepared"), new("Hasty"), new("Prepared"), new("Fortified"),
    };

    public static FormGauge SuppressedGauge(EntityRepository w, Entity e)
    {
        float s = Math.Clamp(w.GetComponentRO<AggregateUnit>(e).SuppressedShare, 0f, 1f);
        int pct = (int)MathF.Round(s * 100f);
        ref var text = ref SuppressedTexts[pct];
        if (text.IsEmpty) text = new FixedString64(string.Create(CultureInfo.InvariantCulture, $"{pct} % of the men under fire"));
        return new FormGauge { Fraction = s, Fill = s >= 0.25f ? UnitFacts.AmberColour : UnitFacts.GreenColour, TickA = 0.25f, TickB = float.NaN, Text = text };
    }

    /// <summary>The men still standing (up and below the break level) over the men up; the text names the broken ones.</summary>
    public static FormGauge MoraleGauge(EntityRepository w, Entity e)
    {
        ref readonly var t = ref w.GetComponentRO<UnitSlotTable>(e);
        int up = 0, broken = 0;
        for (int i = 0; i < t.Count; i++)
        {
            if (!t.IsUp(i)) continue;
            up++;
            if ((float)t.Slots[i].Stress >= StochasticCombat.BreakLevel) broken++;
        }
        float f = up > 0 ? (float)(up - broken) / up : 0f;
        ref var text = ref BrokenTexts[Math.Min(broken, UnitSlotTable.Capacity)];
        if (text.IsEmpty) text = new FixedString64(broken == 1 ? "1 slot broken" : string.Create(CultureInfo.InvariantCulture, $"{broken} slots broken"));
        var fill = f >= 0.9f ? UnitFacts.GreenColour : f >= 0.7f ? UnitFacts.AmberColour : UnitFacts.RedColour;
        return new FormGauge { Fraction = f, Fill = fill, TickA = 0.7f, TickB = 0.9f, Text = text };
    }

    public static FormGauge PreparedGauge(EntityRepository w, Entity e)
    {
        int level = Math.Clamp((int)w.GetComponentRO<AggregateUnit>(e).Prepared, 0, 3);
        return new FormGauge { Fraction = level / 3f, Fill = Info, TickA = float.NaN, TickB = float.NaN, Text = PreparedTexts[level] };
    }

    // ── the table ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A slot's flag: the worst of down · broken · suppressed (stress past half the break level); none = 0.</summary>
    public enum SlotFlag : byte { None, Suppressed, Broken, Down }

    public static SlotFlag FlagOf(in UnitSlotTable t, int i)
    {
        if (!t.IsUp(i)) return SlotFlag.Down;
        float stress = (float)t.Slots[i].Stress;
        return stress >= StochasticCombat.BreakLevel ? SlotFlag.Broken : stress >= 0.5f * StochasticCombat.BreakLevel ? SlotFlag.Suppressed : SlotFlag.None;
    }

    public static string RoleName(SlotRole r) => r switch
    {
        SlotRole.Leader => "Leader", SlotRole.MachineGunner => "MG", SlotRole.AntiTank => "AT", SlotRole.Grenadier => "Grenadier", SlotRole.Marksman => "Marksman",
        SlotRole.Medic => "Medic", SlotRole.Crew => "Crew", SlotRole.Radio => "Radio", SlotRole.Rifleman => "Rifleman", _ => "empty",
    };

    public static string WeaponOf(SlotRole r) => r switch
    {
        SlotRole.MachineGunner => "MG", SlotRole.AntiTank => "ATGM", SlotRole.Grenadier => "Rifle + GL", SlotRole.Marksman => "DMR", SlotRole.Medic => "Rifle, med kit",
        SlotRole.Crew => "Carbine", SlotRole.Empty => "", _ => "Rifle",
    };

    private static (string Glyph, Vector4 Colour) IconOf(SlotRole r) => r switch
    {
        SlotRole.Leader => (IconsFontAwesome6.Crown, UnitFacts.AmberColour),
        SlotRole.Medic => ("", UnitFacts.GreenColour),
        _ => (IconsFontAwesome6.PersonRifle, Info),
    };

    private static readonly string[] Numbers = BuildNumbers();
    private static string[] BuildNumbers() { var n = new string[UnitSlotTable.Capacity + UnitHoldings.Capacity * 8 + 2]; for (int i = 0; i < n.Length; i++) n[i] = (i + 1).ToString(CultureInfo.InvariantCulture); return n; }

    private static readonly Dictionary<long, string> VehicleNames = new();

    private static string VehicleName(EntityRepository w, TkbTypeId type)
    {
        long key = (long)type;
        if (VehicleNames.TryGetValue(key, out var n)) return n;
        n = w.HasSingletonManaged<ITkbDatabase>() && w.GetSingletonManaged<ITkbDatabase>() is { } db && db.TryGetByType(key, out var t) && t != null ? t.Name : "vehicle";
        VehicleNames[key] = n;
        return n;
    }

    /// <summary>The table, after the gauges (the form's <see cref="FormDecl.After"/>): read-only whatever <paramref name="editable"/> says.</summary>
    public static void DrawTable(EntityRepository w, Entity e, bool editable)
    {
        if (!IsStochastic(w, e)) return;
        ref readonly var t = ref w.GetComponentRO<UnitSlotTable>(e);
        var personal = KillerVictimTkb.PersonalSoldiers;
        var db = w.HasSingletonManaged<ITkbDatabase>() ? w.GetSingletonManaged<ITkbDatabase>() : null;
        ImGui.Spacing();
        // the table is read-only by nature: a read-only (live) form's disabled fade would wash its status colours out — draw it at full alpha (still inert)
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 1f);
        if (!ImGui.BeginTable("##slots", 7, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingFixedFit)) { ImGui.PopStyleVar(); return; }
        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 26f);
        ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 118f);
        ImGui.TableSetupColumn("System", ImGuiTableColumnFlags.WidthFixed, 92f);
        ImGui.TableSetupColumn("Health", ImGuiTableColumnFlags.WidthFixed, 80f);
        ImGui.TableSetupColumn("Ammo", ImGuiTableColumnFlags.WidthFixed, 70f);
        ImGui.TableSetupColumn("Stress", ImGuiTableColumnFlags.WidthFixed, 70f);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();
        int row = 0;
        for (int i = 0; i < t.Count; i++, row++)
        {
            ref readonly var s = ref t.Slots[i];
            var flag = FlagOf(t, i);
            bool dim = flag == SlotFlag.Down;
            float health = Math.Clamp((float)s.Health / UnitSplits.SlotFullHealth, 0f, 1f);
            int basic = db != null && personal != null && personal.TryGetValue(s.Role, out var soldier) ? UnitAmmunition.BasicLoad(db, soldier) : 0;
            float ammo = basic > 0 ? Math.Clamp((float)s.Ammo / basic, 0f, 1f) : (s.Ammo > 0 ? 1f : 0f);
            float stress = Math.Clamp((float)s.Stress / StochasticCombat.BreakLevel, 0f, 1f);
            var (glyph, colour) = IconOf(s.Role);
            Row(Numbers[row], glyph, colour, RoleName(s.Role), WeaponOf(s.Role), dim, health, ammo, stress, flag switch
            {
                SlotFlag.Down => ("\uf54c", UnitFacts.RedColour, "down"),
                SlotFlag.Broken => (IconsFontAwesome6.TriangleExclamation, UnitFacts.RedColour, "broken"),
                SlotFlag.Suppressed => (IconsFontAwesome6.TriangleExclamation, UnitFacts.AmberColour, "suppressed"),
                _ => default,
            });
        }
        if (w.IsComponentTypeRegistered<UnitHoldings>() && w.HasComponent<UnitHoldings>(e))
        {
            ref readonly var h = ref w.GetComponentRO<UnitHoldings>(e);
            for (int k = 0; k < h.Count; k++)
            {
                ref readonly var item = ref h.Items[k];
                if (item.Kind != HoldingKind.Vehicle) continue;
                string name = VehicleName(w, item.Type);
                int basic = db != null ? UnitAmmunition.BasicLoad(db, item.Type) : 0;
                float ammoEach = item.Operational > 0 && basic > 0 ? Math.Clamp((float)item.Ammo / ((long)basic * item.Operational), 0f, 1f) : 0f;
                for (int v = 0; v < item.Count; v++, row++)
                {
                    bool ok = v < item.Operational, immobile = ok && v < item.Immobile;
                    Row(Numbers[row], IconsFontAwesome6.VanShuttle, UnitFacts.VehicleColour, name, "", !ok, ok ? 1f : 0f, ok ? ammoEach : 0f, float.NaN,
                        !ok ? ("", UnitFacts.RedColour, "K-kill") : immobile ? (IconsFontAwesome6.TriangleExclamation, UnitFacts.AmberColour, "M-kill") : default);
                }
            }
        }
        ImGui.EndTable();
        ImGui.PopStyleVar();
    }

    private static void Row(string number, string glyph, Vector4 glyphColour, string slot, string system, bool dim, float health, float ammo, float stress,
                            (string? Glyph, Vector4 Colour, string? Word) flag)
    {
        var text = dim ? Dim : ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ImGui.TableNextRow();
        ImGui.TableNextColumn(); ImGui.TextColored(text, number);
        ImGui.TableNextColumn();
        ImGui.TextColored(dim ? glyphColour with { W = 0.45f } : glyphColour, glyph); ImGui.SameLine(); ImGui.TextColored(text, slot);
        ImGui.TableNextColumn(); ImGui.TextColored(dim ? Dim : new Vector4(0.75f, 0.75f, 0.78f, 1f), system);
        ImGui.TableNextColumn(); Bar(health, health >= 0.66f ? UnitFacts.GreenColour : health >= 0.33f ? UnitFacts.AmberColour : UnitFacts.RedColour, 76f);
        ImGui.TableNextColumn(); Bar(ammo, Info, 66f);
        ImGui.TableNextColumn(); if (!float.IsNaN(stress)) Bar(stress, stress < 1f ? UnitFacts.AmberColour : UnitFacts.RedColour, 66f);
        ImGui.TableNextColumn();
        if (flag.Glyph != null) { ImGui.TextColored(flag.Colour, flag.Glyph); ImGui.SameLine(); ImGui.TextColored(flag.Colour, flag.Word!); }
    }

    /// <summary>A bar as two draw-list rectangles (the frame, then the fill) the row's text height — no widget, no text.</summary>
    private static void Bar(float fraction, Vector4 fill, float width)
    {
        var p = ImGui.GetCursorScreenPos();
        float h = ImGui.GetTextLineHeight() * 0.72f, y = p.Y + (ImGui.GetTextLineHeight() - h) * 0.5f;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(new Vector2(p.X, y), new Vector2(p.X + width, y + h), ImGui.GetColorU32(Frame));
        if (fraction > 0f) dl.AddRectFilled(new Vector2(p.X, y), new Vector2(p.X + width * fraction, y + h), ImGui.GetColorU32(fill));
        ImGui.Dummy(new Vector2(width, ImGui.GetTextLineHeight()));
    }
}
