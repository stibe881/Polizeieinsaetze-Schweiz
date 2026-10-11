using UnrealBuildTool;

public class SwissPoliceSim : ModuleRules
{
	public SwissPoliceSim(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
		PublicDependencyModuleNames.AddRange(new string[]
		{
			"Core", "CoreUObject", "Engine", "InputCore", "ProceduralMeshComponent",
			"WebSockets", "HTTP", "Json", "JsonUtilities", "Slate", "SlateCore", "WebBrowser", "ApplicationCore"
		});
	}
}
