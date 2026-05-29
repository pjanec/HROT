using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;

namespace Hrot.Common.Scenario.Migrations.Helpers
{
    /// <summary>
    /// Helper for editing stringified-JSON fields like <c>BehaviorParams</c>
    /// on a MissionTask or <c>ExtensionJson</c> on a RouteWaypoint. These fields
    /// are JSON text inside JSON; manual parse/edit/re-serialize is error-prone.
    /// </summary>
    public static class NestedJsonPatch
    {
        /// <summary>
        /// Parses the value at <paramref name="propertyName"/> as a nested JSON
        /// document, hands it to <paramref name="editAction"/> for in-place
        /// mutation, then re-serializes and stores back. The nested JSON's
        /// formatting (compact) is preserved.
        /// </summary>
        /// <exception cref="MigrationException">
        /// If the property is missing, is not a string, or is not valid JSON.
        /// </exception>
        public static void EditEscapedJsonObject(
            JsonObject parent,
            string propertyName,
            Action<JsonObject> editAction)
        {
            if (!parent.TryGetPropertyValue(propertyName, out JsonNode? node) || node is null)
                throw new MigrationException(
                    $"NestedJsonPatch: property '{propertyName}' is missing.");

            if (node is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out string? raw))
                throw new MigrationException(
                    $"NestedJsonPatch: property '{propertyName}' is not a string.");

            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(raw!);
            }
            catch (JsonException ex)
            {
                throw new MigrationException(
                    $"NestedJsonPatch: property '{propertyName}' does not contain valid JSON: {ex.Message}", ex);
            }

            if (parsed is not JsonObject obj)
                throw new MigrationException(
                    $"NestedJsonPatch: property '{propertyName}' is valid JSON but not an object.");

            editAction(obj);
            parent[propertyName] = obj.ToJsonString();
        }

        /// <summary>
        /// Variant for stringified JSON arrays.
        /// </summary>
        public static void EditEscapedJsonArray(
            JsonObject parent,
            string propertyName,
            Action<JsonArray> editAction)
        {
            if (!parent.TryGetPropertyValue(propertyName, out JsonNode? node) || node is null)
                throw new MigrationException(
                    $"NestedJsonPatch: property '{propertyName}' is missing.");

            if (node is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out string? raw))
                throw new MigrationException(
                    $"NestedJsonPatch: property '{propertyName}' is not a string.");

            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(raw!);
            }
            catch (JsonException ex)
            {
                throw new MigrationException(
                    $"NestedJsonPatch: property '{propertyName}' does not contain valid JSON: {ex.Message}", ex);
            }

            if (parsed is not JsonArray arr)
                throw new MigrationException(
                    $"NestedJsonPatch: property '{propertyName}' is valid JSON but not an array.");

            editAction(arr);
            parent[propertyName] = arr.ToJsonString();
        }
    }
}
