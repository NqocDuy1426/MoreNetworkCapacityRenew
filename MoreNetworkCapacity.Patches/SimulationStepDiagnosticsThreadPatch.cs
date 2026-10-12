using HarmonyLib;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch(typeof(SimulationManager), "SimulationThread")]
internal static class SimulationStepDiagnosticsThreadPatch
{
	private static void Prefix()
	{
		SimulationStepDiagnostics.RecordSimulationThreadEnter();
	}
}

}
