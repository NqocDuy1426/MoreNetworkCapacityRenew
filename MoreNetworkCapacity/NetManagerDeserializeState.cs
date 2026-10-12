namespace MoreNetworkCapacity {

public static class NetManagerDeserializeState
{
	private static bool _loadingExpanded;

	public static bool LoadingExpanded => _loadingExpanded;

	internal static void SetLoadingExpanded(bool value)
	{
		_loadingExpanded = value;
		Capacity.NodeStorageSize = (value ? 65024 : 32768);
	}

	internal static void ResetLoadingExpanded()
	{
		_loadingExpanded = false;
		Capacity.NodeStorageSize = (PatchState.SaveExpanded ? 65024 : 32768);
	}
}

}
