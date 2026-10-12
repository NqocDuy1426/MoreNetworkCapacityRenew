using UnityEngine;

namespace MoreNetworkCapacity {

internal static class Logging
{
	private const string Prefix = "[MoreNetworkCapacity] ";

	public static void Info(string message)
	{
		Debug.Log((object)("[MoreNetworkCapacity] " + message));
	}

	public static void Warning(string message)
	{
		Debug.LogWarning((object)("[MoreNetworkCapacity] " + message));
	}

	public static void Error(string message)
	{
		Debug.LogError((object)("[MoreNetworkCapacity] " + message));
	}
}

}
