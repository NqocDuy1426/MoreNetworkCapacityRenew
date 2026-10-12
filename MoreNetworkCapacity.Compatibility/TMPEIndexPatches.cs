namespace MoreNetworkCapacity.Compatibility {

internal static class TMPEIndexPatches
{
	public static bool TurnOnRedGetIndexPrefix(ushort segmentId, bool startNode, ref int __result)
	{
		__result = segmentId + ((!startNode) ? 65024 : 0);
		return false;
	}

	public static bool SegmentEndGetIndexPrefix(ushort segmentId, bool startNode, ref int __result)
	{
		__result = segmentId + ((!startNode) ? 65024 : 0);
		return false;
	}

	public static bool SegmentEndGetSegmentAndNodeFromIndexPrefix(int index, ref ushort segmentId, ref bool startNode)
	{
		startNode = index < 65024;
		segmentId = (ushort)(index - ((!startNode) ? 65024 : 0));
		return false;
	}

	public static bool TrafficMeasurementGetDirIndexPrefix(ushort segmentId, NetInfo.Direction dir, ref int __result)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0004: Invalid comparison between Unknown and I4
		__result = segmentId + (((int)dir == 2) ? 65024 : 0);
		return false;
	}
}

}
