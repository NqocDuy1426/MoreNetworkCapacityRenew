using System;
using System.Reflection;
using ColossalFramework;
using MoreNetworkCapacity.Patches;
using UnityEngine;

namespace MoreNetworkCapacity {

internal static class NetManagerCustomizer
{
	private static bool _loopLimitActivationPending;

	private static readonly FieldInfo NodesField = typeof(NetManager).GetField("m_nodes");

	private static readonly FieldInfo SegmentsField = typeof(NetManager).GetField("m_segments");

	private static readonly FieldInfo UpdatedNodesField = typeof(NetManager).GetField("m_updatedNodes");

	private static readonly FieldInfo UpdatedSegmentsField = typeof(NetManager).GetField("m_updatedSegments");

	private static readonly FieldInfo AdjustedSegmentsField = typeof(NetManager).GetField("m_adjustedSegments");

	private static readonly FieldInfo NodesUpdatedField = typeof(NetManager).GetField("m_nodesUpdated");

	private static readonly FieldInfo SegmentsUpdatedField = typeof(NetManager).GetField("m_segmentsUpdated");

	private static readonly FieldInfo CoverageNodesField = typeof(CoverageManager).GetField("m_nodes");

	private static readonly FieldInfo CoverageDistancesField = typeof(CoverageManager).GetField("m_distances");

	private static readonly FieldInfo ElectricityNodeGroupsField = typeof(ElectricityManager).GetField("m_nodeGroups");

	private static readonly FieldInfo ElectricityPulseUnitsField = typeof(ElectricityManager).GetField("m_pulseUnits");

	private static readonly FieldInfo WaterWaterPulseUnitsField = typeof(WaterManager).GetField("m_waterPulseUnits");

	private static readonly FieldInfo WaterSewagePulseUnitsField = typeof(WaterManager).GetField("m_sewagePulseUnits");

	private static readonly FieldInfo WaterHeatingPulseUnitsField = typeof(WaterManager).GetField("m_heatingPulseUnits");

	private static readonly FieldInfo WaterNodeDataField = typeof(WaterManager).GetField("m_nodeData");

	public static bool IsGameplayMode()
	{
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Invalid comparison between Unknown and I4
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Invalid comparison between Unknown and I4
		ToolManager instance = Singleton<ToolManager>.instance;
		if ((Object)(object)instance == (Object)null || (Object)(object)((SimulationManagerBase<ToolManager, ToolController>)(object)instance).m_properties == (Object)null)
		{
			return false;
		}
		Availability mode = ((SimulationManagerBase<ToolManager, ToolController>)(object)instance).m_properties.m_mode;
		bool flag = (mode & 2) != 0;
		bool flag2 = (mode & 4) != 0;
		if (!flag)
		{
			return !flag2;
		}
		return false;
	}

	public static bool IsGameplayLoadMode()
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Expected I4, but got Unknown
		SimulationManager instance = Singleton<SimulationManager>.instance;
		if ((Object)(object)instance != (Object)null && instance.m_metaData != null)
		{
			UpdateMode updateMode = instance.m_metaData.m_updateMode;
			switch ((int)updateMode)
			{
			case 0:
			case 1:
			case 4:
			case 5:
			case 6:
			case 7:
				return false;
			case 2:
			case 3:
			case 8:
			case 9:
			case 10:
			case 11:
			case 12:
			case 13:
				return true;
			}
		}
		return IsGameplayMode();
	}

	public static bool PrepareForLoad(NetManager manager)
	{
		if (!PatchState.ExpansionEnabled || (Object)(object)manager == (Object)null)
		{
			return false;
		}
		if (!IsGameplayMode())
		{
			UpdateLoopLimits(manager);
			return IsExpanded(manager);
		}
		return EnsureExpanded(manager);
	}

	public static bool PrepareExpandedSaveForLoad(NetManager manager)
	{
		if (!PatchState.ExpansionEnabled || (Object)(object)manager == (Object)null)
		{
			return false;
		}
		if (!EnsureExpanded(manager))
		{
			return false;
		}
		ResetForGameplayLoad(manager);
		return IsExpanded(manager);
	}

