using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Scenario;

namespace Fdp.Toolkit.ReplayBrowser.Federation
{
    /// <summary>
    /// ⭐ G-7 / CE-4354 (docs/UX/DESIGN_Review_Perspective.md §6.4 I1, §6.9) — where the RAW <see cref="Entity"/> handles of ONE unmanaged component
    /// type live, built ONCE per type: a scalar field, every element of an <c>[InlineArray]</c> of <c>Entity</c> (CE-467 / D03), and an
    /// <c>Entity</c> inside a nested struct or an inline array of structs (<c>RecentDetonations.Slots[].Shooter</c>) — as BYTE OFFSETS into the
    /// component's value. <see cref="Remap{T}"/> applies <c>source → target</c> to each of them on a VALUE COPY; the merge then writes the copy.
    /// <para>⭐ The field knowledge is <see cref="ComponentFieldDiscovery"/>'s — the same discovery <c>FdpAutoSerializer</c> compiles its JSON
    /// delegates from — not a second walker. ⚠ It asks for non-public and <c>[ScenarioIgnore]</c> fields too: a node-local handle in a field the
    /// serializer skips is still wrong in the merged world.</para>
    /// <para>⭐ Offsets are measured with a tiny IL probe (<c>ldflda</c> minus the base) — never <c>Marshal.OffsetOf</c> (its layout is the marshalling
    /// one: a <c>bool</c> is 4 bytes there) and never a field assignment (an <c>Entity</c> field may be <c>readonly</c>).</para>
    /// </summary>
    public sealed class EntityRemapPlan
    {
        private static readonly ConcurrentDictionary<Type, EntityRemapPlan> Plans = new();
        private static readonly Func<Entity, Entity> Identity = e => e;

        /// <summary>The plan of <paramref name="componentType"/> (built on first ask, then cached). A type with no raw <c>Entity</c> has a plan whose <see cref="HasEntities"/> is false.</summary>
        public static EntityRemapPlan For(Type componentType) => Plans.GetOrAdd(componentType, static t => new EntityRemapPlan(t));

        /// <summary>The component type.</summary>
        public Type ComponentType { get; }

        /// <summary>The value size in bytes (<c>Unsafe.SizeOf</c>).</summary>
        public int Size { get; }

        /// <summary>Byte offset of every raw <see cref="Entity"/> in the value (array elements and nested structs expanded).</summary>
        public IReadOnlyList<int> Offsets => _offsets;

        /// <summary>Where they are, as paths (<c>AmmoLoadout.Entity</c>, <c>UnitRoster.SubordinateEntities[]</c>, <c>RecentDetonations.Slots[].Shooter</c>) — for rails and diagnostics.</summary>
        public IReadOnlyList<string> FieldPaths => _paths;

        /// <summary>The component holds at least one raw <see cref="Entity"/>.</summary>
        public bool HasEntities => _offsets.Length > 0;

        private readonly int[] _offsets;
        private readonly string[] _paths;

        private EntityRemapPlan(Type type)
        {
            ComponentType = type;
            var offsets = new List<int>();
            var paths = new List<string>();
            if (type.IsValueType && !type.IsEnum && !type.IsPrimitive)
            {
                Size = SizeOf(type);
                Collect(type, 0, type.Name, offsets, paths, depth: 0);
            }
            _offsets = offsets.ToArray();
            _paths = paths.ToArray();
        }

        /// <summary>
        /// Applies <paramref name="map"/> (source entity → target entity) to every raw <see cref="Entity"/> of <paramref name="value"/> — a copy the
        /// caller owns. <see cref="Entity.Null"/> is handed to <paramref name="map"/> like any other (the caller decides it stays null).
        /// </summary>
        public unsafe void Remap<T>(ref T value, Func<Entity, Entity> map) where T : unmanaged
        {
            if (_offsets.Length == 0) return;
            if (sizeof(T) != Size) throw new ArgumentException($"{typeof(T).Name} is {sizeof(T)} bytes, the plan was built for {ComponentType.Name} ({Size}).");
            fixed (T* p = &value) RemapAt(p, map);
        }

