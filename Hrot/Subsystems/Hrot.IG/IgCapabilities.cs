using System.Collections.Generic;
using System;
using Fdp.ModuleHost.Abstractions;
using Hrot.Common.Infrastructure;
using Hrot.IG.Modules;
using Hrot.IG.Systems;

namespace Hrot.IG;

/// <summary>
/// IG's role-selected capabilities.
/// </summary>
/// <remarks>
/// <para><b><c>B4b</c> step 2, host (b) — the second host onto the capability axis.</b> SimHost went
/// first (<c>SimHostCapabilities</c>); this is the switchover that proves the seam generalises, and the
/// first one to find something it did not yet cover. 📄
/// <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §4.1t.</para>
///
/// <para>⛔⛔ <b>WHY THIS HOST NEEDED A THIRD HOOK.</b> SimHost's units register at the
/// <c>spawning-pipeline</c> boot step, which <see cref="INodeCapability.Register"/> covers. IG's
/// register at <c>additional-modules</c> — an <i>earlier</i> step
/// (<c>SharedApplicationBootstrapper.cs:148</c> vs <c>:172</c>) — so expressing them through
/// <c>Register</c> would have moved all five to <i>after</i> <c>context.BaseModules</c>
/// (<c>EntityLifecycleModule</c>, <c>GeographicModule</c>) and after the spawning pipeline. Registration
/// order is frame execution order, so that is a behaviour change, not a refactor.
/// <see cref="INodeCapability.ProvideModules"/> exists for exactly this step, and the sequence below is
/// pinned by <c>IgNodeBootstrapperTests</c>' two order rails.</para>
/// </remarks>
internal static class IgCapabilities
{
    /// <summary>
    /// The presentation tier: everything that turns replicated entity state into something drawable.
    /// </summary>
    /// <remarks>
    /// <para>⭐ <b>This capability contributes modules ONLY.</b> IG's <c>PopulateSystems</c> has always
    /// been empty — the node runs no simulation of its own — so there is no <c>PopulateSystems</c> half
    /// to write, and <c>Register</c> stays at its default no-op. That is the shape the interface's
    /// defaults are for: a capability implements only the part it actually contributes.</para>
    ///
    /// <para>⚠ <b>The headless branch lives HERE, not at the composition root.</b> Whether a node draws
    /// visual effects is part of what "image generator" <i>means</i> on that node, not a decision the
    /// bootstrapper makes about a capability. Keeping it inside also keeps the module sequence in one
    /// readable place, which is what the order rails assert.</para>
    /// </remarks>
    internal sealed class Presentation : INodeCapability
    {
        private readonly MapUserConfig      _userConfig;
        private readonly int                _effectiveInstanceId;
        private readonly MapCameraViewport  _cameraViewport;
        private readonly bool               _headless;

        internal Presentation(
            MapUserConfig     userConfig,
            int               effectiveInstanceId,
            MapCameraViewport cameraViewport,
            bool              headless)
        {
            _userConfig          = userConfig;
            _effectiveInstanceId = effectiveInstanceId;
            _cameraViewport      = cameraViewport;
            _headless            = headless;
        }

        public string Key => CapabilityKeys.ImageGenerator;

        /// <summary>Nothing shared. Every module below owns only its own state.</summary>
        public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();

        /// <inheritdoc/>
        public IEnumerable<IEcsModule> ProvideModules()
        {
            // E. StyleResolutionModule --- writes ResolvedStyle each Simulation tick
            yield return new StyleResolutionModule(_userConfig, _effectiveInstanceId);

            // F. MapCullingModule --- writes CullingState each PostSimulation tick
            yield return new MapCullingModule(_cameraViewport);

            // G2. MapLayerModule - assigns MapDisplayComponent bitmask per entity (time-sliced)
            yield return new MapLayerModule();

            // G. HistoryTrailModule --- records entity position trails (IG.4.1)
            yield return new HistoryTrailModule();

            // H. EventEffectModule --- spawns and cleans up visual effects (IG.4.2)
            if (!_headless)
                yield return new EventEffectModule();
        }
    }
}
