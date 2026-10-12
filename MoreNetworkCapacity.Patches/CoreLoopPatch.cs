using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch]
internal static class CoreLoopPatch
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		Assembly assembly = typeof(NetManager).Assembly;
		try
		{
			string[][] branchBoundMethods = GeneratedPatchTargets.BranchBoundMethods;
			foreach (string[] target in branchBoundMethods)
			{
				Type type = assembly.GetType(target[0], throwOnError: false);
				if ((object)type == null)
				{
					Logging.Warning("Could not find patch target type " + target[0] + ".");
					continue;
				}
				MethodBase method = FindTargetMethod(type, target[1], target[2], target[3]);
				if ((object)method != null)
				{
					yield return method;
				}
			}
		}
		finally
		{
		}
	}

	private static IEnumerable<CodeInstruction> Transpiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = new List<CodeInstruction>(instructions);
		List<string[]> occurrences = GetOccurrences(original);
		if (occurrences.Count == 0)
		{
			Logging.Error("No exact loop occurrences are defined for " + original.DeclaringType.FullName + "." + original.Name + ".");
			return list;
		}
		int[] array = ResolveOccurrenceIndexes(original, list, occurrences);
		if (array == null)
		{
			return list;
		}
		FieldInfo operand = AccessTools.Field(typeof(Capacity), "NodeLoopLimit");
		FieldInfo operand2 = AccessTools.Field(typeof(Capacity), "SegmentLoopLimit");
		int num = 0;
		int num2 = 0;
		for (int i = 0; i < occurrences.Count; i++)
		{
			string[] array2 = occurrences[i];
			int num3 = array[i];
			if (!int.TryParse(array2[3], out var result) || (array2[4] != "node" && array2[4] != "segment") || num3 < 0 || num3 >= list.Count)
			{
				throw new InvalidOperationException("Invalid exact loop occurrence for " + original.DeclaringType.FullName + "." + original.Name + ".");
			}
			CodeInstruction val = list[num3];
			if (!IsInt32Constant(val, result))
			{
				throw new InvalidOperationException("IL mismatch for " + original.DeclaringType.FullName + "." + original.Name + " occurrence " + num3 + " (expected constant " + result + ").");
			}
			if (HasNarrowingConversionBefore(list, num3))
			{
				Logging.Warning("Skipping 16-bit bound in " + original.DeclaringType.FullName + "." + original.Name + " occurrence " + num3 + " (constant " + result + ").");
			}
			else if (array2[4] == "node")
			{
				val.opcode = OpCodes.Ldsfld;
				val.operand = operand;
				num++;
			}
			else
			{
				val.opcode = OpCodes.Ldsfld;
				val.operand = operand2;
				num2++;
			}
		}
		Logging.Info("Exact loop bounds patched in " + original.DeclaringType.FullName + "." + original.Name + ": nodes=" + num + ", segments=" + num2 + ".");
		return list;
	}

	private static int[] ResolveOccurrenceIndexes(MethodBase original, List<CodeInstruction> instructions, List<string[]> occurrences)
	{
		int[] array = new int[occurrences.Count];
		bool flag = true;
		for (int i = 0; i < occurrences.Count; i++)
		{
			if (!int.TryParse(occurrences[i][1], out var result) || !int.TryParse(occurrences[i][3], out var result2) || result < 0 || result >= instructions.Count || !IsInt32Constant(instructions[result], result2) || result + 1 >= instructions.Count || instructions[result + 1].opcode.FlowControl != FlowControl.Cond_Branch)
			{
				flag = false;
				break;
			}
			array[i] = result;
		}
		if (flag)
		{
			return array;
		}
		int[] array2 = FindFallbackIndexes(instructions, occurrences);
		if (array2 != null)
		{
			Logging.Warning("Using branch-bound fallback for " + original.DeclaringType.FullName + "." + original.Name + ".");
		}
		else
		{
			Logging.Error("Could not resolve shifted loop occurrences for " + original.DeclaringType.FullName + "." + original.Name + ". Leaving this method unchanged.");
		}
		return array2;
	}

	private static int[] FindFallbackIndexes(List<CodeInstruction> instructions, List<string[]> occurrences)
	{
		Dictionary<string, List<string[]>> dictionary = new Dictionary<string, List<string[]>>();
		for (int i = 0; i < occurrences.Count; i++)
		{
			string occurrenceKey = GetOccurrenceKey(occurrences[i]);
			if (occurrenceKey == null)
			{
				return null;
			}
			if (!dictionary.TryGetValue(occurrenceKey, out var value))
			{
				value = new List<string[]>();
				dictionary.Add(occurrenceKey, value);
			}
			value.Add(occurrences[i]);
		}
		Dictionary<string[], int> dictionary2 = new Dictionary<string[], int>();
		foreach (KeyValuePair<string, List<string[]>> item in dictionary)
		{
			List<string[]> value2 = item.Value;
			value2.Sort(CompareOccurrences);
			int constant = int.Parse(value2[0][3]);
			List<int> list = FindBranchBoundCandidates(instructions, constant);
			if (list.Count != value2.Count)
			{
				return null;
			}
			for (int j = 0; j < value2.Count; j++)
			{
				dictionary2.Add(value2[j], list[j]);
			}
		}
		int[] array = new int[occurrences.Count];
		for (int k = 0; k < occurrences.Count; k++)
		{
			if (!dictionary2.TryGetValue(occurrences[k], out array[k]))
			{
				return null;
			}
		}
		return array;
	}

	private static string GetOccurrenceKey(string[] occurrence)
	{
		if (!int.TryParse(occurrence[3], out var result) || (occurrence[4] != "node" && occurrence[4] != "segment"))
		{
			return null;
		}
		return occurrence[4] + ":" + result;
	}

	private static int CompareOccurrences(string[] left, string[] right)
	{
		if (!int.TryParse(left[1], out var result) || !int.TryParse(right[1], out var result2))
		{
			return string.CompareOrdinal(left[1], right[1]);
		}
		return result.CompareTo(result2);
	}

	private static List<int> FindBranchBoundCandidates(List<CodeInstruction> instructions, int constant)
	{
		List<int> list = new List<int>();
		for (int i = 0; i + 1 < instructions.Count; i++)
		{
			if (IsInt32Constant(instructions[i], constant) && instructions[i + 1].opcode.FlowControl == FlowControl.Cond_Branch)
			{
				list.Add(i);
			}
		}
		return list;
	}

	private static bool HasNarrowingConversionBefore(List<CodeInstruction> instructions, int index)
	{
		int num = Math.Max(0, index - 6);
		for (int i = num; i < index; i++)
		{
			if (instructions[i].opcode == OpCodes.Conv_U2 || instructions[i].opcode == OpCodes.Conv_I2)
			{
				return true;
			}
		}
		return false;
	}

	private static List<string[]> GetOccurrences(MethodBase original)
	{
		int num = FindExpectedToken(original);
		if (num < 0)
		{
			return new List<string[]>();
		}
		return GetOccurrences(num);
	}

	private static List<string[]> GetOccurrences(int token)
	{
		List<string[]> list = new List<string[]>();
		string[][] branchBoundOccurrences = GeneratedPatchTargets.BranchBoundOccurrences;
		foreach (string[] array in branchBoundOccurrences)
		{
			if (int.TryParse(array[0], out var result) && result == token)
			{
				list.Add(array);
			}
		}
		return list;
	}

	private static int FindExpectedToken(MethodBase original)
	{
		string signature = GetSignature(original);
		string[][] branchBoundMethods = GeneratedPatchTargets.BranchBoundMethods;
		foreach (string[] array in branchBoundMethods)
		{
			int result;
			if (array[0] == original.DeclaringType.FullName && array[1] == original.Name && array[3] == signature)
			{
				return int.TryParse(array[2], out result) ? result : (-1);
			}
		}
		return -1;
	}

	private static MethodBase FindTargetMethod(Type type, string name, string tokenText, string signature)
	{
		if (!int.TryParse(tokenText, out var result))
		{
			Logging.Warning("Invalid metadata token for " + type.FullName + "." + name + ": " + tokenText);
			return null;
		}
		MethodBase methodBase = null;
		foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(type))
		{
			if (declaredMethod.Name == name && GetSignature(declaredMethod) == signature)
			{
				if ((object)methodBase != null)
				{
					Logging.Error("Multiple exact loop targets match " + type.FullName + "." + name + "(" + signature + ").");
					return null;
				}
				methodBase = declaredMethod;
			}
		}
		if ((object)methodBase == null)
		{
			Logging.Warning("Could not find exact loop target " + type.FullName + "." + name + "(" + signature + ") [token " + tokenText + "].");
			return null;
		}
		if (methodBase.MetadataToken != result)
		{
			Logging.Warning("Runtime method token differs for " + type.FullName + "." + name + " (expected " + result + ", found " + methodBase.MetadataToken + "). Matching by method identity.");
		}
		return methodBase;
	}

	private static string GetSignature(MethodBase method)
	{
		ParameterInfo[] parameters = method.GetParameters();
		string text = "";
		for (int i = 0; i < parameters.Length; i++)
		{
			if (i != 0)
			{
				text += ",";
			}
			string text2 = parameters[i].ParameterType.FullName;
			if (text2 != null)
			{
				text2 = text2.Replace('+', '/');
			}
			text += text2;
		}
		return text;
	}

	private static bool IsInt32Constant(CodeInstruction instruction, int value)
	{
		if ((instruction.opcode == OpCodes.Ldc_I4 || instruction.opcode == OpCodes.Ldc_I4_S) && instruction.operand is int)
		{
			return (int)instruction.operand == value;
		}
		return false;
	}
}

}
