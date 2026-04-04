using System.Collections.Generic;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Time.Controllers;
using Fdp.Kernel;
using Hrot.CGF;
using Hrot.Editor;
using Hrot.Editor.UI;
using Hrot.Orchestrator;
using Hrot.ScenarioEditor;
using Hrot.ScenarioEditor.Services;
using Hrot.SimHost;
using ModuleHost.Core;
using ModuleHost.Core.Abstractions;
using Raylib_cs;

// ── 1. ECS world ─────────────────────────────────────────────────────────────
var world       = new EntityRepository();
var accumulator = new EventAccumulator();
var kernel      = new ModuleHostKernel(world, accumulator);

// ── 2. Time controller (stepping — no DDS sync partner) ────────────────────
var stepping = new SteppingTimeController(new GlobalTime { TimeScale = 1.0f });
kernel.SetTimeController(stepping);

// ── 3. Shared services ────────────────────────────────────────────────────────
var entityMap        = new NetworkEntityMap();
var doctrineRegistry = new DoctrineRegistry();
var clusterSlave     = new ClusterSlave(0, "Editor", world.Bus);
var fileService      = EditorBootstrap.CreateFileService();

// ── 4. Module registration (offline — no translator packs) ───────────────────
// ── 4a. Named pack instances for feature-switch RCU ────────────────────────
var simHostCorePack  = new SimHostCoreLogicPack(entityMap);
var cgfLogicPackInst = new CgfLogicPack(doctrineRegistry, entityMap);
var orchPack         = new OrchestrationLogicPack(clusterSlave);
var scenarioMod      = new ScenarioEditorModule(fileService);

kernel.RegisterModule(simHostCorePack);
kernel.RegisterModule(cgfLogicPackInst);
kernel.RegisterModule(orchPack);
kernel.RegisterModule(scenarioMod);

// ── 4b. Logic-pack list used by EditorApplication.SwitchToExternalAsync ───
var logicPacks = new List<IEcsModule> { simHostCorePack, cgfLogicPackInst };

// ── 5. Kernel initialization ──────────────────────────────────────────────────
kernel.Initialize();

// ── 6. Editor application (IEditorLogic facade) ──────────────────────────────
var app   = new EditorApplication(fileService, world.Bus, world, kernel, logicPacks);
var files = new ScenarioBrowserPanel();
var tools = new EditorToolbarPanel();

// ── 7. Raylib window loop ─────────────────────────────────────────────────────
const int TargetFps      = 60;
const int WindowWidth    = 1280;
const int WindowHeight   = 720;
const string WindowTitle = "HROT Editor";

Raylib.InitWindow(WindowWidth, WindowHeight, WindowTitle);
Raylib.SetTargetFPS(TargetFps);

try
{
    while (!Raylib.WindowShouldClose())
    {
        float dt = Raylib.GetFrameTime();

        // Simulation tick
        stepping.Step(dt);
        kernel.Update();

        // Rendering
        Raylib.BeginDrawing();
        Raylib.ClearBackground(Color.Black);
        // TODO: ImGui panels (Phase 7 — wired in a future batch)
        Raylib.EndDrawing();
    }
}
finally
{
    Raylib.CloseWindow();
    kernel.Dispose();
    world.Dispose();
}