	public static bool PrepareVanillaSaveForLoad(NetManager manager)
	{
		if ((Object)(object)manager == (Object)null)
		{
			return false;
		}
		if (!EnsureExpanded(manager))
		{
			return false;
		}
		ResetForGameplayLoad(manager);
		SetVanillaLoopLimits();
		return IsExpanded(manager);
	}

	public static bool ExpandPreservingLoadedData(NetManager manager)
	{
		if ((Object)(object)manager == (Object)null)
		{
			return false;
		}
		if (IsExpanded(manager))
		{
			UpdateLoopLimits(manager);
			Logging.Info("Network arrays were already expanded for load; skipping post-read replacement.");
			return true;
		}
		int bufferLength = GetBufferLength<NetNode>(manager.m_nodes);
		int bufferLength2 = GetBufferLength<NetSegment>(manager.m_segments);
		if (bufferLength != 32768 || bufferLength2 != 36864)
		{
			Logging.Error("Cannot preserve loaded network data while expanding: nodes=" + bufferLength + ", segments=" + bufferLength2 + ".");
			return false;
		}
		Array16<NetNode> val = CreateOrderedNodeArray(65024, manager.m_nodes);
		Array16<NetSegment> val2 = CreateOrderedSegmentArray(65024, manager.m_segments);
		if (val == null || val2 == null)
		{
			return false;
		}
		manager.m_nodes = val;
		manager.m_segments = val2;
		SetAuxiliaryBuffersPreserving(manager, 65024, 65024);
		UpdateLoopLimits(manager);
		Logging.Info("Expanded NetManager arrays after vanilla-size load: nodes=" + val.m_buffer.Length + ", segments=" + val2.m_buffer.Length + ".");
		return true;
	}

	public static void UpdateLoopLimits(NetManager manager)
	{
		if ((Object)(object)manager == (Object)null)
		{
			Capacity.NodeLoopLimit = 32768;
			Capacity.SegmentLoopLimit = 36864;
			return;
		}
		ApplyExpandedArraySizes(manager);
		if (GetBufferLength<NetNode>(manager.m_nodes) == 65024)
		{
			Capacity.NodeLoopLimit = 65024;
		}
		else
		{
			Capacity.NodeLoopLimit = 32768;
		}
		if (GetBufferLength<NetSegment>(manager.m_segments) == 65024)
		{
			Capacity.SegmentLoopLimit = 65024;
		}
		else
		{
			Capacity.SegmentLoopLimit = 36864;
		}
	}

	private static void ApplyExpandedArraySizes(NetManager manager)
	{
		if (manager.m_nodes != null && GetBufferLength<NetNode>(manager.m_nodes) == 65024 && manager.m_nodes.m_size != 64513)
		{
			manager.m_nodes.m_size = 64513u;
		}
		if (manager.m_segments != null && GetBufferLength<NetSegment>(manager.m_segments) == 65024 && manager.m_segments.m_size != 64513)
		{
			manager.m_segments.m_size = 64513u;
		}
	}

	private static void ApplyLoadedDataArraySizes(NetManager manager, bool hasHighNodes, bool hasHighSegments)
	{
		if (manager.m_nodes != null && GetBufferLength<NetNode>(manager.m_nodes) == 65024)
		{
			uint num = 64513u;
			if (manager.m_nodes.m_size != num)
			{
				manager.m_nodes.m_size = num;
			}
		}
		if (manager.m_segments != null && GetBufferLength<NetSegment>(manager.m_segments) == 65024)
		{
			uint num2 = 64513u;
			if (manager.m_segments.m_size != num2)
			{
				manager.m_segments.m_size = num2;
			}
		}
	}

	public static void UpdateLoopLimitsForLoadedData(NetManager manager)
	{
		if ((Object)(object)manager == (Object)null)
		{
			SetVanillaLoopLimits();
			return;
		}
		bool flag = HasCreatedNodeAboveVanillaLimit(manager);
		bool flag2 = HasCreatedSegmentAboveVanillaLimit(manager);
		Capacity.NodeLoopLimit = ((flag && GetBufferLength<NetNode>(manager.m_nodes) == 65024) ? 65024 : 32768);
		Capacity.SegmentLoopLimit = ((flag2 && GetBufferLength<NetSegment>(manager.m_segments) == 65024) ? 65024 : 36864);
		ApplyLoadedDataArraySizes(manager, flag, flag2);
	}

