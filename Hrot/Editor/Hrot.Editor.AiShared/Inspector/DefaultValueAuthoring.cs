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
    /// <remarks>
    /// ⭐⭐⭐ <b><c>C-dialog</c> (Batch 68) — this is THE one call site that opens a variable edit
    /// session, and <paramref name="scope"/> is the only thing that differs between the two menu
    /// items.</b> §3: <i>"Edit value…"</i> passes <see cref="EditScope.ForField"/>, <i>"Properties…"</i>
    /// passes <see cref="EditScope.WholeComponent"/> — ⛔ <b>same lifecycle, same OK/Cancel, same
    /// validation.</b> §9's rail is a reflection test asserting exactly one such call site exists.
    ///
    /// <para>
    /// 🔴 <b>The rail FAILED before the change.</b> <c>InspectorWindow:352-365</c> inlined its own copy
    /// of <see cref="Hydrate"/> — the same deserialize-or-<c>Activator</c> try/catch — and called
    /// <c>Open</c> itself, so a variable default-value session had <b>two</b> implementations. It now
    /// routes here.
    /// </para>
    /// </remarks>
    /// <param name="seed">
    /// ⭐⭐⭐ <b>The value to OPEN OVER, when it is not the declaration's default.</b>
    ///
    /// <para>🔴🔴 <b>The defect this closes</b> *(user, <c>2026-08-20</c>: "opened Edit on a variable row
    /// which was showing '312'. The Edit variable dialog opened with value '0'")*. 📐 This method only
    /// ever hydrated <c>varEntry.DefaultValueJson</c> ⇒ ⛔ <b>a PAUSED edit opened at the DECLARATION's
    /// default while the row showed the LIVE value</b>, and an OK would then have written that default
    /// over the running value — 📌 the <c>BP-367</c> shape, one layer up.</para>
    ///
    /// <para>⚠ <b>Fails SAFE</b>: null, or a value of the wrong type, falls back to the declaration —
    /// ⛔ a variable the run has not written yet must not open over a guess.</para>
    /// </summary>
    /// <remarks>⭐ Callers pass this only when the edit TARGETS the live blackboard; deciding that is
    /// <c>VariableEditCommit.TargetFor</c>'s job, ⛔ not this method's.</remarks>
    public static IEditSession OpenSession(
        IComponentEditService editService,
        BlackboardVariableEntry varEntry,
        EditScope? scope = null,
        object? seed = null)
    {
        var instance = varEntry.FieldType.IsInstanceOfType(seed)
            ? seed!
            : Hydrate(varEntry.FieldType, varEntry.DefaultValueJson);

        // ⭐⭐⭐ Batch 97 (97a) — A SCALAR IS OPENED THROUGH A ONE-FIELD WRAPPER.
        // 🔴🔴 BP-356: CreateLeafBinding needs a MEMBER and a document ROOT has none, so a scalar
        //    variable's root came back with Binding == null and DrawLeafNode's
        //    `node.Binding?.SetBoxed(value)` silently discarded the designer's typing.
        // ⭐ ScalarEditBox<T> gives the root a bound CHILD; ⛔ the wrapper never escapes the session —
        //   CommitAndSerialize and the live-bytes arm both unwrap. ⛔ StructEdit is untouched.
        var editType = ScalarEditBox.EditTypeFor(varEntry.FieldType);
        return editService.Open(
            ScalarEditBox.Wrap(instance, varEntry.FieldType), editType, scope);
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
        // ⭐⭐⭐ Batch 97 (97a) — UNWRAP FIRST. ⛔ A scalar session commits a ScalarEditBox<T>, and the
        //    declaration must receive `7`, ⛔ never `{"Value":7}`. ⚠ Serializing the box AS fieldType
        //    would not even fail loudly — it would write `{}` through the catch below and look like a
        //    serialisation quirk rather than the wrapper leaking.
        var committed = ScalarEditBox.Unwrap(session.Commit(), fieldType);
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
