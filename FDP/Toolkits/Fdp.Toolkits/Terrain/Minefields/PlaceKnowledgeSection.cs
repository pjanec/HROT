using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>One side's saved knowledge of one field's chunk: the cells above unknown, <c>Marked</c>, <c>LearnedAt</c>, the source.</summary>
    public sealed class SavedKnowledge
    {
        public Guid Subject;
        public byte SidePlus1;
        public PlaceKnowledgeKind Kind = PlaceKnowledgeKind.Minefield;
        public ushort Chunk;
        public bool Marked;
        public SimInstant LearnedAt;
        public KnowledgeSource Source;
        /// <summary>(col, row) → level; only cells above unknown.</summary>
        public readonly SortedDictionary<(byte Row, byte Col), byte> Cells = new();
    }

    /// <summary>
    /// ⭐ CE-2468 UF-3 (DESIGN_Minefields.md §9b.1 K1) — one side's saved knowledge of one place under fire: the record's payload and its AGE at the save (now − learned-at),
    /// ⛔ never the absolute time — a load rebases it on the load's clock.
    /// </summary>
    public sealed class SavedUnderFire
    {
        public Guid Subject;
        public byte SidePlus1;
        public ushort Chunk;
        public UnderFireCause Cause;
        public Vector3 Point;
        public float RangeM, EnemyStrength;
        public KnowledgeSource Source;
        public bool Cooled;
        /// <summary>Seconds between the last evidence and the save (≥ 0).</summary>
        public double AgeSeconds;
    }

    /// <summary>Everything <see cref="PlaceKnowledgeSection"/> holds, read back: knowledge records, struck / cleared mines per field, lanes per field.</summary>
    public sealed class SavedPlaceKnowledge
    {
        public readonly List<SavedKnowledge> Knowledge = new();
        /// <summary>⭐ CE-2468 UF-3 — the places under fire each side knew, by age.</summary>
        public readonly List<SavedUnderFire> UnderFire = new();
        public readonly SortedDictionary<Guid, SortedDictionary<(short Row, short Col), MineGone>> Strikes = new();
        public readonly SortedDictionary<Guid, List<(Vector2 A, Vector2 B, float Width, byte SidePlus1)>> Lanes = new();
        public bool IsEmpty => Knowledge.Count == 0 && Strikes.Count == 0 && Lanes.Count == 0 && UnderFire.Count == 0;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4624</c> MS-T2 (DESIGN_Minefields.md KR-15, MF-14, R-5054) — the scenario's <c>Knowledge</c> section: what the sides learned of PLACES, and the field-level
    /// ground truth beside it, keyed by the SUBJECT's stable guid (a minefield is an entity, not a terrain object — hence not <see cref="TerrainObjectsSection"/>).
    /// <code>"Knowledge": {
    ///   "&lt;guid&gt;|&lt;side+1&gt;|&lt;kind&gt;|&lt;chunk&gt;": { "cells": [[row, col, level], …], "marked": true, "learnedAt": 12.5, "source": 2 },
    ///   "&lt;guid&gt;|strikes": { "mines": [[row, col, how], …] },
    ///   "&lt;guid&gt;|lanes":   { "lanes": [[ax, ay, bx, by, width, side+1], …] } }</code>
    /// <para>⭐ ONE writer and ONE reader for the three places that touch it — the save (<c>ScenarioSerializer</c>), the distributed merge (<c>ScenarioMergeCore</c>), the load
    /// (<c>ScenarioLoadStep</c>). A record entity is NEVER saved as an entity (<c>NoScenario</c>, transient): it is rebuilt at load, one per saved key, owned by the loading node.</para>
    /// <para>⭐ The MERGE of two slices carrying one key applies the KIND's policy (KR-4) — the minefield's is the lattice max per cell (unknown &lt; suspected &lt; known-clear &lt;
    /// known-mined), <c>Marked</c> OR-ed, <c>LearnedAt</c> max — the same rule the union table applies at run time. ⛔ It never throws on a duplicate key: two deciding nodes
    /// writing one (field, side) is the normal case (MR-2), the very defect <c>ScenarioMergeCore</c>'s terrain-object arm would turn into a failed save.</para>
    /// </summary>
    public static unsafe class PlaceKnowledgeSection
    {
        public const string Name = "Knowledge";
        private const string CellsProperty = "cells", MinesProperty = "mines", LanesProperty = "lanes", UnderFireProperty = "underFire";

        // ── keys ───────────────────────────────────────────────────────────────────
        public static string EntryKey(Guid subject, byte sidePlus1, PlaceKnowledgeKind kind, ushort chunk)
            => string.Create(CultureInfo.InvariantCulture, $"{subject:D}|{sidePlus1}|{(int)kind}|{chunk}");
        public static string StrikesKey(Guid field) => string.Create(CultureInfo.InvariantCulture, $"{field:D}|strikes");
        public static string LanesKey(Guid field) => string.Create(CultureInfo.InvariantCulture, $"{field:D}|lanes");

        /// <summary>The section of <paramref name="dom"/> (either casing), or null.</summary>
        public static JsonObject? Of(JsonObject dom) => (dom[Name] ?? dom["knowledge"]) as JsonObject;

        /// <summary>True when <paramref name="entry"/> is a knowledge entry / a strikes entry / a lanes entry.</summary>
        public static bool IsKnowledgeEntry(JsonNode? entry) => entry is JsonObject o && o[CellsProperty] is JsonArray;
        public static bool IsStrikesEntry(JsonNode? entry) => entry is JsonObject o && o[MinesProperty] is JsonArray;
        public static bool IsLanesEntry(JsonNode? entry) => entry is JsonObject o && o[LanesProperty] is JsonArray;
        /// <summary>⭐ CE-2468 UF-3 — an under-fire entry: <c>{ "underFire": [cause, x, y, z, range, strength], "age": s, "source": n, "cooled": true? }</c> under the kind-2 key.</summary>
        public static bool IsUnderFireEntry(JsonNode? entry) => entry is JsonObject o && o[UnderFireProperty] is JsonArray;

        // ── save ───────────────────────────────────────────────────────────────────
        /// <summary>
        /// The section for <paramref name="repo"/>, or null when there is nothing to say: every knowledge / strikes / lanes record this host OWNS (the records' own gate — a
        /// distributed save writes each entity in exactly one slice), merged per key, sorted.
        /// </summary>
        public static JsonObject? Write(EntityRepository repo)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            var saved = new SavedPlaceKnowledge();
            var byKey = new Dictionary<string, SavedKnowledge>(StringComparer.Ordinal);

            if (repo.IsComponentTypeRegistered<MinefieldKnowledge>())
                foreach (var e in repo.Query().With<MinefieldKnowledge>().WithLifecycle(EntityLifecycle.AllButTearDown).Build())
                {
                    if (!RecordWriterCore<MinefieldKnowledge, KnowledgeKey, KnowledgeItem>.OwnsRecord(repo, e, PlaceKnowledgeSystem.OwnershipKey)) continue;
                    var r = repo.GetComponentRO<MinefieldKnowledge>(e);
                    if (r.Header.SidePlus1 == 0 || r.Header.Subject == Guid.Empty) continue;
                    var item = FromRecord(in r);
                    string key = EntryKey(item.Subject, item.SidePlus1, item.Kind, item.Chunk);
                    if (byKey.TryGetValue(key, out var have)) MergeInto(have, item); else byKey[key] = item;
                }
            foreach (var k in byKey.Values) if (!IsBlank(k)) saved.Knowledge.Add(k);

            // ⭐ CE-2468 UF-3 (K1) — the places under fire, by AGE: now − learned-at, on this host's clock
            if (repo.IsComponentTypeRegistered<UnderFireKnowledge>())
            {
                double now = Fdp.Toolkit.Combat.HitModel.Now(repo);
                var byUf = new Dictionary<string, SavedUnderFire>(StringComparer.Ordinal);
                foreach (var e in repo.Query().With<UnderFireKnowledge>().WithLifecycle(EntityLifecycle.AllButTearDown).Build())
                {
                    if (!RecordWriterCore<UnderFireKnowledge, KnowledgeKey, UnderFireItem>.OwnsRecord(repo, e, UnderFireWriter.OwnershipKey)) continue;
                    var r = repo.GetComponentRO<UnderFireKnowledge>(e);
                    if (r.Header.SidePlus1 == 0 || r.Header.Subject == Guid.Empty || r.Cause == 0) continue;
                    var u = new SavedUnderFire
                    {
                        Subject = r.Header.Subject, SidePlus1 = r.Header.SidePlus1, Chunk = r.Header.Chunk, Cause = r.Cause, Point = r.Point, RangeM = r.RangeM,
                        EnemyStrength = r.EnemyStrength, Source = r.Header.Source, Cooled = r.Header.State == UnderFireKnowledge.StateCooled,
                        // a record not yet rebased since its own load still holds its age (UnderFireKnowledge.StateLoaded)
                        AgeSeconds = r.Header.State == UnderFireKnowledge.StateLoaded ? r.Header.LearnedAt.Seconds : Math.Max(0d, now - r.Header.LearnedAt.Seconds),
                    };
                    string key = EntryKey(u.Subject, u.SidePlus1, PlaceKnowledgeKind.UnderFire, u.Chunk);
                    if (byUf.TryGetValue(key, out var have)) MergeInto(have, u); else byUf[key] = u;
                }
                saved.UnderFire.AddRange(byUf.Values);
            }

            if (repo.IsComponentTypeRegistered<MinefieldStrikes>())
                foreach (var e in repo.Query().With<MinefieldStrikes>().WithLifecycle(EntityLifecycle.AllButTearDown).Build())
                {
                    if (!RecordWriterCore<MinefieldStrikes, Guid, StrikeItem>.OwnsRecord(repo, e, MinefieldStrikesSystem.OwnershipKey)) continue;
                    var s = repo.GetComponentRO<MinefieldStrikes>(e);
                    if (s.Field == Guid.Empty) continue;
                    for (int i = 0; i < s.Count && i < MinefieldStrikes.Capacity; i++)
                    {
                        var (row, col, how) = s.Get(i);
                        AddMine(saved, s.Field, row, col, how);
                    }
                }
            if (repo.IsComponentTypeRegistered<MinefieldLanes>())
                foreach (var e in repo.Query().With<MinefieldLanes>().WithLifecycle(EntityLifecycle.AllButTearDown).Build())
                {
                    if (!RecordWriterCore<MinefieldLanes, Guid, LaneItem>.OwnsRecord(repo, e, MinefieldLanesSystem.OwnershipKey)) continue;
                    var l = repo.GetComponentRO<MinefieldLanes>(e);
                    if (l.Field == Guid.Empty) continue;
                    for (int i = 0; i < l.Count && i < MinefieldLanes.Capacity; i++)
                    {
                        var it = l.Get(i);
                        AddLane(saved, l.Field, it.A, it.B, it.Width, it.SidePlus1);
                    }
                }
            if (saved.IsEmpty) return null;
            return ToJson(saved);
        }

        private static SavedKnowledge FromRecord(in MinefieldKnowledge r)
        {
            var k = new SavedKnowledge
            {
                Subject = r.Header.Subject, SidePlus1 = r.Header.SidePlus1, Kind = r.Header.Kind, Chunk = r.Header.Chunk,
                Marked = r.Header.Marked != 0, LearnedAt = r.Header.LearnedAt, Source = r.Header.Source,
            };
            for (int row = 0; row < MinefieldKnowledge.MaxRows; row++)
                for (int col = 0; col < MinefieldKnowledge.MaxCols; col++)
                {
                    var v = r.Get(col, row);
                    if (v != MineCellKnowledge.Unknown) k.Cells[((byte)row, (byte)col)] = (byte)v;
                }
            return k;
        }

        private static bool IsBlank(SavedKnowledge k) => !k.Marked && k.Cells.Count == 0;

        private static void AddMine(SavedPlaceKnowledge s, Guid field, short row, short col, MineGone how)
        {
            if (!s.Strikes.TryGetValue(field, out var d)) s.Strikes[field] = d = new();
            if (!d.TryGetValue((row, col), out var have) || how > have) d[(row, col)] = how;   // cleared (2) outranks struck (1): both mean "gone"
        }

        private static void AddLane(SavedPlaceKnowledge s, Guid field, Vector2 a, Vector2 b, float width, byte side)
        {
            if (!s.Lanes.TryGetValue(field, out var l)) s.Lanes[field] = l = new();
            foreach (var h in l) if (Vector2.DistanceSquared(h.A, a) < 0.01f && Vector2.DistanceSquared(h.B, b) < 0.01f) return;
            l.Add((a, b, width, side));
        }

        // ── the kind's merge policy (KR-4) ─────────────────────────────────────────
        /// <summary>Merges <paramref name="from"/> into <paramref name="into"/> by the kind's policy: the minefield's is the lattice max per cell, Marked OR, LearnedAt max.</summary>
        public static void MergeInto(SavedKnowledge into, SavedKnowledge from)
        {
            switch (into.Kind)
            {
                default:   // PlaceKnowledgeKind.Minefield — the only kind built (MF-17, KG-5)
                    foreach (var (cell, level) in from.Cells)
                        if (!into.Cells.TryGetValue(cell, out var have) || level > have) into.Cells[cell] = level;
                    break;
            }
            into.Marked |= from.Marked;
            if (from.LearnedAt > into.LearnedAt) { into.LearnedAt = from.LearnedAt; into.Source = from.Source; }
        }

        /// <summary>⭐ CE-2468 UF-3 — the under-fire kind's merge: NEWEST wins (the smaller age), the point / cause of the record kept (the same key ⇒ the same place).</summary>
        public static void MergeInto(SavedUnderFire into, SavedUnderFire from)
        {
            if (from.AgeSeconds >= into.AgeSeconds) return;
            into.AgeSeconds = from.AgeSeconds; into.RangeM = from.RangeM; into.EnemyStrength = from.EnemyStrength; into.Source = from.Source; into.Cooled = from.Cooled;
        }

        // ── json ───────────────────────────────────────────────────────────────────
        private static JsonObject ToJson(SavedPlaceKnowledge s)
        {
            var section = new JsonObject();
            var entries = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (var k in s.Knowledge) entries[EntryKey(k.Subject, k.SidePlus1, k.Kind, k.Chunk)] = KnowledgeJson(k);
            foreach (var (field, d) in s.Strikes) entries[StrikesKey(field)] = StrikesJson(d);
            foreach (var (field, l) in s.Lanes) entries[LanesKey(field)] = LanesJson(l);
            foreach (var u in s.UnderFire) entries[EntryKey(u.Subject, u.SidePlus1, PlaceKnowledgeKind.UnderFire, u.Chunk)] = UnderFireJson(u);
            foreach (var (key, node) in entries) section[key] = node;
            return section;
        }

        private static JsonObject KnowledgeJson(SavedKnowledge k)
        {
            var cells = new JsonArray();
            foreach (var ((row, col), level) in k.Cells) cells.Add(new JsonArray((int)row, (int)col, (int)level));
            var o = new JsonObject { [CellsProperty] = cells };
            if (k.Marked) o["marked"] = true;
            o["learnedAt"] = (double)k.LearnedAt;
            o["source"] = (int)k.Source;
            return o;
        }

        private static JsonObject UnderFireJson(SavedUnderFire u)
        {
            var o = new JsonObject
            {
                [UnderFireProperty] = new JsonArray((int)u.Cause, u.Point.X, u.Point.Y, u.Point.Z, u.RangeM, u.EnemyStrength),
                ["age"] = u.AgeSeconds,
                ["source"] = (int)u.Source,
            };
            if (u.Cooled) o["cooled"] = true;
            return o;
        }

        private static JsonObject StrikesJson(SortedDictionary<(short Row, short Col), MineGone> d)
        {
            var arr = new JsonArray();
            foreach (var ((row, col), how) in d) arr.Add(new JsonArray((int)row, (int)col, (int)how));
            return new JsonObject { [MinesProperty] = arr };
        }

        private static JsonObject LanesJson(List<(Vector2 A, Vector2 B, float Width, byte SidePlus1)> l)
        {
            var arr = new JsonArray();
            foreach (var (a, b, w, side) in l) arr.Add(new JsonArray(a.X, a.Y, b.X, b.Y, w, (int)side));
            return new JsonObject { [LanesProperty] = arr };
        }

        // ── load ───────────────────────────────────────────────────────────────────
        /// <summary>
        /// Everything saved in <paramref name="dom"/>. ⛔ A malformed entry fails LOUDLY (a scenario that silently loses what a side learned reloads with the field unknown
        /// to the units that paid for it); an entry of a kind this build does not know is ignored.
        /// </summary>
        public static SavedPlaceKnowledge Read(JsonObject dom)
        {
            var result = new SavedPlaceKnowledge();
            if (Of(dom) is not { } section) return result;
            foreach (var (key, node) in section)
            {
                if (node is not JsonObject entry) continue;
                string at = $"[Scenario] {Name}['{key}']";
                try
                {
                    var parts = key.Split('|');
                    if (!Guid.TryParse(parts[0], out var guid)) throw new FormatException("the key does not start with a guid");
                    if (IsKnowledgeEntry(entry))
                    {
                        if (parts.Length != 4) throw new FormatException("a knowledge key is <guid>|<side+1>|<kind>|<chunk>");
                        var kind = (PlaceKnowledgeKind)int.Parse(parts[2], CultureInfo.InvariantCulture);
                        if (kind != PlaceKnowledgeKind.Minefield) continue;   // a future kind
                        byte side = byte.Parse(parts[1], CultureInfo.InvariantCulture);
                        if (side == 0) throw new FormatException("side + 1 is 0");
                        var k = new SavedKnowledge
                        {
                            Subject = guid, SidePlus1 = side, Kind = kind, Chunk = ushort.Parse(parts[3], CultureInfo.InvariantCulture),
                            Marked = entry["marked"]?.GetValue<bool>() ?? false, LearnedAt = (SimInstant)(entry["learnedAt"]?.GetValue<double>() ?? 0d),
                            Source = (KnowledgeSource)(entry["source"]?.GetValue<int>() ?? 0),
                        };
                        var rows = (JsonArray)entry[CellsProperty]!;
                        for (int i = 0; i < rows.Count; i++)
                        {
                            if (rows[i] is not JsonArray r || r.Count != 3) throw new FormatException($"cells[{i}] is not [row, col, level]");
                            int row = r[0]!.GetValue<int>(), col = r[1]!.GetValue<int>(), level = r[2]!.GetValue<int>();
                            if ((uint)row >= MinefieldKnowledge.MaxRows || (uint)col >= MinefieldKnowledge.MaxCols || (uint)level > 3)
                                throw new FormatException($"cells[{i}] = [{row}, {col}, {level}] is outside the grid / the levels");
                            if (level > 0) k.Cells[((byte)row, (byte)col)] = (byte)level;
                        }
                        result.Knowledge.Add(k);
                    }
                    else if (IsUnderFireEntry(entry))
                    {
                        if (parts.Length != 4) throw new FormatException("an under-fire key is <guid>|<side+1>|<kind>|<chunk>");
                        if ((PlaceKnowledgeKind)int.Parse(parts[2], CultureInfo.InvariantCulture) != PlaceKnowledgeKind.UnderFire) throw new FormatException("an under-fire entry under another kind's key");
                        byte side = byte.Parse(parts[1], CultureInfo.InvariantCulture);
                        if (side == 0) throw new FormatException("side + 1 is 0");
                        var v = (JsonArray)entry[UnderFireProperty]!;
                        if (v.Count != 6) throw new FormatException("underFire is not [cause, x, y, z, range, strength]");
                        int cause = v[0]!.GetValue<int>();
                        if (cause is not ((int)UnderFireCause.Indirect or (int)UnderFireCause.Direct)) throw new FormatException($"underFire's cause {cause} is not 1 (Indirect) or 2 (Direct)");
                        double age = entry["age"]?.GetValue<double>() ?? throw new FormatException("an under-fire entry has no age");
                        if (!(age >= 0d)) throw new FormatException("age must be ≥ 0");
                        result.UnderFire.Add(new SavedUnderFire
                        {
                            Subject = guid, SidePlus1 = side, Chunk = ushort.Parse(parts[3], CultureInfo.InvariantCulture), Cause = (UnderFireCause)cause,
                            Point = new Vector3(v[1]!.GetValue<float>(), v[2]!.GetValue<float>(), v[3]!.GetValue<float>()), RangeM = v[4]!.GetValue<float>(),
                            EnemyStrength = v[5]!.GetValue<float>(), AgeSeconds = age, Source = (KnowledgeSource)(entry["source"]?.GetValue<int>() ?? 0),
                            Cooled = entry["cooled"]?.GetValue<bool>() ?? false,
                        });
                    }
                    else if (IsStrikesEntry(entry))
                    {
                        var rows = (JsonArray)entry[MinesProperty]!;
                        for (int i = 0; i < rows.Count; i++)
                        {
                            if (rows[i] is not JsonArray r || r.Count != 3) throw new FormatException($"mines[{i}] is not [row, col, how]");
                            int how = r[2]!.GetValue<int>();
                            if (how is not (1 or 2)) throw new FormatException($"mines[{i}].how must be 1 (struck) or 2 (cleared)");
                            AddMine(result, guid, checked((short)r[0]!.GetValue<int>()), checked((short)r[1]!.GetValue<int>()), (MineGone)how);
                        }
                    }
                    else if (IsLanesEntry(entry))
                    {
                        var rows = (JsonArray)entry[LanesProperty]!;
                        for (int i = 0; i < rows.Count; i++)
                        {
                            if (rows[i] is not JsonArray r || r.Count != 6) throw new FormatException($"lanes[{i}] is not [ax, ay, bx, by, width, side+1]");
                            float w = r[4]!.GetValue<float>();
                            if (!(w > 0f)) throw new FormatException($"lanes[{i}].width must be > 0");
                            AddLane(result, guid, new Vector2(r[0]!.GetValue<float>(), r[1]!.GetValue<float>()), new Vector2(r[2]!.GetValue<float>(), r[3]!.GetValue<float>()), w, checked((byte)r[5]!.GetValue<int>()));
                        }
                    }
                }
                catch (Exception e) when (e is FormatException or InvalidCastException or NullReferenceException or OverflowException or InvalidOperationException or IndexOutOfRangeException)
                {
                    throw new InvalidOperationException($"{at} is malformed: {e.Message}", e);
                }
            }
            return result;
        }

        public static SavedPlaceKnowledge Read(string json) => JsonNode.Parse(json) is JsonObject dom ? Read(dom) : new SavedPlaceKnowledge();

        // ── merge (two slices) ─────────────────────────────────────────────────────
        /// <summary>
        /// ⭐ The merge of two slices' entries under ONE key (two deciding nodes each saved their own): the kind's policy — knowledge: lattice max per cell, Marked OR;
        /// strikes: the union of mines; lanes: the union of segments. ⛔ Never throws on a duplicate key. <paramref name="have"/> and <paramref name="mine"/> are both entries of one kind.
        /// </summary>
        public static JsonObject Merge(string key, JsonObject have, JsonObject mine)
        {
            var a = Read(new JsonObject { [Name] = new JsonObject { [key] = have.DeepClone() } });
            var b = Read(new JsonObject { [Name] = new JsonObject { [key] = mine.DeepClone() } });
            var merged = new SavedPlaceKnowledge();
            var byKey = new Dictionary<string, SavedKnowledge>(StringComparer.Ordinal);
            foreach (var k in a.Knowledge) byKey[EntryKey(k.Subject, k.SidePlus1, k.Kind, k.Chunk)] = k;
            foreach (var k in b.Knowledge)
            {
                string kk = EntryKey(k.Subject, k.SidePlus1, k.Kind, k.Chunk);
                if (byKey.TryGetValue(kk, out var h)) MergeInto(h, k); else byKey[kk] = k;
            }
            merged.Knowledge.AddRange(byKey.Values);
            var byUf = new Dictionary<string, SavedUnderFire>(StringComparer.Ordinal);
            foreach (var u in a.UnderFire) byUf[EntryKey(u.Subject, u.SidePlus1, PlaceKnowledgeKind.UnderFire, u.Chunk)] = u;
            foreach (var u in b.UnderFire)
            {
                string uk = EntryKey(u.Subject, u.SidePlus1, PlaceKnowledgeKind.UnderFire, u.Chunk);
                if (byUf.TryGetValue(uk, out var h)) MergeInto(h, u); else byUf[uk] = u;
            }
            merged.UnderFire.AddRange(byUf.Values);
            foreach (var src in new[] { a, b })
            {
                foreach (var (f, d) in src.Strikes) foreach (var ((row, col), how) in d) AddMine(merged, f, row, col, how);
                foreach (var (f, l) in src.Lanes) foreach (var (la, lb, w, side) in l) AddLane(merged, f, la, lb, w, side);
            }
            var json = ToJson(merged);
            return (JsonObject)json[key]!.DeepClone();
        }
    }
}
