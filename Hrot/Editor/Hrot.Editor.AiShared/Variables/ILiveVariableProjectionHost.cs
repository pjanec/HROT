using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐ <b>Batch 90 — implemented by a window that BUILDS ROW SOURCES and therefore needs the live
/// projection.</b>
///
/// <para>⭐⭐⭐ <b>Why an interface rather than a constructor argument.</b> 📌 <b><c>R-67</c></b>, and
/// <c>BlueprintMyBlueprintWindow</c> is registered through
/// <c>PerspectiveWorkspaceRegistrar.RegisterExtraWindow</c> — ⭐ <b>the registrar ALREADY HOLDS the
/// provider</b> *(it forwards it to <c>BlackboardAuthoringWindow</c>)*, so stating the need as an
/// interface means the registrar installs it in its ONE pass and ⛔ <b>the composition root gains
/// nothing to forget.</b> ⚠ The Blueprint registrar is the one that has forgotten a service FOUR
/// times.</para>
///
/// <para>⛔ <b>The AI hosts do NOT implement this</b>, and that is not an oversight: their row source
/// is built by the registrar itself *(<c>_sectionSource</c>)*, which already has the provider in hand
/// — ⭐ nothing needs to travel.</para>
/// </summary>
public interface ILiveVariableProjectionHost
{
    /// <summary>
    /// ⭐ Supplies the live projection, or <c>null</c> when this host has none.
    /// ⚠ Called by the registrar at registration; ⛔ a host must tolerate never being called, which is
    /// the headless case and must render <c>(pending)</c> rather than throwing.
    /// </summary>
    void SetLiveProjection(ILiveVariableProjection? projection);
}
