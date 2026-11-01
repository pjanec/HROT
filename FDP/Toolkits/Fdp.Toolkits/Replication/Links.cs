using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Toolkit.Weather;

namespace Fdp.Toolkit.Replication
{
    // ════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // ⭐⭐ The AUTHORED types — what a PERSON writes (params DTOs, trigger when / do args, macro params): the resolved answer PLUS
    // the RefSpec saying how it was written. 📄 docs/DESIGN_Scenario_Rules.md §9.8.0 / §9.8.8 SR-140 / §9.8.9 SR-141 / §9.8.4 SR-134.
    // Code-written data uses the INTERNAL types (EntityRef, WorldPoint, WorldRotation, Bearing) — ⛔ never a *Link in a component.
    //
    // JSON (SR-138, S0 as-built): the SHAPE says the form — no "kind" field — and the writer picks the shape from the VALUE's state:
    //   not authored by reference      ⇒ the bare answer (a number / [x,y,z]) — unchanged for every existing client and file
    //   authored, not resolved yet     ⇒ the authored text ("@BlueHQ" / a GUID) — what a hand-written file holds
    //   authored AND resolved          ⇒ both, {"NetworkId": n, "Ref": "@BlueHQ"} — so resolution can rewrite a params JSON in place and
    //                                    the behaviour still reads the answer; a diagnostics dump shows both; every form reads back.
    // ════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ An AUTHORED reference to an entity: the resolved <see cref="EntityRef"/> (behaviour code reads <see cref="Ref"/>) plus how a
    /// person wrote it (<see cref="Spec"/> — a GUID = this exact entity, <c>"@Key"</c> = whatever entity has that key here, or a bare
    /// network id). Resolution FILLS <see cref="Ref"/> and KEEPS <see cref="Spec"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [JsonConverter(typeof(EntityLinkJsonConverter))]
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct EntityLink : IEquatable<EntityLink>
    {
        /// <summary>The resolved answer (<see cref="EntityRef.None"/> until resolved, or when nothing is referenced).</summary>
        public readonly EntityRef Ref;
        /// <summary>How it was authored (<see cref="RefSpec.None"/> = a bare network id).</summary>
        public readonly RefSpec Spec;

        public EntityLink(EntityRef reference, RefSpec spec) { Ref = reference; Spec = spec; }
        public EntityLink(EntityRef reference) { Ref = reference; Spec = default; }

        /// <summary>Nothing referenced.</summary>
        public static EntityLink None => default;

        /// <summary><c>true</c> when neither an answer nor an authored form is present.</summary>
        public bool IsNone => Ref.IsNone && Spec.IsNone;

        /// <summary>The same authored form with a new answer (what resolution and remapping produce).</summary>
        public EntityLink WithRef(EntityRef reference) => new(reference, Spec);

        /// <summary>A link written as a bare reference (code → param: the picker's id pick, a test, a migration).</summary>
        public static implicit operator EntityLink(EntityRef reference) => new(reference);

        /// <summary>⛔ Explicit: reading the answer drops the authored form — say so at the call site (<c>link.Ref</c> reads better).</summary>
        public static explicit operator EntityRef(EntityLink link) => link.Ref;

        public bool Equals(EntityLink other) => Ref == other.Ref && Spec == other.Spec;
        public override bool Equals(object? obj) => obj is EntityLink l && Equals(l);
        public override int GetHashCode() => HashCode.Combine(Ref, Spec);
        public static bool operator ==(EntityLink a, EntityLink b) => a.Equals(b);
        public static bool operator !=(EntityLink a, EntityLink b) => !a.Equals(b);

        /// <summary><c>@BlueHQ → #9812</c> · <c>@BlueHQ → unresolved</c> · <c>#9812</c> · <c>(none)</c>.</summary>
        public override string ToString()
            => Spec.IsNone ? Ref.ToString() : $"{Spec} → " + (Ref.IsNone ? "unresolved" : Ref.ToString());
    }

