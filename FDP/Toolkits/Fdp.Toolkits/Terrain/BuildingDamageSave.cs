using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>CE-4391</c> S-4 (UTM §8.2 B-7, Q-D3) — one building's saved damage, as the scenario's <c>TerrainObjects</c> section holds it: its breaches (the panel as its
    /// index WITHIN the building — <see cref="BuildingPanelIndex"/>) and whether it collapsed. The ONE shape the save, the distributed merge and the load all use.
    /// </summary>
    public sealed class SavedBuildingDamage
    {
        /// <summary>Breaches with <see cref="WallBreach.Panel"/> = the index within the building, sorted (the file order).</summary>
        public List<WallBreach> Breaches { get; } = new();
        public bool Collapsed { get; set; }

        /// <summary>⭐ <c>CE-11054</c> — what made each breach look as it does (saved: a scorched rim is how the hole LOOKS): its <see cref="BreachCause"/> and explosive kg. A breach with no entry is Unknown / 0
        /// (an old save). ⛔ Not the actor (a vehicle's id does not survive a load) and not the time or blast point (a time means nothing after a load).</summary>
        public Dictionary<WallBreach, (BreachCause Cause, float Kg)> Origins { get; } = new();

        /// <summary>⭐ <c>CE-11054</c> — what collapsed the building and with how many kg (Unknown / 0 = not stated).</summary>
        public BreachCause CollapseCause { get; set; }
        public float CollapseKg { get; set; }

        /// <summary>⭐ <c>CE-11054</c> — the cause / kg of two origins of one hole: Detonation wins (any blast in it ⇒ scorched), else <paramref name="newer"/> (the older when the newer is Unknown); kg the larger.</summary>
        public static (BreachCause Cause, float Kg) Merge((BreachCause Cause, float Kg) newer, (BreachCause Cause, float Kg) older)
            => (newer.Cause == BreachCause.Detonation || older.Cause == BreachCause.Detonation ? BreachCause.Detonation : newer.Cause != BreachCause.Unknown ? newer.Cause : older.Cause,
                MathF.Max(newer.Kg, older.Kg));

        private static int Order(in WallBreach a, in WallBreach b)
        {
            int c = a.Panel.CompareTo(b.Panel); if (c != 0) return c;
            c = a.From.CompareTo(b.From); if (c != 0) return c;
            c = a.Bottom.CompareTo(b.Bottom); if (c != 0) return c;
            c = a.To.CompareTo(b.To); if (c != 0) return c;
            c = a.Top.CompareTo(b.Top); if (c != 0) return c;
            return ((byte)a.Kind).CompareTo((byte)b.Kind);
        }

        /// <summary>Adds <paramref name="b"/> unless an equal breach is already there (the union of two slices holding the same one).</summary>
        public void AddDistinct(in WallBreach b) { if (!Breaches.Contains(b)) Breaches.Add(b); }

        /// <summary>⭐ <c>CE-11054</c> — <see cref="AddDistinct(in WallBreach)"/> that also remembers the breach's cause and kg (merged by <see cref="Merge"/> when the same breach arrives twice).</summary>
        public void AddDistinct(in WallBreach b, BreachCause cause, float kg)
        {
            AddDistinct(b);
            if (cause == BreachCause.Unknown && kg == 0f) return;
            Origins[b] = Origins.TryGetValue(b, out var have) ? Merge((cause, kg), have) : (cause, kg);
        }

        /// <summary>⭐ <c>CE-5033</c> (glass windows GW-2) — the damaged windows, <see cref="WindowEntry.Panel"/> = the index within the building, sorted by (panel, opening).</summary>
        public List<WindowEntry> Windows { get; } = new();

        /// <summary>Adds <paramref name="w"/>; a window already there keeps the entry with more hits (two slices that hit one window — the view's rule).</summary>
        public void AddWindow(in WindowEntry w)
        {
            for (int i = 0; i < Windows.Count; i++)
                if (Windows[i].Panel == w.Panel && Windows[i].Opening == w.Opening) { if (w.Hits > Windows[i].Hits) Windows[i] = w; return; }
            Windows.Add(w);
        }

        /// <summary>The union of <paramref name="other"/> into this: breaches distinct, collapsed OR-ed, windows by the most hits.</summary>
        public void UnionWith(SavedBuildingDamage other)
        {
            foreach (var b in other.Breaches) AddDistinct(b, other.Origins.TryGetValue(b, out var o) ? o.Cause : BreachCause.Unknown, o.Kg);
            Collapsed |= other.Collapsed;
            (CollapseCause, CollapseKg) = Merge((other.CollapseCause, other.CollapseKg), (CollapseCause, CollapseKg));
            foreach (var w in other.Windows) AddWindow(w);
        }

        public void Sort()
        {
            Breaches.Sort(static (a, b) => Order(a, b));
            Windows.Sort(static (a, b) => a.Panel != b.Panel ? a.Panel.CompareTo(b.Panel) : a.Opening.CompareTo(b.Opening));
        }

        public bool IsEmpty => Breaches.Count == 0 && !Collapsed && Windows.Count == 0;
    }

    /// <summary>⭐ <c>CE-4391</c> S-4 — a panel's index WITHIN its WALL GROUP (rank among the group's panels, in <see cref="TerrainWorld.Panels"/> order) ⇄ its global index. Written once, used by the save, the load,
    /// the wire and the remap. ⭐ <c>CE-4599</c> (B-2, R-4236) — a group is a building OR a free-wall line (<see cref="WallGroups"/>).</summary>
    public static class BuildingPanelIndex
    {
        /// <summary>⭐ CE-4427 R-3 / CE-4599 — per world, built ONCE (a world is immutable): each panel's group and rank within it, each group's panels (global indices) in order, the free-wall lines' keys.</summary>
        internal sealed class Table
        {
            public int[] Group = Array.Empty<int>();
            public int[] Rank = Array.Empty<int>();
            public int[][] PanelsOf = Array.Empty<int[]>();
            public string[] LineKeys = Array.Empty<string>();
            public int Buildings;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TerrainWorld, Table> s_tables = new();
        private static int s_built;

        /// <summary>How many rank tables have been built (diagnostics / rails: a steady caller must not rebuild).</summary>
        public static int TablesBuilt => System.Threading.Volatile.Read(ref s_built);

        internal static Table TableOf(TerrainWorld world) => s_tables.GetValue(world, static w =>
        {
            System.Threading.Interlocked.Increment(ref s_built);
            int nb = w.Buildings.Count;
            var t = new Table { Group = new int[w.Panels.Count], Rank = new int[w.Panels.Count], Buildings = nb };
            // ⭐ CE-4599 (V-11) — free-wall LINES: a run of consecutive free panels (Building < 0) with the same label, each starting where the last ended
            //   (one LineString's segments, in parse order). A line's key is "wall:<label>" (or "wall@<x>,<y>" of its first post, in cm, when unlabelled),
            //   with "#n" for the n-th later line of the same name — content-derived, so every node and every re-load names it the same.
            var lineKeys = new List<string>();
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            int line = -1;
            for (int i = 0; i < w.Panels.Count; i++)
            {
                var p = w.Panels[i];
                if (p.Building >= 0) { t.Group[i] = p.Building; continue; }
                bool continues = line >= 0 && i > 0 && w.Panels[i - 1].Building < 0
                    && string.Equals(w.Panels[i - 1].Label, p.Label, StringComparison.Ordinal)
                    && Vector2.DistanceSquared(w.Panels[i - 1].B, p.A) < 1e-4f;
                if (!continues)
                {
                    string name = !string.IsNullOrEmpty(p.Label) ? "wall:" + p.Label
                        : "wall@" + ((long)MathF.Round(p.A.X * 100f)).ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                                  + ((long)MathF.Round(p.A.Y * 100f)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    int n = seen.TryGetValue(name, out int c) ? c : 0;
                    seen[name] = n + 1;
                    lineKeys.Add(n == 0 ? name : name + "#" + n.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    line = lineKeys.Count - 1;
                }
                t.Group[i] = nb + line;
            }
            t.LineKeys = lineKeys.ToArray();
            int groups = nb + lineKeys.Count;
            var counts = new int[groups];
            for (int i = 0; i < w.Panels.Count; i++) t.Rank[i] = counts[t.Group[i]]++;
            t.PanelsOf = new int[groups][];
            for (int g = 0; g < groups; g++) t.PanelsOf[g] = new int[counts[g]];
            var fill = new int[groups];
            for (int i = 0; i < w.Panels.Count; i++) { int g = t.Group[i]; t.PanelsOf[g][fill[g]++] = i; }
            return t;
        });

        /// <summary>The index of global panel <paramref name="panel"/> within its wall group, or −1 when out of range. O(1) after the world's table exists (CE-4427 R-3).</summary>
        public static int ToLocal(TerrainWorld world, int panel)
        {
            if ((uint)panel >= (uint)world.Panels.Count) return -1;
            return TableOf(world).Rank[panel];
        }

        /// <summary>The global index of panel <paramref name="local"/> of wall group <paramref name="building"/> (a building, or a free-wall line — <see cref="WallGroups"/>), or −1 when the group has no such panel.</summary>
        public static int ToGlobal(TerrainWorld world, int building, int local)
        {
            if (local < 0 || building < 0) return -1;
            var of = TableOf(world).PanelsOf;
            if (building >= of.Length) return -1;
            var list = of[building];
            return local < list.Length ? list[local] : -1;
        }

        /// <summary>
        /// ⭐ CE-5012 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-7; §8 R-5) — breach <paramref name="b"/> (written against <paramref name="from"/>) as <paramref name="to"/> numbers it in wall
        /// group <paramref name="group"/>: the panel of the SAME rank when it still has the same ends (the common case — a re-sort, a re-load); else, for a free-wall LINE, the panel of
        /// the new line that holds the breach's middle, with <c>From</c> / <c>To</c> re-measured from that panel's start and clipped to it — so a gate that ROTATES a closed wall's
        /// start keeps every breach where it was, and a breach in the gate's gap is gone (covered by the gate); else (the wall moved, a building) the same rank, as before.
        /// Content-derived from the two worlds, so every node maps it the same; allocation-free.
        /// </summary>
        public static bool TryMapBreach(TerrainWorld from, in WallBreach b, TerrainWorld to, int group, out WallBreach mapped)
        {
            mapped = default;
            int local = ToLocal(from, b.Panel);
            if (local < 0 || group < 0) return false;
            int same = ToGlobal(to, group, local);
            var op = from.Panels[b.Panel];
            if (same >= 0)
            {
                var sp = to.Panels[same];
                if (Vector2.DistanceSquared(sp.A, op.A) < 1e-4f && Vector2.DistanceSquared(sp.B, op.B) < 1e-4f) { mapped = b with { Panel = same }; return true; }
            }
            if (!WallGroups.IsBuilding(to, group))
            {
                var od = op.B - op.A;
                float olen = od.Length();
                if (olen > 1e-4f)
                {
                    var dir = od / olen;
                    var mid = op.A + dir * ((b.From + b.To) * 0.5f);
                    bool onLine = false;   // a panel of the new line lies on this panel's line: the geometry was kept (a gate), so a breach no panel holds is in the gap
                    var list = TableOf(to).PanelsOf;
                    if (group < list.Length)
                        foreach (int g in list[group])
                        {
                            var np = to.Panels[g];
                            var nd = np.B - np.A;
                            float nlen = nd.Length();
                            if (nlen < 1e-4f) continue;
                            var ndir = nd / nlen;
                            if (Vector2.Dot(ndir, dir) < 0.9999f || MathF.Abs(Cross(np.A - op.A, dir)) > 0.01f) continue;
                            float s0 = Vector2.Dot(np.A - op.A, dir), s1 = s0 + nlen;
                            if (s1 <= 0f || s0 >= olen) continue;
                            onLine = true;
                            float at = Vector2.Dot(mid - np.A, ndir);
                            if (at < 0f || at > nlen) continue;
                            float f = Math.Clamp(b.From - s0, 0f, nlen), t = Math.Clamp(b.To - s0, 0f, nlen);
                            if (t - f < 1e-3f) return false;
                            mapped = b with { Panel = g, From = f, To = t };
                            return true;
                        }
                    if (onLine) return false;
                }
            }
            if (same < 0) return false;
            mapped = b with { Panel = same };
            return true;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4599</c> (B-2; 📄 docs/DESIGN_Vehicle_Breaching.md §7.3 D-1, R-4236, V-11) — the WALL GROUPS of a world: what one damage record
    /// (<see cref="BuildingDamage"/>) belongs to. Groups 0 … <c>Buildings.Count − 1</c> are the buildings (unchanged numbering); after them come the
    /// free-wall LINES — a garden wall, a fence, a hedge, each the panels of one line. ⭐ So <see cref="BuildingDamage.Building"/> holds a GROUP index, and
    /// everything keyed by <see cref="BuildingDamages.KeyOf"/> / <see cref="BuildingPanelIndex"/> (the wire, the save, the load, the remap) carries a free
    /// wall's breaches with no second record. A line is never collapsed (it has no footprint).
    /// </summary>
    public static class WallGroups
    {
        /// <summary>Buildings + free-wall lines.</summary>
        public static int Count(TerrainWorld world) => world.Buildings.Count + BuildingPanelIndex.TableOf(world).LineKeys.Length;

        /// <summary>The group of global panel <paramref name="panel"/>, −1 out of range.</summary>
        public static int Of(TerrainWorld world, int panel)
            => (uint)panel < (uint)world.Panels.Count ? BuildingPanelIndex.TableOf(world).Group[panel] : -1;

        /// <summary>True when <paramref name="group"/> is a building (not a free-wall line).</summary>
        public static bool IsBuilding(TerrainWorld world, int group) => (uint)group < (uint)world.Buildings.Count;

        /// <summary>A free-wall line's own key (no terrain prefix), e.g. <c>wall:Garden#1</c>; null for a building or out of range.</summary>
        public static string? LineKey(TerrainWorld world, int group)
        {
            int l = group - world.Buildings.Count;
            var keys = BuildingPanelIndex.TableOf(world).LineKeys;
            return l >= 0 && l < keys.Length ? keys[l] : null;
        }

        /// <summary>
        /// ⭐ <c>CE-11054</c> — THE outward normal of wall panel <paramref name="panel"/> (global index): the left-hand perpendicular of A→B, flipped to face AWAY from its building's footprint centre
        /// (<see cref="BuildingFires.Extent"/>). A free-wall panel (no building) has no inside: the left-hand perpendicular of A→B as it is. Unit length; a zero-length panel gives +X. Allocation-free.
        /// Used by the building fire (<see cref="BuildingFires.NearestBurnableWall"/>) and by whoever draws a breach's rim (the gateway).
        /// </summary>
        public static System.Numerics.Vector2 OutwardNormal(TerrainWorld world, int panel)
        {
            var w = world.Panels[panel];
            return OutwardNormal(world, panel, (w.A + w.B) * 0.5f);
        }

        /// <summary>
        /// ⭐ <c>CE-11054</c> — <see cref="OutwardNormal(TerrainWorld, int)"/> with the side decided at <paramref name="at"/> (a point on the wall): the same answer for a convex footprint wherever on the wall it is taken.
        /// </summary>
        public static System.Numerics.Vector2 OutwardNormal(TerrainWorld world, int panel, System.Numerics.Vector2 at)
        {
            var w = world.Panels[panel];
            var ab = w.B - w.A;
            var n = ab.LengthSquared() > 1e-9f ? System.Numerics.Vector2.Normalize(new System.Numerics.Vector2(-ab.Y, ab.X)) : System.Numerics.Vector2.UnitX;
            if ((uint)w.Building >= (uint)world.Buildings.Count) return n;   // a free wall: no inside, the left-hand side
            var (centre, _) = BuildingFires.Extent(world.Buildings[w.Building]);
            return System.Numerics.Vector2.Dot(n, at - centre) >= 0f ? n : -n;
        }

        /// <summary>
        /// ⭐ <c>CE-11054</c> — the world centre of breach <paramref name="b"/>: A + dir × (From + To) / 2 along its panel (terrain-local metres), z = (Bottom + Top) / 2. Allocation-free.
        /// </summary>
        public static System.Numerics.Vector3 BreachCentre(TerrainWorld world, in WallBreach b)
        {
            var p = world.Panels[b.Panel];
            var ab = p.B - p.A;
            float len = ab.Length();
            var dir = len > 1e-6f ? ab / len : System.Numerics.Vector2.UnitX;
            var at = p.A + dir * ((b.From + b.To) * 0.5f);
            return new System.Numerics.Vector3(at.X, at.Y, (b.Bottom + b.Top) * 0.5f);
        }

        /// <summary>Where a group's damage entity stands: a building's position, a line's first post (its base).</summary>
        public static System.Numerics.Vector3 Anchor(TerrainWorld world, int group)
        {
            if (IsBuilding(world, group)) { var b = world.Buildings[group]; return new System.Numerics.Vector3(b.Position, b.BaseZ); }
            int first = BuildingPanelIndex.ToGlobal(world, group, 0);
            if (first < 0) return default;
            var p = world.Panels[first];
            return new System.Numerics.Vector3(p.A, p.BaseZ);
        }
    }

    /// <summary>⭐ <c>CE-4391</c> S-4 — the JSON of one building's damage entry; <see cref="TerrainObjectsSection"/> owns the section around it.</summary>
    internal static class BuildingDamageJson
    {
        public const string BreachesProperty = "breaches";
        public const string CollapsedProperty = "collapsed";
        /// <summary>⭐ CE-11054 — what collapsed it ("detonation" ...) and with how many kg; omitted when not stated. A breach row may carry a 7th element, its cause, and an 8th, its kg (both omitted when Unknown / 0).</summary>
        public const string CollapseCauseProperty = "collapseCause", CollapseKgProperty = "collapseKg";

        private static string? CauseName(BreachCause c) => c switch
        {
            BreachCause.Detonation => "detonation", BreachCause.Ram => "ram", BreachCause.Blade => "blade", BreachCause.Push => "push", _ => null,
        };

        private static bool TryCause(string? name, out BreachCause cause)
        {
            cause = name?.ToLowerInvariant() switch
            {
                "detonation" => BreachCause.Detonation, "ram" => BreachCause.Ram, "blade" => BreachCause.Blade, "push" => BreachCause.Push, "unknown" => BreachCause.Unknown, _ => (BreachCause)255,
            };
            return cause != (BreachCause)255;
        }
        /// <summary>⭐ CE-5033 — the damaged windows: <c>[panel, opening, hits, u0, v0, u1, v1]</c> each (panel within the building; hits 3 = broken; u, v 0..255).</summary>
        public const string WindowsProperty = "windows";

        public static bool IsEntry(JsonObject entry) => entry.ContainsKey(BreachesProperty) || entry.ContainsKey(CollapsedProperty) || entry.ContainsKey(WindowsProperty);

        public static JsonObject Write(SavedBuildingDamage d)
        {
            d.Sort();
            var o = new JsonObject();
            if (d.Breaches.Count > 0)
            {
                var rows = new JsonArray();
                foreach (var b in d.Breaches)
                {
                    var row = new JsonArray(JsonValue.Create(b.Panel), JsonValue.Create(b.From), JsonValue.Create(b.To), JsonValue.Create(b.Bottom), JsonValue.Create(b.Top),
                        JsonValue.Create(b.Kind == BreachKind.Passage ? "passage" : "hole"));
                    if (d.Origins.TryGetValue(b, out var org) && (org.Cause != BreachCause.Unknown || org.Kg > 0f))   // ⭐ CE-11054 — the cause (and kg) only when stated: a save without them stays as before
                    {
                        row.Add(JsonValue.Create(CauseName(org.Cause) ?? "unknown"));
                        if (org.Kg > 0f) row.Add(JsonValue.Create(org.Kg));
                    }
                    rows.Add(row);
                }
                o[BreachesProperty] = rows;
            }
            if (d.Collapsed)
            {
                o[CollapsedProperty] = true;
                if (CauseName(d.CollapseCause) is { } cc) o[CollapseCauseProperty] = cc;   // ⭐ CE-11054
                if (d.CollapseKg > 0f) o[CollapseKgProperty] = d.CollapseKg;
            }
            if (d.Windows.Count > 0)
            {
                var rows = new JsonArray();
                foreach (var w in d.Windows)
                    rows.Add(new JsonArray(JsonValue.Create(w.Panel), JsonValue.Create((int)w.Opening), JsonValue.Create((int)w.Hits),
                        JsonValue.Create((int)w.U0), JsonValue.Create((int)w.V0), JsonValue.Create((int)w.U1), JsonValue.Create((int)w.V1)));
                o[WindowsProperty] = rows;
            }
            return o;
        }

        private static bool Num(JsonNode? n, out double d)
        {
            d = 0;
            if (n is not JsonValue v) return false;
            if (v.TryGetValue(out double x)) { d = x; return double.IsFinite(x); }
            if (v.TryGetValue(out float f)) { d = f; return double.IsFinite(f); }
            if (v.TryGetValue(out int i)) { d = i; return true; }
            return false;
        }

        /// <summary>⛔ Fails LOUDLY on a malformed entry — a scenario that silently loses a breach or a collapse is what the section exists to prevent.</summary>
        public static SavedBuildingDamage Read(string key, JsonObject entry)
        {
            string at = $"[Scenario] {TerrainObjectsSection.Name}['{key}']";
            var d = new SavedBuildingDamage();
            if (entry[BreachesProperty] is { } br)
            {
                if (br is not JsonArray rows) throw new InvalidOperationException($"{at}.breaches is not an array.");
                int n = 0;
                foreach (var row in rows)
                {
                    string r = $"{at}.breaches[{n++}]";
                    if (row is not JsonArray a || a.Count < 6 || a.Count > 8) throw new InvalidOperationException($"{r} is not [panel, from, to, bottom, top, kind] (optionally followed by a cause and a kg).");
                    if (!Num(a[0], out double p) || p < 0 || p != Math.Floor(p) || p > int.MaxValue) throw new InvalidOperationException($"{r}: the panel '{a[0]}' is not a panel index.");
                    if (!Num(a[1], out double from) || !Num(a[2], out double to) || !Num(a[3], out double bottom) || !Num(a[4], out double top))
                        throw new InvalidOperationException($"{r}: from, to, bottom and top must be numbers.");
                    if (from > to || bottom > top) throw new InvalidOperationException($"{r}: the rectangle is inverted (from > to or bottom > top).");
                    if (a[5] is not JsonValue kv || !kv.TryGetValue(out string? k)
                        || !(string.Equals(k, "hole", StringComparison.OrdinalIgnoreCase) || string.Equals(k, "passage", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException($"{r}: the kind '{a[5]}' is not 'hole' or 'passage'.");
                    var cause = BreachCause.Unknown;
                    float kg = 0f;
                    if (a.Count >= 7 && (a[6] is not JsonValue cv2 || !cv2.TryGetValue(out string? cs) || !TryCause(cs, out cause)))
                        throw new InvalidOperationException($"{r}: the cause '{a[6]}' is not 'detonation', 'ram', 'blade' or 'push'.");
                    if (a.Count == 8)
                    {
                        if (!Num(a[7], out double kgv) || kgv < 0) throw new InvalidOperationException($"{r}: the kg '{a[7]}' is not a number of at least 0.");
                        kg = (float)kgv;
                    }
                    d.AddDistinct(new WallBreach((int)p, (float)from, (float)to, (float)bottom, (float)top,
                        string.Equals(k, "passage", StringComparison.OrdinalIgnoreCase) ? BreachKind.Passage : BreachKind.Hole), cause, kg);   // ⭐ CE-11054 — absent ⇒ Unknown / 0
                }
            }
            if (entry[CollapsedProperty] is { } cn)
            {
                if (cn is not JsonValue cv || !cv.TryGetValue(out bool c)) throw new InvalidOperationException($"{at}.collapsed is not true or false.");
                d.Collapsed = c;
            }
            if (entry[CollapseCauseProperty] is { } ccn)   // ⭐ CE-11054
            {
                if (ccn is not JsonValue ccv || !ccv.TryGetValue(out string? ccs) || !TryCause(ccs, out var collapseCause))
                    throw new InvalidOperationException($"{at}.collapseCause '{ccn}' is not a cause name.");
                d.CollapseCause = collapseCause;
            }
            if (entry[CollapseKgProperty] is { } ckn)
            {
                if (!Num(ckn, out double ck) || ck < 0) throw new InvalidOperationException($"{at}.collapseKg '{ckn}' is not a number of at least 0.");
                d.CollapseKg = (float)ck;
            }
            if (entry[WindowsProperty] is { } wn)
            {
                if (wn is not JsonArray rows) throw new InvalidOperationException($"{at}.windows is not an array.");
                int n = 0;
                foreach (var row in rows)
                {
                    string r = $"{at}.windows[{n++}]";
                    if (row is not JsonArray a || a.Count != 7) throw new InvalidOperationException($"{r} is not [panel, opening, hits, u0, v0, u1, v1].");
                    var v = new int[7];
                    for (int k = 0; k < 7; k++)
                        if (!Num(a[k], out double x) || x != Math.Floor(x) || x < 0 || x > (k == 0 ? int.MaxValue : 255)) throw new InvalidOperationException($"{r}[{k}] '{a[k]}' is not a whole number in range.");
                        else v[k] = (int)x;
                    if (v[2] < 1 || v[2] > WindowDamages.BrokenHits) throw new InvalidOperationException($"{r}: hits {v[2]} is not 1..{WindowDamages.BrokenHits}.");
                    d.AddWindow(new WindowEntry(v[0], (byte)v[1], (byte)v[2], (byte)v[3], (byte)v[4], (byte)v[5], (byte)v[6]));
                }
            }
            d.Sort();
            return d;
        }
    }
}

namespace Fdp.Toolkit.Terrain
{
    /// <summary>⭐ <c>CE-4391</c> S-4 (B-7) — saved building damage → the <see cref="BuildingDamage"/> components a load creates; the toolkit half of <c>TerrainObjectRequests.ForBuildingDamage</c>.</summary>
    public static class BuildingDamageLoad
    {
        /// <summary>
        /// One entry per saved building in key order: its building index and the components to create — breaches in the saved (sorted) order, <see cref="BuildingDamage.Capacity"/> to an entity,
        /// the collapsed byte on the first. A saved building or panel this terrain does not have is reported through <paramref name="warn"/> and ignored (the rest of the building still loads).
        /// </summary>
        public static List<(string Key, int Building, List<BuildingDamage> Entities)> Resolve(
            TerrainWorld world, IReadOnlyDictionary<string, SavedBuildingDamage> saved, Action<string>? warn)
        {
            var byKey = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0, n = WallGroups.Count(world); i < n; i++) byKey[BuildingDamages.KeyOf(world, i)] = i;   // ⭐ CE-4599 — free-wall lines too

            var keys = new List<string>(saved.Keys);
            keys.Sort(StringComparer.Ordinal);
            var result = new List<(string, int, List<BuildingDamage>)>();
            foreach (var key in keys)
            {
                if (!byKey.TryGetValue(key, out int building))
                {
                    warn?.Invoke($"the scenario saves damage for building '{key}', which terrain '{world.Name ?? "(unnamed)"}' does not have — ignored.");
                    continue;
                }
                var s = saved[key];
                var entities = new List<BuildingDamage>();
                var d = new BuildingDamage { Building = building, Collapsed = (byte)(s.Collapsed && WallGroups.IsBuilding(world, building) ? 1 : 0) };
                if (d.Collapsed != 0) d.SetCollapse(s.CollapseCause, default, s.CollapseKg);   // ⭐ CE-11054 — how it looks, not when
                bool any = s.Collapsed;
                foreach (var b in s.Breaches)
                {
                    int global = BuildingPanelIndex.ToGlobal(world, building, b.Panel);
                    if (global < 0)
                    {
                        warn?.Invoke($"the scenario saves a breach on panel {b.Panel} of building '{key}', which has no such panel — ignored.");
                        continue;
                    }
                    if (d.Count == BuildingDamage.Capacity) { entities.Add(d); d = new BuildingDamage { Building = building }; }
                    d.Set(d.Count, b with { Panel = global });
                    if (s.Origins.TryGetValue(b, out var origin)) d.SetOrigin(d.Count, new BreachOrigin(default, default, origin.Cause, origin.Kg));   // ⭐ CE-11054 — time 0 = from a save, the cause and kg are kept
                    d.Count++;
                    any = true;
                }
                any |= s.Windows.Count > 0;   // ⭐ CE-5033 — a building with only damaged windows still gets its entity
                if (any) entities.Add(d);
                if (entities.Count > 0) result.Add((key, building, entities));
            }
            return result;
        }

        /// <summary>⭐ <c>CE-5033</c> — the saved windows of <paramref name="building"/> as this world numbers them (a window it does not have — no panel, no opening, no glass — is warned and dropped).</summary>
        public static WindowDamage WindowsOf(TerrainWorld world, int building, SavedBuildingDamage saved, Action<string>? warn = null)
        {
            var w = new WindowDamage { Building = building };
            foreach (var e in saved.Windows)
            {
                int global = BuildingPanelIndex.ToGlobal(world, building, e.Panel);
                if (global < 0 || e.Opening >= world.Panels[global].Openings.Count || !world.Panels[global].Openings[e.Opening].Glazed)
                {
                    warn?.Invoke($"the scenario saves a damaged window {e.Panel}/{e.Opening} of building {building}, which this terrain has no glass for — ignored.");
                    continue;
                }
                if (w.Count == WindowDamage.Capacity) break;
                w.Set(w.Count, e with { Panel = global });
                w.Count++;
            }
            return w;
        }
    }
}
