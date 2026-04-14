using System;
using System.Numerics;
using Raylib_cs;
using Fdp.Kernel;
using Fdp.Examples.Showcase.Components;

namespace Fdp.Examples.Showcase.Core
{
    /// <summary>
    /// Raylib-based renderer for the battlefield.
    /// Renders all entities with visual shapes, health bars, and effects.
    /// </summary>
    public class ShowcaseRenderer
    {
        private readonly ShowcaseGame _game;
        private Camera2D _camera;
        
        // Battlefield dimensions (world coordinates)
        private const float WORLD_WIDTH = 100.0f;
        private const float WORLD_HEIGHT = 50.0f;
        private const float SCALE = 16.0f; // Pixels per world unit
        
        public Camera2D Camera => _camera;

        public ShowcaseRenderer(ShowcaseGame game)
        {
            _game = game;
            
            // Initialize camera centered on battlefield
            _camera = new Camera2D
            {
                Offset = new Vector2(960, 540), // Will be updated to actual screen size
                Target = new Vector2(WORLD_WIDTH / 2, WORLD_HEIGHT / 2),
                Rotation = 0.0f,
                Zoom = 1.0f
            };
        }
        
        public void UpdateCameraOffset()
        {
            _camera.Offset = new Vector2(Raylib.GetScreenWidth() / 2, Raylib.GetScreenHeight() / 2);
        }
        
        public void UpdateCamera(float dt)
        {
            // Pan with WASD only
            float panSpeed = 30.0f / _camera.Zoom;
            if (Raylib.IsKeyDown(KeyboardKey.D))
                _camera.Target.X += panSpeed * dt;
            if (Raylib.IsKeyDown(KeyboardKey.A))
                _camera.Target.X -= panSpeed * dt;
            if (Raylib.IsKeyDown(KeyboardKey.S))
                _camera.Target.Y += panSpeed * dt;
            if (Raylib.IsKeyDown(KeyboardKey.W))
                _camera.Target.Y -= panSpeed * dt;

            // Zoom with mouse wheel, centered on cursor
            float wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0)
            {
                // Get mouse position in screen space
                Vector2 mousePos = Raylib.GetMousePosition();
                
                // Get the world position before zoom
                Vector2 mouseWorldPos = Raylib.GetScreenToWorld2D(mousePos, _camera);
                
                // Update zoom (unlimited, 2x faster)
                float zoomIncrement = wheel * 0.25f;
                _camera.Zoom += zoomIncrement;
                _camera.Zoom = Math.Max(0.01f, _camera.Zoom); // Minimum zoom to prevent inversion
                
                // Get the world position after zoom
                Vector2 mouseWorldPosAfter = Raylib.GetScreenToWorld2D(mousePos, _camera);
                
                // Adjust camera target to keep the same world point under the cursor
                _camera.Target.X += (mouseWorldPos.X - mouseWorldPosAfter.X);
                _camera.Target.Y += (mouseWorldPos.Y - mouseWorldPosAfter.Y);
            }

            // Reset camera with Home key
            if (Raylib.IsKeyPressed(KeyboardKey.Home))
            {
                _camera.Target = new Vector2(WORLD_WIDTH / 2, WORLD_HEIGHT / 2);
                _camera.Zoom = 1.0f;
            }
        }

