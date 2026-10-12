using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch(typeof(WaterManager), "SimulationStepImpl")]
internal static class WaterManagerSimulationStepPatch
{
	private static readonly int[,] Replacements = new int[2, 2]
	{
		{ 56, 144 },
		{ 64, 156 }
	};

	private static IEnumerable<CodeInstruction> Transpiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
	{
		return AuxiliaryArrayPatchUtility.ExpandSimulationSlices(original, instructions, 100676625, Replacements, "Water");
	}
}

}
