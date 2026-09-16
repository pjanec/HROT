using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Hrot.Core.Network;

namespace Hrot.IG.EntityCreation;

/// <summary>
/// ⭐⭐⭐ <b>Converts an IG authoring tool's <see cref="SpawnEntityCommand"/> into the cross-node
/// <see cref="EntityCreationRequest"/> INTENT that the shared creation pipeline consumes.</b>
///
/// <para>📄 <c>docs/DESIGN_Entity_Creation_Unification.md</c> §3.4b — host (f) IG adoption.</para>
///
/// <para>⭐ <b>Why the tools still build a command.</b> The gizmos and the ExCon panel already assemble a
/// rich <c>SpawnEntityCommand</c> carrying geometry, an anchor transform and a request id. The retarget
/// changes WHERE that shape is posted — the shared request seam instead of the event bus — and this is the
/// ONE place that knows how to translate it, rather than four sites each doing it slightly differently.
/// ⛔ Nothing here publishes: the caller enqueues onto its
/// <see cref="ScenarioEntityCreationRequestSource"/>, exactly as the Editor's scenario path does.</para>
///
/// <para>🔴 <b>Why the owner is 0 and not the command's <c>OwnerNodeId</c> — measured, and deliberate.</b>
/// The retired <c>SpawnEntityCommandEgressTranslator</c> wrote <c>Owner = default</c> onto every wire
/// sample regardless of <c>cmd.OwnerNodeId</c>, so <b>that field was dead on the egress path</b>: every IG
/// creation has always been UNTARGETED and serviced by the default processor. ⇒ mapping it faithfully to
/// <c>OwnerAppInstanceId = 0</c> preserves today's routing exactly. ⚠ Honouring
/// <c>IgNetworkConstants.LocalNodeId</c> instead would make IG own and locally materialise the entity,
/// which under <c>R-140</c> makes it non-persistable — i.e. an operator's spawned unit would silently stop
/// being saved. ⛔ That is a product decision, not a mechanical consequence of the retarget, so it is NOT
/// taken here.</para>
///
/// <para>⚠ <b><see cref="EntityCreationRequest.IsTransient"/> is left <c>false</c> for every IG tool</b>
/// for the same reason: the mechanism exists end to end (<c>D2</c>, and the wire flag added with the NED
/// egress), but WHICH IG affordances author disposable sketches versus persistent tactical graphics is a
/// product call. 📐 The tac-graphic overlay descriptor is explicitly built with
/// <c>PersistenceMode.MODE_PERSISTENT</c>, so at least the area and route tools are NOT sketches.</para>
/// </summary>
public static class IgEntityCreationRequests
{
    /// <summary>
    /// Projects <paramref name="cmd"/> onto the intent shape. The anchor transform is folded into
    /// <see cref="EntityCreationRequest.InitialComponents"/>, which is how a request conveys position —
    /// <c>CreateEntityRequestSystem</c> reads the first <c>SimTransform</c> it finds there.
    /// </summary>
    public static EntityCreationRequest FromSpawnCommand(in SpawnEntityCommand cmd)
    {
        List<object>? components = null;

        if (cmd.InitialTransform.HasValue || cmd.InitialComponents != null)
        {
            components = new List<object>();

            // The transform goes FIRST so it is the one ResolveAnchor/CreateEntityRequestSystem pick up
            // even if a caller also put a SimTransform in InitialComponents.
            if (cmd.InitialTransform.HasValue)
                components.Add(cmd.InitialTransform.Value);

            if (cmd.InitialComponents != null)
                components.AddRange(cmd.InitialComponents);
        }

        return new EntityCreationRequest
        {
            RequestId             = cmd.RequestId == Guid.Empty ? Guid.NewGuid() : cmd.RequestId,
            OwnerAppInstanceId    = 0,   // untargeted — see the class remarks
            TkbType               = cmd.TkbType,
            DisType               = cmd.DisType,
            InitialAttributesJson = cmd.InitialAttributesJson,
            InitialComponents     = components,
            PreAllocatedNetworkId = cmd.NetworkId,
            InitType              = cmd.InitType,
            IsTransient           = false,
        };
    }
}
