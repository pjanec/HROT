using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
using Fdp.Toolkit.Diagnostics;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Serialization;

namespace Fdp.Presentation.Utils;

/// <summary>
/// Shared JSON serialization utilities for inspector panels.
/// Centralizes the logic for converting single components through the ScenarioSerializer
/// translator pipeline vs. the legacy fallback path.
/// </summary>
public static class InspectorJsonUtils
{
    /// <summary>
    /// Serializes a single component for a given entity, routing through the ScenarioSerializer
    /// translator pipeline if available, otherwise falling back to generic JsonSerializer.
    ///
    /// The translator pipeline is restricted to the specific component type via a single-bit mask,
    /// allowing custom IEntityScenarioTranslator implementations to project raw memory into
    /// useful DTOs (e.g., BrainBlackboardTranslator, Blackboard1024Translator).
    /// </summary>
    /// <param name="session">The inspectable session.</param>
    /// <param name="entity">The entity containing the component.</param>
    /// <param name="componentType">The component's type.</param>
    /// <param name="data">The raw component data (boxed).</param>
    /// <param name="serializer">Optional ScenarioSerializer for translator pipeline routing.</param>
    /// <returns>Formatted JSON string, or empty string if serialization fails.</returns>
    public static string BuildComponentJson(
        IInspectableSession session,
        Entity entity,
        Type componentType,
        object? data,
        ScenarioSerializer? serializer)
    {
        if (serializer != null && session is RepositoryAdapter adapter)
        {
            int typeId = ComponentTypeRegistry.GetId(componentType);
            if (typeId >= 0)
            {
                try
                {
                    var resolver = new DiagnosticGuidResolver();
                    var mask = new BitMask256();
                    mask.SetBit(typeId);

                    // Routes through IEntityScenarioTranslator pipeline, restricted to this specific typeId
                    var entityNode = serializer.SerializeEntity(adapter.Repo, entity, resolver, mask);

                    // Unwrap the single component from the entity dictionary to match expected format.
                    // SerializeEntity returns { "ComponentName": {...} }, so extract just the inner value.
                    var outputNode = entityNode.Count == 1 ? entityNode.First().Value : entityNode;
                    if (outputNode != null)
                    {
                        string rawJson = outputNode.ToJsonString(FdpJsonOptionsRegistry.Indented);
                        return JsonAestheticFormatter.FlattenNumericArrays(rawJson);
                    }
                }
                catch
                {
                    // Fall through to legacy path on any translator error
                }
            }
        }

        // Legacy fallback: generic JsonSerializer on raw struct memory
        try
        {
            var options = FdpJsonOptionsRegistry.Indented;
            return JsonAestheticFormatter.FlattenNumericArrays(JsonSerializer.Serialize(data, options));
        }
        catch
        {
            return string.Empty;
        }
    }
}
