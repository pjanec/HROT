using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Hrot.Editor;

/// <summary>What one C# behaviour rebuild + hot reload did (<c>POST /ai/reload</c>, the toolbar's <i>Reload AI</i>).</summary>
/// <param name="Build"><c>ok</c> · <c>failed</c> · <c>not-found</c> (no project) · <c>not-wired</c> (this host has no hot reload).</param>
/// <param name="Errors">The compiler's error lines (distinct, at most 30).</param>
/// <param name="Reloaded">The new assembly was swapped in (the coordinator's completion fired).</param>
/// <param name="Note">Why it did not reload, or what to know.</param>
/// <param name="Added">Behaviour names the reload registered that were not there before.</param>
/// <param name="Removed">Behaviour names that are gone.</param>
/// <param name="Behaviours">Registered behaviours after the reload.</param>
/// <param name="DocumentsReconciled">Open AI documents re-decompiled from the new assembly.</param>
/// <param name="RegistrarFailures">Registrars the reload skipped (each logged in full; the rest registered).</param>
public sealed record AiReloadResult(
    string Build, int ExitCode, IReadOnlyList<string> Errors, bool Reloaded, string? Note,
    IReadOnlyList<string> Added, IReadOnlyList<string> Removed, int Behaviours, int DocumentsReconciled,
    IReadOnlyList<string> RegistrarFailures, double Seconds)
{
    public static AiReloadResult NotWired(string note) => new("not-wired", 0, Array.Empty<string>(), false, note,
        Array.Empty<string>(), Array.Empty<string>(), 0, 0, Array.Empty<string>(), 0);
}

/// <summary>Runs <c>dotnet &lt;args&gt;</c> and returns its exit code and output lines (stdout + stderr).</summary>
public interface IAiBuildRunner
{
    Task<(int ExitCode, IReadOnlyList<string> Lines)> RunAsync(string arguments, CancellationToken ct);
}

/// <summary>The real runner: <c>dotnet</c> on PATH, both pipes read asynchronously (no pipe-buffer deadlock).</summary>
public sealed class DotnetBuildRunner : IAiBuildRunner
{
    private readonly Action<string>? _line;
    public DotnetBuildRunner(Action<string>? line = null) => _line = line;

    public async Task<(int ExitCode, IReadOnlyList<string> Lines)> RunAsync(string arguments, CancellationToken ct)
    {
        var lines = new List<string>();
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet", Arguments = arguments, CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var process = new Process { StartInfo = psi };
        void Add(string? s) { if (s is null) return; lock (lines) lines.Add(s); _line?.Invoke(s); }
        process.OutputDataReceived += (_, e) => Add(e.Data);
        process.ErrorDataReceived += (_, e) => Add(e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        lock (lines) return (process.ExitCode, lines.ToList());
    }
}

/// <summary>The two coordinator events a rebuild waits on (an adapter over <see cref="AiHotReloadCoordinator"/>; a fake in tests).</summary>
public interface IAiReloadSignal
{
    event Action? Completed;
    event Action<string, Exception>? Failed;
}

/// <summary>
/// ⭐⭐ <c>CE-1321</c> (Brain View §12g BV-68) — THE C# BEHAVIOUR REBUILD AND HOT RELOAD, one path for the toolbar button and
/// <c>POST /ai/reload</c>: build <c>Hrot.AI.Behaviors</c> in the editor's own configuration, COPY the built assembly (+ pdb) into the
/// directory the <see cref="AiHotReloadCoordinator"/> watches, wait for its swap (the catalog refresh and the open documents'
/// reconcile ride the same completion), and say what changed.
/// <para>🔴 Measured <c>2026-10-25</c>: the toolbar's build wrote <c>Hrot.AI.Behaviors/bin/…</c> and NOTHING copied it to the editor's
/// base directory, the only place the watcher looks — so the button rebuilt and never reloaded. The copy is the fix.</para>
/// <para>⚠ Running instances keep their state: the registry is merged in place, a running behaviour's slot memory is untouched, HSM
/// instances are re-bound by slot (<c>ReloadHsmInstancesInSlots</c>). A changed params / state LAYOUT is the author's to restart.</para>
/// </summary>
public sealed class AiBehaviorsRebuild
{
    private readonly Func<string?> _projectPath;
    private readonly string _watchDirectory;
    private readonly string _dllName;
    private readonly IAiBuildRunner _runner;
    private readonly IAiReloadSignal? _signal;
    private readonly Func<IReadOnlyCollection<string>> _behaviourNames;
    private readonly Func<int> _openDocuments;
    private readonly string _configuration;
    private readonly Action<string>? _log;
    private int _running;

    public AiBehaviorsRebuild(Func<string?> projectPath, string watchDirectory, string dllName, IAiBuildRunner runner,
        IAiReloadSignal? signal, Func<IReadOnlyCollection<string>> behaviourNames, Func<int> openDocuments,
        string? configuration = null, Action<string>? log = null)
    {
        _projectPath = projectPath;
        _watchDirectory = watchDirectory;
        _dllName = dllName;
        _runner = runner;
        _signal = signal;
        _behaviourNames = behaviourNames;
        _openDocuments = openDocuments;
        _configuration = configuration ?? EditorConfiguration();
        _log = log;
    }

    /// <summary>The configuration this editor was built in (<c>Debug</c> / <c>Release</c>), so the rebuilt assembly matches it.</summary>
    public static string EditorConfiguration()
        => typeof(AiBehaviorsRebuild).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration is { Length: > 0 } c ? c : "Debug";

