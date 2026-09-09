using System;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐⭐ <b><c>E7</c> — read-only, NAME-keyed access to the HOSTING occurrence's variables.</b>
    /// 📄 <c>DESIGN_Parameter_Model.md</c> §3.4 (user ruling, <c>2026-08-16</c>: <i>"use that interface
    /// for host context"</i>).
    ///
    /// <para>
    /// ⛔⛔ <b>DECLARED, NOT IMPLEMENTED — deliberately.</b> Nothing in the repository implements this
    /// yet and nothing passes a non-null instance: every resolver receives <c>null</c>, which is its
    /// defined value for a root behaviour. ⭐ <b>It exists now because adding a parameter to
    /// <see cref="ParseParamsDelegate"/> is a breaking change to every resolver, and doing that twice
    /// is the avoidable cost.</b> <c>E7a</c> populates it later without touching a signature.
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>A hosted occurrence's params may be computed from its host's variables</b> — but that is
    /// not a new supply mechanism (ruling 9). The resolver already does the computing; the one thing
    /// it lacks is <b>addressing</b>, and this is that.
    /// </para>
    ///
    /// <list type="bullet">
    ///   <item>⛔ <b>NAME-keyed, never a raw offset.</b> Cross-asset reads are <c>StructureHash</c>-
    ///   versioned; a name can be re-resolved after a layout change, an offset cannot.</item>
    ///   <item>⛔ <b>READ-ONLY.</b> A resolver never writes its host — a write path here would be a
    ///   second supply mechanism.</item>
    ///   <item>⭐ <b><c>null</c> for a root behaviour</b>, which makes <i>"do I have a host?"</i>
    ///   answerable without a sentinel.</item>
    ///   <item>⭐ <b>Fails CLOSED.</b> Hash mismatch, absent name or type mismatch ⇒ <c>false</c>, and
    ///   the resolver decides what to do. ⛔ Never a silent zero.</item>
    ///   <item>⚠ <b>Resolve-once still holds.</b> This reads the host at the CHILD'S ACTIVATION, not
    ///   continuously — live binding stays out of the model.</item>
    /// </list>
    ///
    /// <para>
    /// 📌 If this ever needs a THIRD extension, bundle it into a <c>ResolveContext</c> then — one
    /// breaking change bought deliberately, rather than churning the delegate a third time.
    /// </para>
    /// </summary>
    public interface IHostVariableAccess
    {
        /// <summary>Reads a host variable by name. Returns <c>false</c> — never a zero value — when the
        /// name is absent, the host's layout has moved, or the type does not match.</summary>
        bool TryRead<T>(string variableName, out T value) where T : unmanaged;

        /// <summary>Byte-wise read for a caller that knows the shape but not the CLR type. Returns
        /// <c>false</c> and writes nothing when the variable does not resolve or does not fit.</summary>
        bool TryReadBytes(string variableName, Span<byte> destination, out int written);
    }
}
