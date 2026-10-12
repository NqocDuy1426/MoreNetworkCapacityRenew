using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace MoreNetworkCapacity.Patches {

internal static class AuxiliaryArrayPatchUtility
{
	public static List<CodeInstruction> ExpandArrays(MethodBase original, IEnumerable<CodeInstruction> instructions, int methodToken, int[,] replacements, string description)
	{
		ValidateMethod(original, methodToken);
		List<CodeInstruction> list = new List<CodeInstruction>(instructions);
		List<int> list2 = FindArrayAllocationIndexes(list, 32768);
		if (list2.Count != replacements.GetLength(0))
		{
			Logging.Error(description + " array allocation pattern mismatch: expected " + replacements.GetLength(0) + ", found " + list2.Count + " in " + list.Count + " instructions. Leaving arrays unchanged.");
			return list;
		}
		for (int i = 0; i < list2.Count; i++)
		{
			CodeInstruction val = list[list2[i]];
			val.opcode = OpCodes.Ldc_I4;
			val.operand = 65024;
		}
		Logging.Info(description + " node-indexed arrays expanded in " + list2.Count + " locations.");
		return list;
	}

	public static List<CodeInstruction> ExpandSimulationSlices(MethodBase original, IEnumerable<CodeInstruction> instructions, int methodToken, int[,] replacements, string description)
	{
		ValidateMethod(original, methodToken);
		List<CodeInstruction> list = new List<CodeInstruction>(instructions);
		FieldInfo operand = AccessTools.Field(typeof(Capacity), "NodeLoopLimit");
		List<int> list2 = FindSimulationSliceIndexes(list);
		if (list2.Count != replacements.GetLength(0))
		{
			Logging.Error(description + " simulation slice pattern mismatch: expected " + replacements.GetLength(0) + ", found " + list2.Count + " in " + list.Count + " instructions. Leaving slices unchanged.");
			return list;
		}
		for (int i = 0; i < list2.Count; i++)
		{
			CodeInstruction val = list[list2[i]];
			val.opcode = OpCodes.Ldsfld;
			val.operand = operand;
		}
		Logging.Info(description + " simulation slices patched in " + list2.Count + " locations.");
		return list;
	}

	private static void ValidateMethod(MethodBase original, int methodToken)
	{
		if (original.MetadataToken != methodToken)
		{
			Logging.Warning("Runtime method token differs for " + original.DeclaringType.FullName + "." + original.Name + " (expected " + methodToken + ", found " + original.MetadataToken + "). Matching by method identity.");
		}
	}

	private static List<int> FindArrayAllocationIndexes(List<CodeInstruction> instructions, int capacity)
	{
		List<int> list = new List<int>();
		for (int i = 0; i + 2 < instructions.Count; i++)
		{
			if (IsInt32Constant(instructions[i], capacity) && !(instructions[i + 1].opcode != OpCodes.Newarr) && !(instructions[i + 2].opcode != OpCodes.Stfld))
			{
				list.Add(i);
			}
		}
		return list;
	}

	private static List<int> FindSimulationSliceIndexes(List<CodeInstruction> instructions)
	{
		List<int> list = new List<int>();
		for (int i = 1; i + 3 < instructions.Count; i++)
		{
			if (IsInt32Constant(instructions[i], 32768) && !(instructions[i + 1].opcode != OpCodes.Mul) && IsInt32Constant(instructions[i + 2], 7) && !(instructions[i + 3].opcode != OpCodes.Shr) && (IsLoadLocal(instructions[i - 1]) || (i >= 3 && IsLoadLocal(instructions[i - 3]) && IsInt32Constant(instructions[i - 2], 1) && !(instructions[i - 1].opcode != OpCodes.Add))))
			{
				list.Add(i);
			}
		}
		return list;
	}

	private static bool IsLoadLocal(CodeInstruction instruction)
	{
		if (!(instruction.opcode == OpCodes.Ldloc_0) && !(instruction.opcode == OpCodes.Ldloc_1) && !(instruction.opcode == OpCodes.Ldloc_2) && !(instruction.opcode == OpCodes.Ldloc_3) && !(instruction.opcode == OpCodes.Ldloc))
		{
			return instruction.opcode == OpCodes.Ldloc_S;
		}
		return true;
	}

	private static bool IsInt32Constant(CodeInstruction instruction, int value)
	{
		if (instruction.opcode == OpCodes.Ldc_I4_M1)
		{
			return value == -1;
		}
		if (instruction.opcode == OpCodes.Ldc_I4_0)
		{
			return value == 0;
		}
		if (instruction.opcode == OpCodes.Ldc_I4_1)
		{
			return value == 1;
		}
		if (instruction.opcode == OpCodes.Ldc_I4_2)
		{
			return value == 2;
		}
		if (instruction.opcode == OpCodes.Ldc_I4_3)
		{
			return value == 3;
		}
		if (instruction.opcode == OpCodes.Ldc_I4_4)
		{
			return value == 4;
		}
		if (instruction.opcode == OpCodes.Ldc_I4_5)
		{
			return value == 5;
		}
		if (instruction.opcode == OpCodes.Ldc_I4_6)
		{
			return value == 6;
		}
		if (instruction.opcode == OpCodes.Ldc_I4_7)
		{
			return value == 7;
		}
		if (instruction.opcode == OpCodes.Ldc_I4_8)
		{
			return value == 8;
		}
		if (instruction.opcode == OpCodes.Ldc_I4 || instruction.opcode == OpCodes.Ldc_I4_S)
		{
			if (instruction.operand is int)
			{
				return (int)instruction.operand == value;
			}
			if (instruction.operand is sbyte)
			{
				return (sbyte)instruction.operand == value;
			}
			if (instruction.operand is byte)
			{
				return (byte)instruction.operand == value;
			}
			if (instruction.operand is short)
			{
				return (short)instruction.operand == value;
			}
		}
		return false;
	}
}

}
