using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch(typeof(NetManager), "CheckLimits")]
internal static class CheckLimitsPatch
{
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int)
			{
				switch ((int)instruction.operand)
				{
				case 32256:
					instruction.opcode = OpCodes.Call;
					instruction.operand = AccessTools.Method(typeof(Capacity), "GetNodeStopLimit", (Type[])null, (Type[])null);
					break;
				case 36352:
					instruction.opcode = OpCodes.Call;
					instruction.operand = AccessTools.Method(typeof(Capacity), "GetSegmentStopLimit", (Type[])null, (Type[])null);
					break;
				}
			}
			yield return instruction;
		}
	}
}

}
