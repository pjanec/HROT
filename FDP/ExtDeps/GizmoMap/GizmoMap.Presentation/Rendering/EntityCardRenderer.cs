using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Raylib_cs;

namespace GizmoMap.Presentation
{
    /// <summary>
    /// ⭐⭐ CE-1033 S5b (docs/DESIGN_Map_3D_Mode.md §3.6, M14) — ONE renderer for entity cards, in 2-D and 3-D: it groups the frame's
    /// <see cref="CoordinateSpace.EntityCard"/> primitives by entity, stacks their rows by <c>ZIndex</c> (row 0 card-wide), measures
    /// the card, places it above the entity's screen point — which the CAMERA answers, so the renderer never asks which mode it is
    /// in — and draws a leader line down to the entity in the colour of the card's outline.
    /// <para>Layer mask and LOD are applied BEFORE this (by the map's own renderer / triage). Unclickable (U21).</para>
    /// </summary>
    public sealed class EntityCardRenderer
    {
        public const float MinWidth = 72f, Padding = 4f, RowGap = 2f, Leader = 20f;

        /// <summary>One item placed in a card: the primitive and the top-left of its row (screen pixels) and the row's height.</summary>
        public readonly record struct Item(DebugPrimitive Primitive, Vector2 RowOrigin, float RowHeight, bool CardWide);

        /// <summary>A laid-out card: where it is, where its entity is, what is in it.</summary>
        public sealed record Card(long NetworkId, Rectangle Rect, Vector2 Anchor, List<Item> Items, Color LeaderColour);

        /// <summary>Cards drawn in the last <see cref="Draw"/>.</summary>
        public int CardsDrawn { get; private set; }

        /// <summary>Card primitives of shapes a card cannot hold (Sphere, Arrow, SemanticShape, …) — counted, not drawn.</summary>
        public ShapeCounters Skipped { get; } = new();

        /// <summary>
        /// Lays out the cards of <paramref name="primitives"/> (only <see cref="CoordinateSpace.EntityCard"/> ones are read).
        /// <paramref name="anchorScreen"/> answers an entity's screen point (null = not on screen / unknown ⇒ no card);
        /// <paramref name="measureText"/> the pixel width of a text at a size. Pure — railable without a window.
        /// </summary>
        public List<Card> Layout(IEnumerable<DebugPrimitive> primitives, Func<long, Vector2?> anchorScreen, Func<string, float, float> measureText)
        {
            Skipped.Reset();
            var byEntity = new Dictionary<long, List<DebugPrimitive>>();
            var order = new List<long>();
            foreach (var p in primitives)
            {
                if (p.Space != CoordinateSpace.EntityCard) continue;
                if (!Allowed(p.Shape)) { Skipped.Add(p.Shape); continue; }
                long id = p.AnchorIndex;
                if (!byEntity.TryGetValue(id, out var list)) { byEntity[id] = list = new List<DebugPrimitive>(); order.Add(id); }
                list.Add(p);
            }

            var cards = new List<Card>();
            foreach (long id in order)
            {
                if (anchorScreen(id) is not { } anchor) continue;
                var prims = byEntity[id];
                var rows = new SortedDictionary<byte, List<DebugPrimitive>>();
                var wide = new List<DebugPrimitive>();
                foreach (var p in prims)
                {
                    if (p.ZIndex == 0) { wide.Add(p); continue; }
                    if (!rows.TryGetValue(p.ZIndex, out var r)) rows[p.ZIndex] = r = new List<DebugPrimitive>();
                    r.Add(p);
                }

                float width = MinWidth - 2f * Padding, height = 0f;
                var heights = new List<float>();
                foreach (var r in rows.Values)
                {
                    float h = 0f;
                    foreach (var p in r)
                    {
                        h = MathF.Max(h, HeightOf(p));
                        width = MathF.Max(width, WidthOf(p, measureText));
                    }
                    heights.Add(h);
                    height += h;
                }
                height += Math.Max(0, heights.Count - 1) * RowGap;
                float cardW = width + 2f * Padding, cardH = MathF.Max(height + 2f * Padding, 2f * Padding + 4f);
                var rect = new Rectangle(anchor.X - cardW / 2f, anchor.Y - Leader - cardH, cardW, cardH);

                var items = new List<Item>();
                foreach (var p in wide) items.Add(new Item(p, new Vector2(rect.X, rect.Y), cardH, true));
                float y = rect.Y + Padding;
                int ri = 0;
                foreach (var r in rows.Values)
                {
                    foreach (var p in r) items.Add(new Item(p, new Vector2(rect.X + Padding, y), heights[ri], false));
                    y += heights[ri] + RowGap;
                    ri++;
                }

                var leader = new Color((byte)170, (byte)170, (byte)170, (byte)220);
                foreach (var p in wide)
                    if (p.Shape == DebugPrimitiveShape.Box2D && p.ThicknessU16 > 0 && p.Color.A > 0)
                        leader = new Color(p.Color.R, p.Color.G, p.Color.B, p.Color.A);
                cards.Add(new Card(id, rect, anchor, items, leader));
            }
            return cards;
        }

