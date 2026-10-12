using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch(typeof(CoverageManager), "Awake")]
internal static class CoverageManagerAwakePatch
{
	private static readonly int[,] Replacements = new int[2, 2]
	{
		{ 6, 18 },
		{ 10, 34 }
	};

	private static IEnumerable<CodeInstruction> Transpiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
	{
		return AuxiliaryArrayPatchUtility.ExpandArrays(original, instructions, 100675981, Replacements, "Coverage");
	}
}

}
