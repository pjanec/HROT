namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 99 (<c>99a</c>) — a host that owns a "Properties…" FORM.</b>
///
/// <para>📌 <c>R-109</c>: Properties is a <b>CUSTOM</b> form, ⛔ <b>not a StructEdit session</b> ⇒
/// <c>VariableEditGestureBinder</c> cannot open one, and the host that has the form must. ⭐ Same shape
/// as <c>ILiveVariableProjectionHost</c> and <c>IVariableWatchToggleHost</c>: the registrar wires it in
/// its ONE attach pass, so 📌 <c>R-67</c> — <b>the composition root gains nothing to forget.</b></para>
///
/// <para>⚠ <b>No host ⇒ the gesture opens nothing, and that is honest</b> rather than a dialog that
/// does nothing. ⭐ BTree and HSM have no Properties form yet; ⛔ faking one is what this refuses.</para>
/// </summary>
public interface IVariablePropertiesFormHost
{
    /// <summary>
    /// ⭐ Opens the host's form on <paramref name="row"/>. ⛔ Returns <c>false</c> when it cannot —
    /// the row could not say what its declaration is, or this host has no form for it.
    /// </summary>
    /// <param name="editable">
    /// ⭐⭐ <b>DIALOG-LEVEL</b>, already decided by <c>VariableEditPolicy</c>.
    /// ⛔ The host must NOT re-derive it *(ruling 9 — one matrix)*.
    /// </param>
    bool OpenVariableProperties(VariableRow row, bool editable);
}