        /// <summary>The same on raw bytes: <paramref name="component"/> points at a value of this plan's type.</summary>
        public unsafe void RemapAt(void* component, Func<Entity, Entity> map)
        {
            byte* b = (byte*)component;
            for (int i = 0; i < _offsets.Length; i++)
            {
                var at = b + _offsets[i];
                Unsafe.WriteUnaligned(at, map(Unsafe.ReadUnaligned<Entity>(at)));
            }
        }

        // ── discovery ───────────────────────────────────────────────────────────────────────

        private static void Collect(Type type, int baseOffset, string path, List<int> offsets, List<string> paths, int depth)
        {
            if (depth > 6) return;   // a struct cannot contain itself; this is only a guard against a pathological nesting

            foreach (var f in ComponentFieldDiscovery.GetPlainFields(type, includeScenarioIgnored: true, includeNonPublic: true))
            {
                if (f.IsStatic) continue;
                var ft = f.FieldType;
                if (ft == typeof(Entity))
                {
                    offsets.Add(baseOffset + OffsetOf(type, f));
                    paths.Add(path + "." + DisplayName(f));
                }
                else if (IsStructWorthWalking(ft))
                {
                    Collect(ft, baseOffset + OffsetOf(type, f), path + "." + DisplayName(f), offsets, paths, depth + 1);
                }
            }

            foreach (var (f, elem, length) in ComponentFieldDiscovery.GetInlineArrayFields(type, includeScenarioIgnored: true, includeNonPublic: true))
            {
                int fieldAt = baseOffset + OffsetOf(type, f);
                if (elem == typeof(Entity))
                {
                    int stride = SizeOf(typeof(Entity));
                    for (int i = 0; i < length; i++) offsets.Add(fieldAt + i * stride);
                    paths.Add(path + "." + DisplayName(f) + "[]");
                }
                else if (IsStructWorthWalking(elem))
                {
                    var innerOffsets = new List<int>();
                    var innerPaths = new List<string>();
                    Collect(elem, 0, path + "." + DisplayName(f) + "[]", innerOffsets, innerPaths, depth + 1);
                    if (innerOffsets.Count == 0) continue;
                    int stride = SizeOf(elem);
                    for (int i = 0; i < length; i++)
                        foreach (int o in innerOffsets) offsets.Add(fieldAt + i * stride + o);
                    paths.AddRange(innerPaths);
                }
            }
        }

        private static bool IsStructWorthWalking(Type t)
            => t.IsValueType && !t.IsPrimitive && !t.IsEnum && t != typeof(Entity)
               && !(t.Namespace?.StartsWith("System", StringComparison.Ordinal) ?? false);   // Vector3, DateTime … hold no Entity

        private static string DisplayName(FieldInfo f)
        {
            string n = f.Name;
            int lt = n.IndexOf('<'), gt = n.IndexOf('>');
            return lt == 0 && gt > 1 ? n.Substring(1, gt - 1) : n;   // an auto-property's backing field
        }

        private static int SizeOf(Type t)
            => (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!.MakeGenericMethod(t).Invoke(null, null)!;

        /// <summary>The byte offset of <paramref name="f"/> inside <paramref name="declaring"/> — an IL probe: <c>(byte*)&amp;box.f - (byte*)&amp;box</c>.</summary>
        private static int OffsetOf(Type declaring, FieldInfo f)
        {
            var dm = new DynamicMethod("EntityRemapPlan.Offset", typeof(int), new[] { typeof(object) }, typeof(EntityRemapPlan).Module, skipVisibility: true);
            var il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox, declaring);
            il.Emit(OpCodes.Ldflda, f);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox, declaring);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ret);
            var probe = (Func<object, int>)dm.CreateDelegate(typeof(Func<object, int>));
            return probe(RuntimeHelpers.GetUninitializedObject(declaring));
        }
    }
}