	public static void DeferLoopLimitActivation(NetManager manager)
	{
		if (!IsExpanded(manager))
		{
			_loopLimitActivationPending = false;
			return;
		}
		_loopLimitActivationPending = true;
		Logging.Warning("Expanded loop limits are deferred until the first gameplay frame completes: current=" + Capacity.NodeLoopLimit + "/" + Capacity.SegmentLoopLimit + ".");
	}

	public static void ActivateDeferredLoopLimits()
	{
		if (_loopLimitActivationPending)
		{
			_loopLimitActivationPending = false;
			NetManager instance = Singleton<NetManager>.instance;
			if (!IsExpanded(instance))
			{
				Logging.Error("Could not activate deferred expanded loop limits: NetManager is no longer expanded.");
				return;
			}
			UpdateLoopLimits(instance);
			NetManagerColorUpdatePatch.RefreshExpandedColors();
			Logging.Warning("Activated expanded loop limits after the first gameplay frame: " + Capacity.NodeLoopLimit + "/" + Capacity.SegmentLoopLimit + ".");
		}
	}

	public static void CancelDeferredLoopLimitActivation()
	{
		_loopLimitActivationPending = false;
	}

	public static bool EnsureExpanded(NetManager manager)
	{
		if ((Object)(object)manager == (Object)null)
		{
			return false;
		}
		if (IsExpanded(manager))
		{
			UpdateLoopLimits(manager);
			return true;
		}
		int bufferLength = GetBufferLength<NetNode>(manager.m_nodes);
		int bufferLength2 = GetBufferLength<NetSegment>(manager.m_segments);
		if (bufferLength != 32768 || bufferLength2 != 36864)
		{
			UpdateLoopLimits(manager);
			Logging.Error("Network arrays have an unexpected size. Another mod may already control NetManager.");
			return false;
		}
		Array16<NetNode> val = CreateOrderedNodeArray(65024);
		Array16<NetSegment> val2 = CreateOrderedSegmentArray(65024);
		if (val == null || val2 == null)
		{
			UpdateLoopLimits(manager);
			return false;
		}
		NodesField.SetValue(manager, val);
		SegmentsField.SetValue(manager, val2);
		SetAuxiliaryBuffers(manager, 65024, 65024);
		UpdateLoopLimits(manager);
		Logging.Info("Expanded NetManager arrays: nodes=" + val.m_buffer.Length + ", segments=" + val2.m_buffer.Length);
		return true;
	}

	public static bool IsExpanded(NetManager manager)
	{
		if ((Object)(object)manager != (Object)null && GetBufferLength<NetNode>(manager.m_nodes) == 65024)
		{
			return GetBufferLength<NetSegment>(manager.m_segments) == 65024;
		}
		return false;
	}

	public static void RebuildFreeLists(NetManager manager)
	{
		if (!((Object)(object)manager == (Object)null))
		{
			RebuildNodeFreeList(manager.m_nodes);
			RebuildSegmentFreeList(manager.m_segments);
		}
	}

	public static void ResetForGameplayLoad(NetManager manager)
	{
		if (!((Object)(object)manager == (Object)null))
		{
			int bufferLength = GetBufferLength<NetNode>(manager.m_nodes);
			int bufferLength2 = GetBufferLength<NetSegment>(manager.m_segments);
			ClearArray((manager.m_nodes == null) ? null : manager.m_nodes.m_buffer);
			ClearArray((manager.m_segments == null) ? null : manager.m_segments.m_buffer);
			if (manager.m_nodes != null)
			{
				manager.m_nodes.ClearUnused();
			}
			if (manager.m_segments != null)
			{
				manager.m_segments.ClearUnused();
			}
			SetAuxiliaryBuffers(manager, bufferLength, bufferLength2);
			Logging.Info("Reset network and node-indexed buffers for load: nodes=" + bufferLength + ", segments=" + bufferLength2 + ".");
		}
	}