    /// <summary>
    /// ⭐⭐ An AUTHORED place: the resolved <see cref="WorldPoint"/> (<see cref="Local"/>, local metres) plus how it was written —
    /// coordinates (<see cref="RefSpec.None"/>; relocated with a macro) or an entity's position (<c>"@Key"</c> / a GUID; resolved into
    /// <see cref="Local"/> ONCE when assigned, never relocated — SR-130 / SR-131). ⛔ No geodetic form (SR-135): lat / lon is the outer edge's.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [JsonConverter(typeof(PointLinkJsonConverter))]
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct PointLink : IEquatable<PointLink>
    {
        /// <summary>The place, local metres (the answer).</summary>
        public readonly WorldPoint Local;
        /// <summary>How it was authored (<see cref="RefSpec.None"/> = coordinates).</summary>
        public readonly RefSpec Spec;

        public PointLink(WorldPoint local, RefSpec spec) { Local = local; Spec = spec; }
        public PointLink(WorldPoint local) { Local = local; Spec = default; }

        /// <summary>The same authored form with a new answer.</summary>
        public PointLink WithLocal(WorldPoint local) => new(local, Spec);

        /// <summary>Coordinates written as a literal (a picked map point, a test, a migration).</summary>
        public static implicit operator PointLink(WorldPoint local) => new(local);
        /// <inheritdoc cref="op_Implicit(WorldPoint)"/>
        public static implicit operator PointLink(Vector3 local) => new(local);
        /// <summary>The place — the authored form is not part of the answer, so reading it is implicit (the ~40 behaviour sites keep compiling).</summary>
        public static implicit operator WorldPoint(PointLink link) => link.Local;
        /// <inheritdoc cref="op_Implicit(PointLink)"/>
        public static implicit operator Vector3(PointLink link) => link.Local;

        public bool Equals(PointLink other) => Local == other.Local && Spec == other.Spec;
        public override bool Equals(object? obj) => obj is PointLink p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(Local, Spec);
        public static bool operator ==(PointLink a, PointLink b) => a.Equals(b);
        public static bool operator !=(PointLink a, PointLink b) => !a.Equals(b);

        public override string ToString() => Spec.IsNone ? Local.ToString() : $"{Spec} → {Local}";
    }

