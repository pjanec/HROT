using Raylib_cs;
using System.Numerics;
using System.Collections.Generic;
using Fhsm.Kernel.Data;

namespace Fhsm.Demo.Visual
{
    public class RenderSystem
    {
        public void RenderAgents(List<Agent> agents, Agent? selectedAgent, Dictionary<string, HsmDefinitionBlob> machines, float currentTime)
        {
            foreach (var agent in agents)
            {
                // Draw target line if moving
                if (agent.Velocity.LengthSquared() > 0)
                {
                     Raylib.DrawLineV(agent.Position, agent.TargetPosition, new Color(200, 200, 200, 100));
                }

                // Draw agent body
                float radius = 8;
                if (agent == selectedAgent)
                {
                    Raylib.DrawCircleV(agent.Position, radius + 4, Color.White); // Selection ring
                }
                
                // Flash white when attacking
                Color agentColor = agent.Color;
                if (agent.AttackFlashTimer > 0)
                {
                    agentColor = Color.White;
                }
                
                Raylib.DrawCircleV(agent.Position, radius, agentColor);
                
                // Draw direction indicator
                var direction = new Vector2(MathF.Cos(agent.Rotation), MathF.Sin(agent.Rotation));
                var endPos = agent.Position + direction * 12;
                Raylib.DrawLineV(agent.Position, endPos, Color.Black);
                
                // Draw attack effect - expanding yellow ring
                if (agent.AttackFlashTimer > 0)
                {
                    float attackRadius = (0.3f - agent.AttackFlashTimer) * 60f;
                    byte alpha = (byte)(agent.AttackFlashTimer / 0.3f * 200);
                    Raylib.DrawCircleLines(
                        (int)agent.Position.X,
                        (int)agent.Position.Y,
                        (int)attackRadius,
                        Raylib.ColorAlpha(Color.Yellow, alpha / 255f));
                }
                
                // Render simple agent ID label
                var labelLines = new List<(string, Color)>
                {
                    ($"Agent {agent.Id}", Color.White),
                    ($"{agent.Role}", Color.LightGray)
                };
                RenderAgentLabel(agent.Position, labelLines, agent == selectedAgent);
            }
        }
        
        private void RenderAgentLabel(Vector2 position, List<(string Text, Color Color)> lines, bool isSelected)
        {
            if (lines.Count == 0) return;

            int fontSize = 10;
            int lineHeight = 12;
            
            // Calculate background size
            int maxWidth = 0;
            foreach (var line in lines)
            {
                 int w = Raylib.MeasureText(line.Text, fontSize);
                 if (w > maxWidth) maxWidth = w;
            }
            
            int bgHeight = lines.Count * lineHeight + 8;
            Vector2 labelPos = position + new Vector2(0, -35); // Move higher
            
            // Draw background
            Color bgColor = isSelected ? new Color(50, 50, 50, 220) : new Color(0, 0, 0, 180);
            Raylib.DrawRectangle(
                (int)(labelPos.X - maxWidth / 2 - 4),
                (int)(labelPos.Y - 4),
                maxWidth + 8,
                bgHeight,
                bgColor);
            
            // Draw each line
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                int textWidth = Raylib.MeasureText(line.Text, fontSize);
                Raylib.DrawText(
                    line.Text,
                    (int)(labelPos.X - textWidth / 2),
                    (int)(labelPos.Y + i * lineHeight),
                    fontSize,
                    line.Color);
            }
        }
    }
}
