using ColossalFramework;
using HarmonyLib;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch(typeof(SerializableDataWrapper), "OnLoadData")]
internal static class PendingUpdateResetPatch
{
	[HarmonyPriority(0)]
	private static void Postfix()
	{
		NetManager instance = Singleton<NetManager>.instance;
		NetManagerCustomizer.ResetPendingUpdatesAfterLoad(instance);
	}
}

}
