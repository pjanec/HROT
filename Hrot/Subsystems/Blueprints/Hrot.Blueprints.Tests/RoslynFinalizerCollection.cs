using Xunit;

namespace Hrot.Blueprints.Tests;

/// <summary>
/// ⭐ <b>Batch 52 §1 — xUnit collection serializing every test class that depends on the
/// process-wide <c>BlueprintCompiler.RoslynFinalizer</c> static.</b>
///
/// <para>
/// ⛔ <b>Why it is now required.</b> <c>RoslynFinalizerIsWiredTests</c> nulls the finalizer to reach
/// <c>BP1672</c>'s arm and restores it in a <c>finally</c>. Before <c>BP1672</c>, a concurrent compile
/// that caught the null window merely got a null PDB and, at worst, one assertion. ⭐ <b>Now it gets a
/// hard compile failure</b> — which is the point of the diagnostic, and exactly why the window has to
/// stop being observable.
/// </para>
///
/// <para>
/// ⚠ <b>Membership rule:</b> any class that constructs <c>CompileOptions</c> with
/// <c>EmitPdbWithEmbeddedSource: true</c>, or that reads or writes the finalizer, belongs here.
/// Classes outside the collection still run in parallel with it — this serializes the members against
/// each other, nothing more, which is all the shared static needs.
/// </para>
/// </summary>
[CollectionDefinition("RoslynFinalizer")]
public sealed class RoslynFinalizerCollection { }
