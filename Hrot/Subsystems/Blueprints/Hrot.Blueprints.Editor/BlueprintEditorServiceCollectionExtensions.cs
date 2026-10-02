using Hrot.Blueprints.Core.Debug;
using Microsoft.Extensions.DependencyInjection;

namespace Hrot.Blueprints.Editor;

public static class BlueprintEditorServiceCollectionExtensions
{
    public static IServiceCollection AddBlueprintEditor(
        this IServiceCollection services)
    {
        services.AddSingleton<DirtyTracker>();
        services.AddSingleton<EditorSelectionStore>();
        services.AddSingleton<EditorState>();

        // Register BlueprintWindowRegistrar as both its concrete type and the engine
        //    Fdp.Toolkit.Runner.IWindowRegistrar. ⚠ Qualified deliberately — the OTHER seam it services is
        //    IShellCommandRegistrar (line below), and the two used to share a name.
        // so the subsystem orchestrator can call RegisterWindows(WindowManager) to wire the panels.
        services.AddSingleton<BlueprintWindowRegistrar>();
        services.AddSingleton<Fdp.Toolkit.Runner.IWindowRegistrar>(
            sp => sp.GetRequiredService<BlueprintWindowRegistrar>());

        services.AddSingleton<BlueprintEditorModule>(sp =>
            new BlueprintEditorModule(
                sp.GetRequiredService<IShellCommandRegistrar>(),
                sp.GetRequiredService<DirtyTracker>(),
                sp.GetRequiredService<EditorSelectionStore>(),
                sp.GetRequiredService<EditorState>(),
                sp.GetRequiredService<IOutputConsole>(),
                sp.GetService<IBlueprintDebugSession>()));
        return services;
    }
}
