using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch(typeof(WaterManager), "Awake")]
internal static class WaterManagerAwakePatch
{
	private static readonly int[,] Replacements = new int[4, 2]
	{
		{ 19, 71 },
		{ 23, 87 },
		{ 27, 103 },
		{ 31, 119 }
	};

	private static IEnumerable<CodeInstruction> Transpiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
	{
		return AuxiliaryArrayPatchUtility.ExpandArrays(original, instructions, 100676587, Replacements, "Water");
	}
}

}
