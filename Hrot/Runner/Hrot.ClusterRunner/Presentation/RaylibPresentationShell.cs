using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Hrot.ClusterRunner.Presentation;

internal sealed class RaylibPresentationShell : IPresentationShell
{
    private Raylib_cs.Texture2D _atlasTexture;
    private IntPtr _iniFilenamePtr;

    public void InitWindow(int width, int height, string title, int targetFps)
    {
        Raylib_cs.Raylib.SetConfigFlags(Raylib_cs.ConfigFlags.ResizableWindow | Raylib_cs.ConfigFlags.Msaa4xHint);
        Raylib_cs.Raylib.InitWindow(width, height, title);
        Raylib_cs.Raylib.SetExitKey(Raylib_cs.KeyboardKey.Null);
        Raylib_cs.Raylib.SetTargetFPS(targetFps);
    }

    public void SetupImGui()
    {
        rlImGui_cs.rlImGui.Setup(true);
        var io = ImGuiNET.ImGui.GetIO();
        io.ConfigFlags |= ImGuiNET.ImGuiConfigFlags.DockingEnable;

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string configDir = Path.Combine(appData, "HROT");
        Directory.CreateDirectory(configDir);
        string iniPath = Path.Combine(configDir, "imgui.ini");
        _iniFilenamePtr = Marshal.StringToHGlobalAnsi(iniPath);
        unsafe
        {
            io.NativePtr->IniFilename = (byte*)_iniFilenamePtr;
        }
    }

    public void ShutdownImGui()
    {
        rlImGui_cs.rlImGui.Shutdown();
        if (_iniFilenamePtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_iniFilenamePtr);
            _iniFilenamePtr = IntPtr.Zero;
        }
    }

    public void CloseWindow()
    {
        if (_atlasTexture.Id != 0)
            Raylib_cs.Raylib.UnloadTexture(_atlasTexture);
        Raylib_cs.Raylib.CloseWindow();
    }

    public void UnloadAtlasTexture()
    {
        if (_atlasTexture.Id != 0)
        {
            Raylib_cs.Raylib.UnloadTexture(_atlasTexture);
            _atlasTexture = default;
        }
    }

    public Fdp.Presentation.Icons.IconAtlas LoadIconAtlas()
    {
        byte[] pngBytes = Fdp.Presentation.Icons.EmbeddedAtlasResources.GetSilkAtlasPngBytes();
        var img = Raylib_cs.Raylib.LoadImageFromMemory(".png", pngBytes);
        _atlasTexture = Raylib_cs.Raylib.LoadTextureFromImage(img);
        Raylib_cs.Raylib.UnloadImage(img);
        return new Fdp.Presentation.Icons.IconAtlas(
            (nint)_atlasTexture.Id, _atlasTexture.Width, _atlasTexture.Height, 16f);
    }
}
