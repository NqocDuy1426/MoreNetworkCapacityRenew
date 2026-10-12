using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using ColossalFramework;
using HarmonyLib;
using UnityEngine;

namespace MoreNetworkCapacity.Compatibility {

internal static class StaticBufferCompatibility
{
	private static readonly HashSet<string> PatchedKianCommonsTypes = new HashSet<string>();

	internal static void ApplyAll(Harmony harmony)
	{
		NetManager instance = Singleton<NetManager>.instance;
		if ((Object)(object)instance == (Object)null || instance.m_nodes == null || instance.m_segments == null || instance.m_lanes == null || instance.m_nodes.m_buffer == null || instance.m_segments.m_buffer == null || instance.m_lanes.m_buffer == null)
		{
			Logging.Warning("NetManager buffers are unavailable; third-party static buffer refresh is skipped.");
			return;
		}
		BuildingManager instance2 = Singleton<BuildingManager>.instance;
		Array buildingBuffer = null;
		if ((Object)(object)instance2 != (Object)null && instance2.m_buildings != null)
		{
			buildingBuffer = instance2.m_buildings.m_buffer;
		}
		ApplyKianCommonsCompatibility(harmony, instance.m_nodes.m_buffer, instance.m_segments.m_buffer, instance.m_lanes.m_buffer);
		RefreshTMPEBuffers(instance.m_nodes.m_buffer, instance.m_segments.m_buffer, instance.m_lanes.m_buffer);
		RefreshMoveItBuffers(instance.m_nodes.m_buffer, instance.m_segments.m_buffer, buildingBuffer);
	}

	internal static void ApplyEarly(Harmony harmony)
	{
		if (harmony == null)
		{
			return;
		}
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		int num = 0;
		foreach (Assembly assembly in assemblies)
		{
			Type type;
			try
			{
				type = assembly.GetType("KianCommons.NetUtil", throwOnError: false);
			}
			catch
			{
				continue;
			}
			if ((object)type != null)
			{
				PatchKianCommonsLimits(harmony, type);
				num++;
			}
		}
		if (num == 0)
		{
			Logging.Info("KianCommons was not loaded; early validity-limit compatibility is inactive.");
		}
	}

	private static void ApplyKianCommonsCompatibility(Harmony harmony, Array nodeBuffer, Array segmentBuffer, Array laneBuffer)
	{
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		int num = 0;
		foreach (Assembly assembly in assemblies)
		{
			Type type;
			try
			{
				type = assembly.GetType("KianCommons.NetUtil", throwOnError: false);
			}
			catch
			{
				continue;
			}
			if ((object)type != null)
			{
				PatchKianCommonsLimits(harmony, type);
				bool flag = false;
				flag |= SetStaticArray(type, "nodeBuffer_", nodeBuffer);
				flag |= SetStaticArray(type, "segmentBuffer_", segmentBuffer);
				if (flag | SetStaticArray(type, "laneBuffer_", laneBuffer))
				{
					num++;
					Logging.Info("Refreshed KianCommons static network buffers in " + assembly.GetName().Name + ".");
				}
			}
		}
		if (num == 0)
		{
			Logging.Info("No stale KianCommons static network buffers found.");
		}
	}

	private static void RefreshTMPEBuffers(Array nodeBuffer, Array segmentBuffer, Array laneBuffer)
	{
		Assembly assembly = ThirdPartyCapacityCompatibility.FindLoadedAssembly("TrafficManager");
		if ((object)assembly == null)
		{
			Logging.Info("TM:PE is not loaded; static buffer refresh is inactive.");
			return;
		}
		int num = 0;
		num += RefreshTMPEBufferField(assembly, "TrafficManager.Util.Extensions.NetNodeExtensions", "_nodeBuffer", nodeBuffer);
		num += RefreshTMPEBufferField(assembly, "TrafficManager.Util.Extensions.NetSegmentExtensions", "_segBuffer", segmentBuffer);
		num += RefreshTMPEBufferField(assembly, "TrafficManager.Util.Extensions.NetLaneExtensions", "_laneBuffer", laneBuffer);
		Logging.Info("Refreshed " + num + " TM:PE static network buffer fields.");
	}

	private static int RefreshTMPEBufferField(Assembly assembly, string typeName, string fieldName, Array value)
	{
		Type type = assembly.GetType(typeName, throwOnError: false);
		if ((object)type == null)
		{
			Logging.Warning("Could not find TM:PE buffer holder " + typeName + ".");
			return 0;
		}
		if (!SetStaticArray(type, fieldName, value))
		{
			return 0;
		}
		return 1;
	}

