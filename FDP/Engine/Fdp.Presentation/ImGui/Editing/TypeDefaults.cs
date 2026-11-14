using System;
using System.Collections.Generic;
using StructEdit.Core;
using StructEdit.Core.Defaults;

namespace Fdp.Presentation.Editing;

/// <summary>
/// ⭐ N-4 (CE-3381, S7) — the default source of a TYPE: a document over <c>new T()</c> (<see cref="DocumentDefaults"/>), built once per type and kept. A host that edits a value of a known type and has no richer Fill source
/// passes <c>TypeDefaults.For(type)</c> to <see cref="ComponentEditDrawer"/> so its leaves show grey at their default and amber with ↺ when changed. Null for a type with no default instance (an abstract type, no parameterless constructor).
/// </summary>
public static class TypeDefaults
{
    private static readonly Dictionary<Type, IFieldDefaults?> Cache = new();
    private static IComponentEditService? _service;

    public static IFieldDefaults? For(Type type)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(type, out var d)) return d;
            d = null;
            try
            {
                if (ContainerOps.DefaultOf(type) is { } instance)
                {
                    _service ??= ComponentEditServices.CreateBuilder().Build();
                    var session = _service.Open(instance, type);   // kept alive for the process: its document is the default source
                    d = new DocumentDefaults(session.Document);
                }
            }
            catch (Exception e) when (e is InvalidOperationException or NotSupportedException or ArgumentException or System.Reflection.TargetInvocationException) { d = null; }
            Cache[type] = d;
            return d;
        }
    }
}
