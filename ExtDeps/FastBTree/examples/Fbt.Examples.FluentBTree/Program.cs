using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
using Fbt;
using Fbt.Examples.FluentBTree;
using Fbt.HotReload;
using Fbt.Runtime;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

// ---- App state ----
var interpreter = AmbushTree.CreateInterpreter();
var bb = new CombatBlackboard { AmmoCount = 5, ThreatVisible = true, EngagementRange = 50f };
var state = new BehaviorTreeState();
var ctx = new CombatContext();
bool paused = false;
string reloadStatus = "No reload yet.";

// FBT-045: pending reload storage (set from ReloadHandler closure)
BehaviorTreeBlob? _pendingBlob = null;
ActionRegistry<CombatBlackboard, CombatContext>? _pendingRegistry = null;

// FBT-045: hot reload manager + assembly reloader
BTreeHotReloadManager hotReloadManager = new BTreeHotReloadManager();
FbtAssemblyHotReloader assemblyReloader = new FbtAssemblyHotReloader(
    AppContext.BaseDirectory,
    ReloadHandler);

assemblyReloader.OnReloadFailed += (path, ex) =>
{
    reloadStatus = $"Reload failed: {ex.Message.Substring(0, Math.Min(40, ex.Message.Length))}";
};

assemblyReloader.OnReloadCompleted += treeName =>
{
    if (_pendingBlob == null || _pendingRegistry == null)
        return;

    var stateArr = new BehaviorTreeState[] { state };
    var reloadResult = hotReloadManager.TryReload(
        treeName,
        _pendingBlob,
        stateArr.AsSpan(),
        (span, i) => span[i] = new BehaviorTreeState());

    interpreter = new Interpreter<CombatBlackboard, CombatContext>(
        _pendingBlob!, _pendingRegistry!);

    if (reloadResult == ReloadResult.HardReset)
        state = stateArr[0];
    else if (reloadResult == ReloadResult.NewTree)
    {
        state = new BehaviorTreeState();
        bb = new CombatBlackboard { AmmoCount = 5, ThreatVisible = true, EngagementRange = 50f };
    }

    reloadStatus = reloadResult.ToString();
};

// ---- Raylib init ----
Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
Raylib.InitWindow(1280, 720, "Ambush BTree Demo");
Raylib.SetTargetFPS(60);
rlImGui.Setup(true);

// ---- Main loop ----
while (!Raylib.WindowShouldClose())
{
    float dt = Raylib.GetFrameTime();
    ctx.DeltaTime = dt;
    ctx.Time += dt;
    ctx.FrameCount++;

    assemblyReloader.DrainPendingCallbacks();

    if (!paused)
        interpreter.Tick(ref bb, ref state, ref ctx);

    Raylib.BeginDrawing();
    Raylib.ClearBackground(Color.DarkGray);

    rlImGui.Begin();
    DrawUI();
    rlImGui.End();

    Raylib.EndDrawing();
}

rlImGui.Shutdown();
assemblyReloader.Dispose();
Raylib.CloseWindow();

// ---- Local function: find Trees csproj by walking up from BaseDirectory ----
string? FindTreesProject()
{
    string? dir = AppContext.BaseDirectory;
    for (int i = 0; i < 10 && dir != null; i++, dir = Path.GetDirectoryName(dir))
    {
        string candidate = Path.Combine(dir, "examples",
            "Fbt.Examples.FluentBTree.Trees",
            "Fbt.Examples.FluentBTree.Trees.csproj");
        if (File.Exists(candidate)) return candidate;
    }
    return null;
}

