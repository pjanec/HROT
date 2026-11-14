using System.Numerics;

namespace HrotStrideApp;

/// <summary>
/// ⭐⭐ <b><c>CE-083</c> — this host's ONE title-bar colour</b>, the same shape every other host has
/// (<c>SimHostWindowColor</c>, and its peers on CGF / IG / the editor).
/// </summary>
/// <remarks>
/// ⭐ A single constant, so there is one value and no way for two windows of this host to drift apart —
/// which is the defect <c>CE-083</c> was raised for.
/// <para>⛔ <b>Deliberately not SimHost's dark red.</b> A mode-2 Stride node reports itself under the
/// <c>SimHost</c> PERSPECTIVE (<c>CE-242</c>: the perspective is what routes to a node's surface), so
/// its windows would otherwise be visually indistinguishable from a real SimHost's. ⭐ The routing
/// identity and the operator's visual identity are different questions, and this is the second one.</para>
/// </remarks>
internal static class StrideWindowColor
{
    /// <summary>Stride teal — distinct from SimHost dark red, CGF, IG and the editor.</summary>
    internal static readonly Vector4 TitleBar = new(0.10f, 0.38f, 0.42f, 1f);
}
