using HarmonyLib;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch(typeof(SimulationManager), "SimulationStep")]
internal static class SimulationStepDiagnosticsFramePatch
{
	[HarmonyPriority(800)]
	private static void Prefix()
	{
		SimulationStepDiagnostics.RecordFrameEnter();
	}

	[HarmonyPriority(800)]
	private static void Postfix()
	{
		SimulationStepDiagnostics.RecordFrameExit();
	}
}

}
