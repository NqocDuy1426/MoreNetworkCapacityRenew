using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MoreNetworkCapacity.Patches;

namespace MoreNetworkCapacity.Compatibility {

internal static class AdaptiveRoadsCompatibility
{
	private const string AssemblyName = "AdaptiveRoads";

	private const string ModName = "AdaptiveRoads";

	private static readonly HashSet<string> PatchedMethods = new HashSet<string>();

	private static object _preparedInstance;

	private static bool _createHookInstalled;

	private static bool _deserializeHookInstalled;

	private static bool _lifeCycleLoadHookInstalled;

	private static bool _asymPavementColorPrefixPatched;

	private static bool _waitingLogged;

	private static int _retryCountdown;

	internal static void Apply(Harmony harmony)
	{
		TryApply(harmony, allowImmediateExpansion: true);
	}

	internal static void PrepareForNetworkDeserialize(Harmony harmony, bool expandedSave)
	{
		TryApply(harmony, expandedSave);
	}

	internal static void Retry(Harmony harmony)
	{
		if (_retryCountdown > 0)
		{
			_retryCountdown--;
		}
		else
		{
			_retryCountdown = 120;
			TryApply(harmony, allowImmediateExpansion: true);
		}
		NetworkColorOffsetPatch.ReapplyFinalPass(harmony);
	}

	internal static void Reset()
	{
		_preparedInstance = null;
		_createHookInstalled = false;
		_deserializeHookInstalled = false;
		_lifeCycleLoadHookInstalled = false;
		_asymPavementColorPrefixPatched = false;
		_waitingLogged = false;
		_retryCountdown = 0;
		PatchedMethods.Clear();
	}

	private static void TryApply(Harmony harmony, bool allowImmediateExpansion)
	{
		Assembly assembly = ThirdPartyCapacityCompatibility.FindLoadedAssembly("AdaptiveRoads");
		if ((object)assembly == null)
		{
			if (!_waitingLogged)
			{
				Logging.Info("AdaptiveRoads is not loaded yet; compatibility will retry after the assembly appears.");
				_waitingLogged = true;
			}
			return;
		}
		Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "AdaptiveRoads.Manager.NetworkExtensionManager", "AdaptiveRoads");
		if ((object)type == null)
		{
			return;
		}
		EnsureRuntimePatches(harmony, assembly, type);
		EnsureAsymPavementColorPrefix(harmony, assembly);
		EnsureCreateHook(harmony, type);
		EnsureDeserializeHook(harmony, type);
		EnsureLifeCycleLoadHook(harmony, assembly);
		object staticInstance = ArrayCompatibility.GetStaticInstance(type, "RawInstance", "instance_", "AdaptiveRoads NetworkExtensionManager");
		if (staticInstance == null)
		{
			if (!_waitingLogged)
			{
				Logging.Warning("AdaptiveRoads NetworkExtensionManager is not initialized yet; runtime patches are installed and buffer expansion will run when Create or Awake completes.");
				_waitingLogged = true;
			}
		}
		else if (allowImmediateExpansion && PrepareInstance(type, staticInstance))
		{
			Logging.Info("AdaptiveRoads compatibility pass completed.");
		}
	}

