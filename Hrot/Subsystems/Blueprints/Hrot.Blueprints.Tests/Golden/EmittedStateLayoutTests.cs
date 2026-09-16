using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Golden;

/// <summary>
/// ⭐⭐⭐ <b><c>W2</c> — the runtime layout gate.</b> Asserts, against the <b>real loaded struct</b>, that
/// every emitted <see cref="BlueprintFieldDescriptor"/> points at the bytes its field actually occupies.
///
/// <para>
/// ⛔⛔ <b>Why the golden corpus cannot do this, however green it is.</b> Tier 1 records the
/// <b>computed</b> offset (<c>GoldenCorpus.AppendFields</c> prints <c>IrField.Offset</c>) and the
/// descriptors are emitted from that same number — ⇒ <b>both sides of the comparison come from one
/// source.</b> 🔴 Tier 1 stays byte-identical while the real field moves. ⚠ <b>Tier 1 green is not
/// evidence here</b>; only a loaded type is.
/// </para>
///
/// <para>
/// 🔴🔴 <b>And the corpus cannot witness it either.</b> Measured across the 42 shipped assets: every
/// declared state type has a CLR alignment that <i>happens</i> to match
/// <c>FieldLayout.TypeAlignment</c>'s <c>SizeBytes switch { 1 =&gt; 1, 2 =&gt; 2, &lt;= 4 =&gt; 4, _ =&gt; 8 }</c>.
/// ⇒ every corpus asset passes this gate today <b>and would pass it if the arithmetic were wrong</b>.
/// ⭐ <c>LayoutAlignmentWitness.bp.json</c> (the 43rd asset, <c>PA-14</c>) is the constructed witness:
/// <c>Vector3</c>/<c>Quaternion</c>/<c>FixedString32</c> are in the editor's 18-member offerable set and
/// every one of them is packed by the CLR at an alignment the switch does not predict.
/// </para>
///
/// <para>
/// ⚠⚠ <b>Two oracles, deliberately separated</b> — see the two tests below. The descriptor's
/// <b>meaning</b> is "the byte the generated writer stores this field at", which is the <b>managed</b>
/// layout; <see cref="Marshal.OffsetOf(Type, string)"/> reports the <b>marshalled</b> layout. They
/// coincide for a blittable sequential struct and diverge for <c>bool</c> — and the compiler itself
/// relies on <c>Marshal.OffsetOf</c> in <c>CSharpEmitter</c>'s <c>layoutFromRuntime</c> arm, so the two
/// models disagreeing is a defect in its own right rather than a test detail.
/// </para>
/// </summary>
public sealed class EmittedStateLayoutTests
{
    private static BlueprintTestFixtureOptions NoAlcCheck { get; } =
        new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false };

    // ────────────────────────────────────────────────────────────────────────
    // ⭐⭐⭐ the gate
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The load-bearing half: the descriptor names the byte the generated code writes to.</b>
    /// The generated tick projects the blackboard with <c>Unsafe.As</c> and assigns
    /// <c>s.Field = …</c> — so where a field <i>is</i> is its <b>managed</b> offset, and the debug
    /// reader slices raw blackboard bytes at <c>descriptor.OffsetBytes</c>. A mismatch is not a
    /// cosmetic disagreement: it reads <b>plausible bytes from the wrong place</b>.
    /// </summary>
    [Fact]
    public void EveryEmittedDescriptor_NamesTheByteItsFieldActuallyOccupies()
    {
        var failures = new List<string>();

        foreach (var (name, def) in LoadWholeCorpus())
        {
            if (def.StateFields.Count == 0) continue;

            Assert.True(def.StateClrType is not null,
                $"'{name}' emitted {def.StateFields.Count} state descriptors but no StateClrType — "
                + "the descriptors name a struct nothing can resolve.");

            foreach (var d in def.StateFields.Values)
            {
                int actual = ManagedOffsetOf(def.StateClrType!, d.Name);
                if (actual != d.OffsetBytes)
                    failures.Add(
                        $"{name}.{d.Name} ({d.ClrType?.Name}): descriptor says @{d.OffsetBytes}, "
                        + $"the field is at @{actual} (delta {actual - d.OffsetBytes}).");

                int actualSize = ManagedSizeOf(d.ClrType!);
                if (actualSize != d.SizeBytes)
                    failures.Add(
                        $"{name}.{d.Name} ({d.ClrType?.Name}): descriptor says size={d.SizeBytes}, "
                        + $"the field is {actualSize} bytes.");

                if (d.OffsetBytes + d.SizeBytes > def.StateSize)
                    failures.Add(
                        $"{name}.{d.Name}: descriptor spans [{d.OffsetBytes},"
                        + $"{d.OffsetBytes + d.SizeBytes}) but StateSize is only {def.StateSize}.");
            }

            // ⭐ `StateSize` sizes the partition slot, so it must bracket the layout rather than merely
            //    contain it. ⚠ This is what stands in for a remembered per-asset number: an explicit
            //    layout that quietly grew the struct — the one way `W4` could have moved a shipped
            //    blackboard slot — shows up here as a size past the last field's aligned end.
            int end = def.StateFields.Values.Max(d => d.OffsetBytes + d.SizeBytes);
            if (def.StateSize > ((end + 7) & ~7))
                failures.Add(
                    $"{name}: StateSize is {def.StateSize} but the last field ends at {end} — "
                    + "the struct carries more padding than the computed layout accounts for.");
        }

        Assert.True(failures.Count == 0, Report("descriptors do not match the loaded struct", failures));
    }

    /// <summary>
    /// ⭐ <b>The marshalled view must agree with the managed one</b>, because the compiler emits
    /// <c>Marshal.OffsetOf&lt;TState&gt;("…")</c> as the descriptor offset whenever
    /// <c>LayoutFromRuntime(asset)</c> is true. ⛔ If the two models disagree, the SAME asset gets two
    /// different answers depending on a flag that has nothing to do with the field — and today no
    /// shipped asset takes the runtime arm, so the disagreement would be dormant rather than absent.
    /// </summary>
    [Fact]
    public void TheMarshalledLayoutAgreesWithTheManagedOne()
    {
        var failures = new List<string>();

        foreach (var (name, def) in LoadWholeCorpus())
        {
            if (def.StateFields.Count == 0 || def.StateClrType is null) continue;

            foreach (var d in def.StateFields.Values)
            {
                int managed    = ManagedOffsetOf(def.StateClrType, d.Name);
                int marshalled = Marshal.OffsetOf(def.StateClrType, d.Name).ToInt32();
                if (managed != marshalled)
                    failures.Add(
                        $"{name}.{d.Name} ({d.ClrType?.Name}): the field is at @{managed}, but "
                        + $"Marshal.OffsetOf reports @{marshalled} — the compiler's LayoutFromRuntime "
                        + "arm would bake the second number.");
            }
        }

        Assert.True(failures.Count == 0, Report("marshalled and managed layouts disagree", failures));
    }

    /// <summary>
    /// ⭐ <b>The <c>WorkingState</c> half, with the same divergent types the shipped witness carries.</b>
    /// ⚠ The corpus's 27 AiPrimitive assets exercise the <b>agreement</b> direction only — none of them
    /// declares a type whose alignment the compiler mispredicts — so the AiPrimitive struct needs its own
    /// constructed witness. ⛔ It is built in memory rather than shipped: the corpus gains exactly one
    /// asset (42 → 43) and that number is a declared gate.
    /// </summary>
    [Fact]
    public void AnAiPrimitivesWorkingState_IsHeldToTheSameRule()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("LayoutAlignmentWitnessWs")
            .WithHostings(AiPrimitiveHosting.HsmAction)
            .WithGraph("Main", g => g.Entry().Return())
            .WithWorkingStateField("Gate",   typeof(byte))
            .WithWorkingStateField("Offset", typeof(System.Numerics.Vector3))
            .WithWorkingStateField("Facing", typeof(System.Numerics.Quaternion))
            .WithWorkingStateField("Tail",   typeof(int))
            .Build();

        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        fixture.CompileAndLoad(asset, GoldenCorpus.Options());

        Assert.True(fixture.Registry.TryGetByName(asset.Name, out var def) && def is not null,
            $"'{asset.Name}' did not register.");

        var failures = new List<string>();
        foreach (var d in def!.StateFields.Values)
        {
            int actual = ManagedOffsetOf(def.StateClrType!, d.Name);
            if (actual != d.OffsetBytes)
                failures.Add($"WorkingState.{d.Name} ({d.ClrType?.Name}): descriptor says "
                    + $"@{d.OffsetBytes}, the field is at @{actual}.");
        }

        Assert.True(failures.Count == 0, Report("AiPrimitive working-state descriptors are wrong", failures));
    }

    /// <summary>
    /// 🔴🔴 <b>The gate on the gate: an asset whose sizes are guesses must NOT get an explicit layout.</b>
    ///
    /// <para>
    /// ⛔ Under <c>Sequential</c> an under-estimated field size merely pushes its neighbours down, and the
    /// descriptors are recovered at runtime by <c>Marshal.OffsetOf</c>. Under <c>Explicit</c> the same
    /// mistake makes the oversized field <b>overlap the next one</b> — two variables aliasing the same
    /// bytes, with no diagnostic. ⭐ So <c>W4</c> is only ever applied where <c>W2</c>'s premise holds,
    /// and this is the test that says so out loud.
    /// </para>
    /// </summary>
    [Fact]
    public void AnAssetWithAGuessedFieldSize_KeepsTheSequentialLayout()
    {
        // A dotted FQN the registry does not know: Stage 4's AN2 "trust the dot" fallback accepts it and
        // marks it SizeReliable = false, which is exactly the condition explicit layout must decline.
        var asset = BlueprintAssetBuilder
            .Instance("SequentialFallbackWitness")
            .WithGraph("Tick", g => g.Entry().Return())
            .WithVariable("Known", typeof(int))
            .Build();
        asset.Variables.Add(new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "Guessed",
            Type = new BlueprintTypeRef { TypeId = "Some.Unknown.ProjectStruct" },
        });

        var result = new Hrot.Blueprints.Core.Compiler.BlueprintCompiler().Compile(asset, GoldenCorpus.Options());

        Assert.True(result.Succeeded,
            "the AN2 fallback should ACCEPT an unknown dotted FQN: "
            + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Contains("LayoutKind.Sequential", result.GeneratedSource);
        Assert.DoesNotContain("FieldOffset", result.GeneratedSource);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>S2</c> — an UNREGISTERED project struct gets its real size, and the field after it
    /// is not laid on top of it.</b>
    ///
    /// <para>
    /// 🔴🔴 <b>What this was before.</b> <c>StaticTypeRegistry</c>'s AN2 arm answers any
    /// <c>global::…</c> id with a guessed <b>4</b> bytes — the right answer for the <c>Int32</c>-backed
    /// enum it was written for, and wrong for every struct. ⛔ It also left <c>SizeReliable</c> at its
    /// <c>true</c> default, so since <c>W4</c> the emitter baked <c>[FieldOffset]</c> from the guess:
    /// <c>Hrot.AI.Behaviors.StructDemoData</c> is <b>12</b> bytes, so <c>Tail</c> landed <b>8 bytes
    /// inside it</b> — two variables aliasing, silently.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>The overlap assertion is the load-bearing one.</b> The descriptor-vs-field checks pass
    /// either way: with a wrong size the emitted struct and the descriptors agree with each other
    /// perfectly — they are two prints of the same wrong number (this file's opening warning, one
    /// layer down). ⭐ Only <i>"does the next field start after this one ends"</i> can see it.
    /// </para>
    ///
    /// <para>
    /// ⭐ The oracle is supplied by hand here rather than by Roslyn: the injected shape is
    /// <c>Func&lt;string,int?&gt;</c> precisely so the seam is testable without an analyzer host. The real
    /// delegate (<c>StructSizeResolver.MakeFieldSizeDelegate</c>) is exercised by the
    /// <c>Hrot.AI.Behaviors</c> build itself.
    /// </para>
    /// </summary>
    [Fact]
    public void AnUnregisteredProjectStruct_IsSizedByTheOracle_AndTheNextFieldClearsIt()
    {
        const string StructFqn = "Hrot.AI.Behaviors.StructDemoData";   // 3 × int, unregistered
        const int    RealSize  = 12;

        var asset = BlueprintAssetBuilder
            .Instance("OracleSizedStructWitness")
            .WithGraph("Tick", g => g.Entry().Return())
            .WithVariable("Head", typeof(int))
            .Build();
        asset.Variables.Add(new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "Payload",
            Type = new BlueprintTypeRef { TypeId = "global::" + StructFqn },
        });
        asset.Variables.Add(new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "Tail",
            Type = new BlueprintTypeRef { TypeId = "int" },
        });

        var options = GoldenCorpus.Options() with
        {
            StructSizeOracle = fqn =>
                fqn == StructFqn || fqn == "global::" + StructFqn ? RealSize : (int?)null,
        };

        GoldenCorpus.EnsureBehaviorAssemblyLoaded();
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        fixture.CompileAndLoad(asset, options);

        Assert.True(fixture.Registry.TryGetByName(asset.Name, out var def) && def is not null,
            $"'{asset.Name}' did not register.");

        var payload = def!.StateFields.Values.Single(d => d.Name == "Payload");
        var tail    = def.StateFields.Values.Single(d => d.Name == "Tail");

        Assert.Equal(RealSize, payload.SizeBytes);
        Assert.True(tail.OffsetBytes >= payload.OffsetBytes + RealSize,
            $"'Tail' starts at @{tail.OffsetBytes} but 'Payload' occupies "
            + $"[{payload.OffsetBytes},{payload.OffsetBytes + RealSize}) — the two variables alias.");

        // …and the loaded struct agrees, so the size is real rather than merely consistent.
        Assert.Equal(payload.OffsetBytes, ManagedOffsetOf(def.StateClrType!, "Payload"));
        Assert.Equal(tail.OffsetBytes,    ManagedOffsetOf(def.StateClrType!, "Tail"));
    }

    /// <summary>
    /// ⭐ <b><c>S2</c>, the other half: NO oracle ⇒ no opinion ⇒ no baked offsets.</b> ⛔ The one
    /// outcome that must never return is a guessed size wearing a reliable flag — so a compile
    /// without an oracle must fall back to <c>Sequential</c> for the <b><c>global::</c>-prefixed</b>
    /// spelling too, and not only for the dotted one
    /// (<see cref="AnAssetWithAGuessedFieldSize_KeepsTheSequentialLayout"/>). ⚠ That prefixed spelling
    /// is the one the EDITOR persists, so it was the arm that mattered and the arm that lied.
    /// </summary>
    [Fact]
    public void WithoutAnOracle_AGlobalPrefixedStructKeepsTheSequentialLayout()
    {
        var asset = BlueprintAssetBuilder
            .Instance("GlobalPrefixedFallbackWitness")
            .WithGraph("Tick", g => g.Entry().Return())
            .WithVariable("Known", typeof(int))
            .Build();
        asset.Variables.Add(new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "Guessed",
            Type = new BlueprintTypeRef { TypeId = "global::Hrot.AI.Behaviors.StructDemoData" },
        });

        var result = new Hrot.Blueprints.Core.Compiler.BlueprintCompiler()
            .Compile(asset, GoldenCorpus.Options());

        Assert.True(result.Succeeded,
            string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Contains("LayoutKind.Sequential", result.GeneratedSource);
        Assert.DoesNotContain("FieldOffset", result.GeneratedSource);
    }

    // ────────────────────────────────────────────────────────────────────────
    // the corpus, compiled and loaded ONCE
    // ────────────────────────────────────────────────────────────────────────

    private static readonly object CorpusLock = new();
    private static IReadOnlyList<(string Name, BlueprintDefinition Def)>? _corpus;

    /// <summary>
    /// ⭐ <b>One merged Roslyn compilation for the whole corpus, not 43.</b> ⚠ Not an optimisation:
    /// <c>SmokeGuard</c> and <c>SmokePatrol</c> call each other, and a single-asset compile emits a call
    /// to a class that is not in the compilation — the sibling <i>signatures</i> satisfy the blueprint
    /// compiler but not the C# one. ⛔ The fixture is deliberately never disposed: the descriptors point
    /// into types owned by its collectible ALC, and unloading it invalidates every one of them.
    /// </summary>
    private static IReadOnlyList<(string, BlueprintDefinition)> LoadWholeCorpus()
    {
        lock (CorpusLock)
        {
            if (_corpus is not null) return _corpus;

            GoldenCorpus.EnsureBehaviorAssemblyLoaded();
            var assets = GoldenCorpus.EnumerateFiles()
                .Select(f => GoldenCorpus.Load(StripSuffix(Path.GetFileName(f))))
                .ToList();

            var fixture = new BlueprintTestFixture(NoAlcCheck);
            fixture.CompileAndLoadMany(assets, GoldenCorpus.Options());

            var byId = assets.ToDictionary(a => a.Name, StringComparer.Ordinal);
            var loaded = new List<(string, BlueprintDefinition)>();
            foreach (var (_, def) in fixture.Registry.GetAll())
                loaded.Add((def.Name, def));

            Assert.True(loaded.Count >= byId.Count - 2,
                $"only {loaded.Count} of {byId.Count} corpus assets registered — the sweep would be "
                + "measuring a fraction of the corpus and reporting green.");

            return _corpus = loaded;
        }
    }

    private static string StripSuffix(string fileName)
        => fileName.EndsWith(".bp.json", StringComparison.Ordinal)
            ? fileName[..^".bp.json".Length]
            : Path.GetFileNameWithoutExtension(fileName);

    // ────────────────────────────────────────────────────────────────────────
    // the oracles
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The true managed offset of a field</b>, which reflection does not expose and
    /// <see cref="Marshal.OffsetOf(Type, string)"/> only approximates.
    ///
    /// <para>
    /// Emits <c>ldflda</c> against a boxed instance and subtracts the two interior pointers. ⚠ The
    /// <see cref="DynamicMethod"/> is anchored to the <b>generated</b> assembly's module, not this test
    /// assembly's: the struct lives in a collectible ALC, and a method owned by a non-collectible module
    /// may not reference it.
    /// </para>
    /// </summary>
    internal static int ManagedOffsetOf(Type structType, string fieldName)
    {
        var field = structType.GetField(fieldName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(structType.FullName, fieldName);

        var dm = new DynamicMethod(
            $"__offsetof_{structType.Name}_{fieldName}",
            typeof(int), new[] { typeof(object) },
            structType.Module, skipVisibility: true);

        var il = dm.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox, structType);   // T& — the field's address is taken from this box…
        il.Emit(OpCodes.Ldflda, field);
        il.Emit(OpCodes.Conv_U);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox, structType);   // …and the base from the SAME box, so the delta is exact
        il.Emit(OpCodes.Conv_U);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ret);

        var probe = (Func<object, int>)dm.CreateDelegate(typeof(Func<object, int>));
        return probe(Activator.CreateInstance(structType)!);
    }

    /// <summary><c>Unsafe.SizeOf&lt;T&gt;()</c> — the managed size, matching what the emitted
    /// <c>StateSize</c> property reports and what <c>FieldLayout</c> means by <c>SizeBytes</c>.</summary>
    private static int ManagedSizeOf(Type t)
        => (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!
                              .MakeGenericMethod(t)
                              .Invoke(null, null)!;

    private static string Report(string headline, IReadOnlyList<string> failures)
    {
        var sb = new StringBuilder();
        sb.Append(failures.Count).Append(' ').Append(headline).Append(":\n");
        foreach (var f in failures) sb.Append("  • ").Append(f).Append('\n');
        return sb.ToString();
    }
}
