using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch(typeof(ElectricityManager), "SimulationStepImpl")]
internal static class ElectricityManagerSimulationStepPatch
{
	private static readonly int[,] Replacements = new int[2, 2]
	{
		{ 38, 102 },
		{ 46, 114 }
	};

	private static IEnumerable<CodeInstruction> Transpiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
	{
		return AuxiliaryArrayPatchUtility.ExpandSimulationSlices(original, instructions, 100676105, Replacements, "Electricity");
	}
}

}
