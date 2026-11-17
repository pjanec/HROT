using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Replication.Patching;
using Hrot.Common.Constants;

namespace Hrot.Common.Actions
{
    /// <summary>
    /// ⭐ <c>CE-8615</c> (docs/designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md §4 UI-9, mockup ⑤; agg-backend ST-5 — NOTE_AggBackend_To_Agg_Contract §3d) — the
    /// instructor's de- / re-aggregate REQUEST: the map menu's <b>Resolution ›</b> rows and the one write they make — <see cref="AggregateUnit.Pin"/> through the
    /// attribute path (<c>Aggregate.Pin</c>, CE-8015: the component on the owner and the wire), which the resolution manager obeys over the policy (SV-14).
    /// </summary>
    public static class ResolutionRequests
    {
        /// <summary>An aggregate unit's menu state in one byte (the menu cache keys on it): bit 7 = aggregate · bits 0-1 the effective resolution · 2-3 the pin · 4-6 the split stage.</summary>
        public static byte Pack(EntityRepository w, Entity e)
        {
            if (!w.IsComponentTypeRegistered<AggregateUnit>() || !w.HasComponent<AggregateUnit>(e)) return 0;
            ref readonly var u = ref w.GetComponentRO<AggregateUnit>(e);
            return (byte)(0x80 | ((int)u.Effective & 3) | (((int)u.Pin & 3) << 2) | (((int)u.Splitting & 7) << 4));
        }

        public static AggregateResolution ResolutionOf(byte packed) => (AggregateResolution)(packed & 3);
        public static ResolutionPin PinOf(byte packed) => (ResolutionPin)((packed >> 2) & 3);
        public static SplitStage StageOf(byte packed) => (SplitStage)((packed >> 4) & 7);

        private static string Word(AggregateResolution r) => r switch { AggregateResolution.Stochastic => "stochastic", AggregateResolution.Fusion => "fusion", _ => "physical" };
        private static string PinWord(ResolutionPin p) => p switch
        {
            ResolutionPin.PinKeep => "pinned as it is", ResolutionPin.PinPhysical => "pinned physical", ResolutionPin.PinStochastic => "pinned stochastic", _ => "not pinned",
        };

        /// <summary>The Resolution › group for a unit (null = not an aggregate unit: no row). Each row greyed says why (mockup ⑤).</summary>
        public static Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto? Menu(byte packed)
        {
            if ((packed & 0x80) == 0) return null;
            var res = ResolutionOf(packed); var pin = PinOf(packed); var stage = StageOf(packed);
            string? busy = stage != SplitStage.NotSplitting ? "a change of resolution is in progress" : null;
            Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto Row(int id, string label, string icon, string? whyNot)
                => new() { Id = id, Label = label, Icon = icon, Enabled = whyNot == null, Tooltip = whyNot };
            return new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto
            {
                Label = "Resolution", Icon = "resolution/menu",
                Children = new[]
                {
                    new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto { Label = $"now: {Word(res)} · {PinWord(pin)}", Enabled = false },
                    Row(GlobalActionIds.ResolutionPhysical, "Make physical now", "resolution/split", res == AggregateResolution.Physical ? "already physical" : busy),
                    Row(GlobalActionIds.ResolutionFusion, "Make fusion now", "resolution/fusion", "no fusion request exists (the pin is physical or stochastic)"),
                    Row(GlobalActionIds.ResolutionAggregate, "Aggregate now", "resolution/merge", res == AggregateResolution.Stochastic ? "already stochastic" : busy),
                    new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto { IsSeparator = true },
                    Row(GlobalActionIds.ResolutionPinKeep, "Pin - keep as it is", "resolution/pin", pin == ResolutionPin.PinKeep ? "already pinned" : null),
                    Row(GlobalActionIds.ResolutionUnpin, "Unpin (policy decides)", "resolution/unpin", pin == ResolutionPin.NoPin ? "not pinned" : null),
                },
            };
        }

        /// <summary>The pin a request row writes.</summary>
        public static ResolutionPin? PinFor(int actionId) => actionId switch
        {
            GlobalActionIds.ResolutionPhysical => ResolutionPin.PinPhysical,
            GlobalActionIds.ResolutionAggregate => ResolutionPin.PinStochastic,
            GlobalActionIds.ResolutionPinKeep => ResolutionPin.PinKeep,
            GlobalActionIds.ResolutionUnpin => ResolutionPin.NoPin,
            _ => null,
        };

        /// <summary>Writes <paramref name="pin"/> on <paramref name="unit"/> through the ONE edit path (the Details form's): the owner applies it, the wire carries it.</summary>
        public static EditTicket Request(EntityRepository w, Entity unit, ResolutionPin pin)
            => Edits.Submit(w, EditOp.Write(unit, new EntityAttributeChange { AttributeId = AttributeIds.AggregatePin, Value = AttributeValue.FromInt((int)pin) },
                pin == ResolutionPin.NoPin ? "Unpin resolution" : "Request resolution"));
    }
}
