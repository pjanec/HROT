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
		const TSharedPtr<IPlugin> Plugin = IPluginManager::Get().FindPlugin(TEXT("HrotTerrain"));
		const FString Dll = FPaths::Combine(Plugin ? Plugin->GetBaseDir() : FString(), TEXT("Binaries/Win64/ddsc.dll"));
		Ddsc = FPlatformProcess::GetDllHandle(*Dll);
		UE_CLOG(!Ddsc, LogTemp, Error, TEXT("HrotDps: cannot load %s (run scripts/unreal/dps_gen.py and rebuild)"), *Dll);
	}

	virtual void ShutdownModule() override
	{
		if (Ddsc) FPlatformProcess::FreeDllHandle(Ddsc);
		Ddsc = nullptr;
	}
};

IMPLEMENT_MODULE(FHrotDpsModule, HrotDps)
