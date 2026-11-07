using System;
using System.Collections.Generic;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;
using Hrot.Core.Network;

namespace Hrot.Common.Macros
{
    /// <summary>
    /// ⭐⭐ A5 (📄 docs/UX/DESIGN_Entity_Copy_Paste.md §2.1) — <b>THE ONE slice expansion</b>: a scenario-shaped JSON slice → the ordinary creation requests of
    /// every entity in it, before anything exists. Shared by the macro expander (a slice read from a macro FILE) and Copy / Paste (a slice of the open
    /// scenario, in memory), so the two cannot drift.
    /// <para>The extractor pre-allocates every network id and remaps the references inside the slice to them (multi-entity references by
    /// construction); <paramref name="relocate"/> moves every positional leaf; each entity's slice <see cref="EntityGuid"/> is replaced by one derived
    /// from <see cref="Options.GuidRoot"/> (<c>prefix + local GUID</c>, as SR-167 does for macro children) so a second expansion never collides.</para>
    /// <para>What differs per caller is an option: transient (a macro child, SR-169) or saved (a paste), the owner, the outside scope (a paste resolves
    /// what it did not copy in the live world; a macro resolves outside ONLY through its args, SR-58, so it passes none), and a per-child hook (the
    /// macro's origin and nested flattening).</para>
    /// </summary>
    public sealed class SliceExpander
    {
        /// <summary>A caller's step on each child: the extracted request, its components (edit in place), the slice's own GUID and the derived one.</summary>
        public delegate void PerChildHook(EntityCreationRequest extracted, List<object> components, Guid localGuid, Guid childGuid);

        /// <param name="GuidRoot">What every child GUID derives from (a macro instance's GUID, a paste's id).</param>
        /// <param name="GuidPrefix">The derivation's prefix (<c>"macro:"</c>, <c>"copy:"</c>).</param>
        /// <param name="Owner">The owner every child gets (0 = the default processor picks).</param>
        /// <param name="Transient">A macro child is never saved (SR-169); a pasted entity is.</param>
        /// <param name="BeforeResolve">A pass before references resolve (a macro's parameter binding).</param>
        /// <param name="Outside">Where a reference the slice does not hold resolves (null = nowhere).</param>
        /// <param name="PerChild">The caller's step on each child, or null.</param>
        public sealed record Options(Guid GuidRoot, string GuidPrefix, int Owner, bool Transient,
                                     LeafApplier? BeforeResolve = null, IRefScope? Outside = null, PerChildHook? PerChild = null);

        /// <summary>An entity of the slice carries no key (<see cref="EntityGuid"/>): nothing is created.</summary>
        public sealed class NoKey : InvalidOperationException { public NoKey() : base("an entity of the slice has no key (EntityGuid)") { } }

        private readonly IScenarioEntityExtractor _extractor;
        private readonly ScenarioSerializer _serializer;
        private readonly ScenarioBehaviorRemapper? _behaviorRemapper;

        public SliceExpander(IScenarioEntityExtractor extractor, ScenarioSerializer serializer, ScenarioBehaviorRemapper? behaviorRemapper = null)
        {
            _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _behaviorRemapper = behaviorRemapper;
        }

        /// <summary>The serializer slices are written with and read back by (Copy writes its slice through it).</summary>
        public ScenarioSerializer Serializer => _serializer;

        /// <summary><paramref name="r"/> under another request id (the edit framework mints the id a creation is journalled under).</summary>
        public static EntityCreationRequest WithRequestId(EntityCreationRequest r, Guid id) => new()
        {
            RequestId               = id,
            OwnerAppInstanceId      = r.OwnerAppInstanceId,
            TkbType                 = r.TkbType,
            DisType                 = r.DisType,
            InitialAttributesJson   = r.InitialAttributesJson,
            InitialComponents       = r.InitialComponents,
            PreAllocatedNetworkId   = r.PreAllocatedNetworkId,
            ChildComponentOverrides = r.ChildComponentOverrides,
            InitType                = r.InitType,
            ReliableInitTimeout     = r.ReliableInitTimeout,
            IsTransient             = r.IsTransient,
            IsScenarioLoad          = r.IsScenarioLoad,
            SpawnHeight             = r.SpawnHeight,
        };

        /// <summary>The child GUID of <paramref name="local"/> under <paramref name="root"/> (SR-167's derivation, shared).</summary>
        public static Guid ChildGuid(Guid root, string prefix, Guid local) => EntityGuidDerivation.Derive(root, prefix + local.ToString("D"));

        /// <summary>Expands <paramref name="sliceJson"/> into the requests that create it (appended to <paramref name="into"/>, in slice order).</summary>
        /// <exception cref="NoKey">An entity of the slice has no <see cref="EntityGuid"/>.</exception>
        public void Expand(string sliceJson, LeafApplier? relocate, INetworkIdAllocator ids, Options options, List<EntityCreationRequest> into)
        {
            ArgumentNullException.ThrowIfNull(sliceJson);
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(into);

            var requests = _extractor.Extract(_serializer, sliceJson, ids, _behaviorRemapper,
                                              new ExtractionPasses(BeforeResolve: options.BeforeResolve, AfterResolve: relocate, Outside: options.Outside));
            for (int n = 0; n < requests.Count; n++)
            {
                var r = requests[n];
                var comps = r.InitialComponents is null ? new List<object>() : new List<object>(r.InitialComponents);
                Guid local = Guid.Empty;
                for (int i = comps.Count - 1; i >= 0; i--)
                    if (comps[i] is EntityGuid g) { local = g.Value; comps.RemoveAt(i); }
                if (local == Guid.Empty) throw new NoKey();
                var childGuid = ChildGuid(options.GuidRoot, options.GuidPrefix, local);
                comps.Add(new EntityGuid(childGuid));
                options.PerChild?.Invoke(r, comps, local, childGuid);

                into.Add(new EntityCreationRequest
                {
                    RequestId               = Guid.NewGuid(),
                    OwnerAppInstanceId      = options.Owner,
                    TkbType                 = r.TkbType,
                    DisType                 = r.DisType,
                    InitialComponents       = comps,
                    PreAllocatedNetworkId   = r.PreAllocatedNetworkId,
                    ChildComponentOverrides = r.ChildComponentOverrides,
                    IsScenarioLoad          = true,    // a scenario-shaped slice: its subordinates are its own entities (CE-3324)
                    IsTransient             = options.Transient,
                    SpawnHeight             = r.SpawnHeight,
                    InitType                = r.InitType,
                });
            }
        }
    }
}