    public async Task<AiReloadResult> RunAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return new("busy", 0, Array.Empty<string>(), false, "A rebuild is already running — wait for it.", Array.Empty<string>(),
                Array.Empty<string>(), 0, 0, Array.Empty<string>(), 0);
        try
        {
            string? project = _projectPath();
            if (project is null || !File.Exists(project))
                return Fail("not-found", $"The {_dllName} project was not found.");

            _log?.Invoke($"Starting dotnet build: {project} ({_configuration})");
            var (exit, lines) = await _runner.RunAsync($"build \"{project}\" -c {_configuration} --nologo -v q", ct).ConfigureAwait(false);
            var errors = lines.Where(l => l.Contains(": error ", StringComparison.Ordinal)).Distinct().Take(30).ToList();
            _log?.Invoke($"Build exited with code {exit}.");
            if (exit != 0)
                return new AiReloadResult("failed", exit, errors, false, "The build failed: the running assembly is unchanged.",
                    Array.Empty<string>(), Array.Empty<string>(), _behaviourNames().Count, 0, Array.Empty<string>(), sw.Elapsed.TotalSeconds);

            var (_, pathLines) = await _runner.RunAsync($"msbuild \"{project}\" -getProperty:TargetPath -p:Configuration={_configuration} -nologo", ct)
                .ConfigureAwait(false);
            string? built = pathLines.Select(l => l.Trim()).LastOrDefault(l => l.EndsWith(_dllName, StringComparison.OrdinalIgnoreCase));
            if (built is null || !File.Exists(built))
                return Done(exit, errors, false, $"Built, but the assembly's path was not found ({string.Join(" | ", pathLines)}).", null, sw);
            if (_signal is null)
                return Done(exit, errors, false, "Built, but this host has no hot-reload coordinator.", null, sw);

            var before = _behaviourNames().ToHashSet(StringComparer.Ordinal);
            var failures = new List<string>();
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnDone() => completed.TrySetResult();
            void OnFail(string path, Exception ex) { lock (failures) failures.Add($"{ex.GetType().Name}: {ex.Message}"); }
            _signal.Completed += OnDone;
            _signal.Failed += OnFail;
            try
            {
                string target = Path.Combine(_watchDirectory, _dllName);
                if (!string.Equals(Path.GetFullPath(built), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                {
                    string pdb = Path.ChangeExtension(built, ".pdb");
                    try
                    {
                        if (File.Exists(pdb)) File.Copy(pdb, Path.ChangeExtension(target, ".pdb"), overwrite: true);
                        File.Copy(built, target, overwrite: true);   // the watcher's cue
                    }
                    catch (IOException ex)
                    {
                        return Done(exit, errors, false, $"Built, but {target} could not be replaced ({ex.Message}) — is it locked by the default load context?", null, sw);
                    }
                }
                else File.SetLastWriteTimeUtc(target, DateTime.UtcNow);   // built in place: touch so the watcher fires
                _log?.Invoke($"Copied {built} → {target}; waiting for the swap.");

                var wait = timeout ?? TimeSpan.FromSeconds(60);
                if (await Task.WhenAny(completed.Task, Task.Delay(wait, ct)).ConfigureAwait(false) != completed.Task)
                    return Done(exit, errors, false, $"Built and copied, but no swap within {wait.TotalSeconds:0} s (failures: {string.Join("; ", failures)}).", failures, sw);
            }
            finally
            {
                _signal.Completed -= OnDone;
                _signal.Failed -= OnFail;
            }

            var after = _behaviourNames();
            var afterSet = after.ToHashSet(StringComparer.Ordinal);
            return new AiReloadResult("ok", exit, errors, true, null,
                after.Where(n => !before.Contains(n)).OrderBy(n => n).ToList(),
                before.Where(n => !afterSet.Contains(n)).OrderBy(n => n).ToList(),
                after.Count, _openDocuments(), failures, sw.Elapsed.TotalSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail("failed", $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }

        AiReloadResult Fail(string build, string note) => new(build, 0, Array.Empty<string>(), false, note, Array.Empty<string>(),
            Array.Empty<string>(), 0, 0, Array.Empty<string>(), sw.Elapsed.TotalSeconds);
    }

    private AiReloadResult Done(int exit, IReadOnlyList<string> errors, bool reloaded, string note, IReadOnlyList<string>? failures, Stopwatch sw)
        => new("ok", exit, errors, reloaded, note, Array.Empty<string>(), Array.Empty<string>(), _behaviourNames().Count, 0,
            (IReadOnlyList<string>?)failures ?? Array.Empty<string>(), sw.Elapsed.TotalSeconds);
}

/// <summary>The coordinator's events as an <see cref="IAiReloadSignal"/>. ⭐ It subscribes on its FIRST listener (a rebuild running), not
/// at construction, so it sits after every subscriber the composition root added — the catalog refresh and the document reconcile.</summary>
internal sealed class AiHotReloadSignal : IAiReloadSignal
{
    private readonly AiHotReloadCoordinator _coordinator;
    private bool _hooked;
    private Action? _completed;
    private Action<string, Exception>? _failed;

    public AiHotReloadSignal(AiHotReloadCoordinator coordinator) => _coordinator = coordinator;

    private void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        _coordinator.OnReloadCompleted += _ => _completed?.Invoke();
        _coordinator.OnReloadFailed += (p, e) => _failed?.Invoke(p, e);
    }

    public event Action? Completed { add { Hook(); _completed += value; } remove => _completed -= value; }
    public event Action<string, Exception>? Failed { add { Hook(); _failed += value; } remove => _failed -= value; }
}
