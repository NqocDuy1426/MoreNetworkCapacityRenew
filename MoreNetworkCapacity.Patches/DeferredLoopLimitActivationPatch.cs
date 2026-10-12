using HarmonyLib;
using MoreNetworkCapacity.Compatibility;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch(typeof(SimulationManager), "SimulationStep")]
internal static class DeferredLoopLimitActivationPatch
{
	private static void Postfix()
	{
		NetManagerCustomizer.ActivateDeferredLoopLimits();
		ThirdPartyCapacityCompatibility.RetryAdaptiveRoads(HarmonyBootstrap.HarmonyInstance);
	}
}

}
