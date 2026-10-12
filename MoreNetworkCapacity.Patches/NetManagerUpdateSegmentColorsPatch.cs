using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch]
internal static class NetManagerUpdateSegmentColorsPatch
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		MethodBase target = NetManagerColorUpdatePatch.FindPoolRefreshMethod("UpdateSegmentColors");
		if ((object)target != null)
		{
			yield return target;
		}
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		return NetManagerColorUpdatePatch.ReplaceRefreshMax(instructions, 36863, "m_colorUpdateMaxSegment", "GetSegmentColorUpdateMax", "NetManager.UpdateSegmentColors()");
	}
}

}
