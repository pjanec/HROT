using Xunit;
using Fdp.Kernel;
using FDP.Toolkit.ImGui.Utils;
using System;
using System.Reflection;

namespace FDP.Toolkit.ImGui.Tests;

public class RepoReflectorTests
{
    private class TestComponent
    {
        public int Value;
    }

    [Fact]
    public void ComponentReflector_HasComponent_ReturnsTrueForExistingComponent()
    {
        using var repo = new EntityRepository();
        repo.RegisterManagedComponent<TestComponent>();
        var entity = repo.CreateEntity();
        repo.SetComponent(entity, new TestComponent { Value = 42 });

        bool has = RepoReflector.HasComponent(repo, entity, typeof(TestComponent));

        Assert.True(has);
    }
    
    [Fact]
    public void ComponentReflector_HasComponent_ReturnsFalseForMissingComponent()
    {
        using var repo = new EntityRepository();
        repo.RegisterManagedComponent<TestComponent>();
        var entity = repo.CreateEntity();

        bool has = RepoReflector.HasComponent(repo, entity, typeof(TestComponent));

        Assert.False(has);
    }
    
    [Fact]
    public void ComponentReflector_GetComponent_ReturnsCorrectObject()
    {
        using var repo = new EntityRepository();
        repo.RegisterManagedComponent<TestComponent>();
        var entity = repo.CreateEntity();
        repo.SetComponent(entity, new TestComponent { Value = 99 });
        
        object? component = RepoReflector.GetComponent(repo, entity, typeof(TestComponent));
        
        Assert.NotNull(component);
        Assert.IsType<TestComponent>(component);
        Assert.Equal(99, ((TestComponent)component).Value);
    }

    [Fact]
    public void ComponentReflector_SetComponent_UpdatesValueInRepo()
    {
        using var repo = new EntityRepository();
        repo.RegisterManagedComponent<TestComponent>();
        var entity = repo.CreateEntity();
        var original = new TestComponent { Value = 10 };
        repo.SetComponent(entity, original);

        var next = new TestComponent { Value = 20 };
        RepoReflector.SetComponent(repo, entity, typeof(TestComponent), next);

        var retrieved = repo.GetComponent<TestComponent>(entity);
        Assert.Equal(20, retrieved.Value);
    }
}
