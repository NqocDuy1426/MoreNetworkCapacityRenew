using System;
using System.Reflection;

namespace MoreNetworkCapacity.Compatibility {

internal static class ZoningAdjusterCompatibility
{
	private const string AssemblyName = "ZoningAdjuster";

	private const string ModName = "Zoning Adjuster";

	internal static void Apply()
	{
		Assembly assembly = ThirdPartyCapacityCompatibility.FindLoadedAssembly("ZoningAdjuster");
		if ((object)assembly == null)
		{
			Logging.Info("Zoning Adjuster is not loaded; compatibility is inactive.");
			return;
		}
		Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "ZoningAdjuster.SegmentData", "Zoning Adjuster");
		if ((object)type != null)
		{
			object staticInstance = ArrayCompatibility.GetStaticInstance(type, "Instance", "s_instance", "Zoning Adjuster SegmentData");
			FieldInfo field = ThirdPartyCapacityCompatibility.FindField(type, "_zoneBlockFlags");
			ArrayCompatibility.ExpandDirect(field, staticInstance, 0, 65024, null, createIfNull: false, "Zoning Adjuster SegmentData._zoneBlockFlags");
			Logging.Info("Zoning Adjuster compatibility pass completed.");
		}
	}
}

}
