using HarmonyLib;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch(typeof(SimulationManager), "FixedUpdate")]
internal static class SimulationStepDiagnosticsFixedUpdatePatch
{
	private static void Prefix()
	{
		SimulationStepDiagnostics.RecordFixedUpdate();
	}
}

}
