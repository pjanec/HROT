// CE-4651 (U-17g) — see the header.
#include "HrotMissile.h"

#include "Components/StaticMeshComponent.h"
#include "Engine/StaticMesh.h"
#include "Engine/World.h"
#include "HrotShotRenderer.h"

AHrotMissile::AHrotMissile()
{
	PrimaryActorTick.bCanEverTick = true;   // the trail follows the body: a few missiles at a time, each a handful of lines a frame
	Root = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));
	RootComponent = Root;
	Body = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Body"));
	Body->SetupAttachment(Root);
	Body->SetCollisionEnabled(ECollisionEnabled::NoCollision);
	Body->SetMobility(EComponentMobility::Movable);
}

void AHrotMissile::OnConstruction(const FTransform& Transform)
{
	Super::OnConstruction(Transform);
	ApplyKind();
}

void AHrotMissile::BeginPlay()
{
	Super::BeginPlay();
	ApplyKind();
	bHasLast = false;
}

void AHrotMissile::ApplyKind()
{
	if (!Kind) return;
	if (Body->GetStaticMesh() != Kind->Mesh) Body->SetStaticMesh(Kind->Mesh);
	Body->SetRelativeTransform(Kind->MeshTransform);
}

AHrotMissile* AHrotMissile::SpawnMissile(UObject* WorldContext, UHrotMissileKind* NewKind, FTransform Where)
{
	UWorld* W = WorldContext ? WorldContext->GetWorld() : nullptr;
	if (!W) return nullptr;
	AHrotMissile* M = W->SpawnActorDeferred<AHrotMissile>(AHrotMissile::StaticClass(), Where);
	if (!M) return nullptr;
	M->Kind = NewKind;
	M->FinishSpawning(Where);
	return M;
}

void AHrotMissile::Launch()
{
	bLaunched = true;
	UHrotShotRenderer* R = UHrotShotRenderer::Get(this);
	if (!R || !Kind || Kind->LaunchBlast <= 0.f) return;
	const FVector Nose = GetActorForwardVector(), Tail = GetActorLocation() - Nose * (Kind->Length * 0.5f);
	const float B = Kind->LaunchBlast;
	// a cone of smoke and dust thrown BACK from the tail (the backblast), a bright flash at the tail
	for (int32 I = 0; I < 14; ++I)
	{
		const float D = (I + 1) / 14.f;
		const FVector Out = (-Nose + FMath::VRand() * 0.45f).GetSafeNormal();
		R->AddPuff(Tail + Out * B * 2.5f * D, B * (0.4f + 0.6f * D), B * (1.5f + 1.5f * D), 2.f + 3.f * D, 0.75f);
	}
	R->AddFlash(FTransform((-Nose).Rotation(), Tail), B / 40.f, 0.12f);
}

void AHrotMissile::Detonate()
{
	if (bDone) return;
	bDone = true; bMotor = false;
	Body->SetVisibility(false);
	UHrotExplosionFx::Explode(this, GetActorLocation(), FVector::UpVector, Kind ? Kind->Blast : EHrotBlast::Rocket);
}

void AHrotMissile::FlyTest(FVector To, float Speed, float ArcHeight, float MotorSeconds, float Delay, bool bDetonate)
{
	bTest = true; TestFrom = GetActorLocation(); TestTo = To; TestSpeed = FMath::Max(Speed, 1.f); TestArc = ArcHeight;
	TestMotor = MotorSeconds; TestDelay = Delay; TestClock = 0.f; bTestDetonate = bDetonate;
	bMotor = false; bHasLast = false; bLaunched = false;
}

void AHrotMissile::Tick(float DeltaTime)
{
	Super::Tick(DeltaTime);
	if (bTest && !bDone)   // the lab's stand-in for HROT: a quadratic arc From → To, the nose along the velocity
	{
		TestClock += DeltaTime;
		const float Fly = TestClock - TestDelay;
		if (Fly < 0.f) { bMotor = false; return; }
		const float Dist = FVector::Dist(TestFrom, TestTo), Dur = Dist / TestSpeed;
		const float U = FMath::Clamp(Fly / Dur, 0.f, 1.f);
		const FVector Mid = (TestFrom + TestTo) * 0.5f + FVector(0.f, 0.f, 2.f * TestArc);
		auto At = [&](float S) { return FMath::Lerp(FMath::Lerp(TestFrom, Mid, S), FMath::Lerp(Mid, TestTo, S), S); };
		const FVector P = At(U), Ahead = At(FMath::Min(U + 0.01f, 1.f)) - At(FMath::Max(U - 0.01f, 0.f));
		SetActorLocationAndRotation(P, Ahead.Rotation());
		if (!bLaunched) Launch();
		bMotor = TestMotor <= 0.f || Fly < TestMotor;
		if (U >= 1.f) { if (bTestDetonate) Detonate(); else bDone = true; return; }
	}
	if (!Kind || bDone) return;
	UHrotShotRenderer* R = UHrotShotRenderer::Get(this);
	const FVector Nose = GetActorForwardVector();
	const FVector Tail = GetActorLocation() - Nose * (Kind->Length * 0.5f);
	if (bMotor && R)
	{
		// the motor's flame: flash cards pointing BACK from the tail, alive a little longer than a frame (redrawn every frame)
		R->AddFlash(FTransform((-Nose).Rotation(), Tail), Kind->FlameSize, FMath::Max(DeltaTime * 1.5f, 0.02f));
		// the trail: a puff every PuffSpacing cm along the path the tail took since the last frame (a fast missile: several a frame)
		if (bHasLast)
		{
			const FVector Step = Tail - LastTail;
			const float Len = Step.Size();
			Carry += Len;
			const float Space = FMath::Max(Kind->PuffSpacing, 5.f);
			while (Carry >= Space)
			{
				Carry -= Space;
				const FVector At = Tail - Step.GetSafeNormal() * Carry;   // back along this frame's path
				const FVector Jitter = FMath::VRand() * Kind->PuffSize * 0.15f;
				R->AddPuff(At + Jitter, Kind->PuffSize, Kind->PuffSize * Kind->PuffGrowth, Kind->PuffLife, Kind->PuffBrightness);
			}
		}
	}
	LastTail = Tail; bHasLast = true;
}
