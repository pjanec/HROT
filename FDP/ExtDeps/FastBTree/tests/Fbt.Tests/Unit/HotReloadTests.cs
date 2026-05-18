using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using Fbt;
using Fbt.Compiler;
using Fbt.HotReload;
using Fbt.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Fbt.Tests.TestFixtures;

namespace Fbt.Tests.Unit
{
    public class HotReloadTests
    {
        // ---- Shared helpers ----

        private static BehaviorTreeBlob MakeBlob(int structureHash, int paramHash)
            => new BehaviorTreeBlob { StructureHash = structureHash, ParamHash = paramHash };

        private static NodeStatus AlwaysSuccess(
            ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
            => NodeStatus.Success;

        private struct SimpleState
        {
            public int Value;
        }

        // ---- FBT-020: BTreeHotReloadManager tests ----

        [Fact]
        public void TryReload_NewTree_ReturnsNewTree()
        {
            var manager = new BTreeHotReloadManager();
            var blob = MakeBlob(structureHash: 1, paramHash: 1);

            var result = manager.TryReload("tree", blob, Span<SimpleState>.Empty, null);

            Assert.Equal(ReloadResult.NewTree, result);
        }

        [Fact]
        public void TryReload_NoChange_WhenHashesIdentical()
        {
            var manager = new BTreeHotReloadManager();
            var blob1 = MakeBlob(structureHash: 10, paramHash: 20);
            var blob2 = MakeBlob(structureHash: 10, paramHash: 20);

            manager.TryReload("tree", blob1, Span<SimpleState>.Empty, null);
            var result = manager.TryReload("tree", blob2, Span<SimpleState>.Empty, null);

            Assert.Equal(ReloadResult.NoChange, result);
        }

        [Fact]
        public void TryReload_SoftReload_WhenOnlyParamHashDiffers()
        {
            var manager = new BTreeHotReloadManager();
            var blob1 = MakeBlob(structureHash: 10, paramHash: 20);
            var blob2 = MakeBlob(structureHash: 10, paramHash: 99);

            manager.TryReload("tree", blob1, Span<SimpleState>.Empty, null);
            var result = manager.TryReload("tree", blob2, Span<SimpleState>.Empty, null);

            Assert.Equal(ReloadResult.SoftReload, result);
        }

        [Fact]
        public void TryReload_HardReset_WhenStructureHashDiffers()
        {
            var manager = new BTreeHotReloadManager();
            var blob1 = MakeBlob(structureHash: 10, paramHash: 20);
            var blob2 = MakeBlob(structureHash: 99, paramHash: 20);

            manager.TryReload("tree", blob1, Span<SimpleState>.Empty, null);
            var result = manager.TryReload("tree", blob2, Span<SimpleState>.Empty, null);

            Assert.Equal(ReloadResult.HardReset, result);
        }

        [Fact]
        public void TryReload_HardReset_CallsHardResetAction_OnAllInstances()
        {
            var manager = new BTreeHotReloadManager();
            var blob1 = MakeBlob(structureHash: 1, paramHash: 1);
            var blob2 = MakeBlob(structureHash: 2, paramHash: 1);

            manager.TryReload("tree", blob1, Span<SimpleState>.Empty, null);

            var instances = new SimpleState[]
            {
                new SimpleState { Value = 42 },
                new SimpleState { Value = 43 },
                new SimpleState { Value = 44 },
            };

            SpanResetAction<SimpleState> resetAll = (span, i) => span[i] = default;
            var result = manager.TryReload("tree", blob2, instances.AsSpan(), resetAll);

            Assert.Equal(ReloadResult.HardReset, result);
            Assert.Equal(0, instances[0].Value);
            Assert.Equal(0, instances[1].Value);
            Assert.Equal(0, instances[2].Value);
        }

        [Fact]
        public void TryReload_NullBlob_ReturnsNoChange()
        {
            var manager = new BTreeHotReloadManager();

            var result = manager.TryReload<SimpleState>("tree", null, Span<SimpleState>.Empty, null);

            Assert.Equal(ReloadResult.NoChange, result);
        }

        [Fact]
        public void TryReload_SoftReload_DoesNotCallHardResetAction()
        {
            var manager = new BTreeHotReloadManager();
            var blob1 = MakeBlob(structureHash: 10, paramHash: 1);
            var blob2 = MakeBlob(structureHash: 10, paramHash: 2);

            manager.TryReload("tree", blob1, Span<SimpleState>.Empty, null);

            bool actionCalled = false;
            SpanResetAction<SimpleState> flagAction = (span, i) => { actionCalled = true; };
            var result = manager.TryReload("tree", blob2, Span<SimpleState>.Empty, flagAction);

            Assert.Equal(ReloadResult.SoftReload, result);
            Assert.False(actionCalled);
        }

        [Fact]
        public void TryReload_EmptySpan_HardReset_DoesNotThrow()
        {
            var manager = new BTreeHotReloadManager();
            var blob1 = MakeBlob(structureHash: 1, paramHash: 1);
            var blob2 = MakeBlob(structureHash: 2, paramHash: 1);

            manager.TryReload("tree", blob1, Span<SimpleState>.Empty, null);

            SpanResetAction<SimpleState> resetAction = (span, i) => span[i] = default;
            var result = manager.TryReload("tree", blob2, Span<SimpleState>.Empty, resetAction);

            Assert.Equal(ReloadResult.HardReset, result);
        }

        // ---- FBT-021: Interpreter hot reload safety check tests ----

        [Fact]
        public void Interpreter_HotReloadCheck_ResetsState_WhenRunningIndexOutOfBounds()
        {
            // Single-action blob: Nodes.Length == 1
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            var blob = builder
                .Action(AlwaysSuccess)
                .Compile("SingleAction");
            var registry = builder.GetRegistry();
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);

            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            // blob.Nodes.Length == 1; index 5 is out of bounds
            state.RunningNodeIndex = 5;
            uint versionBefore = state.TreeVersion;

            // Should not throw
            interpreter.Tick(ref bb, ref state, ref ctx);

            // Bounds check fires: TreeVersion incremented, RunningNodeIndex reset
            Assert.True(state.TreeVersion > versionBefore, "TreeVersion should have been incremented by bounds check");
            Assert.NotEqual((ushort)5, state.RunningNodeIndex);
        }

