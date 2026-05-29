using System;
using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;

namespace Hrot.Common.Scenario.Migrations.Helpers
{
    /// <summary>
    /// Scenario-specific helper for iterating the <c>entities</c> dictionary
    /// and applying per-component transformations. The Entities payload uses
    /// mixed casing (PascalCase from FdpAutoSerializer; camelCase from some
    /// custom translators); these helpers preserve existing casing by default.
    /// </summary>
    public static class EntityPatch
    {
        /// <summary>
        /// Iterates every entity in <c>$.entities</c>. The action receives the
        /// entity GUID and the entity JsonObject. The entity may be mutated in
        /// place. Entities may not be added or removed during iteration.
        /// </summary>
        public static void OnEachEntity(JsonObject root, Action<string, JsonObject> action)
        {
            if (root["entities"] is not JsonObject entities)
                return;

            // Snapshot keys to avoid modification-during-iteration issues.
            var keys = new System.Collections.Generic.List<string>();
            foreach (var kvp in entities)
                keys.Add(kvp.Key);

            foreach (string id in keys)
            {
                if (entities[id] is not JsonObject entity)
                    continue;
                action(id, entity);
            }
        }

        /// <summary>
        /// Iterates only entities that have the named component (PascalCase
        /// short name as it appears in JSON). Entities without that component
        /// are skipped.
        /// </summary>
        public static void OnComponent(
            JsonObject root,
            string componentName,
            Action<string, JsonObject> action)
        {
            OnEachEntity(root, (entityId, entity) =>
            {
                if (entity[componentName] is not JsonObject component)
                    return;
                action(entityId, component);
            });
        }

        /// <summary>
        /// Renames a component across every entity that has it. If an entity
        /// already has a component with the new name, throws
        /// <see cref="MigrationException"/>.
        /// </summary>
        public static void RenameComponent(JsonObject root, string oldName, string newName)
        {
            OnEachEntity(root, (entityId, entity) =>
            {
                if (!entity.ContainsKey(oldName))
                    return;

                if (entity.ContainsKey(newName))
                    throw new MigrationException(
                        $"Entity '{entityId}' already has component '{newName}' alongside '{oldName}'.");

                JsonNode? value = entity[oldName];
                entity.Remove(oldName);
                entity[newName] = value;
            });
        }

        /// <summary>
        /// Renames a field within a specific component, across all entities that have it.
        /// </summary>
        public static void RenameField(
            JsonObject root,
            string componentName,
            string oldField,
            string newField,
            CasingPolicy casing = CasingPolicy.MatchExisting)
        {
            OnComponent(root, componentName, (entityId, component) =>
            {
                if (!component.ContainsKey(oldField))
                    return;

                string targetName = ApplyCasing(newField, casing, component);

                JsonNode? value = component[oldField];
                component.Remove(oldField);
                component[targetName] = value;
            });
        }

        /// <summary>
        /// Adds a field with a static default value; skips if already present (idempotent).
        /// </summary>
        public static void AddField(
            JsonObject root,
            string componentName,
            string fieldName,
            JsonNode defaultValue,
            CasingPolicy casing = CasingPolicy.MatchExisting)
        {
            if (defaultValue is null)
                throw new ArgumentNullException(nameof(defaultValue),
                    "Use JsonValue.Create((object?)null) explicitly if a JSON null default is intended.");

            OnComponent(root, componentName, (entityId, component) =>
            {
                string targetName = ApplyCasing(fieldName, casing, component);
                if (component.ContainsKey(targetName))
                    return;
                component[targetName] = defaultValue.DeepClone();
            });
        }

        /// <summary>
        /// Adds a field computed from the component; skips if already present (idempotent).
        /// </summary>
        public static void AddField(
            JsonObject root,
            string componentName,
            string fieldName,
            Func<JsonObject, JsonNode> computeFromComponent,
            CasingPolicy casing = CasingPolicy.MatchExisting)
        {
            OnComponent(root, componentName, (entityId, component) =>
            {
                string targetName = ApplyCasing(fieldName, casing, component);
                if (component.ContainsKey(targetName))
                    return;
                component[targetName] = computeFromComponent(component);
            });
        }

        /// <summary>
        /// Removes a field; no-op if field absent.
        /// </summary>
        public static void RemoveField(JsonObject root, string componentName, string fieldName)
        {
            OnComponent(root, componentName, (entityId, component) =>
            {
                component.Remove(fieldName);
            });
        }

        /// <summary>
        /// Applies an arbitrary transformation to a component on every entity
        /// that has it. The action receives the parent entity and the component;
        /// it may mutate the component, add or remove sibling components on the
        /// entity, or delete the component entirely (by removing it from the entity).
        /// </summary>
        public static void TransformComponent(
            JsonObject root,
            string componentName,
            Action<JsonObject, JsonObject> transform)
        {
            OnEachEntity(root, (entityId, entity) =>
            {
                if (entity[componentName] is not JsonObject component)
                    return;
                transform(entity, component);
            });
        }

        // ---------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------

        private static string ApplyCasing(string fieldName, CasingPolicy casing, JsonObject component)
        {
            switch (casing)
            {
                case CasingPolicy.ForcePascal:
                    return ToPascalCase(fieldName);

                case CasingPolicy.ForceCamel:
                    return ToCamelCase(fieldName);

                case CasingPolicy.MatchExisting:
                default:
                    return InferCasing(fieldName, component);
            }
        }

        /// <summary>
        /// Infers casing from the existing fields in the component (majority wins;
        /// PascalCase wins ties). Applies that casing to <paramref name="fieldName"/>.
        /// </summary>
        private static string InferCasing(string fieldName, JsonObject component)
        {
            int pascal = 0, camel = 0;
            foreach (var kvp in component)
            {
                if (string.IsNullOrEmpty(kvp.Key)) continue;
                if (char.IsUpper(kvp.Key[0]))
                    pascal++;
                else
                    camel++;
            }

            // PascalCase wins ties (FdpAutoSerializer convention).
            return (camel > pascal) ? ToCamelCase(fieldName) : ToPascalCase(fieldName);
        }

        private static string ToPascalCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (char.IsUpper(name[0])) return name;
            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        private static string ToCamelCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (char.IsLower(name[0])) return name;
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }
    }
}
