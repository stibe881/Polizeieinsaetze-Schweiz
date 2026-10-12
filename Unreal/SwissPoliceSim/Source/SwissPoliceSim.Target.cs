using UnrealBuildTool;
using System.Collections.Generic;

public class SwissPoliceSimTarget : TargetRules
{
	public SwissPoliceSimTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Game;
		DefaultBuildSettings = BuildSettingsVersion.Latest;   // folgt der jeweiligen Engine (5.4 und neuer); feste Werte lehnt UBT bei Editor-Targets neuerer Engines ab
		IncludeOrderVersion = EngineIncludeOrderVersion.Latest;
		ExtraModuleNames.Add("SwissPoliceSim");
	}
}
