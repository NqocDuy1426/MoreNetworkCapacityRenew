using System;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace MoreNetworkCapacity.Patches {

internal static class RenderGroupMeshDataPatch
{
	private static int _fixCount;

	internal static void Apply(Harmony harmony)
	{
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Expected O, but got Unknown
		Type nestedType = typeof(RenderGroup).GetNestedType("MeshData", BindingFlags.Public | BindingFlags.NonPublic);
		if ((object)nestedType == null)
		{
			throw new MissingMemberException("RenderGroup.MeshData type was not found.");
		}
		MethodInfo[] methods = nestedType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		int num = 0;
		foreach (MethodInfo methodInfo in methods)
		{
			ParameterInfo[] parameters = methodInfo.GetParameters();
			if (!(methodInfo.Name != "PopulateMesh") && parameters.Length == 1)
			{
				try
				{
					harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(RenderGroupMeshDataPatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
					num++;
					Logging.Info(string.Concat("RenderGroup.MeshData patched: ", methodInfo, "."));
				}
				catch (Exception ex)
				{
					Logging.Warning(string.Concat("RenderGroup.MeshData patch skipped for ", methodInfo, ": ", ex.GetType().Name, "."));
				}
			}
		}
		Logging.Info("RenderGroup.MeshData patch pass completed: patched=" + num + ", candidates=" + methods.Length + ".");
		PatchMeshExtensionsSetUvs(harmony);
	}

	private static void Prefix(object __instance)
	{
		if (__instance == null)
		{
			return;
		}
		FieldInfo fieldInfo = AccessTools.Field(__instance.GetType(), "m_vertices");
		FieldInfo fieldInfo2 = AccessTools.Field(__instance.GetType(), "m_uvs4");
		if ((object)fieldInfo == null || (object)fieldInfo2 == null)
		{
			return;
		}
		Vector3[] array = fieldInfo.GetValue(__instance) as Vector3[];
		Vector4[] array2 = fieldInfo2.GetValue(__instance) as Vector4[];
		if (array != null && array2 != null && array.Length != array2.Length)
		{
			Vector4[] array3 = (Vector4[])(object)new Vector4[array.Length];
			Array.Copy(array2, array3, Math.Min(array2.Length, array3.Length));
			fieldInfo2.SetValue(__instance, array3);
			if (Interlocked.Increment(ref _fixCount) <= 8)
			{
				Logging.Warning("Normalized RenderGroup.MeshData uv4 length from " + array2.Length + " to vertex count " + array.Length + ".");
			}
		}
	}

	private static void PatchMeshExtensionsSetUvs(Harmony harmony)
	{
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e4: Expected O, but got Unknown
		Type type = typeof(RenderGroup).Assembly.GetType("MeshExtensions");
		if ((object)type == null)
		{
			Logging.Warning("MeshExtensions type was not found.");
			return;
		}
		MethodInfo methodInfo = null;
		MethodInfo[] methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (MethodInfo methodInfo2 in methods)
		{
			ParameterInfo[] parameters = methodInfo2.GetParameters();
			if (!(methodInfo2.Name != "SetUVs") && parameters.Length == 3 && (object)parameters[0].ParameterType == typeof(Mesh) && (object)parameters[1].ParameterType == typeof(int) && (object)parameters[2].ParameterType == typeof(Vector4[]))
			{
				methodInfo = methodInfo2;
				break;
			}
		}
		if ((object)methodInfo == null)
		{
			Logging.Warning("MeshExtensions.SetUVs target was not found.");
			return;
		}
		try
		{
			MethodInfo method = typeof(RenderGroupMeshDataPatch).GetMethod("SetUvsPrefix", BindingFlags.Static | BindingFlags.NonPublic);
			harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(method), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			Logging.Info("MeshExtensions.SetUVs length guard patched.");
		}
		catch (Exception ex)
		{
			Logging.Warning("MeshExtensions.SetUVs patch skipped: " + ex.GetType().Name + ".");
		}
	}

	private static void SetUvsPrefix(Mesh __0, ref Vector4[] __2)
	{
		if (!((Object)(object)__0 == (Object)null) && __2 != null && __0.vertexCount != __2.Length)
		{
			int num = __2.Length;
			int vertexCount = __0.vertexCount;
			Vector4[] array = (Vector4[])(object)new Vector4[vertexCount];
			Array.Copy(__2, array, Math.Min(__2.Length, array.Length));
			__2 = array;
			if (Interlocked.Increment(ref _fixCount) <= 8)
			{
				Logging.Warning("Normalized MeshExtensions.SetUVs array from " + num + " to mesh vertex count " + vertexCount + ".");
			}
		}
	}
}

}
