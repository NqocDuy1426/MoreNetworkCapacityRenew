using HarmonyLib;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch(typeof(AsyncTask), "Execute")]
internal static class SimulationStepDiagnosticsAsyncTaskPatch
{
	private static void Prefix(AsyncTask __instance)
	{
		SimulationStepDiagnostics.RecordAsyncTaskEnter((AsyncTaskBase)(object)__instance);
	}

	private static void Postfix(AsyncTask __instance)
	{
		SimulationStepDiagnostics.RecordAsyncTaskExit((AsyncTaskBase)(object)__instance);
	}
}

}