        [Fact]
        public void Interpreter_HotReloadCheck_DoesNotResetState_WhenRunningIndexValid()
        {
            // Sequence + Action = 2 nodes; index 1 is within bounds
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            var blob = builder
                .Sequence(s => s
                    .Action(AlwaysSuccess))
                .Compile("SeqOneAction");
            var registry = builder.GetRegistry();
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);

            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            // blob.Nodes.Length == 2; index 1 is in bounds (1 < 2)
            state.RunningNodeIndex = 1;
            uint versionBefore = state.TreeVersion; // 0

            // Should not throw; bounds check must NOT fire
            interpreter.Tick(ref bb, ref state, ref ctx);

            // TreeVersion not incremented by bounds check
            Assert.Equal(versionBefore, state.TreeVersion);
        }

        // ---- FBT-022 (remaining): FbtAssemblyHotReloader ALC tests ----

        // Helper: compile a minimal DLL from C# source to a temp file.
        // Returns the output path, or throws if compilation fails.
        private static string CompileTestDll(string outputPath, string sourceCode)
        {
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var references = new MetadataReference[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(
                    typeof(FbtRegistrarAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(
                    Path.Combine(runtimeDir, "System.Runtime.dll")),
            };

            var compilation = CSharpCompilation.Create(
                assemblyName: Path.GetFileNameWithoutExtension(outputPath),
                syntaxTrees: new[] { CSharpSyntaxTree.ParseText(sourceCode) },
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var result = compilation.Emit(outputPath);
            if (!result.Success)
                throw new InvalidOperationException(
                    "Test DLL compilation failed: " +
                    string.Join(", ", result.Diagnostics));
            return outputPath;
        }

        private static readonly string SourceWithRegistrar = """
            using Fbt;
            using System.Reflection;
            [assembly: AssemblyVersion("1.0.0.0")]
            namespace TestHotReloadAssembly
            {
                [FbtRegistrar]
                public static class TestRegistrar { }
            }
            """;

        private static readonly string SourceWithoutRegistrar = """
            using System.Reflection;
            [assembly: AssemblyVersion("1.0.0.0")]
            namespace TestHotReloadAssembly
            {
                public static class NoRegistrar { }
            }
            """;

        // Helper: poll DrainPendingCallbacks until predicate is true or timeout.
        private static bool PollCallbacks(
            FbtAssemblyHotReloader reloader,
            Func<bool> predicate,
            int timeoutMs = 2000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                reloader.DrainPendingCallbacks();
                if (predicate()) return true;
                Thread.Sleep(20);
            }
            return false;
        }

        [Fact]
        public void AlcHotReloader_Dispose_DoesNotThrow()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                var reloader = new FbtAssemblyHotReloader(
                    tempDir,
                    (type, asm) => Array.Empty<(string, BehaviorTreeBlob)>());
                reloader.Dispose();   // Must not throw
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void AlcHotReloader_OnReloadFailed_FiredForDllWithoutRegistrar()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            var dllSource = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".dll");
            try
            {
                CompileTestDll(dllSource, SourceWithoutRegistrar);

                bool failedFired = false;
                var reloader = new FbtAssemblyHotReloader(
                    tempDir,
                    (type, asm) => Array.Empty<(string, BehaviorTreeBlob)>());
                reloader.OnReloadFailed += (path, ex) => { failedFired = true; };

                // Copy the DLL into the watched directory to trigger the watcher
                File.Copy(dllSource, Path.Combine(tempDir, "test.dll"));

                bool fired = PollCallbacks(reloader, () => failedFired);
                reloader.Dispose();

                Assert.True(fired, "OnReloadFailed should have been fired for DLL with no [FbtRegistrar]");
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
                if (File.Exists(dllSource)) File.Delete(dllSource);
            }
        }

