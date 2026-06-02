namespace Hrot.Editor.AiShared.Documents;

/// <summary>
/// Abstraction for switching the editor's active perspective (window group).
/// <para>
/// In production this calls <c>WindowManager.SwitchPerspective(perspectiveName)</c>.
/// In unit tests a simple lambda or fake can be injected instead.
/// </para>
/// </summary>
public interface IPerspectiveSwitcher
{
    /// <summary>
    /// Switches the editor to the named perspective (e.g. <c>"BTree"</c>, <c>"HSM"</c>, <c>"Blueprint"</c>).
    /// </summary>
    /// <param name="perspectiveName">
    /// The perspective identifier — typically the <see cref="AssetKind"/> name.
    /// </param>
    void SwitchPerspective(string perspectiveName);
}
