using System.Runtime.CompilerServices;

namespace Hrot.Blueprints.Tests;

/// <summary>
/// ⭐⭐ <b>Batch 52 §1 — the one place the suite's assembly-load dependencies are satisfied.</b>
///
/// <para>
/// ⛔ <b>The failure mode this exists to make impossible.</b> Several things the suite relies on are
/// wired by a <see cref="ModuleInitializerAttribute"/> or by scanning
/// <c>AppDomain.CurrentDomain.GetAssemblies()</c> — both of which are functions of <b>which assemblies
/// happen to be loaded</b>, and an assembly loads only when something touches a type in it. A test
/// that needs one therefore passes or fails according to <b>what else ran first</b>.
/// </para>
///
/// <para>
/// ⚠ <b>That is not hypothetical — it had already happened three times</b> before this file existed:
/// <c>BP-236</c> (a recipe fallback directory), the two <c>PdbEmbeddedSourceTests</c> the coordinator
/// bisected, and <c>Stage8Tests.Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb</c>, which was masked
/// one level deeper still — green when its own class ran, red when the single test did, because a
/// <b>sibling test in the same class</b> loaded the assembly first.
/// </para>
///
/// <para>
/// ⭐ <b>Five ad-hoc preloads had accumulated</b> — one per class that got caught — each fixing exactly
/// the test that had already gone red and none of the ones that had not yet. ⇒ 📐 <b>One module
/// initializer, running before any test in this assembly, retires the whole class.</b>
/// </para>
///
/// <para>
/// ⛔⛔ <b>Why <see cref="RuntimeHelpers.RunModuleConstructor"/> and not <c>_ = typeof(X).Assembly;</c>.</b>
/// The ad-hoc form loads the assembly, which is all a <i>scanner</i> needs. It does <b>not</b> promise
/// the target module's <c>[ModuleInitializer]</c> has run — the CLR guarantees that only before the
/// first field/method access on a type in that module, and <c>ldtoken</c> + <c>get_Assembly</c> is
/// neither. <c>Hrot.Blueprints.Core</c> wires <c>BlueprintCompiler.RoslynFinalizer</c> from exactly
/// such an initializer, so the weaker form would be a coin-flip. <c>RunModuleConstructor</c> states
/// the requirement instead of hoping for it, and is idempotent.
/// </para>
///
/// <para>
/// ⚠ <b>This file is not the proof.</b> A test asserting <i>"the assembly is loaded"</i> is exactly the
/// order-dependent green it is meant to prevent — it would pass without this file the moment anything
/// else loaded first. The proof is <c>RoslynFinalizerIsWiredTests</c> run <b>as a single isolated
/// test</b>, plus the class-by-class isolation sweep recorded in the batch report.
/// </para>
///
/// <para>
/// ⛔⛔ <b>And a warning to whoever next tries to revert-probe this file.</b> Short-circuiting
/// <see cref="Initialize"/> at RUNTIME does not disable it — every filter stayed green under an early
/// <c>return</c> and even under a <c>throw</c>. ⭐ <b>The <c>typeof(...)</c> arguments load their
/// assemblies when the JIT compiles the method body, before a single statement executes</b>, so the
/// body is doing its work merely by existing. ⇒ 📐 <b>The only true inverse is to remove
/// <c>[ModuleInitializer]</c></b>, so the method is never invoked and therefore never JIT-compiled.
/// Measured that way: all four isolated filters go red, and the two <c>BP1672</c> tests that do not
/// depend on load order stay green.
/// </para>
/// </summary>
internal static class TestAssemblyModuleInit
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "Intentional, and this is a test assembly: it establishes the load state the "
                      + "suite's correctness depends on before any test can observe it.")]
    internal static void Initialize()
    {
        // Hrot.Blueprints.Core — its [ModuleInitializer] installs BlueprintCompiler.RoslynFinalizer
        // (and BlueprintTickSystem.FrameStartCallback). Without it, a Compile() asking for a PDB
        // silently produced none; see BP1672, which now refuses that request out loud.
        Force(typeof(Hrot.Blueprints.Core.Debug.DebugProbe));

        // Hrot.AI.Behaviors — carries the [BpComponent]/recipe types that the node and component
        // registries discover by scanning loaded assemblies. This is the assembly the five ad-hoc
        // preloads named, BP-236 among them.
        Force(typeof(Hrot.AI.Behaviors.BpComponentDemo));

        // ⭐ Fhsm.Kernel — found by the Batch 52 sweep, and the purest instance of the three.
        // `MetadataReferenceResolver.ForRuntimeAssemblies(AppDomain.CurrentDomain.GetAssemblies())`
        // builds Roslyn's REFERENCE SET out of whatever is loaded, so an HsmAction primitive whose
        // generated registrar names `Fhsm.Kernel` failed to compile at all — CS0400, "could not be
        // found in the global namespace" — purely because nothing had touched that assembly yet.
        // ⚠ Its sibling test in the same class passed, because the first test's failed compile is
        // itself what loaded it.
        Force(typeof(Fhsm.Kernel.HsmActionDispatcher));
    }

    // ⚠ Deliberately an explicit witness list, NOT `Assembly.LoadFrom` over every DLL in the output
    // directory. A blanket load would also be general, but it would change what the assembly-scanning
    // registries DISCOVER — quietly widening node/component palettes and moving test outcomes that
    // have nothing to do with load order. ⭐ The sweep in `scripts/order-dependency-sweep.sh` is what
    // finds the next entry; this list is what fixes it.

    private static void Force(Type witness)
        => RuntimeHelpers.RunModuleConstructor(witness.Module.ModuleHandle);
}
