using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// ⭐ G-7 / CE-4354 — the ONE place that knows where a component type's data fields are: its public instance fields, its
    /// <c>[FixedBuffer]</c> fields and its <c>[InlineArray]</c> fields (docs/UX/DESIGN_Review_Perspective.md §6.4 I1: "reuse the auto-serializer's
    /// per-type field knowledge for the remap, not a second walker"). <see cref="FdpAutoSerializer"/> builds its JSON delegates from it;
    /// <c>EntityRemapPlan</c> (the binary merge) finds the raw <c>Entity</c> fields from it.
    /// <para>⚠ <c>includeNonPublic</c>: the serializer reads PUBLIC fields only; a raw <c>Entity</c> in a private field is still a node-local handle, so the remap asks for those too.</para>
    /// <para>⚠ <c>includeScenarioIgnored</c>: the serializer skips a <c>[ScenarioIgnore]</c> field (it is not authored); the remap must NOT — a
    /// raw <c>Entity</c> in an ignored field is still a node-local handle in the merged world.</para>
    /// </summary>
    internal static class ComponentFieldDiscovery
    {
        private static BindingFlags Flags(bool includeNonPublic)
            => includeNonPublic ? BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance : BindingFlags.Public | BindingFlags.Instance;

        /// <summary>Public instance fields that are neither a fixed buffer nor an <c>[InlineArray]</c> (those are listed separately).</summary>
        public static FieldInfo[] GetPlainFields(Type type, bool includeScenarioIgnored = false, bool includeNonPublic = false)
        {
            var all = type.GetFields(Flags(includeNonPublic));
            var result = new List<FieldInfo>(all.Length);
            foreach (var f in all)
            {
                if (!includeScenarioIgnored && f.GetCustomAttribute<ScenarioIgnoreAttribute>() != null) continue;
                // Fixed-buffer fields — handled separately by GetFixedBufferFields.
                if (f.GetCustomAttribute<FixedBufferAttribute>() != null) continue;
                // InlineArray fields — handled separately by GetInlineArrayFields.
                if (f.FieldType.GetCustomAttribute<InlineArrayAttribute>() != null) continue;
                result.Add(f);
            }
            return result.ToArray();
        }

        /// <summary>Fixed-buffer fields together with their element type and element count, as declared by <see cref="FixedBufferAttribute"/>.</summary>
        public static (FieldInfo field, Type elemType, int length)[] GetFixedBufferFields(Type type, bool includeScenarioIgnored = false, bool includeNonPublic = false)
        {
            var result = new List<(FieldInfo, Type, int)>();
            foreach (var f in type.GetFields(Flags(includeNonPublic)))
            {
                if (!includeScenarioIgnored && f.GetCustomAttribute<ScenarioIgnoreAttribute>() != null) continue;
                var attr = f.GetCustomAttribute<FixedBufferAttribute>();
                if (attr == null) continue;
                result.Add((f, attr.ElementType, attr.Length));
            }
            return result.ToArray();
        }

        /// <summary>Fields whose declared type carries <see cref="InlineArrayAttribute"/>, with the element type (the first field of the
        /// inline-array struct) and the capacity.</summary>
        public static (FieldInfo field, Type elemType, int length)[] GetInlineArrayFields(Type type, bool includeScenarioIgnored = false, bool includeNonPublic = false)
        {
            var result = new List<(FieldInfo, Type, int)>();
            foreach (var f in type.GetFields(Flags(includeNonPublic)))
            {
                if (!includeScenarioIgnored && f.GetCustomAttribute<ScenarioIgnoreAttribute>() != null) continue;
                var attr = f.FieldType.GetCustomAttribute<InlineArrayAttribute>();
                if (attr == null) continue;
                // The element type is the (only) field of the InlineArray struct.
                var elemField = f.FieldType.GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault();
                if (elemField == null) continue;
                result.Add((f, elemField.FieldType, attr.Length));
            }
            return result.ToArray();
        }
    }
}
