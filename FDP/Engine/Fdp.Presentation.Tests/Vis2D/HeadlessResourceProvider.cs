using Fdp.Toolkit.Vis2D.Abstractions;

namespace Fdp.Presentation.Tests.Vis2D;

/// <summary>
/// Batch 91 (91d) — a realistic <see cref="IResourceProvider"/> for headless render tests.
///
/// <para>
/// WHY THIS EXISTS, and why the fix is here rather than in the renderer. BP-337 was the whole
/// Fdp.Presentation.Tests suite aborting with "Test host process crashed", from a
/// NullReferenceException at DebugPrimitiveRenderer2D.cs:28 — `ctx.Resources.Get&lt;MapCamera&gt;()`
/// on a RenderContext built by a test helper that never set Resources.
/// </para>
///
/// <para>
/// MEASURED before choosing a side, because a guard that hides a real contract violation is worse
/// than a red suite you can see:
/// </para>
/// <list type="bullet">
///   <item><description>RenderContext is built in production at <b>exactly one</b> site —
///   <c>MapCanvas.Draw():119</c> — and it sets <c>Resources = this</c> unconditionally. No
///   production path yields a null provider.</description></item>
///   <item><description>Three production readers dereference <c>ctx.Resources</c> with <b>no null
///   check</b>: this renderer, <c>DebugGizmoLayer:102</c>, and
///   <c>VehicleVisualizer:95</c>.</description></item>
///   <item><description>The field is declared <c>IResourceProvider Resources</c> — <b>not</b>
///   nullable — one line above <c>IDebugDrawBuilder? DrawBuilder</c>, which IS nullable and is
///   documented "May be null in headless test contexts". The author distinguished the two
///   deliberately.</description></item>
/// </list>
///
/// <para>
/// ⇒ a null provider is NOT legitimate; the fixture was unrealistic. Guarding the renderer would
/// contradict the type's own annotation and diverge from two other readers that do not guard.
/// </para>
///
/// <para>
/// Returning null from <c>Get&lt;T&gt;()</c> IS realistic: production's MapCanvas may genuinely have
/// no MapCamera registered, and the renderer already handles that
/// (<c>mapCamera != null ? mapCamera.InnerCamera : default</c>). The absent resource was never the
/// problem — the absent PROVIDER was.
/// </para>
/// </summary>
internal sealed class HeadlessResourceProvider : IResourceProvider
{
    /// <summary>The one instance; it holds no state, so a per-test allocation would be noise.</summary>
    public static readonly HeadlessResourceProvider Instance = new();

    private HeadlessResourceProvider() { }

    public T? Get<T>() where T : class => null;
    public bool Has<T>() where T : class => false;
}
