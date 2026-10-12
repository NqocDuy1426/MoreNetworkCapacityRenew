using System;
using System.Collections.Generic;
using System.Reflection;
using CitiesHarmony.API;
using HarmonyLib;
using MoreNetworkCapacity.Compatibility;
using MoreNetworkCapacity.Patches;

namespace MoreNetworkCapacity {

internal static class HarmonyBootstrap
{
	internal const string HarmonyId = "com.codex.csl.more-network-capacity";

	private static Harmony _harmony;

	internal static Harmony HarmonyInstance => _harmony;

	internal static void Enable()
	{
		HarmonyHelper.DoOnHarmonyReady(ApplyPatches);
	}

	internal static void Disable()
	{
		if (_harmony != null)
		{
			_harmony.UnpatchAll("com.codex.csl.more-network-capacity");
			_harmony = null;
			AdaptiveRoadsCompatibility.Reset();
			NetworkColorOffsetPatch.ResetFinalPass();
			PatchState.MarkDisabled();
		}
	}

	private static void ApplyPatches()
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Expected O, but got Unknown
		if (_harmony != null)
		{
			return;
		}
		if (!GameVersion.IsSupported)
		{
			Logging.Error("Unsupported game assembly. The mod will stay inactive.");
			return;
		}
		_harmony = new Harmony("com.codex.csl.more-network-capacity");
		List<Type> patchTypes = GetPatchTypes();
		int num = 0;
		for (int i = 0; i < patchTypes.Count; i++)
		{
			Type type = patchTypes[i];
			try
			{
				_harmony.CreateClassProcessor(type).Patch();
				num++;
			}
			catch (Exception ex)
			{
				PatchState.MarkDisabled();
				Logging.Error("Failed to apply patch class " + type.FullName + ": " + ex);
				return;
			}
		}
		PatchState.MarkEnabled();
		SimulationStepDiagnostics.InstallEarly(_harmony);
		SimulationStepDiagnostics.InitializeEarly();
		RenderGroupMeshDataPatch.Apply(_harmony);
		Logging.Info("Harmony patches applied for 1.0.15 (" + num + " patch classes).");
		Logging.Info("Expanded network capacity: nodes=" + 65024 + ", segments=" + 65024 + " (kept below 65536 so third-party UInt16 loop counters terminate).");
		ThirdPartyCapacityCompatibility.ApplyEarly(_harmony);
	}

	private static List<Type> GetPatchTypes()
	{
		Assembly assembly = typeof(HarmonyBootstrap).Assembly;
		List<Type> list = new List<Type>();
		Type[] types = assembly.GetTypes();
		foreach (Type type in types)
		{
			if (type.GetCustomAttributes(typeof(HarmonyPatch), inherit: true).Length != 0)
			{
				list.Add(type);
			}
		}
		list.Sort(ComparePatchTypes);
		return list;
	}

	private static int ComparePatchTypes(Type left, Type right)
	{
		int patchPriority = GetPatchPriority(left);
		int patchPriority2 = GetPatchPriority(right);
		if (patchPriority != patchPriority2)
		{
			return patchPriority.CompareTo(patchPriority2);
		}
		return string.CompareOrdinal(left.FullName, right.FullName);
	}

	private static int GetPatchPriority(Type type)
	{
		if (type.FullName == "MoreNetworkCapacity.Patches.CoreLoopPatch")
		{
			return 0;
		}
		if (type.FullName == "MoreNetworkCapacity.Patches.CheckLimitsPatch" || type.FullName == "MoreNetworkCapacity.Patches.NetManagerColorUpdatePatch")
		{
			return 1;
		}
		return 2;
	}
}

}
