using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hrot.Editor;
using Xunit;

namespace Hrot.Editor.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-1321</c> (Brain View §12g BV-68) — the C# behaviour rebuild: build, COPY the built assembly into the watched directory
    /// (🔴 measured: nothing did — the button rebuilt and never reloaded), wait for the coordinator's swap, say what changed. A fake
    /// <c>dotnet</c> and a fake swap signal: the real build and swap are the system test <c>The_csharp_behaviours_rebuild_and_hot_swap</c>.
    /// </summary>
    public sealed class AiBehaviorsRebuildTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ce1321-" + Guid.NewGuid().ToString("N"));
        private readonly string _watch, _built, _project;

        public AiBehaviorsRebuildTests()
        {
            _watch = Directory.CreateDirectory(Path.Combine(_root, "editor")).FullName;
            var bin = Directory.CreateDirectory(Path.Combine(_root, "ai", "bin")).FullName;
            _built = Path.Combine(bin, "Hrot.AI.Behaviors.dll");
            _project = Path.Combine(_root, "ai", "Hrot.AI.Behaviors.csproj");
            File.WriteAllText(_project, "<Project />");
            File.WriteAllText(_built, "NEW");
            File.WriteAllText(Path.ChangeExtension(_built, ".pdb"), "PDB");
            File.WriteAllText(Path.Combine(_watch, "Hrot.AI.Behaviors.dll"), "OLD");
        }

        public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch (IOException) { } }

        private sealed class FakeDotnet : IAiBuildRunner
        {
            public int BuildExit;
            public string BuiltPath = "";
            public readonly List<string> Calls = new();
            public Task<(int, IReadOnlyList<string>)> RunAsync(string arguments, CancellationToken ct)
            {
                Calls.Add(arguments);
                if (arguments.StartsWith("build", StringComparison.Ordinal))
                    return Task.FromResult<(int, IReadOnlyList<string>)>((BuildExit, BuildExit == 0
                        ? new[] { "Build succeeded." }
                        : new[] { "Foo.cs(3,1): error CS1002: ; expected", "Foo.cs(3,1): error CS1002: ; expected", "Build FAILED." }));
                return Task.FromResult<(int, IReadOnlyList<string>)>((0, new[] { "", BuiltPath }));
            }
        }

        /// <summary>The coordinator's swap, faked: when the watched file holds the new bytes, it "swaps" (changes the names) and fires.</summary>
        private sealed class FakeSwap : IAiReloadSignal
        {
            private readonly string _target;
            public readonly List<string> Names;
            public bool Fire = true;
            public FakeSwap(string target, List<string> names) { _target = target; Names = names; }
            public event Action<string, Exception>? Failed;
            private Action? _completed;
            public event Action? Completed
            {
                add
                {
                    _completed += value;
                    if (!Fire) return;
                    _ = Task.Run(async () =>
                    {
                        for (int i = 0; i < 200 && (!File.Exists(_target) || File.ReadAllText(_target) != "NEW"); i++) await Task.Delay(10);
                        Failed?.Invoke(_target, new InvalidOperationException("registrar 'Broken' skipped"));
                        Names.Remove("Old"); Names.Add("Fresh");
                        _completed?.Invoke();
                    });
                }
                remove => _completed -= value;
            }
        }

        private AiBehaviorsRebuild Rebuild(FakeDotnet dotnet, IAiReloadSignal? signal, List<string> names)
            => new(() => _project, _watch, "Hrot.AI.Behaviors.dll", dotnet, signal, () => names.ToArray(), () => 2, "Debug");

        /// <summary>✅ Red-proof: the copy removed ⇒ the fake swap never sees the new bytes ⇒ not reloaded.</summary>
        // ⚠ CE-1392 — "no swap within 5 s" in full Hrot.Editor.Tests runs (2/2 quiet runs `2026-10-28`), passes alone 4/4. Opt-in only until fixed.
        [Fdp.Testing.FlakyFact("CE-1392: the swap wait times out in full-suite runs, passes alone")]
        [Trait("Category", "Flaky")]
        public async Task ABuild_IsCopiedIntoTheWatchedDirectory_ThenTheSwapIsAwaited_AndWhatChangedIsSaid()
        {
            var names = new List<string> { "Old", "Kept" };
            var dotnet = new FakeDotnet { BuiltPath = _built };
            var r = await Rebuild(dotnet, new FakeSwap(Path.Combine(_watch, "Hrot.AI.Behaviors.dll"), names), names).RunAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("ok", r.Build);
            Assert.True(r.Reloaded, r.Note);
            Assert.Equal("NEW", File.ReadAllText(Path.Combine(_watch, "Hrot.AI.Behaviors.dll")));
            Assert.Equal("PDB", File.ReadAllText(Path.Combine(_watch, "Hrot.AI.Behaviors.pdb")));
            Assert.Equal(new[] { "Fresh" }, r.Added);
            Assert.Equal(new[] { "Old" }, r.Removed);
            Assert.Equal((2, 2), (r.Behaviours, r.DocumentsReconciled));
            Assert.Contains(r.RegistrarFailures, f => f.Contains("Broken"));
            Assert.StartsWith($"build \"{_project}\" -c Debug", dotnet.Calls[0]);   // the editor's own configuration
        }

        [Fact]
        public async Task AFailedBuild_SaysTheErrors_AndTouchesNothing()
        {
            var names = new List<string> { "Old" };
            var r = await Rebuild(new FakeDotnet { BuildExit = 1, BuiltPath = _built }, new FakeSwap(_watch, names), names)
                .RunAsync(TimeSpan.FromSeconds(1));

            Assert.Equal(("failed", false), (r.Build, r.Reloaded));
            Assert.Single(r.Errors);                                                   // distinct
            Assert.Equal("OLD", File.ReadAllText(Path.Combine(_watch, "Hrot.AI.Behaviors.dll")));
        }

        [Fact]
        public async Task NoSwap_IsSaid_NotHung()
        {
            var names = new List<string> { "Old" };
            var swap = new FakeSwap(Path.Combine(_watch, "Hrot.AI.Behaviors.dll"), names) { Fire = false };
            var r = await Rebuild(new FakeDotnet { BuiltPath = _built }, swap, names).RunAsync(TimeSpan.FromMilliseconds(300));
            Assert.False(r.Reloaded);
            Assert.Contains("no swap", r.Note);
        }

        [Fact]
        public async Task AHostWithoutTheCoordinator_Builds_AndSaysItCannotSwap()
        {
            var names = new List<string>();
            var r = await Rebuild(new FakeDotnet { BuiltPath = _built }, null, names).RunAsync(TimeSpan.FromSeconds(1));
            Assert.False(r.Reloaded);
            Assert.Contains("no hot-reload coordinator", r.Note);
        }
    }
}
