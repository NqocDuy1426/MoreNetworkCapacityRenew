using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using ColossalFramework;
using HarmonyLib;
using UnityEngine;

namespace MoreNetworkCapacity {

internal static class SimulationStepDiagnostics
{
	private sealed class ActiveStep
	{
		public string Name;

		public DateTime StartedUtc;

		public int SubStep;
	}

	private const int CapturedFrameCount = 3;

	private const int FirstFrameWarningSeconds = 20;

	private const int WaitForFirstFrameSeconds = 30;

	private const int RepeatReportSeconds = 10;

	private const int MaximumReports = 8;

	private const int HistoryLimit = 512;

	private const int TraceLinesInReport = 48;

	private const int LoadingStateSampleMilliseconds = 1000;

	private static readonly object Sync = new object();

	private static readonly object PatchSync = new object();

	private static readonly object FileSync = new object();

	private static readonly List<ActiveStep> ActiveSteps = new List<ActiveStep>();

	private static readonly List<ActiveStep> ActiveAsyncTasks = new List<ActiveStep>();

	private static readonly List<ActiveStep> ActiveDetails = new List<ActiveStep>();

	private static readonly Queue<string> History = new Queue<string>();

	private static readonly HashSet<MethodBase> PatchedMethods = new HashSet<MethodBase>();

	private static readonly HashSet<string> PatchedTypeNames = new HashSet<string>();

	private static readonly Dictionary<int, int> TerrainIteratorStates = new Dictionary<int, int>();

