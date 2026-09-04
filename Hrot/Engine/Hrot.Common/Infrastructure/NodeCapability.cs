using System;
using System.Collections.Generic;
using System.Linq;

namespace Hrot.Common.Infrastructure;

/// <summary>
/// A named resource a capability needs, and a named capability a role selects. Both are plain
/// strings, deliberately.
/// </summary>
/// <remarks>
/// <para><b>Why strings and not typed key structs.</b> <see cref="NodeBootPlan"/> already keys its
/// <c>Requires</c>/<c>Provides</c> graph by <c>string</c>, and resources published by a provider are
/// read back through <see cref="NodeBootValues"/> using those same strings. A second, typed key
/// vocabulary would mean two spellings for one dependency and a conversion between them — which is
/// the duplication this programme removes, not an improvement on it.</para>
///
/// <para>The constants below exist so the strings are written once. A typo in a key is caught at boot
/// by the plan's own verification, not silently ignored.</para>
/// </remarks>
public static class CapabilityKeys
{
    /// <summary>Behaviour, mission planning, AI and cognitive dispatch.</summary>
    public const string Brain = "cap:brain";

    /// <summary>Ground kinematics, formation and navigation execution.</summary>
    public const string MuscleGround = "cap:muscle-ground";

    /// <summary>LOS, broadphase and threat evaluation.</summary>
    public const string Perception = "cap:perception";

    /// <summary>On-demand pathfinding.</summary>
    public const string NavigationSolver = "cap:navigation-solver";

    /// <summary>Presentation-only node; no simulation logic.</summary>
    public const string ImageGenerator = "cap:image-generator";
}

/// <summary>Keys for the one-per-world resources capabilities share.</summary>
/// <remarks>
/// Each names a resource that is allocated <b>once</b> per world and borrowed by every capability
/// that declares it. The whole point of the split (<c>B3</c>) is that a node selecting two roles
/// that both need one resource gets one allocation, not two.
/// </remarks>
public static class ResourceKeys
{
    /// <summary><c>RaycastBatchData</c> — owned by <c>PhysicsToolkitModule</c>.</summary>
    public const string RaycastBatch = "res:raycast-batch";

    /// <summary>The perception <c>SpatialHashGrid</c> — owned by <c>PerceptionGridProvider</c>.</summary>
    public const string PerceptionGrid = "res:perception-grid";

    /// <summary><c>TrajectoryPoolManager</c> — routes written by the solver, read by kinematics.</summary>
    public const string TrajectoryPool = "res:trajectory-pool";

    /// <summary>The navigation/EQS pools registered by <c>NavigationSolverComponentRegistry</c>.</summary>
    public const string NavigationPools = "res:navigation-pools";
}

/// <summary>
/// One selectable unit of node behaviour: the systems and modules a <see cref="NodeRole"/> implies,
/// plus the resources they borrow.
/// </summary>
/// <remarks>
/// <para><b>The variation point is the INSTANCE, not a factory.</b> The original design
/// (§4.1j) paired this interface with an <c>IImplementationFactory</c> so a host could ask for its
/// own variant of a capability. Measured, that indirection buys nothing: a host that needs a
/// different <c>MuscleGround</c> — Stride swapping <c>GroundKinematicsModule</c> for
/// <c>StrideKinematicsModule</c> — simply supplies a different <see cref="INodeCapability"/> under
/// the same <see cref="Key"/>. One mechanism instead of two.</para>
///
/// <para><b>Reading a resource.</b> <see cref="Register"/> receives the running plan's
/// <see cref="NodeBootValues"/>, which refuses any key the step did not declare. So a capability
/// cannot quietly reach a resource it never listed in <see cref="Needs"/> — the declaration and the
/// data cannot drift apart, which is the property <see cref="NodeBootPlan"/> already enforces for
/// boot steps and this reuses rather than reinvents.</para>
/// </remarks>
public interface INodeCapability
{
    /// <summary>Identity — one of <see cref="CapabilityKeys"/>. Two capabilities with the same key
    /// are the same capability, and only one is registered.</summary>
    string Key { get; }

    /// <summary>Resource keys this capability borrows. Every one must be provided by a
    /// <see cref="INodeResourceProvider"/> the plan also selected.</summary>
    IReadOnlyList<string> Needs { get; }

