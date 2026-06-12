using System;
using System.Text.Json;
using Hrot.Editor.AiShared.Blackboard;
using StructEdit.Core;

namespace Hrot.Editor.AiShared.Inspector;

/// <summary>
/// Headless-testable helper for the B-3 default-value authoring flow:
/// hydrate a DTO instance from a variable's <c>DefaultValueJson</c>, open a StructEdit
/// session, apply edits, commit, and serialize the result back to JSON.
/// <para>
/// The <see cref="InspectorWindow"/> calls these helpers from its ImGui draw path.
/// Tests exercise this class directly — no ImGui context required.
/// </para>
/// <para>
/// Tooltip text for the "Static Parameters" panel is exposed as a const so tests can
/// assert its content without touching ImGui (B-5).
/// </para>
/// </summary>
public static class DefaultValueAuthoring
{
    // ── B-5: static-vs-dynamic tooltip ───────────────────────────────────────

    /// <summary>
    /// One-line tooltip shown next to the "Static Parameters" panel in the Inspector.
    /// Reminds designers that BTree/HSM static values are applied once at behavior
    /// assignment and are not re-applied per tick — bind a variable for live/dynamic values.
    /// </summary>
    public const string StaticVsDynamicTooltip =
        "BTree/HSM static value = applied once at behavior assignment; " +
        "bind a variable for live/dynamic values.";

    // ── JSON options ──────────────────────────────────────────────────────────

    /// <summary>
    /// Shared serializer options used for default-value JSON round-trips.
    /// <c>IncludeFields = true</c> ensures public struct fields (in addition to properties)
    /// are serialized/deserialized, which is the common pattern for game-dev DTO structs.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true,
    };

    // ── Hydration ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Hydrates a boxed DTO instance from <paramref name="defaultValueJson"/>.
    /// Falls back to <c>Activator.CreateInstance</c> when the JSON is null/empty or
    /// deserialization fails — never returns null for a value type.
    /// </summary>
    /// <param name="fieldType">The CLR type of the variable (and hence the DTO).</param>
    /// <param name="defaultValueJson">The persisted JSON string (may be null or empty).</param>
    /// <returns>A boxed instance of <paramref name="fieldType"/>.</returns>
    public static object Hydrate(Type fieldType, string? defaultValueJson)
    {
        if (!string.IsNullOrEmpty(defaultValueJson))
        {
            try
            {
                var deserialized = JsonSerializer.Deserialize(defaultValueJson, fieldType, JsonOptions);
                if (deserialized is not null) return deserialized;
            }
            catch
            {
                // Fall through to default construction.
            }
        }
        return Activator.CreateInstance(fieldType)!;
    }

    // ── Session management ────────────────────────────────────────────────────

    /// <summary>
    /// Opens a new StructEdit session for editing the default value of
    /// <paramref name="varEntry"/>.
    /// <para>
    /// The caller owns the returned session and must dispose it when done.
    /// </para>
    /// </summary>
    /// <param name="editService">The StructEdit edit-service (from the composition root).</param>
    /// <param name="varEntry">The variable whose default value is being edited.</param>
    /// <returns>An open <see cref="IEditSession"/> for the hydrated DTO instance.</returns>
    public static IEditSession OpenSession(
        IComponentEditService editService,
        BlackboardVariableEntry varEntry)
    {
        var instance = Hydrate(varEntry.FieldType, varEntry.DefaultValueJson);
        return editService.Open(instance, varEntry.FieldType);
    }

    // ── Commit + serialize ────────────────────────────────────────────────────

    /// <summary>
    /// Commits the edit session and serializes the result to a JSON string.
    /// Returns <c>"{}"</c> on serialization failure (never throws).
    /// Uses <see cref="JsonOptions"/> so that public struct fields are included.
    /// </summary>
    /// <param name="session">The active, dirty edit session to commit.</param>
    /// <param name="fieldType">The CLR type used for serialization.</param>
    /// <returns>The JSON-serialized committed value.</returns>
    public static string CommitAndSerialize(IEditSession session, Type fieldType)
    {
        var committed = session.Commit();
        try
        {
            return JsonSerializer.Serialize(committed, fieldType, JsonOptions);
        }
        catch
        {
            return "{}";
        }
    }

    // ── Round-trip helper ─────────────────────────────────────────────────────

    /// <summary>
    /// Convenience overload: commits the session, serializes to JSON, and hydrates back
    /// to a boxed instance — useful for verifying the JSON round-trip in tests.
    /// </summary>
    public static (string json, object rehydrated) CommitSerializeAndRehydrate(
        IEditSession session,
        Type fieldType)
    {
        string json = CommitAndSerialize(session, fieldType);
        object rehydrated = Hydrate(fieldType, json);
        return (json, rehydrated);
    }
}
