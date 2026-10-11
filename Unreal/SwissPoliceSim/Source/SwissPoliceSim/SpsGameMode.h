#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"
#include "SpsGameMode.generated.h"

/** Spawns the server client and uses the Sps controller; there is no default pawn (the client creates the officer and the patrol car). */
UCLASS()
class ASpsGameMode : public AGameModeBase
{
	GENERATED_BODY()

public:
	ASpsGameMode();
	virtual void StartPlay() override;
};
