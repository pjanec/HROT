using System;
using System.Collections.Generic;
using System.Reflection;
using Fdp.Core;

namespace Fdp.Toolkit.Replay
{
    /// <summary>
    /// ⭐ CE-4099 (📄 AQ108 §4c A4 / §4c.1 amendment) — a PRIVATE world rebuilt from a keyframe: the types the source world registered, the keyframe
    /// applied, and the read-only CONTENT singletons the caller hands over shared by reference. What an autosave extracts from on a worker (it touches
    /// no shared world), and what the A0 rail measures against the live save — ⛔ one implementation for both.
    /// </summary>
    public static class KeyframeSandbox
    {
        private static readonly MethodInfo _register = Find("RegisterComponent");
        private static readonly MethodInfo _registerManaged = Find("RegisterManagedComponent");
        private static readonly MethodInfo _setSingletonManaged = FindSingleton();

        /// <summary>The component types <paramref name="world"/> registered, captured on the frame thread (the sandbox registers the same).</summary>
        public static Type[] TypesOf(EntityRepository world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var types = new List<Type>();
            foreach (var t in world.GetRegisteredComponentTypes().Keys) types.Add(t);
            return types.ToArray();
        }

        /// <summary>
        /// A new repository with <paramref name="types"/> registered and <paramref name="keyframe"/> applied, plus each of <paramref name="contentSingletons"/>
        /// set as a managed singleton BY REFERENCE (read-only content a keyframe does not carry — e.g. the terrain's authored door states). The caller disposes it.
        /// </summary>
        public static EntityRepository Rebuild(IReadOnlyList<Type> types, WorldKeyframe keyframe, IReadOnlyList<object>? contentSingletons = null)
        {
            if (types == null) throw new ArgumentNullException(nameof(types));
            if (keyframe == null) throw new ArgumentNullException(nameof(keyframe));
            var sandbox = new EntityRepository();
            try
            {
                foreach (var t in types)
                    (t.IsValueType ? _register : _registerManaged).MakeGenericMethod(t).Invoke(sandbox, new object?[] { null });
                WorldKeyframes.Apply(sandbox, keyframe);
                if (contentSingletons != null)
                    foreach (var s in contentSingletons)
                        if (s != null) _setSingletonManaged.MakeGenericMethod(s.GetType()).Invoke(sandbox, new[] { s });
                return sandbox;
            }
            catch
            {
                sandbox.Dispose();
                throw;
            }
        }

        private static MethodInfo Find(string name)
        {
            foreach (var m in typeof(EntityRepository).GetMethods(BindingFlags.Public | BindingFlags.Instance))
                if (m.Name == name && m.IsGenericMethodDefinition && m.GetParameters().Length == 1) return m;
            throw new InvalidOperationException($"EntityRepository.{name}<T>(DataPolicy?) not found");
        }

        private static MethodInfo FindSingleton()
        {
            foreach (var m in typeof(EntityRepository).GetMethods(BindingFlags.Public | BindingFlags.Instance))
                if (m.Name == "SetSingletonManaged" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1) return m;
            throw new InvalidOperationException("EntityRepository.SetSingletonManaged<T>(T) not found");
        }
    }
}
