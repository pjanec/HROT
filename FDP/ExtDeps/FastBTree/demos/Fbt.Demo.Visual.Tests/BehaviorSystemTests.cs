using Fbt.Demo.Visual;
using Fbt.Runtime;
using Fbt.Serialization;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace Fbt.Demo.Visual.Tests
{
    public class BehaviorSystemTests
    {
        [Fact]
        public void FindPatrolPoint_SetsTargetPosition_NonZero()
        {
            // Arrange
            // Mimic exact structure of patrol.json causing issues
            string json = @"{
                ""TreeName"": ""PatrolTest"",
                ""Root"": {
                    ""Type"": ""Repeater"",
                    ""RepeatCount"": -1,
                    ""Children"": [
                        {
                            ""Type"": ""Sequence"",
                            ""Children"": [
                                { ""Type"": ""Action"", ""Action"": ""FindPatrolPoint"" },
                                { ""Type"": ""Wait"", ""Duration"": 2.0 } 
                            ]
                        }
                    ]
                }
            }";
            var blob = TreeCompiler.CompileFromJson(json);
            var trees = new Dictionary<string, BehaviorTreeBlob> { { "patrol", blob } };
            
            var system = new BehaviorSystem(trees);
            
            var agent = new Agent(1, Vector2.Zero, "patrol", AgentRole.Patrol);
            // Default target should be 0,0
            Assert.Equal(Vector2.Zero, agent.TargetPosition);
            
            // Act
            system.Update(new List<Agent> { agent }, 0f, 0.16f);
            
            // Assert
            // FindPatrolPoint should have run and set a random target (100-1180, 100-620)
            Assert.NotEqual(Vector2.Zero, agent.TargetPosition);
            Assert.True(agent.TargetPosition.X >= 100 && agent.TargetPosition.X <= 1180);
            Assert.True(agent.TargetPosition.Y >= 100 && agent.TargetPosition.Y <= 620);
        }

        [Fact]
        public void Movement_UpdatesPosition_TowardsTarget()
        {
            // Arrange
            var agent = new Agent(1, Vector2.Zero, "patrol", AgentRole.Patrol);
            agent.TargetPosition = new Vector2(100f, 0f); // Move right
            agent.Speed = 10f;
            
            // Act
            agent.UpdateMovement(1.0f); // 1 sec
            
            // Assert
            // Should be at (10, 0)
            Assert.Equal(new Vector2(10f, 0f), agent.Position);
        }
    }
}
