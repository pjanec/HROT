using System.Runtime.CompilerServices;
using Fdp.Core;

namespace Fdp.ModuleHost.Tests;

/// <summary>
/// A snapshot of the <see cref="FdpConfig"/> switches <b>as shipped</b>, captured before any test runs.
/// </summary>
/// <remarks>
/// <para><b>Why this type exists.</b> <c>FdpConfig</c>'s switches are mutable process-globals, and tests
/// whose <i>subject</i> is the behaviour behind a switch must set it — <c>ResilienceIntegrationTests</c>
/// turns <see cref="FdpConfig.FailFastOnModuleException"/> off because the catch path is the thing it
/// tests. xUnit runs test classes in <b>parallel</b>, so any test that reads the live static to assert
/// "the shipped default is X" is racing those constructors.</para>
///
/// <para>📌 <b>Measured, <c>2026-09-04</c>:</b> a rail written that way (<c>FailFastIsOnByDefault</c>)
/// failed deterministically the first time it was run honestly — not because the default was wrong, but
/// because it was reading a value another test had legitimately changed.</para>
///
/// <para>The module initialiser below runs once, when the assembly is first touched, which is before any
/// test constructor. ⇒ what it captures IS the shipped default, whatever ordering or parallelism the
/// runner chooses. ⛔ Do not turn these into properties that re-read <c>FdpConfig</c> — that reintroduces
/// exactly the race this exists to remove.</para>
/// </remarks>
internal static class ShippedDefaults
{
    /// <summary>The value <c>FdpConfig.FailFastOnModuleException</c> initialised itself to.</summary>
    internal static bool FailFastOnModuleException { get; private set; }

    [ModuleInitializer]
    internal static void Capture()
        => FailFastOnModuleException = FdpConfig.FailFastOnModuleException;
}
