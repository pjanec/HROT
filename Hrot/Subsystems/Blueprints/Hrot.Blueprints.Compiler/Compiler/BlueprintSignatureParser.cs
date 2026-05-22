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

            var assetId = root.TryGetProperty("assetId", out var idProp)
                ? Guid.TryParse(idProp.GetString(), out var g) ? g : Guid.Empty
                : Guid.Empty;

            var name = root.TryGetProperty("name", out var nameProp)
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
                ExportedFunctionNames: exportedFunctions,
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
        if (!root.TryGetProperty("dispatch", out var dispProp)) return BlueprintDispatchKind.Library;
        return dispProp.GetString()?.ToLowerInvariant() switch
        {
            "aiprimitive" => BlueprintDispatchKind.AiPrimitive,
            "instance"    => BlueprintDispatchKind.Instance,
            _             => BlueprintDispatchKind.Library,
        };
    }

    private static IReadOnlyList<string> ParseExportedFunctions(JsonElement root)
    {
        var result = new List<string>();
        if (!root.TryGetProperty("graphs", out var graphs)) return result;
        foreach (var graph in graphs.EnumerateArray())
        {
            var kind = graph.TryGetProperty("kind", out var kp) ? kp.GetString() : null;
            if (kind?.Equals("Function", StringComparison.OrdinalIgnoreCase) != true) continue;
            var name = graph.TryGetProperty("name", out var np) ? np.GetString() : null;
            if (!string.IsNullOrEmpty(name)) result.Add(name!);
        }
        return result;
    }

    private static IReadOnlyList<AiPrimitiveHosting> ParseHostings(JsonElement root)
    {
        var result = new List<AiPrimitiveHosting>();
        if (!root.TryGetProperty("primitive", out var prim)) return result;
        if (!prim.TryGetProperty("hostings", out var hostings)) return result;
        foreach (var h in hostings.EnumerateArray())
        {
            var val = h.GetString();
            if (Enum.TryParse<AiPrimitiveHosting>(val, ignoreCase: true, out var hosting))
                result.Add(hosting);
        }
        return result;
    }

    private static IReadOnlyList<Guid> ParseCallablePeers(JsonElement root)
    {
        var result = new List<Guid>();
        if (!root.TryGetProperty("callablePeers", out var peers)) return result;
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
            ExportedFunctionNames: Array.Empty<string>(),
            Hostings: Array.Empty<AiPrimitiveHosting>(),
            DeclaredCallablePeers: Array.Empty<Guid>());
}
