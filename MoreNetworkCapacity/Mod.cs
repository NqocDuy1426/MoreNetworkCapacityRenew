using ICities;

namespace MoreNetworkCapacity {

public sealed class Mod : IUserMod
{
	public string Name => "More Network Capacity 1.0.15";

	public string Description => "Expands network node and segment capacity at runtime. Expanded saves must not be loaded without this mod.";

	public void OnEnabled()
	{
		HarmonyBootstrap.Enable();
	}

	public void OnDisabled()
	{
		HarmonyBootstrap.Disable();
	}
}

}
