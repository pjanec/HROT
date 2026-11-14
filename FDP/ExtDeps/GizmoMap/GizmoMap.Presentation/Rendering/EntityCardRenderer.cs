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

        /// <summary>
        /// A laid-out card: where it is, where its entity is, what is in it. ⭐ CE-1478 P4 — POOLED by the renderer (reused every <see cref="Layout(IReadOnlyList{DebugPrimitive}, Func{long, Vector2?}, Func{string, float, float})"/>):
        /// valid until the next Layout call; ⛔ never keep one across frames.
        /// </summary>
        public sealed class Card
        {
            public long NetworkId { get; internal set; }
            public Rectangle Rect { get; internal set; }
            public Vector2 Anchor { get; internal set; }
            public List<Item> Items { get; } = new();
            public Color LeaderColour { get; internal set; }
        }

        /// <summary>Cards drawn in the last <see cref="Draw"/>.</summary>
        public int CardsDrawn { get; private set; }

        /// <summary>Card primitives of shapes a card cannot hold (Sphere, Arrow, SemanticShape, …) — counted, not drawn.</summary>
        public ShapeCounters Skipped { get; } = new();

        // ⭐ CE-1478 P4 — everything Layout needs lives in fields, grown and never shrunk: 4.15 MB of garbage per frame (a Dictionary + List per entity, a SortedDictionary
        //   per card, an Item list, a card record, the text of every Text) became 0 B once warm. (docs/DESIGN_Replay_Playback_Performance.md §3 P4, MEASURE §4 row 1.)
        private readonly List<Card> _cards = new();
        private int _cardCount;
        private readonly List<DebugPrimitive> _input = new();           // the IEnumerable overload's copy
        private DebugPrimitive[] _prims = new DebugPrimitive[256];      // the card primitives of the frame, in arrival order
        private int[] _primGroup = new int[256];
        private int _primCount;
        private readonly Dictionary<long, int> _groupOf = new();        // entity id → group, first-seen order
        private long[] _groupId = new long[64];
        private int[] _groupStart = new int[65];
        private int[] _groupFill = new int[64];
        private int _groupCount;
        private int[] _order = new int[256];                            // primitive indexes, grouped, then z-sorted within a group
        private float[] _rowHeight = new float[256];

        /// <summary>
        /// Lays out the cards of <paramref name="primitives"/> (only <see cref="CoordinateSpace.EntityCard"/> ones are read). A convenience for callers
        /// that hold a sequence, not a list: it COPIES into a reusable list (the enumerator itself allocates) — a caller with a list uses the other overload.
        /// </summary>
        public List<Card> Layout(IEnumerable<DebugPrimitive> primitives, Func<long, Vector2?> anchorScreen, Func<string, float, float> measureText)
        {
            _input.Clear();
            foreach (var p in primitives) _input.Add(p);
            return Layout(_input, anchorScreen, measureText);
        }

        /// <summary>
        /// Lays out the cards of <paramref name="primitives"/> (only <see cref="CoordinateSpace.EntityCard"/> ones are read).
        /// <paramref name="anchorScreen"/> answers an entity's screen point (null = not on screen / unknown ⇒ no card);
        /// <paramref name="measureText"/> the pixel width of a text at a size. Pure — railable without a window.
        /// ⭐ CE-1478 P4 — allocates nothing once warm; the returned list and its cards are the renderer's own (valid until the next call).
        /// </summary>
        public List<Card> Layout(IReadOnlyList<DebugPrimitive> primitives, Func<long, Vector2?> anchorScreen, Func<string, float, float> measureText)
        {
            Skipped.Reset();
            _primCount = 0; _groupCount = 0; _cardCount = 0;
            _groupOf.Clear();
            for (int i = 0; i < primitives.Count; i++)
            {
                var p = primitives[i];
                if (p.Space != CoordinateSpace.EntityCard) continue;
                if (!Allowed(p.Shape)) { Skipped.Add(p.Shape); continue; }
                long id = p.AnchorIndex;
                if (!_groupOf.TryGetValue(id, out int g))
                {
                    g = _groupCount++;
                    if (g == _groupId.Length) { Array.Resize(ref _groupId, g * 2); Array.Resize(ref _groupFill, g * 2); Array.Resize(ref _groupStart, g * 2 + 1); }
                    _groupId[g] = id; _groupFill[g] = 0;
                    _groupOf[id] = g;
                }
                if (_primCount == _prims.Length) { Array.Resize(ref _prims, _primCount * 2); Array.Resize(ref _primGroup, _primCount * 2); Array.Resize(ref _order, _primCount * 2); }
                _prims[_primCount] = p; _primGroup[_primCount] = g; _groupFill[g]++;
                _primCount++;
            }

            // group the primitives (a counting sort: arrival order kept inside a group), then z-sort each group stably (insertion: a card holds a handful)
            int start = 0;
            for (int g = 0; g < _groupCount; g++) { _groupStart[g] = start; start += _groupFill[g]; _groupFill[g] = 0; }
            _groupStart[_groupCount] = start;
            for (int i = 0; i < _primCount; i++) { int g = _primGroup[i]; _order[_groupStart[g] + _groupFill[g]++] = i; }

            for (int g = 0; g < _groupCount; g++)
            {
                long id = _groupId[g];
                if (anchorScreen(id) is not { } anchor) continue;
                int lo = _groupStart[g], hi = _groupStart[g + 1];
                for (int i = lo + 1; i < hi; i++)   // stable by ZIndex: ZIndex 0 (card-wide) first, then the rows bottom-up
                {
                    int cur = _order[i]; byte z = _prims[cur].ZIndex; int j = i - 1;
                    while (j >= lo && _prims[_order[j]].ZIndex > z) { _order[j + 1] = _order[j]; j--; }
                    _order[j + 1] = cur;
                }

                // the rows: runs of one ZIndex after the card-wide ones
                int rowsStart = lo;
                while (rowsStart < hi && _prims[_order[rowsStart]].ZIndex == 0) rowsStart++;
                float width = MinWidth - 2f * Padding, height = 0f;
                int rowCount = 0;
                for (int i = rowsStart; i < hi;)
                {
                    byte z = _prims[_order[i]].ZIndex;
                    float h = 0f;
                    for (; i < hi && _prims[_order[i]].ZIndex == z; i++)
                    {
                        h = MathF.Max(h, HeightOf(in _prims[_order[i]]));
                        width = MathF.Max(width, WidthOf(in _prims[_order[i]], measureText));
                    }
                    if (rowCount == _rowHeight.Length) Array.Resize(ref _rowHeight, rowCount * 2);
                    _rowHeight[rowCount++] = h;
                    height += h;
                }
                height += Math.Max(0, rowCount - 1) * RowGap;
                float cardW = width + 2f * Padding, cardH = MathF.Max(height + 2f * Padding, 2f * Padding + 4f);
                var rect = new Rectangle(anchor.X - cardW / 2f, anchor.Y - Leader - cardH, cardW, cardH);

                var card = NextCard();
                var items = card.Items;
                items.Clear();
                for (int i = lo; i < rowsStart; i++) items.Add(new Item(_prims[_order[i]], new Vector2(rect.X, rect.Y), cardH, true));
                float y = rect.Y + Padding;
                int ri = 0;
                for (int i = rowsStart; i < hi;)
                {
                    byte z = _prims[_order[i]].ZIndex;
                    for (; i < hi && _prims[_order[i]].ZIndex == z; i++)
                        items.Add(new Item(_prims[_order[i]], new Vector2(rect.X + Padding, y), _rowHeight[ri], false));
                    y += _rowHeight[ri] + RowGap;
                    ri++;
                }

                var leader = new Color((byte)170, (byte)170, (byte)170, (byte)220);
                for (int i = lo; i < rowsStart; i++)
                {
                    ref readonly var p = ref _prims[_order[i]];
                    if (p.Shape == DebugPrimitiveShape.Box2D && p.ThicknessU16 > 0 && p.Color.A > 0)
                        leader = new Color(p.Color.R, p.Color.G, p.Color.B, p.Color.A);
                }
                card.NetworkId = id; card.Rect = rect; card.Anchor = anchor; card.LeaderColour = leader;
            }
            if (_cards.Count > _cardCount) _cards.RemoveRange(_cardCount, _cards.Count - _cardCount);   // a shrink keeps the pooled objects in _cardPool
            return _cards;
        }

        private readonly List<Card> _cardPool = new();

        private Card NextCard()
        {
            if (_cardCount == _cardPool.Count) _cardPool.Add(new Card());
            var c = _cardPool[_cardCount];
            if (_cardCount < _cards.Count) _cards[_cardCount] = c; else _cards.Add(c);
            _cardCount++;
            return c;
        }

        /// <summary>Lays out and draws the cards (screen space — call outside any camera mode).</summary>
        public void Draw(IEnumerable<DebugPrimitive> primitives, Func<long, Vector2?> anchorScreen)
        {
            _input.Clear();
            foreach (var p in primitives) _input.Add(p);
            Draw(_input, anchorScreen);
        }

        /// <summary>⭐ CE-1478 P4 — the list overload: no enumerator, and the text widths come from a cache (a text is measured once per size, not once per frame).</summary>
        public void Draw(IReadOnlyList<DebugPrimitive> primitives, Func<long, Vector2?> anchorScreen)
        {
            _measureCached ??= MeasureCached;
            var cards = Layout(primitives, anchorScreen, _measureCached);
            // ⭐ CE-4582 (§6am D-1, D-2, D-4) — with an open occlusion scope each card is drawn at ITS entity's depth (the whole stack at one depth,
            //   so frame, bars and rows are cut by the same line); the cards that must always show (the selected unit's) after the scope ends.
            var occ = Occlusion;
            bool tested = occ is { Active: true };
            int onTop = 0;
            for (int c = 0; c < cards.Count; c++)
            {
                var card = cards[c];
                if (tested && AlwaysOnTop?.Invoke(card.NetworkId) == true)
                {
                    if (onTop == _onTop.Length) Array.Resize(ref _onTop, onTop * 2);
                    _onTop[onTop++] = c;
                    continue;
                }
                if (tested) occ!.Push(card.NetworkId);
                DrawCard(card);
                if (tested) occ!.Pop();
            }
            if (tested) occ!.End();
            for (int k = 0; k < onTop; k++) DrawCard(cards[_onTop[k]]);
            CardsDrawn = cards.Count;
        }

        /// <summary>⭐ CE-4582 (§6am D-1) — set by a 3-D host: the cards are hidden behind what stands in front of their entity. Null ⇒ on top (2-D, M13).</summary>
        public ICardOcclusion? Occlusion { get; set; }

        /// <summary>⭐ CE-4582 (§6am D-4) — a card that is never hidden (the host's selection), drawn after the occlusion scope.</summary>
        public Func<long, bool>? AlwaysOnTop { get; set; }

        private int[] _onTop = new int[8];

        private void DrawCard(Card card)
        {
            // row 0 backgrounds first, then the rows, then row 0's outlines — the frame is on top, the background behind
            var items = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(card.Items);
            for (int i = 0; i < items.Length; i++) if (items[i].CardWide && IsFill(items[i].Primitive)) DrawItem(card, in items[i]);
            for (int i = 0; i < items.Length; i++) if (!items[i].CardWide) DrawItem(card, in items[i]);
            for (int i = 0; i < items.Length; i++) if (items[i].CardWide && !IsFill(items[i].Primitive)) DrawItem(card, in items[i]);
            Raylib.DrawLineEx(new Vector2(card.Anchor.X, card.Rect.Y + card.Rect.Height), card.Anchor, 1f, card.LeaderColour);
        }

        private Func<string, float, float>? _measureCached;
        private readonly Dictionary<(string, float), float> _widths = new();
        private const int MaxCachedWidths = 8192;

        private float MeasureCached(string s, float px)
        {
            if (_widths.TryGetValue((s, px), out float w)) return w;
            if (_widths.Count >= MaxCachedWidths) _widths.Clear();
            return _widths[(s, px)] = Raylib.MeasureTextEx(GizmoTextDraw.Font, s, px, 1f).X;
        }

        // ⭐ CE-1478 P4 — a card's text as a string WITHOUT building one per frame: the interned long text, else a canonical string per distinct 32 bytes (cached).
        //   📐 The key is the raw bytes — FixedString32.Equals / GetHashCode build a string each (they would defeat the cache).
        private readonly record struct TextKey(ulong A, ulong B, ulong C, ulong D);
        private readonly Dictionary<TextKey, string> _texts = new();
        private const int MaxCachedTexts = 8192;

        private string TextOfCached(in DebugPrimitive p)
        {
            if (p.Space != CoordinateSpace.EntityLocal && p.StringHash != 0 && GizmoTextDraw.Strings?.TryResolve(p.StringHash) is { } full) return full;
            var key = System.Runtime.CompilerServices.Unsafe.As<FixedString32, TextKey>(ref System.Runtime.CompilerServices.Unsafe.AsRef(in p.TextContent));
            if (_texts.TryGetValue(key, out var cached)) return cached;
            if (_texts.Count >= MaxCachedTexts) _texts.Clear();
            return _texts[key] = p.TextContent.ToString();
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

        private float WidthOf(in DebugPrimitive p, Func<string, float, float> measure) => p.Shape switch
        {
            DebugPrimitiveShape.Text => p.TextX + measure(TextOfCached(in p), HeightOf(p)),
            DebugPrimitiveShape.Box2D when p.SizeMode != SizeMode.ScreenPercent => p.BoxCenterX + p.BoxExtentX,
            DebugPrimitiveShape.Line => MathF.Max(p.LineStart.X, p.LineEnd.X),
            DebugPrimitiveShape.EntityBadge => 8f * CharCount(in p.TextContent),
            _ => 0f,
        };

        private static int CharCount(in FixedString32 s)
        {
            ref byte start = ref System.Runtime.CompilerServices.Unsafe.As<FixedString32, byte>(ref System.Runtime.CompilerServices.Unsafe.AsRef(in s));
            var span = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref start, 32);
            int len = 0;
            while (len < FixedString32.MaxLength && span[len] != 0) len++;
            return len == 0 ? 0 : System.Text.Encoding.UTF8.GetCharCount(span.Slice(0, len));
        }

        private void DrawItem(Card card, in Item it)
        {
            var p = it.Primitive;
            var o = it.RowOrigin;
            var colour = new Color(p.Color.R, p.Color.G, p.Color.B, p.Color.A);
            float innerW = card.Rect.Width - 2f * Padding;
            switch (p.Shape)
            {
                case DebugPrimitiveShape.Text:
                {
                    var at = o + new Vector2(p.TextX, p.TextY);
                    GizmoTextDraw.DrawStyled(GizmoTextDraw.Font, TextOfCached(in p), new Vector2(at.X, at.Y + p.LineOffsetPx), GizmoTextDraw.PixelSize(p), 1f, 1f, p.TextStyle, colour);
                }
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
