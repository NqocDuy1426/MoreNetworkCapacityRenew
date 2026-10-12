using HarmonyLib;
using UnityEngine;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch(typeof(SimulationManager), "RegisterManager")]
internal static class SimulationStepDiagnosticsRegistrationPatch
{
	private static void Postfix(Object manager)
	{
		SimulationStepDiagnostics.RecordManagerRegistered(manager);
	}
}

}
