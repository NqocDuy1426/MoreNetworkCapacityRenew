using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch]
internal static class NetworkColorOffsetPatch
{
	private static bool _finalPassApplied;

	private static bool _lateFinalPassApplied;

	private static bool _postLoadFinalPassApplied;

	private static bool _adaptiveRoadsFinalPassApplied;

	private static readonly string[,] Targets = new string[14, 2]
	{
		{ "NetManager", "UpdateColorMap" },
		{ "NetManager", "UpdateNodeRenderer" },
		{ "NetNode", "RenderInstance" },
		{ "NetNode", "RefreshEndData" },
		{ "NetNode", "RefreshEndFenceData" },
		{ "NetNode", "RefreshBendData" },
		{ "NetNode", "RefreshBendFenceData" },
		{ "NetNode", "RefreshJunctionData" },
		{ "NetNode", "PopulateGroupData" },
		{ "NetSegment", "RenderInstance" },
		{ "NetSegment", "RefreshRoadFence" },
		{ "NetSegment", "PopulateGroupData" },
		{ "EventManager", "UpdateColorMap" },
		{ "EventManager", "GetEventRouteRoadColorIndex" }
	};

	private static IEnumerable<MethodBase> TargetMethods()
	{
		Assembly assembly = typeof(NetManager).Assembly;
		for (int targetIndex = 0; targetIndex < Targets.GetLength(0); targetIndex++)
		{
			Type type = assembly.GetType(Targets[targetIndex, 0], throwOnError: false);
			if ((object)type == null)
			{
				Logging.Warning("Could not find color-offset target type " + Targets[targetIndex, 0] + ".");
				continue;
			}
			bool found = false;
			foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
			{
				if (!(method.Name != Targets[targetIndex, 1]))
				{
					found = true;
					yield return method;
				}
			}
			if (!found)
			{
				Logging.Warning("Could not find color-offset target " + Targets[targetIndex, 0] + "." + Targets[targetIndex, 1] + ".");
			}
		}
	}

