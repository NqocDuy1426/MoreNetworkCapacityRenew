using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using ColossalFramework;
using HarmonyLib;
using MoreNetworkCapacity.Compatibility;
using UnityEngine;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch(typeof(NetManager.Data), "Deserialize")]
internal static class NetManagerDeserializePatch
{
	[HarmonyPriority(800)]
	private static void Prefix()
	{
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		NetManager instance = Singleton<NetManager>.instance;
		bool flag = Metadata.IsExpandedSave();
		bool flag2 = NetManagerCustomizer.IsGameplayLoadMode();
		bool flag3 = false;
		if (PatchState.ExpansionEnabled && (Object)(object)instance != (Object)null)
		{
			if (flag)
			{
				flag3 = NetManagerCustomizer.PrepareExpandedSaveForLoad(instance);
			}
			else if (flag2)
			{
				NetManagerCustomizer.PrepareVanillaSaveForLoad(instance);
			}
		}
		if (flag3)
		{
			ThirdPartyCapacityCompatibility.PrepareForNetworkDeserialize(HarmonyBootstrap.HarmonyInstance, expandedSave: true);
		}
		NetManagerDeserializeState.SetLoadingExpanded(flag && flag3);
		Logging.Info(string.Concat("Network load preparation: save expanded=", flag, ", update mode=", GetUpdateMode(), ", gameplay=", flag2, ", arrays expanded=", flag3, ", loading expanded=", NetManagerDeserializeState.LoadingExpanded, "."));
	}

	[HarmonyPriority(0)]
	private static void Postfix()
	{
		NetManager instance = Singleton<NetManager>.instance;
		if (!((Object)(object)instance == (Object)null) && PatchState.ExpansionEnabled && NetManagerCustomizer.IsGameplayLoadMode() && (NetManagerDeserializeState.LoadingExpanded || NetManagerCustomizer.ExpandPreservingLoadedData(instance)))
		{
			NetManagerCustomizer.RebuildFreeLists(instance);
			NetManagerCustomizer.UpdateLoopLimitsForLoadedData(instance);
		}
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = new List<CodeInstruction>(instructions);
		FieldInfo fieldInfo = AccessTools.Field(typeof(NetManager), "m_nodes");
		FieldInfo fieldInfo2 = AccessTools.Field(typeof(NetManager), "m_segments");
		FieldInfo fieldInfo3 = (((object)fieldInfo == null) ? null : AccessTools.Field(fieldInfo.FieldType, "m_buffer"));
		FieldInfo fieldInfo4 = (((object)fieldInfo2 == null) ? null : AccessTools.Field(fieldInfo2.FieldType, "m_buffer"));
		if ((object)fieldInfo3 == null || (object)fieldInfo4 == null)
		{
			Logging.Error("Could not resolve NetManager buffer fields.");
			return list;
		}
		int num = ReplaceBufferReadSize(list, fieldInfo, fieldInfo3, AccessTools.Method(typeof(Capacity), "GetNodeReadSize", (Type[])null, (Type[])null));
		int num2 = ReplaceBufferReadSize(list, fieldInfo2, fieldInfo4, AccessTools.Method(typeof(Capacity), "GetSegmentReadSize", (Type[])null, (Type[])null));
		Logging.Info("Network load sizes patched: nodes=" + num + ", segments=" + num2 + ", expanded=" + NetManagerDeserializeState.LoadingExpanded + ".");
		return list;
	}

	private static int ReplaceBufferReadSize(List<CodeInstruction> instructions, FieldInfo managerField, FieldInfo bufferField, MethodInfo readSizeMethod)
	{
		for (int i = 0; i + 2 < instructions.Count; i++)
		{
			if (!IsFieldLoad(instructions[i], managerField) || !IsFieldLoad(instructions[i + 1], bufferField))
			{
				continue;
			}
			CodeInstruction val = instructions[i + 2];
			if (IsStoreLocal(val))
			{
				int num = FindLoadLocal(instructions, i + 3, val, forward: true);
				if (num >= 0 && num + 1 < instructions.Count && !(instructions[num + 1].opcode != OpCodes.Ldlen))
				{
					CodeInstruction val2 = instructions[num + 1];
					val2.opcode = OpCodes.Call;
					val2.operand = readSizeMethod;
					instructions[num].opcode = OpCodes.Nop;
					instructions[num].operand = null;
					return 1;
				}
			}
		}
		Logging.Error("Could not patch Data.Deserialize size for " + managerField.Name);
		return 0;
	}

	private static int FindLoadLocal(List<CodeInstruction> instructions, int start, CodeInstruction store, bool forward)
	{
		if (!TryGetStoreLocalIndex(store, out var index))
		{
			return -1;
		}
		if (forward)
		{
			for (int i = start; i < instructions.Count; i++)
			{
				if (IsLoadLocal(instructions[i], index))
				{
					return i;
				}
			}
		}
		else
		{
			for (int num = start; num >= 0; num--)
			{
				if (IsLoadLocal(instructions[num], index))
				{
					return num;
				}
			}
		}
		return -1;
	}

	private static bool IsStoreLocal(CodeInstruction instruction)
	{
		if (!(instruction.opcode == OpCodes.Stloc_0) && !(instruction.opcode == OpCodes.Stloc_1) && !(instruction.opcode == OpCodes.Stloc_2) && !(instruction.opcode == OpCodes.Stloc_3) && !(instruction.opcode == OpCodes.Stloc))
		{
			return instruction.opcode == OpCodes.Stloc_S;
		}
		return true;
	}

	private static bool TryGetStoreLocalIndex(CodeInstruction instruction, out int index)
	{
		if (instruction.opcode == OpCodes.Stloc_0)
		{
			index = 0;
			return true;
		}
		if (instruction.opcode == OpCodes.Stloc_1)
		{
			index = 1;
			return true;
		}
		if (instruction.opcode == OpCodes.Stloc_2)
		{
			index = 2;
			return true;
		}
		if (instruction.opcode == OpCodes.Stloc_3)
		{
			index = 3;
			return true;
		}
		if (instruction.operand is LocalBuilder localBuilder)
		{
			index = localBuilder.LocalIndex;
			return true;
		}
		index = -1;
		return false;
	}

	private static bool IsLoadLocal(CodeInstruction instruction, int index)
	{
		if (instruction.opcode == OpCodes.Ldloc_0)
		{
			return index == 0;
		}
		if (instruction.opcode == OpCodes.Ldloc_1)
		{
			return index == 1;
		}
		if (instruction.opcode == OpCodes.Ldloc_2)
		{
			return index == 2;
		}
		if (instruction.opcode == OpCodes.Ldloc_3)
		{
			return index == 3;
		}
		if (instruction.operand is LocalBuilder localBuilder)
		{
			return localBuilder.LocalIndex == index;
		}
		return false;
	}

	private static bool IsFieldLoad(CodeInstruction instruction, FieldInfo expected)
	{
		if (instruction.opcode != OpCodes.Ldfld)
		{
			return false;
		}
		if (instruction.operand is FieldInfo fieldInfo)
		{
			if ((object)fieldInfo != expected)
			{
				if (fieldInfo.Name == expected.Name)
				{
					return (object)fieldInfo.DeclaringType == expected.DeclaringType;
				}
				return false;
			}
			return true;
		}
		return false;
	}

	private static SimulationManager.UpdateMode GetUpdateMode()
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		SimulationManager instance = Singleton<SimulationManager>.instance;
		if ((Object)(object)instance == (Object)null || instance.m_metaData == null)
		{
			return (SimulationManager.UpdateMode)(-1);
		}
		return instance.m_metaData.m_updateMode;
	}
}

}
