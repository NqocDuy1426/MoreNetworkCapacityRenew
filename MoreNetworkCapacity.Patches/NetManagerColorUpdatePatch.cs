using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using ColossalFramework;
using HarmonyLib;
using UnityEngine;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch(typeof(NetManager), "UpdateColorMap")]
internal static class NetManagerColorUpdatePatch
{
	private static void Prefix()
	{
		SimulationStepDiagnostics.RecordColorUpdateEnter();
	}

	private static void Postfix()
	{
		SimulationStepDiagnostics.RecordColorUpdateExit();
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = new List<CodeInstruction>(instructions);
		FieldInfo operand = AccessTools.Field(typeof(Capacity), "NodeLoopLimit");
		FieldInfo operand2 = AccessTools.Field(typeof(Capacity), "SegmentLoopLimit");
		int num = 0;
		for (int i = 0; i + 1 < list.Count; i++)
		{
			if (IsAnyInt32Constant(list[i]) && !(list[i + 1].opcode != OpCodes.Stfld) && list[i + 1].operand is FieldInfo fieldInfo && list[i].operand is int)
			{
				int num2 = (int)list[i].operand;
				if (num2 == 32768 && fieldInfo.Name == "m_colorUpdateMinNode")
				{
					list[i].opcode = OpCodes.Ldsfld;
					list[i].operand = operand;
					num++;
				}
				else if (num2 == 36864 && fieldInfo.Name == "m_colorUpdateMinSegment")
				{
					list[i].opcode = OpCodes.Ldsfld;
					list[i].operand = operand2;
					num++;
				}
			}
		}
		Logging.Info("Color update ranges patched: " + num);
		return list;
	}

	internal static void RefreshExpandedColors()
	{
		NetManager instance = Singleton<NetManager>.instance;
		if ((Object)(object)instance == (Object)null)
		{
			return;
		}
		bool flag = Capacity.NodeLoopLimit > 32768;
		bool flag2 = Capacity.SegmentLoopLimit > 36864;
		if (flag || flag2)
		{
			if (flag)
			{
				instance.UpdateNodeColors((ushort)0, (ushort)(Capacity.NodeLoopLimit - 1));
			}
			if (flag2)
			{
				instance.UpdateSegmentColors((ushort)0, (ushort)(Capacity.SegmentLoopLimit - 1));
			}
			Logging.Info("Queued a full color refresh for the expanded pools: nodes=" + (flag ? Capacity.NodeLoopLimit : 0) + ", segments=" + (flag2 ? Capacity.SegmentLoopLimit : 0) + ".");
		}
	}

	internal static IEnumerable<CodeInstruction> ReplaceRefreshMax(IEnumerable<CodeInstruction> instructions, int vanillaMax, string fieldName, string helperName, string label)
	{
		List<CodeInstruction> list = new List<CodeInstruction>(instructions);
		MethodInfo method = typeof(Capacity).GetMethod(helperName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
		if ((object)method == null)
		{
			Logging.Error("Could not find Capacity." + helperName + "(); leaving " + label + " unchanged.");
			return list;
		}
		int num = 0;
		for (int i = 0; i + 1 < list.Count; i++)
		{
			if (IsInt32Constant(list[i], vanillaMax) && !(list[i + 1].opcode != OpCodes.Stfld) && list[i + 1].operand is FieldInfo fieldInfo && !(fieldInfo.Name != fieldName))
			{
				list[i].opcode = OpCodes.Call;
				list[i].operand = method;
				num++;
			}
		}
		Logging.Info("Color refresh range patched in " + label + ": " + num);
		return list;
	}

	private static bool IsAnyInt32Constant(CodeInstruction instruction)
	{
		if (instruction.opcode == OpCodes.Ldc_I4 || instruction.opcode == OpCodes.Ldc_I4_S)
		{
			return instruction.operand is int;
		}
		return false;
	}

	internal static bool IsInt32Constant(CodeInstruction instruction, int value)
	{
		if (IsAnyInt32Constant(instruction))
		{
			return (int)instruction.operand == value;
		}
		return false;
	}

	internal static MethodBase FindPoolRefreshMethod(string methodName)
	{
		MethodInfo[] methods = typeof(NetManager).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		for (int i = 0; i < methods.Length; i++)
		{
			if (methods[i].Name == methodName && methods[i].GetParameters().Length == 0)
			{
				return methods[i];
			}
		}
		Logging.Error("Could not find the no-argument overload NetManager." + methodName + "().");
		return null;
	}
}

}
