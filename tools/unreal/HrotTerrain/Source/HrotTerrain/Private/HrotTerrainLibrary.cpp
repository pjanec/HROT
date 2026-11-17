// CE-4651 — the HROT terrain import for the Unreal image generator.
#include "HrotTerrainLibrary.h"

#include "Editor.h"
#include "Engine/World.h"
#include "Landscape.h"
#include "LandscapeProxy.h"
#include "Misc/FileHelper.h"
#include "Modules/ModuleManager.h"

DEFINE_LOG_CATEGORY_STATIC(LogHrotTerrain, Log, All);

IMPLEMENT_MODULE(FDefaultModuleImpl, HrotTerrain)

ALandscape* UHrotTerrainLibrary::ImportHeights(const FString& R16Path, int32 SizeX, int32 SizeY, int32 QuadsPerSection,
	int32 SectionsPerComponent, FVector LocationCm, FVector ScaleCm, const FString& Label)
{
	const int32 ComponentQuads = QuadsPerSection * SectionsPerComponent;
	if (ComponentQuads <= 0 || (SizeX - 1) % ComponentQuads != 0 || (SizeY - 1) % ComponentQuads != 0)
	{
		UE_LOG(LogHrotTerrain, Error, TEXT("%d x %d vertices are not whole components of %d quads (the exporter pads them - UL-7)"), SizeX, SizeY, ComponentQuads);
		return nullptr;
	}

	TArray<uint8> Bytes;
	if (!FFileHelper::LoadFileToArray(Bytes, *R16Path))
	{
		UE_LOG(LogHrotTerrain, Error, TEXT("cannot read %s"), *R16Path);
		return nullptr;
	}
	if (Bytes.Num() != SizeX * SizeY * 2)
	{
		UE_LOG(LogHrotTerrain, Error, TEXT("%s holds %d bytes; %d x %d heights need %d"), *R16Path, Bytes.Num(), SizeX, SizeY, SizeX * SizeY * 2);
		return nullptr;
	}
	TArray<uint16> Heights;
	Heights.SetNumUninitialized(SizeX * SizeY);
	FMemory::Memcpy(Heights.GetData(), Bytes.GetData(), Bytes.Num());   // little-endian on every platform the editor runs on

	UWorld* World = GEditor ? GEditor->GetEditorWorldContext().World() : nullptr;
	if (!World)
	{
		UE_LOG(LogHrotTerrain, Error, TEXT("no editor world"));
		return nullptr;
	}

	FActorSpawnParameters Params;
	ALandscape* Landscape = World->SpawnActor<ALandscape>(LocationCm, FRotator::ZeroRotator, Params);
	if (!Landscape) return nullptr;
	Landscape->SetActorScale3D(ScaleCm);
	Landscape->SetActorLocation(LocationCm);
	Landscape->SetActorLabel(Label);

	const TMap<FGuid, TArray<uint16>> HeightData = { { FGuid(), MoveTemp(Heights) } };
	const TMap<FGuid, TArray<FLandscapeImportLayerInfo>> Layers = { { FGuid(), TArray<FLandscapeImportLayerInfo>() } };
	Landscape->Import(FGuid::NewGuid(), 0, 0, SizeX - 1, SizeY - 1, SectionsPerComponent, QuadsPerSection, HeightData, nullptr, Layers,
		ELandscapeImportAlphamapType::Additive, TArrayView<const FLandscapeLayer>());

	UE_LOG(LogHrotTerrain, Log, TEXT("imported %s: %d x %d vertices, %d x %d components of %d quads, at %s, scale %s"), *Label, SizeX, SizeY,
		(SizeX - 1) / ComponentQuads, (SizeY - 1) / ComponentQuads, ComponentQuads, *LocationCm.ToString(), *ScaleCm.ToString());
	return Landscape;
}
