using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using ColossalFramework;
using HarmonyLib;
using UnityEngine;

namespace MoreNetworkCapacity.Patches {

[HarmonyPatch(typeof(RenderManager), "Awake")]
internal static class RenderManagerAwakePatch
{
	private const int MethodToken = 100676925;

	private static readonly int[,] Replacements = new int[2, 2]
	{
		{ 7, 23 },
		{ 64, 187 }
	};

	[HarmonyPriority(800)]
	private static void Prefix()
	{
		ConfigureColorMapFields(null);
	}

	[HarmonyPriority(0)]
	private static void Postfix()
	{
		EnsureCapacity();
		RenderManager instance = Singleton<RenderManager>.instance;
		if (!((Object)(object)instance == (Object)null) && !((Object)(object)instance.m_objectColorMap == (Object)null))
		{
			InitializeColorMap(instance.m_objectColorMap);
			QueueBuildingColorRefresh();
		}
	}

	internal static void EnsureCapacity()
	{
		RenderManager instance = Singleton<RenderManager>.instance;
		if (!((Object)(object)instance == (Object)null))
		{
			ConfigureColorMapFields(instance, out var width, out var height);
			EnsureIndexArray(instance);
			EnsureColorMap(instance, Capacity.GetColorIndexCapacity(), width, height);
		}
	}

	private static void ConfigureColorMapFields(RenderManager manager)
	{
		ConfigureColorMapFields(manager, out var _, out var _);
	}

	private static void ConfigureColorMapFields(RenderManager manager, out int width, out int height)
	{
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		int colorIndexCapacity = Capacity.GetColorIndexCapacity();
		width = RenderManager.OBJECT_COLORMAP_WIDTH;
		height = RenderManager.OBJECT_COLORMAP_HEIGHT;
		if ((width <= 0 || height <= 0) && (Object)(object)manager != (Object)null && (Object)(object)manager.m_objectColorMap != (Object)null)
		{
			width = ((Texture)manager.m_objectColorMap).width;
			height = ((Texture)manager.m_objectColorMap).height;
		}
		if (width <= 0 || height <= 0)
		{
			width = NextPowerOfTwo(CeilingSquareRoot(colorIndexCapacity));
			height = NextPowerOfTwo((colorIndexCapacity + width - 1) / width);
		}
		else
		{
			width = NextPowerOfTwo(width);
			height = NextPowerOfTwo(height);
			if ((long)width * (long)height < colorIndexCapacity)
			{
				height = NextPowerOfTwo((colorIndexCapacity + width - 1) / width);
			}
		}
		if ((long)width * (long)height < colorIndexCapacity)
		{
			throw new InvalidOperationException("Object color map capacity is smaller than required.");
		}
		RenderManager.OBJECT_COLORMAP_WIDTH = width;
		RenderManager.OBJECT_COLORMAP_HEIGHT = height;
		RenderManager.DefaultColorLocation = new Vector4(0.5f / (float)width, 0.5f / (float)height, 0f, 0f);
		Logging.Info("Object color map configured as " + width + "x" + height + " (required entries=" + colorIndexCapacity + ").");
	}

	private static void EnsureIndexArray(RenderManager manager)
	{
		int colorIndexCapacity = Capacity.GetColorIndexCapacity();
		if (manager.m_indices == null || manager.m_indices.Length < colorIndexCapacity)
		{
			ushort[] indices = manager.m_indices;
			ushort[] array = new ushort[colorIndexCapacity];
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = ushort.MaxValue;
			}
			if (indices != null)
			{
				Array.Copy(indices, array, Math.Min(indices.Length, array.Length));
			}
			manager.m_indices = array;
			Logging.Info("Render instance index array expanded to " + array.Length + " entries.");
		}
	}

