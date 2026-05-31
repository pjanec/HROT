using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests;

/// <summary>
/// BPF-042: ApplyReload must use a staging BehaviorRegistry so a throwing registrar
/// cannot partially corrupt the live registry.
/// BPF-044: DoLoadAndScan must propagate background scan failures to OnReloadFailed
/// (not silently swallow them).
/// </summary>
public sealed class AiHotReloadCoordinatorTests : IDisposable
{
    private readonly BehaviorRegistry              _liveRegistry      = new();
    private readonly BlueprintRegistry             _blueprintRegistry = new();
    private readonly AiHotReloadCoordinatorOptions _options           = new();

    public void Dispose()
    {
        _liveRegistry.Clear();
    }

    private AiHotReloadCoordinator CreateCoordinator() =>
        new AiHotReloadCoordinator(_liveRegistry, _blueprintRegistry, _options);

    // ---- BPF-042 tests -------------------------------------------------------

    /// <summary>
    /// BPF-042: If a registrar throws during ApplyReload, the live BehaviorRegistry
    /// must be left completely unchanged (no partial registration).
    /// </summary>
    [Fact]
    public void ApplyReload_ThrowingRegistrar_DoesNotMutateLiveRegistry()
    {
        using var coordinator = CreateCoordinator();

        // Pre-populate live registry so we can detect pollution.
        _liveRegistry.Register(1, "PreExisting",
            new BehaviorDefinition { Name = "PreExisting", BrainTier = BehaviorConstants.BrainTierBTree });

        // Build a registrar that registers "First" and then throws.
        // If the live registry is used directly (old bug), "First" would appear in it.
        ThrowingHelper.FirstName   = "First";
        ThrowingHelper.ShouldThrow = true;
        var throwingRegistrar = MakeRegistrar(typeof(ThrowingHelper));

        var alc = new AssemblyLoadContext("test-bpf042-throw", isCollectible: true);
        coordinator.EnqueueReloadForTest(new[] { throwingRegistrar }, alc);

        Exception? capturedFailure = null;
        coordinator.OnReloadFailed += ex => capturedFailure = ex;

        // DrainPendingCallbacks applies the reload; the registrar throws.
        coordinator.DrainPendingCallbacks();

        // OnReloadFailed must have fired.
        Assert.NotNull(capturedFailure);

        // Live registry must be exactly as before: only "PreExisting", NOT "First".
        Assert.True(_liveRegistry.TryGetId("PreExisting", out _));
        Assert.False(_liveRegistry.TryGetId("First", out _),
            "'First' must not appear in the live registry after a failed reload.");
    }

    /// <summary>
    /// BPF-042: A successful reload (no throws) must merge all registrations into
    /// the live registry.
    /// </summary>
    [Fact]
    public void ApplyReload_SuccessfulRegistrar_MergesIntoBehaviorRegistry()
    {
        using var coordinator = CreateCoordinator();

        SuccessHelper.BehaviorName = "NewBehavior";
        var successRegistrar = MakeRegistrar(typeof(SuccessHelper));

        var alc = new AssemblyLoadContext("test-bpf042-success", isCollectible: true);
        coordinator.EnqueueReloadForTest(new[] { successRegistrar }, alc);

        bool reloadCompleted = false;
        coordinator.OnReloadCompleted += () => reloadCompleted = true;
        coordinator.DrainPendingCallbacks();

        Assert.True(reloadCompleted);
        Assert.True(_liveRegistry.TryGetId("NewBehavior", out _),
            "'NewBehavior' must appear in the live registry after a successful reload.");
    }

    // ---- BPF-044 tests -------------------------------------------------------

    /// <summary>
    /// BPF-044: Failures enqueued from background scan must be reported to
    /// OnReloadFailed on the main thread via DrainPendingCallbacks.
    /// </summary>
    [Fact]
    public void DrainPendingCallbacks_BackgroundScanFailure_FiresOnReloadFailed()
    {
        using var coordinator = CreateCoordinator();

        var scanError = new InvalidOperationException("simulated background scan failure");
        coordinator.EnqueueFailureForTest(scanError);

        Exception? capturedFailure = null;
        coordinator.OnReloadFailed += ex => capturedFailure = ex;

        coordinator.DrainPendingCallbacks();

        Assert.NotNull(capturedFailure);
        Assert.Same(scanError, capturedFailure);
    }

    /// <summary>
    /// BPF-044: Multiple background failures must all be reported in a single
    /// DrainPendingCallbacks call.
    /// </summary>
    [Fact]
    public void DrainPendingCallbacks_MultipleBackgroundFailures_AllReported()
    {
        using var coordinator = CreateCoordinator();

        coordinator.EnqueueFailureForTest(new Exception("fail1"));
        coordinator.EnqueueFailureForTest(new Exception("fail2"));

        var reported = new List<Exception>();
        coordinator.OnReloadFailed += ex => reported.Add(ex);
        coordinator.DrainPendingCallbacks();

        Assert.Equal(2, reported.Count);
        Assert.Equal("fail1", reported[0].Message);
        Assert.Equal("fail2", reported[1].Message);
    }

    // ---- Helpers -------------------------------------------------------

    private static ResolvedRegistrar MakeRegistrar(Type helperType)
    {
        var method = helperType.GetMethod("Register", BindingFlags.Public | BindingFlags.Static)!;
        var parameters = method.GetParameters()
            .Select((p, i) => new RegistrarParameter(p.Name ?? $"arg{i}", p.ParameterType, i))
            .ToList();
        return new ResolvedRegistrar(helperType, method, parameters);
    }

    // ---- Inner registrar helpers -------------------------------------------------------

    private static class ThrowingHelper
    {
        public static string FirstName   = "";
        public static bool   ShouldThrow = false;

        public static void Register(BehaviorRegistry registry)
        {
            registry.Register(100, FirstName,
                new BehaviorDefinition { Name = FirstName, BrainTier = BehaviorConstants.BrainTierBTree });
            if (ShouldThrow)
                throw new InvalidOperationException("registrar threw after partial registration");
        }
    }

    private static class SuccessHelper
    {
        public static string BehaviorName = "";

        public static void Register(BehaviorRegistry registry)
        {
            registry.Register(200, BehaviorName,
                new BehaviorDefinition { Name = BehaviorName, BrainTier = BehaviorConstants.BrainTierBTree });
        }
    }
}

