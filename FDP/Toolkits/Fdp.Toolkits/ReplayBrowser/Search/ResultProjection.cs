using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// ⭐ <c>R-4077</c> extension (docs/DESIGN_Director_Timeline_Editor.md §9c ③) — WHICH fields of a match to keep beside its frame and
    /// entity: who did it, to whom, where, how much. ⛔ It decides nothing — the predicate decides; this only reads the match.
    /// Paths are dotted member paths (<c>"Origin.X"</c>), as everywhere else in the model.
    /// </summary>
    public sealed class ResultProjectionDto
    {
        /// <summary>Read the paths from this component of the matched ENTITY; null = from the matched object itself (an event, a ring
        /// entry). E.g. a structural match reads its position from <c>SimTransform</c>.</summary>
        [JsonConverter(typeof(TypeNameJsonConverter))]
        public Type? SourceComponent { get; set; }
        /// <summary>An entity or id field naming who the result is about; empty = the matched entity.</summary>
        public string? SubjectPath { get; set; }
        /// <summary>An entity or id field naming the counterpart (the target, the order's receiver).</summary>
        public string? OtherPath { get; set; }
        public string? XPath { get; set; }
        public string? YPath { get; set; }
        /// <summary>A number or enum (the damage, the rounds, the entry's kind).</summary>
        public string? ValuePath { get; set; }
    }

    /// <summary>⭐ What a projection kept: ids are NETWORK ids where the entity had one at that frame, else <c>-(index + 1)</c>, 0 for none.</summary>
    public readonly record struct SearchResultPayload(long Subject, long Other, float X, float Y, float Value);

    /// <summary>
    /// ⭐ A <see cref="ResultProjectionDto"/> compiled against the type its paths are read from — one getter per path, built once
    /// (<see cref="PropertyEvaluator.BuildGetter"/>, the model's one getter compiler).
    /// </summary>
    public sealed class CompiledProjection
    {
        private readonly Func<object, object?>? _subject, _other, _x, _y, _value;

        public Type SourceType { get; }
        /// <summary>The component read off the matched entity, or null when the paths read the matched object.</summary>
        public Type? SourceComponent { get; }

        public CompiledProjection(ResultProjectionDto projection, Type matchedObjectType)
        {
            if (projection == null) throw new ArgumentNullException(nameof(projection));
            SourceComponent = projection.SourceComponent;
            SourceType = projection.SourceComponent ?? matchedObjectType;
            _subject = Getter(projection.SubjectPath);
            _other = Getter(projection.OtherPath);
            _x = Getter(projection.XPath);
            _y = Getter(projection.YPath);
            _value = Getter(projection.ValuePath);
        }

        private Func<object, object?>? Getter(string? path) =>
            string.IsNullOrWhiteSpace(path) ? null : PropertyEvaluator.BuildGetter(SourceType, path);

        /// <summary>
        /// Applies the projection. <paramref name="matchedObject"/> is the event or ring entry (boxed) when the paths read it; when they read
        /// a component, it is read here off <paramref name="matched"/>. Returns the subject entity (the matched one unless a subject path
        /// names another) through <paramref name="subject"/>.
        /// </summary>
        public SearchResultPayload Apply(EntityRepository repo, object? matchedObject, Entity matched, out Entity subject)
        {
            object? source = SourceComponent == null ? matchedObject : ComponentBox.Read(repo, matched, SourceComponent);
            subject = matched;
            long subjectId = ResultIds.Of(repo, matched), otherId = 0;
            float x = 0, y = 0, value = 0;
            if (source != null)
            {
                if (_subject != null) subjectId = IdOf(repo, _subject(source), ref subject);
                if (_other != null) { var ignored = Entity.Null; otherId = IdOf(repo, _other(source), ref ignored); }
                x = Number(_x?.Invoke(source));
                y = Number(_y?.Invoke(source));
                value = Number(_value?.Invoke(source));
            }
            return new SearchResultPayload(subjectId, otherId, x, y, value);
        }

        private static long IdOf(EntityRepository repo, object? v, ref Entity entity)
        {
            switch (v)
            {
                case null: return 0;
                case Entity e: entity = e; return ResultIds.Of(repo, e);
                case Enum en: return Convert.ToInt64(en);
                case IConvertible c: try { return c.ToInt64(null); } catch (FormatException) { return 0; } catch (InvalidCastException) { return 0; }
                default: return 0;
            }
        }

        private static float Number(object? v) => v switch
        {
            null => 0f,
            bool b => b ? 1f : 0f,
            Enum en => Convert.ToSingle(Convert.ToInt64(en)),
            IConvertible c => SafeSingle(c),
            _ => 0f,
        };

        private static float SafeSingle(IConvertible c)
        {
            try { return c.ToSingle(null); } catch (FormatException) { return 0f; } catch (InvalidCastException) { return 0f; }
        }
    }

    /// <summary>⭐ The id a result names: the entity's NETWORK id where it has one, else <c>-(index + 1)</c>, 0 for none — so it means the
    /// same thing in a search, the timeline's index, its sidecar and the live world.</summary>
    public static class ResultIds
    {
        public static long Of(EntityRepository repo, Entity e)
        {
            if (e.IsNull || e.Equals(default(Entity))) return 0;
            if (repo.IsComponentTypeRegistered<NetworkIdentity>())
            {
                long net = NetworkIdResolver.RuntimeNetworkIdOf(repo, e);
                if (net > 0) return net;
            }
            return -(e.Index + 1L);
        }
    }

    /// <summary>⭐ One component of an entity, boxed, by its runtime type — the lifecycle scan's read, shared.</summary>
    public static class ComponentBox
    {
        public static object? Read(EntityRepository repo, Entity entity, Type componentType)
        {
            if (entity.IsNull || !repo.IsAlive(entity)) return null;
            int typeId = ComponentTypeRegistry.GetId(componentType);
            if (typeId < 0 || !repo.HasComponentByTypeId(entity, typeId)) return null;
            if (!componentType.IsValueType) return repo.GetManagedComponentByTypeId(entity, typeId);
            unsafe
            {
                void* ptr = repo.GetComponentPointer(entity, typeId);
                return ptr == null ? null : Marshal.PtrToStructure(new IntPtr(ptr), componentType);
            }
        }
    }
}
