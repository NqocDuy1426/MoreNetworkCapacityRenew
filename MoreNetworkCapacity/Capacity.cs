using ColossalFramework;
using UnityEngine;

namespace MoreNetworkCapacity {

public static class Capacity
{
	public const int VanillaNodeCapacity = 32768;

	public const int VanillaSegmentCapacity = 36864;

	public const int VanillaNodeSize = 32768;

	public const int VanillaSegmentSize = 36864;

	public const int ExpandedNodeCapacity = 65024;

	public const int ExpandedSegmentCapacity = 65024;

	public const int ExpandedNodeStop = 64512;

	public const int ExpandedSegmentStop = 64512;

	public const int ExpandedNodeSize = 64513;

	public const int ExpandedSegmentSize = 64513;

	public const int VanillaColorIndexCapacity = 119042;

	public const int SegmentColorIndexBase = 49152;

	public const int VanillaNodeColorIndexBase = 86016;

	public const int VanillaEventColorIndexBase = 118784;

	public const int EventColorIndexCount = 256;

	public const int ExpandedColorIndexCapacity = 180224;

	public static int NodeLoopLimit = 32768;

	public static int SegmentLoopLimit = 36864;

	public static int NodeStorageSize = 32768;

	public static int GetNodeBufferLength()
	{
		NetManager instance = Singleton<NetManager>.instance;
		if ((Object)(object)instance == (Object)null || instance.m_nodes == null || instance.m_nodes.m_buffer == null)
		{
			return 32768;
		}
		return instance.m_nodes.m_buffer.Length;
	}

	public static int GetSegmentBufferLength()
	{
		NetManager instance = Singleton<NetManager>.instance;
		if ((Object)(object)instance == (Object)null || instance.m_segments == null || instance.m_segments.m_buffer == null)
		{
			return 36864;
		}
		return instance.m_segments.m_buffer.Length;
	}

	public static int GetNodeWriteSize()
	{
		int nodeBufferLength = GetNodeBufferLength();
		if (PatchState.SaveExpanded)
		{
			return nodeBufferLength;
		}
		if (nodeBufferLength >= 32768)
		{
			return 32768;
		}
		return nodeBufferLength;
	}

	public static int GetSegmentWriteSize()
	{
		int segmentBufferLength = GetSegmentBufferLength();
		if (PatchState.SaveExpanded)
		{
			return segmentBufferLength;
		}
		if (segmentBufferLength >= 36864)
		{
			return 36864;
		}
		return segmentBufferLength;
	}

	public static int GetNodeStopLimit()
	{
		int nodeBufferLength = GetNodeBufferLength();
		int num = nodeBufferLength - 512;
		if (num > 64512)
		{
			num = 64512;
		}
		return num;
	}

	public static int GetSegmentStopLimit()
	{
		int segmentBufferLength = GetSegmentBufferLength();
		int num = segmentBufferLength - 512;
		if (num > 64512)
		{
			num = 64512;
		}
		return num;
	}

	public static int GetColorIndexCapacity()
	{
		return 180224;
	}

	public static int GetNodeColorIndexBase()
	{
		int num = SegmentLoopLimit;
		if (num < 36864)
		{
			num = 36864;
		}
		return 49152 + num;
	}

	public static int GetEventColorIndexBase()
	{
		int num = NodeLoopLimit;
		if (num < 32768)
		{
			num = 32768;
		}
		return GetNodeColorIndexBase() + num;
	}

	public static int GetAdaptiveRoadsSerializationCapacity()
	{
		return 4194304;
	}

	public static int GetNodeColorUpdateMax()
	{
		return NodeLoopLimit - 1;
	}

	public static int GetSegmentColorUpdateMax()
	{
		return SegmentLoopLimit - 1;
	}

	public static int GetNodeIdValidationLimit()
	{
		if (!PatchState.SaveExpanded && !NetManagerDeserializeState.LoadingExpanded && !Metadata.IsExpandedSave())
		{
			return 32768;
		}
		if (NodeLoopLimit <= 32768)
		{
			return 65024;
		}
		return NodeLoopLimit;
	}

	public static int GetElectricityNodeGroupLength()
	{
		if (!PatchState.SaveExpanded && !NetManagerDeserializeState.LoadingExpanded && !Metadata.IsExpandedSave())
		{
			return 32768;
		}
		ElectricityManager instance = Singleton<ElectricityManager>.instance;
		if ((Object)(object)instance == (Object)null || instance.m_nodeGroups == null)
		{
			return 65024;
		}
		return instance.m_nodeGroups.Length;
	}

	public static int GetNodeReadSize()
	{
		if (NetManagerDeserializeState.LoadingExpanded)
		{
			return GetNodeBufferLength();
		}
		int nodeBufferLength = GetNodeBufferLength();
		if (nodeBufferLength >= 32768)
		{
			return 32768;
		}
		return nodeBufferLength;
	}

	public static int GetAuxiliaryNodeStorageSize()
	{
		if (PatchState.SaveExpanded || NetManagerDeserializeState.LoadingExpanded || Metadata.IsExpandedSave())
		{
			return 65024;
		}
		return 32768;
	}

	public static int GetSegmentReadSize()
	{
		if (NetManagerDeserializeState.LoadingExpanded)
		{
			return GetSegmentBufferLength();
		}
		int segmentBufferLength = GetSegmentBufferLength();
		if (segmentBufferLength >= 36864)
		{
			return 36864;
		}
		return segmentBufferLength;
	}
}

}
