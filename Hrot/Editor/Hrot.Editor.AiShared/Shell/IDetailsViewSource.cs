using System.Collections.Generic;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L1.2</c> — A WINDOW THAT CONTRIBUTES DETAILS VIEWS SAYS SO, and the claim chain does the
/// rest.</b> 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §5: the registrar's generic half is
/// <i>"a claim chain, 9 × <c>if (window is IX)</c>; ⭐ windows self-wire, the root passes nothing
/// extra"</i>; §6 <c>L1.2</c>: <i>"registration through the existing claim chain — ⛔ no new root
/// argument"</i>.
///
/// <para>⭐⭐⭐ <b>Why an interface and not a registrar argument</b> — 📌 <c>R-67</c>, and this registrar
/// is the one that has forgotten a service <b>four times</b> *(Batches 79/80/81, then 96d)*. ⇒ ⛔ a
/// constructor parameter is one more thing <c>EditorSubsystem</c> must not forget; ⭐ an interface arm
/// means <b>a host added later binds itself with no new line anywhere.</b></para>
///
/// <para>⚠ <b>Contributing views is not the same as HOSTING the panel</b> — a window may do either,
/// both, or neither. 📌 <c>R-120</c>: a descriptor is a catalogue entry, so contributing one costs the
/// window nothing and shares no state.</para>
/// </summary>
public interface IDetailsViewSource
{
    /// <summary>
    /// ⭐ The views this window contributes to its perspective's catalogue.
    ///
    /// <para>⚠ <b>Read ONCE, at registration</b> — ⛔ not per frame. A source that wants to vary what
    /// is offered does so in its descriptors' <see cref="DetailsViewDescriptor.AppliesTo"/>
    /// predicate, which is 📌 <c>R-116</c>'s whole point.</para>
    /// </summary>
    IEnumerable<DetailsViewDescriptor> DetailsViews { get; }
}
