using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Ambient;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules.Actions;
using Hrot.Core.Ambience;

namespace Hrot.AI.Behaviors.Brains.Objectives.Verbs
{
    /// <summary>
    /// ⭐ <c>CE-11034</c> (R-11014, AM-3; 📄 docs/designs/audio/DESIGN_Ambience.md §1, §4) — <c>SetAmbience</c>'s params: the layer's <c>Kind</c> (a dotted name from <see cref="AmbienceKinds"/>),
    /// <c>Intensity</c> (0…1; <b>0 removes the layer</b>), <c>Place</c> (none = POSITIONLESS, heard everywhere; a point or <c>"@Key"</c> = heard within <c>Radius</c> of it), <c>Radius</c> (m) and
    /// <c>Duration</c> (sim seconds; 0 = until removed). ⚠ A place written as coordinates exactly (0,0,0) reads as "no place" — an authored town is never exactly the terrain origin;
    /// key it with <c>"@Key"</c> if it must be.
    /// </summary>
    public struct SetAmbienceParams : IActionArgsCheck
    {
        public FixedString32 Kind;
        public float Intensity;
        public PointLink Place;
        public float Radius;
        public SimDuration Duration;
        public SetAmbienceParams() { Kind = default; Intensity = 1f; Place = default; Radius = AmbienceVerbs.DefaultRadius; Duration = SimDuration.Zero; }

        /// <summary>True when the layer has a position (a place was given).</summary>
        public readonly bool Positioned => !(((Vector3)Place) == Vector3.Zero && Place.Spec.IsNone);

        /// <summary>The authoring check: a kind is required and must fit a layer. ⭐ AM-4: an UNKNOWN kind is allowed (a newer scenario on an older HROT must not fail) — it is logged once when it runs, and the editor's picker never offers it.</summary>
        public readonly string? Check(JsonObject args)
        {
            foreach (var (k, v) in args)
                if (string.Equals(k, "kind", StringComparison.OrdinalIgnoreCase) && v is JsonValue jv && jv.TryGetValue<string>(out var kind))
                {
                    if (string.IsNullOrWhiteSpace(kind)) return "the ambience kind is empty";
                    if (kind.Length > AmbienceKinds.MaxLength) return $"the ambience kind is {kind.Length} characters; at most {AmbienceKinds.MaxLength}";
                    return null;
                }
            return "kind is required";
        }
    }

    /// <summary><c>SetAmbience</c>'s working state: the layer's derived GUID (two halves — unmanaged) once it asked for one, and when.</summary>
    public struct SetAmbienceState { public byte Began; public ulong GuidLo, GuidHi; public Fdp.Core.SimInstant AskedAt; public byte Waits; }

    /// <summary>
    /// ⭐⭐ <c>CE-11034</c> (SR-93; AM-3) — <c>SetAmbience</c>: raises, updates or removes an AMBIENCE LAYER from a trigger, a behaviour or a director. The layer is an ordinary entity
    /// the ONE creation path makes (an <see cref="EntityCreationRequest"/>, TRANSIENT — the trigger is saved, never what it raised), with its FINAL <see cref="Ambience"/> and, with a place,
    /// its <c>SimTransform</c>: nothing writes a layer but its owner. The same <c>Kind</c> at the same <c>Place</c> is ONE layer — a second call UPDATES it (never duplicates); intensity 0 REMOVES it.
    /// A new layer holds the step (Running) until it appears on this node — so a following step finds it — and fails after <see cref="AppearTimeoutSeconds"/>.
    /// </summary>
    public static class AmbienceVerbs
    {
        private sealed class Log { }

        /// <summary>Metres a positioned layer is heard when the step gives none.</summary>
        public const float DefaultRadius = 500f;
        /// <summary>Sim seconds a new layer may take to appear on this node before the step fails.</summary>
        public const double AppearTimeoutSeconds = 30;
        /// <summary>Two places this close (m, on the ground plane) are the same place.</summary>
        public const float SamePlaceMetres = 0.5f;