        /// <summary>Lays out and draws the cards (screen space — call outside any camera mode).</summary>
        public void Draw(IEnumerable<DebugPrimitive> primitives, Func<long, Vector2?> anchorScreen)
        {
            var cards = Layout(primitives, anchorScreen, (s, px) => Raylib.MeasureTextEx(GizmoTextDraw.Font, s, px, 1f).X);
            foreach (var card in cards)
            {
                // row 0 backgrounds first, then the rows, then row 0's outlines — the frame is on top, the background behind
                foreach (var it in card.Items) if (it.CardWide && IsFill(it.Primitive)) DrawItem(card, it);
                foreach (var it in card.Items) if (!it.CardWide) DrawItem(card, it);
                foreach (var it in card.Items) if (it.CardWide && !IsFill(it.Primitive)) DrawItem(card, it);
                Raylib.DrawLineEx(new Vector2(card.Anchor.X, card.Rect.Y + card.Rect.Height), card.Anchor, 1f, card.LeaderColour);
            }
            CardsDrawn = cards.Count;
        }

        private static bool IsFill(in DebugPrimitive p) => p.Shape == DebugPrimitiveShape.Box2D && p.FillColor.A > 0;

        private static bool Allowed(DebugPrimitiveShape s) => s is DebugPrimitiveShape.Text or DebugPrimitiveShape.Box2D
            or DebugPrimitiveShape.Line or DebugPrimitiveShape.Icon or DebugPrimitiveShape.EntityBadge or DebugPrimitiveShape.FilledTriangle;

        private static float HeightOf(in DebugPrimitive p) => p.Shape switch
        {
            DebugPrimitiveShape.Text => p.ThicknessU16 > 0 ? p.ThicknessU16 : GizmoTextDraw.DefaultPx,
            DebugPrimitiveShape.Box2D => p.BoxCenterY + p.BoxExtentY,
            DebugPrimitiveShape.Line => MathF.Max(p.LineStart.Y, p.LineEnd.Y) + 1f,
            DebugPrimitiveShape.EntityBadge => 14f,
            DebugPrimitiveShape.FilledTriangle => MathF.Max(p.TriA.Y, MathF.Max(p.TriB.Y, p.TriC.Y)),
            _ => 10f,
        };

        private static float WidthOf(in DebugPrimitive p, Func<string, float, float> measure) => p.Shape switch
        {
            DebugPrimitiveShape.Text => p.TextX + measure(p.TextContent.ToString(), HeightOf(p)),
            DebugPrimitiveShape.Box2D when p.SizeMode != SizeMode.ScreenPercent => p.BoxCenterX + p.BoxExtentX,
            DebugPrimitiveShape.Line => MathF.Max(p.LineStart.X, p.LineEnd.X),
            DebugPrimitiveShape.EntityBadge => 8f * p.BadgeRichText.ToString().Length,
            _ => 0f,
        };

        private static void DrawItem(Card card, in Item it)
        {
            var p = it.Primitive;
            var o = it.RowOrigin;
            var colour = new Color(p.Color.R, p.Color.G, p.Color.B, p.Color.A);
            float innerW = card.Rect.Width - 2f * Padding;
            switch (p.Shape)
            {
                case DebugPrimitiveShape.Text:
                    GizmoTextDraw.DrawScreen(in p, o + new Vector2(p.TextX, p.TextY), colour);
                    break;
                case DebugPrimitiveShape.Box2D:
                {
                    Rectangle r;
                    if (it.CardWide)   // row 0: both axes fractions of the whole card
                        r = new Rectangle(card.Rect.X + (p.BoxCenterX - p.BoxExtentX) * card.Rect.Width,
                                          card.Rect.Y + (p.BoxCenterY - p.BoxExtentY) * card.Rect.Height,
                                          2f * p.BoxExtentX * card.Rect.Width, 2f * p.BoxExtentY * card.Rect.Height);
                    else if (p.SizeMode == SizeMode.ScreenPercent)   // a row: x a fraction of the card's inner width, y pixels
                        r = new Rectangle(o.X + (p.BoxCenterX - p.BoxExtentX) * innerW, o.Y + p.BoxCenterY - p.BoxExtentY,
                                          2f * p.BoxExtentX * innerW, 2f * p.BoxExtentY);
                    else
                        r = new Rectangle(o.X + p.BoxCenterX - p.BoxExtentX, o.Y + p.BoxCenterY - p.BoxExtentY, 2f * p.BoxExtentX, 2f * p.BoxExtentY);
                    if (p.FillColor.A > 0) Raylib.DrawRectangleRec(r, new Color(p.FillColor.R, p.FillColor.G, p.FillColor.B, p.FillColor.A));
                    if (p.ThicknessU16 > 0 && p.Color.A > 0) Raylib.DrawRectangleLinesEx(r, MathF.Max(1f, p.ThicknessU16 / 10f), colour);
                    break;
                }
                case DebugPrimitiveShape.Line:
                    Raylib.DrawLineEx(o + new Vector2(p.LineStart.X, p.LineStart.Y), o + new Vector2(p.LineEnd.X, p.LineEnd.Y), 1f, colour);
                    break;
                case DebugPrimitiveShape.Icon:
                    Raylib.DrawCircleV(o + new Vector2(p.IconWorldPosX, p.IconWorldPosY), 4f, colour);
                    break;
                case DebugPrimitiveShape.EntityBadge:
                {
                    var rich = p.BadgeRichText;
                    RichTextRenderer.DrawRichTextBadge(ref rich, (int)o.X, (int)o.Y, 12);
                    break;
                }
                case DebugPrimitiveShape.FilledTriangle:
                {
                    var a = o + p.TriA; var b = o + p.TriB; var c = o + p.TriC;
                    Raylib.DrawTriangle(a, b, c, colour);
                    Raylib.DrawTriangle(a, c, b, colour);
                    break;
                }
            }
        }
    }
}