        public void RenderBattlefield()
        {
            Raylib.BeginMode2D(_camera);
            
            // Draw battlefield boundary
            Raylib.DrawRectangleLinesEx(
                new Rectangle(0, 0, WORLD_WIDTH, WORLD_HEIGHT),
                0.2f,
                new Color(100, 100, 150, 255)
            );
            
            // Draw grid
            DrawGrid();
            
            // Draw all entities
            var renderQuery = _game.Repo.Query()
                .With<Position>()
                .With<RenderSymbol>()
                .Build();

            // First pass: Draw entities
            foreach (var entity in renderQuery)
            {
                ref readonly var pos = ref _game.Repo.GetComponentRO<Position>(entity);
                ref readonly var sym = ref _game.Repo.GetComponentRO<RenderSymbol>(entity);
                
                DrawEntity(entity, pos, sym);
            }
            
            // Second pass: Draw health bars and effects on top
            foreach (var entity in renderQuery)
            {
                ref readonly var pos = ref _game.Repo.GetComponentRO<Position>(entity);
                ref readonly var sym = ref _game.Repo.GetComponentRO<RenderSymbol>(entity);
                
                if (_game.Repo.HasComponent<UnitStats>(entity))
                {
                    ref readonly var stats = ref _game.Repo.GetComponentRO<UnitStats>(entity);
                    DrawHealthBar(pos, stats, sym.Size);
                }
                
                // Draw velocity vector if entity has velocity
                if (_game.Repo.HasComponent<Velocity>(entity))
                {
                    ref readonly var vel = ref _game.Repo.GetComponentRO<Velocity>(entity);
                    DrawVelocityIndicator(pos, vel, sym.Size);
                }
                
                // Draw hit flash effect
                if (_game.Repo.HasComponent<HitFlash>(entity))
                {
                    ref readonly var flash = ref _game.Repo.GetComponentRO<HitFlash>(entity);
                    if (flash.Remaining > 0)
                    {
                        DrawHitFlash(pos, sym.Size, flash.Remaining);
                    }
                }
            }
            
            // Draw particles
            var particleQuery = _game.Repo.Query().With<Particle>().With<Position>().Build();
            foreach (var entity in particleQuery)
            {
                ref readonly var pos = ref _game.Repo.GetComponentRO<Position>(entity);
                ref readonly var particle = ref _game.Repo.GetComponentRO<Particle>(entity);
                
                DrawParticle(pos, particle);
            }
            
            Raylib.EndMode2D();
        }
        
        private void DrawGrid()
        {
            Color gridColor = new Color(50, 50, 70, 100);
            
            // Vertical lines every 10 units
            for (float x = 10; x < WORLD_WIDTH; x += 10)
            {
                Raylib.DrawLineEx(
                    new Vector2(x, 0),
                    new Vector2(x, WORLD_HEIGHT),
                    0.05f,
                    gridColor
                );
            }
            
            // Horizontal lines every 10 units
            for (float y = 10; y < WORLD_HEIGHT; y += 10)
            {
                Raylib.DrawLineEx(
                    new Vector2(0, y),
                    new Vector2(WORLD_WIDTH, y),
                    0.05f,
                    gridColor
                );
            }
        }
        
        private void DrawEntity(Entity entity, in Position pos, in RenderSymbol sym)
        {
            Vector2 worldPos = new Vector2(pos.X, pos.Y);
            Color color = sym.ToColor();
            float size = sym.Size;
            
            switch (sym.Shape)
            {
                case EntityShape.Triangle:
                    DrawTriangle(worldPos, size, color);
                    break;
                    
                case EntityShape.Square:
                    Raylib.DrawRectangleV(
                        new Vector2(worldPos.X - size / 2, worldPos.Y - size / 2),
                        new Vector2(size, size),
                        color
                    );
                    // Draw outline
                    Raylib.DrawRectangleLinesEx(
                        new Rectangle(worldPos.X - size / 2, worldPos.Y - size / 2, size, size),
                        0.1f,
                        Color.White
                    );
                    break;
                    
                case EntityShape.Circle:
                    Raylib.DrawCircleV(worldPos, size / 2, color);
                    Raylib.DrawCircleLinesV(worldPos, size / 2, Color.White);
                    break;
                    
                case EntityShape.Diamond:
                    DrawDiamond(worldPos, size, color);
                    break;
                    
                case EntityShape.Hexagon:
                    Raylib.DrawPoly(worldPos, 6, size / 2, 0, color);
                    Raylib.DrawPolyLinesEx(worldPos, 6, size / 2, 0, 0.1f, Color.White);
                    break;
                    
                case EntityShape.Cross:
                    DrawCross(worldPos, size, color);
                    break;
            }
        }
        