    /// <summary>
    /// Contributes systems to the node's togglable phase groups. Called during the base's
    /// <c>system-groups</c> boot step.
    /// </summary>
    /// <remarks>
    /// <para><b>Why two hooks and not one.</b> The base composes a node in two distinct steps that run
    /// at different times — <c>system-groups</c> builds the togglable phase groups, and
    /// <c>spawning-pipeline</c> registers modules and global systems. A capability that contributes to
    /// both (the Muscle pack does) needs to be asked twice. These hooks mirror the boot plan's existing
    /// steps rather than inventing a third place to compose; both default to doing nothing, so a
    /// capability implements only the half it actually contributes.</para>
    /// </remarks>
    void PopulateSystems(
        HrotNodeContext context,
        System.Collections.Generic.List<Fdp.ModuleHost.Abstractions.IEcsModuleSystem> input,
        System.Collections.Generic.List<Fdp.ModuleHost.Abstractions.IEcsModuleSystem> simulation,
        System.Collections.Generic.List<Fdp.ModuleHost.Abstractions.IEcsModuleSystem> postSimulation)
    { }

    /// <summary>Registers this capability's modules and global systems onto the node.</summary>
    /// <param name="context">The node being built.</param>
    /// <param name="values">
    /// The plan's value bag. Reads are checked against the declared <see cref="Needs"/>.
    /// </param>
    /// <remarks>
    /// ⛔ <b>Capabilities are registered in resolution order, and that order is EXECUTION order:</b>
    /// <c>ModuleHostKernel.RegisterModule</c> appends to a plain list which the frame loop iterates in
    /// sequence. A composition root reordering its capabilities is therefore changing behaviour, not
    /// tidying — which is why a switchover must reproduce the host's existing sequence exactly.
    /// </remarks>
    void Register(HrotNodeContext context, NodeBootValues values) { }
}

/// <summary>
/// Allocates one shared resource and publishes it for the capabilities that declared it.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Named <c>INodeResourceProvider</c>, not <c>IResourceProvider</c>.</b> The design called
/// it the latter; that name is already taken in this solution by
/// <c>Fdp.Presentation/Vis2D/Abstractions/IResourceProvider.cs</c>, an unrelated rendering concept.
/// Two same-named interfaces one namespace apart is a trap for the next reader, so this one carries
/// the qualifier.</para>
///
/// <para><b>The owner frees it.</b> A provider allocates in <see cref="Allocate"/> and frees in
/// <see cref="IDisposable.Dispose"/>; capabilities <i>borrow</i> and must never free. That asymmetry
/// is the one that matters: a leak wastes memory, but a borrower freeing a shared resource corrupts
/// every other consumer still holding it.</para>
/// </remarks>
public interface INodeResourceProvider : IDisposable
{
    /// <summary>Identity — one of <see cref="ResourceKeys"/>.</summary>
    string Key { get; }

    /// <summary>Allocates the resource and publishes it under <see cref="Key"/>.</summary>
    void Allocate(HrotNodeContext context, NodeBootValues values);
}

/// <summary>
/// Resolves a <see cref="NodeRole"/> flag set into the capabilities and resources a node composes.
/// </summary>
/// <remarks>
/// <para><b>What this adds over <see cref="NodeBootPlan"/>, and what it deliberately does not.</b>
/// The boot plan already answers <i>"in what order do the node's steps run, and did each one get
/// what it declared?"</i>. It has no opinion on <i>which</i> units a node should contain — that is
/// decided today by each host overriding <c>PopulateSystems</c> with a hand-written list. This class
/// answers only that second question, and then hands the result to the plan. ⛔ It does not run
/// anything, order anything, or own a lifetime.</para>
///
/// <para><b>Union, then deduplicate, IN DECLARATION ORDER.</b> A node's capability set is the union over
/// its role flags, so two roles selecting the same capability must yield ONE registration.
/// Deduplication is by <see cref="INodeCapability.Key"/> and is first-wins, matching
/// <c>SystemComposition.DistinctByType</c>'s rule for systems — the same policy at a different
/// granularity, not a second one.</para>
///
/// <para>⛔⛔ <b>The ORDER of the result is load-bearing.</b> Capabilities register in resolution order,
/// registration order is the kernel's execution order, so the resolved sequence must be exactly the
/// order the host declared — including when declarations for different roles are interleaved. A host
/// switching from a hand-written registration block to a resolved set is behaviour-preserving only if
/// this holds, which is why it has its own rail rather than being left to the collection type.</para>
/// </remarks>
public sealed class NodeCompositionPlan
{
    // ⛔⛔ A FLAT, ORDERED list — deliberately NOT a Dictionary<NodeRole, List<...>>.
    //
    // Resolution order is registration order is EXECUTION order (ModuleHostKernel.RegisterModule appends
    // to a plain List the frame loop walks in sequence). Grouping declarations by role would therefore
    // silently REORDER a host's module registrations at the moment it switched over — capabilities
    // declared A(role1) B(role2) C(role3) D(role2) would resolve as A B D C. That is a behaviour change
    // disguised as a refactor, and it is precisely what the first draft of this class did.
    private readonly List<(NodeRole Role, INodeCapability Capability)> _declared = new();
    private readonly Dictionary<string, INodeResourceProvider>         _providers = new(StringComparer.Ordinal);