	public static void ResetPendingUpdatesAfterLoad(NetManager manager)
	{
		if (PatchState.ExpansionEnabled && !((Object)(object)manager == (Object)null) && IsExpanded(manager))
		{
			int num = CountHighNodeIds(manager);
			int num2 = CountHighSegmentIds(manager);
			ulong[] array = UpdatedNodesField.GetValue(manager) as ulong[];
			ulong[] array2 = UpdatedSegmentsField.GetValue(manager) as ulong[];
			ulong[] bitmap = AdjustedSegmentsField.GetValue(manager) as ulong[];
			int num3 = CountSetBits(array);
			int num4 = CountSetBits(array2);
			int num5 = CountSetBits(bitmap);
			bool flag = (object)NodesUpdatedField != null && (bool)NodesUpdatedField.GetValue(manager);
			bool flag2 = (object)SegmentsUpdatedField != null && (bool)SegmentsUpdatedField.GetValue(manager);
			ClearArray(array);
			ClearArray(array2);
			if ((object)NodesUpdatedField != null)
			{
				NodesUpdatedField.SetValue(manager, false);
			}
			if ((object)SegmentsUpdatedField != null)
			{
				SegmentsUpdatedField.SetValue(manager, false);
			}
			Logging.Warning("Cleared pending network update work before simulation warm-up: nodes=" + num3 + " bits (flag=" + flag + "), segments=" + num4 + " bits (flag=" + flag2 + "), adjusted segments left intact=" + num5 + " bits, expanded-save high ids=" + num + "/" + num2 + " (loading expanded=" + NetManagerDeserializeState.LoadingExpanded + ").");
		}
	}

