using HrotStrideApp;

// BATCH-11 (STR-LOG-1): Initialize NLog file logging BEFORE the game runs so that
// startup, Stride engine messages, the per-second diagnostics dump, and any unhandled
// (incl. GPU-path) crash all land in <BaseDirectory>/logs/editor_stride.log.
// All HROT processes log via NLog; this mirrors Hrot.ClusterRunner/Program.cs.
// WinExe-safe: file logging does not require a console window.
StrideLogging.Configure();
try
{
    // BATCH-10 (STR-LIVE-1): Boot StrideHrotGame instead of the stock Stride Game.
    // StrideHrotGame.BeginRun() boots EditorStrideSubsystem with StrideVisualFactory,
    // neutralizes the template player/camera scripts, adds a fixed overview camera,
    // and enqueues the UrbanCombat demo entity spawns.
    // StrideHrotGame.Update(GameTime) drives EditorStrideSubsystem.Tick(dt) each frame.
    using var game = new StrideHrotGame();
    game.Run();
}
finally
{
    // Flush + close the log file so it is complete even on a clean exit.
    StrideLogging.Shutdown();
}
