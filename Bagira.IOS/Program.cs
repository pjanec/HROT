using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.IOS.Logic;
using Bagira.IOS.Panels;
using Bagira.IOS.Services;
using Bagira.Map.Common.Dds;
using FDP.Toolkit.DER;
using Raylib_cs;
using rlImGui_cs;

namespace Bagira.IOS;

/// <summary>
/// IOS Mock entry point.
///
/// <para><b>CLI arguments</b> (all optional, parsed positionally by flag):
/// <list type="table">
///   <item><term>--domain &lt;n&gt;</term><description>DDS domain ID (default: 0).</description></item>
///   <item><term>--node &lt;n&gt;</term><description>Local node ID (default: 10).</description></item>
/// </list>
/// Example: <c>Bagira.IOS --domain 1 --node 20</c>
/// </para>
///
/// <para><b>Raylib lifecycle:</b>
/// <list type="number">
///   <item>Parse CLI args.</item>
///   <item>Construct services and panels.</item>
///   <item>Open Raylib window; set up rlImGui.</item>
///   <item>Enter main loop: <c>Update → BeginDrawing → rlImGui.Begin → DrawUI → rlImGui.End → EndDrawing</c>.</item>
///   <item>On window close: dispose mock, shut down ImGui, close window.</item>
/// </list>
/// </para>
/// </summary>
class Program
{
    // ── Window configuration ──────────────────────────────────────────────────

    private const int    WindowWidth  = 1280;
    private const int    WindowHeight = 720;
    private const int    TargetFps    = 60;
    private const string WindowTitle  = "IOS Mock";

    // ── CLI defaults ──────────────────────────────────────────────────────────

    private const int DefaultDomainId = 0;
    private const int DefaultNodeId   = 10;

    // ── Entry point ───────────────────────────────────────────────────────────

    static void Main(string[] args)
    {
        // 1. Parse CLI arguments
        int domainId = DefaultDomainId;
        int nodeId   = DefaultNodeId;

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--domain" && int.TryParse(args[i + 1], out int d)) domainId = d;
            if (args[i] == "--node"   && int.TryParse(args[i + 1], out int n)) nodeId   = n;
        }

        Console.WriteLine($"[IOS] Starting – domain={domainId} node={nodeId}");

        // 2. Construct services and panels
        var repo              = new DerRepo();
        var transactionMgr    = new RequestTransactionManager();
        var interactionPanel  = new InteractionPanel();

        // Event queues (backed by ConcurrentQueue; DDS adapters would enqueue here)
        var clickQueue     = new ConcurrentEventQueue<MapClickEvent>();
        var selectionQueue = new ConcurrentEventQueue<SelectionChangedEvent>();

        // DDS writer stubs – replace with live DdsWriter<T> wrappers when
        // CycloneDDS.Runtime is wired into this project.
        var configWriter       = new NullDdsWriter<MapInteractionConfig>();
        var createEntityWriter = new NullDdsWriter<CreateEntityRequest>();
        var missionCmdWriter   = new NullDdsWriter<MissionControlRequest>();

        var missionEditorSvc = new MissionEditorService(repo, missionCmdWriter);
        var contextMenuWriter = new NullDdsWriter<ContextActionsUpdate>();
        var contextMenuLogic  = new ContextMenuLogic(contextMenuWriter);

        var logic = new IosLogic(
            repo:                repo,
            missionEditorService: missionEditorSvc,
            contextMenuLogic:    contextMenuLogic,
            transactionManager:  transactionMgr,
            configWriter:        configWriter,
            createEntityWriter:  createEntityWriter,
            clickQueue:          clickQueue,
            selectionQueue:      selectionQueue,
            interactionPanel:    interactionPanel,
            ingressHandlers:     null,          // no live DDS participant yet
            mapGroupId:          IosLogicConstants.DefaultMapGroupId);

        var mock = new IosMock(
            logic:            logic,
            configPanel:      new ConfigPanel(),
            orbatPanel:       new OrbatPanel(),
            missionPanel:     new MissionPanel(),
            interactionPanel: interactionPanel,
            spawnerPanel:     new SpawnerPanel(),
            dataMonitorPanel: new DataMonitorPanel());

        // 3. Open Raylib window
        Raylib.InitWindow(WindowWidth, WindowHeight, WindowTitle);
        Raylib.SetTargetFPS(TargetFps);
        rlImGui.Setup(true);  // enable docking

        try
        {
            // 4. Main loop
            while (!Raylib.WindowShouldClose())
            {
                float dt = Raylib.GetFrameTime();
                mock.Update(dt);

                Raylib.BeginDrawing();
                Raylib.ClearBackground(Color.DarkGray);

                rlImGui.Begin();
                mock.DrawUI();
                rlImGui.End();

                Raylib.EndDrawing();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[IOS] FATAL: {ex}");
        }
        finally
        {
            // 5. Graceful shutdown
            mock.Dispose();
            rlImGui.Shutdown();
            Raylib.CloseWindow();
        }
    }
}

// ---------------------------------------------------------------------------
// Null writer — silent no-op used until live DDS participants are wired in.
// ---------------------------------------------------------------------------

/// <summary>
/// Silent no-op implementation of <see cref="IDdsWriter{T}"/>.
/// Used in <see cref="Program.Main"/> when a live DDS participant is not yet
/// available.
/// </summary>
file sealed class NullDdsWriter<T> : IDdsWriter<T>
{
    public void Write(T sample) { /* intentional no-op */ }
}

