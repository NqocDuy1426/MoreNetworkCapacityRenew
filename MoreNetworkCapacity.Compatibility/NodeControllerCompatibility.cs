using System;
using System.Reflection;
using HarmonyLib;

namespace MoreNetworkCapacity.Compatibility {

internal static class NodeControllerCompatibility
{
	private const string AssemblyName = "NodeControllerRenewal";

	private const string ModName = "Node Controller Renewal";

	private const string ManagerTypeName = "NodeController.Manager";

	private static bool _patchesApplied;

	private static bool _fromXmlPatched;

	internal static void Apply(Harmony harmony)
	{
		Assembly assembly = ThirdPartyCapacityCompatibility.FindLoadedAssembly("NodeControllerRenewal");
		if ((object)assembly == null)
		{
			Logging.Info("Node Controller Renewal is not loaded; compatibility is inactive.");
			return;
		}
		Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "NodeController.Manager", "Node Controller Renewal");
		if ((object)type != null)
		{
			if (!_patchesApplied)
			{
				_patchesApplied = PatchManager(harmony, type);
			}
			Logging.Info("Node Controller Renewal compatibility pass completed.");
		}
	}

	private static bool PatchManager(Harmony harmony, Type managerType)
	{
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Expected O, but got Unknown
		//IL_00df: Unknown result type (might be due to invalid IL or missing references)
		//IL_00eb: Expected O, but got Unknown
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b1: Expected O, but got Unknown
		//IL_0124: Unknown result type (might be due to invalid IL or missing references)
		//IL_012f: Expected O, but got Unknown
		if (harmony == null)
		{
			Logging.Warning("Harmony is unavailable; Node Controller Renewal compatibility is inactive.");
			return false;
		}
		try
		{
			MethodInfo method = typeof(NodeControllerPatches).GetMethod("EnsureManagerBufferPrefix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			MethodInfo method2 = typeof(NodeControllerPatches).GetMethod("ManagerConstructorPostfix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			MethodInfo method3 = typeof(NodeControllerPatches).GetMethod("ManagerClearPostfix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			ConstructorInfo constructor = managerType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
			if ((object)constructor != null)
			{
				harmony.Patch((MethodBase)constructor, (HarmonyMethod)null, new HarmonyMethod(method2), (HarmonyMethod)null, (HarmonyMethod)null);
			}
			MethodInfo[] methods = managerType.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (MethodInfo methodInfo in methods)
			{
				if (!methodInfo.IsAbstract && !methodInfo.ContainsGenericParameters)
				{
					harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(method), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
				}
			}
			MethodInfo method4 = managerType.GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
			if ((object)method4 != null)
			{
				harmony.Patch((MethodBase)method4, (HarmonyMethod)null, new HarmonyMethod(method3), (HarmonyMethod)null, (HarmonyMethod)null);
			}
			if (!_fromXmlPatched)
			{
				MethodInfo methodInfo2 = FindMethod(managerType, "FromXml", 3);
				if ((object)methodInfo2 != null)
				{
					MethodInfo method5 = typeof(NodeControllerPatches).GetMethod("FromXmlTranspiler", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
					harmony.Patch((MethodBase)methodInfo2, (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(method5), (HarmonyMethod)null);
					_fromXmlPatched = true;
					Logging.Info("Expanded Node Controller Renewal FromXml node limit.");
				}
			}
			Logging.Info("Patched Node Controller Renewal Manager buffer access.");
			return true;
		}
		catch (Exception ex)
		{
			Logging.Error("Failed to patch Node Controller Renewal Manager: " + ex);
			return false;
		}
	}

	private static MethodInfo FindMethod(Type type, string name, int parameterCount)
	{
		MethodInfo[] methods = type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (MethodInfo methodInfo in methods)
		{
			if (string.Equals(methodInfo.Name, name, StringComparison.Ordinal) && methodInfo.GetParameters().Length == parameterCount)
			{
				return methodInfo;
			}
		}
		return null;
	}

	internal static void EnsureManagerBuffer(object manager)
	{
		if (manager == null)
		{
			return;
		}
		try
		{
			Type type = manager.GetType();
			FieldInfo fieldInfo = ThirdPartyCapacityCompatibility.FindBackingField(type, "Buffer") ?? ThirdPartyCapacityCompatibility.FindField(type, "Buffer");
			if ((object)fieldInfo == null)
			{
				return;
			}
			Type elementType = fieldInfo.FieldType.GetElementType();
			if ((object)elementType == null || !(fieldInfo.GetValue(manager) is Array array))
			{
				return;
			}
			int num = 65024;
			if (array.Length < num)
			{
				if (array.Length != 32768)
				{
					Logging.Warning("Node Controller Renewal Manager.Buffer has unexpected length " + array.Length + "; leaving it unchanged.");
					return;
				}
				Array array2 = Array.CreateInstance(elementType, num);
				Array.Copy(array, array2, array.Length);
				fieldInfo.SetValue(manager, array2);
				Logging.Info("Expanded Node Controller Renewal Manager.Buffer: " + array.Length + " -> " + num + ".");
			}
		}
		catch (Exception ex)
		{
			Logging.Error("Failed to expand Node Controller Renewal Manager.Buffer: " + ex);
		}
	}
}

}
