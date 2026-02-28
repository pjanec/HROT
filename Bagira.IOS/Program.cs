using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.IOS.Logic;
using Bagira.IOS.Panels;
using Bagira.IOS.Services;
using Bagira.Map.Common.Dds;
using CycloneDDS.Runtime;
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

        // Live DDS participant for all topic writers.
        var participant = new DdsParticipant((uint)domainId);

        var repo              = new DerRepo(nodeId);
        var transactionMgr    = new RequestTransactionManager();
        var interactionPanel  = new InteractionPanel();

        // Event queues (backed by ConcurrentQueue; DDS adapters enqueue here)
        var clickQueue     = new ConcurrentEventQueue<MapClickEvent>();
        var selectionQueue = new ConcurrentEventQueue<SelectionChangedEvent>();

        // Live DDS writers — replace NullDdsWriter stubs with real DDS adapters.
        var configWriter       = new DdsWriterAdapter<MapInteractionConfig>(participant, IosLogicConstants.LogTopicConfig);
        var createEntityWriter = new DdsWriterAdapter<CreateEntityRequest>(participant, IosLogicConstants.LogTopicCreate);
        var missionCmdWriter   = new DdsWriterAdapter<MissionControlRequest>(participant, IosLogicConstants.TopicMissionControl);

        var missionEditorSvc = new MissionEditorService(repo, missionCmdWriter);
        var contextMenuWriter = new DdsWriterAdapter<ContextActionsUpdate>(participant, IosLogicConstants.TopicContextActions);
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
            ingressHandlers:     null,
            mapGroupId:          IosLogicConstants.DefaultMapGroupId);

        var mock = new IosMock(
            logic:            logic,
            configPanel:      new ConfigPanel(),
            orbatPanel:       new OrbatPanel(),
            missionPanel:     new MissionPanel(),
            interactionPanel: interactionPanel,
            spawnerPanel:     new SpawnerPanel());

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
            configWriter.Dispose();
            createEntityWriter.Dispose();
            missionCmdWriter.Dispose();
            contextMenuWriter.Dispose();
            participant.Dispose();
            rlImGui.Shutdown();
            Raylib.CloseWindow();
        }
    }
}
