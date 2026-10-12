using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace MoreNetworkCapacity {

internal static class Metadata
{
	private const string ExpandedSaveKey = "User/MoreNetworkCapacity/64K-v1";

	public static bool IsExpandedSave()
	{
		SimulationManager instance = Singleton<SimulationManager>.instance;
		if ((Object)(object)instance == (Object)null || instance.m_metaData == null)
		{
			return false;
		}
		Dictionary<string, bool> modOverride = instance.m_metaData.m_modOverride;
		if (modOverride == null)
		{
			return false;
		}
		if (modOverride.TryGetValue("User/MoreNetworkCapacity/64K-v1", out var value))
		{
			return value;
		}
		return false;
	}

	public static void SetExpandedSave()
	{
		PatchState.MarkSaveExpanded();
		Capacity.NodeStorageSize = 65024;
		SimulationManager instance = Singleton<SimulationManager>.instance;
		if ((Object)(object)instance == (Object)null || instance.m_metaData == null)
		{
			return;
		}
		lock (instance.m_metaData)
		{
			if (instance.m_metaData.m_modOverride == null)
			{
				instance.m_metaData.m_modOverride = new Dictionary<string, bool>();
			}
			instance.m_metaData.m_modOverride["User/MoreNetworkCapacity/64K-v1"] = true;
		}
	}

	public static void ClearExpandedSave()
	{
		PatchState.MarkSaveVanilla();
		Capacity.NodeStorageSize = 32768;
		SimulationManager instance = Singleton<SimulationManager>.instance;
		if ((Object)(object)instance == (Object)null || instance.m_metaData == null)
		{
			return;
		}
		lock (instance.m_metaData)
		{
			if (instance.m_metaData.m_modOverride != null)
			{
				instance.m_metaData.m_modOverride.Remove("User/MoreNetworkCapacity/64K-v1");
			}
		}
	}
}

}
