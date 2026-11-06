using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Interfaces;
using Fdp.Toolkit.Lifecycle;
using Hrot.Common.EntityCreation;
using Hrot.Common.Infrastructure;
using Hrot.Core.Network;

namespace Hrot.EcsNode.Hosting
{
    /// <summary>
    /// ⭐ CE-4270 B0 (R-3077, D5) — where a host registers its capabilities and the genesis tail RELATIVE to the creation pack. ⚠ Temporary: the
    /// hosts registered them in two different orders; each host moves onto <see cref="HostSpawning"/> with its OWN order first (its S0 snapshot
    /// identical), then ONE commit makes every host <see cref="PackThenCapabilities"/> (D5: "I see no reason why would a different host
    /// initialize in different order").
    /// </summary>
    public enum SpawningOrder
    {
        /// <summary>The pack, then the capabilities, then the genesis tail — SimHost, CGF, the editor; the ONE order after D5.</summary>
        PackThenCapabilities,
        /// <summary>The capabilities, then the pack, then the genesis tail — IG, Traffic (until D5).</summary>
        CapabilitiesThenPack,
        /// <summary>The capabilities, then the genesis tail, then the pack — Stride (until D5).</summary>
        CapabilitiesThenGenesisThenPack,
    }

    /// <summary>
    /// ⭐ CE-4270 (docs/DESIGN_Subsystem_Composition_Unification.md §4.1ag) — what differs between hosts in the spawning step, as VALUES. Everything
    /// else (the lifecycle module, the network adapters, the expected peers, the genesis tail) is the same for every host and is not here.
    /// </summary>
    public sealed record SpawningSpec
    {
        /// <summary>The one node that answers broadcast creation requests — the Brain (Q65 §1): CGF, the editor, the fused server.</summary>
        public bool IsBroadcastArbiter { get; init; }
        /// <summary>TKB translators beyond the shared ones (AI diagnostics, animation).</summary>
        public IReadOnlyList<ITkbEntityTranslator>? ExtraTranslators { get; init; }
        /// <summary>Translator placements (the editor's per-translator schedule); null ⇒ the default.</summary>
        public IReadOnlyList<Hrot.Core.Tkb.TranslatorPlacement>? TranslatorPlacements { get; init; }
        /// <summary>The macro expander — a Brain host's.</summary>
        public Hrot.Common.Macros.MacroExpander? Macros { get; init; }
        /// <summary>An offline host's attribute compiler (a networked one takes it from its adapters).</summary>
        public Fdp.Toolkit.Replication.Patching.JsonAttributeCompiler? JsonAttributeCompiler { get; init; }
        /// <summary>No allocator (a headless rail) ⇒ no pack, as Traffic and Stride always did.</summary>
        public bool SkipWithoutAllocator { get; init; }
        /// <summary>See <see cref="SpawningOrder"/> — temporary until D5.</summary>
        public SpawningOrder Order { get; init; } = SpawningOrder.PackThenCapabilities;
    }

    /// <summary>
    /// ⭐⭐ CE-4270 B0 — the ONE place a host builds and schedules its entity-creation pack. 📐 Before it, five hosts each carried their own copy of
    /// the same <c>EntityCreationPack.Build</c> (60–80 % identical, differing in 2–4 values) and Traffic a private one marked <c>UNIFY</c>.
    /// </summary>
    public static class HostSpawning
    {
        /// <summary>
        /// Builds the pack from <paramref name="spec"/>, schedules it on the kernel, and runs <paramref name="registerCapabilities"/> and the
        /// genesis tail in the spec's order. Returns the pack (null when skipped for a missing allocator).
        /// </summary>
        public static EntityCreation? Register(HrotNodeContext context, INetworkFactory? networkFactory, SpawningSpec spec, Action registerCapabilities)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(registerCapabilities);
            EntityCreation? creation = null;
            switch (spec.Order)
            {
                case SpawningOrder.PackThenCapabilities:
                    creation = Pack(context, networkFactory, spec);
                    registerCapabilities();
                    Genesis(context, spec, creation);
                    break;
                case SpawningOrder.CapabilitiesThenPack:
                    registerCapabilities();
                    creation = Pack(context, networkFactory, spec);
                    Genesis(context, spec, creation);
                    break;
                case SpawningOrder.CapabilitiesThenGenesisThenPack:
                    registerCapabilities();
                    Hrot.SimHost.Systems.GenesisTail.RegisterOn(context.Kernel, context.EntityMap);
                    creation = Pack(context, networkFactory, spec);
                    break;
            }
            return creation;
        }

        private static void Genesis(HrotNodeContext context, SpawningSpec spec, EntityCreation? creation)
        {
            if (creation == null && spec.SkipWithoutAllocator) return;   // Traffic skipped its tail with its pack
            Hrot.SimHost.Systems.GenesisTail.RegisterOn(context.Kernel, context.EntityMap);
        }

        private static EntityCreation? Pack(HrotNodeContext context, INetworkFactory? networkFactory, SpawningSpec spec)
        {
            if (spec.SkipWithoutAllocator && context.IdAllocator == null) return null;
            var creation = EntityCreationPack.Build(new EntityCreationContext
            {
                World                 = context.World,
                EntityMap             = context.EntityMap,
                TkbDb                 = context.TkbDb!,
                IdAllocator           = context.IdAllocator!,
                Elm                   = context.BaseModules.OfType<EntityLifecycleModule>().First(),
                NodeId                = context.NodeId,
                NetworkAdapters       = networkFactory?.CreateCgfEntityLifecycleAdapters(),
                ExpectedPeers         = context.NedReplication?.ExpectedPeers,
                ExtraTranslators      = spec.ExtraTranslators,
                TranslatorPlacements  = spec.TranslatorPlacements,
                IsBroadcastArbiter    = spec.IsBroadcastArbiter,
                Macros                = spec.Macros,
                JsonAttributeCompiler = spec.JsonAttributeCompiler,
            });
            creation.RegisterOn(context.Kernel);
            return creation;
        }
    }
}
