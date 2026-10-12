using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch(typeof(ElectricityManager), "Awake")]
internal static class ElectricityManagerAwakePatch
{
	private static readonly int[,] Replacements = new int[2, 2]
	{
		{ 11, 39 },
		{ 15, 55 }
	};

	private static IEnumerable<CodeInstruction> Transpiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
	{
		return AuxiliaryArrayPatchUtility.ExpandArrays(original, instructions, 100676087, Replacements, "Electricity");
	}
}

}
