using System;

namespace Hrot.Presentation.DebugApi;

/// <summary>
/// ⭐⭐⭐ <b>THE TYPED *"this host does not offer that capability"* SIGNAL.</b>
/// 📄 <c>Architect_Question_54</c> Q54-1 **Option C** *(RESOLVED)* · charter <c>D3</c>/<c>D4</c>.
///
/// <para>⛔⛔ <b>Why not a bare 404, and why not an empty model.</b> 📌 Q54 rejected both explicitly:
/// <list type="bullet">
/// <item>a <b>404</b> is *"a 404 to interpret"* — a genuinely broken panel and an unported one look
/// identical, which is what makes absence un-assertable;</item>
/// <item>a <b>silent null / empty model</b> is worse: the false green the programme exists to kill — a broken
/// panel reads as *"not implemented yet"* forever.</item>
/// </list>
/// ⇒ ⭐⭐ absence is <b>DECLARED</b>, carries the capability KEY, and conformance reads it from the manifest
/// rather than inferring it from a missing panel.</para>
///
/// <para>⭐ <c>DebugApiHost</c> maps this to HTTP <b>501</b> with
/// <c>{"code":"NOT_SUPPORTED_HERE","capability":"…"}</c> — a machine-readable answer an agent can act on.</para>
/// </summary>
public sealed class NotSupportedHereException : Exception
{
    /// <summary>⭐ The capability key that is absent — one of <see cref="DebugCapabilities"/>'s constants.</summary>
    public string Capability { get; }

    public NotSupportedHereException(string capability)
        : base($"NOT_SUPPORTED_HERE: this host does not offer '{capability}' in the active perspective. "
             + "GET /capabilities lists what it does offer.")
        => Capability = capability;
}
