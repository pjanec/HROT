using System.Text.Json;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Determinism;
using Hrot.Blueprints.Core.Compiler.Emit;

namespace Hrot.Blueprints.Core.Compiler;

/// <summary>
/// Lightweight JSON extractor for per-asset signature metadata.
/// Reads only identity, dispatch, and callable-export info.
/// Does NOT parse nodes or links.
/// </summary>
public static class BlueprintSignatureParser
{
    public static BlueprintSignature Parse(string filePath, string jsonText)
    {
        if (string.IsNullOrWhiteSpace(jsonText))
            return Empty(filePath);

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            var assetId = root.TryGetPropCI("assetId", out var idProp)
                ? Guid.TryParse(idProp.GetString(), out var g) ? g : Guid.Empty
                : Guid.Empty;

            var name = root.TryGetPropCI("name", out var nameProp)
                ? nameProp.GetString() ?? ""
                : "";

            var dispatch = ParseDispatch(root);

            var exportedFunctions = ParseExportedFunctions(root);
            var hostings = ParseHostings(root);
            var callablePeers = ParseCallablePeers(root);

            var sanitized = Sanitizer.SanitizeName(name);
            int blueprintId = 0;
            if (assetId != Guid.Empty)
            {
                var bytes = assetId.ToByteArray();
                blueprintId = unchecked((int)FnvHasher.Hash32(bytes));
            }

            return new BlueprintSignature(
                Path: filePath,
                AssetId: assetId,
                Name: name,
                SanitizedName: sanitized,
                BlueprintId: blueprintId,
                Dispatch: dispatch,
                ExportedFunctions: exportedFunctions,
                Hostings: hostings,
                DeclaredCallablePeers: callablePeers);
        }
        catch
        {
            return Empty(filePath);
        }
    }

    private static BlueprintDispatchKind ParseDispatch(JsonElement root)
    {
        if (!root.TryGetPropCI("dispatch", out var dispProp)) return BlueprintDispatchKind.Library;
        // The real .bp.json serializes the enum as a NUMBER (Library=0, AiPrimitive=1, Instance=2);
        // some fixtures use the string name. Handle both (GetString() on a number would throw).
        if (dispProp.ValueKind == JsonValueKind.Number && dispProp.TryGetInt32(out var n)
            && Enum.IsDefined(typeof(BlueprintDispatchKind), n))
            return (BlueprintDispatchKind)n;
        var s = dispProp.ValueKind == JsonValueKind.String ? dispProp.GetString() : null;
        return s?.ToLowerInvariant() switch
        {
            "aiprimitive" => BlueprintDispatchKind.AiPrimitive,
            "instance"    => BlueprintDispatchKind.Instance,
            _             => BlueprintDispatchKind.Library,
        };
    }

    private static IReadOnlyList<BlueprintFunctionSig> ParseExportedFunctions(JsonElement root)
    {
        var result = new List<BlueprintFunctionSig>();
        if (!root.TryGetPropCI("graphs", out var graphs)) return result;
        foreach (var graph in graphs.EnumerateArray())
        {
            // Accept both string "Function" and integer 0 (GraphKind.Function == 0).
            if (!graph.TryGetPropCI("kind", out var kp)) continue;
            bool isFunction = kp.ValueKind == JsonValueKind.String
                ? string.Equals(kp.GetString(), "Function", StringComparison.OrdinalIgnoreCase)
                : kp.ValueKind == JsonValueKind.Number && kp.TryGetInt32(out var gk) && gk == 0;
            if (!isFunction) continue;
            var name = graph.TryGetPropCI("name", out var np) ? np.GetString() : null;
            if (string.IsNullOrEmpty(name)) continue;

            var inputs  = ParseParamList(graph, "inputs");
            var outputs = ParseParamList(graph, "outputs");
            result.Add(new BlueprintFunctionSig(name!, inputs, outputs));
        }
        return result;
    }

    private static IReadOnlyList<BlueprintParamSig> ParseParamList(JsonElement graph, string propertyName)
    {
        if (!graph.TryGetPropCI(propertyName, out var arr)) return Array.Empty<BlueprintParamSig>();
        var result = new List<BlueprintParamSig>();
        foreach (var item in arr.EnumerateArray())
        {
            var name   = item.TryGetPropCI("name",   out var np) ? np.GetString() : null;
            var typeId = item.TryGetPropCI("type",   out var tp)
                         && tp.TryGetPropCI("typeid", out var tidp)
                         ? tidp.GetString()
                         : null;
            if (!string.IsNullOrEmpty(name))
                result.Add(new BlueprintParamSig(name!, typeId ?? "System.Object"));
        }
        return result;
    }

    private static IReadOnlyList<AiPrimitiveHosting> ParseHostings(JsonElement root)
    {
        var result = new List<AiPrimitiveHosting>();
        if (!root.TryGetPropCI("primitive", out var prim)) return result;
        if (!prim.TryGetPropCI("hostings", out var hostings)) return result;
        foreach (var h in hostings.EnumerateArray())
        {
            // Hostings may be serialized as enum names (string) or numbers; tolerate both.
            if (h.ValueKind == JsonValueKind.String)
            {
                if (Enum.TryParse<AiPrimitiveHosting>(h.GetString(), ignoreCase: true, out var hosting))
                    result.Add(hosting);
            }
            else if (h.ValueKind == JsonValueKind.Number && h.TryGetInt32(out var hn)
                     && Enum.IsDefined(typeof(AiPrimitiveHosting), hn))
            {
                result.Add((AiPrimitiveHosting)hn);
            }
        }
        return result;
    }

    private static IReadOnlyList<Guid> ParseCallablePeers(JsonElement root)
    {
        var result = new List<Guid>();
        if (!root.TryGetPropCI("callablePeers", out var peers)) return result;
        foreach (var p in peers.EnumerateArray())
        {
            if (Guid.TryParse(p.GetString(), out var id))
                result.Add(id);
        }
        return result;
    }

    private static BlueprintSignature Empty(string filePath) =>
        new BlueprintSignature(
            Path: filePath,
            AssetId: Guid.Empty,
            Name: "",
            SanitizedName: "_",
            BlueprintId: 0,
            Dispatch: BlueprintDispatchKind.Library,
            ExportedFunctions: Array.Empty<BlueprintFunctionSig>(),
            Hostings: Array.Empty<AiPrimitiveHosting>(),
            DeclaredCallablePeers: Array.Empty<Guid>());
}

/// <summary>
/// Case-insensitive JSON property lookup. The on-disk <c>.bp.json</c> is serialized PascalCase
/// (<c>AssetId</c>, <c>Name</c>, <c>Dispatch</c>, …) while some legacy/test fixtures are camelCase;
/// <see cref="JsonElement.TryGetProperty(string, out JsonElement)"/> is case-sensitive, so reading
/// camelCase names against PascalCase files silently failed (every parsed sibling got
/// <c>AssetId = Guid.Empty</c>, which then collided in <c>ValidationContext</c>'s
/// <c>ToDictionary(s =&gt; s.AssetId)</c>). JSON parsing must be case-insensitive.
/// </summary>
internal static class JsonElementCaseInsensitiveExtensions
{
    public static bool TryGetPropCI(this JsonElement el, string name, out JsonElement value)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }
}
