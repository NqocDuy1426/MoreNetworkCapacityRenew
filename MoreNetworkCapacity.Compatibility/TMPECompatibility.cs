using System;
using System.Reflection;
using HarmonyLib;

namespace MoreNetworkCapacity.Compatibility {

internal static class TMPECompatibility
{
	private const string AssemblyName = "TrafficManager";

	private const string ModName = "TM:PE";

	private static bool _turnOnRedIndexPatched;

	private static bool _segmentEndIndexPatched;

	private static bool _segmentEndDecodePatched;

	private static bool _trafficMeasurementIndexPatched;

	private static bool _laneConnectionDataPrepared;

	internal static void Apply(Harmony harmony)
	{
		Assembly assembly = ThirdPartyCapacityCompatibility.FindLoadedAssembly("TrafficManager");
		if ((object)assembly == null)
		{
			Logging.Info("TM:PE is not loaded; compatibility is inactive.");
			return;
		}
		ExpandExtSegmentManager(assembly);
		ExpandExtNodeManager(assembly);
		ExpandExtSegmentEndManager(assembly);
		ExpandTurnOnRedManager(assembly);
		ExpandSegmentEndManager(assembly);
		ExpandTrafficMeasurementManager(assembly);
		ExpandJunctionRestrictionsManager(assembly);
		ExpandTrafficPriorityManager(assembly);
		ExpandCustomSegmentLightsManager(assembly);
		ExpandVehicleRestrictionsManager(assembly);
		ExpandFlags(assembly);
		ExpandTrafficLightSimulationManager(assembly);
		ExpandParkingRestrictionsManager(assembly);
		ExpandRoutingManager(assembly);
		ExpandGeometryManager(assembly);
		ApplyIndexPatches(harmony, assembly);
		Logging.Info("TM:PE compatibility pass completed.");
	}

	internal static void PrepareForNetworkDeserialize(Harmony harmony, bool expandedSave)
	{
		Assembly assembly = ThirdPartyCapacityCompatibility.FindLoadedAssembly("TrafficManager");
		if ((object)assembly == null)
		{
			Logging.Info("TM:PE is not loaded; early network-load preparation is inactive.");
			return;
		}
		Apply(harmony);
		if (expandedSave)
		{
			PrepareLaneConnectionData(assembly, force: true);
		}
	}

	private static void PrepareLaneConnectionData(Assembly assembly, bool force)
	{
		if (_laneConnectionDataPrepared && !force)
		{
			return;
		}
		Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "TrafficManager.Manager.Impl.LaneConnection.LaneConnectionManager", "TM:PE");
		if ((object)type == null)
		{
			return;
		}
		object instance = GetInstance(type, "Instance", "LaneConnectionManager");
		if (instance == null)
		{
			return;
		}
		MethodInfo method = type.GetMethod("OnBeforeLoadData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
		if ((object)method == null)
		{
			Logging.Warning("Could not find TM:PE LaneConnectionManager.OnBeforeLoadData.");
		}
		else if (force || !_laneConnectionDataPrepared)
		{
			try
			{
				method.Invoke(instance, null);
				_laneConnectionDataPrepared = true;
				Logging.Info("Prepared TM:PE LaneConnectionManager before AdaptiveRoads network refresh.");
			}
			catch (Exception ex)
			{
				Logging.Error("Failed to prepare TM:PE LaneConnectionManager: " + ex);
			}
		}
	}

	private static void ExpandExtSegmentManager(Assembly assembly)
	{
		Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "TrafficManager.Manager.Impl.ExtSegmentManager", "TM:PE");
		if ((object)type != null)
		{
			object instance = GetInstance(type, "Instance", "ExtSegmentManager");
			FieldInfo field = FindMember(type, "ExtSegments");
			Type elementType = GetElementType(field, "TM:PE ExtSegments");
			ArrayCompatibility.ExpandDirect(field, instance, 36864, 65024, CreateUshortConstructorFactory(elementType), createIfNull: false, "TM:PE ExtSegmentManager.ExtSegments");
		}
	}

