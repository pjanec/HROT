using System.Collections.Generic;
using Fdp.Core;

namespace Hrot.AI.Behaviors
{
    /// <summary>
    /// CA-07d-2 demo -- a MANAGED (reference-type) ECS component carrying a plain
    /// <see cref="System.Collections.Generic.List{T}"/> collection field, so the editor's
    /// component-collection reflector can project a collection out-pin off a native managed member
    /// (<see cref="MemberIds"/>) WITHOUT any curated <c>[BlueprintCollection]</c>/<c>[BlueprintCollectionItem]</c>
    /// accessor pair (Q#18-C/D). This is the managed counterpart of <see cref="BpCollectionDemo"/> (the
    /// curated fixed-buffer demo): there the collection is exposed via <see cref="Brains.BpCollectionDemoOps"/>
    /// static accessors; here it is exposed via native <c>.Count</c>/<c>[i]</c> on the list member, and
    /// the compiler emits an <c>IReadOnlyList&lt;int&gt;</c>-typed null-safe re-read (see
    /// <c>CollectionKind.ManagedMember</c> and <c>StatementEmitter.RenderCollectionAccessors</c>).
    /// <para>
    /// A managed component is a <c>class</c>/<c>record</c> (reference type) with a <c>[ComponentId]</c> --
    /// mirrors <c>Fdp.Tests.ManagedComponentTests.InventoryData</c> (a record with a <c>List&lt;string&gt;</c>).
    /// The five collection consumers (ForEach / ItemGet / ItemCount / Contains / Find) all work off the
    /// projected out-pin unchanged; only the emitted element access differs (native member vs curated call).
    /// </para>
    /// </summary>
    [ComponentId(190)] // Hrot application-level block (160-199); next free after 189 (BpCollectionDemo).
    public sealed class BpManagedCollectionDemo
    {
        /// <summary>Managed collection member -- the editor projects a collection out-pin off this list (element type = <see cref="int"/>); no curated accessors needed.</summary>
        public List<int> MemberIds { get; set; } = new();
    }
}
