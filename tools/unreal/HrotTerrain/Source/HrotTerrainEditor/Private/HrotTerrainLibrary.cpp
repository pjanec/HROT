// CE-4651 — the HROT terrain import for the Unreal image generator.
#include "HrotTerrainLibrary.h"

#include "Editor.h"
#include "Engine/World.h"
#include "Landscape.h"
#include "LandscapeProxy.h"
#include "Misc/FileHelper.h"
#include "Components/HierarchicalInstancedStaticMeshComponent.h"
#include "Engine/StaticMesh.h"
#include "Materials/MaterialInterface.h"
#include "Engine/StaticMeshActor.h"
#include "MeshDescription.h"
#include "MeshDescriptionBuilder.h"
#include "StaticMeshAttributes.h"
#include "UObject/Package.h"
#include "AssetRegistry/AssetRegistryModule.h"
#include "Kismet2/KismetEditorUtilities.h"
#include "Engine/Blueprint.h"
#include "HrotPersonAnimInstance.h"
#include "HrotRuntimeLibrary.h"
#include "Components/SkeletalMeshComponent.h"
#include "Modules/ModuleManager.h"

DEFINE_LOG_CATEGORY_STATIC(LogHrotTerrain, Log, All);

IMPLEMENT_MODULE(FDefaultModuleImpl, HrotTerrainEditor)

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

AActor* UHrotTerrainLibrary::ScatterInstances(const FString& BinPath, UStaticMesh* Mesh, UMaterialInterface* Material, const FString& Label,
	float CullDistanceCm, FVector MeshScale, float PivotUpCm)
{
	if (!Mesh)
	{
		UE_LOG(LogHrotTerrain, Error, TEXT("ScatterInstances %s: no mesh"), *Label);
		return nullptr;
	}
	TArray<uint8> Bytes;
	if (!FFileHelper::LoadFileToArray(Bytes, *BinPath) || Bytes.Num() % (6 * sizeof(float)) != 0)
	{
		UE_LOG(LogHrotTerrain, Error, TEXT("ScatterInstances %s: cannot read %s as records of 6 floats"), *Label, *BinPath);
		return nullptr;
	}
	UWorld* World = GEditor ? GEditor->GetEditorWorldContext().World() : nullptr;
	if (!World) return nullptr;

	const float* F = reinterpret_cast<const float*>(Bytes.GetData());
	const int32 Count = Bytes.Num() / (6 * sizeof(float));
	TArray<FTransform> Transforms;
	Transforms.Reserve(Count);
	for (int32 I = 0; I < Count; ++I, F += 6)
	{
		const FVector Scale(F[4] * MeshScale.X, F[4] * MeshScale.Y, F[5] * MeshScale.Z);
		Transforms.Emplace(FRotator(0.f, F[3], 0.f), FVector(F[0], F[1], F[2] + PivotUpCm * Scale.Z), Scale);
	}

	AActor* Actor = World->SpawnActor<AActor>(AActor::StaticClass(), FTransform::Identity);
	USceneComponent* Root = NewObject<USceneComponent>(Actor, TEXT("Root"));
	Actor->SetRootComponent(Root);
	Actor->AddInstanceComponent(Root);
	Root->RegisterComponent();

	UHierarchicalInstancedStaticMeshComponent* Hism = NewObject<UHierarchicalInstancedStaticMeshComponent>(Actor, TEXT("Instances"));
	Hism->SetStaticMesh(Mesh);
	if (Material) Hism->SetMaterial(0, Material);
	Hism->SetMobility(EComponentMobility::Static);
	Hism->SetCollisionEnabled(ECollisionEnabled::NoCollision);   // the picture only: HROT owns line of sight and movement
	if (CullDistanceCm > 0.f) Hism->SetCullDistances(0, (int32)CullDistanceCm);
	Hism->SetupAttachment(Root);
	Actor->AddInstanceComponent(Hism);
	Hism->RegisterComponent();
	Hism->AddInstances(Transforms, false, true);
	Actor->SetActorLabel(Label);

	UE_LOG(LogHrotTerrain, Log, TEXT("scattered %d instances of %s as %s"), Count, *Mesh->GetName(), *Label);
	return Actor;
}