	private static void PatchKianCommonsLimits(Harmony harmony, Type type)
	{
		string item = type.Assembly.FullName + "|" + type.FullName;
		if (!PatchedKianCommonsTypes.Add(item))
		{
			return;
		}
		if (harmony == null)
		{
			Logging.Warning("Harmony is unavailable; KianCommons limit compatibility is inactive.");
			return;
		}
		try
		{
			PatchKianCommonsMethod(harmony, type, "IsNodeValid", "NodeLimitTranspiler");
			PatchKianCommonsMethod(harmony, type, "IsSegmentValid", "SegmentLimitTranspiler");
			PatchKianCommonsMethod(harmony, type, "AssertSegmentValid", "SegmentLimitTranspiler");
			Logging.Info("Patched KianCommons validity limits in " + type.Assembly.GetName().Name + ".");
		}
		catch (Exception ex)
		{
			PatchedKianCommonsTypes.Remove(item);
			Logging.Error("Failed to patch KianCommons limits in " + type.Assembly.GetName().Name + ": " + ex);
		}
	}

	private static void PatchKianCommonsMethod(Harmony harmony, Type type, string methodName, string transpilerName)
	{
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		MethodInfo methodInfo = null;
		MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (MethodInfo methodInfo2 in methods)
		{
			try
			{
				ParameterInfo[] parameters = methodInfo2.GetParameters();
				if (string.Equals(methodInfo2.Name, methodName, StringComparison.Ordinal) && parameters.Length == 1 && (object)parameters[0].ParameterType == typeof(ushort))
				{
					methodInfo = methodInfo2;
					break;
				}
			}
			catch (Exception)
			{
				continue;
			}
		}
		if ((object)methodInfo != null)
		{
			MethodInfo method = typeof(StaticBufferCompatibility).GetMethod(transpilerName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			harmony.Patch((MethodBase)methodInfo, (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(method), (HarmonyMethod)null);
		}
	}

	private static void RefreshMoveItBuffers(Array nodeBuffer, Array segmentBuffer, Array buildingBuffer)
	{
		Assembly assembly = ThirdPartyCapacityCompatibility.FindLoadedAssembly("MoveIt");
		if ((object)assembly == null)
		{
			Logging.Info("MoveIt is not loaded; static buffer refresh is inactive.");
			return;
		}
		int num = 0;
		num += RefreshMoveItType(assembly, "MoveIt.MoveItTool", nodeBuffer, segmentBuffer, buildingBuffer);
		num += RefreshMoveItType(assembly, "MoveIt.Instance", nodeBuffer, segmentBuffer, buildingBuffer);
		num += RefreshMoveItType(assembly, "MoveIt.Action", nodeBuffer, segmentBuffer, buildingBuffer);
		Logging.Info("Refreshed " + num + " MoveIt static buffer fields.");
	}

	private static int RefreshMoveItType(Assembly assembly, string typeName, Array nodeBuffer, Array segmentBuffer, Array buildingBuffer)
	{
		Type type = assembly.GetType(typeName, throwOnError: false);
		if ((object)type == null)
		{
			return 0;
		}
		int num = 0;
		if (SetStaticArray(type, "nodeBuffer", nodeBuffer))
		{
			num++;
		}
		if (SetStaticArray(type, "segmentBuffer", segmentBuffer))
		{
			num++;
		}
		if (buildingBuffer != null && SetStaticArray(type, "buildingBuffer", buildingBuffer))
		{
			num++;
		}
		return num;
	}

	private static bool SetStaticArray(Type type, string fieldName, Array value)
	{
		try
		{
			FieldInfo fieldInfo = ThirdPartyCapacityCompatibility.FindField(type, fieldName);
			if ((object)fieldInfo == null || !fieldInfo.IsStatic || !fieldInfo.FieldType.IsArray || value == null || (object)fieldInfo.FieldType.GetElementType() != value.GetType().GetElementType())
			{
				return false;
			}
			Array objA = fieldInfo.GetValue(null) as Array;
			if (object.ReferenceEquals(objA, value))
			{
				return false;
			}
			fieldInfo.SetValue(null, value);
			return true;
		}
		catch (Exception ex)
		{
			Logging.Error("Failed to refresh static buffer " + type.FullName + "." + fieldName + ": " + ex);
			return false;
		}
	}

	public static IEnumerable<CodeInstruction> NodeLimitTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		return ReplaceLimit(instructions, 32768, 65024);
	}

	public static IEnumerable<CodeInstruction> SegmentLimitTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		return ReplaceLimit(instructions, 36864, 65024);
	}

	private static IEnumerable<CodeInstruction> ReplaceLimit(IEnumerable<CodeInstruction> instructions, int oldLimit, int newLimit)
	{
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int && (int)instruction.operand == oldLimit)
			{
				instruction.operand = newLimit;
			}
			yield return instruction;
		}
	}
}

}
