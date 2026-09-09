using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>BP-509</c> — the scenario load's staging→runtime network-id translation table,
    /// published on the CONTROL-PLANE bus.</b>
    /// 📄 <c>DESIGN_Variable_Watch_Pinning.md</c> §5 *(restart survival BY TRANSLATION)</b>, §8 ①, §8a.
    ///
    /// <para>⛔⛔ <b>Why this event exists at all.</b> 📐 <c>StagingEntityExtractor</c> Pass 1 allocates a
    /// NEW runtime network id for every authored entity on every load, and records
    /// <c>oldToNewMap</c> — ⛔ <b>which was a LOCAL that died inside the extractor</b>. ⇒ anything keyed on
    /// a RUNTIME id *(a watch pin, a breakpoint)* broke on every scenario reload, with no way to recover
    /// the correspondence.</para>
    ///
    /// <para>⭐⭐ <b>Why the BUS and not a shared object.</b> 📌 <c>R-79</c>: the editor and CGF are
    /// <b>separately deployable</b> — in a cluster run the extraction happens in another process
    /// entirely. ⇒ ⛔ an in-process shared map would work in the editor and silently be empty everywhere
    /// else. ⭐ The control-plane bus is the channel that already carries every load's orchestration
    /// traffic.</para>
    ///
    /// <para>⭐ <b>A managed <c>Dictionary</c> travels whole</b> — <c>FdpEventBus.PublishManaged&lt;T&gt;</c>
    /// carries no <c>unmanaged</c> constraint, so ⛔ there is no flattening to parallel arrays.</para>
    ///
    /// <para>⛔⛔ <b>The remap CODE does not move and is not copied</b> *(ruling 9 · <c>R-79</c> — the most
    /// safety-critical mapping in the system)*. ⭐ <b>Only the map is published.</b></para>
    /// </summary>
    [EventId(9059)]
    [DataPolicy(DataPolicy.NoRecord)]
    public struct StagingRemapPublishedEvent
    {
        /// <summary>
        /// ⭐ <b>authored (staging) network id → runtime network id</b>, for the load that just ran.
        ///
        /// <para>⭐⭐ <b>The direction matters and is not arbitrary:</b> the STAGING id is the stable
        /// authoring artefact — what a designer was looking at when they pinned something — and the
        /// runtime id is what this load happened to hand out. ⇒ a durable key stores the staging id and
        /// translates FORWARD here; a caller that holds a runtime id and wants to make it durable
        /// inverts this map.</para>
        ///
        /// <para>⚠ Never <c>null</c> in a published event, and it may legitimately be EMPTY — a scenario
        /// with no networked entities remaps nothing, and that is an answer.</para>
        /// </summary>
        public IReadOnlyDictionary<long, long> StagingToRuntime;

        /// <summary>
        /// ⭐ The node that performed the extraction. ⚠ Present so a reader in a multi-node run can tell
        /// whose load it is looking at rather than assuming the last one wins.
        /// </summary>
        public int SourceNodeId;
    }
}