    /// <summary>
    /// ⭐⭐ An AUTHORED area — one type for every shape (SR-134): a circle (<see cref="Center"/> + radius in <see cref="Size"/>.X), a rotated
    /// rectangle (<see cref="Center"/> + width × length in <see cref="Size"/>, <see cref="Heading"/> the compass direction of its length), or
    /// an AREA ENTITY (<c>"@Village"</c> / a GUID / an id — its <c>EditablePolyline</c> outline; resolved into <see cref="Area"/>).
    /// ⭐ ONE containment for every user: <see cref="Contains"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [JsonConverter(typeof(AreaLinkJsonConverter))]
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct AreaLink : IEquatable<AreaLink>
    {
        /// <summary>The area entity (entity form; the answer once resolved).</summary>
        public readonly EntityRef Area;
        /// <summary>The form: <see cref="RefForm.Circle"/> / <see cref="RefForm.Rectangle"/> / <see cref="RefForm.Exact"/> / <see cref="RefForm.Key"/>; <see cref="RefForm.None"/> with <see cref="Area"/> set = a bare id.</summary>
        public readonly RefSpec Spec;
        /// <summary>The shape's centre, local metres (circle / rectangle).</summary>
        public readonly WorldPoint Center;
        /// <summary>Rectangle: X = width (across the heading), Y = length (along it), metres · circle: X = radius.</summary>
        public readonly Vector2 Size;
        /// <summary>Rectangle: the compass direction of its length (0 = north).</summary>
        public readonly Bearing Heading;

        private AreaLink(EntityRef area, RefSpec spec, WorldPoint center, Vector2 size, Bearing heading)
        { Area = area; Spec = spec; Center = center; Size = size; Heading = heading; }

        /// <summary>A circle of <paramref name="radius"/> metres around <paramref name="center"/>.</summary>
        public static AreaLink Circle(WorldPoint center, float radius)
            => new(default, RefSpec.Shape(RefForm.Circle), center, new Vector2(radius, 0f), default);

        /// <summary>A rectangle <paramref name="width"/> × <paramref name="length"/> metres around <paramref name="center"/>, its length along <paramref name="heading"/>.</summary>
        public static AreaLink Rectangle(WorldPoint center, float width, float length, Bearing heading)
            => new(default, RefSpec.Shape(RefForm.Rectangle), center, new Vector2(width, length), heading);

        /// <summary>An area entity, written as <paramref name="spec"/> (a key / GUID) and/or already resolved to <paramref name="area"/>.</summary>
        public static AreaLink OfEntity(EntityRef area, RefSpec spec = default)
            => spec.Form is RefForm.Circle or RefForm.Rectangle ? throw new ArgumentException("an entity area is written as a key, a GUID or an id", nameof(spec))
                                                                : new(area, spec, default, default, default);

        /// <summary>The same authored form with a new area entity answer.</summary>
        public AreaLink WithArea(EntityRef area) => new(area, Spec, Center, Size, Heading);

        /// <summary>The same shape moved / turned (relocation): a new centre and heading, the size kept.</summary>
        public AreaLink WithPlacement(WorldPoint center, Bearing heading) => new(Area, Spec, center, Size, heading);

        /// <summary><c>true</c> for a circle or a rectangle (the shape is in the value); <c>false</c> for an area entity.</summary>
        public bool IsShape => Spec.Form is RefForm.Circle or RefForm.Rectangle;

        /// <summary><c>true</c> when nothing is described.</summary>
        public bool IsNone => !IsShape && Area.IsNone && Spec.IsNone;

        /// <summary>
        /// ⭐⭐ Whether <paramref name="point"/> (its XY; heights ignored) is inside — the ONE area containment (SR-134): the circle and
        /// rectangle maths here, an area entity through <see cref="WeatherField.AreaEntityContains"/> (its outline + the even-odd test).
        /// An unresolved or vanished area entity contains nothing.
        /// </summary>
        public bool Contains(EntityRepository? world, Vector3 point)
        {
            var d = new Vector2(point.X - Center.X, point.Y - Center.Y);
            switch (Spec.Form)
            {
                case RefForm.Circle:
                    return d.LengthSquared() <= Size.X * Size.X;
                case RefForm.Rectangle:
                {
                    float rad = Heading.Degrees * (MathF.PI / 180f);
                    var along  = new Vector2(MathF.Sin(rad), MathF.Cos(rad));    // compass: 0 = +Y (north), 90 = +X (east)
                    var across = new Vector2(along.Y, -along.X);
                    return MathF.Abs(Vector2.Dot(d, across)) <= Size.X * 0.5f && MathF.Abs(Vector2.Dot(d, along)) <= Size.Y * 0.5f;
                }
                default:
                    if (world is null || Area.IsNone) return false;
                    var e = Area.Resolve(world);
                    return !e.IsNull && WeatherField.AreaEntityContains(world, e, new Vector2(point.X, point.Y));
            }
        }

        public bool Equals(AreaLink other)
            => Area == other.Area && Spec == other.Spec && Center == other.Center && Size == other.Size && Heading.Equals(other.Heading);
        public override bool Equals(object? obj) => obj is AreaLink a && Equals(a);
        public override int GetHashCode() => HashCode.Combine(Area, Spec, Center, Size, Heading);
        public static bool operator ==(AreaLink a, AreaLink b) => a.Equals(b);
        public static bool operator !=(AreaLink a, AreaLink b) => !a.Equals(b);

        public override string ToString() => Spec.Form switch
        {
            RefForm.Circle    => $"circle r={Size.X:0.#} m at {Center}",
            RefForm.Rectangle => $"rect {Size.X:0.#}×{Size.Y:0.#} m at {Center}, {Heading}",
            RefForm.None      => Area.ToString(),
            _                 => $"{Spec} → " + (Area.IsNone ? "unresolved" : Area.ToString()),
        };
    }

    // ── the converters ─────────────────────────────────────────────────────────────────────────────────────────────────────

    internal static class LinkJson
    {
        /// <summary>The authored TEXT of a string token — <c>"@Key"</c> or a GUID — or a loud <see cref="JsonException"/>.</summary>
        public static RefSpec Spec(string? text, string type)
        {
            try
            {
                if (RefSpec.TryParse(text, out var spec)) return spec;
            }
            catch (ArgumentException ex) { throw new JsonException($"{type}: {ex.Message}", ex); }
            throw new JsonException($"{type}: '{text}' is neither \"@Key\" nor an entity GUID.");
        }

