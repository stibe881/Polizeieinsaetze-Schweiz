using UnrealBuildTool;
using System.Collections.Generic;

public class SwissPoliceSimEditorTarget : TargetRules
{
	public SwissPoliceSimEditorTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Editor;
		DefaultBuildSettings = BuildSettingsVersion.V5;
		IncludeOrderVersion = EngineIncludeOrderVersion.Unreal5_4;
		ExtraModuleNames.Add("SwissPoliceSim");
	}
}