        [Fact]
        public void AlcHotReloader_OnReloadCompleted_FiredForValidDll()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            var dllSource = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".dll");
            try
            {
                CompileTestDll(dllSource, SourceWithRegistrar);

                bool completedFired = false;
                string? completedName = null;
                var reloader = new FbtAssemblyHotReloader(
                    tempDir,
                    (type, asm) => new[] { ("dummyTree", new BehaviorTreeBlob()) });
                reloader.OnReloadCompleted += name =>
                {
                    completedFired = true;
                    completedName = name;
                };

                File.Copy(dllSource, Path.Combine(tempDir, "valid.dll"));

                bool fired = PollCallbacks(reloader, () => completedFired);
                reloader.Dispose();

                Assert.True(fired, "OnReloadCompleted should have been fired for DLL with [FbtRegistrar]");
                Assert.Equal("dummyTree", completedName);
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
                if (File.Exists(dllSource)) File.Delete(dllSource);
            }
        }

        [Fact]
        public void AlcHotReloader_OldAlc_IsUnloaded_AfterReload()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            var dll1 = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".dll");
            var dll2 = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".dll");
            try
            {
                CompileTestDll(dll1, SourceWithRegistrar);
                CompileTestDll(dll2, SourceWithRegistrar);

                int reloadCount = 0;
                var reloader = new FbtAssemblyHotReloader(
                    tempDir,
                    (type, asm) => new[] { ("tree", new BehaviorTreeBlob()) });
                reloader.OnReloadCompleted += _ => { Interlocked.Increment(ref reloadCount); };

                // First reload
                File.Copy(dll1, Path.Combine(tempDir, "reload.dll"));
                PollCallbacks(reloader, () => reloadCount >= 1);

                // Second reload: overwrite to trigger Changed event
                File.Copy(dll2, Path.Combine(tempDir, "reload.dll"), overwrite: true);
                PollCallbacks(reloader, () => reloadCount >= 2);

                reloader.Dispose();

                // PreviousAlcRef was set when second reload unloaded the first ALC
                Assert.NotNull(reloader.PreviousAlcRef);

                // Force GC to collect the old ALC
                bool unloaded = false;
                for (int i = 0; i < 10; i++)
                {
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                    if (!reloader.PreviousAlcRef!.TryGetTarget(out _))
                    {
                        unloaded = true;
                        break;
                    }
                    Thread.Sleep(50);
                }

                Assert.True(unloaded, "Old ALC should have been GC'd after Unload() and forced collection");
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
                if (File.Exists(dll1)) File.Delete(dll1);
                if (File.Exists(dll2)) File.Delete(dll2);
            }
        }
    }
}
