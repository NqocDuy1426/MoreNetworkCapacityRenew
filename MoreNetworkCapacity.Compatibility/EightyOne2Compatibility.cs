using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using ColossalFramework.IO;
using HarmonyLib;

namespace MoreNetworkCapacity.Compatibility {

internal static class EightyOne2Compatibility
{
	private const string AssemblyName = "EightyOne2";

	private const string ModName = "EightyOne2";

	private const string ExpandedManagerTypeName = "EightyOne2.Patches.ExpandedElectricityManager";

	private const string DataContainerTypeName = "EightyOne2.Serialization.ElectricityDataContainer";

	private const string ExpandedWaterManagerTypeName = "EightyOne2.Patches.ExpandedWaterManager";

	private const string WaterDataPatchesTypeName = "EightyOne2.Patches.WaterManagerDataPatches";

	private static readonly string[] WaterStaticArrayFields = new string[3] { "s_waterPulseUnits", "s_sewagePulseUnits", "s_heatingPulseUnits" };

	private static readonly HashSet<string> PatchedMethods = new HashSet<string>();

	private static Assembly _assembly;

	private static Type _expandedManagerType;

	private static Type _expandedWaterManagerType;

	internal static void Apply(Harmony harmony)
	{
		_assembly = ThirdPartyCapacityCompatibility.FindLoadedAssembly("EightyOne2");
		if ((object)_assembly == null)
		{
			Logging.Info("EightyOne2 is not loaded; compatibility is inactive.");
			return;
		}
		_expandedManagerType = ThirdPartyCapacityCompatibility.FindType(_assembly, "EightyOne2.Patches.ExpandedElectricityManager", "EightyOne2");
		if ((object)_expandedManagerType == null)
		{
			Logging.Warning("EightyOne2 is loaded but its expanded electricity manager was not found.");
			return;
		}
		EnsureStaticArrays(65024);
		_expandedWaterManagerType = ThirdPartyCapacityCompatibility.FindType(_assembly, "EightyOne2.Patches.ExpandedWaterManager", "EightyOne2");
		if ((object)_expandedWaterManagerType == null)
		{
			Logging.Warning("EightyOne2 is loaded but its expanded water manager was not found; water serialization may overflow.");
		}
		else
		{
			EnsureWaterStaticArrays(65024);
		}
		PatchPrefixes(harmony);
		PatchDataContainer(harmony, "Serialize", "DataContainerSerializeTranspiler", 1);
		PatchDataContainer(harmony, "Deserialize", "DataContainerDeserializeTranspiler", 2);
		PatchWaterReadValidation(harmony);
		Logging.Info("EightyOne2 compatibility pass completed.");
	}

	public static void CustomSerializePrefix(object[] __args)
	{
		EnsureStaticArraysForArguments(__args, 1);
	}

	public static void CustomDeserializePrefix(object[] __args)
	{
		EnsureStaticArraysForArguments(__args, 2);
	}

	public static void WaterCustomSerializePrefix(object[] __args)
	{
		EnsureWaterStaticArraysForArguments(__args, 1);
	}

	public static void WaterCustomDeserializePrefix(object[] __args)
	{
		EnsureWaterStaticArraysForArguments(__args, 2);
	}

	public static IEnumerable<CodeInstruction> DataContainerSerializeTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		return ReplaceNodeGroupCapacity(instructions, 1, "ElectricityDataContainer.Serialize");
	}

