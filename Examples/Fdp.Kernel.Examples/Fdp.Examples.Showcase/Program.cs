using System;
using Raylib_cs;
using rlImGui_cs;
using Fdp.Examples.Showcase.Core;

namespace Fdp.Examples.Showcase
{
    class Program
    {
        const int WINDOW_WIDTH = 1920;
        const int WINDOW_HEIGHT = 1080;
        const int TARGET_FPS = 144;

        static void Main(string[] args)
        {
            // Initialize Raylib window
            Raylib.InitWindow(WINDOW_WIDTH, WINDOW_HEIGHT, "FDP Military Showcase - Raylib + ImGui");
            Raylib.SetTargetFPS(TARGET_FPS);
            
            // Initialize ImGui bridge
            rlImGui.Setup(true);

            try
            {
                var game = new ShowcaseGame();
                game.Initialize();
                game.RunRaylibLoop();
                game.Cleanup();
            }
            catch (Exception ex)
            {
                Console.WriteLine("CRITICAL ERROR: " + ex.ToString());
            }
            finally
            {
                rlImGui.Shutdown();
                Raylib.CloseWindow();
            }
        }
    }
}