	private static bool PrepareInstance(Type managerType, object instance)
	{
		if (instance == null)
		{
			return false;
		}
		if (object.ReferenceEquals(_preparedInstance, instance))
		{
			return false;
		}
		FieldInfo field = ThirdPartyCapacityCompatibility.FindField(managerType, "NodeBuffer");
		Type elementType = GetElementType(field, "AdaptiveRoads NodeBuffer");
		ArrayCompatibility.ExpandDirect(field, instance, 32768, 65024, CreateInitFactory(elementType, "Init", segmentEnd: false, lane: false), createIfNull: false, "AdaptiveRoads NetworkExtensionManager.NodeBuffer");
		FieldInfo field2 = ThirdPartyCapacityCompatibility.FindField(managerType, "SegmentBuffer");
		Type elementType2 = GetElementType(field2, "AdaptiveRoads SegmentBuffer");
		ArrayCompatibility.ExpandDirect(field2, instance, 36864, 65024, CreateInitFactory(elementType2, "Init", segmentEnd: false, lane: false), createIfNull: false, "AdaptiveRoads NetworkExtensionManager.SegmentBuffer");
		FieldInfo field3 = ThirdPartyCapacityCompatibility.FindField(managerType, "SegmentEndBuffer");
		Type elementType3 = GetElementType(field3, "AdaptiveRoads SegmentEndBuffer");
		ArrayCompatibility.ExpandDirect(field3, instance, 73728, 130048, CreateInitFactory(elementType3, "Init", segmentEnd: true, lane: false), createIfNull: false, "AdaptiveRoads NetworkExtensionManager.SegmentEndBuffer");
		ArrayCompatibility.ExpandDirect(ThirdPartyCapacityCompatibility.FindField(managerType, "m_updatedNodes"), instance, 512, 1016, null, createIfNull: false, "AdaptiveRoads NetworkExtensionManager.m_updatedNodes");
		ArrayCompatibility.ExpandDirect(ThirdPartyCapacityCompatibility.FindField(managerType, "m_updatedSegments"), instance, 576, 1016, null, createIfNull: false, "AdaptiveRoads NetworkExtensionManager.m_updatedSegments");
		if (!HasExpectedLength(field, instance, 65024) || !HasExpectedLength(field2, instance, 65024) || !HasExpectedLength(field3, instance, 130048) || !HasExpectedLength(ThirdPartyCapacityCompatibility.FindField(managerType, "m_updatedNodes"), instance, 1016) || !HasExpectedLength(ThirdPartyCapacityCompatibility.FindField(managerType, "m_updatedSegments"), instance, 1016))
		{
			Logging.Error("AdaptiveRoads buffer expansion did not reach the expected lengths; compatibility remains pending.");
			return false;
		}
		_preparedInstance = instance;
		_waitingLogged = false;
		Logging.Info("Expanded AdaptiveRoads runtime buffers: nodes=" + 65024 + ", segments=" + 65024 + ", segmentEnds=" + 130048 + ".");
		return true;
	}

