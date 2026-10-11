extern alias GizmoMapContracts;

using CardText = GizmoMapContracts::Fdp.Toolkit.Diagnostics.Gizmos.FixedString32;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ CE-1033 S5b (docs/DESIGN_Map_3D_Mode.md §3.6, M14) — the gizmo-author sugar for the ENTITY CARD: a small canvas above an
    /// entity that exists because something drew into it (U20 — no header, no special shape). Every call emits ORDINARY primitives
    /// with <see cref="CoordinateSpace.EntityCard"/>, the entity's network id and the row in <c>ZIndex</c> — nothing new on the wire.
    /// <code>
    /// var card = draw.Card(netId, layer);
    /// card.Row(0).Frame(sideColour);                       // row 0 is card-wide: drawn over the whole card
    /// card.Row(10).Bar(hp, Green, DarkGrey);               // widths are fractions of the card
    /// card.Row(20).Text("T-72 #1043", White);
    /// </code>
    /// </summary>
    public static class EntityCards
    {
        /// <summary>The card of the entity with network id <paramref name="networkId"/>, on debug layer <paramref name="layer"/>.</summary>
        public static EntityCard Card(this IDebugDrawBuilder draw, long networkId, byte layer = 0) => new(draw, networkId, layer);
    }

    /// <summary>An entity's card (see <see cref="EntityCards"/>).</summary>
    public readonly struct EntityCard
    {
        internal readonly IDebugDrawBuilder Draw;
        internal readonly long NetworkId;
        internal readonly byte Layer;

        internal EntityCard(IDebugDrawBuilder draw, long networkId, byte layer)
        {
            Draw = draw ?? throw new System.ArgumentNullException(nameof(draw));
            NetworkId = networkId;
            Layer = layer;
        }

        /// <summary>A row: rows stack top-down by <paramref name="order"/>; 0 is card-wide (frame, background).</summary>
        public EntityCardRow Row(byte order) => new(this, order);
    }

    /// <summary>One row of an entity card.</summary>
    public readonly struct EntityCardRow
    {
        private readonly EntityCard _card;
        private readonly byte _order;

        internal EntityCardRow(EntityCard card, byte order) { _card = card; _order = order; }

        private DebugPrimitive New(DebugPrimitiveShape shape)
        {
            var p = default(DebugPrimitive);
            p.Shape = shape;
            p.Space = CoordinateSpace.EntityCard;
            p.AnchorIndex = unchecked((int)_card.NetworkId);
            p.ZIndex = _order;
            p.DebugLayer = _card.Layer;
            p.TargetView = PipelineTarget.All;
            return p;
        }

        /// <summary>A line of text at <paramref name="xPx"/> from the row's left (truncated to 31 characters).</summary>
        public EntityCardRow Text(string text, Rgba32 colour, float sizePx = 13f, float xPx = 0f)
        {
            var p = New(DebugPrimitiveShape.Text);
            p.Color = colour;
            p.TextX = xPx;
            p.TextY = 0f;
            p.TextContent = new CardText(text.Length > 31 ? text.Substring(0, 31) : text);
            p.ThicknessU16 = (ushort)System.Math.Clamp(sizePx, 6f, 64f);
            _card.Draw.EmitRaw(in p);
            return this;
        }

        /// <summary>A bar the card's width: <paramref name="back"/> full width, <paramref name="fill"/> <paramref name="fraction"/> of it.</summary>
        public EntityCardRow Bar(float fraction, Rgba32 fill, Rgba32 back, float heightPx = 4f)
        {
            fraction = System.Math.Clamp(fraction, 0f, 1f);
            Box(0.5f, 0.5f, heightPx, back);
            if (fraction > 0f) Box(fraction / 2f, fraction / 2f, heightPx, fill);
            return this;
        }

        private void Box(float centreFraction, float halfFraction, float heightPx, Rgba32 colour)
        {
            var p = New(DebugPrimitiveShape.Box2D);
            p.SizeMode = SizeMode.ScreenPercent;
            p.Color = colour;
            p.FillColor = colour;
            p.BoxCenterX = centreFraction;
            p.BoxExtentX = halfFraction;
            p.BoxCenterY = heightPx / 2f;
            p.BoxExtentY = heightPx / 2f;
            _card.Draw.EmitRaw(in p);
        }

        /// <summary>Row 0's frame: a translucent background and a one-pixel outline in <paramref name="colour"/>, the whole card.</summary>
        public EntityCardRow Frame(Rgba32 colour, byte backgroundAlpha = 150)
        {
            var back = New(DebugPrimitiveShape.Box2D);
            back.SizeMode = SizeMode.ScreenPercent;
            back.Color = new Rgba32(20, 22, 26, backgroundAlpha);
            back.FillColor = back.Color;
            back.BoxCenterX = back.BoxCenterY = 0.5f;
            back.BoxExtentX = back.BoxExtentY = 0.5f;
            _card.Draw.EmitRaw(in back);

            var outline = back;
            outline.Color = colour;
            outline.FillColor = default;
            outline.ThicknessU16 = 10;   // 1 px
            _card.Draw.EmitRaw(in outline);
            return this;
        }
    }
}
