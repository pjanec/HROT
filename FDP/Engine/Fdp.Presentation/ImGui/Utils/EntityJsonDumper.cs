using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Serialization;

namespace Fdp.Presentation.Utils;

public static class EntityJsonDumper
{
    public static string Dump(IInspectableSession session, Entity entity)
    {
        var dict = new Dictionary<string, object>();
        dict["EntityId"] = new int[] { entity.Index, entity.Generation };

        var componentsDict = new Dictionary<string, object>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);

        var allTypes = session.GetAllComponentTypes().OrderBy(t => t.Name).ToList();
        foreach (var type in allTypes)
        {
            if (!session.HasComponent(entity, type)) continue;

            object? data = session.GetComponent(entity, type);
            if (data == null) continue;

            componentsDict[type.Name] = MapObject(data, type, visited) ?? new object();
        }

        dict["Components"] = componentsDict;

        var options = FdpJsonOptionsRegistry.Indented;
        string rawJson = JsonSerializer.Serialize(dict, options);
        return JsonAestheticFormatter.FlattenNumericArrays(rawJson);
    }

    // ⭐⭐ CE-2030 — the mapping is DtoDiagnosticMapper.MapObject. ⛔ This file carried a FORK of it that had missed two of
    //   its fixes: QA-007 (a FixedString is a string, not a byte array) and CE-476 (an [InlineArray] of an enum threw, because
    //   the fork marshalled it) — so the editor's Inspector and Watch dumps rendered names as byte lists and could fail on a
    //   blueprint behaviour's root block. Its GetSizeOf/ReadPointer copies went with it.
    private static object? MapObject(object? obj, Type type, HashSet<object> visited)
        => Fdp.Toolkit.Diagnostics.DtoDiagnosticMapper.MapObject(obj, type, visited);
}
