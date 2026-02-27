using Bagira.BDC.SSTD;
using Bagira.IOS.Services;
using CycloneDDS.Runtime;

namespace Bagira.IOS.Tests;

/// <summary>
/// Verifies INTS-P1-003: <see cref="DdsWriterAdapter{T}"/> correctly wraps a
/// live DDS writer and enforces the <see cref="IDisposable"/> contract.
///
/// These tests interact with the CycloneDDS runtime library.  They are run on
/// domain 99 to avoid interfering with any ambient DDS traffic on domain 0.
/// </summary>
[Collection("Integration")]
public class DdsWriterAdapterTests
{
    private const uint TestDomain = 99u;

    // ═══════════════════════════════════════════════════════════════════════════
    // P1-003-T1: Implements interface
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <see cref="DdsWriterAdapter{T}"/> must be assignable to <see cref="IDdsWriter{T}"/>.
    /// Verifies that the implementation satisfies the dependency-injection contract
    /// used by <c>IosLogic</c> and related services.
    /// </summary>
    [Fact]
    public void DdsWriterAdapter_ImplementsIDdsWriter()
    {
        // Compile-time check — ensure assignment is valid.
        // No DDS participant needed; the cast is a pure type-system assertion.
        Assert.True(
            typeof(IDdsWriter<MapInteractionConfig>).IsAssignableFrom(
                typeof(DdsWriterAdapter<MapInteractionConfig>)),
            "DdsWriterAdapter<T> must implement IDdsWriter<T>.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // P1-003-T2: Dispose contract
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Calling <see cref="DdsWriterAdapter{T}.Dispose"/> on an un-written adapter
    /// must not throw.
    /// </summary>
    [Fact]
    public void DdsWriterAdapter_Dispose_DoesNotThrow()
    {
        using var participant = new DdsParticipant(TestDomain);

        var exception = Record.Exception(() =>
        {
            using var adapter = new DdsWriterAdapter<MapInteractionConfig>(
                participant, "MapInteractionConfig_Test");
        });

        Assert.Null(exception);
    }

    /// <summary>
    /// Calling <see cref="DdsWriterAdapter{T}.Dispose"/> twice must be idempotent —
    /// second call must not throw.
    /// </summary>
    [Fact]
    public void DdsWriterAdapter_DoubleDispose_IsIdempotent()
    {
        using var participant = new DdsParticipant(TestDomain);
        var adapter = new DdsWriterAdapter<MapInteractionConfig>(
            participant, "MapInteractionConfig_Test2");

        adapter.Dispose();

        var exception = Record.Exception(() => adapter.Dispose());

        Assert.Null(exception);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // P1-003-T3: Write after dispose
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Calling <see cref="DdsWriterAdapter{T}.Write"/> after
    /// <see cref="DdsWriterAdapter{T}.Dispose"/> must throw
    /// <see cref="ObjectDisposedException"/>.
    /// </summary>
    [Fact]
    public void DdsWriterAdapter_WriteAfterDispose_ThrowsObjectDisposedException()
    {
        using var participant = new DdsParticipant(TestDomain);
        var adapter = new DdsWriterAdapter<MapInteractionConfig>(
            participant, "MapInteractionConfig_Test3");

        adapter.Dispose();

        var sample = new MapInteractionConfig();
        Assert.Throws<ObjectDisposedException>(() => adapter.Write(sample));
    }
}
