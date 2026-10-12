using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace MoreNetworkCapacity.Compatibility {

internal static class NodeControllerPatches
{
	public static void EnsureManagerBufferPrefix(object __instance)
	{
		NodeControllerCompatibility.EnsureManagerBuffer(__instance);
	}

	public static void ManagerConstructorPostfix(object __instance)
	{
		NodeControllerCompatibility.EnsureManagerBuffer(__instance);
	}

	public static void ManagerClearPostfix(object __instance)
	{
		NodeControllerCompatibility.EnsureManagerBuffer(__instance);
	}

	public static IEnumerable<CodeInstruction> FromXmlTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int && (int)instruction.operand == 32768)
			{
				instruction.operand = 65023;
			}
			yield return instruction;
		}
	}
}

}
