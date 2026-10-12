using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch]
internal static class NetworkDataSerializationPatch
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		Type electricityData = GetDataType(typeof(ElectricityManager));
		Type waterData = GetDataType(typeof(WaterManager));
		foreach (MethodBase serializationMethod in GetSerializationMethods(electricityData))
		{
			yield return serializationMethod;
		}
		foreach (MethodBase serializationMethod2 in GetSerializationMethods(waterData))
		{
			yield return serializationMethod2;
		}
	}

	private static IEnumerable<CodeInstruction> Transpiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = new List<CodeInstruction>(instructions);
		MethodInfo methodInfo = AccessTools.Method(typeof(Capacity), "GetAuxiliaryNodeStorageSize", (Type[])null, (Type[])null);
		if ((object)methodInfo == null)
		{
			Logging.Error("Could not find Capacity.GetAuxiliaryNodeStorageSize(); leaving auxiliary node serialization unchanged.");
			return list;
		}
		int num = 0;
		for (int i = 0; i + 1 < list.Count; i++)
		{
			if (IsInt32Constant(list[i], 32768) && ShouldReplace(original, list, i))
			{
				list[i].opcode = OpCodes.Call;
				list[i].operand = methodInfo;
				num++;
			}
		}
		if (num > 0)
		{
			Logging.Info("Auxiliary node serialization patched in " + original.DeclaringType.FullName + "." + original.Name + ": " + num);
		}
		return list;
	}

	private static bool ShouldReplace(MethodBase original, List<CodeInstruction> list, int index)
	{
		bool flag = original.DeclaringType.FullName == "ElectricityManager+Data";
		bool flag2 = original.DeclaringType.FullName == "WaterManager+Data";
		bool flag3 = original.Name == "Deserialize";
		if (flag)
		{
			if (IsConditionalBranch(list[index + 1].opcode))
			{
				return HasFieldNear(list, index, "m_nodeGroups", lookBack: true, 32);
			}
			return false;
		}
		if (!flag2)
		{
			return false;
		}
		if (!flag3)
		{
			if (IsConditionalBranch(list[index + 1].opcode))
			{
				return HasFieldNear(list, index, "m_nodeData", lookBack: true, 32);
			}
			return false;
		}
		if (IsConditionalBranch(list[index + 1].opcode) && HasFieldNear(list, index, "m_nodeData", lookBack: true, 32))
		{
			return true;
		}
		if (IsStoreLocal(list[index + 1]))
		{
			return HasFieldNear(list, index, "m_nodeData", lookBack: false, 16);
		}
		return false;
	}

	private static bool HasFieldNear(List<CodeInstruction> list, int index, string fieldName, bool lookBack, int distance)
	{
		int num = (lookBack ? Math.Max(0, index - distance) : index);
		int num2 = (lookBack ? (index - 1) : Math.Min(list.Count - 1, index + distance));
		for (int i = num; i <= num2; i++)
		{
			if (list[i].operand != null && list[i].operand.ToString().Contains(fieldName))
			{
				return true;
			}
		}
		return false;
	}

	private static IEnumerable<MethodBase> GetSerializationMethods(Type dataType)
	{
		if ((object)dataType == null)
		{
			Logging.Warning("Could not find a manager Data type.");
			yield break;
		}
		foreach (MethodInfo method in AccessTools.GetDeclaredMethods(dataType))
		{
			if (method.Name == "Serialize" || method.Name == "Deserialize")
			{
				yield return method;
			}
		}
	}

	private static Type GetDataType(Type managerType)
	{
		return managerType.GetNestedType("Data", BindingFlags.Public | BindingFlags.NonPublic);
	}

	private static bool IsInt32Constant(CodeInstruction instruction, int value)
	{
		if ((instruction.opcode == OpCodes.Ldc_I4 || instruction.opcode == OpCodes.Ldc_I4_S) && instruction.operand is int)
		{
			return (int)instruction.operand == value;
		}
		return false;
	}

	private static bool IsConditionalBranch(OpCode opcode)
	{
		if (!(opcode == OpCodes.Blt) && !(opcode == OpCodes.Blt_Un) && !(opcode == OpCodes.Ble) && !(opcode == OpCodes.Ble_Un) && !(opcode == OpCodes.Bgt) && !(opcode == OpCodes.Bgt_Un) && !(opcode == OpCodes.Bge) && !(opcode == OpCodes.Bge_Un) && !(opcode == OpCodes.Beq) && !(opcode == OpCodes.Beq_S) && !(opcode == OpCodes.Bne_Un))
		{
			return opcode == OpCodes.Bne_Un_S;
		}
		return true;
	}

	private static bool IsStoreLocal(CodeInstruction instruction)
	{
		if (!(instruction.opcode == OpCodes.Stloc_0) && !(instruction.opcode == OpCodes.Stloc_1) && !(instruction.opcode == OpCodes.Stloc_2) && !(instruction.opcode == OpCodes.Stloc_3) && !(instruction.opcode == OpCodes.Stloc))
		{
			return instruction.opcode == OpCodes.Stloc_S;
		}
		return true;
	}
}

}