	private static void ExpandExtNodeManager(Assembly assembly)
	{
		Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "TrafficManager.Manager.Impl.ExtNodeManager", "TM:PE");
		if ((object)type != null)
		{
			object instance = GetInstance(type, "Instance", "ExtNodeManager");
			FieldInfo field = FindMember(type, "ExtNodes");
			Type elementType = GetElementType(field, "TM:PE ExtNodes");
			ArrayCompatibility.ExpandDirect(field, instance, 32768, 65024, CreateUshortConstructorFactory(elementType), createIfNull: false, "TM:PE ExtNodeManager.ExtNodes");
		}
	}

	private static void ExpandExtSegmentEndManager(Assembly assembly)
	{
		Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "TrafficManager.Manager.Impl.ExtSegmentEndManager", "TM:PE");
		if ((object)type != null)
		{
			object instance = GetInstance(type, "Instance", "ExtSegmentEndManager");
			FieldInfo field = FindMember(type, "ExtSegmentEnds");
			Type elementType = GetElementType(field, "TM:PE ExtSegmentEnds");
			ArrayCompatibility.ExpandDirect(field, instance, 73728, 130048, CreateExtSegmentEndFactory(elementType), createIfNull: false, "TM:PE ExtSegmentEndManager.ExtSegmentEnds");
		}
	}

	private static void ExpandTurnOnRedManager(Assembly assembly)
	{
		Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "TrafficManager.Manager.Impl.TurnOnRedManager", "TM:PE");
		if ((object)type != null)
		{
			object instance = GetInstance(type, "Instance", "TurnOnRedManager");
			FieldInfo field = FindMember(type, "TurnOnRedSegments");
			ArrayCompatibility.ExpandSplit(field, instance, 36864, 65024, null, "TM:PE TurnOnRedManager.TurnOnRedSegments");
		}
	}

	private static void ExpandSegmentEndManager(Assembly assembly)
	{
		Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "TrafficManager.Manager.Impl.SegmentEndManager", "TM:PE");
		if ((object)type != null)
		{
			object instance = GetInstance(type, "Instance", "SegmentEndManager");
			FieldInfo field = ThirdPartyCapacityCompatibility.FindField(type, "SegmentEnds");
			ArrayCompatibility.ExpandSplit(field, instance, 36864, 65024, null, "TM:PE SegmentEndManager.SegmentEnds");
		}
	}

	private static void ExpandTrafficMeasurementManager(Assembly assembly)
	{
		Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "TrafficManager.Manager.Impl.TrafficMeasurementManager", "TM:PE");
		if ((object)type != null)
		{
			object instance = GetInstance(type, "Instance", "TrafficMeasurementManager");
			FieldInfo field = FindMember(type, "LaneTrafficData");
			ArrayCompatibility.ExpandDirect(field, instance, 36864, 65024, null, createIfNull: false, "TM:PE TrafficMeasurementManager.LaneTrafficData");
			FieldInfo field2 = FindMember(type, "SegmentDirTrafficData");
			Type elementType = GetElementType(field2, "TM:PE SegmentDirTrafficData");
			ArrayCompatibility.ExpandSplit(field2, instance, 36864, 65024, CreateSegmentDirTrafficFactory(elementType), "TM:PE TrafficMeasurementManager.SegmentDirTrafficData");
		}
	}

	private static void ExpandJunctionRestrictionsManager(Assembly assembly)
	{
		Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "TrafficManager.Manager.Impl.JunctionRestrictionsManager", "TM:PE");
		if ((object)type != null)
		{
			object instance = GetInstance(type, "Instance", "JunctionRestrictionsManager");
			ArrayCompatibility.ExpandDirect(ThirdPartyCapacityCompatibility.FindField(type, "segmentRestrictions"), instance, 36864, 65024, null, createIfNull: false, "TM:PE JunctionRestrictionsManager.segmentRestrictions");
			ArrayCompatibility.ExpandDirect(ThirdPartyCapacityCompatibility.FindField(type, "invalidSegmentRestrictions"), instance, 36864, 65024, null, createIfNull: false, "TM:PE JunctionRestrictionsManager.invalidSegmentRestrictions");
		}
	}

	private static void ExpandTrafficPriorityManager(Assembly assembly)
	{
		Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "TrafficManager.Manager.Impl.TrafficPriorityManager", "TM:PE");
		if ((object)type != null)
		{
			object instance = GetInstance(type, "Instance", "TrafficPriorityManager");
			ArrayCompatibility.ExpandDirect(ThirdPartyCapacityCompatibility.FindField(type, "PrioritySegments"), instance, 36864, 65024, null, createIfNull: false, "TM:PE TrafficPriorityManager.PrioritySegments");
			ArrayCompatibility.ExpandDirect(ThirdPartyCapacityCompatibility.FindField(type, "invalidPrioritySegments"), instance, 36864, 65024, null, createIfNull: false, "TM:PE TrafficPriorityManager.invalidPrioritySegments");
		}
	}

	private static void ExpandCustomSegmentLightsManager(Assembly assembly)
	{
		Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "TrafficManager.Manager.Impl.CustomSegmentLightsManager", "TM:PE");
		if ((object)type != null)
		{
			object instance = GetInstance(type, "Instance", "CustomSegmentLightsManager");
			ArrayCompatibility.ExpandDirect(ThirdPartyCapacityCompatibility.FindField(type, "customSegments_"), instance, 36864, 65024, null, createIfNull: false, "TM:PE CustomSegmentLightsManager.customSegments_");
		}
	}

	private static void ExpandVehicleRestrictionsManager(Assembly assembly)
	{
		Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "TrafficManager.Manager.Impl.VehicleRestrictionsManager", "TM:PE");
		if ((object)type != null)
		{
			object instance = GetInstance(type, "Instance", "VehicleRestrictionsManager");
			ArrayCompatibility.ExpandDirect(ThirdPartyCapacityCompatibility.FindField(type, "defaultVehicleTypeCache"), instance, 36864, 65024, null, createIfNull: true, "TM:PE VehicleRestrictionsManager.defaultVehicleTypeCache");
		}
	}

	private static void ExpandFlags(Assembly assembly)
	{
		Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "TrafficManager.State.Flags", "TM:PE");
		if ((object)type != null)
		{
			ArrayCompatibility.ExpandDirect(ThirdPartyCapacityCompatibility.FindField(type, "laneAllowedVehicleTypesArray"), null, 36864, 65024, null, createIfNull: true, "TM:PE Flags.laneAllowedVehicleTypesArray");
		}
	}

	private static void ExpandTrafficLightSimulationManager(Assembly assembly)
	{
		Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "TrafficManager.Manager.Impl.TrafficLightSimulationManager", "TM:PE");
		if ((object)type != null)
		{
			object instance = GetInstance(type, "Instance", "TrafficLightSimulationManager");
			FieldInfo field = FindMember(type, "TrafficLightSimulations");
			Type elementType = GetElementType(field, "TM:PE TrafficLightSimulations");
			ArrayCompatibility.ExpandDirect(field, instance, 32768, 65024, CreateUshortConstructorFactory(elementType), createIfNull: false, "TM:PE TrafficLightSimulationManager.TrafficLightSimulations");
		}
	}

	private static void ExpandParkingRestrictionsManager(Assembly assembly)
	{
		Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "TrafficManager.Manager.Impl.ParkingRestrictionsManager", "TM:PE");
		if ((object)type != null)
		{
			object instance = GetInstance(type, "Instance", "ParkingRestrictionsManager");
			ArrayCompatibility.ExpandDirect(ThirdPartyCapacityCompatibility.FindField(type, "parkingAllowed"), instance, 36864, 65024, CreateParkingEntry, createIfNull: false, "TM:PE ParkingRestrictionsManager.parkingAllowed");
		}
	}

	private static void ExpandRoutingManager(Assembly assembly)
	{
		Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "TrafficManager.Manager.Impl.RoutingManager", "TM:PE");
		if ((object)type != null)
		{
			object instance = GetInstance(type, "Instance", "RoutingManager");
			ArrayCompatibility.ExpandDirect(ThirdPartyCapacityCompatibility.FindField(type, "updatedSegmentBuckets"), instance, 576, 1016, null, createIfNull: false, "TM:PE RoutingManager.updatedSegmentBuckets");
			ArrayCompatibility.ExpandDirect(FindMember(type, "SegmentRoutings"), instance, 36864, 65024, null, createIfNull: false, "TM:PE RoutingManager.SegmentRoutings");
		}
	}

	private static void ExpandGeometryManager(Assembly assembly)
	{
		Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "TrafficManager.Manager.Impl.GeometryManager", "TM:PE");
		if ((object)type != null)
		{
			object instance = GetInstance(type, "Instance", "GeometryManager");
			ArrayCompatibility.ExpandDirect(ThirdPartyCapacityCompatibility.FindField(type, "updatedSegmentBuckets"), instance, 576, 1016, null, createIfNull: false, "TM:PE GeometryManager.updatedSegmentBuckets");
			ArrayCompatibility.ExpandDirect(ThirdPartyCapacityCompatibility.FindField(type, "updatedNodeBuckets"), instance, 512, 1016, null, createIfNull: false, "TM:PE GeometryManager.updatedNodeBuckets");
		}
	}

	private static void ApplyIndexPatches(Harmony harmony, Assembly assembly)
	{
		if (harmony != null)
		{
			if (!_turnOnRedIndexPatched)
			{
				Type type = ThirdPartyCapacityCompatibility.FindType(assembly, "TrafficManager.Manager.Impl.TurnOnRedManager", "TM:PE");
				_turnOnRedIndexPatched = TryPatch(harmony, type, "GetIndex", new Type[2]
				{
					typeof(ushort),
					typeof(bool)
				}, "TurnOnRedGetIndexPrefix", "TM:PE TurnOnRedManager.GetIndex");
			}
			if (!_segmentEndIndexPatched)
			{
				Type type2 = ThirdPartyCapacityCompatibility.FindType(assembly, "TrafficManager.Manager.Impl.SegmentEndManager", "TM:PE");
				_segmentEndIndexPatched = TryPatch(harmony, type2, "GetIndex", new Type[2]
				{
					typeof(ushort),
					typeof(bool)
				}, "SegmentEndGetIndexPrefix", "TM:PE SegmentEndManager.GetIndex");
			}
			if (!_segmentEndDecodePatched)
			{
				Type type3 = ThirdPartyCapacityCompatibility.FindType(assembly, "TrafficManager.Manager.Impl.SegmentEndManager", "TM:PE");
				_segmentEndDecodePatched = TryPatch(harmony, type3, "GetSegmentAndNodeFromIndex", new Type[3]
				{
					typeof(int),
					typeof(ushort).MakeByRefType(),
					typeof(bool).MakeByRefType()
				}, "SegmentEndGetSegmentAndNodeFromIndexPrefix", "TM:PE SegmentEndManager.GetSegmentAndNodeFromIndex");
			}
			if (!_trafficMeasurementIndexPatched)
			{
				Type type4 = ThirdPartyCapacityCompatibility.FindType(assembly, "TrafficManager.Manager.Impl.TrafficMeasurementManager", "TM:PE");
				_trafficMeasurementIndexPatched = TryPatch(harmony, type4, "GetDirIndex", new Type[2]
				{
					typeof(ushort),
					typeof(Direction)
				}, "TrafficMeasurementGetDirIndexPrefix", "TM:PE TrafficMeasurementManager.GetDirIndex");
			}
		}
	}

	private static bool TryPatch(Harmony harmony, Type type, string methodName, Type[] arguments, string prefixName, string label)
	{
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Expected O, but got Unknown
		if ((object)type == null)
		{
			return false;
		}
		try
		{
			MethodInfo method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, arguments, null);
			if ((object)method == null)
			{
				Logging.Warning("Could not find " + label + " for compatibility patching.");
				return false;
			}
			MethodInfo method2 = typeof(TMPEIndexPatches).GetMethod(prefixName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if ((object)method2 == null)
			{
				Logging.Error("Could not find compatibility prefix " + prefixName + ".");
				return false;
			}
			harmony.Patch((MethodBase)method, new HarmonyMethod(method2), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			Logging.Info("Patched " + label + " for expanded offsets.");
			return true;
		}
		catch (Exception ex)
		{
			Logging.Error("Failed to patch " + label + ": " + ex);
			return false;
		}
	}

	private static object GetInstance(Type type, string memberName, string label)
	{
		object staticInstance = ArrayCompatibility.GetStaticInstance(type, memberName, memberName, "TM:PE " + label);
		if (staticInstance == null)
		{
			Logging.Warning("Could not access TM:PE " + label + " instance.");
		}
		return staticInstance;
	}

	private static FieldInfo FindMember(Type type, string name)
	{
		return ThirdPartyCapacityCompatibility.FindField(type, name) ?? ThirdPartyCapacityCompatibility.FindBackingField(type, name);
	}

	private static Type GetElementType(FieldInfo field, string label)
	{
		if ((object)field == null)
		{
			return null;
		}
		Type elementType = field.FieldType.GetElementType();
		if ((object)elementType == null)
		{
			Logging.Warning("Compatibility member is not an array: " + label + ".");
		}
		return elementType;
	}

	private static IndexedElementFactory CreateUshortConstructorFactory(Type elementType)
	{
		if ((object)elementType == null)
		{
			return null;
		}
		return (int index) => Activator.CreateInstance(elementType, (ushort)index);
	}

	private static IndexedElementFactory CreateExtSegmentEndFactory(Type elementType)
	{
		if ((object)elementType == null)
		{
			return null;
		}
		return delegate(int index)
		{
			ushort num = (ushort)(index / 2);
			bool flag = (index & 1) == 0;
			return Activator.CreateInstance(elementType, num, flag);
		};
	}

	private static SplitElementFactory CreateSegmentDirTrafficFactory(Type elementType)
	{
		if ((object)elementType == null)
		{
			return null;
		}
		FieldInfo meanSpeed = ThirdPartyCapacityCompatibility.FindField(elementType, "meanSpeed");
		return delegate
		{
			object obj = Activator.CreateInstance(elementType);
			if ((object)meanSpeed != null)
			{
				meanSpeed.SetValue(obj, (ushort)10000);
			}
			return obj;
		};
	}

	private static object CreateParkingEntry(int index)
	{
		return new bool[2] { true, true };
	}
}

}