AActor* UHrotTerrainLibrary::BuildMesh(const FString& BinPath, const FString& AssetPath, const TArray<UMaterialInterface*>& Materials,
	FVector LocationCm, const FString& Label)
{
	TArray<uint8> Bytes;
	if (!FFileHelper::LoadFileToArray(Bytes, *BinPath) || Bytes.Num() < 8)
	{
		UE_LOG(LogHrotTerrain, Error, TEXT("BuildMesh %s: cannot read %s"), *Label, *BinPath);
		return nullptr;
	}
	const int32 NV = *reinterpret_cast<const int32*>(Bytes.GetData());
	const int32 NT = *reinterpret_cast<const int32*>(Bytes.GetData() + 4);
	const int64 Need = 8 + int64(NV) * 12 + int64(NT) * 12 + NT;
	const bool bIds = Bytes.Num() == Need + int64(NV) * 8;   // U-20: a per-vertex ids trailer (building index, part) -> UV channel 1
	if (NV <= 0 || NT <= 0 || (Bytes.Num() != Need && !bIds))
	{
		UE_LOG(LogHrotTerrain, Error, TEXT("BuildMesh %s: %d vertices, %d triangles need %lld bytes, the file has %d"), *Label, NV, NT, Need, Bytes.Num());
		return nullptr;
	}
	const float* P = reinterpret_cast<const float*>(Bytes.GetData() + 8);
	const int32* T = reinterpret_cast<const int32*>(Bytes.GetData() + 8 + NV * 12);
	const uint8* M = Bytes.GetData() + 8 + NV * 12 + NT * 12;
	const float* Ids = bIds ? reinterpret_cast<const float*>(Bytes.GetData() + Need) : nullptr;

	FMeshDescription MD;
	FStaticMeshAttributes Attributes(MD);
	Attributes.Register();
	FMeshDescriptionBuilder B;
	B.SetMeshDescription(&MD);
	B.EnablePolyGroups();
	B.SetNumUVLayers(Ids ? 2 : 1);

	TArray<FVertexID> V;
	V.Reserve(NV);
	for (int32 I = 0; I < NV; ++I) V.Add(B.AppendVertex(FVector(P[I * 3], P[I * 3 + 1], P[I * 3 + 2])));
	const int32 Slots = FMath::Max(1, Materials.Num());
	TArray<FPolygonGroupID> Groups;
	for (int32 S = 0; S < Slots; ++S) Groups.Add(B.AppendPolygonGroup(FName(*FString::Printf(TEXT("Slot%d"), S))));

	for (int32 I = 0; I < NT; ++I)
	{
		const int32 A = T[I * 3], Bi = T[I * 3 + 1], C = T[I * 3 + 2];
		if (A < 0 || Bi < 0 || C < 0 || A >= NV || Bi >= NV || C >= NV) continue;
		const FVector PA(P[A * 3], P[A * 3 + 1], P[A * 3 + 2]), PB(P[Bi * 3], P[Bi * 3 + 1], P[Bi * 3 + 2]), PC(P[C * 3], P[C * 3 + 1], P[C * 3 + 2]);
		const FVector N = FVector::CrossProduct(PC - PA, PB - PA).GetSafeNormal();   // flat: every face its own normal (crisp walls)
		// a planar UV in metres-ish units (300 cm per tile): walls by their horizontal run and height, roofs by X / Y
		auto UV = [&](const FVector& Q) -> FVector2D
		{
			if (FMath::Abs(N.Z) > 0.7f) return FVector2D(Q.X / 300.0, Q.Y / 300.0);
			const FVector Along = FVector(-N.Y, N.X, 0.0).GetSafeNormal();
			return FVector2D(FVector::DotProduct(Q, Along) / 300.0, -Q.Z / 300.0);
		};
		FVertexInstanceID I0 = B.AppendInstance(V[A]), I1 = B.AppendInstance(V[Bi]), I2 = B.AppendInstance(V[C]);
		for (const FVertexInstanceID& Id : { I0, I1, I2 }) B.SetInstanceNormal(Id, N);
		B.SetInstanceUV(I0, UV(PA), 0); B.SetInstanceUV(I1, UV(PB), 0); B.SetInstanceUV(I2, UV(PC), 0);
		if (Ids)   // U-20: which house (x) and which window / door (y) this vertex belongs to - read back at runtime to take a house out of its tile
		{
			B.SetInstanceUV(I0, FVector2D(Ids[A * 2], Ids[A * 2 + 1]), 1); B.SetInstanceUV(I1, FVector2D(Ids[Bi * 2], Ids[Bi * 2 + 1]), 1);
			B.SetInstanceUV(I2, FVector2D(Ids[C * 2], Ids[C * 2 + 1]), 1);
		}
		B.AppendTriangle(I0, I1, I2, Groups[FMath::Clamp<int32>(M[I], 0, Slots - 1)]);
	}

	const FString PackageName = AssetPath;
	const FString AssetName = FPackageName::GetLongPackageAssetName(PackageName);
	UPackage* Package = CreatePackage(*PackageName);
	UStaticMesh* Mesh = NewObject<UStaticMesh>(Package, *AssetName, RF_Public | RF_Standalone);
	for (int32 S = 0; S < Slots; ++S)
		Mesh->GetStaticMaterials().Add(FStaticMaterial(Materials.IsValidIndex(S) ? Materials[S] : nullptr, FName(*FString::Printf(TEXT("Slot%d"), S))));
	if (Ids) Mesh->bAllowCPUAccess = true;   // U-20: the runtime copies the tile into a dynamic mesh when one of its houses is damaged
	UStaticMesh::FBuildMeshDescriptionsParams Params;
	Params.bBuildSimpleCollision = false;
	Params.bFastBuild = true;
	if (Ids)   // U-20: the ids must stay exact integers (half-float UVs lose them above 2048) and readable at runtime
	{
		Params.bAllowCpuAccess = true;
		UStaticMesh::FBuildMeshDescriptionsLODParams Lod; Lod.bUseFullPrecisionUVs = true;
		Params.PerLODOverrides.Add(Lod);
	}
	Mesh->BuildFromMeshDescriptions({ &MD }, Params);
	FAssetRegistryModule::AssetCreated(Mesh);
	Package->MarkPackageDirty();

	UWorld* World = GEditor ? GEditor->GetEditorWorldContext().World() : nullptr;
	if (!World) return nullptr;
	AStaticMeshActor* Actor = World->SpawnActor<AStaticMeshActor>(LocationCm, FRotator::ZeroRotator);
	Actor->GetStaticMeshComponent()->SetStaticMesh(Mesh);
	Actor->SetActorLabel(Label);
	UE_LOG(LogHrotTerrain, Log, TEXT("built %s: %d vertices, %d triangles"), *Label, NV, NT);
	return Actor;
}

UClass* UHrotTerrainLibrary::BlueprintFromActor(AActor* Actor, const FString& AssetPath)
{
	if (!Actor) return nullptr;
	FKismetEditorUtilities::FCreateBlueprintFromActorParams Params;
	Params.bReplaceActor = false;
	Params.bKeepMobility = true;
	Params.bOpenBlueprint = false;
	UBlueprint* Bp = FKismetEditorUtilities::CreateBlueprintFromActor(AssetPath, Actor, Params);
	if (!Bp)
	{
		UE_LOG(LogHrotTerrain, Error, TEXT("BlueprintFromActor: could not make %s from %s"), *AssetPath, *Actor->GetName());
		return nullptr;
	}
	return Bp->GeneratedClass;
}

UHrotPersonState* UHrotTerrainLibrary::AddPersonState(AActor* Actor) { return UHrotRuntimeLibrary::AddPersonState(Actor); }

void UHrotTerrainLibrary::PoseNow(USkeletalMeshComponent* Mesh, float Seconds, int32 Steps) { UHrotRuntimeLibrary::PoseNow(Mesh, Seconds, Steps); }
