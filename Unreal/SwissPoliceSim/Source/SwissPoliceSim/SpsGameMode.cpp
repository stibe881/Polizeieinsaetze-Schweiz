#include "SpsGameMode.h"
#include "SpsClient.h"
#include "SpsController.h"
#include "Engine/World.h"

ASpsGameMode::ASpsGameMode()
{
	DefaultPawnClass = nullptr;
	PlayerControllerClass = ASpsController::StaticClass();
	HUDClass = nullptr;
}

void ASpsGameMode::StartPlay()
{
	GetWorld()->SpawnActor<ASpsClient>(FVector::ZeroVector, FRotator::ZeroRotator);
	Super::StartPlay();
}