        private void DrawTriangle(Vector2 center, float size, Color color)
        {
            float h = size * 0.7f;
            Raylib.DrawTriangle(
                new Vector2(center.X, center.Y - h),
                new Vector2(center.X - size / 2, center.Y + size / 2),
                new Vector2(center.X + size / 2, center.Y + size / 2),
                color
            );
            // Outline
            Raylib.DrawTriangleLines(
                new Vector2(center.X, center.Y - h),
                new Vector2(center.X - size / 2, center.Y + size / 2),
                new Vector2(center.X + size / 2, center.Y + size / 2),
                Color.White
            );
        }
        
        private void DrawDiamond(Vector2 center, float size, Color color)
        {
            float half = size / 2;
            Raylib.DrawTriangle(
                new Vector2(center.X, center.Y - half),
                new Vector2(center.X - half, center.Y),
                new Vector2(center.X, center.Y + half),
                color
            );
            Raylib.DrawTriangle(
                new Vector2(center.X, center.Y - half),
                new Vector2(center.X + half, center.Y),
                new Vector2(center.X, center.Y + half),
                color
            );
            // Outline
            Raylib.DrawLineEx(new Vector2(center.X, center.Y - half), new Vector2(center.X - half, center.Y), 0.1f, Color.White);
            Raylib.DrawLineEx(new Vector2(center.X - half, center.Y), new Vector2(center.X, center.Y + half), 0.1f, Color.White);
            Raylib.DrawLineEx(new Vector2(center.X, center.Y + half), new Vector2(center.X + half, center.Y), 0.1f, Color.White);
            Raylib.DrawLineEx(new Vector2(center.X + half, center.Y), new Vector2(center.X, center.Y - half), 0.1f, Color.White);
        }
        
        private void DrawCross(Vector2 center, float size, Color color)
        {
            float half = size / 2;
            Raylib.DrawLineEx(new Vector2(center.X - half, center.Y), new Vector2(center.X + half, center.Y), 0.15f, color);
            Raylib.DrawLineEx(new Vector2(center.X, center.Y - half), new Vector2(center.X, center.Y + half), 0.15f, color);
        }
        
        private void DrawHealthBar(in Position pos, in UnitStats stats, float entitySize)
        {
            float barWidth = entitySize * 1.2f;
            float barHeight = 0.3f;
            float barY = pos.Y - entitySize * 0.6f - 0.5f;
            
            // Background
            Raylib.DrawRectangleV(
                new Vector2(pos.X - barWidth / 2, barY),
                new Vector2(barWidth, barHeight),
                new Color(50, 50, 50, 200)
            );
            
            // Health fill
            float healthPercent = stats.Health / stats.MaxHealth;
            float healthWidth = barWidth * healthPercent;
            Color healthColor = healthPercent > 0.6f ? Color.Green : 
                               (healthPercent > 0.3f ? Color.Yellow : Color.Red);
            
            Raylib.DrawRectangleV(
                new Vector2(pos.X - barWidth / 2, barY),
                new Vector2(healthWidth, barHeight),
                healthColor
            );
        }
        
        private void DrawVelocityIndicator(in Position pos, in Velocity vel, float entitySize)
        {
            float speed = MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
            if (speed < 0.1f) return;
            
            Vector2 dir = new Vector2(vel.X / speed, vel.Y / speed);
            Vector2 start = new Vector2(pos.X, pos.Y);
            Vector2 end = new Vector2(pos.X + dir.X * (entitySize + 0.5f), pos.Y + dir.Y * (entitySize + 0.5f));
            
            Raylib.DrawLineEx(start, end, 0.1f, new Color(255, 255, 100, 180));
        }
        
        private void DrawHitFlash(in Position pos, float entitySize, float intensity)
        {
            float radius = entitySize * (1.0f + intensity * 0.5f);
            byte alpha = (byte)(intensity * 255);
            Raylib.DrawCircleV(new Vector2(pos.X, pos.Y), radius, new Color((byte)255, (byte)255, (byte)255, alpha));
        }
        
        private void DrawParticle(in Position pos, in Particle particle)
        {
            float life = particle.LifeRemaining / particle.MaxLife;
            byte alpha = (byte)(life * 200);
            Color color = new Color(particle.R, particle.G, particle.B, alpha);
            
            Raylib.DrawCircleV(new Vector2(pos.X, pos.Y), particle.Size, color);
        }
    }
}