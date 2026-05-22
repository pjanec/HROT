# T-01 — Solution Scaffolding

## Goal
Create the .NET 8 solution with the six projects and their dependencies.

## Project
All projects. This task lays the file structure.

## References
- `../README.md` for solution layout.
- `00-START-HERE.md` for conventions.

## Deliverables

Create the following files (paths relative to your VS Code workspace root —
adapt as needed):

```
NodeEditor.sln
src/NodeEditor.Primitives/NodeEditor.Primitives.csproj
src/NodeEditor.Core/NodeEditor.Core.csproj
src/NodeEditor.UI/NodeEditor.UI.csproj
src/NodeEditor.Demo/NodeEditor.Demo.csproj
tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj
tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj
```

## Implementation

### `Directory.Build.props` (solution root)

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>12</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest</AnalysisLevel>
  </PropertyGroup>
</Project>
```

### `NodeEditor.Primitives.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>NodeEditor.Primitives</RootNamespace>
    <AssemblyName>NodeEditor.Primitives</AssemblyName>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
```

### `NodeEditor.Core.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>NodeEditor.Core</RootNamespace>
    <AssemblyName>NodeEditor.Core</AssemblyName>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\NodeEditor.Primitives\NodeEditor.Primitives.csproj" />
  </ItemGroup>
</Project>
```

### `NodeEditor.UI.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>NodeEditor.UI</RootNamespace>
    <AssemblyName>NodeEditor.UI</AssemblyName>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\NodeEditor.Core\NodeEditor.Core.csproj" />
    <PackageReference Include="ImGui.NET" Version="1.91.6.1" />
  </ItemGroup>
</Project>
```

### `NodeEditor.Demo.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>NodeEditor.Demo</RootNamespace>
    <AssemblyName>NodeEditor.Demo</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\NodeEditor.UI\NodeEditor.UI.csproj" />
    <PackageReference Include="Raylib-cs" Version="6.1.1" />
    <PackageReference Include="rlImGui-cs" Version="2.1.0" />
  </ItemGroup>
</Project>
```

Versions are starting points; if NuGet resolves higher patch versions
that's fine, but pin major/minor.

### `NodeEditor.Core.Tests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <RootNamespace>NodeEditor.Core.Tests</RootNamespace>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\NodeEditor.Core\NodeEditor.Core.csproj" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
  </ItemGroup>
</Project>
```

### `NodeEditor.UI.Tests.csproj`

Same structure, but reference `NodeEditor.UI` instead of Core.

### `NodeEditor.sln`

Use `dotnet new sln -n NodeEditor`, then
`dotnet sln add` for each project.

## Demo entry point (placeholder)

Create `src/NodeEditor.Demo/Program.cs`:

```csharp
namespace NodeEditor.Demo;

internal static class Program
{
    private static void Main()
    {
        Console.WriteLine("NodeEditor.Demo placeholder. Real window comes in T-20.");
    }
}
```

## Acceptance

- `dotnet build` succeeds for the whole solution.
- `dotnet test` succeeds (no tests yet; should still pass).
- All projects have `<GenerateDocumentationFile>true</GenerateDocumentationFile>`.
- TreatWarningsAsErrors at solution level.

## Estimated size

Config files only. Should take ~10 minutes.

## Status

(fill in when complete)
