// CE-4651 (U-18a) — loads ddsc.dll from the plugin's Binaries (it is delay-loaded; Windows would not look in the plugin's folder).
#include "Modules/ModuleManager.h"
#include "Interfaces/IPluginManager.h"
#include "HAL/PlatformProcess.h"
#include "Misc/Paths.h"

class FHrotDpsModule : public IModuleInterface
{
	void* Ddsc = nullptr;

	virtual void StartupModule() override
	{
		// the editor: the plugin's Binaries; U-18s a PACKAGED client: next to the executable (RuntimeDependencies stage it to $(BinaryOutputDir))
		const TSharedPtr<IPlugin> Plugin = IPluginManager::Get().FindPlugin(TEXT("HrotTerrain"));
		const FString InPlugin = FPaths::Combine(Plugin ? Plugin->GetBaseDir() : FString(), TEXT("Binaries/Win64/ddsc.dll"));
		const FString ByExe = FPaths::Combine(FPlatformProcess::BaseDir(), TEXT("ddsc.dll"));
		for (const FString& Dll : { InPlugin, ByExe })
			if (FPaths::FileExists(Dll) && (Ddsc = FPlatformProcess::GetDllHandle(*Dll)) != nullptr) break;
		UE_CLOG(!Ddsc, LogTemp, Error, TEXT("HrotDps: cannot load ddsc.dll from %s or %s (run scripts/unreal/dps_gen.py and rebuild)"), *InPlugin, *ByExe);
	}

	virtual void ShutdownModule() override
	{
		if (Ddsc) FPlatformProcess::FreeDllHandle(Ddsc);
		Ddsc = nullptr;
	}
};

IMPLEMENT_MODULE(FHrotDpsModule, HrotDps)
