using System;
using System.Text.Json;
using Fdp.Toolkit.Behavior;
using StructEdit.Core;

namespace Hrot.Editor.AiShared.Inspector;

/// <summary>
/// ⭐⭐ <c>CE-3043</c> — THE ONE PARAMS FORM: edit a behaviour's authored params (<c>BehaviorDefinition.JsonParamsDtoType</c>)
/// as a StructEdit document and produce the JSON an assignment carries. 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.6.
/// <para>⭐ Reads and writes with <see cref="BehaviorParams.JsonOptions"/> — the options every parse side uses — so what
/// the form writes is exactly what the behaviour reads (<c>DESIGN_Decision_Layer.md</c> §4.4: one options object both
/// ways). ⚠ That is why this is not <see cref="DefaultValueAuthoring"/>: its options are a blackboard default's, not the
/// parse side's. The session plumbing (<see cref="ScalarEditBox"/>, the edit service, <c>ComponentEditDrawer</c>) is shared.</para>
/// </summary>
public static class BehaviorParamsForm
{
    /// <summary>The params type's value from <paramref name="json"/>; the type's default when the JSON is empty or invalid.</summary>
    public static object Hydrate(Type paramsType, string? json)
    {
        if (paramsType is null) throw new ArgumentNullException(nameof(paramsType));
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                if (JsonSerializer.Deserialize(json, paramsType, BehaviorParams.JsonOptions) is { } value) return value;
            }
            catch (JsonException) { }
        }
        return Activator.CreateInstance(paramsType)!;
    }

    /// <summary>Opens an edit session over <paramref name="paramsType"/> seeded from <paramref name="json"/>.</summary>
    public static IEditSession Open(IComponentEditService editService, Type paramsType, string? json)
    {
        if (editService is null) throw new ArgumentNullException(nameof(editService));
        var instance = Hydrate(paramsType, json);
        return editService.Open(ScalarEditBox.Wrap(instance, paramsType), ScalarEditBox.EditTypeFor(paramsType), null);
    }

    /// <summary>Commits <paramref name="session"/> and returns the assignment's JSON.</summary>
    public static string CommitToJson(IEditSession session, Type paramsType)
        => ToJson(ScalarEditBox.Unwrap(session.Commit(), paramsType), paramsType);

    /// <summary>A params value as the assignment's JSON (the parse side's options).</summary>
    public static string ToJson(object value, Type paramsType)
        => JsonSerializer.Serialize(value, paramsType, BehaviorParams.JsonOptions);
}
