using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using ColossalFramework;
using HarmonyLib;
using UnityEngine;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch(typeof(NetManager.Data), "Serialize")]
internal static class NetManagerSerializePatch
{
	private static DateTime _serializeStartedUtc;

	private static void Prefix()
	{
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		_serializeStartedUtc = DateTime.UtcNow;
		Logging.Info(string.Concat("MNC SAVE NetManager.Serialize BEGIN | saveExpanded=", PatchState.SaveExpanded, " | metadataExpanded=", Metadata.IsExpandedSave(), " | updateMode=", GetUpdateMode(), " | nodes=", GetNodeBufferLength(), " | segments=", GetSegmentBufferLength(), " | lanes=", GetLaneBufferLength(), " | nodeWriteSize=", Capacity.GetNodeWriteSize(), " | segmentWriteSize=", Capacity.GetSegmentWriteSize()));
	}

	private static void Postfix()
	{
		Logging.Info("MNC SAVE NetManager.Serialize END | elapsedMs=" + (DateTime.UtcNow - _serializeStartedUtc).TotalMilliseconds + " | saveExpanded=" + PatchState.SaveExpanded + " | metadataExpanded=" + Metadata.IsExpandedSave() + " | nodes=" + GetNodeBufferLength() + " | segments=" + GetSegmentBufferLength() + " | nodeWriteSize=" + Capacity.GetNodeWriteSize() + " | segmentWriteSize=" + Capacity.GetSegmentWriteSize());
	}

	private static Exception Finalizer(Exception __exception)
	{
		if (__exception != null)
		{
			Logging.Error("MNC SAVE NetManager.Serialize EXCEPTION: " + __exception);
		}
		return __exception;
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
		int num = ReplaceBufferWriteSize(list, fieldInfo, fieldInfo3, AccessTools.Method(typeof(Capacity), "GetNodeWriteSize", (Type[])null, (Type[])null));
		int num2 = ReplaceBufferWriteSize(list, fieldInfo2, fieldInfo4, AccessTools.Method(typeof(Capacity), "GetSegmentWriteSize", (Type[])null, (Type[])null));
		Logging.Info("Network save sizes patched: nodes=" + num + ", segments=" + num2 + ", expanded=" + PatchState.SaveExpanded + ".");
		return list;
	}

	private static int ReplaceBufferWriteSize(List<CodeInstruction> instructions, FieldInfo managerField, FieldInfo bufferField, MethodInfo writeSizeMethod)
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
				int num = FindLoadLocal(instructions, i + 3, val);
				if (num >= 0 && num + 1 < instructions.Count && !(instructions[num + 1].opcode != OpCodes.Ldlen))
				{
					CodeInstruction val2 = instructions[num + 1];
					val2.opcode = OpCodes.Call;
					val2.operand = writeSizeMethod;
					instructions[num].opcode = OpCodes.Nop;
					instructions[num].operand = null;
					return 1;
				}
			}
		}
		Logging.Error("Could not patch Data.Serialize size for " + managerField.Name);
		return 0;
	}

	private static int GetNodeBufferLength()
	{
		NetManager instance = Singleton<NetManager>.instance;
		if (!((Object)(object)instance == (Object)null) && instance.m_nodes != null && instance.m_nodes.m_buffer != null)
		{
			return instance.m_nodes.m_buffer.Length;
		}
		return -1;
	}

	private static int GetSegmentBufferLength()
	{
		NetManager instance = Singleton<NetManager>.instance;
		if (!((Object)(object)instance == (Object)null) && instance.m_segments != null && instance.m_segments.m_buffer != null)
		{
			return instance.m_segments.m_buffer.Length;
		}
		return -1;
	}

	private static int GetLaneBufferLength()
	{
		NetManager instance = Singleton<NetManager>.instance;
		if (!((Object)(object)instance == (Object)null) && instance.m_lanes != null && instance.m_lanes.m_buffer != null)
		{
			return instance.m_lanes.m_buffer.Length;
		}
		return -1;
	}

	private static SimulationManager.UpdateMode GetUpdateMode()
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		SimulationManager instance = Singleton<SimulationManager>.instance;
		if ((Object)(object)instance == (Object)null || instance.m_metaData == null)
		{
			return (SimulationManager.UpdateMode)(-1);
		}
		return instance.m_metaData.m_updateMode;
	}

	private static int FindLoadLocal(List<CodeInstruction> instructions, int start, CodeInstruction store)
	{
		if (!TryGetStoreLocalIndex(store, out var index))
		{
			return -1;
		}
		for (int i = start; i < instructions.Count; i++)
		{
			if (IsLoadLocal(instructions[i], index))
			{
				return i;
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
}

}