// ---- Local function: reload handler called by FbtAssemblyHotReloader ----
IEnumerable<(string, BehaviorTreeBlob)> ReloadHandler(Type registrarType, Assembly newAssembly)
{
    var newRegistry = new ActionRegistry<CombatBlackboard, CombatContext>();
    var registerAllMethod = registrarType.GetMethod("RegisterAll",
        new[] { typeof(ActionRegistry<CombatBlackboard, CombatContext>) });
    registerAllMethod?.Invoke(null, new object[] { newRegistry });

    var results = new List<(string, BehaviorTreeBlob)>();
    foreach (var type in newAssembly.GetTypes())
    {
        foreach (var method in type.GetMethods(
            BindingFlags.Public | BindingFlags.Static))
        {
            var attr = method.GetCustomAttribute(typeof(BTreeDefinitionAttribute));
            if (attr == null) continue;
            string treeName = ((BTreeDefinitionAttribute)attr).TreeName;
            if (method.Invoke(null, null) is BehaviorTreeBlob blob)
            {
                _pendingBlob = blob;
                results.Add((treeName, blob));
            }
        }
    }

    _pendingRegistry = newRegistry;
    return results;
}

// ---- Local function: draw all ImGui windows ----
void DrawUI()
{
    // --- Window 1: Blackboard ---
    ImGui.Begin("Blackboard");
    ImGui.Text($"FPS: {Raylib.GetFPS()}");
    ImGui.Checkbox("Paused", ref paused);
    ImGui.Separator();
    int ammo = bb.AmmoCount;
    if (ImGui.SliderInt("AmmoCount", ref ammo, 0, 20))
        bb.AmmoCount = ammo;
    bool threat = bb.ThreatVisible;
    if (ImGui.Checkbox("ThreatVisible", ref threat))
        bb.ThreatVisible = threat;
    float range = bb.EngagementRange;
    if (ImGui.SliderFloat("EngagementRange", ref range, 0f, 200f))
        bb.EngagementRange = range;
    ImGui.End();

    // --- Window 2: Behavior Tree ---
    ImGui.Begin("Behavior Tree");
    ImGui.Text($"RunningNode: {state.RunningNodeIndex}  TreeVersion: {state.TreeVersion}");
    ImGui.Text($"Reload: {reloadStatus}");
    if (ImGui.Button("Recompile & Reload"))
    {
        string? proj = FindTreesProject();
        if (proj != null)
        {
            reloadStatus = "Building...";
            System.Threading.Tasks.Task.Run(() =>
            {
                var psi = new System.Diagnostics.ProcessStartInfo("dotnet",
                    $"build \"{proj}\" --no-restore -c Debug")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = System.Diagnostics.Process.Start(psi)!;
                proc.WaitForExit();
                if (proc.ExitCode != 0)
                {
                    string err = proc.StandardError.ReadToEnd();
                    reloadStatus = $"Build failed: {err.Substring(0, Math.Min(60, err.Length))}";
                }
            });
        }
        else
        {
            reloadStatus = "Trees project not found.";
        }
    }
    ImGui.Separator();
    var blob = interpreter.Blob;
    if (blob.Nodes != null)
        RenderNode(blob, 0, state.RunningNodeIndex, 0);
    ImGui.End();
}

// ---- Local function: recursive tree renderer ----
void RenderNode(BehaviorTreeBlob blob, int index, int runningIndex, int depth)
{
    if (index >= blob.Nodes.Length) return;

    var node = blob.Nodes[index];
    string indent = new string(' ', depth * 2);

    string label = $"{indent}[{index}] {node.Type}";
    if ((node.Type == NodeType.Action || node.Type == NodeType.Condition)
        && blob.MethodNames != null
        && node.PayloadIndex >= 0 && node.PayloadIndex < blob.MethodNames.Length)
    {
        string fullName = blob.MethodNames[node.PayloadIndex];
        int at = fullName.LastIndexOf('@');
        int dot = fullName.LastIndexOf('.', at >= 0 ? at - 1 : fullName.Length - 1);
        label += $" \"{(dot >= 0 ? fullName.Substring(dot + 1) : fullName)}\"";
    }

    bool isRunning = (index == runningIndex);
    if (isRunning)
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 0f, 1f));

    ImGui.Selectable(label, false);

    if (isRunning)
        ImGui.PopStyleColor();

    int childIdx = index + 1;
    for (int i = 0; i < node.ChildCount; i++)
    {
        RenderNode(blob, childIdx, runningIndex, depth + 1);
        childIdx += blob.Nodes[childIdx].SubtreeOffset;
    }
}