	private static readonly FieldInfo AsyncTaskNameField = typeof(AsyncTaskBase).GetField("m_Name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static Harmony _harmony;

	private static Thread _watchdog;

	private static string _logPath;

	private static string _managerSnapshot = "unavailable";

	private static SimulationManager _simulationManager;

	private static LoadingManager _loadingManager;

	private static DateTime _monitorStartedUtc;

	private static DateTime _frameStartedUtc;

	private static DateTime _lastReportUtc;

	private static DateTime _simulationThreadStartedUtc;

	private static DateTime _lastFixedUpdateUtc;

	private static int _armed;

	private static int _enabled;

	private static int _stopWatchdog;

	private static int _frameActive;

	private static int _frameNumber;

	private static int _completedFrames;

	private static int _reportCount;

	private static int _colorUpdateCount;

	private static int _asyncTaskCount;

	private static int _fixedUpdateCount;

	private static int _patchedMethodCount;

	private static int _detailPatched;

	private static bool _logFailed;

	private static DateTime _lastLoadingStateSampleUtc;

	private static bool _loadingStatePatched;

	private static MethodBase _loadingManagerUpdate;

	private static MethodBase _loadingQueueLoadingAction;

	private static MethodBase _terrainUpdateData;

	private static MethodBase _terrainIteratorMoveNext;

	private static MethodBase _waterSimulationUpdatePatch;

	private static MethodBase _terrainLateUpdate;

	private static MethodBase _loadingWrapperOnLevelLoaded;

	private static FieldInfo _loadingQueueField;

	private static FieldInfo _loadingHasQueuedActionsField;

	private static MethodInfo _loadingQueuePeekMethod;

	private static FieldInfo _terrainIteratorIndexField;

	private static FieldInfo _terrainIteratorStateField;

	private static DateTime _lastQueueStateUtc;

	private static int _lastQueueCount = -1;

	private static string _lastQueueHeadType;

	private static bool _lastQueueHasQueuedActions;

	private static int _terrainUpdateActive;

	private static int _terrainUpdateEnqueueCount;

	private static DateTime _terrainUpdateStartedUtc;

	internal static bool IsEnabled => Interlocked.CompareExchange(ref _enabled, 0, 0) != 0;

	internal static bool IsArmed => Interlocked.CompareExchange(ref _armed, 0, 0) != 0;

	internal static void InstallEarly(Harmony harmony)
	{
		if (harmony != null)
		{
			_harmony = harmony;
			bool flag = Interlocked.CompareExchange(ref _armed, 1, 0) == 0;
			int num = PatchAllManagerMethods();
			_patchedMethodCount += num;
			int num2 = PatchNetworkDetailMethods();
			if (flag)
			{
				Logging.Info("Simulation diagnostics armed before the simulation thread starts (patched manager methods=" + num + ", network detail methods=" + num2 + ").");
			}
		}
	}

	internal static void InitializeEarly()
	{
		if (IsArmed && _harmony != null)
		{
			InitializeLog();
			PatchLoadingStateMethods();
			WriteLine("EARLY DIAGNOSTICS 1.0.15");
		}
	}

	internal static void BeginMonitoring(Harmony harmony, NetManager manager)
	{
		InstallEarly(harmony);
		if (!IsArmed || Interlocked.CompareExchange(ref _enabled, 1, 0) != 0)
		{
			return;
		}
		_simulationManager = Singleton<SimulationManager>.instance;
		_loadingManager = Singleton<LoadingManager>.instance;
		_monitorStartedUtc = DateTime.UtcNow;
		_lastReportUtc = DateTime.MinValue;
		if (_frameActive == 0)
		{
			_frameStartedUtc = DateTime.MinValue;
			_frameNumber = 0;
		}
		_completedFrames = 0;
		_reportCount = 0;
		_colorUpdateCount = 0;
		_asyncTaskCount = 0;
		_fixedUpdateCount = 0;
		_logFailed = false;
		_stopWatchdog = 0;
		_lastQueueStateUtc = DateTime.MinValue;
		_lastQueueCount = -1;
		_lastQueueHeadType = null;
		_lastQueueHasQueuedActions = false;
		lock (Sync)
		{
			if (_frameActive == 0)
			{
				ActiveSteps.Clear();
				ActiveAsyncTasks.Clear();
				ActiveDetails.Clear();
				History.Clear();
			}
		}
		InitializeLog();
		int num = PatchAllManagerMethods();
		_patchedMethodCount += num;
		_managerSnapshot = BuildRegisteredManagerSnapshot();
		int num2 = (((Object)(object)manager == (Object)null || manager.m_nodes == null) ? (-1) : manager.m_nodes.m_buffer.Length);
		int num3 = (((Object)(object)manager == (Object)null || manager.m_segments == null) ? (-1) : manager.m_segments.m_buffer.Length);
		WriteLine("BEGIN 1.0.15 | nodes=" + num2 + " | segments=" + num3 + " | loopLimits=" + Capacity.NodeLoopLimit + "/" + Capacity.SegmentLoopLimit + " | patchedManagerMethods=" + _patchedMethodCount + " | currentFrameActive=" + (_frameActive != 0) + " | currentFrameElapsed=" + ((_frameActive != 0) ? FormatSeconds(GetElapsedSeconds(_frameStartedUtc)) : "0.000s"));
		WriteLine("REGISTERED " + _managerSnapshot);
		WriteTerrainState();
		if (_logFailed || string.IsNullOrEmpty(_logPath))
		{
			Logging.Warning("Simulation diagnostics could not create their log file. Only the normal game log will contain the startup marker.");
		}
		else
		{
			Logging.Info("Simulation diagnostics enabled for the expanded save. Log: " + _logPath + " (patched manager methods=" + _patchedMethodCount + ").");
		}
		_watchdog = new Thread(WatchdogLoop);
		_watchdog.IsBackground = true;
		_watchdog.Name = "MoreNetworkCapacity-SimulationWatchdog";
		_watchdog.Start();
	}

	private static void PatchLoadingStateMethods()
	{
		if (!_loadingStatePatched)
		{
			_loadingStatePatched = true;
			TryPatchLoadingMethod(typeof(LoadingManager), "Update", Type.EmptyTypes, "LoadingManager.Update");
			TryPatchLoadingMethod(typeof(LoadingManager), "QueueLoadingAction", new Type[1] { typeof(IEnumerator) }, "LoadingManager.QueueLoadingAction");
			TryPatchLoadingMethod(typeof(TerrainManager), "UpdateData", new Type[1] { typeof(UpdateMode) }, "TerrainManager.UpdateData");
			TryPatchLoadingMethod(typeof(TerrainManager), "LateUpdateData", new Type[1] { typeof(UpdateMode) }, "TerrainManager.LateUpdateData");
			TryPatchLoadingMethod(typeof(LoadingWrapper), "OnLevelLoaded", new Type[1] { typeof(UpdateMode) }, "LoadingWrapper.OnLevelLoaded");
			TryPatchTerrainIterator();
			TryPatchWaterSimulation();
			WriteLine("LOADING PATCHES | loadingManager=" + ((object)_loadingManagerUpdate != null) + " queue=" + ((object)_loadingQueueLoadingAction != null) + " terrainUpdateData=" + ((object)_terrainUpdateData != null) + " terrainIterator=" + ((object)_terrainIteratorMoveNext != null) + " water=" + ((object)_waterSimulationUpdatePatch != null) + " terrain=" + ((object)_terrainLateUpdate != null) + " wrapper=" + ((object)_loadingWrapperOnLevelLoaded != null));
		}
	}

	private static void TryPatchLoadingMethod(Type type, string name, Type[] parameters, string label)
	{
		//IL_0136: Unknown result type (might be due to invalid IL or missing references)
		//IL_013d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0149: Unknown result type (might be due to invalid IL or missing references)
		//IL_0156: Expected O, but got Unknown
		//IL_0156: Expected O, but got Unknown
		MethodBase methodBase = FindDeclaredMethod(type, name, parameters);
		if ((object)methodBase == null)
		{
			Logging.Warning("Could not find loading diagnostic target " + label);
			return;
		}
		try
		{
			Logging.Info("Resolved loading diagnostic target " + label + " as " + GetMethodLabel(methodBase) + " | parameters=" + methodBase.GetParameters().Length + " | token=" + methodBase.MetadataToken);
			string name2 = ((name == "QueueLoadingAction") ? "QueueLoadingActionPrefix" : ((name == "UpdateData") ? "TerrainUpdateDataPrefix" : "LoadingPrefix"));
			string name3 = ((name == "QueueLoadingAction") ? "QueueLoadingActionPostfix" : ((name == "UpdateData") ? "TerrainUpdateDataPostfix" : "LoadingPostfix"));
			MethodInfo method = typeof(SimulationStepDiagnostics).GetMethod(name2, BindingFlags.Static | BindingFlags.NonPublic);
			MethodInfo method2 = typeof(SimulationStepDiagnostics).GetMethod(name3, BindingFlags.Static | BindingFlags.NonPublic);
			MethodInfo methodInfo = ((name == "UpdateData") ? typeof(SimulationStepDiagnostics).GetMethod("TerrainUpdateDataFinalizer", BindingFlags.Static | BindingFlags.NonPublic) : null);
			_harmony.Patch(methodBase, new HarmonyMethod(method), new HarmonyMethod(method2), (HarmonyMethod)null, ((object)methodInfo == null) ? ((HarmonyMethod)null) : new HarmonyMethod(methodInfo));
			if ((object)type == typeof(LoadingManager))
			{
				if (name == "Update")
				{
					_loadingManagerUpdate = methodBase;
				}
				else if (name == "QueueLoadingAction")
				{
					_loadingQueueLoadingAction = methodBase;
				}
			}
			else if ((object)type == typeof(TerrainManager))
			{
				if (name == "UpdateData")
				{
					_terrainUpdateData = methodBase;
				}
				else
				{
					_terrainLateUpdate = methodBase;
				}
			}
			else
			{
				_loadingWrapperOnLevelLoaded = methodBase;
			}
		}
		catch (Exception ex)
		{
			Logging.Warning("Could not attach loading diagnostics to " + label + ": " + ex.Message);
		}
	}

	private static MethodBase FindDeclaredMethod(Type type, string name, Type[] parameters)
	{
		Type type2 = type;
		while ((object)type2 != null)
		{
			try
			{
				MethodInfo methodInfo = AccessTools.DeclaredMethod(type2, name, parameters, (Type[])null);
				if ((object)methodInfo != null)
				{
					return methodInfo;
				}
			}
			catch
			{
			}
			try
			{
				List<MethodInfo> declaredMethods = AccessTools.GetDeclaredMethods(type2);
				for (int i = 0; i < declaredMethods.Count; i++)
				{
					MethodInfo methodInfo2 = declaredMethods[i];
					if (methodInfo2.Name != name)
					{
						continue;
					}
					ParameterInfo[] parameters2 = methodInfo2.GetParameters();
					if (parameters2.Length != parameters.Length)
					{
						continue;
					}
					bool flag = true;
					for (int j = 0; j < parameters.Length; j++)
					{
						if ((object)parameters2[j].ParameterType != parameters[j])
						{
							flag = false;
							break;
						}
					}
					if (flag)
					{
						return methodInfo2;
					}
				}
			}
			catch
			{
			}
			try
			{
				MethodInfo method = type2.GetMethod(name, BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, parameters, null);
				if ((object)method != null)
				{
					return method;
				}
			}
			catch (AmbiguousMatchException)
			{
				MethodInfo[] methods = type2.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				foreach (MethodInfo methodInfo3 in methods)
				{
					if (methodInfo3.Name != name)
					{
						continue;
					}
					ParameterInfo[] parameters3 = methodInfo3.GetParameters();
					if (parameters3.Length != parameters.Length)
					{
						continue;
					}
					bool flag2 = true;
					for (int l = 0; l < parameters.Length; l++)
					{
						if ((object)parameters3[l].ParameterType != parameters[l])
						{
							flag2 = false;
							break;
						}
					}
					if (flag2)
					{
						return methodInfo3;
					}
				}
			}
			catch
			{
			}
			type2 = type2.BaseType;
		}
		return null;
	}

	private static void TryPatchTerrainIterator()
	{
		//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00db: Expected O, but got Unknown
		//IL_00db: Expected O, but got Unknown
		Type[] nestedTypes = typeof(TerrainManager).GetNestedTypes(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (Type type in nestedTypes)
		{
			if (type.Name.IndexOf("UpdateTerrainPatch", StringComparison.Ordinal) < 0)
			{
				continue;
			}
			MethodBase methodBase = AccessTools.Method(type, "MoveNext", Type.EmptyTypes, (Type[])null);
			if ((object)methodBase != null)
			{
				_terrainIteratorIndexField = AccessTools.Field(type, "index");
				_terrainIteratorStateField = AccessTools.Field(type, "$PC");
				if ((object)_terrainIteratorIndexField == null || (object)_terrainIteratorStateField == null)
				{
					Logging.Warning("Terrain iterator fields were not found on " + type.FullName);
					return;
				}
				try
				{
					MethodInfo method = typeof(SimulationStepDiagnostics).GetMethod("TerrainIteratorPrefix", BindingFlags.Static | BindingFlags.NonPublic);
					MethodInfo method2 = typeof(SimulationStepDiagnostics).GetMethod("TerrainIteratorPostfix", BindingFlags.Static | BindingFlags.NonPublic);
					_harmony.Patch(methodBase, new HarmonyMethod(method), new HarmonyMethod(method2), (HarmonyMethod)null, (HarmonyMethod)null);
					_terrainIteratorMoveNext = methodBase;
					return;
				}
				catch (Exception ex)
				{
					Logging.Warning("Could not attach terrain iterator diagnostics: " + ex.Message);
					return;
				}
			}
		}
		Logging.Warning("Could not find TerrainManager.UpdateTerrainPatch iterator");
	}

	private static void TryPatchWaterSimulation()
	{
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Expected O, but got Unknown
		//IL_00ad: Expected O, but got Unknown
		//IL_00ad: Expected O, but got Unknown
		MethodBase methodBase = AccessTools.Method(typeof(WaterSimulation), "UpdatePatch", new Type[2]
		{
			typeof(int),
			typeof(int)
		}, (Type[])null);
		if ((object)methodBase == null)
		{
			Logging.Warning("Could not find WaterSimulation.UpdatePatch diagnostic target");
			return;
		}
		try
		{
			MethodInfo method = typeof(SimulationStepDiagnostics).GetMethod("WaterPatchPrefix", BindingFlags.Static | BindingFlags.NonPublic);
			MethodInfo method2 = typeof(SimulationStepDiagnostics).GetMethod("WaterPatchPostfix", BindingFlags.Static | BindingFlags.NonPublic);
			MethodInfo method3 = typeof(SimulationStepDiagnostics).GetMethod("WaterPatchFinalizer", BindingFlags.Static | BindingFlags.NonPublic);
			_harmony.Patch(methodBase, new HarmonyMethod(method), new HarmonyMethod(method2), (HarmonyMethod)null, new HarmonyMethod(method3));
			_waterSimulationUpdatePatch = methodBase;
		}
		catch (Exception ex)
		{
			Logging.Warning("Could not attach water patch diagnostics: " + ex.Message);
		}
	}

	private static void LoadingPrefix(MethodBase __originalMethod)
	{
		WriteLoadingState("ENTER " + GetMethodLabel(__originalMethod));
	}

	private static void LoadingPostfix(MethodBase __originalMethod)
	{
		WriteLoadingState("EXIT " + GetMethodLabel(__originalMethod));
	}

	private static void QueueLoadingActionPrefix(IEnumerator __0)
	{
		if (__0 == null)
		{
			WriteLine("QUEUE action=null");
			return;
		}
		string fullName = __0.GetType().FullName;
		if (fullName.IndexOf("TerrainManager+<UpdateTerrainPatch>", StringComparison.Ordinal) >= 0)
		{
			if (TryReadTerrainIteratorIndex(__0, out var index))
			{
				if (Interlocked.CompareExchange(ref _terrainUpdateActive, 0, 0) != 0)
				{
					Interlocked.Increment(ref _terrainUpdateEnqueueCount);
				}
				WriteLine("QUEUE terrain-enqueue | index=" + index + " | before=" + ReadLoadingQueueCount());
			}
			else
			{
				WriteLine("QUEUE terrain-enqueue | index=unavailable | before=" + ReadLoadingQueueCount());
			}
		}
		else if (fullName.IndexOf("RenderManager+<UpdateRenderGroup>", StringComparison.Ordinal) < 0)
		{
			WriteLine("QUEUE enqueue | before=" + ReadLoadingQueueCount() + " | action=" + fullName);
		}
	}

	private static void QueueLoadingActionPostfix()
	{
		RecordQueueState("QUEUE enqueue-complete");
	}

	private static void TerrainUpdateDataPrefix(TerrainManager __instance, SimulationManager.UpdateMode __0)
	{
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		_terrainUpdateStartedUtc = DateTime.UtcNow;
		Interlocked.Exchange(ref _terrainUpdateEnqueueCount, 0);
		Interlocked.Exchange(ref _terrainUpdateActive, 1);
		WriteLine(string.Concat("TERRAIN_UPDATE_DATA enter | mode=", __0, " | ", DescribeTerrainBuffers(__instance)));
	}

	private static void TerrainUpdateDataPostfix(TerrainManager __instance, SimulationManager.UpdateMode __0)
	{
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		int num = Interlocked.Exchange(ref _terrainUpdateEnqueueCount, 0);
		Interlocked.Exchange(ref _terrainUpdateActive, 0);
		WriteLine(string.Concat("TERRAIN_UPDATE_DATA exit | mode=", __0, " | terrainEnqueues=", num, " | elapsed=", FormatSeconds((_terrainUpdateStartedUtc == DateTime.MinValue) ? 0.0 : (DateTime.UtcNow - _terrainUpdateStartedUtc).TotalSeconds), " | ", DescribeTerrainBuffers(__instance)));
	}

	private static Exception TerrainUpdateDataFinalizer(Exception __exception, TerrainManager __instance, SimulationManager.UpdateMode __0)
	{
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		if (__exception == null)
		{
			return null;
		}
		int num = Interlocked.Exchange(ref _terrainUpdateEnqueueCount, 0);
		Interlocked.Exchange(ref _terrainUpdateActive, 0);
		WriteLine(string.Concat("TERRAIN_UPDATE_DATA exception | mode=", __0, " | terrainEnqueues=", num, " | exception=", __exception.GetType().FullName, " | message=", __exception.Message, " | details=", __exception.ToString().Replace(Environment.NewLine, " <- "), " | ", DescribeTerrainBuffers(__instance)));
		return __exception;
	}

	private static string DescribeTerrainBuffers(TerrainManager terrain)
	{
		if ((Object)(object)terrain == (Object)null)
		{
			return "terrain=null";
		}
		try
		{
			Array array = ReadTerrainArray(terrain, "m_patches");
			Array array2 = ReadTerrainArray(terrain, "m_blockHeights");
			Array array3 = ReadTerrainArray(terrain, "m_blockHeights2");
			return "lastUpdatedPatch=" + ReadTerrainInt(terrain, "m_lastUpdatedPatch") + " | terrainPatches=" + (array?.Length ?? (-1)) + " | blockHeights=" + (array2?.Length ?? (-1)) + "/" + (array3?.Length ?? (-1));
		}
		catch (Exception ex)
		{
			return "terrain-state-error=" + ex.GetType().Name;
		}
	}

	private static Array ReadTerrainArray(TerrainManager terrain, string fieldName)
	{
		FieldInfo fieldInfo = AccessTools.Field(typeof(TerrainManager), fieldName);
		if ((object)fieldInfo != null)
		{
			return fieldInfo.GetValue(terrain) as Array;
		}
		return null;
	}

	private static int ReadTerrainInt(TerrainManager terrain, string fieldName)
	{
		FieldInfo fieldInfo = AccessTools.Field(typeof(TerrainManager), fieldName);
		if ((object)fieldInfo != null)
		{
			return Convert.ToInt32(fieldInfo.GetValue(terrain));
		}
		return -1;
	}

	private static void RecordQueueState(string eventName)
	{
		DateTime utcNow = DateTime.UtcNow;
		if (!(_lastQueueStateUtc != DateTime.MinValue) || !((utcNow - _lastQueueStateUtc).TotalMilliseconds < 250.0))
		{
			int num = ReadLoadingQueueCount();
			string text = ReadLoadingQueueHeadType();
			bool flag = ReadLoadingHasQueuedActions();
			if (num != _lastQueueCount || !string.Equals(text, _lastQueueHeadType, StringComparison.Ordinal) || flag != _lastQueueHasQueuedActions || !(_lastQueueStateUtc != DateTime.MinValue))
			{
				_lastQueueStateUtc = utcNow;
				_lastQueueCount = num;
				_lastQueueHeadType = text;
				_lastQueueHasQueuedActions = flag;
				WriteLine(eventName + " | count=" + num + " | head=" + (text ?? "<empty>") + " | hasQueuedActions=" + flag);
			}
		}
	}

	private static int ReadLoadingQueueCount()
	{
		try
		{
			LoadingManager instance = Singleton<LoadingManager>.instance;
			if ((Object)(object)instance == (Object)null)
			{
				return -1;
			}
			if ((object)_loadingQueueField == null)
			{
				_loadingQueueField = AccessTools.Field(typeof(LoadingManager), "m_mainThreadQueue");
			}
			object obj = (((object)_loadingQueueField == null) ? null : _loadingQueueField.GetValue(instance));
			PropertyInfo propertyInfo = obj?.GetType().GetProperty("Count");
			return ((object)propertyInfo == null) ? (-1) : ((int)propertyInfo.GetValue(obj, null));
		}
		catch
		{
			return -1;
		}
	}

	private static string ReadLoadingQueueHeadType()
	{
		try
		{
			LoadingManager instance = Singleton<LoadingManager>.instance;
			if ((Object)(object)instance == (Object)null)
			{
				return "<loading-null>";
			}
			if ((object)_loadingQueueField == null)
			{
				_loadingQueueField = AccessTools.Field(typeof(LoadingManager), "m_mainThreadQueue");
			}
			object obj = (((object)_loadingQueueField == null) ? null : _loadingQueueField.GetValue(instance));
			if (obj == null)
			{
				return "<queue-null>";
			}
			if ((object)_loadingQueuePeekMethod == null)
			{
				_loadingQueuePeekMethod = obj.GetType().GetMethod("Peek", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
			}
			if ((object)_loadingQueuePeekMethod == null)
			{
				return "<peek-unavailable>";
			}
			object obj2 = _loadingQueuePeekMethod.Invoke(obj, null);
			return (obj2 == null) ? "<empty>" : obj2.GetType().FullName;
		}
		catch (Exception ex)
		{
			return "<queue-error:" + ex.GetType().Name + ">";
		}
	}

	private static bool TryReadTerrainIteratorIndex(object instance, out int index)
	{
		index = -1;
		if (instance == null)
		{
			return false;
		}
		try
		{
			FieldInfo fieldInfo = _terrainIteratorIndexField;
			if ((object)fieldInfo == null)
			{
				fieldInfo = AccessTools.Field(instance.GetType(), "index");
			}
			if ((object)fieldInfo == null)
			{
				return false;
			}
			index = (int)fieldInfo.GetValue(instance);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool ReadLoadingHasQueuedActions()
	{
		try
		{
			LoadingManager instance = Singleton<LoadingManager>.instance;
			if ((Object)(object)instance == (Object)null)
			{
				return false;
			}
			if ((object)_loadingHasQueuedActionsField == null)
			{
				_loadingHasQueuedActionsField = AccessTools.Field(typeof(LoadingManager), "m_hasQueuedActions");
			}
			return (object)_loadingHasQueuedActionsField != null && (bool)_loadingHasQueuedActionsField.GetValue(instance);
		}
		catch
		{
			return false;
		}
	}

	private static void TerrainIteratorPrefix(object __instance)
	{
		if (!TryReadTerrainIteratorState(__instance, out var index, out var state) || !IsInterestingTerrainPatch(index))
		{
			return;
		}
		bool flag = false;
		lock (Sync)
		{
			if (!TerrainIteratorStates.TryGetValue(index, out var value) || value != state)
			{
				TerrainIteratorStates[index] = state;
				flag = true;
			}
		}
		if (flag)
		{
			WriteLine("TERRAIN_ITER enter | index=" + index + " | state=" + state + " | queue=" + ReadLoadingQueueCount());
		}
	}

	private static void TerrainIteratorPostfix(object __instance, bool __result)
	{
		if (TryReadTerrainIteratorState(__instance, out var index, out var state) && IsInterestingTerrainPatch(index) && !__result)
		{
			WriteLine("TERRAIN_ITER complete | index=" + index + " | state=" + state);
		}
	}

	private static bool TryReadTerrainIteratorState(object instance, out int index, out int state)
	{
		index = -1;
		state = -1;
		if (instance == null || (object)_terrainIteratorIndexField == null || (object)_terrainIteratorStateField == null)
		{
			return false;
		}
		try
		{
			index = (int)_terrainIteratorIndexField.GetValue(instance);
			state = (int)_terrainIteratorStateField.GetValue(instance);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsInterestingTerrainPatch(int index)
	{
		if (index != 54 && index != 55)
		{
			return index == 80;
		}
		return true;
	}

	private static void WaterPatchPrefix(int x, int z)
	{
		int num = x + z * 9;
		if (IsInterestingTerrainPatch(num))
		{
			WriteLine("WATER_PATCH enter | index=" + num + " | x=" + x + " | z=" + z);
		}
	}

	private static void WaterPatchPostfix(int x, int z)
	{
		int num = x + z * 9;
		if (IsInterestingTerrainPatch(num))
		{
			WriteLine("WATER_PATCH exit | index=" + num + " | exception=none");
		}
	}

	private static Exception WaterPatchFinalizer(Exception __exception, int x, int z)
	{
		if (__exception == null)
		{
			return null;
		}
		int num = x + z * 9;
		if (IsInterestingTerrainPatch(num))
		{
			WriteLine("WATER_PATCH exception | index=" + num + " | exception=" + __exception.GetType().FullName + " | message=" + __exception.Message);
		}
		return __exception;
	}

	private static void WriteLoadingState(string eventName)
	{
		DateTime utcNow = DateTime.UtcNow;
		if (eventName.IndexOf("OnLevelLoaded", StringComparison.Ordinal) < 0 && _lastLoadingStateSampleUtc != DateTime.MinValue && (utcNow - _lastLoadingStateSampleUtc).TotalMilliseconds < 1000.0)
		{
			return;
		}
		_lastLoadingStateSampleUtc = utcNow;
		try
		{
			LoadingManager instance = Singleton<LoadingManager>.instance;
			TerrainManager instance2 = Singleton<TerrainManager>.instance;
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append("LOADSTATE ");
			stringBuilder.Append(eventName);
			stringBuilder.Append(" | ");
			AppendLoadingField(stringBuilder, instance, "m_loadingComplete");
			AppendLoadingField(stringBuilder, instance, "m_sceneProgress");
			AppendLoadingField(stringBuilder, instance, "m_simulationProgress");
			AppendLoadingField(stringBuilder, instance, "m_currentCoroutine");
			AppendLoadingField(stringBuilder, instance, "m_loadingProfilerMain");
			AppendLoadingField(stringBuilder, instance, "m_loadingProfilerSimulation");
			AppendLoadingField(stringBuilder, instance2, "m_lastUpdatedPatch");
			WriteLine(stringBuilder.ToString());
		}
		catch (Exception ex)
		{
			WriteLine("LOADSTATE error=" + ex.GetType().Name + ":" + ex.Message);
		}
	}

	private static void AppendLoadingField(StringBuilder builder, object instance, string fieldName)
	{
		builder.Append(fieldName);
		builder.Append("=");
		if (instance == null)
		{
			builder.Append("instance-null");
			return;
		}
		try
		{
			FieldInfo fieldInfo = AccessTools.Field(instance.GetType(), fieldName);
			object obj = fieldInfo?.GetValue(instance);
			if (obj == null)
			{
				builder.Append(((object)fieldInfo == null) ? "unavailable" : "null");
				return;
			}
			LoadingProfiler val = (LoadingProfiler)((obj is LoadingProfiler) ? obj : null);
			if (val != null)
			{
				AppendProfiler(builder, val);
			}
			else
			{
				builder.Append(Convert.ToString(obj, CultureInfo.InvariantCulture));
			}
		}
		catch (Exception ex)
		{
			builder.Append("error:");
			builder.Append(ex.GetType().Name);
		}
	}

	private static void AppendProfiler(StringBuilder builder, LoadingProfiler profiler)
	{
		try
		{
			object obj = AccessTools.Field(typeof(LoadingProfiler), "m_currentLoadingEvent")?.GetValue(profiler);
			builder.Append("active=");
			builder.Append((obj == null) ? "null" : obj);
		}
		catch (Exception ex)
		{
			builder.Append("error:");
			builder.Append(ex.GetType().Name);
		}
	}

	private static void WriteTerrainState()
	{
		TerrainManager instance = Singleton<TerrainManager>.instance;
		if ((Object)(object)instance == (Object)null)
		{
			WriteLine("TERRAIN manager=null");
			return;
		}
		try
		{
			FieldInfo fieldInfo = AccessTools.Field(typeof(TerrainManager), "m_lastUpdatedPatch");
			FieldInfo fieldInfo2 = AccessTools.Field(typeof(TerrainManager), "m_patches");
			FieldInfo fieldInfo3 = AccessTools.Field(typeof(TerrainManager), "m_updateLock");
			Array array = (((object)fieldInfo2 == null) ? null : (fieldInfo2.GetValue(instance) as Array));
			object obj = fieldInfo3?.GetValue(instance);
			WriteLine(string.Concat("TERRAIN lastUpdatedPatch=", ((object)fieldInfo == null) ? "unavailable" : fieldInfo.GetValue(instance), " patches=", (array == null) ? "null" : array.Length.ToString(), " updateLock=", (obj == null) ? "null" : "set"));
		}
		catch (Exception ex)
		{
			WriteLine("TERRAIN stateError=" + ex.GetType().Name + ":" + ex.Message);
		}
	}

	internal static void RecordFrameEnter()
	{
		if (!IsArmed)
		{
			return;
		}
		lock (Sync)
		{
			if (_completedFrames < 3 && _frameActive == 0)
			{
				_frameActive = 1;
				_frameNumber++;
				_frameStartedUtc = DateTime.UtcNow;
				AddHistoryLocked("FRAME " + _frameNumber + " ENTER");
			}
		}
	}

	internal static void RecordFrameExit()
	{
		if (!IsArmed)
		{
			return;
		}
		int num;
		int frameNumber;
		double elapsedSeconds;
		lock (Sync)
		{
			if (_frameActive == 0)
			{
				return;
			}
			num = ++_completedFrames;
			frameNumber = _frameNumber;
			elapsedSeconds = GetElapsedSeconds(_frameStartedUtc);
			_frameActive = 0;
			ActiveDetails.Clear();
			AddHistoryLocked("FRAME " + frameNumber + " EXIT " + FormatSeconds(elapsedSeconds));
		}
		if (num <= 3)
		{
			WriteLine("FRAME " + frameNumber + " COMPLETE | elapsed=" + FormatSeconds(elapsedSeconds) + " | colorUpdates=" + _colorUpdateCount);
		}
		if (num == 3)
		{
			WriteLine("CAPTURE COMPLETE | completedFrames=" + num);
			Interlocked.Exchange(ref _enabled, 0);
			Interlocked.Exchange(ref _stopWatchdog, 1);
		}
	}

	internal static void RecordManagerEnter(MethodBase method, int subStep)
	{
		if (!IsArmed || (object)method == null)
		{
			return;
		}
		string methodLabel = GetMethodLabel(method);
		lock (Sync)
		{
			if (_frameActive != 0)
			{
				ActiveStep activeStep = new ActiveStep();
				activeStep.Name = methodLabel;
				activeStep.StartedUtc = DateTime.UtcNow;
				activeStep.SubStep = subStep;
				ActiveSteps.Add(activeStep);
				AddHistoryLocked("  ENTER " + methodLabel + " subStep=" + subStep);
			}
		}
	}

	internal static void RecordManagerExit(MethodBase method)
	{
		if (!IsArmed || (object)method == null)
		{
			return;
		}
		string methodLabel = GetMethodLabel(method);
		double value = 0.0;
		lock (Sync)
		{
			for (int num = ActiveSteps.Count - 1; num >= 0; num--)
			{
				if (!(ActiveSteps[num].Name != methodLabel))
				{
					value = GetElapsedSeconds(ActiveSteps[num].StartedUtc);
					ActiveSteps.RemoveAt(num);
					break;
				}
			}
			AddHistoryLocked("  EXIT  " + methodLabel + " " + FormatSeconds(value));
		}
	}

	internal static void RecordDetailEnter(MethodBase method, int itemId)
	{
		if (!IsArmed || (object)method == null)
		{
			return;
		}
		string methodLabel = GetMethodLabel(method);
		lock (Sync)
		{
			if (_frameActive != 0)
			{
				ActiveStep activeStep = new ActiveStep();
				activeStep.Name = methodLabel;
				activeStep.StartedUtc = DateTime.UtcNow;
				activeStep.SubStep = itemId;
				ActiveDetails.Add(activeStep);
			}
		}
	}

	internal static void RecordDetailExit(MethodBase method)
	{
		if (!IsArmed || (object)method == null)
		{
			return;
		}
		string methodLabel = GetMethodLabel(method);
		lock (Sync)
		{
			for (int num = ActiveDetails.Count - 1; num >= 0; num--)
			{
				if (!(ActiveDetails[num].Name != methodLabel))
				{
					ActiveDetails.RemoveAt(num);
					break;
				}
			}
		}
	}

	internal static void RecordColorUpdateEnter()
	{
		if (!IsEnabled)
		{
			return;
		}
		lock (Sync)
		{
			if (_completedFrames < 3)
			{
				AddHistoryLocked("  COLOR UpdateColorMap ENTER");
			}
		}
	}

	internal static void RecordColorUpdateExit()
	{
		if (!IsEnabled)
		{
			return;
		}
		lock (Sync)
		{
			if (_completedFrames < 3)
			{
				AddHistoryLocked("  COLOR UpdateColorMap EXIT count=" + ++_colorUpdateCount);
			}
		}
	}

	internal static void RecordManagerRegistered(UnityEngine.Object manager)
	{
		if (IsArmed && !(manager == (UnityEngine.Object)null))
		{
			TryPatchManagerType(((object)manager).GetType());
		}
	}

	internal static void RecordSimulationThreadEnter()
	{
		if (!IsArmed)
		{
			return;
		}
		lock (Sync)
		{
			_simulationThreadStartedUtc = DateTime.UtcNow;
			AddHistoryLocked("SIMULATION THREAD START");
		}
	}

	internal static void RecordFixedUpdate()
	{
		if (!IsArmed)
		{
			return;
		}
		lock (Sync)
		{
			_fixedUpdateCount++;
			_lastFixedUpdateUtc = DateTime.UtcNow;
		}
	}

	internal static void RecordAsyncTaskEnter(AsyncTaskBase task)
	{
		if (!IsArmed || task == null)
		{
			return;
		}
		string asyncTaskName = GetAsyncTaskName(task);
		lock (Sync)
		{
			if (_frameActive != 0)
			{
				ActiveStep activeStep = new ActiveStep();
				activeStep.Name = asyncTaskName;
				activeStep.StartedUtc = DateTime.UtcNow;
				ActiveAsyncTasks.Add(activeStep);
				_asyncTaskCount++;
				AddHistoryLocked("  ASYNC ENTER " + asyncTaskName);
			}
		}
	}

	internal static void RecordAsyncTaskExit(AsyncTaskBase task)
	{
		if (!IsArmed || task == null)
		{
			return;
		}
		string asyncTaskName = GetAsyncTaskName(task);
		double value = 0.0;
		lock (Sync)
		{
			for (int num = ActiveAsyncTasks.Count - 1; num >= 0; num--)
			{
				if (!(ActiveAsyncTasks[num].Name != asyncTaskName))
				{
					value = GetElapsedSeconds(ActiveAsyncTasks[num].StartedUtc);
					ActiveAsyncTasks.RemoveAt(num);
					break;
				}
			}
			AddHistoryLocked("  ASYNC EXIT  " + asyncTaskName + " " + FormatSeconds(value));
		}
	}

	private static void WatchdogLoop()
	{
		while (Interlocked.CompareExchange(ref _stopWatchdog, 0, 0) == 0)
		{
			Thread.Sleep(1000);
			if (!IsEnabled)
			{
				break;
			}
			RecordQueueState("QUEUE watchdog");
			string message = null;
			lock (Sync)
			{
				DateTime utcNow = DateTime.UtcNow;
				bool flag = _frameActive != 0;
				double num = (flag ? GetElapsedSeconds(_frameStartedUtc) : GetElapsedSeconds(_monitorStartedUtc));
				int num2 = (flag ? 20 : 30);
				if (num < (double)num2 || _reportCount >= 8 || (_lastReportUtc != DateTime.MinValue && GetElapsedSeconds(_lastReportUtc) < 10.0))
				{
					continue;
				}
				message = BuildWatchdogReportLocked(utcNow, flag, num);
				_lastReportUtc = utcNow;
				_reportCount++;
				goto IL_00c2;
			}
			IL_00c2:
			WriteLine(message);
		}
	}

	private static string BuildWatchdogReportLocked(DateTime now, bool frameActive, double elapsedSeconds)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("WATCHDOG | ");
		if (frameActive)
		{
			stringBuilder.Append("simulation frame ");
			stringBuilder.Append(_frameNumber);
			stringBuilder.Append(" has not completed for ");
		}
		else
		{
			stringBuilder.Append("SimulationManager.SimulationStep has not entered for ");
		}
		stringBuilder.Append(FormatSeconds(elapsedSeconds));
		stringBuilder.Append(" | completedFrames=");
		stringBuilder.Append(_completedFrames);
		stringBuilder.Append(" | colorUpdates=");
		stringBuilder.Append(_colorUpdateCount);
		stringBuilder.Append(" | fixedUpdates=");
		stringBuilder.Append(_fixedUpdateCount);
		stringBuilder.Append(" | asyncTasks=");
		stringBuilder.Append(_asyncTaskCount);
		if (ActiveSteps.Count == 0)
		{
			if (frameActive)
			{
				stringBuilder.Append(" | NO ACTIVE ISimulationManager (the main thread is between manager calls, before the manager loop, or inside native code)");
			}
		}
		else
		{
			ActiveStep activeStep = ActiveSteps[ActiveSteps.Count - 1];
			stringBuilder.Append(" | CURRENT ");
			stringBuilder.Append(activeStep.Name);
			stringBuilder.Append(" subStep=");
			stringBuilder.Append(activeStep.SubStep);
			stringBuilder.Append(" entered=");
			stringBuilder.Append(FormatSeconds(GetElapsedSeconds(activeStep.StartedUtc)));
			if (ActiveSteps.Count > 1)
			{
				stringBuilder.Append(" | activeDepth=");
				stringBuilder.Append(ActiveSteps.Count);
			}
		}
		if (ActiveAsyncTasks.Count > 0)
		{
			ActiveStep activeStep2 = ActiveAsyncTasks[ActiveAsyncTasks.Count - 1];
			stringBuilder.Append(" | CURRENT ASYNC ");
			stringBuilder.Append(activeStep2.Name);
			stringBuilder.Append(" entered=");
			stringBuilder.Append(FormatSeconds(GetElapsedSeconds(activeStep2.StartedUtc)));
			if (ActiveAsyncTasks.Count > 1)
			{
				stringBuilder.Append(" | asyncDepth=");
				stringBuilder.Append(ActiveAsyncTasks.Count);
			}
		}
		if (ActiveDetails.Count > 0)
		{
			ActiveStep activeStep3 = ActiveDetails[ActiveDetails.Count - 1];
			stringBuilder.Append(" | DETAIL ");
			stringBuilder.Append(activeStep3.Name);
			if (activeStep3.SubStep >= 0)
			{
				stringBuilder.Append(" id=");
				stringBuilder.Append(activeStep3.SubStep);
			}
			stringBuilder.Append(" entered=");
			stringBuilder.Append(FormatSeconds(GetElapsedSeconds(activeStep3.StartedUtc)));
			if (ActiveDetails.Count > 1)
			{
				stringBuilder.Append(" | detailDepth=");
				stringBuilder.Append(ActiveDetails.Count);
			}
		}
		AppendRuntimeStateLocked(stringBuilder);
		AppendNetworkStateLocked(stringBuilder);
		AppendSimulationStackLocked(stringBuilder);
		stringBuilder.Append(Environment.NewLine);
		stringBuilder.Append("REGISTERED ");
		stringBuilder.Append(_managerSnapshot);
		stringBuilder.Append(Environment.NewLine);
		stringBuilder.Append("RECENT TRACE:");
		string[] array = History.ToArray();
		int num = Math.Max(0, array.Length - 48);
		for (int i = num; i < array.Length; i++)
		{
			stringBuilder.Append(Environment.NewLine);
			stringBuilder.Append("  ");
			stringBuilder.Append(array[i]);
		}
		return stringBuilder.ToString();
	}

	private static void AppendRuntimeStateLocked(StringBuilder builder)
	{
		builder.Append(Environment.NewLine);
		builder.Append("RUNTIME");
		if ((Object)(object)_simulationManager == (Object)null)
		{
			builder.Append(" simulationManager=null");
		}
		else
		{
			AppendField(builder, _simulationManager, "m_updateCounter");
			AppendField(builder, _simulationManager, "m_maxFramesBehind");
			AppendField(builder, _simulationManager, "m_inSimulationStep");
			AppendField(builder, _simulationManager, "m_hasActions");
			AppendField(builder, _simulationManager, "m_simulationSpeed");
			AppendField(builder, _simulationManager, "m_simulationPaused");
			AppendField(builder, _simulationManager, "m_forcedSimulationPause");
			AppendField(builder, _simulationManager, "m_currentFrameIndex");
			AppendField(builder, _simulationManager, "m_referenceFrameIndex");
			AppendField(builder, _simulationManager, "m_simulationThread");
			AppendField(builder, _simulationManager, "m_simulationActions");
		}
		if ((Object)(object)_loadingManager == (Object)null)
		{
			builder.Append(" loadingManager=null");
			return;
		}
		AppendField(builder, _loadingManager, "m_currentlyLoading");
		AppendField(builder, _loadingManager, "m_renderDataReady");
		AppendField(builder, _loadingManager, "m_metaDataLoaded");
		AppendField(builder, _loadingManager, "m_simulationDataLoaded");
		AppendField(builder, _loadingManager, "m_loadingComplete");
		AppendField(builder, _loadingManager, "m_sceneProgress");
		AppendField(builder, _loadingManager, "m_simulationProgress");
	}

	private static void AppendNetworkStateLocked(StringBuilder builder)
	{
		builder.Append(Environment.NewLine);
		builder.Append("NETSTATE");
		NetManager instance = Singleton<NetManager>.instance;
		if ((Object)(object)instance == (Object)null)
		{
			builder.Append(" manager=null");
			return;
		}
		AppendArrayFieldState(builder, instance, "m_updatedNodes", "updatedNodes");
		AppendArrayFieldState(builder, instance, "m_updatedSegments", "updatedSegments");
		AppendArrayFieldState(builder, instance, "m_adjustedSegments", "adjustedSegments");
		AppendField(builder, instance, "m_nodesUpdated");
		AppendField(builder, instance, "m_segmentsUpdated");
		AppendField(builder, instance, "m_wetnessChanged");
		AppendField(builder, instance, "m_updateNameVisibility");
		try
		{
			builder.Append(" pendingNodeBits=");
			builder.Append(NetManagerCustomizer.CountPendingNodeUpdates());
			builder.Append(" pendingSegmentBits=");
			builder.Append(NetManagerCustomizer.CountPendingSegmentUpdates());
			builder.Append(" highCreatedNodes=");
			builder.Append(NetManagerCustomizer.CountHighNodeIds(instance));
			builder.Append(" highCreatedSegments=");
			builder.Append(NetManagerCustomizer.CountHighSegmentIds(instance));
		}
		catch (Exception ex)
		{
			builder.Append(" networkCounts=error:");
			builder.Append(ex.Message);
		}
	}

	private static void AppendArrayFieldState(StringBuilder builder, object instance, string fieldName, string label)
	{
		try
		{
			FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			Array array = (((object)field == null) ? null : (field.GetValue(instance) as Array));
			builder.Append(" ");
			builder.Append(label);
			builder.Append("Length=");
			builder.Append(array?.Length ?? (-1));
			ulong[] bits = array as ulong[];
			builder.Append(" ");
			builder.Append(label);
			builder.Append("Bits=");
			builder.Append(CountSetBits(bits));
		}
		catch (Exception ex)
		{
			builder.Append(" ");
			builder.Append(label);
			builder.Append("=error:");
			builder.Append(ex.Message);
		}
	}

	private static void AppendSimulationStackLocked(StringBuilder builder)
	{
		builder.Append(Environment.NewLine);
		builder.Append("SIMSTACK");
		if ((Object)(object)_simulationManager == (Object)null)
		{
			builder.Append(" simulationManager=null");
			return;
		}
		try
		{
			FieldInfo field = typeof(SimulationManager).GetField("m_simulationThread", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			Thread thread = (((object)field == null) ? null : (field.GetValue(_simulationManager) as Thread));
			if (thread == null)
			{
				builder.Append(" thread=null");
				return;
			}
			builder.Append(" thread=");
			builder.Append(thread.Name);
			builder.Append(" alive=");
			builder.Append(thread.IsAlive);
			builder.Append(" state=");
			builder.Append(thread.ThreadState);
			if (!thread.IsAlive)
			{
				return;
			}
			builder.Append(" managedStack=unsupported");
			for (int num = ActiveDetails.Count - 1; num >= 0; num--)
			{
				ActiveStep activeStep = ActiveDetails[num];
				builder.Append(Environment.NewLine);
				builder.Append("  #");
				builder.Append(ActiveDetails.Count - 1 - num);
				builder.Append(" ");
				builder.Append(activeStep.Name);
				if (activeStep.SubStep >= 0)
				{
					builder.Append(" id=");
					builder.Append(activeStep.SubStep);
				}
				builder.Append(" entered=");
				builder.Append(FormatSeconds(GetElapsedSeconds(activeStep.StartedUtc)));
			}
		}
		catch (Exception ex)
		{
			builder.Append(" stack=error:");
			builder.Append(ex.Message);
		}
	}

	private static int CountSetBits(ulong[] bits)
	{
		if (bits == null)
		{
			return 0;
		}
		int num = 0;
		for (int i = 0; i < bits.Length; i++)
		{
			ulong num2 = bits[i];
			while (num2 != 0)
			{
				num2 &= num2 - 1;
				num++;
			}
		}
		return num;
	}

	private static void AppendField(StringBuilder builder, object instance, string fieldName)
	{
		try
		{
			FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if ((object)field == null)
			{
				builder.Append(" ");
				builder.Append(fieldName);
				builder.Append("=missing");
				return;
			}
			object value = field.GetValue(instance);
			builder.Append(" ");
			builder.Append(fieldName);
			builder.Append("=");
			if (value == null)
			{
				builder.Append("null");
			}
			else if (value is Thread thread)
			{
				builder.Append("alive:");
				builder.Append(thread.IsAlive);
				builder.Append(",state:");
				builder.Append(thread.ThreadState);
			}
			else if (value is ICollection collection)
			{
				builder.Append("count:");
				builder.Append(collection.Count);
			}
			else
			{
				builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
			}
		}
		catch (Exception ex)
		{
			builder.Append(" ");
			builder.Append(fieldName);
			builder.Append("=error:");
			builder.Append(ex.Message);
		}
	}

	private static void InitializeLog()
	{
		try
		{
			string text = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			if (string.IsNullOrEmpty(text))
			{
				text = Path.GetTempPath();
			}
			string text2 = Path.Combine(Path.Combine(Path.Combine(text, "Colossal Order"), "Cities_Skylines"), "MoreNetworkCapacity");
			Directory.CreateDirectory(text2);
			_logPath = Path.Combine(text2, "simulation-step-diagnostics.log");
			File.WriteAllText(_logPath, "MoreNetworkCapacity simulation-step diagnostics" + Environment.NewLine);
		}
		catch (Exception ex)
		{
			_logFailed = true;
			_logPath = null;
			Logging.Warning("Could not initialize the simulation diagnostics log: " + ex.Message);
		}
	}

	private static void WriteLine(string message)
	{
		if (string.IsNullOrEmpty(_logPath) || _logFailed)
		{
			return;
		}
		string contents = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " | " + message + Environment.NewLine;
		lock (FileSync)
		{
			try
			{
				File.AppendAllText(_logPath, contents);
			}
			catch
			{
				_logFailed = true;
			}
		}
	}

	private static int PatchNetworkDetailMethods()
	{
		if (Interlocked.CompareExchange(ref _detailPatched, 1, 0) != 0)
		{
			return 0;
		}
		int num = 0;
		num += PatchDetailMethod(typeof(NetManager), "SimulationStepImpl", new Type[1] { typeof(int) }, 2);
		num += PatchDetailMethod(typeof(NetNode), "CalculateNode", new Type[1] { typeof(ushort) }, 1);
		num += PatchDetailMethod(typeof(NetNode), "UpdateNode", new Type[1] { typeof(ushort) }, 1);
		num += PatchDetailMethod(typeof(NetNode), "UpdateBounds", new Type[1] { typeof(ushort) }, 1);
		num += PatchDetailMethod(typeof(NetNode), "UpdateLaneConnection", new Type[1] { typeof(ushort) }, 1);
		num += PatchDetailMethod(typeof(NetManager), "UpdateNodeFlags", new Type[1] { typeof(ushort) }, 1);
		num += PatchDetailMethod(typeof(NetManager), "UpdateNodeRenderer", new Type[2]
		{
			typeof(ushort),
			typeof(bool)
		}, 1);
		num += PatchDetailMethod(typeof(NetSegment), "CalculateSegment", new Type[1] { typeof(ushort) }, 1);
		num += PatchDetailMethod(typeof(NetSegment), "UpdateSegment", new Type[1] { typeof(ushort) }, 1);
		num += PatchDetailMethod(typeof(NetSegment), "UpdateBounds", new Type[1] { typeof(ushort) }, 1);
		num += PatchDetailMethod(typeof(NetSegment), "UpdateLanes", new Type[2]
		{
			typeof(ushort),
			typeof(bool)
		}, 1);
		num += PatchDetailMethod(typeof(NetManager), "UpdateSegmentFlags", new Type[1] { typeof(ushort) }, 1);
		num += PatchDetailMethod(typeof(NetManager), "UpdateSegmentRenderer", new Type[2]
		{
			typeof(ushort),
			typeof(bool)
		}, 1);
		num += PatchDetailMethod(typeof(PathVisualizer), "SimulationStep", new Type[1] { typeof(int) }, 2);
		num += PatchDetailMethod(typeof(NetAdjust), "SimulationStep", new Type[1] { typeof(int) }, 2);
		Logging.Info("Simulation network detail diagnostics patched " + num + " methods.");
		return num;
	}

	private static int PatchDetailMethod(Type type, string name, Type[] parameters, int argumentMode)
	{
		MethodBase methodBase = AccessTools.Method(type, name, parameters, (Type[])null);
		if ((object)methodBase == null)
		{
			Logging.Warning("Could not find network detail diagnostic target " + GetTypeName(type) + "." + name + ".");
			return 0;
		}
		return TryPatchDetailMethod(methodBase, argumentMode);
	}

	private static int TryPatchDetailMethod(MethodBase method, int argumentMode)
	{
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Expected O, but got Unknown
		//IL_0099: Expected O, but got Unknown
		if ((object)method == null || _harmony == null)
		{
			return 0;
		}
		lock (PatchSync)
		{
			if (PatchedMethods.Contains(method))
			{
				return 0;
			}
			PatchedMethods.Add(method);
			try
			{
				MethodInfo method2 = typeof(SimulationStepDiagnostics).GetMethod(argumentMode switch
				{
					2 => "DetailIntPrefix", 
					1 => "DetailIdPrefix", 
					_ => "DetailPrefix", 
				}, BindingFlags.Static | BindingFlags.NonPublic);
				MethodInfo method3 = typeof(SimulationStepDiagnostics).GetMethod("DetailPostfix", BindingFlags.Static | BindingFlags.NonPublic);
				_harmony.Patch(method, new HarmonyMethod(method2), new HarmonyMethod(method3), (HarmonyMethod)null, (HarmonyMethod)null);
				return 1;
			}
			catch (Exception ex)
			{
				PatchedMethods.Remove(method);
				Logging.Warning("Could not attach network detail diagnostics to " + GetMethodLabel(method) + ": " + ex.Message);
				return 0;
			}
		}
	}

	private static int PatchAllManagerMethods()
	{
		int num = 0;
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		for (int i = 0; i < assemblies.Length; i++)
		{
			Type[] loadableTypes = GetLoadableTypes(assemblies[i]);
			foreach (Type type in loadableTypes)
			{
				if ((object)type != null && !type.IsInterface)
				{
					num += TryPatchManagerType(type);
				}
			}
		}
		return num;
	}

	private static int TryPatchManagerType(Type type)
	{
		if ((object)type == null || type.IsInterface)
		{
			return 0;
		}
		Type gameSimulationManagerInterface;
		try
		{
			gameSimulationManagerInterface = GetGameSimulationManagerInterface(type);
		}
		catch
		{
			return 0;
		}
		if ((object)gameSimulationManagerInterface == null)
		{
			return 0;
		}
		int num = 0;
		try
		{
			InterfaceMapping interfaceMap = type.GetInterfaceMap(gameSimulationManagerInterface);
			for (int i = 0; i < interfaceMap.InterfaceMethods.Length; i++)
			{
				if (!(interfaceMap.InterfaceMethods[i].Name != "SimulationStep") && interfaceMap.InterfaceMethods[i].GetParameters().Length == 1 && TryPatchManagerMethod(interfaceMap.TargetMethods[i]))
				{
					num++;
				}
			}
		}
		catch (Exception ex)
		{
			Logging.Warning("Could not inspect simulation manager " + GetTypeName(type) + ": " + ex.Message);
		}
		return num;
	}

	private static bool TryPatchManagerMethod(MethodBase method)
	{
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Expected O, but got Unknown
		//IL_0080: Expected O, but got Unknown
		if ((object)method == null || _harmony == null)
		{
			return false;
		}
		lock (PatchSync)
		{
			if (PatchedMethods.Contains(method))
			{
				return false;
			}
			PatchedMethods.Add(method);
			try
			{
				MethodInfo method2 = typeof(SimulationStepDiagnostics).GetMethod("ManagerPrefix", BindingFlags.Static | BindingFlags.NonPublic);
				MethodInfo method3 = typeof(SimulationStepDiagnostics).GetMethod("ManagerPostfix", BindingFlags.Static | BindingFlags.NonPublic);
				_harmony.Patch(method, new HarmonyMethod(method2), new HarmonyMethod(method3), (HarmonyMethod)null, (HarmonyMethod)null);
				PatchedTypeNames.Add(GetTypeName(method.DeclaringType));
				return true;
			}
			catch (Exception ex)
			{
				PatchedMethods.Remove(method);
				Logging.Warning("Could not attach simulation diagnostics to " + GetMethodLabel(method) + ": " + ex.Message);
				return false;
			}
		}
	}

	private static void ManagerPrefix(MethodBase __originalMethod, int subStep)
	{
		RecordManagerEnter(__originalMethod, subStep);
	}

	private static void ManagerPostfix(MethodBase __originalMethod)
	{
		RecordManagerExit(__originalMethod);
	}

	private static void DetailPrefix(MethodBase __originalMethod)
	{
		RecordDetailEnter(__originalMethod, -1);
	}

	private static void DetailIdPrefix(MethodBase __originalMethod, ushort __0)
	{
		RecordDetailEnter(__originalMethod, __0);
	}

	private static void DetailIntPrefix(MethodBase __originalMethod, int __0)
	{
		RecordDetailEnter(__originalMethod, __0);
	}

	private static void DetailPostfix(MethodBase __originalMethod)
	{
		RecordDetailExit(__originalMethod);
	}

	private static Type GetGameSimulationManagerInterface(Type type)
	{
		if ((object)type == null)
		{
			return null;
		}
		Type[] interfaces = type.GetInterfaces();
		foreach (Type type2 in interfaces)
		{
			if (type2.FullName == "ISimulationManager" && (object)type2.Assembly == typeof(SimulationManager).Assembly)
			{
				return type2;
			}
		}
		return null;
	}

	private static Type[] GetLoadableTypes(Assembly assembly)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			List<Type> list = new List<Type>();
			Type[] types = ex.Types;
			for (int i = 0; i < types.Length; i++)
			{
				if ((object)types[i] != null)
				{
					list.Add(types[i]);
				}
			}
			return list.ToArray();
		}
		catch
		{
			return new Type[0];
		}
	}

	private static string BuildRegisteredManagerSnapshot()
	{
		try
		{
			FieldInfo field = typeof(SimulationManager).GetField("m_managers", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if ((object)field == null)
			{
				return "m_managers field unavailable";
			}
			object value = field.GetValue(null);
			if (value == null)
			{
				return "m_managers is null";
			}
			Type type = value.GetType();
			FieldInfo field2 = type.GetField("m_buffer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			FieldInfo field3 = type.GetField("m_size", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if ((object)field2 == null || (object)field3 == null)
			{
				return "m_managers layout unavailable";
			}
			Array array = field2.GetValue(value) as Array;
			int num = (int)field3.GetValue(value);
			if (array == null)
			{
				return "m_managers buffer is null";
			}
			List<string> list = new List<string>();
			for (int i = 0; i < num && i < array.Length; i++)
			{
				object value2 = array.GetValue(i);
				if (value2 != null)
				{
					string typeName = GetTypeName(value2.GetType());
					typeName = ((!PatchedTypeNames.Contains(typeName)) ? (typeName + "[not-patched]") : (typeName + "[patched]"));
					list.Add(typeName);
				}
			}
			return string.Join(", ", list.ToArray());
		}
		catch (Exception ex)
		{
			return "m_managers snapshot failed: " + ex.Message;
		}
	}

	private static void AddHistoryLocked(string entry)
	{
		History.Enqueue(entry);
		while (History.Count > 512)
		{
			History.Dequeue();
		}
	}

	private static string GetMethodLabel(MethodBase method)
	{
		if ((object)method == null)
		{
			return "<null>";
		}
		return GetTypeName(method.DeclaringType) + "." + method.Name;
	}

	private static string GetAsyncTaskName(AsyncTaskBase task)
	{
		if (task == null)
		{
			return "<null>";
		}
		try
		{
			if ((object)AsyncTaskNameField != null)
			{
				object value = AsyncTaskNameField.GetValue(task);
				string text = value as string;
				if (!string.IsNullOrEmpty(text))
				{
					return text;
				}
			}
		}
		catch
		{
		}
		return GetTypeName(((object)task).GetType());
	}

	private static string GetTypeName(Type type)
	{
		if ((object)type != null)
		{
			return type.FullName;
		}
		return "<null>";
	}

	private static double GetElapsedSeconds(DateTime startedUtc)
	{
		return Math.Max(0.0, (DateTime.UtcNow - startedUtc).TotalSeconds);
	}

	private static string FormatSeconds(double value)
	{
		return value.ToString("0.000", CultureInfo.InvariantCulture) + "s";
	}
}

}