        [SharedAiAction]
        public static NodeStatus SetAmbience(ref SetAmbienceParams p, ref SetAmbienceState s, Entity self, EntityRepository world)
        {
            double now = ObjectiveHost.Now(world);
            if (s.Began != 0)
            {
                if (s.Waits == 0) return NodeStatus.Success;   // an update / a removal: done the tick it ran
                if (!MacroInsertion.FindByGuid(world, GuidOf(s)).IsNull) return NodeStatus.Success;
                return now - s.AskedAt >= AppearTimeoutSeconds - 1e-9 ? Fail(world, self, "the ambience layer never appeared") : NodeStatus.Running;
            }

            string kind = p.Kind.ToString();
            if (string.IsNullOrWhiteSpace(kind)) return Fail(world, self, "no ambience kind named");
            s.Began = 1; s.AskedAt = (SimInstant)now;
            bool positioned = p.Positioned;
            Vector3 at = positioned ? (Vector3)p.Place : default;
            float intensity = Math.Clamp(p.Intensity, 0f, 1f);
            float radius = positioned ? (p.Radius > 0f ? p.Radius : DefaultRadius) : 0f;
            double until = p.Duration > 0f ? now + p.Duration : 0.0;
            if (AmbienceKinds.Resolve(kind) == null)   // ⭐ AM-4 — allowed, and said (once per run of the step: it runs on a rule's edge, never every frame)
                Fdp.Core.Logging.FdpLog<Log>.Warn($"SetAmbience: '{kind}' is not a listed ambience kind (nor a child of one); a renderer will find no sound for it. (asked by {self})");

            var existing = Find(world, kind, positioned, at);
            if (existing.IsNull && intensity <= 0f) return NodeStatus.Success;   // nothing sounding to remove
            var layer = new Ambience { Kind = p.Kind, Intensity = intensity, Radius = radius, Until = (SimInstant)until };

            if (existing.IsNull)
            {
                string? refused = Request(world, self, kind, positioned, at, layer, out Guid guid);
                if (refused != null) return Fail(world, self, refused);
                var bytes = guid.ToByteArray();
                s.GuidLo = BitConverter.ToUInt64(bytes, 0); s.GuidHi = BitConverter.ToUInt64(bytes, 8);
                s.Waits = 1;
                return NodeStatus.Running;
            }

            // ⭐ the layer is ANOTHER entity: changed serially (RuleAfterPhase), on the LIVE state — see CLAUDE.md "BEHAVIOUR CODE RUNS IN PARALLEL"
            RuleAfterPhase.Run(world, self, w =>
            {
                if (!w.IsAlive(existing) || !w.HasComponent<Ambience>(existing)) return;
                if (intensity <= 0f) { End(w, existing); Fdp.Core.Logging.FdpLog<Log>.Info($"ambience '{kind}': removed (asked by {self})"); return; }
                if (w.HasAuthority(existing))
                {
                    ref var cur = ref w.GetComponentRW<Ambience>(existing);   // its owner: in place
                    cur.Intensity = layer.Intensity; cur.Radius = layer.Radius; cur.Until = layer.Until;
                    Fdp.Core.Logging.FdpLog<Log>.Info($"ambience '{kind}': updated, intensity {intensity:0.##} (asked by {self})");
                }
                else
                {
                    // not ours (a replica): the owner alone writes it — end it through the network and ask for the new one
                    End(w, existing);
                    Request(w, self, kind, positioned, at, layer, out _, salt: "replace");
                    Fdp.Core.Logging.FdpLog<Log>.Info($"ambience '{kind}': replaced (a replica's owner writes it), intensity {intensity:0.##} (asked by {self})");
                }
            });
            return NodeStatus.Success;
        }

