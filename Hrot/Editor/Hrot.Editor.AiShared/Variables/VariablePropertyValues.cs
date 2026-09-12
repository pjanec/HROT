namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 99 (<c>99a</c>) — the declaration's WRITABLE properties, as ONE neutral carrier.</b>
///
/// <para>⭐ <b>Neutral so the schema-source seam is ONE method.</b> The form's own backing store is
/// <see cref="VariablePropertyState"/>; this is what crosses the interface. ⛔ Without it, every host
/// would need a setter per property and a host that implemented four of five would look finished.</para>
///
/// <para>⚠⚠ <b><c>null</c> means "this kind does not carry it", and an implementer MUST LEAVE THAT
/// MEMBER ALONE</b> — ⛔ coercing <c>null</c> to <c>""</c> erases a comment the form never showed.
/// 📌 That is the same honesty rule <c>(pending)</c> rests on: absence and emptiness are different facts.</para>
///
/// <para>⛔⛔ <b><c>Name</c> and <c>Type</c> are ABSENT BY RULING, not by oversight.</b> 📌 <c>R-109</c>:
/// they are <b>OPERATIONS</b>, not writes — <c>Name</c> is a rename *(⇒ <see cref="VariableRenameCommit"/>,
/// the refactor service, <c>M-15</c>)* and <c>Type</c> is a retype migration *(<c>StructureHash</c>
/// moves — <c>R-24</c>)*. ⭐ Keeping them out of this record is what stops a future caller writing
/// either as if it were a field.</para>
/// </summary>
public sealed record VariablePropertyValues(
    string? DefaultValueJson = null,
    string? Tooltip          = null,
    string? Comment          = null,
    string? Category         = null,
    bool?   IsEditable       = null,
    bool?   IsExposedOnSpawn = null);

/// <summary>
/// ⭐⭐ <b>What a row's declaration IS, and what it currently holds.</b>
/// ⭐ The KIND travels with the values because the source that built the row is the only thing that
/// knows which carrier it read — ⛔ a section name is display text, and 📌 <c>95a</c> measured that the
/// asset is exactly what cannot be type-tested.
/// </summary>
/// <param name="TypeId">
/// ⭐ The declaration's current type id, for the form's Type combo. ⚠ Read-only in practice this batch —
/// see <c>VariablePropertyValues</c> for why a retype is not a field write.
/// </param>
public sealed record DeclarationPropertySnapshot(
    VariableDeclarationKind Kind,
    VariablePropertyValues  Values,
    string                  TypeId = "");
