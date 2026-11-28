using Fdp.Toolkit.Replication;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Serializers;

namespace Hrot.SimHost.Serializers
{
    /// <summary>
    /// ⭐ Simulated devices SD-S5b (§3.13d D-119) — custom scenario translator for <see cref="DeviceLinks"/>: the devices a device SENDS to.
    ///
    /// <para>On <b>Extract</b> (save), resolves each peer handle to a stable GUID string (null for no peer).</para>
    /// <para>On <b>Inject</b> (load), resolves each GUID to a Network ID and writes <see cref="InitialDeviceLinksIntent"/> so that
    /// <c>GenesisMaterializationSystem</c> can resolve it to live <see cref="Entity"/> handles once every named peer is alive. ⛔ Not the generic serializer: a
    /// device is created through its TKB type after the file's own entities, so a plain <c>Entity</c> field would name the load pass's placeholder (measured —
    /// <c>dv-linked-target</c>'s first run sent to an entity that was no device).</para>
    /// </summary>
    public sealed class DeviceLinksTranslator : IEntityScenarioTranslator
    {
        private const string Key = "DeviceLinks";

        // ── IEntityScenarioTranslator ─────────────────────────────────────────

        public BitMask512 GetConsumedComponentsMask()
        {
            var mask = new BitMask512();
            int id = ComponentTypeRegistry.GetId(typeof(DeviceLinks));
            if (id >= 0) mask.SetBit(id);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.IsComponentTypeRegistered<DeviceLinks>() && repo.HasComponent<DeviceLinks>(entity);

        public Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver resolver)
        {
            var links = repo.GetComponent<DeviceLinks>(entity);
            return new Dictionary<string, object>
            {
                [Key] = new JsonObject { ["Peer0"] = GuidOf(repo, links.Peer0, resolver), ["Peer1"] = GuidOf(repo, links.Peer1, resolver) }
            };
        }

        private static string? GuidOf(EntityRepository repo, Entity peer, IGuidResolver resolver)
            => !peer.IsNull && repo.IsAlive(peer) ? resolver.Resolve(peer) : null;

        public void Inject(
            EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver resolver)
        {
            if (!scenarioData.TryGetValue(Key, out var raw)) return;
            if (raw is not JsonObject obj) return;

            var guid0 = obj["Peer0"]?.GetValue<string?>();
            var guid1 = obj["Peer1"]?.GetValue<string?>();
            if (string.IsNullOrEmpty(guid0) && string.IsNullOrEmpty(guid1)) return;

            var intent = new InitialDeviceLinksIntent();
            if (!string.IsNullOrEmpty(guid0)) intent.Peer0 = FileLinks.FromGuidText(guid0);
            if (!string.IsNullOrEmpty(guid1)) intent.Peer1 = FileLinks.FromGuidText(guid1);
            repo.SetManagedComponent(entity, intent);
        }

        public IEnumerable<string> GetOutputDomKeys() => Array.Empty<string>();
    }
}
