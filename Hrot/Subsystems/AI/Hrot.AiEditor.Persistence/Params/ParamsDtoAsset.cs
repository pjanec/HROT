using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hrot.AiEditor.Persistence.Params;

/// <summary>
/// ⭐⭐ <c>CE-2136</c> (B4a, docs/DESIGN_Orders_Params_Entity_Classes.md §12) — a <c>*.params.json</c> asset: an editor-authored params DTO.
/// The JSON is the source; <c>ParamsDtoJsonGenerator</c> turns it into a <c>[ParamsDto]</c> struct at build (the Category-2 path the
/// editor-managed blackboard takes), so a behaviour can name it as its params DTO with no hand-written C#.
/// </summary>
public sealed class ParamsDtoAssetDto
{
    /// <summary>⭐ <c>CE-3185</c> (F1, R-2000) — the asset's stable id (a GUID), like every <c>.bp</c> / <c>.btree</c> / <c>.hsm</c> asset. The
    /// generator emits it into <c>[ParamsDto(AssetId = …)]</c>, so the DTO's origin survives the file moving. Required.</summary>
    public string? AssetId { get; set; }

    /// <summary>The DTO's type name (a C# identifier); ⭐ <c>CE-3185</c> — also the asset's file name (<c>&lt;Name&gt;.params.json</c>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The namespace; <see cref="ParamsDtoEmitCore.DefaultNamespace"/> when empty.</summary>
    public string? Namespace { get; set; }

    /// <summary>What the DTO is for (the generated type's doc comment).</summary>
    public string? Comment { get; set; }

    /// <summary>Set ⇒ the DTO is an ORDER's DTO: the generator adds <c>[Order(OrderName)]</c> (§1 O1).</summary>
    public string? OrderName { get; set; }

    /// <summary>The <c>OrderGesture</c> member name that issues the order with no menu (an RTS click), if any.</summary>
    public string? Gesture { get; set; }

    /// <summary>The fields, in layout order.</summary>
    public List<ParamsDtoFieldDto> Fields { get; set; } = new();
}

/// <summary>One field of a <see cref="ParamsDtoAssetDto"/>.</summary>
public sealed class ParamsDtoFieldDto
{
    /// <summary>The field name (a C# identifier); also its JSON key.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The field's type, by metadata name (<c>System.Single</c>, <c>System.Numerics.Vector3</c>, <c>Ns.Outer+Inner</c>).</summary>
    public string TypeId { get; set; } = string.Empty;

    /// <summary>The field's default as JSON (<c>1.5</c>, <c>[0,0,0]</c>, <c>"Prefer"</c>); empty = the type's default.</summary>
    public string? DefaultJson { get; set; }

    /// <summary>What the field means (the generated field's doc comment).</summary>
    public string? Comment { get; set; }
}

/// <summary>Reads a <c>*.params.json</c>.</summary>
public static class ParamsDtoJson
{
    public const string Extension = ".params.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>The asset, or throws on malformed JSON.</summary>
    public static ParamsDtoAssetDto Deserialize(string text)
        => JsonSerializer.Deserialize<ParamsDtoAssetDto>(text, Options)
           ?? throw new JsonException("a params DTO asset is empty");

    public static string Serialize(ParamsDtoAssetDto dto) => JsonSerializer.Serialize(dto, Options);

    /// <summary>
    /// ⭐ <c>CE-3185</c> (§13.7 P1-g) — the <c>*.params.json</c> under <paramref name="root"/> whose <c>AssetId</c> is
    /// <paramref name="assetId"/>, or null: the "open asset" locator for a generated params DTO (its <c>[ParamsDto(AssetId)]</c>) until the
    /// asset gets an editor and a catalog contributor (B4b). Unreadable files are skipped.
    /// </summary>
    public static string? FindByAssetId(string root, Guid assetId)
    {
        if (string.IsNullOrEmpty(root) || !System.IO.Directory.Exists(root)) return null;
        foreach (var path in System.IO.Directory.EnumerateFiles(root, "*" + Extension, System.IO.SearchOption.AllDirectories))
        {
            try
            {
                if (Guid.TryParse(Deserialize(System.IO.File.ReadAllText(path)).AssetId, out var id) && id == assetId) return path;
            }
            catch (Exception) { /* not a params asset we can read: skip */ }
        }
        return null;
    }
}

/// <summary>
/// ⭐ <c>CE-2136</c> — the params DTO asset's C#: one <c>[StructLayout(Sequential)]</c> struct with a public field per row,
/// <c>[ParamsDto(AssetId = …)]</c> (so <c>ParamsDtoRegistry</c> lists it) and <c>[Order]</c> when it names an order.
/// ⭐⭐ <c>CE-3194</c> (R-3002) — a field's default is its INITIALISER, and the struct gets a parameterless constructor so
/// <c>new T()</c> applies them; ⛔ the defaults are no longer copied into <c>[ParamsDto(DefaultJson)]</c> (retired). ⚠ Field
/// types are emitted from their type ids (<c>global::Ns.T</c>) — a generator sees only JSON text, so an unknown type is a compile
/// error in the generated file, never a guess.
/// </summary>
public static class ParamsDtoEmitCore
{
    public const string DefaultNamespace = "Hrot.AI.Behaviors.Params";
    private const string Attr = "global::Fdp.Core.ParamsDto";   // ⭐ CE-3185 — moved to Fdp.Core
    private const string OrderAttr = "global::Hrot.Map.Definitions.Behavior.Orders.Order";
    private const string GestureEnum = "global::Hrot.Map.Definitions.Behavior.Orders.OrderGesture";

