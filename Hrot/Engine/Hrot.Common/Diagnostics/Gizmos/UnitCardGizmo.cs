using System;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Command;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ <c>CE-8607</c> (docs/designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md §4 UI-11; docs/UX/DESIGN_Unit_Symbols.md P-5, R-8602) — what a UNIT's card says, read
    /// ONCE for both of its writers: the strength (present / authorized + the SITREP colour), the ammunition share and the ONE worst alarm. A stochastic unit is read
    /// from its slot table (⚠ until agg-behaviors' <c>UnitStrength</c> branch lands — RV-5 — then <see cref="UnitStrength"/> answers it and this branch goes);
    /// a physical one through <see cref="UnitStrength"/> and the staff's readers, as its SITREP is. Allocation-free.
    /// </summary>
    public static class UnitCardFacts
    {
        public static readonly Rgba32 Green = new(91, 208, 106, 255), Amber = new(242, 192, 51, 255), Red = new(230, 85, 85, 255), Grey = new(160, 160, 170, 255),
                                      Teal = new(62, 198, 176, 255), Blue = new(106, 176, 255, 255), Violet = new(176, 124, 240, 255), Back = new(58, 58, 58, 220);

        /// <summary>The worst alarm a unit's card names (UI-11's order: broken &gt; suppressed &gt; out of ammo &gt; splitting).</summary>
        public enum Alarm : byte { None, Splitting, OutOfAmmo, Suppressed, Broken }

        public static bool IsStochastic(ISimulationView view, Entity e)
            => (view is not EntityRepository repo || repo.IsComponentTypeRegistered<AggregateUnit>()) && view.HasComponent<AggregateUnit>(e)
               && view.GetComponentRO<AggregateUnit>(e).Effective == AggregateResolution.Stochastic;

        private static bool HasSlots(ISimulationView view, Entity e)
            => (view is not EntityRepository repo || repo.IsComponentTypeRegistered<UnitSlotTable>()) && view.HasComponent<UnitSlotTable>(e);

        /// <summary>The unit's strength: men present, men authorized, the SITREP colour; <c>Known</c> false when nothing says.</summary>
        public static (int Present, int Authorized, StatusColour Colour, bool Known) Strength(ISimulationView view, Entity e)
        {
            if (IsStochastic(view, e) && HasSlots(view, e))
            {
                ref readonly var t = ref view.GetComponentRO<UnitSlotTable>(e);
                int p = t.Present(), a = t.Authorized();
                float f = a > 0 ? (float)p / a : 0f;
                var c = p <= 0 ? StatusColour.Black : f >= StaffComposerSystem.GreenFraction ? StatusColour.Green : f >= StaffComposerSystem.AmberFraction ? StatusColour.Amber : StatusColour.Red;
                return (p, a, c, a > 0);
            }
            var s = UnitStrength.Of(view, e);
            return ((int)s.Present, s.Authorized, UnitStrength.ColourOf(view, e), s.Known);
        }

        /// <summary>The share of its rounds the unit still carries (NaN = not known — a stochastic unit's slots carry rounds, not a capacity).</summary>
        public static float AmmoShare(ISimulationView view, Entity e)
        {
            if (IsStochastic(view, e)) return float.NaN;
            long have = StaffComposerSystem.RoundsOf(view, e, out long max);
            return max > 0 ? (float)have / max : float.NaN;
        }

        /// <summary>The ONE worst alarm, and its number (broken men, or the suppressed share in %).</summary>
        public static (Alarm Kind, int Value) WorstAlarm(ISimulationView view, Entity e)
        {
            int broken = 0, suppressedPct = 0; bool splitting = false; int stage = 0;
            if (IsStochastic(view, e))
            {
                ref readonly var u = ref view.GetComponentRO<AggregateUnit>(e);
                suppressedPct = (int)(u.SuppressedShare * 100f);

                if (HasSlots(view, e))
                {
                    ref readonly var t = ref view.GetComponentRO<UnitSlotTable>(e);
                    for (int i = 0; i < t.Count; i++) if (t.IsUp(i) && (float)t.Slots[i].Stress >= 1f) broken++;
                }
            }
            else if (view.HasComponent<UnitRoster>(e)) broken = BrokenMen(view, e, 0);
            // ⭐ CE-8615 — ANY aggregate unit can be changing resolution (a merging one is still physical): the card says WHICH stage (mockup ⑤ "split 2/3")
            if ((view is not EntityRepository r || r.IsComponentTypeRegistered<AggregateUnit>()) && view.HasComponent<AggregateUnit>(e))
            {
                var st = view.GetComponentRO<AggregateUnit>(e).Splitting;
                splitting = st != SplitStage.NotSplitting; stage = (int)st;
            }
            if (broken > 0) return (Alarm.Broken, broken);
            if (suppressedPct >= 25) return (Alarm.Suppressed, suppressedPct);
            if (!IsStochastic(view, e) && StaffComposerSystem.AmmoOf(view, e) == StatusColour.Black) return (Alarm.OutOfAmmo, 0);
            if (splitting) return (Alarm.Splitting, stage);
            return (Alarm.None, 0);
        }

        private static int BrokenMen(ISimulationView view, Entity unit, int depth)
        {
            if (depth > CombatEffective.MaxDepth) return 0;
            ref readonly var roster = ref view.GetComponentRO<UnitRoster>(unit);
            int n = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (m.IsNull || !view.IsAlive(m)) continue;
                if (view.HasComponent<UnitRoster>(m)) { n += BrokenMen(view, m, depth + 1); continue; }
                if (Fdp.Toolkit.Behavior.Components.Stresses.IsBroken(view, m) && CombatEffective.IsInTheFight(view, m)) n++;
            }
            return n;
        }

        public static Rgba32 ColourOf(StatusColour c) => c switch { StatusColour.Green => Green, StatusColour.Amber => Amber, StatusColour.Red => Red, _ => Grey };

        /// <summary>
        /// ⭐ UI-11 line 3 — the unit's ONE info line on ITS card (row <see cref="EntityCardRows.UnitInfo"/>): <c>present/authorized</c> in the strength colour ·
        /// <paramref name="doing"/> (teal; null = nothing running) · the worst alarm (<c>BROKEN n</c> red · <c>SUPP n%</c> amber · <c>NO AMMO</c> red · <c>SPLIT</c>
        /// blue); a stochastic unit's line starts with a violet <c>~</c> (not physical). Formatted on the stack — 0 B.
        /// </summary>
        public static void InfoRow(EntityCard card, ISimulationView view, Entity e, string? doing)
        {
            var (present, authorized, colour, known) = Strength(view, e);
            var row = card.Row(EntityCardRows.UnitInfo);
            const float Size = 12f, PerChar = Size * 0.5f;
            float x = 0f;
            Span<char> buf = stackalloc char[32];
            if (IsStochastic(view, e)) { row.Text("~", Violet, 13f, x); x += 12f; }
            if (known && present.TryFormat(buf, out int n1) && n1 < 30)
            {
                buf[n1++] = '/';
                authorized.TryFormat(buf[n1..], out int n2);
                row.Text(buf[..(n1 + n2)], ColourOf(colour), Size, x);
                x += (n1 + n2) * PerChar + 4f;
            }
            if (!string.IsNullOrEmpty(doing))
            {
                int len = Math.Min(doing.Length, 18);
                buf[0] = '·'; buf[1] = ' ';
                doing.AsSpan(0, len).CopyTo(buf[2..]);
                row.Text(buf[..(len + 2)], Teal, Size, x);
                x += (len + 2) * PerChar + 6f;
            }
            var (alarm, value) = WorstAlarm(view, e);
            switch (alarm)
            {
                case Alarm.Broken: { "BROKEN ".AsSpan().CopyTo(buf); value.TryFormat(buf[7..], out int k); row.Text(buf[..(7 + k)], Red, Size, x); break; }
                case Alarm.Suppressed: { "SUPP ".AsSpan().CopyTo(buf); value.TryFormat(buf[5..], out int k); buf[5 + k] = '%'; row.Text(buf[..(6 + k)], Amber, Size, x); break; }
                case Alarm.OutOfAmmo: row.Text("NO AMMO", Red, Size, x); break;
                case Alarm.Splitting:
                    // ⭐ CE-8615 (UI-9) — "SPLIT 2/3": the stage of the three (Planning · Materialising · Handover); a merge says MERGE
                    if (value == (int)SplitStage.Merging) row.Text("MERGE", Blue, Size, x);
                    else { "SPLIT ".AsSpan().CopyTo(buf); buf[6] = (char)('0' + Math.Clamp(value, 1, 3)); buf[7] = '/'; buf[8] = '3'; row.Text(buf[..9], Blue, Size, x); }
                    break;
            }
        }
    }

    /// <summary>
    /// ⭐ <c>CE-8607</c> (UI-11 line 1; DESIGN_Unit_Symbols P-5) — a UNIT's card's top: its STRENGTH bar in row 10 (where a man's health bar is — a unit has no
    /// <c>Health</c>), in the SITREP colour with ticks at the AMBER (70 %) and GREEN (90 %) thresholds, and a thin AMMUNITION bar in row 13 (green ≥ 50 %, amber
    /// ≥ 25 %, red). On the Unit cards layer (CE-8605). Physical and stochastic units alike.
    /// </summary>
    [GizmoProjector(typeof(UnitEntity), typeof(NetworkIdentity), MaxMetresPerPixel = CardZoom.FrameMaxMetresPerPixel)]
    public sealed class UnitCardGizmo : IStatelessGizmo
    {
        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            int net = view.GetComponentRO<NetworkIdentity>(entity).Value;
            if (net == 0 || !Seeable.OnMap(view, entity)) return;
            var (present, authorized, colour, known) = UnitCardFacts.Strength(view, entity);
            if (!known || authorized <= 0) return;
            var card = draw.Card(net, DebugTraceLayers.Labels, unitCard: true);
            card.Row(EntityCardRows.UnitStrengthBar).Bar((float)present / authorized, UnitCardFacts.ColourOf(colour), UnitCardFacts.Back, 5f)
                .Tick(StaffComposerSystem.AmberFraction, new Rgba32(220, 220, 225, 255), 7f).Tick(StaffComposerSystem.GreenFraction, new Rgba32(220, 220, 225, 255), 7f);
            float ammo = UnitCardFacts.AmmoShare(view, entity);
            if (!float.IsNaN(ammo))
            {
                var a = ammo >= StaffComposerSystem.AmmoGreenFraction ? UnitCardFacts.Green : ammo >= StaffComposerSystem.AmmoAmberFraction ? UnitCardFacts.Amber : UnitCardFacts.Red;
                card.Row(EntityCardRows.UnitAmmoBar).Bar(ammo, a, UnitCardFacts.Back, 3f);
            }
        }
    }
}
