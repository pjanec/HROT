using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Events;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Replication.Services;

namespace Fdp.Toolkit.Replication.Attributes;

/// <summary>
/// ⭐⭐⭐ <b><c>AX-005b</c> — wires the write router's *"not mine, ask the owner"* branch onto the FDP bus.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §11.3 *(the corrected intent path)*.</para>
///
/// <para>⭐⭐ <b>Nothing here touches DDS</b> *(<c>R-134</c>)</b>: it publishes the FDP-internal
/// <see cref="UpdateEntityAttributeCommand"/> carrying FDP-internal
/// <see cref="EntityAttributeChange"/> records. The wire appears only inside
/// <c>UpdateEntityAttributeCommandEgressTranslator</c>, which drains this event.</para>
///
/// <para>⭐ <b>One helper rather than a per-host lambda</b> — the entity→network-id resolution and the
/// *"unreplicated entity has nobody to ask"* rule are the same on every host, and this is where they live
/// once. ⛔ A host writing its own would be the fourth copy of the id lookup this batch just collapsed.</para>
/// </summary>
public static class EntityAttributeChangeRequests
{
    /// <summary>
    /// Builds the <c>publishRequest</c> delegate for
    /// <see cref="AttributeEntityComponentWriter"/>.
    ///
    /// <para>⚠ <b>An entity with no network identity is REFUSED, loudly.</b> 📐 The request addresses the
    /// owner by <c>NetworkId</c>, so an unreplicated entity has literally nobody to ask — ⛔ publishing
    /// <c>NetworkId = 0</c> would put a request on the wire that no node can match, and it would fail
    /// silently. ⭐ A warning names the entity instead.</para>
    /// </summary>
    /// <param name="repo">
    /// The local world — used both for the id lookup and, ⭐ deliberately, as the SOURCE OF THE BUS.
    ///
    /// <para>⚠⚠ <b>The bus is NOT a parameter, and that is a silent-failure guard.</b> 📐 The translator
    /// drains via <c>view.ReadManagedEvents&lt;T&gt;()</c>, i.e. the WORLD's bus — so a caller handed a bus
    /// parameter could pass the ORCHESTRATION bus *(which several hosts also hold)* and the command would
    /// be published successfully, drained by nobody, and lost with no error at all. ⛔ Taking the world and
    /// reading <c>repo.Bus</c> makes that mistake unrepresentable.</para>
    /// </param>
    public static Action<Entity, IReadOnlyList<EntityAttributeChange>> PublishOnto(EntityRepository repo)
    {
        ArgumentNullException.ThrowIfNull(repo);

        return (entity, changes) =>
        {
            if (changes == null || changes.Count == 0) return;

            long networkId = NetworkIdResolver.RuntimeNetworkIdOf(repo, entity);

            if (networkId <= 0)
            {
                FdpLog<AttributeEntityComponentWriter>.Warn(
                    "[AttributeWrite] Entity {0} is not replicated (no NetworkIdentity), so its {1} attribute " +
                    "change(s) cannot be requested from its owner — there is no owner to address. NOT applied.",
                    entity, changes.Count);
                return;
            }

            repo.Bus.PublishManaged(new UpdateEntityAttributeCommand
            {
                NetworkId        = networkId,
                // ⭐ AX-007 — the WHOLE batch in ONE command, so a multi-attribute gesture (a drag's
                //   GeoLat+GeoLon) reaches the owner as one apply and one geodetic flush.
                AttributeChanges = changes,
                // ⚠ Deliberately empty: this is the BINARY arm. ⛔ Sending a JSON patch too would apply the
                //   same change twice on the owner, through two different compilers.
                AttributePatchJson = string.Empty,
            });
        };
    }
    /// <summary>
    /// ⭐⭐ <b><c>CE-3003</c> — the JSON arm of the same request, for a caller that holds a JSON attribute patch
    /// rather than binary changes</b> (the debug API's <c>POST /entities/{id}/attribute</c>).
    ///
    /// <para>⭐ The owner applies it through its own <c>JsonAttributeCompiler</c>
    /// (<c>UpdateEntityAttributeRequestSystem</c>), the path <c>ExConOrbatAdapter</c> already uses — ⛔ no new
    /// message, no new translator (<c>R-158</c>). ⚠ The binary arm is left empty for the reason
    /// <see cref="PublishOnto"/> leaves the JSON one empty: one change, one compiler.</para>
    ///
    /// <para>⚠ The same rule as the binary arm: an entity with no network identity has nobody to ask, so this
    /// returns <see langword="false"/> and publishes nothing rather than put an unmatchable request on the
    /// wire.</para>
    /// </summary>
    /// <returns><see langword="true"/> when the request was published onto <paramref name="repo"/>'s bus.</returns>
    public static bool TryPublishJsonPatch(EntityRepository repo, Entity entity, string patchJson)
    {
        ArgumentNullException.ThrowIfNull(repo);
        if (string.IsNullOrWhiteSpace(patchJson)) return false;

        long networkId = NetworkIdResolver.RuntimeNetworkIdOf(repo, entity);
        if (networkId <= 0) return false;

        repo.Bus.PublishManaged(new UpdateEntityAttributeCommand
        {
            NetworkId          = networkId,
            AttributePatchJson = patchJson,
            AttributeChanges   = null,
        });
        return true;
    }
}