	private static IEnumerable<CodeInstruction> Transpiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = new List<CodeInstruction>(instructions);
		MethodInfo methodInfo = AccessTools.Method(typeof(Capacity), "GetNodeColorIndexBase", (Type[])null, (Type[])null);
		MethodInfo methodInfo2 = AccessTools.Method(typeof(Capacity), "GetEventColorIndexBase", (Type[])null, (Type[])null);
		if ((object)methodInfo == null || (object)methodInfo2 == null)
		{
			Logging.Error("Network color-offset helpers are unavailable; leaving " + GetLabel(original) + " unchanged.");
			return list;
		}
		int num = 0;
		int num2 = 0;
		for (int i = 0; i < list.Count; i++)
		{
			CodeInstruction val = list[i];
			if (IsInt32Constant(val, 86016))
			{
				val.opcode = OpCodes.Call;
				val.operand = methodInfo;
				num++;
			}
			else if (IsInt32Constant(val, 118784))
			{
				val.opcode = OpCodes.Call;
				val.operand = methodInfo2;
				num2++;
			}
		}
		if (num != 0 || num2 != 0)
		{
			Logging.Info("Network color offsets patched in " + GetLabel(original) + ": nodes=" + num + ", events=" + num2 + ".");
		}
		return list;
	}

	internal static void ApplyFinalPass(Harmony harmony)
	{
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Expected O, but got Unknown
		if (_finalPassApplied || harmony == null)
		{
			return;
		}
		MethodInfo methodInfo = AccessTools.Method(typeof(NetworkColorOffsetPatch), "FinalNodeBaseTranspiler", (Type[])null, (Type[])null);
		if ((object)methodInfo == null)
		{
			Logging.Error("Could not find the final network node-color offset transpiler.");
			return;
		}
		HarmonyMethod val = new HarmonyMethod(methodInfo);
		val.priority = 0;
		int num = 0;
		foreach (MethodBase item in TargetMethods())
		{
			harmony.Patch(item, (HarmonyMethod)null, (HarmonyMethod)null, val, (HarmonyMethod)null);
			num++;
		}
		_finalPassApplied = num != 0;
		Logging.Info("Installed final network node-color offset pass for " + num + " methods.");
	}

	internal static void ReapplyFinalPass(Harmony harmony)
	{
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Expected O, but got Unknown
		if (_lateFinalPassApplied || harmony == null)
		{
			return;
		}
		MethodInfo methodInfo = AccessTools.Method(typeof(NetworkColorOffsetPatch), "LateFinalNodeBaseTranspiler", (Type[])null, (Type[])null);
		if ((object)methodInfo == null)
		{
			Logging.Error("Could not find the late network node-color offset transpiler.");
			return;
		}
		HarmonyMethod val = new HarmonyMethod(methodInfo);
		val.priority = 0;
		int num = 0;
		foreach (MethodBase item in TargetMethods())
		{
			try
			{
				harmony.Patch(item, (HarmonyMethod)null, (HarmonyMethod)null, val, (HarmonyMethod)null);
				num++;
			}
			catch (Exception ex)
			{
				Logging.Warning("Late network node-color pass skipped for " + GetLabel(item) + ": " + ex.GetType().Name + ".");
			}
		}
		_lateFinalPassApplied = num != 0;
		Logging.Info("Reapplied late network node-color offset pass for " + num + " methods.");
	}

	internal static void ApplyPostLoadFinalPass(Harmony harmony)
	{
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Expected O, but got Unknown
		if (_postLoadFinalPassApplied || harmony == null)
		{
			return;
		}
		MethodInfo methodInfo = AccessTools.Method(typeof(NetworkColorOffsetPatch), "PostLoadFinalNodeBaseTranspiler", (Type[])null, (Type[])null);
		if ((object)methodInfo == null)
		{
			Logging.Error("Could not find the post-load network node-color offset transpiler.");
			return;
		}
		HarmonyMethod val = new HarmonyMethod(methodInfo);
		val.priority = 0;
		int num = 0;
		foreach (MethodBase item in TargetMethods())
		{
			try
			{
				harmony.Patch(item, (HarmonyMethod)null, (HarmonyMethod)null, val, (HarmonyMethod)null);
				num++;
			}
			catch (Exception ex)
			{
				Logging.Warning("Post-load network node-color pass skipped for " + GetLabel(item) + ": " + ex.GetType().Name + ".");
			}
		}
		_postLoadFinalPassApplied = num != 0;
		Logging.Info("Installed post-load network node-color offset pass for " + num + " methods.");
	}

	internal static void ResetFinalPass()
	{
		_finalPassApplied = false;
		_lateFinalPassApplied = false;
		_postLoadFinalPassApplied = false;
		_adaptiveRoadsFinalPassApplied = false;
	}

	internal static void ApplyAfterAdaptiveRoadsLoad(Harmony harmony)
	{
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Expected O, but got Unknown
		if (_adaptiveRoadsFinalPassApplied || harmony == null)
		{
			return;
		}
		MethodInfo methodInfo = AccessTools.Method(typeof(NetworkColorOffsetPatch), "AdaptiveRoadsFinalNodeBaseTranspiler", (Type[])null, (Type[])null);
		if ((object)methodInfo == null)
		{
			Logging.Error("Could not find the AdaptiveRoads-final network node-color offset transpiler.");
			return;
		}
		HarmonyMethod val = new HarmonyMethod(methodInfo);
		val.priority = 0;
		int num = 0;
		foreach (MethodBase item in TargetMethods())
		{
			try
			{
				harmony.Patch(item, (HarmonyMethod)null, (HarmonyMethod)null, val, (HarmonyMethod)null);
				num++;
			}
			catch (Exception ex)
			{
				Logging.Warning("AdaptiveRoads-final network node-color pass skipped for " + GetLabel(item) + ": " + ex.GetType().Name + ".");
			}
		}
		_adaptiveRoadsFinalPassApplied = num != 0;
		Logging.Info("Installed AdaptiveRoads-final network node-color offset pass for " + num + " methods.");
	}

	private static IEnumerable<CodeInstruction> FinalNodeBaseTranspiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
	{
		return ReplaceNodeBases(original, instructions, "Final network node-color offsets patched in ");
	}

	private static IEnumerable<CodeInstruction> LateFinalNodeBaseTranspiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
	{
		return ReplaceNodeBases(original, instructions, "Late final network node-color offsets patched in ");
	}

	private static IEnumerable<CodeInstruction> PostLoadFinalNodeBaseTranspiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
	{
		return ReplaceNodeBases(original, instructions, "Post-load final network node-color offsets patched in ");
	}

	private static IEnumerable<CodeInstruction> AdaptiveRoadsFinalNodeBaseTranspiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
	{
		return ReplaceNodeBases(original, instructions, "AdaptiveRoads-final network node-color offsets patched in ");
	}

	private static IEnumerable<CodeInstruction> ReplaceNodeBases(MethodBase original, IEnumerable<CodeInstruction> instructions, string logPrefix)
	{
		List<CodeInstruction> list = new List<CodeInstruction>(instructions);
		MethodInfo methodInfo = AccessTools.Method(typeof(Capacity), "GetNodeColorIndexBase", (Type[])null, (Type[])null);
		if ((object)methodInfo == null)
		{
			return list;
		}
		int num = 0;
		for (int i = 0; i < list.Count; i++)
		{
			if (IsInt32Constant(list[i], 86016))
			{
				list[i].opcode = OpCodes.Call;
				list[i].operand = methodInfo;
				num++;
			}
		}
		if (num != 0)
		{
			Logging.Info(logPrefix + GetLabel(original) + ": " + num + ".");
		}
		return list;
	}

	private static string GetLabel(MethodBase method)
	{
		return method.DeclaringType.FullName + "." + method.Name;
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
