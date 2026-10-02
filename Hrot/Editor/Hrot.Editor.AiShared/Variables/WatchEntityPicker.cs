using System.Threading;
using System.Threading.Tasks;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b><c>BP-507</c> / <c>AQ55</c> — "ask the designer to point at an entity, and tell me what a
/// CONCRETE pin on it would be bound to."</b>
/// 📄 <c>Architect_Question_55_Watch_Concrete_Entity_Picker.md</c> *(<c>Q55-A</c>/<c>Q55-C</c>)* ·
/// <c>DESIGN_Variable_Watch_Pinning.md</c> §3 *(the two kinds)*, §9c *(the picker)*.
///
/// <para>⭐⭐ <b>The pick itself is REUSED, exactly as <c>Q55-A</c> rules</b> — the host implements this
/// by calling the existing <c>IMapPickService.PickEntityAsync()</c>, whose returned <c>NetworkId</c> is
/// already §3's restart-stable identity, and then resolving that id to an in-session <c>Entity</c> the
/// way it already does for *"Mark Target for N Units…"*. ⛔ <b>No second picker is built.</b></para>
///
/// <para>⚠⚠ <b>Why a delegate and not the <c>IMapPickService</c> the AQ55 class diagram draws.</b>
/// 📐 Measured: <c>IMapPickService</c> lives in <c>Hrot.Presentation</c>, which
/// <c>Hrot.Editor.AiShared</c> does <b>not</b> reference — and referencing it would point the shared
/// editor library at the application layer that composes it. ⭐ This codebase already has a settled
/// answer for *"an AiShared window needs a host capability"*: a delegate the host installs
/// *(<c>SetRunStateSource</c>, <c>SetFacetEditService</c>, <c>SetFacetDispatcher</c>)*. ⇒ the SERVICE is
/// reused; only the way its type crosses the assembly boundary differs. 📌 Folded back into AQ55 as a
/// named deviation *(obligation ⑤)*.</para>
///
/// <para>⭐ <b>It returns the BINDING, not the id</b>, because the two halves of a concrete binding —
/// the durable <c>NetworkId</c> and the in-session <c>Entity</c> — are both things only the host can
/// produce, and splitting them across the seam would make the window resolve entities it has no map
/// for.</para>
/// </summary>
/// <param name="ct">Cancels the pick; the host's map-pick mode exits and the task faults or answers null.</param>
/// <returns>
/// ⭐ The concrete binding for the picked entity, or <see langword="null"/> when the designer cancelled
/// or the pick produced nothing usable. ⛔ Never a chameleon — this gesture exists to bind to a
/// SPECIFIC entity.
/// </returns>
public delegate Task<EntityBinding?> WatchEntityPicker(CancellationToken ct);
