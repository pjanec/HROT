using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;

namespace Hrot.AI.Behaviors.Brains.Objectives.Verbs
{
    /// <summary>
    /// ⭐⭐ rules S4 E-1 (§00.11) — the ONE place a behaviour ASKS the creation path for a macro instance, and reads how the expansion went. Extracted from
    /// the body of <see cref="MacroVerbs.InsertMacro"/> (macros S3b B1); <c>InsertMacro</c> and <c>MemberEvaluation</c> both call it, so a runtime
    /// insertion has one shape: TRANSIENT (SR-171), the GUID DERIVED from the inserter's GUID + the tick (SR-167), the request the creating node serves.
    /// </summary>
    public static class MacroInsertion
    {
        /// <summary>
        /// Publishes the <see cref="EntityCreationRequest"/> of one macro instance at <paramref name="at"/> facing <paramref name="heading"/> with
        /// <paramref name="argsJson"/> as its args. Null = asked (<paramref name="instanceGuid"/> is the instance's GUID); else the reason it could not be.
        /// <paramref name="salt"/>: what tells apart SEVERAL insertions by one inserter in one tick (a member's id) — part of the derived GUID, so the same
        /// salt in the same tick re-derives the same id in a replay; empty for a single insertion.
        /// </summary>
        public static string? Request(EntityRepository world, Entity self, string macro, WorldPoint at, Bearing heading, string argsJson, out Guid instanceGuid, string salt = "")
        {
            instanceGuid = Guid.Empty;
            if (string.IsNullOrWhiteSpace(macro)) return "no macro named";
            if (!world.Bus.IsRegisteredManaged<EntityCreationRequest>()) return "this node cannot create entities";

            Guid inserter = world.IsComponentTypeRegistered<EntityGuid>() && world.HasComponent<EntityGuid>(self)
                ? world.GetComponentRO<EntityGuid>(self).Value : Guid.Empty;
            string key = $"insert:{self.Index}:{world.SimulationTick}";
            instanceGuid = EntityGuidDerivation.Derive(inserter, salt.Length == 0 ? key : key + ":" + salt);
            var rotation = Fdp.Modules.Geographic.Systems.SimTransformBridgeSystem.WithHeadingDeg(System.Numerics.Quaternion.Identity, heading.Degrees);
            world.Bus.PublishManaged(new EntityCreationRequest
            {
                RequestId = Guid.NewGuid(),
                OwnerAppInstanceId = Hrot.Core.Network.EntityCreationRouting.DefaultEntityCreationRequestProcessor,
                TkbType = (TkbTypeId)Hrot.Map.Common.TkbEntityTypes.ScenarioMacro,
                InitType = ReliableInitType.None,
                IsTransient = true,   // SR-171
                InitialComponents = new List<object>
                {
                    new SimTransform { Position = at, Rotation = rotation },
                    new EntityInfo { Name = macro.Length > 60 ? macro.Substring(0, 60) : macro },
                    new EntityGuid(instanceGuid),
                    new MacroInstance { Macro = macro, ArgsJson = string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson },
                },
            });
            return null;
        }

        /// <summary>The expansion state of the instance with this GUID (SR-96): null = no such instance on this node yet; <c>Expanding</c> while it has no
        /// status; <paramref name="reason"/> = a Failed one's reason.</summary>
        public static MacroExpansionState? State(EntityRepository world, Guid instanceGuid, out string reason)
        {
            reason = "";
            var instance = Find(world, instanceGuid);
            if (instance.IsNull) return null;
            if (!world.HasComponent<MacroExpansionStatus>(instance)) return MacroExpansionState.Expanding;
            var status = world.GetComponentRO<MacroExpansionStatus>(instance);
            reason = status.Reason.Length > 0 ? status.Reason.ToString() : "";
            return status.State;
        }

        private static Entity Find(EntityRepository world, Guid guid)
        {
            if (!world.IsComponentTypeRegistered<EntityGuid>()) return Entity.Null;
            foreach (var e in world.Query().With<EntityGuid>().With<MacroExpansionStatus>().Build())
                if (world.GetComponentRO<EntityGuid>(e).Value == guid) return e;
            foreach (var e in world.Query().With<EntityGuid>().Build())
                if (world.GetComponentRO<EntityGuid>(e).Value == guid) return e;
            return Entity.Null;
        }
    }
}
