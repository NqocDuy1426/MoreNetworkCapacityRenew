namespace MoreNetworkCapacity {

internal static class PatchState
{
	private static bool _expansionEnabled;

	private static bool _saveExpanded;

	public static bool ExpansionEnabled => _expansionEnabled;

	public static bool SaveExpanded => _saveExpanded;

	internal static void MarkEnabled()
	{
		_expansionEnabled = true;
	}

	internal static void MarkDisabled()
	{
		_expansionEnabled = false;
	}

	internal static void MarkSaveExpanded()
	{
		_saveExpanded = true;
	}

	internal static void MarkSaveVanilla()
	{
		_saveExpanded = false;
	}
}

}
