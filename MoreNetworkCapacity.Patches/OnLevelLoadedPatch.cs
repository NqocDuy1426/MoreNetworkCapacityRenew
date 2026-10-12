using ColossalFramework;
using HarmonyLib;
using MoreNetworkCapacity.Compatibility;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch(typeof(LoadingWrapper), "OnLevelLoaded")]
internal static class OnLevelLoadedPatch
{
	private static void Postfix()
	{
		NetManager instance = Singleton<NetManager>.instance;
		bool flag = Metadata.IsExpandedSave();
		if (!NetManagerCustomizer.IsGameplayMode())
		{
			NetManagerCustomizer.CancelDeferredLoopLimitActivation();
			Metadata.ClearExpandedSave();
			NetManagerDeserializeState.ResetLoadingExpanded();
			return;
		}
		if (!NetManagerCustomizer.IsExpanded(instance))
		{
			NetManagerCustomizer.CancelDeferredLoopLimitActivation();
			Logging.Error("Network arrays were not expanded during deserialization. Leaving the loaded network untouched.");
			Metadata.ClearExpandedSave();
			NetManagerDeserializeState.ResetLoadingExpanded();
			return;
		}
		NetManagerCustomizer.UpdateLoopLimitsForLoadedData(instance);
		Metadata.SetExpandedSave();
		if (flag)
		{
			NetManagerCustomizer.UpdateLoopLimits(instance);
			NetManagerCustomizer.CancelDeferredLoopLimitActivation();
			Logging.Info("Activated expanded loop limits immediately for a marked expanded save: " + Capacity.NodeLoopLimit + "/" + Capacity.SegmentLoopLimit + ".");
			SimulationStepDiagnostics.BeginMonitoring(HarmonyBootstrap.HarmonyInstance, instance);
		}
		ThirdPartyCapacityCompatibility.ApplyAll(HarmonyBootstrap.HarmonyInstance);
		NetworkColorOffsetPatch.ApplyPostLoadFinalPass(HarmonyBootstrap.HarmonyInstance);
		RenderManagerAwakePatch.EnsureCapacity();
		NetManagerColorUpdatePatch.RefreshExpandedColors();
		NetManagerDeserializeState.ResetLoadingExpanded();
		if (!flag && Capacity.NodeLoopLimit == 32768 && Capacity.SegmentLoopLimit == 36864)
		{
			NetManagerCustomizer.DeferLoopLimitActivation(instance);
		}
		else
		{
			NetManagerCustomizer.CancelDeferredLoopLimitActivation();
		}
		Logging.Info("Network load completed with expanded arrays.");
	}
}

}