        public static int ReadId(ref Utf8JsonReader reader, string type) => reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetInt32(),
            JsonTokenType.Null   => 0,
            JsonTokenType.String when int.TryParse(reader.GetString(), out int n) => n,
            _ => throw new JsonException($"{type}: expected a network id, saw {reader.TokenType}."),
        };
    }

    /// <summary><see cref="EntityLink"/>: a number (a bare id) · <c>"@Key"</c> · a GUID · <c>{"NetworkId": n, "Ref": "@Key"}</c> (both) · <c>null</c>.</summary>
    public sealed class EntityLinkJsonConverter : JsonConverter<EntityLink>
    {
        public override EntityLink Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:   return EntityLink.None;
                case JsonTokenType.Number: return new EntityLink(new EntityRef(reader.GetInt32()));
                case JsonTokenType.String:
                    var s = reader.GetString();
                    if (string.IsNullOrWhiteSpace(s)) return EntityLink.None;
                    if (int.TryParse(s, out int id)) return new EntityLink(new EntityRef(id));
                    return new EntityLink(EntityRef.None, LinkJson.Spec(s, nameof(EntityLink)));
                case JsonTokenType.StartObject:
                    int nid = 0; RefSpec spec = default;
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType != JsonTokenType.PropertyName) continue;
                        string name = reader.GetString()!;
                        reader.Read();
                        if (name.Equals("NetworkId", StringComparison.OrdinalIgnoreCase)) nid = LinkJson.ReadId(ref reader, nameof(EntityLink));
                        else if (name.Equals("Ref", StringComparison.OrdinalIgnoreCase) && reader.TokenType == JsonTokenType.String)
                            spec = LinkJson.Spec(reader.GetString(), nameof(EntityLink));
                        else reader.Skip();
                    }
                    return new EntityLink(new EntityRef(nid), spec);
                default:
                    throw new JsonException($"EntityLink: unexpected {reader.TokenType}.");
            }
        }

        public override void Write(Utf8JsonWriter writer, EntityLink value, JsonSerializerOptions options)
        {
            if (value.Spec.IsNone) { writer.WriteNumberValue(value.Ref.NetworkId); return; }
            if (value.Ref.IsNone)  { writer.WriteStringValue(value.Spec.ToString()); return; }
            writer.WriteStartObject();
            writer.WriteNumber("NetworkId", value.Ref.NetworkId);
            writer.WriteString("Ref", value.Spec.ToString());
            writer.WriteEndObject();
        }
    }

    /// <summary><see cref="PointLink"/>: <c>[x, y, z]</c> (coordinates — the options' own <see cref="Vector3"/> form) · <c>"@Key"</c> · a GUID · <c>{"Local": [x, y, z], "Ref": "@Key"}</c> (both).</summary>
    public sealed class PointLinkJsonConverter : JsonConverter<PointLink>
    {
        public override PointLink Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:       return default;
                case JsonTokenType.StartArray: return new PointLink(JsonSerializer.Deserialize<Vector3>(ref reader, options));
                case JsonTokenType.String:     return new PointLink(default, LinkJson.Spec(reader.GetString(), nameof(PointLink)));
                case JsonTokenType.StartObject:
                    Vector3 local = default; RefSpec spec = default;
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType != JsonTokenType.PropertyName) continue;
                        string name = reader.GetString()!;
                        reader.Read();
                        if (name.Equals("Local", StringComparison.OrdinalIgnoreCase)) local = JsonSerializer.Deserialize<Vector3>(ref reader, options);
                        else if (name.Equals("Ref", StringComparison.OrdinalIgnoreCase) && reader.TokenType == JsonTokenType.String)
                            spec = LinkJson.Spec(reader.GetString(), nameof(PointLink));
                        else reader.Skip();
                    }
                    return new PointLink(local, spec);
                default:
                    throw new JsonException($"PointLink: unexpected {reader.TokenType} (a point is [x, y, z], \"@Key\" or an entity GUID).");
            }
        }

        public override void Write(Utf8JsonWriter writer, PointLink value, JsonSerializerOptions options)
        {
            if (value.Spec.IsNone) { JsonSerializer.Serialize(writer, (Vector3)value.Local, options); return; }
            if (value.Local == WorldPoint.Zero) { writer.WriteStringValue(value.Spec.ToString()); return; }   // not resolved yet
            writer.WriteStartObject();
            writer.WritePropertyName("Local");
            JsonSerializer.Serialize(writer, (Vector3)value.Local, options);
            writer.WriteString("Ref", value.Spec.ToString());
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// <see cref="AreaLink"/>: <c>{"circle": {"center": [x, y, z], "radius": r}}</c> · <c>{"rect": {"center": …, "size": [w, l], "heading": h}}</c>
    /// · an area entity <c>"@Key"</c> / a GUID / a number · <c>{"Area": n, "Ref": "@Key"}</c> (both) · <c>null</c>.
    /// </summary>
    public sealed class AreaLinkJsonConverter : JsonConverter<AreaLink>
    {
        public override AreaLink Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:   return default;
                case JsonTokenType.Number: return AreaLink.OfEntity(new EntityRef(reader.GetInt32()));
                case JsonTokenType.String:
                    var s = reader.GetString();
                    if (int.TryParse(s, out int id)) return AreaLink.OfEntity(new EntityRef(id));
                    return AreaLink.OfEntity(EntityRef.None, LinkJson.Spec(s, nameof(AreaLink)));
                case JsonTokenType.StartObject:
                    int area = 0; RefSpec spec = default; AreaLink? shape = null;
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType != JsonTokenType.PropertyName) continue;
                        string name = reader.GetString()!;
                        reader.Read();
                        if (name.Equals("circle", StringComparison.OrdinalIgnoreCase)) shape = ReadShape(ref reader, options, RefForm.Circle);
                        else if (name.Equals("rect", StringComparison.OrdinalIgnoreCase)) shape = ReadShape(ref reader, options, RefForm.Rectangle);
                        else if (name.Equals("Area", StringComparison.OrdinalIgnoreCase)) area = LinkJson.ReadId(ref reader, nameof(AreaLink));
                        else if (name.Equals("Ref", StringComparison.OrdinalIgnoreCase) && reader.TokenType == JsonTokenType.String)
                            spec = LinkJson.Spec(reader.GetString(), nameof(AreaLink));
                        else reader.Skip();
                    }
                    return shape ?? AreaLink.OfEntity(new EntityRef(area), spec);
                default:
                    throw new JsonException($"AreaLink: unexpected {reader.TokenType}.");
            }
        }

        private static AreaLink ReadShape(ref Utf8JsonReader reader, JsonSerializerOptions options, RefForm form)
        {
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException($"AreaLink: a {form} is an object.");
            Vector3 center = default; float radius = 0, heading = 0; Vector2 size = default;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) continue;
                string name = reader.GetString()!.ToLowerInvariant();
                reader.Read();
                switch (name)
                {
                    case "center":  center  = JsonSerializer.Deserialize<Vector3>(ref reader, options); break;
                    case "radius":  radius  = reader.GetSingle(); break;
                    case "size":    size    = JsonSerializer.Deserialize<Vector2>(ref reader, options); break;
                    case "heading": heading = reader.GetSingle(); break;
                    default:        reader.Skip(); break;
                }
            }
            return form == RefForm.Circle ? AreaLink.Circle(center, radius) : AreaLink.Rectangle(center, size.X, size.Y, heading);
        }

        public override void Write(Utf8JsonWriter writer, AreaLink value, JsonSerializerOptions options)
        {
            switch (value.Spec.Form)
            {
                case RefForm.Circle:
                    writer.WriteStartObject(); writer.WritePropertyName("circle"); writer.WriteStartObject();
                    writer.WritePropertyName("center"); JsonSerializer.Serialize(writer, (Vector3)value.Center, options);
                    writer.WriteNumber("radius", value.Size.X);
                    writer.WriteEndObject(); writer.WriteEndObject();
                    return;
                case RefForm.Rectangle:
                    writer.WriteStartObject(); writer.WritePropertyName("rect"); writer.WriteStartObject();
                    writer.WritePropertyName("center"); JsonSerializer.Serialize(writer, (Vector3)value.Center, options);
                    writer.WritePropertyName("size"); JsonSerializer.Serialize(writer, value.Size, options);
                    writer.WriteNumber("heading", value.Heading.Degrees);
                    writer.WriteEndObject(); writer.WriteEndObject();
                    return;
                case RefForm.None:
                    if (value.Area.IsNone) writer.WriteNullValue(); else writer.WriteNumberValue(value.Area.NetworkId);
                    return;
                default:
                    if (value.Area.IsNone) { writer.WriteStringValue(value.Spec.ToString()); return; }
                    writer.WriteStartObject();
                    writer.WriteNumber("Area", value.Area.NetworkId);
                    writer.WriteString("Ref", value.Spec.ToString());
                    writer.WriteEndObject();
                    return;
            }
        }
    }
}