    /// <summary>Declares that <paramref name="role"/> selects <paramref name="capability"/>.</summary>
    public NodeCompositionPlan Capability(NodeRole role, INodeCapability capability)
    {
        if (capability is null) throw new ArgumentNullException(nameof(capability));

        _declared.Add((role, capability));
        return this;
    }

    /// <summary>Registers the provider that can satisfy <paramref name="provider"/>'s key.</summary>
    /// <remarks>
    /// Providers are declared for the whole plan rather than per role: a resource is selected because
    /// some chosen capability <i>needs</i> it, never because a role name mentions it. That is what
    /// makes the set minimal — a node that selects no capability needing the trajectory pool never
    /// allocates one.
    /// </remarks>
    public NodeCompositionPlan Provider(INodeResourceProvider provider)
    {
        if (provider is null) throw new ArgumentNullException(nameof(provider));

        if (_providers.TryGetValue(provider.Key, out INodeResourceProvider? existing) && !ReferenceEquals(existing, provider))
            throw new InvalidOperationException(
                $"Two different providers were declared for resource '{provider.Key}' " +
                $"({existing.GetType().Name} and {provider.GetType().Name}). A resource has exactly one owner.");

        _providers[provider.Key] = provider;
        return this;
    }

    /// <summary>
    /// The capabilities <paramref name="role"/> selects, as a union over its flags, deduplicated by
    /// <see cref="INodeCapability.Key"/> in declaration order.
    /// </summary>
    public IReadOnlyList<INodeCapability> Resolve(NodeRole role)
    {
        var seen   = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<INodeCapability>();

        foreach ((NodeRole declaredFor, INodeCapability capability) in _declared)
        {
            if (declaredFor == NodeRole.None || (role & declaredFor) != declaredFor) continue;

            if (seen.Add(capability.Key))
                result.Add(capability);
        }

        return result;
    }

    /// <summary>
    /// The providers needed to satisfy <paramref name="role"/>'s capabilities — the union of their
    /// declared <see cref="INodeCapability.Needs"/>, and nothing else.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A selected capability declares a need no provider was registered for. This is the check the
    /// design's sequence diagram calls "assert every declared Need was allocated", raised here — at
    /// resolution — rather than after the fact, so a misconfigured node fails before it allocates
    /// anything.
    /// </exception>
    public IReadOnlyList<INodeResourceProvider> RequiredResources(NodeRole role)
    {
        var seen   = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<INodeResourceProvider>();

        foreach (INodeCapability capability in Resolve(role))
        {
            foreach (string need in capability.Needs)
            {
                if (!seen.Add(need)) continue;

                if (!_providers.TryGetValue(need, out INodeResourceProvider? provider))
                    throw new InvalidOperationException(
                        $"Capability '{capability.Key}' needs resource '{need}', which no provider supplies. " +
                        $"Declared providers: [{string.Join(", ", _providers.Keys.OrderBy(k => k, StringComparer.Ordinal))}]. " +
                        "Either register a provider for it, or drop it from the capability's Needs — " +
                        "an undeclared need is how a capability ends up silently allocating its own copy.");

                result.Add(provider);
            }
        }

        return result;
    }
}
