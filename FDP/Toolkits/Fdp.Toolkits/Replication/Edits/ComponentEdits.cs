using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Replication.Attributes;

namespace Fdp.Toolkit.Replication.Edits
{
    /// <summary>
    /// ⭐ Edit framework F3 (📄 docs/UX/DESIGN_Edit_Framework.md §3, CE-4270-era B-3) — add or remove ONE component on an entity, owner-side: the Playable
    /// toggle, a spawn area's role. ⭐ Only a component the ONE attribute vocabulary targets may be added or removed (<see cref="AttributeVocabulary"/> —
    /// the forms' components; never a lifecycle or network component). A component is added with its default value; the form then writes its fields.
    /// <para>The owner applies it (<see cref="Edits.Submit"/> here, <c>UpdateEntityAttributeRequestSystem</c>'s <see cref="PatchKey"/> intercept
    /// elsewhere — the <c>"CommanderId"</c> precedent).</para>
    /// </summary>
    public static class ComponentEdits
    {
        /// <summary>The JSON patch key a non-owner sends: <c>{"$component":{"add":"Playable"}}</c> or <c>{"$component":{"remove":"Playable"}}</c>.</summary>
        public const string PatchKey = "$component";

        private static readonly MethodInfo AddGeneric = typeof(ComponentEdits).GetMethod(nameof(AddDefault), BindingFlags.NonPublic | BindingFlags.Static)!;
        private static readonly MethodInfo RemoveGeneric = typeof(EntityRepository).GetMethod(nameof(EntityRepository.RemoveComponent), 1, new[] { typeof(Entity) })!;

        /// <summary>The component types that may be added / removed this way (those the vocabulary targets).</summary>
        public static IReadOnlyCollection<Type> Editable { get; } = AttributeVocabulary.All.Select(a => a.Component).Distinct().ToArray();

        /// <summary>The editable component named <paramref name="name"/> (short or full name), or null.</summary>
        public static Type? Find(string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var t in Editable) if (t.Name == name || t.FullName == name) return t;
            return null;
        }

        /// <summary>Applies the add / remove HERE (the caller has established ownership). Null on success, else the whole reason.</summary>
        public static string? Apply(EntityRepository repo, Entity entity, string typeName, bool add)
        {
            var type = Find(typeName);
            if (type is null) return $"'{typeName}' cannot be added or removed here (not a form component)";
            if (!repo.IsAlive(entity)) return "the entity is gone";
            int typeId = ComponentTypeRegistry.GetOrRegisterManaged(type);
            bool has = repo.HasComponentByTypeId(entity, typeId);
            if (add == has) return null;   // already as asked
            if (add)
            {
                AddGeneric.MakeGenericMethod(type).Invoke(null, new object[] { repo, entity });
                repo.SetAuthority(entity, typeId, true);   // the owner owns what it added (its fields are written next)
            }
            else RemoveGeneric.MakeGenericMethod(type).Invoke(repo, new object[] { entity });
            return null;
        }

        private static void AddDefault<T>(EntityRepository repo, Entity entity) where T : unmanaged
        {
            if (!repo.IsComponentTypeRegistered<T>()) repo.RegisterComponent<T>();
            repo.AddComponent(entity, default(T));
        }

        /// <summary>The non-owner's patch.</summary>
        public static string Patch(string typeName, bool add)
            => "{\"" + PatchKey + "\":{\"" + (add ? "add" : "remove") + "\":\"" + typeName + "\"}}";
    }
}