	public static IEnumerable<CodeInstruction> DataContainerDeserializeTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		return ReplaceNodeGroupCapacity(instructions, 2, "ElectricityDataContainer.Deserialize");
	}

	public static IEnumerable<CodeInstruction> NodeLimitValidationTranspiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
	{
		string label = "WaterDataContainer";
		if ((object)original != null && (object)original.DeclaringType != null)
		{
			label = original.DeclaringType.Name + "." + original.Name;
		}
		return ReplaceSingleNodeLimit(instructions, label);
	}

	private static void PatchWaterReadValidation(Harmony harmony)
	{
		if (harmony != null)
		{
			Type type = ThirdPartyCapacityCompatibility.FindType(_assembly, "EightyOne2.Serialization.WaterDataContainer", "EightyOne2");
			if ((object)type != null)
			{
				PatchConstantTranspiler(harmony, type, "ReadPulseGroups", 2);
				PatchConstantTranspiler(harmony, type, "ReadPulseUnits", 2);
			}
		}
	}

	private static void PatchConstantTranspiler(Harmony harmony, Type type, string methodName, int parameterCount)
	{
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d8: Expected O, but got Unknown
		string item = type.FullName + "|" + methodName + "|nodelimit";
		if (!PatchedMethods.Add(item))
		{
			return;
		}
		MethodInfo methodInfo = null;
		MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		for (int i = 0; i < methods.Length; i++)
		{
			if (methods[i].Name == methodName && methods[i].GetParameters().Length == parameterCount)
			{
				methodInfo = methods[i];
				break;
			}
		}
		if ((object)methodInfo == null)
		{
			PatchedMethods.Remove(item);
			Logging.Warning("Could not find " + type.Name + "." + methodName + " for expanded node id validation.");
		}
		else
		{
			MethodInfo method = typeof(EightyOne2Compatibility).GetMethod("NodeLimitValidationTranspiler", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			harmony.Patch((MethodBase)methodInfo, (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(method), (HarmonyMethod)null);
			Logging.Info("Patched " + type.Name + "." + methodName + " so expanded node ids are not rejected while reading.");
		}
	}

	private static void PatchPrefixes(Harmony harmony)
	{
		if (harmony == null)
		{
			Logging.Warning("Harmony is unavailable; EightyOne2 serialization prefixes are inactive.");
			return;
		}
		Type type = ThirdPartyCapacityCompatibility.FindType(_assembly, "EightyOne2.Patches.ElectricityManagerDataPatches", "EightyOne2");
		if ((object)type != null)
		{
			PatchPrefix(harmony, type, "CustomSerialize", "CustomSerializePrefix", "electricity");
			PatchPrefix(harmony, type, "CustomDeserialize", "CustomDeserializePrefix", "electricity");
		}
		Type type2 = ThirdPartyCapacityCompatibility.FindType(_assembly, "EightyOne2.Patches.WaterManagerDataPatches", "EightyOne2");
		if ((object)type2 != null)
		{
			PatchPrefix(harmony, type2, "CustomSerialize", "WaterCustomSerializePrefix", "water");
			PatchPrefix(harmony, type2, "CustomDeserialize", "WaterCustomDeserializePrefix", "water");
		}
	}

	private static void PatchPrefix(Harmony harmony, Type type, string methodName, string prefixName, string label)
	{
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Expected O, but got Unknown
		string item = type.FullName + "|" + methodName;
		if (!PatchedMethods.Add(item))
		{
			return;
		}
		MethodInfo methodInfo = null;
		MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		for (int i = 0; i < methods.Length; i++)
		{
			if (methods[i].Name == methodName)
			{
				methodInfo = methods[i];
				break;
			}
		}
		if ((object)methodInfo == null)
		{
			PatchedMethods.Remove(item);
			Logging.Warning("Could not find EightyOne2." + type.Name + "." + methodName + " for serialization compatibility.");
		}
		else
		{
			MethodInfo method = typeof(EightyOne2Compatibility).GetMethod(prefixName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(method), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			Logging.Info("Patched EightyOne2." + type.Name + "." + methodName + " for expanded " + label + " storage.");
		}
	}

	private static void PatchDataContainer(Harmony harmony, string methodName, string transpilerName, int expectedReplacements)
	{
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Expected O, but got Unknown
		if (harmony == null)
		{
			return;
		}
		Type type = ThirdPartyCapacityCompatibility.FindType(_assembly, "EightyOne2.Serialization.ElectricityDataContainer", "EightyOne2");
		if ((object)type == null)
		{
			return;
		}
		string item = type.FullName + "|" + methodName + "|container";
		if (PatchedMethods.Add(item))
		{
			MethodInfo method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { typeof(DataSerializer) }, null);
			if ((object)method == null)
			{
				PatchedMethods.Remove(item);
				Logging.Warning("Could not find EightyOne2.Serialization.ElectricityDataContainer." + methodName + " for expanded serialization.");
				return;
			}
			MethodInfo method2 = typeof(EightyOne2Compatibility).GetMethod(transpilerName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			harmony.Patch((MethodBase)method, (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(method2), (HarmonyMethod)null);
			Logging.Info("Patched EightyOne2.Serialization.ElectricityDataContainer." + methodName + " for " + expectedReplacements + " expanded electricity length(s).");
		}
	}

	private static void EnsureStaticArraysForArguments(object[] arguments, int firstPulseArrayIndex)
	{
		int num = 65024;
		if (arguments != null)
		{
			for (int i = firstPulseArrayIndex; i < firstPulseArrayIndex + 2 && i < arguments.Length; i++)
			{
				if (arguments[i] is Array array && array.Length > num)
				{
					num = array.Length;
				}
			}
		}
		EnsureStaticArrays(num);
	}

	private static void EnsureStaticArrays(int requiredLength)
	{
		if ((object)_expandedManagerType != null)
		{
			if (requiredLength < 65024)
			{
				requiredLength = 65024;
			}
			EnsureStaticArray(_expandedManagerType, "electricity", "s_pulseGroups", requiredLength);
			EnsureStaticArray(_expandedManagerType, "electricity", "s_pulseUnits", requiredLength);
		}
	}

	private static void EnsureWaterStaticArraysForArguments(object[] arguments, int firstPulseArrayIndex)
	{
		for (int i = 0; i < WaterStaticArrayFields.Length; i++)
		{
			int num = 65024;
			int num2 = firstPulseArrayIndex + i;
			if (arguments != null && num2 < arguments.Length && arguments[num2] is Array array && array.Length > num)
			{
				num = array.Length;
			}
			EnsureWaterStaticArray(i, num);
		}
	}

	private static void EnsureWaterStaticArrays(int requiredLength)
	{
		for (int i = 0; i < WaterStaticArrayFields.Length; i++)
		{
			EnsureWaterStaticArray(i, requiredLength);
		}
	}

	private static void EnsureWaterStaticArray(int index, int requiredLength)
	{
		if ((object)_expandedWaterManagerType != null)
		{
			if (requiredLength < 65024)
			{
				requiredLength = 65024;
			}
			EnsureStaticArray(_expandedWaterManagerType, "water", WaterStaticArrayFields[index], requiredLength);
		}
	}

	private static void EnsureStaticArray(Type ownerType, string label, string fieldName, int requiredLength)
	{
		FieldInfo fieldInfo = ThirdPartyCapacityCompatibility.FindField(ownerType, fieldName);
		if ((object)fieldInfo == null || !fieldInfo.IsStatic)
		{
			Logging.Warning("Could not find EightyOne2 static " + label + " array " + fieldName + ".");
			return;
		}
		try
		{
			Array array = fieldInfo.GetValue(null) as Array;
			if (array != null && array.Length >= requiredLength)
			{
				return;
			}
			Type elementType = fieldInfo.FieldType.GetElementType();
			if ((object)elementType == null)
			{
				Logging.Warning("EightyOne2 member is not an array: " + fieldName + ".");
				return;
			}
			Array array2 = Array.CreateInstance(elementType, requiredLength);
			int num = 0;
			if (array != null)
			{
				num = array.Length;
				Array.Copy(array, array2, num);
			}
			fieldInfo.SetValue(null, array2);
			Logging.Info("Expanded EightyOne2 static " + label + " array " + fieldName + ": " + num + " -> " + requiredLength + ".");
		}
		catch (Exception ex)
		{
			Logging.Error("Failed to expand EightyOne2 static " + label + " array " + fieldName + ": " + ex);
		}
	}

	private static IEnumerable<CodeInstruction> ReplaceNodeGroupCapacity(IEnumerable<CodeInstruction> instructions, int expected, string label)
	{
		List<CodeInstruction> list = new List<CodeInstruction>(instructions);
		MethodInfo operand = AccessTools.Method(typeof(Capacity), "GetElectricityNodeGroupLength", (Type[])null, (Type[])null);
		List<int> list2 = new List<int>();
		for (int i = 0; i < list.Count; i++)
		{
			if (IsInt32Constant(list[i], 32768))
			{
				list2.Add(i);
			}
		}
		if (list2.Count != expected)
		{
			Logging.Error("EightyOne2 " + label + " length pattern mismatch: expected " + expected + ", found " + list2.Count + ". Leaving values unchanged.");
			return list;
		}
		for (int j = 0; j < list2.Count; j++)
		{
			CodeInstruction val = list[list2[j]];
			val.opcode = OpCodes.Call;
			val.operand = operand;
		}
		return list;
	}

	private static IEnumerable<CodeInstruction> ReplaceSingleNodeLimit(IEnumerable<CodeInstruction> instructions, string label)
	{
		List<CodeInstruction> list = new List<CodeInstruction>(instructions);
		List<int> list2 = new List<int>();
		for (int i = 0; i < list.Count; i++)
		{
			if (IsInt32Constant(list[i], 32768))
			{
				list2.Add(i);
			}
		}
		if (list2.Count != 1)
		{
			Logging.Error("EightyOne2 " + label + " node-limit pattern mismatch: expected 1, found " + list2.Count + ". Leaving values unchanged.");
			return list;
		}
		MethodInfo method = typeof(Capacity).GetMethod("GetNodeIdValidationLimit", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
		if ((object)method == null)
		{
			Logging.Error("Could not find Capacity.GetNodeIdValidationLimit(); leaving EightyOne2 " + label + " unchanged.");
			return list;
		}
		CodeInstruction val = list[list2[0]];
		val.opcode = OpCodes.Call;
		val.operand = method;
		return list;
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
