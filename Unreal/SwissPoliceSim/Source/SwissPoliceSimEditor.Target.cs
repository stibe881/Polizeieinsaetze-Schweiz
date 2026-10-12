using UnrealBuildTool;
using System.Collections.Generic;

public class SwissPoliceSimEditorTarget : TargetRules
{
	public SwissPoliceSimEditorTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Editor;
		DefaultBuildSettings = BuildSettingsVersion.Latest;   // folgt der jeweiligen Engine (5.4 und neuer); feste Werte lehnt UBT bei Editor-Targets neuerer Engines ab
		IncludeOrderVersion = EngineIncludeOrderVersion.Latest;
		ExtraModuleNames.Add("SwissPoliceSim");
	}
}
