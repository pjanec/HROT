// CE-4651 — the HROT RUNTIME classes of the Unreal image generator (docs/designs/IG/DRAFT_Unreal_Image_Generator.md): vehicles, people,
// animals, missiles, fire, the shot renderer, the soundscape. U-18s: a RUNTIME module (a packaged client loads it); the importers live in
// HrotTerrainEditor. No editor module here except under bBuildEditor (WITH_EDITOR code only).
using UnrealBuildTool;

public class HrotTerrain : ModuleRules
{
	public HrotTerrain(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
		PublicDependencyModuleNames.AddRange(new string[] { "Core", "CoreUObject", "Engine", "Landscape", "Niagara", "SkeletalMerging" });
		PrivateDependencyModuleNames.AddRange(new string[] { "AssetRegistry", "Foliage" });
		if (Target.bBuildEditor) PrivateDependencyModuleNames.Add("UnrealEd");   // HrotShotRenderer: the editor's background-audio setting (WITH_EDITOR)
	}
}