	private static void EnsureRuntimePatches(Harmony harmony, Assembly assembly, Type managerType)
	{
		if (harmony == null)
		{
			Logging.Warning("Harmony is unavailable; AdaptiveRoads runtime limit compatibility is inactive.");
			return;
		}
		int count = PatchedMethods.Count;
		PatchMethod(harmony, managerType, "UpdateAllNetworkFlags", Type.EmptyTypes, "AllNetworkLimitsTranspiler");
		PatchMethod(harmony, managerType, "get_SerializationCapacity", Type.EmptyTypes, "SerializationCapacityTranspiler");
		PatchMethod(harmony, ThirdPartyCapacityCompatibility.FindType(assembly, "AdaptiveRoads.Manager.NetInfoExtionsion+Net", "AdaptiveRoads"), "RemoveSegmentUserValue", new Type[1] { typeof(int) }, "SegmentLimitTranspiler");
		PatchMethod(harmony, ThirdPartyCapacityCompatibility.FindType(assembly, "AdaptiveRoads.Manager.NetInfoExtionsion+Net", "AdaptiveRoads"), "AllocateUserData", Type.EmptyTypes, "SegmentLimitTranspiler");
		PatchMethod(harmony, ThirdPartyCapacityCompatibility.FindType(assembly, "AdaptiveRoads.Util.RoadUtils", "AdaptiveRoads"), "SetDirectionImpl", new Type[1] { typeof(bool) }, "SegmentLimitTranspiler");
		PatchMethod(harmony, ThirdPartyCapacityCompatibility.FindType(assembly, "AdaptiveRoads.Util.RoadEditorUtils", "AdaptiveRoads"), "RefreshAllNetworks", Type.EmptyTypes, "NodeLimitTranspiler");
		PatchMethod(harmony, ThirdPartyCapacityCompatibility.FindType(assembly, "AdaptiveRoads.UI.RoadEditor.Overlay", "AdaptiveRoads"), "RenderOverlay", new Type[1] { typeof(CameraInfo) }, "SegmentLimitTranspiler");
		Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "AdaptiveRoads.Manager.TrackManager", "AdaptiveRoads");
		PatchMethod(harmony, type, "EndRendering", 1, "AllNetworkLimitsTranspiler");
		PatchMethod(harmony, type, "CalculateGroupData", 7, "AllNetworkLimitsTranspiler");
		PatchMethod(harmony, type, "PopulateGroupData", 12, "AllNetworkLimitsTranspiler");
		Type type2 = ThirdPartyCapacityCompatibility.FindType(assembly, "KianCommons.NetUtil", "AdaptiveRoads");
		PatchMethod(harmony, type2, "IsSegmentValid", new Type[1] { typeof(ushort) }, "SegmentLimitTranspiler");
		PatchMethod(harmony, type2, "AssertSegmentValid", new Type[1] { typeof(ushort) }, "SegmentLimitTranspiler");
		PatchMethod(harmony, type2, "IsNodeValid", new Type[1] { typeof(ushort) }, "NodeLimitTranspiler");
		PatchSimilarLaneIterators(harmony, assembly);
		if (PatchedMethods.Count == 0)
		{
			Logging.Warning("No known AdaptiveRoads runtime capacity loops were patched.");
		}
		else if (PatchedMethods.Count != count)
		{
			Logging.Info("Patched " + PatchedMethods.Count + " AdaptiveRoads runtime capacity methods.");
		}
	}

	private static void EnsureAsymPavementColorPrefix(Harmony harmony, Assembly assembly)
	{
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Expected O, but got Unknown
		if (harmony == null || _asymPavementColorPrefixPatched)
		{
			return;
		}
		try
		{
			Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "AdaptiveRoads.Patches.AsymPavements.RefreshJunctionDataPatch", "AdaptiveRoads");
			if ((object)type != null)
			{
				MethodInfo method = type.GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				MethodInfo method2 = typeof(AdaptiveRoadsCompatibility).GetMethod("AdaptiveRoadsColorPrefixTranspiler", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				if ((object)method == null || (object)method2 == null)
				{
					Logging.Warning("Could not resolve AdaptiveRoads AsymPavements RefreshJunctionData Prefix for color compatibility.");
					return;
				}
				harmony.Patch((MethodBase)method, (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(method2), (HarmonyMethod)null);
				_asymPavementColorPrefixPatched = true;
				Logging.Info("Patched AdaptiveRoads AsymPavements RefreshJunctionData Prefix to use the expanded node-color base.");
			}
		}
		catch (Exception ex)
		{
			Logging.Warning("Failed to patch AdaptiveRoads AsymPavements color Prefix: " + ex.GetType().Name + ".");
		}
	}

	public static IEnumerable<CodeInstruction> AdaptiveRoadsColorPrefixTranspiler(IEnumerable<CodeInstruction> instructions)
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
		Logging.Info("AdaptiveRoads AsymPavements color Prefix node-base replacements: " + num + ".");
		return list;
	}

	private static void EnsureCreateHook(Harmony harmony, Type managerType)
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Expected O, but got Unknown
		if (harmony == null || _createHookInstalled)
		{
			return;
		}
		try
		{
			MethodInfo method = managerType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
			MethodInfo method2 = typeof(AdaptiveRoadsCompatibility).GetMethod("CreatePostfix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if ((object)method == null || (object)method2 == null)
			{
				Logging.Warning("Could not install the AdaptiveRoads Create hook.");
				return;
			}
			harmony.Patch((MethodBase)method, (HarmonyMethod)null, new HarmonyMethod(method2), (HarmonyMethod)null, (HarmonyMethod)null);
			_createHookInstalled = true;
			Logging.Info("Installed AdaptiveRoads Create postfix for immediate expanded-buffer initialization.");
		}
		catch (Exception ex)
		{
			Logging.Error("Failed to install the AdaptiveRoads Create hook: " + ex);
		}
	}

	private static void CreatePostfix(object __result)
	{
		if (__result != null && Metadata.IsExpandedSave())
		{
			PrepareInstance(__result.GetType(), __result);
		}
	}

	private static void EnsureDeserializeHook(Harmony harmony, Type managerType)
	{
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Expected O, but got Unknown
		if (harmony == null || _deserializeHookInstalled)
		{
			return;
		}
		try
		{
			MethodInfo methodInfo = null;
			MethodInfo[] methods = managerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			for (int i = 0; i < methods.Length; i++)
			{
				if (!(methods[i].Name != "DeserializeImp") && methods[i].GetParameters().Length == 1)
				{
					if ((object)methodInfo != null)
					{
						methodInfo = null;
						break;
					}
					methodInfo = methods[i];
				}
			}
			MethodInfo method = typeof(AdaptiveRoadsCompatibility).GetMethod("DeserializeImpPostfix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if ((object)methodInfo == null || (object)method == null)
			{
				Logging.Warning("Could not install the AdaptiveRoads DeserializeImp hook.");
				return;
			}
			harmony.Patch((MethodBase)methodInfo, (HarmonyMethod)null, new HarmonyMethod(method), (HarmonyMethod)null, (HarmonyMethod)null);
			_deserializeHookInstalled = true;
			Logging.Info("Installed AdaptiveRoads DeserializeImp postfix for vanilla-save buffer expansion.");
		}
		catch (Exception ex)
		{
			Logging.Error("Failed to install the AdaptiveRoads DeserializeImp hook: " + ex);
		}
	}

	private static void EnsureLifeCycleLoadHook(Harmony harmony, Assembly assembly)
	{
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Expected O, but got Unknown
		if (harmony == null || _lifeCycleLoadHookInstalled)
		{
			return;
		}
		try
		{
			Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "AdaptiveRoads.LifeCycle.LifeCycle", "AdaptiveRoads");
			if ((object)type != null)
			{
				MethodInfo method = type.GetMethod("Load", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
				MethodInfo method2 = typeof(AdaptiveRoadsCompatibility).GetMethod("LifeCycleLoadPostfix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				if ((object)method == null || (object)method2 == null)
				{
					Logging.Warning("Could not install the AdaptiveRoads LifeCycle.Load color compatibility hook.");
					return;
				}
				harmony.Patch((MethodBase)method, (HarmonyMethod)null, new HarmonyMethod(method2), (HarmonyMethod)null, (HarmonyMethod)null);
				_lifeCycleLoadHookInstalled = true;
				Logging.Info("Installed AdaptiveRoads LifeCycle.Load hook for the post-pipeline network color pass.");
			}
		}
		catch (Exception ex)
		{
			Logging.Warning("Failed to install the AdaptiveRoads LifeCycle.Load color compatibility hook: " + ex.GetType().Name + ".");
		}
	}

	private static void LifeCycleLoadPostfix()
	{
		NetworkColorOffsetPatch.ApplyAfterAdaptiveRoadsLoad(HarmonyBootstrap.HarmonyInstance);
	}

	private static void DeserializeImpPostfix(object __instance)
	{
		if (__instance != null && !Metadata.IsExpandedSave())
		{
			PrepareInstance(__instance.GetType(), __instance);
		}
	}

	private static bool HasExpectedLength(FieldInfo field, object instance, int expectedLength)
	{
		if ((object)field == null)
		{
			return false;
		}
		try
		{
			return field.GetValue(instance) is Array array && array.Length == expectedLength;
		}
		catch
		{
			return false;
		}
	}

	private static void PatchSimilarLaneIterators(Harmony harmony, Assembly assembly)
	{
		Type[] types;
		try
		{
			types = assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			types = ex.Types;
		}
		foreach (Type type in types)
		{
			if ((object)type != null && type.FullName != null && type.FullName.IndexOf("AdaptiveRoads.Util.LaneHelpers+<GetSimilarLanes>", StringComparison.Ordinal) >= 0)
			{
				PatchMethod(harmony, type, "MoveNext", Type.EmptyTypes, "SegmentLimitTranspiler");
			}
		}
	}

	private static void PatchMethod(Harmony harmony, Type type, string methodName, Type[] parameterTypes, string transpilerName)
	{
		if ((object)type != null)
		{
			MethodInfo target = null;
			try
			{
				target = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, parameterTypes, null);
			}
			catch (Exception ex)
			{
				Logging.Error("Failed to resolve AdaptiveRoads method " + type.FullName + "." + methodName + ": " + ex);
			}
			PatchResolvedMethod(harmony, type, methodName, target, transpilerName);
		}
	}

	private static void PatchMethod(Harmony harmony, Type type, string methodName, int parameterCount, string transpilerName)
	{
		if ((object)type == null)
		{
			return;
		}
		MethodInfo methodInfo = null;
		try
		{
			MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			for (int i = 0; i < methods.Length; i++)
			{
				if (!(methods[i].Name != methodName) && methods[i].GetParameters().Length == parameterCount)
				{
					if ((object)methodInfo != null)
					{
						Logging.Warning("Multiple AdaptiveRoads methods match " + type.FullName + "." + methodName + " with " + parameterCount + " parameters; leaving it unchanged.");
						methodInfo = null;
						break;
					}
					methodInfo = methods[i];
				}
			}
		}
		catch (Exception ex)
		{
			Logging.Error("Failed to resolve AdaptiveRoads method " + type.FullName + "." + methodName + ": " + ex);
		}
		PatchResolvedMethod(harmony, type, methodName, methodInfo, transpilerName);
	}

	private static void PatchResolvedMethod(Harmony harmony, Type type, string methodName, MethodInfo target, string transpilerName)
	{
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Expected O, but got Unknown
		if ((object)type == null)
		{
			return;
		}
		string item = type.Assembly.FullName + "|" + type.FullName + "|" + methodName;
		if (!PatchedMethods.Add(item))
		{
			return;
		}
		if ((object)target == null)
		{
			PatchedMethods.Remove(item);
			Logging.Warning("Could not find AdaptiveRoads method " + type.FullName + "." + methodName + " for capacity patching.");
			return;
		}
		try
		{
			MethodInfo method = typeof(AdaptiveRoadsCompatibility).GetMethod(transpilerName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if ((object)method == null)
			{
				PatchedMethods.Remove(item);
				Logging.Error("Could not find AdaptiveRoads transpiler " + transpilerName + ".");
				return;
			}
			harmony.Patch((MethodBase)target, (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(method), (HarmonyMethod)null);
			Logging.Info("Patched AdaptiveRoads method " + type.FullName + "." + methodName + " for expanded limits.");
		}
		catch (Exception ex)
		{
			PatchedMethods.Remove(item);
			Logging.Error("Failed to patch AdaptiveRoads method " + type.FullName + "." + methodName + ": " + ex);
		}
	}

	public static IEnumerable<CodeInstruction> AllNetworkLimitsTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		return ReplaceLimits(instructions, replaceNodes: true, replaceSegments: true);
	}

	public static IEnumerable<CodeInstruction> NodeLimitTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		return ReplaceLimits(instructions, replaceNodes: true, replaceSegments: false);
	}

	public static IEnumerable<CodeInstruction> SegmentLimitTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		return ReplaceLimits(instructions, replaceNodes: false, replaceSegments: true);
	}

	public static IEnumerable<CodeInstruction> SerializationCapacityTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = new List<CodeInstruction>(instructions);
		MethodInfo methodInfo = AccessTools.Method(typeof(Capacity), "GetAdaptiveRoadsSerializationCapacity", (Type[])null, (Type[])null);
		if ((object)methodInfo == null)
		{
			Logging.Error("Could not find Capacity.GetAdaptiveRoadsSerializationCapacity(); leaving AdaptiveRoads serialization capacity unchanged.");
			return list;
		}
		int num = 0;
		for (int i = 0; i < list.Count; i++)
		{
			CodeInstruction val = list[i];
			if (IsInt32Constant(val, 1916928))
			{
				val.opcode = OpCodes.Call;
				val.operand = methodInfo;
				num++;
			}
		}
		if (num == 0)
		{
			Logging.Warning("AdaptiveRoads SerializationCapacity constant was not found; save buffer capacity remains vanilla-sized.");
		}
		return list;
	}

	private static IEnumerable<CodeInstruction> ReplaceLimits(IEnumerable<CodeInstruction> instructions, bool replaceNodes, bool replaceSegments)
	{
		foreach (CodeInstruction instruction in instructions)
		{
			if (replaceNodes && IsInt32Constant(instruction, 32768))
			{
				instruction.opcode = OpCodes.Ldc_I4;
				instruction.operand = 65024;
			}
			else if (replaceSegments && IsInt32Constant(instruction, 36864))
			{
				instruction.opcode = OpCodes.Ldc_I4;
				instruction.operand = 65024;
			}
			yield return instruction;
		}
	}

	private static bool IsInt32Constant(CodeInstruction instruction, int value)
	{
		if ((instruction.opcode == OpCodes.Ldc_I4 || instruction.opcode == OpCodes.Ldc_I4_S) && instruction.operand is int)
		{
			return (int)instruction.operand == value;
		}
		return false;
	}

	private static Type GetElementType(FieldInfo field, string label)
	{
		if ((object)field == null)
		{
			return null;
		}
		Type elementType = field.FieldType.GetElementType();
		if ((object)elementType == null)
		{
			Logging.Warning("Compatibility member is not an array: " + label + ".");
		}
		return elementType;
	}

	private static IndexedElementFactory CreateInitFactory(Type elementType, string methodName, bool segmentEnd, bool lane)
	{
		if ((object)elementType == null)
		{
			return null;
		}
		MethodInfo init = elementType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if ((object)init == null)
		{
			Logging.Warning("Could not find " + elementType.FullName + "." + methodName + " for AdaptiveRoads initialization.");
			return null;
		}
		return delegate(int index)
		{
			object obj = Activator.CreateInstance(elementType);
			if (segmentEnd)
			{
				init.Invoke(obj, new object[2]
				{
					(ushort)(index / 2),
					(index & 1) == 0
				});
			}
			else if (lane)
			{
				init.Invoke(obj, new object[1] { (uint)index });
			}
			else
			{
				init.Invoke(obj, new object[1] { (ushort)index });
			}
			return obj;
		};
	}
}

}
