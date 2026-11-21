// CE-4651 — the HROT terrain import for the Unreal image generator (docs/designs/IG/DRAFT_Unreal_Image_Generator.md).
using UnrealBuildTool;

public class HrotTerrain : ModuleRules
{
	public HrotTerrain(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
		PublicDependencyModuleNames.AddRange(new string[] { "Core", "CoreUObject", "Engine", "Landscape", "Niagara", "SkeletalMerging" });
		PrivateDependencyModuleNames.AddRange(new string[] { "UnrealEd", "MeshDescription", "StaticMeshDescription", "MeshConversion", "AssetRegistry", "Kismet", "Foliage" });
	}
}