        /// <summary>The layer of this kind at this place — a positioned one within <see cref="SamePlaceMetres"/>, or a positionless one — or <see cref="Entity.Null"/>.</summary>
        public static Entity Find(EntityRepository world, string kind, bool positioned, Vector3 at)
        {
            if (!world.IsComponentTypeRegistered<Ambience>()) return Entity.Null;
            bool hasTransform = world.IsComponentTypeRegistered<SimTransform>();
            foreach (var e in world.Query().With<Ambience>().Build())
            {
                if (!world.GetComponentRO<Ambience>(e).Kind.Equals(new FixedString32(kind))) continue;
                bool there = hasTransform && world.HasComponent<SimTransform>(e);
                if (!positioned) { if (!there) return e; continue; }
                if (!there) continue;
                var pos = world.GetComponentRO<SimTransform>(e).Position;
                if (Vector2.Distance(new Vector2(pos.X, pos.Y), new Vector2(at.X, at.Y)) <= SamePlaceMetres) return e;
            }
            return Entity.Null;
        }

        /// <summary>Ends a layer: the one network destroy (its replicas go too) and TearDown at once, else destroyed here.</summary>
        private static void End(EntityRepository w, Entity layer)
        {
            if (NetworkEntityDestroy.Request(w, layer, "Ambience removed")) w.SetLifecycleState(layer, EntityLifecycle.TearDown);
            else w.DestroyEntity(layer);
        }

        /// <summary>
        /// Asks the ONE creation path for the layer — TRANSIENT (SR-171), its GUID DERIVED from the asker's GUID + kind + place + tick (SR-167: a replay re-asks the same id) —
        /// with its FINAL <see cref="Ambience"/> (never added then written) and, when positioned, its <c>SimTransform</c>. Null = asked; else why not.
        /// </summary>
        private static string? Request(EntityRepository world, Entity self, string kind, bool positioned, Vector3 at, in Ambience layer, out Guid guid, string salt = "")
        {
            guid = Guid.Empty;
            if (!world.Bus.IsRegisteredManaged<EntityCreationRequest>()) return "this node cannot create entities";
            Guid asker = world.IsComponentTypeRegistered<EntityGuid>() && world.HasComponent<EntityGuid>(self) ? world.GetComponentRO<EntityGuid>(self).Value : Guid.Empty;
            string where = positioned ? FormattableString.Invariant($"{at.X:0.##},{at.Y:0.##}") : "everywhere";
            guid = EntityGuidDerivation.Derive(asker, FormattableString.Invariant($"ambience:{self.Index}:{kind}:{where}:{world.SimulationTick}:{salt}"));
            var components = new List<object> { layer, new EntityGuid(guid), new EntityInfo { Name = "Ambience " + kind } };   // the name is what a check, the editor and the map's list call it
            if (positioned) components.Add(new SimTransform { Position = at, Rotation = Quaternion.Identity });
            world.Bus.PublishManaged(new EntityCreationRequest
            {
                RequestId = Guid.NewGuid(),
                OwnerAppInstanceId = Hrot.Core.Network.EntityCreationRouting.DefaultEntityCreationRequestProcessor,
                TkbType = (TkbTypeId)Hrot.Map.Common.TkbEntityTypes.AmbienceLayer,
                InitType = ReliableInitType.None,
                IsTransient = true,   // SR-171: the trigger is saved, never what it raised
                InitialComponents = components,
            });
            return null;
        }

        private static Guid GuidOf(in SetAmbienceState s)
        {
            Span<byte> b = stackalloc byte[16];
            BitConverter.TryWriteBytes(b, s.GuidLo);
            BitConverter.TryWriteBytes(b.Slice(8), s.GuidHi);
            return new Guid(b);
        }

        private static NodeStatus Fail(EntityRepository world, Entity self, string reason)
        {
            ActionFailure.Set(world, self, reason);
            return NodeStatus.Failure;
        }
    }
}
