// CE-4651 (U-18s) — the HROT terrain IMPORT (editor only): heights into a Landscape, scattered instances, building meshes, Blueprints from actors.
using UnrealBuildTool;

public class HrotTerrainEditor : ModuleRules
{
	public HrotTerrainEditor(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
		PublicDependencyModuleNames.AddRange(new string[] { "Core", "CoreUObject", "Engine", "Landscape", "HrotTerrain" });
		PrivateDependencyModuleNames.AddRange(new string[] { "UnrealEd", "MeshDescription", "StaticMeshDescription", "MeshConversion", "AssetRegistry", "Kismet", "Foliage" });
	}
}
