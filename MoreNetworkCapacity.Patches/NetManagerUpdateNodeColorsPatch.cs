using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch]
internal static class NetManagerUpdateNodeColorsPatch
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		MethodBase target = NetManagerColorUpdatePatch.FindPoolRefreshMethod("UpdateNodeColors");
		if ((object)target != null)
		{
			yield return target;
		}
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		return NetManagerColorUpdatePatch.ReplaceRefreshMax(instructions, 32767, "m_colorUpdateMaxNode", "GetNodeColorUpdateMax", "NetManager.UpdateNodeColors()");
	}
}

}
