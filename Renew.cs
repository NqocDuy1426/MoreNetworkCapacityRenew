using ICities;
using ColossalFramework;
using System.Reflection;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using Harmony;
using System.Linq;
using System.Collections.Generic;
using System.Reflection.Emit;
using ColossalFramework.Plugins;

namespace MoreNetworkCapacityRenew
{
    // ============================================================================
    // DIAGNOSTIC LOGGER
    // ============================================================================
    public static class DiagnosticLogger
    {
        private static DateTime lastCheckpoint = DateTime.Now;
        
        public static void Checkpoint(string phase)
        {
            DateTime now = DateTime.Now;
            double elapsedMs = (now - lastCheckpoint).TotalMilliseconds;
            UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] CHECKPOINT [{0}] +{1:F0}ms", phase, elapsedMs));
            lastCheckpoint = now;
        }
    }

    // ============================================================================
    // MEMORY OPTIMIZER & GC SECTION GUARDIAN
    // ============================================================================
    public static class MemoryOptimizer
    {
        public static bool EnableStrategicGC = true;
        public static bool EnableEarlyPreallocation = true;

        public static void StrategicGarbageCollect(string phase)
        {
            if (!EnableStrategicGC) return;
            try
            {
                long beforeBytes = GC.GetTotalMemory(false);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long afterBytes = GC.GetTotalMemory(false);
                double freedMB = (beforeBytes - afterBytes) / (1024.0 * 1024.0);
                double totalMB = afterBytes / (1024.0 * 1024.0);
                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] [MemoryGuard] Strategic GC at [{0}]: Freed {1:F2} MB. Managed Heap: {2:F2} MB", phase, freedMB, totalMB));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] StrategicGarbageCollect warning: " + ex.Message);
            }
        }

        public static void LogMemoryStatus(string phase)
        {
            try
            {
                long totalBytes = GC.GetTotalMemory(false);
                double totalMB = totalBytes / (1024.0 * 1024.0);
                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] [MemoryGuard] Heap Status [{0}]: {1:F2} MB managed memory allocated.", phase, totalMB));
            }
            catch {}
        }

        public static void CheckProceduralObjectsPresence()
        {
            try
            {
                bool poFound = false;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name == "ProceduralObjects")
                    {
                        poFound = true;
                        break;
                    }
                }
                if (poFound)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] [MemoryGuard] Procedural Objects (PO) detected! Aggressive memory prewarming and GC section protection active.");
                }
            }
            catch {}
        }

        public static void PrewarmHeap()
        {
            if (!EnableEarlyPreallocation) return;
            try
            {
                DiagnosticLogger.Checkpoint("MemoryOptimizer.PrewarmHeap.Start");
                
                // Flush temporary objects before preallocating large contiguous buffers
                StrategicGarbageCollect("PrewarmHeap.Start");

                // 1. Pre-allocate RenderManager indices early while virtual memory is clean
                RenewMod.EnsureRenderManagerCapacity();

                // 2. Pre-allocate InstanceManager buffer
                InstanceManagerCompatibility.EnsureCompatibility();

                // 3. Prewarm TM:PE buffers if loaded
                try
                {
                    TMPECompatibility.SynchronizeTMPEBuffersAndArrays("PrewarmHeap");
                }
                catch {}

                StrategicGarbageCollect("PrewarmHeap.Complete");
                DiagnosticLogger.Checkpoint("MemoryOptimizer.PrewarmHeap.Complete");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] PrewarmHeap error: " + ex);
            }
        }
    }

    // ============================================================================
    // MOD INFO (IUserMod)
    // ============================================================================
    public class RenewMod : IUserMod
    {
        public string Name => "More Network Capacity Renew by NgocDuy";
        public string Description => "Expands network node and segment capacity to 64K with Mac stability, TM:PE compatibility, traffic lights & accurate traffic flow simulation. Renewed by NgocDuy.";

        static RenewMod()
        {
            try
            {
                AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
                {
                    try
                    {
                        if (args.Name.StartsWith("MoreNetworkCapacity,") || args.Name == "MoreNetworkCapacity")
                        {
                            return EnsureCoreLoaded();
                        }
                    }
                    catch {}
                    return null;
                };
            }
            catch {}
        }

        public void OnEnabled()
        {
            EnsureCoreActive();
            try
            {
                AppDomain.CurrentDomain.AssemblyLoad += (sender, args) =>
                {
                    try
                    {
                        if (args.LoadedAssembly != null && args.LoadedAssembly.GetName().Name == "MoreNetworkCapacity")
                        {
                            EnsureCoreActive();
                        }
                    }
                    catch {}
                };
            }
            catch {}

            try
            {
                MemoryOptimizer.CheckProceduralObjectsPresence();
                MemoryOptimizer.PrewarmHeap();
            }
            catch {}
        }

        public void OnDisabled()
        {
            DisableCore();
        }

        public void OnSettingsUI(UIHelperBase helper)
        {
            try
            {
                UIHelperBase group = helper.AddGroup("Memory & GC Section Optimization (NgocDuy)");
                group.AddCheckbox("Strategic Garbage Collection (Reduces 'Too many heap sections')", MemoryOptimizer.EnableStrategicGC, (b) => {
                    MemoryOptimizer.EnableStrategicGC = b;
                });
                group.AddCheckbox("Pre-allocate Large Buffers Early (Prevents heap fragmentation)", MemoryOptimizer.EnableEarlyPreallocation, (b) => {
                    MemoryOptimizer.EnableEarlyPreallocation = b;
                });
                group.AddButton("Run Garbage Collection Now", () => {
                    MemoryOptimizer.StrategicGarbageCollect("ManualTrigger");
                });
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] OnSettingsUI error: " + ex);
            }
        }

        public static Assembly EnsureCoreLoaded()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name == "MoreNetworkCapacity")
                {
                    return a;
                }
            }

            // 1. Try loading from the mod folder
            try
            {
                string thisLocation = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(thisLocation))
                {
                    string dir = Path.GetDirectoryName(thisLocation);
                    string coreFile = Path.Combine(dir, "MoreNetworkCapacity.Core.dll");
                    if (File.Exists(coreFile))
                    {
                        return Assembly.LoadFrom(coreFile);
                    }
                }
            }
            catch {}

            // 2. Try loading from embedded resource
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MoreNetworkCapacity.Core.dll"))
                {
                    if (stream != null)
                    {
                        byte[] data = new byte[stream.Length];
                        stream.Read(data, 0, data.Length);

                        // If possible, also extract to disk for native reference
                        try
                        {
                            string thisLocation = Assembly.GetExecutingAssembly().Location;
                            if (!string.IsNullOrEmpty(thisLocation))
                            {
                                string dir = Path.GetDirectoryName(thisLocation);
                                string coreFile = Path.Combine(dir, "MoreNetworkCapacity.Core.dll");
                                if (!File.Exists(coreFile))
                                {
                                    File.WriteAllBytes(coreFile, data);
                                    return Assembly.LoadFrom(coreFile);
                                }
                            }
                        }
                        catch {}

                        return Assembly.Load(data);
                    }
                }
            }
            catch {}

            return null;
        }

        public static void EnsureCoreActive()
        {
            try
            {
                Assembly mnc = EnsureCoreLoaded();
                if (mnc == null)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Warning: MoreNetworkCapacity core assembly not found yet.");
                    return;
                }

                Type gvType = mnc.GetType("MoreNetworkCapacity.GameVersion");
                if (gvType != null)
                {
                    var fChecked = gvType.GetField("_checked", BindingFlags.NonPublic | BindingFlags.Static);
                    var fSupp = gvType.GetField("_supported", BindingFlags.NonPublic | BindingFlags.Static);
                    if (fChecked != null) fChecked.SetValue(null, true);
                    if (fSupp != null) fSupp.SetValue(null, true);
                }

                Type hbType = mnc.GetType("MoreNetworkCapacity.HarmonyBootstrap");
                if (hbType != null)
                {
                    var fHarmony = hbType.GetField("_harmony", BindingFlags.NonPublic | BindingFlags.Static);
                    if (fHarmony != null && fHarmony.GetValue(null) == null)
                    {
                        var mEnable = hbType.GetMethod("Enable", BindingFlags.NonPublic | BindingFlags.Static);
                        if (mEnable != null)
                        {
                            mEnable.Invoke(null, null);
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] MoreNetworkCapacity HarmonyBootstrap.Enable() triggered successfully!");
                        }
                    }
                }
                EnsureRenderManagerCapacity();
                InstanceManagerCompatibility.EnsureCompatibility();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureCoreActive error: " + ex);
            }
        }

        public static void EnsureRenderManagerCapacity()
        {
            try
            {
                Assembly mnc = EnsureCoreLoaded();
                if (mnc != null)
                {
                    Type rmap = mnc.GetType("MoreNetworkCapacity.Patches.RenderManagerAwakePatch");
                    if (rmap != null)
                    {
                        MethodInfo mEnsure = rmap.GetMethod("EnsureCapacity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (mEnsure != null)
                        {
                            mEnsure.Invoke(null, null);
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] RenderManagerAwakePatch.EnsureCapacity invoked.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureCapacity invoke warning: " + ex.Message);
            }

            try
            {
                var rm = Singleton<RenderManager>.instance;
                if (rm != null)
                {
                    const int targetSize = 262144;
                    if (rm.m_indices == null || rm.m_indices.Length < targetSize)
                    {
                        ushort[] oldArr = rm.m_indices;
                        ushort[] newArr = new ushort[targetSize];
                        for (int i = 0; i < newArr.Length; i++) newArr[i] = ushort.MaxValue;
                        if (oldArr != null)
                        {
                            Array.Copy(oldArr, newArr, Math.Min(oldArr.Length, newArr.Length));
                        }
                        rm.m_indices = newArr;
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Guaranteed RenderManager.m_indices capacity: {0} (was {1}).", targetSize, oldArr != null ? oldArr.Length : 0));
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Guaranteed RenderManager capacity error: " + ex);
            }
        }

        public static void DisableCore()
        {
            try
            {
                Assembly mnc = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name == "MoreNetworkCapacity")
                    {
                        mnc = a;
                        break;
                    }
                }
                if (mnc == null) return;

                Type hbType = mnc.GetType("MoreNetworkCapacity.HarmonyBootstrap");
                if (hbType != null)
                {
                    var mDisable = hbType.GetMethod("Disable", BindingFlags.NonPublic | BindingFlags.Static);
                    if (mDisable != null)
                    {
                        mDisable.Invoke(null, null);
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] MoreNetworkCapacity HarmonyBootstrap.Disable() triggered.");
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] DisableCore error: " + ex);
            }
        }
    }

    // ============================================================================
    // INSTANCE MANAGER COMPATIBILITY
    // ============================================================================
    public static class InstanceManagerCompatibility
    {
        private const int EXPANDED_SIZE = 65024;
        
        public static void EnsureCompatibility()
        {
            DiagnosticLogger.Checkpoint("InstanceManager.Start");
            
            try
            {
                Type instanceManagerType = typeof(InstanceManager);
                InstanceManager manager = Singleton<InstanceManager>.instance;
                
                if (manager == null)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] InstanceManager not yet initialized");
                    return;
                }

                // Expand m_instances array
                FieldInfo instancesField = instanceManagerType.GetField("m_instances", BindingFlags.NonPublic | BindingFlags.Instance);
                if (instancesField != null)
                {
                    Array currentArray = (Array)instancesField.GetValue(manager);
                    if (currentArray != null)
                    {
                        int currentLength = currentArray.Length;
                        if (currentLength < EXPANDED_SIZE)
                        {
                            Type elementType = currentArray.GetType().GetElementType();
                            Array expandedArray = Array.CreateInstance(elementType, EXPANDED_SIZE);
                            Array.Copy(currentArray, expandedArray, currentLength);
                            instancesField.SetValue(manager, expandedArray);
                            UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Expanded InstanceManager.m_instances: {0} -> {1}", currentLength, EXPANDED_SIZE));
                        }
                    }
                }

                // Expand m_free array if it exists
                FieldInfo freeField = instanceManagerType.GetField("m_free", BindingFlags.NonPublic | BindingFlags.Instance);
                if (freeField != null)
                {
                    Array currentFree = (Array)freeField.GetValue(manager);
                    if (currentFree != null && currentFree.Length < EXPANDED_SIZE)
                    {
                        Type elementType = currentFree.GetType().GetElementType();
                        Array expandedFree = Array.CreateInstance(elementType, EXPANDED_SIZE);
                        Array.Copy(currentFree, expandedFree, currentFree.Length);
                        freeField.SetValue(manager, expandedFree);
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Expanded InstanceManager.m_free to {0}", EXPANDED_SIZE));
                    }
                }

                DiagnosticLogger.Checkpoint("InstanceManager.Complete");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] InstanceManager compatibility error: " + ex);
                DiagnosticLogger.Checkpoint("InstanceManager.Error");
            }
        }
    }

    // ============================================================================
    // MISSING ASSET PROTECTION
    // ============================================================================
    public static class MissingAssetProtection
    {
        private static int nullPrefabCount = 0;
        private static HashSet<uint> reportedIndices = new HashSet<uint>();

        public static void Initialize(HarmonyInstance harmony)
        {
            DiagnosticLogger.Checkpoint("AssetProtection.Start");
            
            try
            {
                Type prefabCollectionBuilding = typeof(PrefabCollection<BuildingInfo>);
                MethodInfo getPrefabBuilding = prefabCollectionBuilding.GetMethod("GetPrefab", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(uint) }, null);
                
                if (getPrefabBuilding != null)
                {
                    MethodInfo postfix = typeof(MissingAssetProtection).GetMethod(nameof(GetPrefabSafePostfix), BindingFlags.NonPublic | BindingFlags.Static);
                    harmony.Patch(getPrefabBuilding, postfix: new HarmonyMethod(postfix));
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched PrefabCollection<BuildingInfo>.GetPrefab");
                }

                DiagnosticLogger.Checkpoint("AssetProtection.Complete");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Asset protection initialization error: " + ex);
            }
        }

        private static void GetPrefabSafePostfix(uint index, ref BuildingInfo __result)
        {
            if (__result == null && !reportedIndices.Contains(index))
            {
                nullPrefabCount++;
                reportedIndices.Add(index);
                
                if (nullPrefabCount <= 10 || nullPrefabCount % 50 == 0)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Missing BuildingInfo prefab at index {0} (total: {1})", index, nullPrefabCount));
                }
            }
        }

        public static void ReportFinalStats()
        {
            if (nullPrefabCount > 0)
            {
                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] FINAL: Detected {0} missing prefab lookups across {1} unique indices", nullPrefabCount, reportedIndices.Count));
            }
        }
    }

    // ============================================================================
    // TM:PE COMPATIBILITY & LIFECYCLE SYNCHRONIZATION
    // ============================================================================
    public static class TMPECompatibility
    {
        private static Assembly cachedTMPEAssembly = null;

        public static Assembly GetTMPEAssembly()
        {
            if (cachedTMPEAssembly != null) return cachedTMPEAssembly;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == "TrafficManager")
                {
                    cachedTMPEAssembly = asm;
                    return asm;
                }
            }
            try
            {
                cachedTMPEAssembly = Assembly.Load("TrafficManager");
            }
            catch { }
            return cachedTMPEAssembly;
        }

        public static void Apply(HarmonyInstance harmony)
        {
            Assembly tmpe = GetTMPEAssembly();
            if (tmpe == null)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TM:PE assembly not loaded yet; dynamic hooks will apply when available.");
                return;
            }

            try
            {
                // 1. SerializableDataExtension.Load (Prefix & Postfix)
                Type sde = tmpe.GetType("TrafficManager.Lifecycle.SerializableDataExtension");
                if (sde != null)
                {
                    MethodInfo loadM = sde.GetMethod("Load", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (loadM != null)
                    {
                        var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.SerializableDataExtension_Load_Prefix));
                        var post = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.SerializableDataExtension_Load_Postfix));
                        harmony.Patch(loadM, prefix: new HarmonyMethod(pre), postfix: new HarmonyMethod(post));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE SerializableDataExtension.Load");
                    }

                    MethodInfo desM = sde.GetMethod("DeserializeData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (desM != null)
                    {
                        var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.SerializableDataExtension_DeserializeData_Prefix));
                        harmony.Patch(desM, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE SerializableDataExtension.DeserializeData");
                    }

                    MethodInfo saveM = sde.GetMethod("Save", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (saveM != null)
                    {
                        var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.SerializableDataExtension_Save_Prefix));
                        var post = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.SerializableDataExtension_Save_Postfix));
                        harmony.Patch(saveM, prefix: new HarmonyMethod(pre), postfix: new HarmonyMethod(post));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE SerializableDataExtension.Save");
                    }
                }

                // 2. ParkingRestrictionsManager
                Type prm = tmpe.GetType("TrafficManager.Manager.Impl.ParkingRestrictionsManager");
                if (prm != null)
                {
                    MethodInfo onBefore = prm.GetMethod("OnBeforeLoadData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (onBefore != null)
                    {
                        var post = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.ParkingRestrictionsManager_OnBeforeLoadData_Postfix));
                        harmony.Patch(onBefore, postfix: new HarmonyMethod(post));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE ParkingRestrictionsManager.OnBeforeLoadData");
                    }

                    foreach (var m in prm.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (m.Name == "SetParkingAllowed" && m.GetParameters().Length == 3)
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.ParkingRestrictionsManager_SetParkingAllowed_Prefix));
                            harmony.Patch(m, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE ParkingRestrictionsManager.SetParkingAllowed");
                            break;
                        }
                    }

                    MethodInfo loadDataM = prm.GetMethod("LoadData", BindingFlags.Public | BindingFlags.Instance);
                    if (loadDataM != null)
                    {
                        var post = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.ParkingRestrictionsManager_LoadData_Postfix));
                        harmony.Patch(loadDataM, postfix: new HarmonyMethod(post));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE ParkingRestrictionsManager.LoadData");
                    }
                }

                // 3. SegmentEndManager.GetIndex & GetSegmentAndNodeFromIndex
                Type sem = tmpe.GetType("TrafficManager.Manager.Impl.SegmentEndManager");
                if (sem != null)
                {
                    MethodInfo getIndex = sem.GetMethod("GetIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(uint), typeof(bool) }, null)
                                       ?? sem.GetMethod("GetIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(ushort), typeof(bool) }, null);
                    if (getIndex != null)
                    {
                        var pre = getIndex.GetParameters()[0].ParameterType == typeof(uint)
                            ? typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.SegmentEndManager_GetIndex_Prefix))
                            : typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.SegmentEndManager_GetIndex_ushort_Prefix));
                        harmony.Patch(getIndex, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE SegmentEndManager.GetIndex");
                    }

                    MethodInfo getSegNode = sem.GetMethod("GetSegmentAndNodeFromIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (getSegNode != null)
                    {
                        var prms = getSegNode.GetParameters();
                        bool isUint = prms.Length >= 2 && prms[1].ParameterType == typeof(uint).MakeByRefType();
                        var pre = isUint
                            ? typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.SegmentEndManager_GetSegmentAndNodeFromIndex_Prefix))
                            : typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.SegmentEndManager_GetSegmentAndNodeFromIndex_ushort_Prefix));
                        harmony.Patch(getSegNode, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE SegmentEndManager.GetSegmentAndNodeFromIndex");
                    }
                }

                // 4. TurnOnRedManager.GetIndex
                Type torm = tmpe.GetType("TrafficManager.Manager.Impl.TurnOnRedManager");
                if (torm != null)
                {
                    MethodInfo getIndex = torm.GetMethod("GetIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(uint), typeof(bool) }, null)
                                       ?? torm.GetMethod("GetIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(ushort), typeof(bool) }, null);
                    if (getIndex != null)
                    {
                        var pre = getIndex.GetParameters()[0].ParameterType == typeof(uint)
                            ? typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.TurnOnRedManager_GetIndex_Prefix))
                            : typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.TurnOnRedManager_GetIndex_ushort_Prefix));
                        harmony.Patch(getIndex, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE TurnOnRedManager.GetIndex");
                    }
                }

                // 5. TrafficMeasurementManager.GetDirIndex
                Type tmm = tmpe.GetType("TrafficManager.Manager.Impl.TrafficMeasurementManager");
                if (tmm != null)
                {
                    foreach (var m in tmm.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        if (m.Name == "GetDirIndex" && m.GetParameters().Length == 2)
                        {
                            var pre = m.GetParameters()[0].ParameterType == typeof(uint)
                                ? typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.TrafficMeasurementManager_GetDirIndex_Prefix))
                                : typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.TrafficMeasurementManager_GetDirIndex_ushort_Prefix));
                            harmony.Patch(m, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE TrafficMeasurementManager.GetDirIndex");
                            break;
                        }
                    }
                }

                // 6. RoutingManager.GetLaneEndRoutingIndex
                Type rm = tmpe.GetType("TrafficManager.Manager.Impl.RoutingManager");
                if (rm != null)
                {
                    MethodInfo getIdx = rm.GetMethod("GetLaneEndRoutingIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (getIdx != null)
                    {
                        var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.RoutingManager_GetLaneEndRoutingIndex_Prefix));
                        harmony.Patch(getIdx, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE RoutingManager.GetLaneEndRoutingIndex");
                    }
                }

                // 7. TrafficLightSimulationManager.SimulationStep
                Type tlsm = tmpe.GetType("TrafficManager.Manager.Impl.TrafficLightSimulationManager");
                if (tlsm != null)
                {
                    MethodInfo simStep = tlsm.GetMethod("SimulationStep", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    if (simStep != null)
                    {
                        var post = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.TrafficLightSimulationManager_SimulationStep_Postfix));
                        harmony.Patch(simStep, postfix: new HarmonyMethod(post));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE TrafficLightSimulationManager.SimulationStep");
                    }
                }

                // 8. TMPELifecycle.Load
                Type lifecycleType = tmpe.GetType("TrafficManager.Lifecycle.TMPELifecycle");
                if (lifecycleType != null)
                {
                    MethodInfo loadMethod = lifecycleType.GetMethod("Load", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (loadMethod != null)
                    {
                        var post = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.TMPELifecycle_Load_Postfix));
                        harmony.Patch(loadMethod, postfix: new HarmonyMethod(post));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE TMPELifecycle.Load");
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TMPE dynamic patching error: " + ex);
            }
        }

        public static void SynchronizeTMPEBuffersAndArrays(string context)
        {
            try
            {
                Assembly tmpe = GetTMPEAssembly();
                if (tmpe == null) return;

                DiagnosticLogger.Checkpoint("TMPE.Sync.Start." + context);

                // 1. Sync Static Extension Buffers
                // CRITICAL: These MUST point directly to the live game buffers! NEVER reallocate them!
                NetManager nm = Singleton<NetManager>.instance;
                if (nm != null)
                {
                    SetStaticField(tmpe, "TrafficManager.Util.Extensions.NetSegmentExtensions", "_segBuffer", nm.m_segments.m_buffer);
                    SetStaticField(tmpe, "TrafficManager.Util.Extensions.NetNodeExtensions", "_nodeBuffer", nm.m_nodes.m_buffer);
                    SetStaticField(tmpe, "TrafficManager.Util.Extensions.NetLaneExtensions", "_laneBuffer", nm.m_lanes.m_buffer);
                }

                BuildingManager bm = Singleton<BuildingManager>.instance;
                if (bm != null)
                {
                    SetStaticField(tmpe, "TrafficManager.Util.Extensions.BuildingExtensions", "_buildingBuffer", bm.m_buildings.m_buffer);
                }

                VehicleManager vm = Singleton<VehicleManager>.instance;
                if (vm != null)
                {
                    SetStaticField(tmpe, "TrafficManager.Util.Extensions.VehicleExtensions", "_vehicleBuffer", vm.m_vehicles.m_buffer);
                    SetStaticField(tmpe, "TrafficManager.Util.Extensions.ParkedVehicleExtensions", "_parkedVehiclesBuffer", vm.m_parkedVehicles.m_buffer);
                }

                // 2. Flags static arrays
                Type flagsType = tmpe.GetType("TrafficManager.State.Flags");
                if (flagsType != null)
                {
                    ResizeStaticArray(flagsType, "laneArrowFlags", 524288);
                    ResizeStaticArray(flagsType, "highwayLaneArrowFlags", 524288);
                    ResizeStaticArray(flagsType, "laneAllowedVehicleTypesArray", 65536);
                }

                // 3. ParkingRestrictionsManager
                EnsureParkingRestrictionsCapacity(65536);

                // 4. ExtSegmentEndManager & SegmentEndManager
                EnsureSegmentEndManagersCapacity(tmpe);

                // 5. Manager arrays
                ResizeAllManagerArrays(tmpe);

                // 6. GenericArrayCache
                ResizeGenericArrayCaches(tmpe);

                // 7. Ensure PathFind capacity (vanilla and TM:PE)
                EnsurePathFindCapacity();

                DiagnosticLogger.Checkpoint("TMPE.Sync.Complete." + context);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TMPE sync error (" + context + "): " + ex);
            }
        }

        private static void SetStaticField(Assembly asm, string typeName, string fieldName, object value)
        {
            try
            {
                Type t = asm.GetType(typeName);
                if (t != null)
                {
                    FieldInfo f = t.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (f != null && value != null)
                    {
                        f.SetValue(null, value);
                    }
                }
            }
            catch { }
        }

        private static void ResizeStaticArray(Type type, string fieldName, int targetSize)
        {
            try
            {
                FieldInfo f = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null && f.GetValue(null) is Array arr && arr.Length < targetSize)
                {
                    Array newArr = Array.CreateInstance(arr.GetType().GetElementType(), targetSize);
                    Array.Copy(arr, newArr, arr.Length);
                    f.SetValue(null, newArr);
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized static array {0}.{1}: {2} -> {3}", type.Name, fieldName, arr.Length, targetSize));
                }
            }
            catch { }
        }

        public static object GetManagerInstance(Type t)
        {
            if (t == null) return null;
            try
            {
                FieldInfo f = t.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null)
                {
                    object val = f.GetValue(null);
                    if (val != null) return val;
                }

                PropertyInfo p = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (p != null)
                {
                    object val = p.GetValue(null, null);
                    if (val != null) return val;
                }

                MethodInfo m = t.GetMethod("get_Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (m != null)
                {
                    object val = m.Invoke(null, null);
                    if (val != null) return val;
                }

                // Fallback: Constants.ManagerFactory
                Assembly asm = t.Assembly;
                Type constType = asm.GetType("TrafficManager.Constants");
                if (constType != null)
                {
                    PropertyInfo mfProp = constType.GetProperty("ManagerFactory", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (mfProp != null)
                    {
                        object mf = mfProp.GetValue(null, null);
                        if (mf != null)
                        {
                            foreach (var prop in mf.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                            {
                                if (prop.PropertyType == t || t.IsAssignableFrom(prop.PropertyType))
                                {
                                    object val = prop.GetValue(mf, null);
                                    if (val != null) return val;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public static void EnsureParkingRestrictionsCapacity(int minSize)
        {
            try
            {
                Assembly tmpe = GetTMPEAssembly();
                if (tmpe == null) return;
                Type mgrType = tmpe.GetType("TrafficManager.Manager.Impl.ParkingRestrictionsManager");
                if (mgrType == null) return;
                object inst = GetManagerInstance(mgrType);
                if (inst == null) return;

                EnsureParkingRestrictionsCapacity(inst, minSize);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureParkingRestrictionsCapacity error: " + ex);
            }
        }

        public static void EnsureParkingRestrictionsCapacity(object inst, int minSize)
        {
            try
            {
                if (inst == null) return;
                Type mgrType = inst.GetType();
                FieldInfo f = mgrType.GetField("parkingAllowed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null) return;

                bool[][] current = f.GetValue(inst) as bool[][];
                int targetSize = Math.Max(minSize, 65536);
                if (current == null || current.Length < targetSize)
                {
                    bool[][] newArr = new bool[targetSize][];
                    if (current != null)
                    {
                        for (int i = 0; i < current.Length; i++)
                            newArr[i] = current[i];
                    }
                    for (int i = 0; i < targetSize; i++)
                    {
                        if (newArr[i] == null)
                            newArr[i] = new bool[2] { true, true };
                    }
                    f.SetValue(inst, newArr);
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Expanded ParkingRestrictionsManager.parkingAllowed: {0} -> {1}", current != null ? current.Length : 0, targetSize));
                }
                else
                {
                    for (int i = 0; i < current.Length; i++)
                    {
                        if (current[i] == null)
                            current[i] = new bool[2] { true, true };
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureParkingRestrictionsCapacity instance error: " + ex);
            }
        }

        public static void EnsureSegmentEndManagersCapacity(Assembly tmpe)
        {
            try
            {
                // ExtSegmentEndManager
                Type esmType = tmpe.GetType("TrafficManager.Manager.Impl.ExtSegmentEndManager");
                if (esmType != null)
                {
                    object inst = GetManagerInstance(esmType);
                    if (inst != null)
                    {
                        var f = esmType.GetField("<ExtSegmentEnds>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                             ?? esmType.GetField("ExtSegmentEnds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (f != null && f.GetValue(inst) is Array arr)
                        {
                            Array targetArr = arr;
                            Type elemType = arr.GetType().GetElementType();
                            if (arr.Length < 130048)
                            {
                                Array newArr = Array.CreateInstance(elemType, 130048);
                                Array.Copy(arr, newArr, arr.Length);
                                f.SetValue(inst, newArr);
                                targetArr = newArr;
                                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Expanded ExtSegmentEndManager.ExtSegmentEnds: {0} -> 130048", arr.Length));
                            }

                            // Ensure all 65,024 segment ends are properly constructed
                            var ctor = elemType.GetConstructor(new Type[] { typeof(uint), typeof(bool) })
                                    ?? elemType.GetConstructor(new Type[] { typeof(ushort), typeof(bool) });
                            if (ctor != null)
                            {
                                bool isUint = ctor.GetParameters()[0].ParameterType == typeof(uint);
                                FieldInfo fSegId = elemType.GetField("segmentId");
                                int countFixed = 0;
                                for (uint segId = 0; segId < 65024u; segId++)
                                {
                                    int idxStart = (int)(segId * 2);
                                    int idxEnd = idxStart + 1;
                                    object argId = isUint ? (object)segId : (object)(ushort)segId;

                                    object s0 = targetArr.GetValue(idxStart);
                                    if (s0 == null || (segId > 0 && fSegId != null && Convert.ToUInt32(fSegId.GetValue(s0)) != segId))
                                    {
                                        targetArr.SetValue(ctor.Invoke(new object[] { argId, true }), idxStart);
                                        countFixed++;
                                    }

                                    object s1 = targetArr.GetValue(idxEnd);
                                    if (s1 == null || (segId > 0 && fSegId != null && Convert.ToUInt32(fSegId.GetValue(s1)) != segId))
                                    {
                                        targetArr.SetValue(ctor.Invoke(new object[] { argId, false }), idxEnd);
                                        countFixed++;
                                    }
                                }
                                if (countFixed > 0)
                                {
                                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Initialized {0} ExtSegmentEnd slots", countFixed));
                                }

                                // If live game is active, recalculate existing segments >= 36864 to unfreeze current traffic!
                                try
                                {
                                    if (NetManager.instance != null && NetManager.instance.m_segments != null && NetManager.instance.m_segments.m_buffer != null)
                                    {
                                        MethodInfo recalc = esmType.GetMethod("Recalculate", new Type[] { typeof(ushort) })
                                                           ?? esmType.GetMethod("Recalculate", new Type[] { typeof(uint) });
                                        if (recalc != null)
                                        {
                                            int recalcCount = 0;
                                            for (uint segId = 36864u; segId < (uint)Math.Min(NetManager.instance.m_segments.m_buffer.Length, 65024); segId++)
                                            {
                                                if ((NetManager.instance.m_segments.m_buffer[segId].m_flags & NetSegment.Flags.Created) != 0)
                                                {
                                                    recalc.Invoke(inst, new object[] { (ushort)segId });
                                                    recalcCount++;
                                                }
                                            }
                                            if (recalcCount > 0)
                                            {
                                                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Recalculated {0} existing segments >= 36864 in TM:PE ExtSegmentEndManager", recalcCount));
                                            }
                                        }

                                        // Recalculate RoutingManager lane transitions for all existing segments >= 36864
                                        try
                                        {
                                            Type rmType = tmpe.GetType("TrafficManager.Manager.Impl.RoutingManager");
                                            if (rmType != null)
                                            {
                                                object rmInst = GetManagerInstance(rmType);
                                                if (rmInst != null)
                                                {
                                                    MethodInfo rmRecalc = rmType.GetMethod("RecalculateSegment", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(ushort) }, null);
                                                    if (rmRecalc != null)
                                                    {
                                                        int rmCount = 0;
                                                        for (uint segId = 36864u; segId < (uint)Math.Min(NetManager.instance.m_segments.m_buffer.Length, 65024); segId++)
                                                        {
                                                            if ((NetManager.instance.m_segments.m_buffer[segId].m_flags & NetSegment.Flags.Created) != 0)
                                                            {
                                                                rmRecalc.Invoke(rmInst, new object[] { (ushort)segId });
                                                                rmCount++;
                                                            }
                                                        }
                                                        if (rmCount > 0)
                                                        {
                                                            UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Recalculated {0} existing segments >= 36864 in TM:PE RoutingManager", rmCount));
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        catch (Exception exRm)
                                        {
                                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] RoutingManager recalc error: " + exRm);
                                        }
                                    }
                                }
                                catch (Exception exRecalc)
                                {
                                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Recalc segments error: " + exRecalc);
                                }
                            }
                        }
                    }
                }

                // SegmentEndManager
                Type semType = tmpe.GetType("TrafficManager.Manager.Impl.SegmentEndManager");
                if (semType != null)
                {
                    object inst = GetManagerInstance(semType);
                    if (inst != null)
                    {
                        var f = semType.GetField("SegmentEnds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (f != null && f.GetValue(inst) is Array arr && arr.Length < 130048)
                        {
                            Type elemType = arr.GetType().GetElementType();
                            Array newArr = Array.CreateInstance(elemType, 130048);
                            int oldCap = arr.Length / 2;
                            int newCap = 65024;
                            Array.Copy(arr, 0, newArr, 0, oldCap);
                            Array.Copy(arr, oldCap, newArr, newCap, oldCap);
                            f.SetValue(inst, newArr);
                            UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Expanded Split SegmentEndManager.SegmentEnds: {0} -> 130048 (offset {1} -> {2})", arr.Length, oldCap, newCap));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureSegmentEndManagersCapacity error: " + ex);
            }
        }

        private static void ResizeAllManagerArrays(Assembly tmpe)
        {
            try
            {
                Type[] types = tmpe.GetTypes();
                foreach (Type t in types)
                {
                    if (t.Namespace == null || !t.Namespace.StartsWith("TrafficManager")) continue;
                    // CRITICAL: NEVER touch extension buffer classes! They MUST remain direct references to live game buffers!
                    if (t.Namespace.Contains("Extensions")) continue;

                    object inst = GetManagerInstance(t);

                    var fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                    foreach (var f in fields)
                    {
                        if (f.FieldType.IsArray)
                        {
                            Array arr = null;
                            try
                            {
                                if (f.IsStatic) arr = f.GetValue(null) as Array;
                                else if (inst != null) arr = f.GetValue(inst) as Array;
                            }
                            catch { }

                            if (arr == null) continue;

                            int newSize = 0;
                            bool isSplit = false;
                            int splitOldCap = 0;
                            int splitNewCap = 0;

                            // Only resize original node/segment arrays (32768, 36864) to 65024
                            if ((arr.Length == 32768 || arr.Length == 36864 || arr.Length == 32767 || arr.Length == 36863) && arr.Length < 65024)
                            {
                                newSize = 65024;
                            }
                            // Only resize original SegmentEnd arrays (73728 = 36864 * 2) to 130048
                            else if ((arr.Length == 73728 || arr.Length == 73727) && arr.Length < 130048)
                            {
                                newSize = 130048;
                                if (t.Name != "ExtSegmentEndManager")
                                {
                                    isSplit = true;
                                    splitOldCap = arr.Length / 2;
                                    splitNewCap = 65024;
                                }
                            }
                            // Spatial / Geometry buckets (512, 576) to 1024
                            else if ((arr.Length == 512 || arr.Length == 576) && arr.Length < 1024)
                            {
                                newSize = 1024;
                            }
                            // Lane arrays (262144) to 524288
                            else if ((arr.Length == 262144 || arr.Length == 262143) && arr.Length < 524288)
                            {
                                newSize = 524288;
                            }
                            // LaneEnd backward/forward routings in RoutingManager (524288 = 262144 * 2) to 1048576 (524288 * 2)
                            else if (arr.Length == 524288 && (f.Name.Contains("LaneEnd") || t.Name.Contains("RoutingManager")))
                            {
                                newSize = 1048576;
                                isSplit = true;
                                splitOldCap = 262144;
                                splitNewCap = 524288;
                            }

                            if (newSize > 0)
                            {
                                Type elemType = arr.GetType().GetElementType();
                                Array newArr = Array.CreateInstance(elemType, newSize);
                                if (isSplit)
                                {
                                    Array.Copy(arr, 0, newArr, 0, splitOldCap);
                                    Array.Copy(arr, splitOldCap, newArr, splitNewCap, splitOldCap);
                                }
                                else
                                {
                                    Array.Copy(arr, 0, newArr, 0, arr.Length);
                                }

                                // Instantiate element objects if constructor exists (e.g. TrafficLightSimulation(ushort), ExtLane(uint))
                                ConstructorInfo ctorUshort = elemType.GetConstructor(new Type[] { typeof(ushort) });
                                if (ctorUshort != null && !isSplit)
                                {
                                    for (int i = arr.Length; i < newSize; i++)
                                    {
                                        try { newArr.SetValue(ctorUshort.Invoke(new object[] { (ushort)i }), i); } catch { }
                                    }
                                }

                                ConstructorInfo ctorUint = elemType.GetConstructor(new Type[] { typeof(uint) });
                                if (ctorUint != null && !isSplit)
                                {
                                    for (int i = arr.Length; i < newSize; i++)
                                    {
                                        try { newArr.SetValue(ctorUint.Invoke(new object[] { (uint)i }), i); } catch { }
                                    }
                                }

                                // Initialize SegmentDirTrafficData.meanSpeed = 10000
                                if (elemType.Name == "SegmentDirTrafficData")
                                {
                                    FieldInfo msf = elemType.GetField("meanSpeed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (msf != null)
                                    {
                                        object defaultVal = Activator.CreateInstance(elemType);
                                        msf.SetValue(defaultVal, (ushort)10000);
                                        if (isSplit)
                                        {
                                            for (int i = splitOldCap; i < splitNewCap; i++)
                                                newArr.SetValue(defaultVal, i);
                                            for (int i = splitNewCap + splitOldCap; i < newSize; i++)
                                                newArr.SetValue(defaultVal, i);
                                        }
                                        else
                                        {
                                            for (int i = arr.Length; i < newSize; i++)
                                                newArr.SetValue(defaultVal, i);
                                        }
                                    }
                                }

                                if (f.IsStatic) f.SetValue(null, newArr);
                                else if (inst != null) f.SetValue(inst, newArr);
                                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized TM:PE Array {0}.{1}: {2} -> {3}{4}", t.Name, f.Name, arr.Length, newSize, isSplit ? " (split)" : ""));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] ResizeAllManagerArrays error: " + ex);
            }
        }

        private static void ResizeGenericArrayCaches(Assembly tmpe)
        {
            try
            {
                foreach (Type t in tmpe.GetTypes())
                {
                    if (t.Namespace == null || !t.Namespace.StartsWith("TrafficManager")) continue;

                    object inst = GetManagerInstance(t);

                    var fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                    foreach (var f in fields)
                    {
                        if (f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition().Name.Contains("GenericArrayCache"))
                        {
                            object cacheObj = f.IsStatic ? f.GetValue(null) : (inst != null ? f.GetValue(inst) : null);
                            if (cacheObj != null)
                            {
                                Type cacheType = cacheObj.GetType();
                                FieldInfo maxField = cacheType.GetField("maxSize_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                FieldInfo valuesField = cacheType.GetField("Values", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (maxField != null) maxField.SetValue(cacheObj, 65024);
                                if (valuesField != null && valuesField.GetValue(cacheObj) is Array valArr && valArr.Length < 65024)
                                {
                                    Array newValArr = Array.CreateInstance(valArr.GetType().GetElementType(), 65024);
                                    Array.Copy(valArr, newValArr, valArr.Length);
                                    valuesField.SetValue(cacheObj, newValArr);
                                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Expanded GenericArrayCache in {0}.{1}: {2} -> 65024", t.Name, f.Name, valArr.Length));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] ResizeGenericArrayCaches error: " + ex);
            }
        }

        public static void EnsurePathFindCapacity()
        {
            try
            {
                // 1. Vanilla PathManager.m_pathfinds
                PathManager pm = Singleton<PathManager>.instance;
                if (pm != null)
                {
                    FieldInfo fPfs = typeof(PathManager).GetField("m_pathfinds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fPfs != null && fPfs.GetValue(pm) is Array pfsArr)
                    {
                        foreach (object pf in pfsArr)
                        {
                            if (pf == null) continue;
                            Type pfType = pf.GetType();
                            FieldInfo fLoc = pfType.GetField("m_laneLocation", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            FieldInfo fTarg = pfType.GetField("m_laneTarget", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (fLoc != null && fLoc.GetValue(pf) is uint[] locArr && locArr.Length < 524288)
                            {
                                uint[] newLoc = new uint[524288];
                                Array.Copy(locArr, newLoc, locArr.Length);
                                fLoc.SetValue(pf, newLoc);
                            }
                            if (fTarg != null && fTarg.GetValue(pf) is Array targArr && targArr.Length < 524288)
                            {
                                Array newTarg = Array.CreateInstance(targArr.GetType().GetElementType(), 524288);
                                Array.Copy(targArr, newTarg, targArr.Length);
                                fTarg.SetValue(pf, newTarg);
                            }
                        }
                    }
                }

                // 2. TM:PE CustomPathManager._replacementPathFinds
                Assembly tmpe = GetTMPEAssembly();
                if (tmpe != null)
                {
                    Type cpmType = tmpe.GetType("TrafficManager.Custom.PathFinding.CustomPathManager");
                    if (cpmType != null)
                    {
                        FieldInfo fRepl = cpmType.GetField("_replacementPathFinds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (fRepl != null && fRepl.GetValue(null) is Array replArr)
                        {
                            foreach (object cpf in replArr)
                            {
                                if (cpf == null) continue;
                                Type cpfType = cpf.GetType();
                                FieldInfo fLoc = cpfType.GetField("laneLocation_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                FieldInfo fTarg = cpfType.GetField("laneTarget_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (fLoc != null && fLoc.GetValue(cpf) is uint[] locArr && locArr.Length < 524288)
                                {
                                    uint[] newLoc = new uint[524288];
                                    Array.Copy(locArr, newLoc, locArr.Length);
                                    fLoc.SetValue(cpf, newLoc);
                                }
                                if (fTarg != null && fTarg.GetValue(cpf) is Array targArr && targArr.Length < 524288)
                                {
                                    Array newTarg = Array.CreateInstance(targArr.GetType().GetElementType(), 524288);
                                    Array.Copy(targArr, newTarg, targArr.Length);
                                    fTarg.SetValue(cpf, newTarg);
                                }
                            }
                        }
                    }
                }
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Ensured PathFind and CustomPathFind capacity (524288 lanes).");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsurePathFindCapacity error: " + ex);
            }
        }
    }

    // ============================================================================
    // TM:PE HARMONY PATCH CALLBACKS
    // ============================================================================
    public static class TMPEPatches
    {
        public static void SerializableDataExtension_Load_Prefix()
        {
            MemoryOptimizer.StrategicGarbageCollect("TMPE.Load.Prefix");
            TMPECompatibility.SynchronizeTMPEBuffersAndArrays("SerializableDataExtension.Load.Prefix");
        }

        public static void SerializableDataExtension_Load_Postfix()
        {
            TMPECompatibility.SynchronizeTMPEBuffersAndArrays("SerializableDataExtension.Load.Postfix");
            MemoryOptimizer.StrategicGarbageCollect("TMPE.Load.Postfix");
        }

        public static void SerializableDataExtension_DeserializeData_Prefix()
        {
            TMPECompatibility.SynchronizeTMPEBuffersAndArrays("SerializableDataExtension.DeserializeData.Prefix");
        }

        public static void SerializableDataExtension_Save_Prefix()
        {
            TMPECompatibility.SynchronizeTMPEBuffersAndArrays("SerializableDataExtension.Save.Prefix");
            DiagnosticLogger.Checkpoint("TMPE.Save.Start");
        }

        public static void SerializableDataExtension_Save_Postfix()
        {
            DiagnosticLogger.Checkpoint("TMPE.Save.Complete");
            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TM:PE Save completed: all timed traffic lights and node data saved.");
        }

        public static void ParkingRestrictionsManager_OnBeforeLoadData_Postfix(object __instance)
        {
            TMPECompatibility.EnsureParkingRestrictionsCapacity(__instance, 65536);
        }

        public static bool ParkingRestrictionsManager_SetParkingAllowed_Prefix(object __instance, ushort segmentId, ref bool __result)
        {
            try
            {
                TMPECompatibility.EnsureParkingRestrictionsCapacity(__instance, Math.Max((int)segmentId + 1, 65536));
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SetParkingAllowed_Prefix error: " + ex);
                __result = false;
                return false;
            }
        }

        public static void ParkingRestrictionsManager_LoadData_Postfix(ref bool __result)
        {
            if (!__result)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TM:PE ParkingRestrictionsManager.LoadData reported false. Overriding to true to prevent save load abortion.");
                __result = true;
            }
        }

        public static bool SegmentEndManager_GetIndex_Prefix(uint segmentId, bool startNode, ref int __result)
        {
            __result = startNode ? (int)segmentId : ((int)segmentId + 65024);
            return false;
        }

        public static bool SegmentEndManager_GetIndex_ushort_Prefix(ushort segmentId, bool startNode, ref int __result)
        {
            __result = startNode ? (int)segmentId : ((int)segmentId + 65024);
            return false;
        }

        public static bool SegmentEndManager_GetSegmentAndNodeFromIndex_Prefix(int index, out uint segmentId, out bool startNode)
        {
            if (index < 0 || index >= 130048)
            {
                segmentId = 0u;
                startNode = false;
                return false;
            }
            startNode = index < 65024;
            segmentId = (uint)(startNode ? index : (index - 65024));
            return false;
        }

        public static bool SegmentEndManager_GetSegmentAndNodeFromIndex_ushort_Prefix(int index, out ushort segmentId, out bool startNode)
        {
            if (index < 0 || index >= 130048)
            {
                segmentId = 0;
                startNode = false;
                return false;
            }
            startNode = index < 65024;
            segmentId = (ushort)(startNode ? index : (index - 65024));
            return false;
        }

        public static bool TurnOnRedManager_GetIndex_Prefix(uint segmentId, bool startNode, ref int __result)
        {
            __result = startNode ? (int)segmentId : ((int)segmentId + 65024);
            return false;
        }

        public static bool TurnOnRedManager_GetIndex_ushort_Prefix(ushort segmentId, bool startNode, ref int __result)
        {
            __result = startNode ? (int)segmentId : ((int)segmentId + 65024);
            return false;
        }

        public static bool TrafficMeasurementManager_GetDirIndex_Prefix(uint segmentId, NetInfo.Direction dir, ref int __result)
        {
            __result = (int)segmentId + (dir == NetInfo.Direction.Backward ? 65024 : 0);
            return false;
        }

        public static bool TrafficMeasurementManager_GetDirIndex_ushort_Prefix(ushort segmentId, NetInfo.Direction dir, ref int __result)
        {
            __result = (int)segmentId + (dir == NetInfo.Direction.Backward ? 65024 : 0);
            return false;
        }

        public static bool RoutingManager_GetLaneEndRoutingIndex_Prefix(uint laneId, bool startNode, ref uint __result)
        {
            __result = startNode ? laneId : (laneId + 524288u);
            return false;
        }

        private static PropertyInfo propSimulations = null;
        private static MethodInfo removeNodeMethod = null;
        private static MethodInfo fallbackStepMethod = null;
        private delegate void SimOneDelegate(object arr, int idx);
        private static SimOneDelegate simOneDel = null;
        private static bool tmpePostfixInitDone = false;
        private static bool tmpePostfixLoggedOnce = false;

        public static void TrafficLightSimulationManager_SimulationStep_Postfix(object __instance)
        {
            if (__instance == null) return;
            try
            {
                if (propSimulations == null)
                {
                    Type t = __instance.GetType();
                    propSimulations = t.GetProperty("TrafficLightSimulations", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    removeNodeMethod = t.GetMethod("RemoveNodeFromSimulation", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(ushort) }, null)
                                       ?? t.GetMethod("RemoveNodeFromSimulation", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(uint) }, null);
                }

                Array rawArray = propSimulations != null ? propSimulations.GetValue(__instance, null) as Array : null;
                if (rawArray == null || rawArray.Length <= 32768) return;

                if (!tmpePostfixInitDone)
                {
                    tmpePostfixInitDone = true;
                    Type elemType = rawArray.GetType().GetElementType();
                    fallbackStepMethod = elemType.GetMethod("SimulationStep", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    if (fallbackStepMethod != null)
                    {
                        try
                        {
                            Type arrayType = rawArray.GetType();
                            var dm = new DynamicMethod("SimOneTLS", null, new Type[] { typeof(object), typeof(int) }, typeof(TMPEPatches).Module, true);
                            var il = dm.GetILGenerator();
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Castclass, arrayType);
                            il.Emit(OpCodes.Ldarg_1);
                            il.Emit(OpCodes.Ldelema, elemType);
                            il.Emit(OpCodes.Call, fallbackStepMethod);
                            il.Emit(OpCodes.Ret);
                            simOneDel = (SimOneDelegate)dm.CreateDelegate(typeof(SimOneDelegate));
                            UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] TM:PE SimOne DynamicMethod created OK. elemType={0} IsValueType={1} arrayLen={2}", elemType.Name, elemType.IsValueType, rawArray.Length));
                        }
                        catch (Exception ex)
                        {
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TM:PE DynamicMethod creation failed: " + ex);
                            simOneDel = null;
                        }
                    }
                    else
                    {
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TM:PE TrafficLightSimulation.SimulationStep method NOT found!");
                    }
                }

                if (simOneDel == null && fallbackStepMethod == null) return;

                var sm = Singleton<SimulationManager>.instance;
                if (sm == null) return;

                int f = (int)(sm.m_currentFrameIndex & 63);
                int start = 32768 + f * 504;
                int end = Math.Min(start + 504, rawArray.Length);

                if (!tmpePostfixLoggedOnce)
                {
                    tmpePostfixLoggedOnce = true;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] TM:PE Postfix RUNNING. frame={0} range=[{1}..{2}) arrayLen={3} delegate={4}", f, start, end, rawArray.Length, simOneDel != null ? "JIT" : "reflection"));
                }

                for (int i = start; i < end; i++)
                {
                    try
                    {
                        if (simOneDel != null)
                        {
                            simOneDel(rawArray, i);
                        }
                        else
                        {
                            object boxed = rawArray.GetValue(i);
                            fallbackStepMethod.Invoke(boxed, null);
                            rawArray.SetValue(boxed, i);
                        }
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Exception in TM:PE SimulationStep for node {0}: {1}", i, ex.Message));
                        if (removeNodeMethod != null)
                        {
                            try { removeNodeMethod.Invoke(__instance, new object[] { (ushort)i }); } catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TrafficLightSimulationManager Postfix error: " + ex);
            }
        }

        public static void TMPELifecycle_Load_Postfix()
        {
            try
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TMPELifecycle.Load completed! Synchronizing all manager arrays and recalculating segments...");
                TMPECompatibility.SynchronizeTMPEBuffersAndArrays("TMPELifecycle.Load.Postfix");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TMPELifecycle_Load_Postfix error: " + ex);
            }
        }
    }

    // ============================================================================
    // NETMANAGER SIMULATION STEP PATCH (VANILLA SIMULATION FOR EXPANDED NODES & SEGMENTS)
    // ============================================================================
    public static class NetManagerSimulationPatches
    {
        private static bool loggedOnce = false;

        public static void Postfix(NetManager __instance, int subStep)
        {
            if (__instance == null) return;
            if (subStep == 0) return;

            SimulationManager sm = Singleton<SimulationManager>.instance;
            if (sm == null) return;

            int frameIndex = (int)(sm.m_currentFrameIndex & 255u);

            if (!loggedOnce)
            {
                loggedOnce = true;
                int nodeCount = __instance.m_nodes.m_buffer != null ? __instance.m_nodes.m_buffer.Length : 0;
                int segCount = __instance.m_segments.m_buffer != null ? __instance.m_segments.m_buffer.Length : 0;
                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] NetManager.SimulationStepImpl Postfix RUNNING! frameIndex={0} nodeBuffer={1} segBuffer={2}", frameIndex, nodeCount, segCount));
            }

            // 1. Simulate expanded nodes: 32768 .. 65023 (32256 nodes / 256 = 126 per frame)
            int nodeStart = 32768 + frameIndex * 126;
            int nodeEnd = nodeStart + 126;
            if (nodeEnd > 65024) nodeEnd = 65024;

            NetNode[] nodes = __instance.m_nodes.m_buffer;
            if (nodes != null)
            {
                int maxNode = Math.Min(nodeEnd, nodes.Length);
                for (int i = nodeStart; i < maxNode; i++)
                {
                    if ((nodes[i].m_flags & NetNode.Flags.Created) != 0)
                    {
                        Notification.ProblemStruct problems = nodes[i].m_problems;
                        NetInfo info = nodes[i].Info;
                        if (info != null && info.m_netAI != null)
                        {
                            try
                            {
                                info.m_netAI.SimulationStep((ushort)i, ref nodes[i]);
                            }
                            catch (Exception ex)
                            {
                                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Exception simulating node {0}: {1}", i, ex.Message));
                            }
                        }
                        if (nodes[i].m_problems != problems)
                        {
                            try { __instance.UpdateNodeNotifications((ushort)i, problems, nodes[i].m_problems); } catch { }
                            __instance.UpdateNodeRenderer((ushort)i, false);
                        }
                    }
                }

                if (Singleton<InfoManager>.exists)
                {
                    var mode = Singleton<InfoManager>.instance.CurrentMode;
                    if (mode != InfoManager.InfoMode.None || __instance.m_wetnessChanged != 0)
                    {
                        if (nodeStart < maxNode)
                        {
                            __instance.UpdateNodeColors((ushort)nodeStart, (ushort)(maxNode - 1));
                        }
                    }
                }
            }

            // 2. Simulate expanded segments: 36864 .. 65023 (28160 segments / 256 = 110 per frame)
            int segStart = 36864 + frameIndex * 110;
            int segEnd = segStart + 110;
            if (segEnd > 65024) segEnd = 65024;

            NetSegment[] segments = __instance.m_segments.m_buffer;
            if (segments != null)
            {
                int maxSeg = Math.Min(segEnd, segments.Length);
                for (int i = segStart; i < maxSeg; i++)
                {
                    if ((segments[i].m_flags & NetSegment.Flags.Created) != 0)
                    {
                        Notification.ProblemStruct problems = segments[i].m_problems;
                        NetInfo info = segments[i].Info;
                        if (info != null && info.m_netAI != null)
                        {
                            try
                            {
                                info.m_netAI.SimulationStep((ushort)i, ref segments[i]);
                            }
                            catch (Exception ex)
                            {
                                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Exception simulating segment {0}: {1}", i, ex.Message));
                            }
                        }
                        if (segments[i].m_problems != problems)
                        {
                            try { __instance.UpdateSegmentNotifications((ushort)i, problems, segments[i].m_problems); } catch { }
                            __instance.UpdateSegmentRenderer((ushort)i, false);
                        }
                    }
                }

                if (Singleton<InfoManager>.exists)
                {
                    var mode = Singleton<InfoManager>.instance.CurrentMode;
                    if (mode != InfoManager.InfoMode.None || __instance.m_wetnessChanged != 0)
                    {
                        if (segStart < maxSeg)
                        {
                            __instance.UpdateSegmentColors((ushort)segStart, (ushort)(maxSeg - 1));
                        }
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(NetManager), "UpdateSegmentColors", new Type[0])]
    public static class NetManagerUpdateSegmentColorsZeroPatch
    {
        public static void Postfix(NetManager __instance)
        {
            if (__instance == null || __instance.m_segments.m_buffer == null) return;
            __instance.UpdateSegmentColors(0, (ushort)(__instance.m_segments.m_buffer.Length - 1));
        }
    }

    [HarmonyPatch(typeof(NetManager), "UpdateNodeColors", new Type[0])]
    public static class NetManagerUpdateNodeColorsZeroPatch
    {
        public static void Postfix(NetManager __instance)
        {
            if (__instance == null || __instance.m_nodes.m_buffer == null) return;
            __instance.UpdateNodeColors(0, (ushort)(__instance.m_nodes.m_buffer.Length - 1));
        }
    }

    [HarmonyPatch(typeof(InfoManager), "SetActualMode")]
    public static class InfoManagerSetActualModePatch
    {
        public static void Postfix()
        {
            try
            {
                var nm = Singleton<NetManager>.instance;
                if (nm != null && nm.m_segments.m_buffer != null && nm.m_nodes.m_buffer != null)
                {
                    nm.UpdateSegmentColors(0, (ushort)(nm.m_segments.m_buffer.Length - 1));
                    nm.UpdateNodeColors(0, (ushort)(nm.m_nodes.m_buffer.Length - 1));
                }
            }
            catch {}
        }
    }

    [HarmonyPatch(typeof(RenderManager), "UpdateInstance", new Type[] { typeof(uint) })]
    public static class RenderManagerUpdateInstancePatch
    {
        public static bool Prefix(RenderManager __instance, uint holder)
        {
            try
            {
                if (__instance == null) return false;
                if (__instance.m_indices == null || holder >= (uint)__instance.m_indices.Length)
                {
                    int newSize = (int)Math.Max(holder + 4096, 262144);
                    ushort[] oldArr = __instance.m_indices;
                    ushort[] newArr = new ushort[newSize];
                    for (int i = 0; i < newArr.Length; i++) newArr[i] = ushort.MaxValue;
                    if (oldArr != null)
                    {
                        Array.Copy(oldArr, newArr, Math.Min(oldArr.Length, newArr.Length));
                    }
                    __instance.m_indices = newArr;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Dynamically expanded RenderManager.m_indices to {0} for holder {1}", newSize, holder));
                }
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] RenderManagerUpdateInstancePatch error: " + ex);
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(RenderManager), "GetInstanceIndex")]
    public static class RenderManagerGetInstanceIndexPatch
    {
        public static bool Prefix(RenderManager __instance, uint holder, ref uint instanceIndex, ref bool __result)
        {
            try
            {
                if (__instance == null || __instance.m_indices == null || holder >= (uint)__instance.m_indices.Length)
                {
                    instanceIndex = 0;
                    __result = false;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] RenderManagerGetInstanceIndexPatch error: " + ex);
                instanceIndex = 0;
                __result = false;
                return false;
            }
        }
    }

    // ============================================================================
    // MAIN LOADING EXTENSION
    // ============================================================================
    public class RenewLoading : LoadingExtensionBase
    {
        private static bool patchApplied = false;

        private static int SafePatchKianCommons(HarmonyInstance harmony, Type type, string methodName, string transpilerName)
        {
            try
            {
                MethodInfo methodInfo = null;
                MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (MethodInfo methodInfo2 in methods)
                {
                    try
                    {
                        if (methodInfo2.Name != methodName) continue;
                        var parameters = methodInfo2.GetParameters();
                        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(ushort))
                        {
                            methodInfo = methodInfo2;
                            break;
                        }
                    }
                    catch { continue; }
                }

                if (methodInfo != null)
                {
                    MethodInfo transpiler = typeof(Transpilers).GetMethod(transpilerName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (transpiler != null)
                    {
                        harmony.Patch(methodInfo, transpiler: new HarmonyMethod(transpiler));
                        UnityEngine.Debug.Log($"[MoreNetworkCapacityRenew] Safely patched KianCommons.{methodName} on Mac.");
                        return 1;
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log($"[MoreNetworkCapacityRenew] Error patching {methodName}: {ex.Message}");
            }
            return 0;
        }
        
        public override void OnCreated(ILoading loading)
        {
            base.OnCreated(loading);
            MemoryOptimizer.LogMemoryStatus("OnCreated.PreLoad");
            MemoryOptimizer.StrategicGarbageCollect("OnCreated (Pre-Save Loading)");
            RenewMod.EnsureCoreActive();
            MemoryOptimizer.PrewarmHeap();
            DiagnosticLogger.Checkpoint("OnCreated.Start");
            
            if (!patchApplied)
            {
                var harmony = HarmonyInstance.Create("MoreNetworkCapacityRenew.Comprehensive");

                try
                {
                    harmony.PatchAll(Assembly.GetExecutingAssembly());
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Static Harmony patches applied successfully.");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Static Harmony patches error: " + ex);
                }

                try
                {
                    // Dynamic IL transpiler for constants
                    DynamicPatcher.Apply(harmony);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] DynamicPatcher error: " + ex);
                }

                try
                {
                    // Dynamic hooks for TM:PE
                    TMPECompatibility.Apply(harmony);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TMPECompatibility error: " + ex);
                }

                try
                {
                    // Patch KianCommons limits
                    int patchedCount = 0;
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type netUtilType = assembly.GetType("KianCommons.NetUtil");
                        if (netUtilType != null)
                        {
                            patchedCount += SafePatchKianCommons(harmony, netUtilType, "IsNodeValid", nameof(Transpilers.NodeLimitTranspiler));
                            patchedCount += SafePatchKianCommons(harmony, netUtilType, "IsSegmentValid", nameof(Transpilers.SegmentLimitTranspiler));
                            patchedCount += SafePatchKianCommons(harmony, netUtilType, "AssertSegmentValid", nameof(Transpilers.SegmentLimitTranspiler));
                        }
                    }
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Successfully patched " + patchedCount + " KianCommons limits to prevent Mac freeze!");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] KianCommons patching error: " + ex);
                }

                try
                {
                    // Patch NetManager.SimulationStepImpl for expanded nodes and segments simulation
                    MethodInfo simStepImpl = typeof(NetManager).GetMethod("SimulationStepImpl", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (simStepImpl != null)
                    {
                        MethodInfo postfix = typeof(NetManagerSimulationPatches).GetMethod(nameof(NetManagerSimulationPatches.Postfix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(simStepImpl, postfix: new HarmonyMethod(postfix));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched NetManager.SimulationStepImpl for expanded nodes and segments simulation!");
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] NetManager.SimulationStepImpl patch error: " + ex);
                }

                patchApplied = true;
                DiagnosticLogger.Checkpoint("OnCreated.AllPatchesApplied");
                
                TMPECompatibility.SynchronizeTMPEBuffersAndArrays("OnCreated");
                RenewMod.EnsureRenderManagerCapacity();
                InstanceManagerCompatibility.EnsureCompatibility();
            }
        }

        public override void OnLevelLoaded(LoadMode mode)
        {
            DiagnosticLogger.Checkpoint("OnLevelLoaded.Start");
            MemoryOptimizer.LogMemoryStatus("OnLevelLoaded.Start");
            
            try
            {
                RenewMod.EnsureRenderManagerCapacity();
                InstanceManagerCompatibility.EnsureCompatibility();
                // Synchronize TM:PE buffers on level load
                TMPECompatibility.SynchronizeTMPEBuffersAndArrays("OnLevelLoaded");
                TMPECompatibility.EnsurePathFindCapacity();

                // ToolController collision arrays
                var tm = Singleton<ToolManager>.instance;
                if (tm != null)
                {
                    var tc = tm.m_properties;
                    if (tc != null)
                    {
                        int requiredSize = 1024; // 65536 / 64

                        FieldInfo f1 = typeof(ToolController).GetField("m_collidingSegments1", BindingFlags.NonPublic | BindingFlags.Instance);
                        FieldInfo f2 = typeof(ToolController).GetField("m_collidingSegments2", BindingFlags.NonPublic | BindingFlags.Instance);
                        
                        if (f1 != null) f1.SetValue(tc, new ulong[requiredSize]);
                        if (f2 != null) f2.SetValue(tc, new ulong[requiredSize]);

                        FieldInfo b1 = typeof(ToolController).GetField("m_collidingBuildings1", BindingFlags.NonPublic | BindingFlags.Instance);
                        FieldInfo b2 = typeof(ToolController).GetField("m_collidingBuildings2", BindingFlags.NonPublic | BindingFlags.Instance);

                        if (b1 != null) b1.SetValue(tc, new ulong[1024]);
                        if (b2 != null) b2.SetValue(tc, new ulong[1024]);

                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Successfully resized ToolController collision arrays to " + requiredSize);
                    }
                }
                
                // Network sanitization
                SanitizeNetwork();

                // Vehicle grid sanitization (breaks any circular vehicle loops)
                SanitizeVehicleGrid();
                
                // Report missing asset stats
                MissingAssetProtection.ReportFinalStats();

                // Initial full network color refresh
                var nm = Singleton<NetManager>.instance;
                if (nm != null && nm.m_segments.m_buffer != null && nm.m_nodes.m_buffer != null)
                {
                    nm.UpdateSegmentColors(0, (ushort)(nm.m_segments.m_buffer.Length - 1));
                    nm.UpdateNodeColors(0, (ushort)(nm.m_nodes.m_buffer.Length - 1));
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Initialized full network color refresh on level load.");
                }
                
                DiagnosticLogger.Checkpoint("OnLevelLoaded.Complete");
                MemoryOptimizer.StrategicGarbageCollect("OnLevelLoaded (Post-Save Cleanup)");
                MemoryOptimizer.LogMemoryStatus("Gameplay Ready");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] OnLevelLoaded error: " + ex);
            }
        }

        public override void OnLevelUnloading()
        {
            base.OnLevelUnloading();
            MemoryOptimizer.LogMemoryStatus("OnLevelUnloading");
            MemoryOptimizer.StrategicGarbageCollect("LevelUnloading (Free City Resources)");
        }

        public override void OnReleased()
        {
            base.OnReleased();
            MemoryOptimizer.LogMemoryStatus("OnReleased");
            MemoryOptimizer.StrategicGarbageCollect("OnReleased (Flush Heap)");
        }

        private void SanitizeNetwork()
        {
            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Starting network sanitization...");
            int fixedCount = 0;
            NetManager nm = Singleton<NetManager>.instance;
            
            for (int i = 1; i < nm.m_segments.m_size; i++)
            {
                if ((nm.m_segments.m_buffer[i].m_flags & NetSegment.Flags.Created) != 0)
                {
                    NetInfo info = nm.m_segments.m_buffer[i].Info;
                    if (info == null)
                    {
                        nm.m_segments.m_buffer[i].m_flags = NetSegment.Flags.None;
                        fixedCount++;
                        continue;
                    }

                    if (info.m_lanes == null) continue;
                    
                    int maxLanes = info.m_lanes.Length;
                    uint currentLane = nm.m_segments.m_buffer[i].m_lanes;
                    int count = 0;
                    uint prevLane = 0;
                    
                    while (currentLane != 0 && count < maxLanes)
                    {
                        prevLane = currentLane;
                        currentLane = nm.m_lanes.m_buffer[currentLane].m_nextLane;
                        count++;
                    }
                    
                    if (currentLane != 0 && prevLane != 0)
                    {
                        nm.m_lanes.m_buffer[prevLane].m_nextLane = 0;
                        fixedCount++;
                    }

                    // Clean up abnormal traffic buffer or blocked flags caused by previous unsimulated segments
                    if (nm.m_segments.m_buffer[i].m_trafficBuffer == 65535)
                    {
                        nm.m_segments.m_buffer[i].m_trafficBuffer = 0;
                        nm.m_segments.m_buffer[i].m_flags &= ~NetSegment.Flags.Blocked;
                    }
                    if (nm.m_segments.m_buffer[i].m_trafficDensity > 100)
                    {
                        nm.m_segments.m_buffer[i].m_trafficDensity = 100;
                    }
                }
            }
            UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Network sanitization complete. Fixed {0} corrupted segments.", fixedCount));
        }

        private void SanitizeVehicleGrid()
        {
            try
            {
                VehicleManager vm = Singleton<VehicleManager>.instance;
                if (vm == null || vm.m_vehicles == null || vm.m_vehicles.m_buffer == null) return;

                int fixedGrid = 0;
                int fixedGrid2 = 0;
                int fixedParked = 0;

                // 1. Active vehicles grid
                if (vm.m_vehicleGrid != null)
                {
                    var visited = new HashSet<ushort>();
                    for (int i = 0; i < vm.m_vehicleGrid.Length; i++)
                    {
                        visited.Clear();
                        ushort vId = vm.m_vehicleGrid[i];
                        ushort prev = 0;
                        while (vId != 0)
                        {
                            if (visited.Contains(vId) || vId >= vm.m_vehicles.m_buffer.Length)
                            {
                                if (prev != 0) vm.m_vehicles.m_buffer[prev].m_nextGridVehicle = 0;
                                else vm.m_vehicleGrid[i] = 0;
                                fixedGrid++;
                                break;
                            }
                            visited.Add(vId);
                            prev = vId;
                            vId = vm.m_vehicles.m_buffer[vId].m_nextGridVehicle;
                        }
                    }
                }

                // 2. Secondary grid
                if (vm.m_vehicleGrid2 != null)
                {
                    var visited = new HashSet<ushort>();
                    for (int i = 0; i < vm.m_vehicleGrid2.Length; i++)
                    {
                        visited.Clear();
                        ushort vId = vm.m_vehicleGrid2[i];
                        ushort prev = 0;
                        while (vId != 0)
                        {
                            if (visited.Contains(vId) || vId >= vm.m_vehicles.m_buffer.Length)
                            {
                                if (prev != 0) vm.m_vehicles.m_buffer[prev].m_nextGridVehicle = 0;
                                else vm.m_vehicleGrid2[i] = 0;
                                fixedGrid2++;
                                break;
                            }
                            visited.Add(vId);
                            prev = vId;
                            vId = vm.m_vehicles.m_buffer[vId].m_nextGridVehicle;
                        }
                    }
                }

                // 3. Parked vehicles grid
                if (vm.m_parkedGrid != null && vm.m_parkedVehicles != null && vm.m_parkedVehicles.m_buffer != null)
                {
                    var visited = new HashSet<ushort>();
                    for (int i = 0; i < vm.m_parkedGrid.Length; i++)
                    {
                        visited.Clear();
                        ushort pId = vm.m_parkedGrid[i];
                        ushort prev = 0;
                        while (pId != 0)
                        {
                            if (visited.Contains(pId) || pId >= vm.m_parkedVehicles.m_buffer.Length)
                            {
                                if (prev != 0) vm.m_parkedVehicles.m_buffer[prev].m_nextGridParked = 0;
                                else vm.m_parkedGrid[i] = 0;
                                fixedParked++;
                                break;
                            }
                            visited.Add(pId);
                            prev = pId;
                            pId = vm.m_parkedVehicles.m_buffer[pId].m_nextGridParked;
                        }
                    }
                }

                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Vehicle grid sanitization complete: broke {0} active loops, {1} grid2 loops, {2} parked loops", fixedGrid, fixedGrid2, fixedParked));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeVehicleGrid error: " + ex);
            }
        }
    }

    // ============================================================================
    // TRANSPILERS FOR KIANCOMMONS
    // ============================================================================
    internal static class Transpilers
    {
        public static IEnumerable<CodeInstruction> NodeLimitTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int val && val == 32768)
                {
                    instruction.operand = 65024;
                }
                yield return instruction;
            }
        }

        public static IEnumerable<CodeInstruction> SegmentLimitTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int val && val == 36864)
                {
                    instruction.operand = 65024;
                }
                yield return instruction;
            }
        }
    }

    // ============================================================================
    // DYNAMIC IL CONSTANT PATCHER
    // ============================================================================
    public static class DynamicPatcher
    {
        public static void Apply(HarmonyInstance harmony)
        {
            DiagnosticLogger.Checkpoint("DynamicPatcher.Start");
            try
            {
                int patchedMethodCount = 0;
                int patchedConstCount = 0;

                Assembly tmpe = TMPECompatibility.GetTMPEAssembly();
                if (tmpe != null)
                {
                    foreach (Type type in tmpe.GetTypes())
                    {
                        if (type.Namespace != null && (type.Namespace.StartsWith("TrafficManager") || type.Namespace.StartsWith("CSUtil.Commons") || type.Namespace.StartsWith("SharedEnvironment")))
                        {
                            int result = PatchType(harmony, type);
                            if (result > 0) patchedMethodCount++;
                            patchedConstCount += result;
                        }
                    }
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Patched {0} constants in {1} TM:PE methods", patchedConstCount, patchedMethodCount));
                }

                try
                {
                    Assembly baseAssembly = Assembly.Load("Assembly-CSharp");
                    if (baseAssembly != null)
                    {
                        string[] safeTypes = new string[] {
                            "NetManager", "CoverageManager",
                            "TransportManager", "TransportLine", "TransportTool", "TransportNode",
                            "PathManager", "DistrictManager", "ZoneBlock", "BuildingDecoration",
                            "NetAdjust", "UnlockManager", "EventManager", "Building",
                            "NetTool", "BuildingTool",
                            "RoadBaseAI", "CarAI", "VehicleAI", "PassengerCarAI", "BusAI", "TrainAI",
                            "NetAI", "RoadAI", "TrainTrackAI", "PedestrianPathAI", 
                            "NetSegment", "NetNode", "NetLane", "Vehicle", "Citizen", "CitizenInstance"
                        };

                        int patchedCount2 = 0;
                        int patchedConstCount2 = 0;
                        foreach (Type type in baseAssembly.GetTypes())
                        {
                            string checkName = type.Name;
                            if (type.DeclaringType != null) checkName = type.DeclaringType.Name;
                            
                            if (Array.IndexOf(safeTypes, checkName) >= 0)
                            {
                                int result = PatchType(harmony, type);
                                if (result > 0) patchedCount2++;
                                patchedConstCount2 += result;
                            }
                        }
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Patched {0} constants in {1} Assembly-CSharp methods", patchedConstCount2, patchedCount2));
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Assembly-CSharp patch error: " + ex);
                }

                DiagnosticLogger.Checkpoint("DynamicPatcher.Complete");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] DynamicPatcher error: " + ex);
            }
        }

        private static int PatchType(HarmonyInstance harmony, Type type)
        {
            if (type == null || type.IsInterface || type.IsGenericTypeDefinition || type.IsGenericType || type.IsEnum)
                return 0;

            int count = 0;
            try
            {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                
                var transpiler = typeof(DynamicPatcher).GetMethod("ValidationTranspiler", BindingFlags.NonPublic | BindingFlags.Static);

                foreach (var method in methods)
                {
                    try
                    {
                        if (method.IsGenericMethod || method.IsGenericMethodDefinition) continue;
                        if (method.IsAbstract) continue;
                        if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0) continue;

                        var body = method.GetMethodBody();
                        if (body == null) continue;
                        byte[] il = body.GetILAsByteArray();
                        if (il == null) continue;

                        bool isPathFindOrRouting = type.Name.Contains("PathFind") || type.Name.Contains("RoutingManager");
                        bool hasConstant = false;
                        for (int i = 0; i < il.Length - 4; i++)
                        {
                            if (il[i] == 0x20) // ldc.i4
                            {
                                int val = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
                                if (val == 36864 || val == 32768 || val == 36863 || val == 32767 || val == 73728 || val == 73727 ||
                                    (isPathFindOrRouting && (val == 262144 || val == 262143)))
                                {
                                    hasConstant = true;
                                    break;
                                }
                            }
                        }
                        if (!hasConstant) continue;

                        harmony.Patch(method, transpiler: new HarmonyMethod(transpiler));
                        count++;
                    }
                    catch { }
                }

                foreach (var ctor in constructors)
                {
                    try
                    {
                        var body = ctor.GetMethodBody();
                        if (body == null) continue;
                        byte[] il = body.GetILAsByteArray();
                        if (il == null) continue;

                        bool isPathFindOrRouting = type.Name.Contains("PathFind") || type.Name.Contains("RoutingManager");
                        bool hasConstant = false;
                        for (int i = 0; i < il.Length - 4; i++)
                        {
                            if (il[i] == 0x20) // ldc.i4
                            {
                                int val = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
                                if (val == 36864 || val == 32768 || val == 36863 || val == 32767 || val == 73728 || val == 73727 ||
                                    (isPathFindOrRouting && (val == 262144 || val == 262143)))
                                {
                                    hasConstant = true;
                                    break;
                                }
                            }
                        }
                        if (!hasConstant) continue;

                        harmony.Patch(ctor, transpiler: new HarmonyMethod(transpiler));
                        count++;
                    }
                    catch { }
                }
            }
            catch { }
            return count;
        }

        private static bool IsBoundsCheckOp(OpCode op)
        {
            return op == OpCodes.Blt || op == OpCodes.Blt_S || op == OpCodes.Blt_Un || op == OpCodes.Blt_Un_S
                || op == OpCodes.Ble || op == OpCodes.Ble_S || op == OpCodes.Ble_Un || op == OpCodes.Ble_Un_S
                || op == OpCodes.Bgt || op == OpCodes.Bgt_S || op == OpCodes.Bgt_Un || op == OpCodes.Bgt_Un_S
                || op == OpCodes.Bge || op == OpCodes.Bge_S || op == OpCodes.Bge_Un || op == OpCodes.Bge_Un_S
                || op == OpCodes.Bne_Un || op == OpCodes.Bne_Un_S
                || op == OpCodes.Beq || op == OpCodes.Beq_S
                || op == OpCodes.Clt || op == OpCodes.Clt_Un
                || op == OpCodes.Cgt || op == OpCodes.Cgt_Un
                || op == OpCodes.Ceq;
        }

        private static bool IsAllocationOp(OpCode op)
        {
            return op == OpCodes.Newarr || op == OpCodes.Newobj;
        }

        private static bool IsPreserveOp(OpCode op)
        {
            return op == OpCodes.And || op == OpCodes.Or || op == OpCodes.Xor
                || op == OpCodes.Div || op == OpCodes.Div_Un
                || op == OpCodes.Rem || op == OpCodes.Rem_Un
                || op == OpCodes.Mul
                || op == OpCodes.Stloc || op == OpCodes.Stloc_S
                || op == OpCodes.Stloc_0 || op == OpCodes.Stloc_1 || op == OpCodes.Stloc_2 || op == OpCodes.Stloc_3
                || op == OpCodes.Call || op == OpCodes.Callvirt;
        }

        private static IEnumerable<CodeInstruction> ValidationTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                var instruction = codes[i];
                if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int val)
                {
                    bool isPathFindOrRouting = original != null && original.DeclaringType != null &&
                        (original.DeclaringType.Name.Contains("PathFind") || original.DeclaringType.Name.Contains("RoutingManager"));
                    if (val == 36864 || val == 32768 || val == 36863 || val == 32767 || val == 73728 || val == 73727 ||
                        (isPathFindOrRouting && (val == 262144 || val == 262143)))
                    {
                        bool shouldChange = false;

                        for (int j = i + 1; j < Math.Min(i + 3, codes.Count); j++)
                        {
                            var nextOp = codes[j].opcode;

                            if (IsPreserveOp(nextOp) && !((nextOp == OpCodes.Stloc || nextOp == OpCodes.Stloc_S || nextOp == OpCodes.Stloc_0 || nextOp == OpCodes.Stloc_1 || nextOp == OpCodes.Stloc_2 || nextOp == OpCodes.Stloc_3) && (original != null && (original.Name == "SimulationStepImpl" || original.Name == "SimulationStep" || original.Name == "CalculatePath" || original.Name == "UpdateData" || original.Name == "Deserialize"))))
                            {
                                shouldChange = false;
                                break;
                            }

                            if (IsBoundsCheckOp(nextOp) || IsAllocationOp(nextOp) || nextOp == OpCodes.Stfld || nextOp == OpCodes.Stsfld)
                            {
                                shouldChange = true;
                                break;
                            }
                            
                            if (nextOp == OpCodes.Prefix1)
                            {
                                shouldChange = true;
                                break;
                            }
                        }

                        // Check previous instruction for bitwise flag test pattern: AND, BRFALSE
                        if (shouldChange && i >= 2)
                        {
                            var prevOp = codes[i - 1].opcode;
                            if (prevOp == OpCodes.Brfalse || prevOp == OpCodes.Brfalse_S ||
                                prevOp == OpCodes.Brtrue || prevOp == OpCodes.Brtrue_S)
                            {
                                for (int k = i - 2; k >= Math.Max(0, i - 4); k--)
                                {
                                    if (codes[k].opcode == OpCodes.And || codes[k].opcode == OpCodes.Or)
                                    {
                                        shouldChange = false;
                                        break;
                                    }
                                }
                            }
                        }

                        if (shouldChange)
                        {
                            int expectedVal = val;
                            if (val == 32768 || val == 36864) expectedVal = 65024;
                            else if (val == 32767 || val == 36863) expectedVal = 65023;
                            else if (val == 73728) expectedVal = 130048;
                            else if (val == 73727) expectedVal = 130047;
                            else if (val == 262144) expectedVal = 524288;
                            else if (val == 262143) expectedVal = 524287;

                            instruction.operand = expectedVal;
                        }
                    }
                }
                yield return instruction;
            }
        }
    }
}
