using System;
using System.Reflection;
using HarmonyLib;

namespace MoreNetworkCapacity.Compatibility {

internal static class NetworkSkinsCompatibility
{
	private const string AssemblyName = "NetworkSkins";

	private const string ModName = "NetworkSkins";

	private static bool _awakePatched;

	private static Type _managerType;

	internal static void Apply(Harmony harmony)
	{
		Assembly assembly = ThirdPartyCapacityCompatibility.FindLoadedAssembly("NetworkSkins");
		if ((object)assembly == null)
		{
			Logging.Info("NetworkSkins is not loaded; compatibility is inactive.");
			return;
		}
		Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "NetworkSkins.Skins.NetworkSkinManager", "NetworkSkins");
		if ((object)type != null)
		{
			_managerType = type;
			ExpandManager();
			PatchAwake(harmony, type);
			Logging.Info("NetworkSkins compatibility pass completed.");
		}
	}

	public static void AwakePostfix()
	{
		ExpandManager();
	}

	private static void ExpandManager()
	{
		Type managerType = _managerType;
		if ((object)managerType != null)
		{
			ArrayCompatibility.ExpandDirect(ThirdPartyCapacityCompatibility.FindField(managerType, "SegmentSkins"), null, 36864, 65024, null, createIfNull: true, "NetworkSkins NetworkSkinManager.SegmentSkins");
			ArrayCompatibility.ExpandDirect(ThirdPartyCapacityCompatibility.FindField(managerType, "NodeSkins"), null, 32768, 65024, null, createIfNull: true, "NetworkSkins NetworkSkinManager.NodeSkins");
		}
	}

	private static void PatchAwake(Harmony harmony, Type type)
	{
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Expected O, but got Unknown
		if (!_awakePatched && harmony != null)
		{
			MethodInfo method = type.GetMethod("Awake", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
			if ((object)method == null)
			{
				Logging.Warning("Could not find NetworkSkins NetworkSkinManager.Awake.");
				return;
			}
			MethodInfo method2 = typeof(NetworkSkinsCompatibility).GetMethod("AwakePostfix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			harmony.Patch((MethodBase)method, (HarmonyMethod)null, new HarmonyMethod(method2), (HarmonyMethod)null, (HarmonyMethod)null);
			_awakePatched = true;
			Logging.Info("Patched NetworkSkins NetworkSkinManager.Awake for post-construction array expansion.");
		}
	}
}

}