    /// <summary>The generated type's metadata name — what an asset's <c>ParamsDtoTypeId</c> names.</summary>
    public static string TypeIdOf(ParamsDtoAssetDto dto)
        => (string.IsNullOrWhiteSpace(dto.Namespace) ? DefaultNamespace : dto.Namespace!.Trim()) + "." + dto.Name.Trim();

    /// <summary>The asset's problems, or an empty list: a name and every field name must be identifiers, field names unique, every
    /// field typed, every default valid JSON.</summary>
    public static IReadOnlyList<string> Validate(ParamsDtoAssetDto dto)
    {
        var errors = new List<string>();
        if (!IsIdentifier(dto.Name)) errors.Add($"'{dto.Name}' is not a C# identifier (the DTO's name)");
        // ⭐ CE-3185 (F1) — the asset's identity travels into the DTO's [ParamsDto(AssetId)]; without one the origin is lost.
        if (!Guid.TryParse(dto.AssetId ?? "", out var id) || id == Guid.Empty)
            errors.Add($"names no AssetId (a non-empty GUID) — every params DTO asset has one (CE-3185)");
        if (!string.IsNullOrWhiteSpace(dto.Namespace))
            foreach (var part in dto.Namespace!.Trim().Split('.'))
                if (!IsIdentifier(part)) { errors.Add($"namespace '{dto.Namespace}' is not a dotted C# name"); break; }
        if (dto.OrderName != null && !IsIdentifier(dto.OrderName)) errors.Add($"order name '{dto.OrderName}' is not an identifier");
        if (dto.Gesture != null && dto.OrderName == null) errors.Add("a Gesture needs an OrderName");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in dto.Fields ?? new List<ParamsDtoFieldDto>())
        {
            if (!IsIdentifier(f.Name)) errors.Add($"field '{f.Name}' is not a C# identifier");
            else if (!seen.Add(f.Name)) errors.Add($"field '{f.Name}' is declared twice");
            if (string.IsNullOrWhiteSpace(f.TypeId)) errors.Add($"field '{f.Name}' names no type");
            if (!string.IsNullOrWhiteSpace(f.DefaultJson))
            {
                try { using var _ = JsonDocument.Parse(f.DefaultJson!); }
                catch (JsonException) { errors.Add($"field '{f.Name}': the default '{f.DefaultJson}' is not JSON"); }
            }
        }
        return errors;
    }

    /// <summary>The generated source. Call <see cref="Validate"/> first; this assumes a valid asset.</summary>
    public static string Emit(ParamsDtoAssetDto dto, string sourcePath)
    {
        string ns = string.IsNullOrWhiteSpace(dto.Namespace) ? DefaultNamespace : dto.Namespace!.Trim();
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine($"// ⭐ CE-2136 — generated from {System.IO.Path.GetFileName(sourcePath)} (a params DTO asset). Edit the asset, not this file.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine($"namespace {ns}");
        sb.AppendLine("{");
        Doc(sb, "    ", dto.Comment);
        sb.AppendLine($"    [{Attr}(AssetId = {Literal(Guid.Parse(dto.AssetId!).ToString("D"))})]");
        if (dto.OrderName != null)
            sb.AppendLine(dto.Gesture == null
                ? $"    [{OrderAttr}({Literal(dto.OrderName)})]"
                : $"    [{OrderAttr}({Literal(dto.OrderName)}, Gesture = {GestureEnum}.{dto.Gesture})]");
        sb.AppendLine("    [global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
        sb.AppendLine($"    public struct {dto.Name.Trim()}");
        sb.AppendLine("    {");
        foreach (var f in dto.Fields ?? new List<ParamsDtoFieldDto>())
        {
            Doc(sb, "        ", f.Comment);
            string init = string.IsNullOrWhiteSpace(f.DefaultJson) ? "" : " = " + InitialiserOf(f.TypeId, f.DefaultJson!);
            sb.AppendLine($"        public {TypeRef(f.TypeId)} {f.Name}{init};");
        }
        // ⭐ CE-3194 (R-3002) — C# applies field initialisers only through a constructor call (CS8983 without one).
        sb.AppendLine();
        sb.AppendLine($"        public {dto.Name.Trim()}() {{ }}");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>One laid-out field of a generated DTO.</summary>
    public readonly struct LaidOutField
    {
        public LaidOutField(string name, string typeId, int offset, int size) { Name = name; TypeId = typeId; Offset = offset; Size = size; }
        public string Name { get; }
        public string TypeId { get; }
        public int Offset { get; }
        public int Size { get; }
    }

    /// <summary>
    /// The generated struct's CLR layout (<c>LayoutKind.Sequential</c>: each field at the next multiple of its alignment, the whole
    /// rounded up to the largest) — what a sibling generator needs to size it or bind <c>Params.&lt;field&gt;</c> in the SAME build
    /// pass, before the struct exists. ⛔ False when any field type cannot be laid out (fail closed, never a guess).
    /// </summary>
    public static bool TryLayout(ParamsDtoAssetDto dto, Func<string, (int Size, int Align)?> fieldLayout,
        out IReadOnlyList<LaidOutField> fields, out int size)
    {
        var list = new List<LaidOutField>();
        int offset = 0, maxAlign = 1;
        foreach (var f in dto.Fields ?? new List<ParamsDtoFieldDto>())
        {
            var l = fieldLayout(f.TypeId);
            if (l == null || l.Value.Size < 0) { fields = list; size = -1; return false; }
            int align = Math.Max(1, l.Value.Align);
            if (offset % align != 0) offset += align - offset % align;
            list.Add(new LaidOutField(f.Name, f.TypeId, offset, l.Value.Size));
            offset += l.Value.Size;
            maxAlign = Math.Max(maxAlign, align);
        }
        if (offset % maxAlign != 0) offset += maxAlign - offset % maxAlign;
        fields = list;
        size = list.Count == 0 ? 1 : offset;   // an empty struct is 1 byte in the CLR
        return true;
    }

    /// <summary><c>Ns.Outer+Inner</c> → <c>global::Ns.Outer.Inner</c>.</summary>
    public static string TypeRef(string typeId)
    {
        string id = typeId.Trim();
        if (id.StartsWith("global::", StringComparison.Ordinal)) id = id.Substring("global::".Length);
        return "global::" + id.Replace('+', '.');
    }

    private static void Doc(StringBuilder sb, string pad, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        string esc = text!.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r", "").Replace("\n", " ");
        sb.AppendLine($"{pad}/// <summary>{esc}</summary>");
    }

    /// <summary>
    /// ⭐ <c>CE-3194</c> — a field's JSON default as a C# initialiser: a literal for the primitives and the <c>System.Numerics</c>
    /// vectors (no JSON parse each time the struct is made), else the platform's own JSON read of it — the same options the params
    /// JSON is read with (<c>BehaviorParams.JsonOptions</c>: enums by name, vectors as arrays), so a default and an order's value
    /// cannot read differently.
    /// </summary>
    internal static string InitialiserOf(string typeId, string json)
    {
        string id = typeId.Trim();
        if (id.StartsWith("global::", StringComparison.Ordinal)) id = id.Substring("global::".Length);
        using var doc = JsonDocument.Parse(json);
        var e = doc.RootElement;
        string Num(JsonElement n, string suffix) => n.GetRawText() + suffix;
        switch (id)
        {
            case "System.Single"  when e.ValueKind == JsonValueKind.Number: return Num(e, "f");
            case "System.Double"  when e.ValueKind == JsonValueKind.Number: return Num(e, "d");
            case "System.Decimal" when e.ValueKind == JsonValueKind.Number: return Num(e, "m");
            case "System.Int32" or "System.Int16" or "System.Byte" or "System.SByte" or "System.UInt16"
                when e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out _):
                return $"({TypeRef(id)}){e.GetRawText()}";
            case "System.Int64"  when e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out _):  return e.GetRawText() + "L";
            case "System.UInt32" when e.ValueKind == JsonValueKind.Number && e.TryGetUInt32(out _): return e.GetRawText() + "u";
            case "System.UInt64" when e.ValueKind == JsonValueKind.Number && e.TryGetUInt64(out _): return e.GetRawText() + "ul";
            case "System.Boolean" when e.ValueKind is JsonValueKind.True or JsonValueKind.False: return e.ValueKind == JsonValueKind.True ? "true" : "false";
            case "System.String"  when e.ValueKind == JsonValueKind.String: return Literal(e.GetString()!);
            case "System.Numerics.Vector2" or "System.Numerics.Vector3" or "System.Numerics.Vector4"
                when e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == id[id.Length - 1] - '0'
                     && e.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Number):
                return $"new {TypeRef(id)}({string.Join(", ", e.EnumerateArray().Select(x => x.GetRawText() + "f"))})";
        }
        return $"global::System.Text.Json.JsonSerializer.Deserialize<{TypeRef(id)}>({Literal(Compact(json))}, global::Fdp.Toolkit.Behavior.BehaviorParams.JsonOptions)!";
    }

    private static string Literal(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Compact(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement);
    }

    private static bool IsIdentifier(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (!(char.IsLetter(s![0]) || s[0] == '_')) return false;
        for (int i = 1; i < s.Length; i++)
            if (!(char.IsLetterOrDigit(s[i]) || s[i] == '_')) return false;
        return true;
    }
}
