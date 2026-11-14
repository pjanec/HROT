using System;
using System.Collections.Generic;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐ <c>CE-3299</c> B5 / <c>CE-1372</c> — the type a RECORDED id names, found by its id attribute (<c>[ComponentId]</c>, <c>[EventId]</c>) in
    /// the loaded assemblies, for a process that has not registered it yet. ONE scan for both registries (it was the component registry's
    /// own); cached per attribute, rescanned when more assemblies are loaded.
    /// </summary>
    internal static class AttributeIdScan
    {
        private static readonly object _lock = new();
        private static readonly Dictionary<Type, (Dictionary<int, Type> Map, int Assemblies)> _byAttribute = new();

        public static Type? Find<TAttribute>(int id, Func<TAttribute, int> idOf) where TAttribute : Attribute
        {
            lock (_lock)
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                if (!_byAttribute.TryGetValue(typeof(TAttribute), out var cached) || cached.Assemblies != assemblies.Length)
                {
                    var map = new Dictionary<int, Type>();
                    foreach (var asm in assemblies)
                    {
                        if (asm.IsDynamic) continue;
                        Type?[] types;
                        try { types = asm.GetTypes(); }
                        catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
                        catch (Exception) { continue; }
                        foreach (var t in types)
                            if (t != null && System.Reflection.CustomAttributeExtensions.GetCustomAttribute<TAttribute>(t, false) is { } attr)
                                map.TryAdd(idOf(attr), t);
                    }
                    cached = (map, assemblies.Length);
                    _byAttribute[typeof(TAttribute)] = cached;
                }
                return cached.Map.TryGetValue(id, out var found) ? found : null;
            }
        }
    }
}