	private static void EnsureColorMap(RenderManager manager, int required, int width, int height)
	{
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Expected O, but got Unknown
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
		Texture2D objectColorMap = manager.m_objectColorMap;
		if ((Object)(object)objectColorMap != (Object)null && ((Texture)objectColorMap).width >= width && ((Texture)objectColorMap).height >= height && (long)((Texture)objectColorMap).width * (long)((Texture)objectColorMap).height >= required)
		{
			return;
		}
		Texture2D val = new Texture2D(width, height, (TextureFormat)5, false, false);
		((Texture)val).filterMode = (FilterMode)0;
		Color32[] array = (Color32[])(object)new Color32[width * height];
		Color32 val2 = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = val2;
		}
		if ((Object)(object)objectColorMap != (Object)null)
		{
			int num = Math.Min(((Texture)objectColorMap).width, width);
			int num2 = Math.Min(((Texture)objectColorMap).height, height);
			Color32[] pixels = objectColorMap.GetPixels32();
			if (num > 0 && num2 > 0)
			{
				for (int j = 0; j < num2; j++)
				{
					int num3 = j * width;
					int num4 = j * ((Texture)objectColorMap).width;
					for (int k = 0; k < num; k++)
					{
						ref Color32 reference = ref array[num3 + k];
						reference = pixels[num4 + k];
					}
				}
			}
		}
		val.SetPixels32(array);
		val.Apply();
		manager.m_objectColorMap = val;
		Shader.SetGlobalTexture("_ObjectColorMap", (Texture)(object)val);
		if ((Object)(object)objectColorMap != (Object)null)
		{
			Object.Destroy((Object)(object)objectColorMap);
		}
		Logging.Info("Object color-map texture resized to " + width + "x" + height + ".");
	}

	private static void InitializeColorMap(Texture2D texture)
	{
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		Color32[] array = (Color32[])(object)new Color32[((Texture)texture).width * ((Texture)texture).height];
		Color32 val = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = val;
		}
		texture.SetPixels32(array);
		texture.Apply();
		Logging.Info("Object color-map texture initialized to white: " + ((Texture)texture).width + "x" + ((Texture)texture).height + ".");
	}

	private static void QueueBuildingColorRefresh()
	{
		BuildingManager instance = Singleton<BuildingManager>.instance;
		if ((Object)(object)instance == (Object)null)
		{
			Logging.Warning("Could not queue a building color refresh: BuildingManager is not available.");
			return;
		}
		instance.UpdateBuildingColors();
		Logging.Info("Queued a full building color refresh after object color-map initialization.");
	}

	private static IEnumerable<CodeInstruction> Transpiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = new List<CodeInstruction>(instructions);
		if (original.MetadataToken != 100676925)
		{
			Logging.Warning("Runtime RenderManager.Awake token differs (expected " + 100676925 + ", found " + original.MetadataToken + "). Matching by method identity.");
		}
		MethodInfo capacityMethod = AccessTools.Method(typeof(Capacity), "GetColorIndexCapacity", (Type[])null, (Type[])null);
		List<int> list2 = FindColorCapacityIndexes(list);
		if (list2.Count != Replacements.GetLength(0))
		{
			Logging.Error("RenderManager color-index pattern mismatch: expected " + Replacements.GetLength(0) + ", found " + list2.Count + " in " + list.Count + " instructions. Leaving values unchanged.");
			return list;
		}
		for (int i = 0; i < list2.Count; i++)
		{
			ReplaceExactConstant(list, list2[i], capacityMethod);
		}
		Logging.Info("Render color-index capacity replaced in " + list2.Count + " locations.");
		return list;
	}

	private static List<int> FindColorCapacityIndexes(List<CodeInstruction> instructions)
	{
		List<int> list = new List<int>();
		for (int i = 0; i < instructions.Count; i++)
		{
			if (IsInt32Constant(instructions[i], 119042))
			{
				bool flag = i + 2 < instructions.Count && instructions[i + 1].opcode == OpCodes.Newarr && instructions[i + 2].opcode == OpCodes.Stfld;
				bool flag2 = i + 1 < instructions.Count && instructions[i + 1].opcode.FlowControl == FlowControl.Cond_Branch;
				if (flag || flag2)
				{
					list.Add(i);
				}
			}
		}
		return list;
	}

	private static void ReplaceExactConstant(List<CodeInstruction> instructions, int index, MethodInfo capacityMethod)
	{
		if (index < 0 || index >= instructions.Count)
		{
			throw new InvalidOperationException("Instruction index " + index + " is outside RenderManager.Awake.");
		}
		CodeInstruction val = instructions[index];
		if (!IsInt32Constant(val, 119042))
		{
			throw new InvalidOperationException("Unexpected RenderManager color-map IL at index " + index + ".");
		}
		val.opcode = OpCodes.Call;
		val.operand = capacityMethod;
	}

	private static bool IsInt32Constant(CodeInstruction instruction, int value)
	{
		if ((instruction.opcode == OpCodes.Ldc_I4 || instruction.opcode == OpCodes.Ldc_I4_S) && instruction.operand is int)
		{
			return (int)instruction.operand == value;
		}
		return false;
	}

	private static int CeilingSquareRoot(int value)
	{
		int i;
		for (i = 0; (long)i * (long)i < value; i++)
		{
		}
		return i;
	}

	private static int NextPowerOfTwo(int value)
	{
		int num;
		for (num = 1; num < value; num <<= 1)
		{
		}
		return num;
	}
}

}
