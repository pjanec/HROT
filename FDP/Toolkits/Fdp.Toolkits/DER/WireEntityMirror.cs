using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using CycloneDDS.Runtime;
using CycloneDDS.Schema;

namespace Fdp.Toolkit.DER
{
    /// <summary>
    /// ⭐ AQ109 §5a/§5b (X5) — which generated wire types the generic inspector mirrors, in ONE place, so the inspector and its rails
    /// cannot disagree on what "entity-keyed" means. 📄 docs/blueprints/Architect_Question_109_Retire_ExCon.md §5b, §6.5;
    /// docs/reference/NED_DDS_Protocol.md §Concepts 4 (keys).
    ///
    /// <para><b>Entity-keyed</b> = the protocol's keyed rule: the FIRST <c>[DdsKey]</c> field (declaration order) is <c>EntityId</c> or
    /// <c>ParentNetworkId</c>. Types keyed by anything else (node id, transfer id, client id …) have no entity to mirror.</para>
    /// </summary>
    public static class WireTypeCatalog
    {
        private static readonly string[] OwnPrefixes = { "Hrot.", "Fdp.", "GizmoMap." };

        private static bool IsOwnAssembly(string? name)
            => name is not null && OwnPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal))
               && !name.EndsWith(".Tests", StringComparison.Ordinal);

        /// <summary>Every <c>[DdsTopic]</c> type in the given assemblies and the own (Hrot./Fdp./GizmoMap., non-test) assemblies they reference, transitively.</summary>
        public static IReadOnlyList<Type> TopicTypes(IEnumerable<Assembly> anchors)
        {
            var seen = new Dictionary<string, Assembly>(StringComparer.Ordinal);
            void Visit(Assembly a)
            {
                var n = a.GetName().Name;
                if (!IsOwnAssembly(n) || !seen.TryAdd(n!, a)) return;
                foreach (var r in a.GetReferencedAssemblies())
                {
                    try { Visit(Assembly.Load(r)); } catch (Exception) { /* a reference that cannot load carries no topic we can name */ }
                }
            }
            foreach (var a in anchors) Visit(a);

            var types = new List<Type>();
            foreach (var a in seen.Values)
            {
                Type?[] all;
                try { all = a.GetTypes(); }
                catch (ReflectionTypeLoadException e) { all = e.Types; }
                types.AddRange(all.Where(t => t is not null && t.GetCustomAttribute<DdsTopicAttribute>() is not null)!);
            }
            return types;
        }

        /// <summary>The <c>[DdsKey]</c> fields of a type, in declaration order.</summary>
        public static IReadOnlyList<FieldInfo> KeyFields(Type t)
            => t.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.GetCustomAttribute<DdsKeyAttribute>() is not null)
                .OrderBy(f => f.MetadataToken).ToList();

        /// <summary>The first <c>[DdsKey]</c> field's name; null when the type is unkeyed.</summary>
        public static string? FirstKeyName(Type t) => KeyFields(t).FirstOrDefault()?.Name;

        /// <summary>The protocol's keyed rule: first key is <c>EntityId</c> or <c>ParentNetworkId</c>.</summary>
        public static bool IsEntityKeyed(Type t) => FirstKeyName(t) is "EntityId" or "ParentNetworkId";

        /// <summary>The entity-keyed subset of <paramref name="topicTypes"/>, ordered by full name.</summary>
        public static IReadOnlyList<Type> EntityKeyed(IEnumerable<Type> topicTypes)
            => topicTypes.Where(IsEntityKeyed).OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();

        /// <summary>The entity-keyed wire types reachable from <paramref name="anchors"/>.</summary>
        public static IReadOnlyList<Type> EntityKeyedTypes(IEnumerable<Assembly> anchors) => EntityKeyed(TopicTypes(anchors));

        private static readonly Lazy<IReadOnlyList<Type>> Loaded =
            new(() => EntityKeyedTypes(AppDomain.CurrentDomain.GetAssemblies()));

        /// <summary>The entity-keyed wire types reachable from the assemblies loaded in this process (computed once).
        /// ⚠ A host sees only the wire types its own assemblies reference.</summary>
        public static IReadOnlyList<Type> EntityKeyedTypesInProcess() => Loaded.Value;
    }

    /// <summary>
    /// ⭐ AQ109 §5a/§5b (X5) — THE one generic wire mirror. Opens a <c>DdsReader&lt;T&gt;</c> per entity-keyed generated wire type
    /// (reflectively — no per-descriptor code) and stores each alive sample into a <see cref="DerRepo"/> under its entity id, creating
    /// the entity on its first descriptor; a multi-instance type's second key is the part id.
    ///
    /// <para>⭐ <b>Opt-in by construction:</b> the readers exist only between construction and <see cref="Dispose"/>; the owner (the
    /// "Wire Entity Inspector" window) creates it on open and disposes it on close. Nothing is read except in <see cref="Poll"/>.</para>
    /// <para>Entity ids are the wire's <c>long</c>, never narrowed. Dispose notifications are not applied — the mirror shows the
    /// last descriptor seen; <see cref="Dispose"/> drops everything.</para>
    /// </summary>
    public sealed class WireEntityMirror : IDisposable
    {
        private readonly List<IWireFeed> _feeds = new();
        private readonly DerRepo _repo = new();

        /// <summary>The mirrored entities.</summary>
        public IDerRepo Repo => _repo;

        /// <summary>The wire types a reader was opened for.</summary>
        public IReadOnlyList<Type> OpenedTypes => _feeds.Select(f => f.WireType).ToList();

        /// <summary>Samples stored so far.</summary>
        public long SamplesStored { get; private set; }

        public WireEntityMirror(DdsParticipant participant, IEnumerable<Type> entityKeyedTypes)
        {
            ArgumentNullException.ThrowIfNull(participant);
            ArgumentNullException.ThrowIfNull(entityKeyedTypes);
            try
            {
                foreach (var t in entityKeyedTypes)
                {
                    if (!WireTypeCatalog.IsEntityKeyed(t)) throw new ArgumentException($"{t.FullName} is not entity-keyed", nameof(entityKeyedTypes));
                    _feeds.Add((IWireFeed)Activator.CreateInstance(typeof(WireFeed<>).MakeGenericType(t), participant, _repo)!);
                }
            }
            catch { Dispose(); throw; }
        }

        /// <summary>Takes what arrived since the last call into the repo. Call once per frame, only while the owner is open.</summary>
        public void Poll()
        {
            foreach (var f in _feeds) SamplesStored += f.Poll();
        }

        public void Dispose()
        {
            foreach (var f in _feeds) f.Dispose();
            _feeds.Clear();
            foreach (var e in _repo.GetAllEntities().ToList()) _repo.DeleteEntity(e.EntityId);
        }

        private interface IWireFeed : IDisposable
        {
            Type WireType { get; }
            int Poll();
        }

        private sealed class WireFeed<T> : IWireFeed where T : struct
        {
            private readonly DdsReader<T> _reader;
            private readonly DerRepo _repo;
            private readonly Func<T, long> _entityId;
            private readonly Func<T, long>? _part;
            private readonly Func<T, long>? _tkb;

            public Type WireType => typeof(T);

            public WireFeed(DdsParticipant participant, DerRepo repo)
            {
                _repo = repo;
                var keys = WireTypeCatalog.KeyFields(typeof(T));
                _entityId = Getter(keys[0])!;
                _part = keys.Count > 1 ? Getter(keys[1]) : null;
                _tkb = typeof(T).GetField("TkbType", BindingFlags.Public | BindingFlags.Instance) is { } f ? Getter(f) : null;
                _reader = new DdsReader<T>(participant);
            }

            /// <summary>A compiled numeric field getter; null when the field is not numeric (then the part id is 0).</summary>
            private static Func<T, long>? Getter(FieldInfo f)
            {
                if (!(f.FieldType.IsPrimitive && f.FieldType != typeof(bool) && f.FieldType != typeof(char)
                      && f.FieldType != typeof(float) && f.FieldType != typeof(double))) return null;
                var p = Expression.Parameter(typeof(T), "s");
                return Expression.Lambda<Func<T, long>>(Expression.Convert(Expression.Field(p, f), typeof(long)), p).Compile();
            }

            public int Poll()
            {
                int n = 0;
                using var loan = _reader.Take(64);
                foreach (var sample in loan)
                {
                    if (sample.Info.InstanceState != DdsInstanceState.Alive || !sample.IsValid) continue;
                    var data = sample.Data;
                    long id = _entityId(data);
                    var entity = _repo.GetEntity(id) ?? _repo.CreateEntity(id, _tkb?.Invoke(data) ?? 0L);
                    entity.SetDescriptor(data, _part is null ? 0 : unchecked((int)_part(data)));
                    n++;
                }
                return n;
            }

            public void Dispose() => _reader.Dispose();
        }
    }
}
