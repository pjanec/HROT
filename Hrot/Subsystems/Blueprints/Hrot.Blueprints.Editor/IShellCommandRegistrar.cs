namespace Hrot.Blueprints.Editor;

/// <summary>
/// ⭐⭐⭐ <b>A FEATURE binds its actions to the shell's affordances — a menu path, a toolbar button, a
/// keybind.</b> 📄 <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §5b.
///
/// <para>⚠⚠ <b>RENAMED `2026-08-27` from <c>IWindowRegistrar</c>, and the old name was actively
/// misleading.</b> 📐 There were <b>TWO</b> interfaces called <c>IWindowRegistrar</c> with unrelated
/// contracts:
/// <list type="bullet">
/// <item>⭐ <c>Fdp.Toolkit.Runner.IWindowRegistrar</c> — <c>RegisterWindows(WindowManager)</c>. A <b>HOST</b>
/// seam: the subsystem orchestrator calls it once per subsystem at startup. <b>8 subsystems implement
/// it.</b> ⛔ Unchanged by the rename;</item>
/// <item>⭐ <b>this one</b> — a <b>FEATURE</b> seam. It registers no windows at all, which is precisely why
/// the old name misdirected every reader.</item>
/// </list></para>
///
/// <para>⭐⭐⭐ <b>Why this interface matters beyond Blueprints:</b> 📐 measured `2026-08-27` at in-degree
/// <b>24</b> — ⇒ <b>the composition programme's "bundle" contract already existed here</b>, in a feature
/// assembly, doing exactly the job §5b sets out to generalise. 📌 The seam law again: *"we need a shared
/// X"* meant X was built and under-adopted. ⛔ Phase 1 invents nothing; it names this shape.</para>
///
/// <para>⭐⭐ <b><c>BlueprintWindowRegistrar</c> is the working precedent</b> — it implements <b>this</b>
/// seam *(consumed by <c>BlueprintEditorModule</c>)* <b>and</b> the engine host seam, adapting between them
/// through a private <c>WindowManagerRegistry</c>, and DI registers it as both. ⚠ Its own name still says
/// *"Window"* because it genuinely does register windows on the host side; ⛔ that one is not a misnomer.</para>
///
/// <para>⚠ <b>Not renamed with it, deliberately:</b> the engine interface keeps its name. It is the older,
/// wider-implemented of the two and *"register windows"* describes it accurately.</para>
/// </summary>
public interface IShellCommandRegistrar
{
    /// <summary>Bind <paramref name="onSelected"/> to a global-menu path (e.g. <c>"Windows/Blueprints"</c>).</summary>
    void RegisterMenuEntry(string path, Action onSelected);

    /// <summary>Bind <paramref name="onClicked"/> to a main-toolbar button labelled <paramref name="label"/>.</summary>
    void RegisterToolbarEntry(string label, Action onClicked);

    /// <summary>Bind <paramref name="onTriggered"/> to a keybind (e.g. <c>"Ctrl+B"</c>).</summary>
    void RegisterShortcut(string keybind, Action onTriggered);
}