	public static int CountHighNodeIds(NetManager manager)
	{
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)manager == (Object)null || manager.m_nodes == null || manager.m_nodes.m_buffer == null)
		{
			return 0;
		}
		int num = 0;
		NetNode[] buffer = manager.m_nodes.m_buffer;
		for (int i = 32768; i < buffer.Length; i++)
		{
			if ((buffer[i].m_flags & 1) != 0)
			{
				num++;
			}
		}
		return num;
	}

	public static int CountHighSegmentIds(NetManager manager)
	{
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)manager == (Object)null || manager.m_segments == null || manager.m_segments.m_buffer == null)
		{
			return 0;
		}
		int num = 0;
		NetSegment[] buffer = manager.m_segments.m_buffer;
		for (int i = 36864; i < buffer.Length; i++)
		{
			if ((buffer[i].m_flags & 1) != 0)
			{
				num++;
			}
		}
		return num;
	}

	public static int CountPendingNodeUpdates()
	{
		NetManager instance = Singleton<NetManager>.instance;
		if (!((Object)(object)instance == (Object)null))
		{
			return CountSetBits(UpdatedNodesField.GetValue(instance) as ulong[]);
		}
		return 0;
	}

	public static int CountPendingSegmentUpdates()
	{
		NetManager instance = Singleton<NetManager>.instance;
		if (!((Object)(object)instance == (Object)null))
		{
			return CountSetBits(UpdatedSegmentsField.GetValue(instance) as ulong[]);
		}
		return 0;
	}

	private static bool HasCreatedNodeAboveVanillaLimit(NetManager manager)
	{
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)manager == (Object)null || manager.m_nodes == null || manager.m_nodes.m_buffer == null)
		{
			return false;
		}
		NetNode[] buffer = manager.m_nodes.m_buffer;
		int num = Math.Min(buffer.Length, 65024);
		for (int i = 32768; i < num; i++)
		{
			if ((buffer[i].m_flags & 1) != 0)
			{
				return true;
			}
		}
		return false;
	}

	private static bool HasCreatedSegmentAboveVanillaLimit(NetManager manager)
	{
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)manager == (Object)null || manager.m_segments == null || manager.m_segments.m_buffer == null)
		{
			return false;
		}
		NetSegment[] buffer = manager.m_segments.m_buffer;
		int num = Math.Min(buffer.Length, 65024);
		for (int i = 36864; i < num; i++)
		{
			if ((buffer[i].m_flags & 1) != 0)
			{
				return true;
			}
		}
		return false;
	}

	private static void SetVanillaLoopLimits()
	{
		Capacity.NodeLoopLimit = 32768;
		Capacity.SegmentLoopLimit = 36864;
		NetManager instance = Singleton<NetManager>.instance;
		if ((Object)(object)instance != (Object)null)
		{
			ApplyLoadedDataArraySizes(instance, hasHighNodes: false, hasHighSegments: false);
		}
	}

	private static Array16<NetNode> CreateOrderedNodeArray(int capacity)
	{
		return CreateOrderedNodeArray(capacity, null);
	}

	private static Array16<NetNode> CreateOrderedNodeArray(int capacity, Array16<NetNode> source)
	{
		Array16<NetNode> val = new Array16<NetNode>((uint)capacity);
		ushort num = default(ushort);
		if (!val.CreateItem(ref num) || num != 0)
		{
			Logging.Error("Failed to reserve node id 0.");
			return null;
		}
		if (source != null && source.m_buffer != null)
		{
			Array.Copy(source.m_buffer, val.m_buffer, Math.Min(source.m_buffer.Length, val.m_buffer.Length));
		}
		RebuildNodeFreeList(val);
		return val;
	}

	private static Array16<NetSegment> CreateOrderedSegmentArray(int capacity)
	{
		return CreateOrderedSegmentArray(capacity, null);
	}

	private static Array16<NetSegment> CreateOrderedSegmentArray(int capacity, Array16<NetSegment> source)
	{
		Array16<NetSegment> val = new Array16<NetSegment>((uint)capacity);
		ushort num = default(ushort);
		if (!val.CreateItem(ref num) || num != 0)
		{
			Logging.Error("Failed to reserve segment id 0.");
			return null;
		}
		if (source != null && source.m_buffer != null)
		{
			Array.Copy(source.m_buffer, val.m_buffer, Math.Min(source.m_buffer.Length, val.m_buffer.Length));
		}
		RebuildSegmentFreeList(val);
		return val;
	}

	private static void SetAuxiliaryBuffersPreserving(NetManager manager, int nodeCapacity, int segmentCapacity)
	{
		UpdatedNodesField.SetValue(manager, ResizeArray(UpdatedNodesField.GetValue(manager) as ulong[], nodeCapacity >> 6));
		UpdatedSegmentsField.SetValue(manager, ResizeArray(UpdatedSegmentsField.GetValue(manager) as ulong[], segmentCapacity >> 6));
		AdjustedSegmentsField.SetValue(manager, ResizeArray(AdjustedSegmentsField.GetValue(manager) as ulong[], segmentCapacity >> 6));
		ResizeNodeIndexedArray(Singleton<CoverageManager>.instance, CoverageNodesField, nodeCapacity);
		ResizeNodeIndexedArray(Singleton<CoverageManager>.instance, CoverageDistancesField, nodeCapacity);
		ResizeNodeIndexedArray(Singleton<ElectricityManager>.instance, ElectricityPulseUnitsField, nodeCapacity);
		ResizeNodeIndexedArray(Singleton<ElectricityManager>.instance, ElectricityNodeGroupsField, nodeCapacity);
		ResizeNodeIndexedArray(Singleton<WaterManager>.instance, WaterWaterPulseUnitsField, nodeCapacity);
		ResizeNodeIndexedArray(Singleton<WaterManager>.instance, WaterSewagePulseUnitsField, nodeCapacity);
		ResizeNodeIndexedArray(Singleton<WaterManager>.instance, WaterHeatingPulseUnitsField, nodeCapacity);
		ResizeNodeIndexedArray(Singleton<WaterManager>.instance, WaterNodeDataField, nodeCapacity);
	}

	private static ulong[] ResizeArray(ulong[] source, int length)
	{
		ulong[] array = new ulong[length];
		if (source != null)
		{
			Array.Copy(source, array, Math.Min(source.Length, array.Length));
		}
		return array;
	}

	private static void ResizeNodeIndexedArray(object manager, FieldInfo field, int capacity)
	{
		if (manager == null || (object)field == null || capacity <= 0)
		{
			return;
		}
		Type elementType = field.FieldType.GetElementType();
		if ((object)elementType == null)
		{
			Logging.Warning("Could not resize non-array field " + field.DeclaringType.Name + "." + field.Name + ".");
		}
		else
		{
			Array array = field.GetValue(manager) as Array;
			Array array2 = Array.CreateInstance(elementType, capacity);
			if (array != null)
			{
				Array.Copy(array, array2, Math.Min(array.Length, array2.Length));
			}
			field.SetValue(manager, array2);
		}
	}

	private static void SetAuxiliaryBuffers(NetManager manager, int nodeCapacity, int segmentCapacity)
	{
		UpdatedNodesField.SetValue(manager, new ulong[nodeCapacity >> 6]);
		UpdatedSegmentsField.SetValue(manager, new ulong[segmentCapacity >> 6]);
		AdjustedSegmentsField.SetValue(manager, new ulong[segmentCapacity >> 6]);
		ResetNodeIndexedArray(Singleton<CoverageManager>.instance, CoverageNodesField, nodeCapacity);
		ResetNodeIndexedArray(Singleton<CoverageManager>.instance, CoverageDistancesField, nodeCapacity);
		ResetNodeIndexedArray(Singleton<ElectricityManager>.instance, ElectricityPulseUnitsField, nodeCapacity);
		ResetNodeIndexedArray(Singleton<ElectricityManager>.instance, ElectricityNodeGroupsField, nodeCapacity);
		ResetNodeIndexedArray(Singleton<WaterManager>.instance, WaterWaterPulseUnitsField, nodeCapacity);
		ResetNodeIndexedArray(Singleton<WaterManager>.instance, WaterSewagePulseUnitsField, nodeCapacity);
		ResetNodeIndexedArray(Singleton<WaterManager>.instance, WaterHeatingPulseUnitsField, nodeCapacity);
		ResetNodeIndexedArray(Singleton<WaterManager>.instance, WaterNodeDataField, nodeCapacity);
	}

	private static void RebuildNodeFreeList(Array16<NetNode> array)
	{
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		if (array == null)
		{
			return;
		}
		array.ClearUnused();
		NetNode[] buffer = array.m_buffer;
		int num = Math.Min(buffer.Length, (int)array.m_size);
		for (int num2 = num - 1; num2 >= 1; num2--)
		{
			if ((buffer[num2].m_flags & 1) == 0)
			{
				array.ReleaseItem((ushort)num2);
			}
		}
		Logging.Info("Rebuilt node free list: buffer=" + buffer.Length + ", size=" + array.m_size + ".");
	}

	private static void RebuildSegmentFreeList(Array16<NetSegment> array)
	{
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		if (array == null)
		{
			return;
		}
		array.ClearUnused();
		NetSegment[] buffer = array.m_buffer;
		int num = Math.Min(buffer.Length, (int)array.m_size);
		for (int num2 = num - 1; num2 >= 1; num2--)
		{
			if ((buffer[num2].m_flags & 1) == 0)
			{
				array.ReleaseItem((ushort)num2);
			}
		}
		Logging.Info("Rebuilt segment free list: buffer=" + buffer.Length + ", size=" + array.m_size + ".");
	}

	private static void ClearArray(Array array)
	{
		if (array != null)
		{
			Array.Clear(array, 0, array.Length);
		}
	}

	private static int CountSetBits(ulong[] bitmap)
	{
		if (bitmap == null)
		{
			return 0;
		}
		int num = 0;
		for (int i = 0; i < bitmap.Length; i++)
		{
			ulong num2 = bitmap[i];
			while (num2 != 0)
			{
				num2 &= num2 - 1;
				num++;
			}
		}
		return num;
	}

	private static void ResetNodeIndexedArray(object manager, FieldInfo field, int capacity)
	{
		if (manager != null && (object)field != null && capacity > 0)
		{
			Type elementType = field.FieldType.GetElementType();
			if ((object)elementType == null)
			{
				Logging.Warning("Could not reset non-array field " + field.DeclaringType.Name + "." + field.Name + ".");
			}
			else
			{
				field.SetValue(manager, Array.CreateInstance(elementType, capacity));
			}
		}
	}

	private static int GetBufferLength<T>(Array16<T> array)
	{
		if (array != null && array.m_buffer != null)
		{
			return array.m_buffer.Length;
		}
		return 0;
	}
}

}
