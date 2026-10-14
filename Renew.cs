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
                        if (args.Name.StartsWith("EManagersLib,") || args.Name == "EManagersLib")
                        {
                            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                            {
                                if (a.GetName().Name == "EManagersLib.API" || a.GetName().Name == "EManagersLib") return a;
                            }
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
                SavePanelOnSavingCompletePatch.ApplySavePanelPatch();
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
            try
            {
                MethodInfo forwardEx = typeof(ColossalFramework.UI.UIView).GetMethod("ForwardException", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(Exception) }, null);
                if (forwardEx != null)
                {
                    var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.ForwardException_Prefix));
                    harmony.Patch(forwardEx, prefix: new HarmonyMethod(pre));
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched UIView.ForwardException to suppress TM:PE warning popups");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching UIView.ForwardException: " + ex);
            }

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

                    MethodInfo ldsM = sde.GetMethod("LoadDataState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (ldsM != null)
                    {
                        var post = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.SerializableDataExtension_LoadDataState_Postfix));
                        harmony.Patch(ldsM, postfix: new HarmonyMethod(post));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE SerializableDataExtension.LoadDataState");
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

                // 1b. ExtSegmentManager (OnBeforeLoadData safe Prefix & Recalculate guard)
                Type esm = tmpe.GetType("TrafficManager.Manager.Impl.ExtSegmentManager");
                if (esm != null)
                {
                    MethodInfo obldM = esm.GetMethod("OnBeforeLoadData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    if (obldM != null)
                    {
                        var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.ExtSegmentManager_OnBeforeLoadData_Prefix));
                        harmony.Patch(obldM, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE ExtSegmentManager.OnBeforeLoadData (safe Prefix)");
                    }

                    MethodInfo recalcM = esm.GetMethod("Recalculate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(ushort) }, null);
                    if (recalcM != null)
                    {
                        var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.ExtSegmentManager_Recalculate_ushort_Prefix));
                        harmony.Patch(recalcM, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE ExtSegmentManager.Recalculate");
                    }
                }

                // 1c. ExtSegmentEndManager (OnLevelLoading safe Prefix & CalculateCorners guard)
                Type esem = tmpe.GetType("TrafficManager.Manager.Impl.ExtSegmentEndManager");
                if (esem != null)
                {
                    MethodInfo ollM = esem.GetMethod("OnLevelLoading", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    if (ollM != null)
                    {
                        var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.ExtSegmentEndManager_OnLevelLoading_Prefix));
                        harmony.Patch(ollM, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE ExtSegmentEndManager.OnLevelLoading (safe Prefix)");
                    }

                    MethodInfo calcCornersM = esem.GetMethod("CalculateCorners", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(ushort), typeof(bool) }, null);
                    if (calcCornersM != null)
                    {
                        var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.ExtSegmentEndManager_CalculateCorners_Prefix));
                        harmony.Patch(calcCornersM, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE ExtSegmentEndManager.CalculateCorners");
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

                    MethodInfo updEndM = sem.GetMethod("UpdateSegmentEnd", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(ushort), typeof(bool) }, null);
                    if (updEndM != null)
                    {
                        var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.SegmentEndManager_UpdateSegmentEnd_Prefix));
                        harmony.Patch(updEndM, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE SegmentEndManager.UpdateSegmentEnd");
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

                // 5. TrafficMeasurementManager.GetDirIndex & data access
                Type tmm = tmpe.GetType("TrafficManager.Manager.Impl.TrafficMeasurementManager");
                if (tmm != null)
                {
                    try
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
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching TrafficMeasurementManager.GetDirIndex: " + ex);
                    }

                    try
                    {
                        MethodInfo addTrafficM = tmm.GetMethod("AddTraffic", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(ushort), typeof(byte), typeof(ushort) }, null);
                        if (addTrafficM != null)
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.TrafficMeasurementManager_AddTraffic_Prefix));
                            harmony.Patch(addTrafficM, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE TrafficMeasurementManager.AddTraffic (safe Prefix)");
                        }
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching TrafficMeasurementManager.AddTraffic: " + ex);
                    }

                    try
                    {
                        MethodInfo onSimStepM = tmm.GetMethod("OnBeforeSimulationStep", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(ushort), typeof(NetSegment).MakeByRefType() }, null);
                        if (onSimStepM != null)
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.TrafficMeasurementManager_OnBeforeSimulationStep_Prefix));
                            harmony.Patch(onSimStepM, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE TrafficMeasurementManager.OnBeforeSimulationStep (safe Prefix)");
                        }
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching TrafficMeasurementManager.OnBeforeSimulationStep: " + ex);
                    }

                    try
                    {
                        MethodInfo destroyStatsM = tmm.GetMethod("DestroySegmentStats", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(ushort) }, null);
                        if (destroyStatsM != null)
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.TrafficMeasurementManager_DestroySegmentStats_Prefix));
                            harmony.Patch(destroyStatsM, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE TrafficMeasurementManager.DestroySegmentStats (safe Prefix)");
                        }
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching TrafficMeasurementManager.DestroySegmentStats: " + ex);
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

                // 9. SpeedLimitManager.GetGameSpeedLimit (Prefix)
                Type slm = tmpe.GetType("TrafficManager.Manager.Impl.SpeedLimitManager");
                if (slm != null)
                {
                    MethodInfo getSpeedM = slm.GetMethod("GetGameSpeedLimit", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(uint), typeof(NetInfo.Lane) }, null);
                    if (getSpeedM != null)
                    {
                        var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.SpeedLimitManager_GetGameSpeedLimit_Prefix));
                        harmony.Patch(getSpeedM, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE SpeedLimitManager.GetGameSpeedLimit (Prefix)");
                    }

                    MethodInfo getSpeedM4 = slm.GetMethod("GetGameSpeedLimit", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(ushort), typeof(byte), typeof(uint), typeof(NetInfo.Lane) }, null);
                    if (getSpeedM4 != null)
                    {
                        var pre4 = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.SpeedLimitManager_GetGameSpeedLimit4_Prefix));
                        harmony.Patch(getSpeedM4, prefix: new HarmonyMethod(pre4));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE SpeedLimitManager.GetGameSpeedLimit (4-arg Prefix)");
                    }

                    MethodInfo getSpeedM1 = slm.GetMethod("GetGameSpeedLimit", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(uint) }, null);
                    if (getSpeedM1 != null)
                    {
                        var pre1 = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.SpeedLimitManager_GetGameSpeedLimit1_Prefix));
                        harmony.Patch(getSpeedM1, prefix: new HarmonyMethod(pre1));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE SpeedLimitManager.GetGameSpeedLimit (1-arg Prefix)");
                    }
                }

                // 10. CustomPathManager.UpdateWithPathManagerValues (Postfix)
                Type cpmType = tmpe.GetType("TrafficManager.Custom.PathFinding.CustomPathManager");
                if (cpmType != null)
                {
                    MethodInfo upmvM = cpmType.GetMethod("UpdateWithPathManagerValues", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (upmvM != null)
                    {
                        var post = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.CustomPathManager_UpdateWithPathManagerValues_Postfix));
                        harmony.Patch(upmvM, postfix: new HarmonyMethod(post));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE CustomPathManager.UpdateWithPathManagerValues (Postfix)");
                    }
                }

                // 11. CustomSegmentLightsManager (Overflow protection & capacity)
                Type cslmType = tmpe.GetType("TrafficManager.Manager.Impl.CustomSegmentLightsManager");
                if (cslmType != null)
                {
                    MethodInfo onUnload = cslmType.GetMethod("OnLevelUnloading", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (onUnload != null)
                    {
                        var post = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.CustomSegmentLightsManager_OnLevelUnloading_Postfix));
                        harmony.Patch(onUnload, postfix: new HarmonyMethod(post));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE CustomSegmentLightsManager.OnLevelUnloading");
                    }

                    MethodInfo isSegLight = cslmType.GetMethod("IsSegmentLight", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(ushort), typeof(bool) }, null);
                    if (isSegLight != null)
                    {
                        var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.CustomSegmentLightsManager_IsSegmentLight_Prefix));
                        harmony.Patch(isSegLight, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE CustomSegmentLightsManager.IsSegmentLight");
                    }

                    MethodInfo remSegLights = cslmType.GetMethod("RemoveSegmentLights", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(ushort) }, null);
                    if (remSegLights != null)
                    {
                        var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.CustomSegmentLightsManager_RemoveSegmentLights_Prefix));
                        harmony.Patch(remSegLights, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE CustomSegmentLightsManager.RemoveSegmentLights");
                    }

                    MethodInfo remSegLight = cslmType.GetMethod("RemoveSegmentLight", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(ushort), typeof(bool) }, null);
                    if (remSegLight != null)
                    {
                        var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.CustomSegmentLightsManager_RemoveSegmentLight_Prefix));
                        harmony.Patch(remSegLight, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE CustomSegmentLightsManager.RemoveSegmentLight");
                    }

                    MethodInfo addSegLights = cslmType.GetMethod("AddSegmentLights", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(ushort), typeof(bool), typeof(RoadBaseAI.TrafficLightState) }, null);
                    if (addSegLights != null)
                    {
                        var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.CustomSegmentLightsManager_EnsureCapacity_Prefix));
                        harmony.Patch(addSegLights, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE CustomSegmentLightsManager.AddSegmentLights");
                    }

                    MethodInfo getSegLights = cslmType.GetMethod("GetSegmentLights", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(ushort), typeof(bool), typeof(bool), typeof(RoadBaseAI.TrafficLightState) }, null);
                    if (getSegLights != null)
                    {
                        var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.CustomSegmentLightsManager_EnsureCapacity_Prefix));
                        harmony.Patch(getSegLights, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE CustomSegmentLightsManager.GetSegmentLights");
                    }
                }

                // 12. VehicleRestrictionsManager (Overflow protection & capacity)
                Type vrmType = tmpe.GetType("TrafficManager.Manager.Impl.VehicleRestrictionsManager");
                if (vrmType != null)
                {
                    foreach (var m in vrmType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (m.Name == "GetDefaultAllowedVehicleTypes")
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.VehicleRestrictionsManager_EnsureCapacity_Prefix));
                            harmony.Patch(m, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE VehicleRestrictionsManager.GetDefaultAllowedVehicleTypes");
                        }
                        else if (m.Name == "LoadData")
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.VehicleRestrictionsManager_EnsureCapacity_Prefix));
                            var post = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.GenericManager_LoadData_Postfix));
                            harmony.Patch(m, prefix: new HarmonyMethod(pre), postfix: new HarmonyMethod(post));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE VehicleRestrictionsManager.LoadData");
                        }
                    }
                }

                // 13. TrafficLightSimulationManager (SetUpTimedTrafficLight & LoadData)
                if (tlsm != null)
                {
                    foreach (var m in tlsm.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (m.Name == "SetUpTimedTrafficLight" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(ushort))
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.TrafficLightSimulationManager_EnsureCapacity_Prefix));
                            harmony.Patch(m, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE TrafficLightSimulationManager.SetUpTimedTrafficLight");
                            break;
                        }
                    }

                    foreach (var m in tlsm.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (m.Name == "LoadData")
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.TrafficLightSimulationManager_LoadData_Prefix));
                            var post = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.GenericManager_LoadData_Postfix));
                            harmony.Patch(m, prefix: new HarmonyMethod(pre), postfix: new HarmonyMethod(post));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE TrafficLightSimulationManager.LoadData");
                            break;
                        }
                    }
                }

                // 14. TrafficPriorityManager.LoadData & signs
                try
                {
                    Type tpmType = tmpe.GetType("TrafficManager.Manager.Impl.TrafficPriorityManager");
                    if (tpmType != null)
                    {
                        foreach (var m in tpmType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (m.Name == "LoadData")
                            {
                                var post = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.GenericManager_LoadData_Postfix));
                                harmony.Patch(m, postfix: new HarmonyMethod(post));
                                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE TrafficPriorityManager.LoadData");
                            }
                        }

                        MethodInfo remSignM = tpmType.GetMethod("RemovePrioritySignFromSegmentEnd", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(ushort), typeof(bool) }, null);
                        if (remSignM != null)
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.TrafficPriorityManager_RemovePrioritySignFromSegmentEnd_Prefix));
                            harmony.Patch(remSignM, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE TrafficPriorityManager.RemovePrioritySignFromSegmentEnd");
                        }

                        MethodInfo hasSignM1 = tpmType.GetMethod("HasSegmentPrioritySign", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(ushort) }, null);
                        if (hasSignM1 != null)
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.TrafficPriorityManager_HasSegmentPrioritySign_Prefix));
                            harmony.Patch(hasSignM1, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE TrafficPriorityManager.HasSegmentPrioritySign (1-arg)");
                        }

                        MethodInfo hasSignM2 = tpmType.GetMethod("HasSegmentPrioritySign", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(ushort), typeof(bool) }, null);
                        if (hasSignM2 != null)
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.TrafficPriorityManager_HasSegmentPrioritySign2_Prefix));
                            harmony.Patch(hasSignM2, prefix: new HarmonyMethod(pre));
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching TrafficPriorityManager: " + ex);
                }

                // 15. JunctionRestrictionsManager.LoadData & queries
                try
                {
                    Type jrmType = tmpe.GetType("TrafficManager.Manager.Impl.JunctionRestrictionsManager");
                    if (jrmType != null)
                    {
                        foreach (var m in jrmType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (m.Name == "LoadData")
                            {
                                var post = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.GenericManager_LoadData_Postfix));
                                harmony.Patch(m, postfix: new HarmonyMethod(post));
                                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE JunctionRestrictionsManager.LoadData");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching JunctionRestrictionsManager: " + ex);
                }

                // 16. LaneArrowManager.LoadData
                try
                {
                    Type lamType = tmpe.GetType("TrafficManager.Manager.Impl.LaneArrowManager");
                    if (lamType != null)
                    {
                        foreach (var m in lamType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (m.Name == "LoadData")
                            {
                                var post = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.GenericManager_LoadData_Postfix));
                                harmony.Patch(m, postfix: new HarmonyMethod(post));
                                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE LaneArrowManager.LoadData");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching LaneArrowManager: " + ex);
                }

                // 17. TrafficLightManager.LoadData
                try
                {
                    Type tlmType = tmpe.GetType("TrafficManager.Manager.Impl.TrafficLightManager");
                    if (tlmType != null)
                    {
                        foreach (var m in tlmType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (m.Name == "LoadData")
                            {
                                var post = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.GenericManager_LoadData_Postfix));
                                harmony.Patch(m, postfix: new HarmonyMethod(post));
                                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE TrafficLightManager.LoadData");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching TrafficLightManager: " + ex);
                }

                // 18. SpeedLimitManager.LoadData
                try
                {
                    if (slm != null)
                    {
                        foreach (var m in slm.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (m.Name == "LoadData")
                            {
                                var post = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.GenericManager_LoadData_Postfix));
                                harmony.Patch(m, postfix: new HarmonyMethod(post));
                                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE SpeedLimitManager.LoadData");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching SpeedLimitManager: " + ex);
                }

                // 19. Flags.SetLaneAllowedVehicleTypes
                try
                {
                    Type flagsType = tmpe.GetType("TrafficManager.State.Flags");
                    if (flagsType != null)
                    {
                        foreach (var m in flagsType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                        {
                            if (m.Name == "SetLaneAllowedVehicleTypes")
                            {
                                var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.Flags_SetLaneAllowedVehicleTypes_Prefix));
                                harmony.Patch(m, prefix: new HarmonyMethod(pre));
                                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE Flags.SetLaneAllowedVehicleTypes");
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching Flags: " + ex);
                }

                // 20. RoadUI (Suppress NullReferenceException on level unloading)
                try
                {
                    Type roadUiType = tmpe.GetType("TrafficManager.UI.Textures.RoadUI");
                    if (roadUiType != null)
                    {
                        MethodInfo onUnload = roadUiType.GetMethod("OnLevelUnloading", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                        if (onUnload != null)
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.RoadUI_OnLevelUnloading_Prefix));
                            harmony.Patch(onUnload, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE RoadUI.OnLevelUnloading");
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching RoadUI: " + ex);
                }

                // 21. PrioritySignsTool (Safe RefreshCurrentPriorityNodeIds)
                try
                {
                    Type pstType = tmpe.GetType("TrafficManager.UI.SubTools.PrioritySigns.PrioritySignsTool");
                    if (pstType != null)
                    {
                        MethodInfo refPriM = pstType.GetMethod("RefreshCurrentPriorityNodeIds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        if (refPriM != null)
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.PrioritySignsTool_RefreshCurrentPriorityNodeIds_Prefix));
                            harmony.Patch(refPriM, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE PrioritySignsTool.RefreshCurrentPriorityNodeIds (safe Prefix)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching PrioritySignsTool: " + ex);
                }

                // 22. JunctionRestrictionsTool (Safe RefreshCurrentRestrictedNodeIds)
                try
                {
                    Type jrtType = tmpe.GetType("TrafficManager.UI.SubTools.JunctionRestrictionsTool");
                    if (jrtType != null)
                    {
                        MethodInfo refJuncM = jrtType.GetMethod("RefreshCurrentRestrictedNodeIds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(ushort) }, null);
                        if (refJuncM != null)
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.JunctionRestrictionsTool_RefreshCurrentRestrictedNodeIds_Prefix));
                            harmony.Patch(refJuncM, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE JunctionRestrictionsTool.RefreshCurrentRestrictedNodeIds (safe Prefix)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching JunctionRestrictionsTool: " + ex);
                }

                // 23. CustomPathFind (Awake, PathFindImplementation, GetLaneDirection, ProcessItemMain)
                try
                {
                    Type cpfType = tmpe.GetType("TrafficManager.Custom.PathFinding.CustomPathFind");
                    if (cpfType != null)
                    {
                        MethodInfo awakeM = cpfType.GetMethod("Awake", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (awakeM != null)
                        {
                            var post = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.CustomPathFind_Awake_Postfix));
                            harmony.Patch(awakeM, postfix: new HarmonyMethod(post));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE CustomPathFind.Awake (capacity postfix)");
                        }

                        MethodInfo gldM = cpfType.GetMethod("GetLaneDirection", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (gldM != null)
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.CustomPathFind_GetLaneDirection_Prefix));
                            harmony.Patch(gldM, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE CustomPathFind.GetLaneDirection");
                        }

                        MethodInfo pimM = cpfType.GetMethod("ProcessItemMain", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (pimM != null)
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.CustomPathFind_ProcessItemMain_Prefix));
                            harmony.Patch(pimM, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE CustomPathFind.ProcessItemMain");
                        }

                        MethodInfo pfiM = cpfType.GetMethod("PathFindImplementation", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (pfiM != null)
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.CustomPathFind_PathFindImplementation_Prefix));
                            harmony.Patch(pfiM, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE CustomPathFind.PathFindImplementation with safety prefix");
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching CustomPathFind: " + ex);
                }

                // 25. ThreadingExtension.OnBeforeSimulationFrame
                try
                {
                    Type teType = tmpe.GetType("TrafficManager.Lifecycle.ThreadingExtension");
                    if (teType != null)
                    {
                        MethodInfo obfM = teType.GetMethod("OnBeforeSimulationFrame", BindingFlags.Public | BindingFlags.Instance);
                        if (obfM != null)
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.ThreadingExtension_OnBeforeSimulationFrame_Prefix));
                            harmony.Patch(obfM, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE ThreadingExtension.OnBeforeSimulationFrame");
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching ThreadingExtension: " + ex);
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
                    ResizeStaticArray(flagsType, "laneAllowedVehicleTypesArray", 65024);
                }

                // 3. ParkingRestrictionsManager
                EnsureParkingRestrictionsCapacity(65024);

                // 4. ExtNodeManager & ExtSegmentManager (Ensure all 65,536 nodes and segments are initialized)
                EnsureExtNodeManagerCapacity(tmpe);
                EnsureExtSegmentManagerCapacity(tmpe);

                // 4b. GeometryManager, RoutingManager, VehicleRestrictions, CustomSegmentLights, TrafficLightSimulations, TrafficPriority, JunctionRestrictions, TurnOnRed
                EnsureGeometryManagerCapacity(tmpe);
                EnsureRoutingManagerCapacity(tmpe);
                EnsureVehicleRestrictionsCapacity(tmpe);
                EnsureCustomSegmentLightsCapacity(tmpe);
                EnsureTrafficLightSimulationCapacity(tmpe);
                EnsureTrafficPriorityCapacity(tmpe);
                EnsureJunctionRestrictionsCapacity(tmpe);
                EnsureTurnOnRedCapacity(tmpe);
                EnsureTrafficMeasurementCapacity(tmpe);

                // 4c. ExtSegmentEndManager & SegmentEndManager
                EnsureSegmentEndManagersCapacity(tmpe);

                // 5. Manager arrays
                ResizeAllManagerArrays(tmpe);

                // 5b. SpeedLimitManager capacity & initialize default speeds
                EnsureSpeedLimitManagerCapacity(tmpe);

                // 6. GenericArrayCache
                ResizeGenericArrayCaches(tmpe);

                // 7. Ensure PathFind capacity (vanilla and TM:PE) - Synchronize all 65K node, segment, lane buffers
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
                if (f != null)
                {
                    Array arr = f.GetValue(null) as Array;
                    if (arr == null || arr.Length < targetSize)
                    {
                        Type elemType = f.FieldType.GetElementType();
                        Array newArr = Array.CreateInstance(elemType, targetSize);
                        if (arr != null)
                        {
                            Array.Copy(arr, newArr, arr.Length);
                        }
                        f.SetValue(null, newArr);
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized static array {0}.{1}: {2} -> {3}", type.Name, fieldName, arr != null ? arr.Length : 0, targetSize));
                    }
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

        public static void EnsureFieldInstance(Type type, object instance, string fieldName, string managerTypeName)
        {
            try
            {
                FieldInfo f = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.GetValue(instance) == null)
                {
                    Assembly tmpe = GetTMPEAssembly();
                    if (tmpe != null)
                    {
                        Type mgrType = tmpe.GetType(managerTypeName);
                        if (mgrType != null)
                        {
                            object mgr = GetManagerInstance(mgrType);
                            if (mgr != null)
                            {
                                f.SetValue(instance, mgr);
                            }
                        }
                    }
                }
            }
            catch { }
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
                int targetSize = Math.Max(minSize, 65024);
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

        public static float[] s_cachedLaneSpeedLimits = null;

        public static void EnsureExtNodeManagerCapacity(Assembly tmpe)
        {
            try
            {
                Type enmType = tmpe.GetType("TrafficManager.Manager.Impl.ExtNodeManager");
                if (enmType == null) return;
                object inst = GetManagerInstance(enmType);
                if (inst == null) return;

                FieldInfo f = enmType.GetField("<ExtNodes>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                           ?? enmType.GetField("ExtNodes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null) return;

                Array arr = f.GetValue(inst) as Array;
                if (arr == null) return;

                Type elemType = arr.GetType().GetElementType();
                int targetLen = 65024;
                Array targetArr = arr;

                if (arr.Length < targetLen)
                {
                    Array newArr = Array.CreateInstance(elemType, targetLen);
                    Array.Copy(arr, newArr, arr.Length);
                    f.SetValue(inst, newArr);
                    targetArr = newArr;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized TM:PE ExtNodeManager.ExtNodes: {0} -> {1}", arr.Length, targetLen));
                }

                // Initialize all ExtNode structs so segmentIds != null across all nodes
                ConstructorInfo ctor = elemType.GetConstructor(new Type[] { typeof(ushort) })
                                    ?? elemType.GetConstructor(new Type[] { typeof(uint) });
                FieldInfo fSegIds = elemType.GetField("segmentIds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (ctor != null)
                {
                    bool isUint = ctor.GetParameters()[0].ParameterType == typeof(uint);
                    int countInitialized = 0;
                    for (int i = 0; i < targetArr.Length; i++)
                    {
                        object nodeObj = targetArr.GetValue(i);
                        bool needsInit = false;
                        if (nodeObj == null)
                        {
                            needsInit = true;
                        }
                        else if (fSegIds != null && fSegIds.GetValue(nodeObj) == null)
                        {
                            needsInit = true;
                        }

                        if (needsInit)
                        {
                            object arg = isUint ? (object)(uint)i : (object)(ushort)i;
                            targetArr.SetValue(ctor.Invoke(new object[] { arg }), i);
                            countInitialized++;
                        }
                    }

                    if (countInitialized > 0)
                    {
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Initialized {0} uninitialized ExtNodes in TM:PE ExtNodeManager", countInitialized));
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureExtNodeManagerCapacity error: " + ex);
            }
        }

        public static void EnsureExtSegmentManagerCapacity(Assembly tmpe)
        {
            try
            {
                Type esmType = tmpe.GetType("TrafficManager.Manager.Impl.ExtSegmentManager");
                if (esmType == null) return;
                object inst = GetManagerInstance(esmType);
                if (inst == null) return;

                FieldInfo f = esmType.GetField("<ExtSegments>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                           ?? esmType.GetField("ExtSegments", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null) return;

                Array arr = f.GetValue(inst) as Array;
                if (arr == null) return;

                Type elemType = arr.GetType().GetElementType();
                int targetLen = 65024;
                Array targetArr = arr;

                if (arr.Length < targetLen)
                {
                    Array newArr = Array.CreateInstance(elemType, targetLen);
                    Array.Copy(arr, newArr, arr.Length);
                    f.SetValue(inst, newArr);
                    targetArr = newArr;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized TM:PE ExtSegmentManager.ExtSegments: {0} -> {1}", arr.Length, targetLen));
                }

                ConstructorInfo ctor = elemType.GetConstructor(new Type[] { typeof(ushort) })
                                    ?? elemType.GetConstructor(new Type[] { typeof(uint) });

                if (ctor != null)
                {
                    bool isUint = ctor.GetParameters()[0].ParameterType == typeof(uint);
                    int countInitialized = 0;
                    for (int i = 0; i < targetArr.Length; i++)
                    {
                        object segObj = targetArr.GetValue(i);
                        if (segObj == null)
                        {
                            object arg = isUint ? (object)(uint)i : (object)(ushort)i;
                            targetArr.SetValue(ctor.Invoke(new object[] { arg }), i);
                            countInitialized++;
                        }
                    }

                    if (countInitialized > 0)
                    {
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Initialized {0} uninitialized ExtSegments in TM:PE ExtSegmentManager", countInitialized));
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureExtSegmentManagerCapacity error: " + ex);
            }
        }

        public static void EnsureGeometryManagerCapacity(Assembly tmpe)
        {
            try
            {
                Type gmType = tmpe.GetType("TrafficManager.Manager.Impl.GeometryManager");
                if (gmType == null) return;
                object inst = GetManagerInstance(gmType);
                if (inst == null) return;

                FieldInfo fSeg = gmType.GetField("updatedSegmentBuckets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fSeg != null && fSeg.GetValue(inst) is ulong[] segBuckets && segBuckets.Length < 1016)
                {
                    ulong[] newBuckets = new ulong[1016];
                    Array.Copy(segBuckets, newBuckets, segBuckets.Length);
                    fSeg.SetValue(inst, newBuckets);
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized GeometryManager.updatedSegmentBuckets: {0} -> 1016", segBuckets.Length));
                }

                FieldInfo fNode = gmType.GetField("updatedNodeBuckets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fNode != null && fNode.GetValue(inst) is ulong[] nodeBuckets && nodeBuckets.Length < 1016)
                {
                    ulong[] newBuckets = new ulong[1016];
                    Array.Copy(nodeBuckets, newBuckets, nodeBuckets.Length);
                    fNode.SetValue(inst, newBuckets);
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized GeometryManager.updatedNodeBuckets: {0} -> 1016", nodeBuckets.Length));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureGeometryManagerCapacity error: " + ex);
            }
        }

        public static void EnsureRoutingManagerCapacity(Assembly tmpe)
        {
            try
            {
                Type rmType = tmpe.GetType("TrafficManager.Manager.Impl.RoutingManager");
                if (rmType == null) return;
                object inst = GetManagerInstance(rmType);
                if (inst == null) return;

                // 1. updatedSegmentBuckets (size 576 -> 1016)
                FieldInfo fBuckets = rmType.GetField("updatedSegmentBuckets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fBuckets != null && fBuckets.GetValue(inst) is ulong[] buckets && buckets.Length < 1016)
                {
                    ulong[] newBuckets = new ulong[1016];
                    Array.Copy(buckets, newBuckets, buckets.Length);
                    fBuckets.SetValue(inst, newBuckets);
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized RoutingManager.updatedSegmentBuckets: {0} -> 1016", buckets.Length));
                }

                // 2. SegmentRoutings (size 36864 -> 65024)
                FieldInfo fSegRoutings = rmType.GetField("<SegmentRoutings>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                      ?? rmType.GetField("SegmentRoutings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fSegRoutings != null && fSegRoutings.GetValue(inst) is Array segRoutings && segRoutings.Length < 65024)
                {
                    Array newSegRoutings = Array.CreateInstance(segRoutings.GetType().GetElementType(), 65024);
                    Array.Copy(segRoutings, newSegRoutings, segRoutings.Length);
                    fSegRoutings.SetValue(inst, newSegRoutings);
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized RoutingManager.SegmentRoutings: {0} -> 65024", segRoutings.Length));
                }

                // 3. LaneEndBackwardRoutings & LaneEndForwardRoutings (size 524288 -> 1048576, split)
                FieldInfo fBack = rmType.GetField("<LaneEndBackwardRoutings>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                               ?? rmType.GetField("LaneEndBackwardRoutings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fBack != null && fBack.GetValue(inst) is Array backArr && backArr.Length < 1048576)
                {
                    Array newBack = Array.CreateInstance(backArr.GetType().GetElementType(), 1048576);
                    int oldCap = backArr.Length / 2; // 262144
                    int newCap = 524288;
                    Array.Copy(backArr, 0, newBack, 0, oldCap);
                    Array.Copy(backArr, oldCap, newBack, newCap, oldCap);
                    fBack.SetValue(inst, newBack);
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized RoutingManager.LaneEndBackwardRoutings: {0} -> 1048576 (split)", backArr.Length));
                }

                FieldInfo fFwd = rmType.GetField("<LaneEndForwardRoutings>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                              ?? rmType.GetField("LaneEndForwardRoutings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fFwd != null && fFwd.GetValue(inst) is Array fwdArr && fwdArr.Length < 1048576)
                {
                    Array newFwd = Array.CreateInstance(fwdArr.GetType().GetElementType(), 1048576);
                    int oldCap = fwdArr.Length / 2;
                    int newCap = 524288;
                    Array.Copy(fwdArr, 0, newFwd, 0, oldCap);
                    Array.Copy(fwdArr, oldCap, newFwd, newCap, oldCap);
                    fFwd.SetValue(inst, newFwd);
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized RoutingManager.LaneEndForwardRoutings: {0} -> 1048576 (split)", fwdArr.Length));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureRoutingManagerCapacity error: " + ex);
            }
        }

        public static void EnsureVehicleRestrictionsCapacity(Assembly tmpe)
        {
            try
            {
                Type vrmType = tmpe.GetType("TrafficManager.Manager.Impl.VehicleRestrictionsManager");
                if (vrmType == null) return;
                object inst = GetManagerInstance(vrmType);
                if (inst == null) return;

                FieldInfo f = vrmType.GetField("defaultVehicleTypeCache", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    Array cache = f.GetValue(inst) as Array;
                    int targetSize = 65024;
                    if (cache == null || cache.Length < targetSize)
                    {
                        Type elemType = f.FieldType.GetElementType();
                        Array newCache = Array.CreateInstance(elemType, targetSize);
                        if (cache != null)
                        {
                            Array.Copy(cache, newCache, cache.Length);
                        }
                        f.SetValue(inst, newCache);
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Initialized/Expanded VehicleRestrictionsManager.defaultVehicleTypeCache: {0} -> {1}", cache != null ? cache.Length : 0, targetSize));
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureVehicleRestrictionsCapacity error: " + ex);
            }
        }

        public static void EnsureCustomSegmentLightsCapacity(Assembly tmpe)
        {
            try
            {
                Type cslmType = tmpe.GetType("TrafficManager.Manager.Impl.CustomSegmentLightsManager");
                if (cslmType == null) return;
                object inst = GetManagerInstance(cslmType);
                if (inst == null) return;

                FieldInfo f = cslmType.GetField("customSegments_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    Array arr = f.GetValue(inst) as Array;
                    int targetSize = 65024;
                    if (arr == null || arr.Length < targetSize)
                    {
                        Type elemType = f.FieldType.GetElementType();
                        Array newArr = Array.CreateInstance(elemType, targetSize);
                        if (arr != null)
                        {
                            Array.Copy(arr, newArr, arr.Length);
                        }
                        f.SetValue(inst, newArr);
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Expanded CustomSegmentLightsManager.customSegments_: {0} -> {1}", arr != null ? arr.Length : 0, targetSize));
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureCustomSegmentLightsCapacity error: " + ex);
            }
        }

        public static void EnsureTrafficLightSimulationCapacity(Assembly tmpe)
        {
            try
            {
                Type tlsmType = tmpe.GetType("TrafficManager.Manager.Impl.TrafficLightSimulationManager");
                if (tlsmType == null) return;
                object inst = GetManagerInstance(tlsmType);
                if (inst == null) return;

                FieldInfo f = tlsmType.GetField("<TrafficLightSimulations>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                           ?? tlsmType.GetField("TrafficLightSimulations", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    Array arr = f.GetValue(inst) as Array;
                    int targetSize = 65024;
                    if (arr == null || arr.Length < targetSize)
                    {
                        Type elemType = arr != null ? arr.GetType().GetElementType() : tmpe.GetType("TrafficManager.TrafficLight.Impl.TrafficLightSimulation");
                        Array newArr = Array.CreateInstance(elemType, targetSize);
                        int oldLen = arr != null ? arr.Length : 0;
                        if (arr != null)
                        {
                            Array.Copy(arr, newArr, oldLen);
                        }

                        ConstructorInfo ctor = elemType.GetConstructor(new Type[] { typeof(ushort) });
                        for (int i = oldLen; i < targetSize; i++)
                        {
                            if (ctor != null)
                            {
                                try { newArr.SetValue(ctor.Invoke(new object[] { (ushort)i }), i); } catch { }
                            }
                        }

                        f.SetValue(inst, newArr);
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Expanded TrafficLightSimulationManager.TrafficLightSimulations: {0} -> {1}", oldLen, targetSize));
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureTrafficLightSimulationCapacity error: " + ex);
            }
        }

        public static void EnsureTrafficPriorityCapacity(Assembly tmpe)
        {
            try
            {
                Type tpmType = tmpe.GetType("TrafficManager.Manager.Impl.TrafficPriorityManager");
                if (tpmType == null) return;
                object inst = GetManagerInstance(tpmType);
                if (inst == null) return;

                string[] fieldNames = new string[] { "PrioritySegments", "invalidPrioritySegments" };
                foreach (string fname in fieldNames)
                {
                    FieldInfo f = tpmType.GetField(fname, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null && f.GetValue(inst) is Array arr && arr.Length < 65024)
                    {
                        Array newArr = Array.CreateInstance(arr.GetType().GetElementType(), 65024);
                        Array.Copy(arr, newArr, arr.Length);
                        f.SetValue(inst, newArr);
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized TrafficPriorityManager.{0}: {1} -> 65024", fname, arr.Length));
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureTrafficPriorityCapacity error: " + ex);
            }
        }

        public static void EnsureJunctionRestrictionsCapacity(Assembly tmpe)
        {
            try
            {
                Type jrmType = tmpe.GetType("TrafficManager.Manager.Impl.JunctionRestrictionsManager");
                if (jrmType == null) return;
                object inst = GetManagerInstance(jrmType);
                if (inst == null) return;

                string[] fieldNames = new string[] { "segmentRestrictions", "invalidSegmentRestrictions" };
                foreach (string fname in fieldNames)
                {
                    FieldInfo f = jrmType.GetField(fname, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null && f.GetValue(inst) is Array arr && arr.Length < 65024)
                    {
                        Array newArr = Array.CreateInstance(arr.GetType().GetElementType(), 65024);
                        Array.Copy(arr, newArr, arr.Length);
                        f.SetValue(inst, newArr);
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized JunctionRestrictionsManager.{0}: {1} -> 65024", fname, arr.Length));
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureJunctionRestrictionsCapacity error: " + ex);
            }
        }

        public static void EnsureTurnOnRedCapacity(Assembly tmpe)
        {
            try
            {
                Type tormType = tmpe.GetType("TrafficManager.Manager.Impl.TurnOnRedManager");
                if (tormType == null) return;
                object inst = GetManagerInstance(tormType);
                if (inst == null) return;

                FieldInfo f = tormType.GetField("<TurnOnRedSegments>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                           ?? tormType.GetField("TurnOnRedSegments", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.GetValue(inst) is Array arr && arr.Length < 130048)
                {
                    Array newArr = Array.CreateInstance(arr.GetType().GetElementType(), 130048);
                    int oldCap = arr.Length / 2; // 36864
                    int newCap = 65024;
                    Array.Copy(arr, 0, newArr, 0, oldCap);
                    Array.Copy(arr, oldCap, newArr, newCap, oldCap);
                    f.SetValue(inst, newArr);
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized TurnOnRedManager.TurnOnRedSegments: {0} -> 130048 (split)", arr.Length));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureTurnOnRedCapacity error: " + ex);
            }
        }

        public static void EnsureTrafficMeasurementCapacity(Assembly tmpe)
        {
            try
            {
                if (tmpe == null) return;
                Type tmmType = tmpe.GetType("TrafficManager.Manager.Impl.TrafficMeasurementManager");
                if (tmmType == null) return;
                object inst = GetManagerInstance(tmmType);
                if (inst == null) return;

                FieldInfo fLane = tmmType.GetField("<LaneTrafficData>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                               ?? tmmType.GetField("LaneTrafficData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fLane != null && fLane.GetValue(inst) is Array arrLane && arrLane.Length < 65024)
                {
                    Array newArr = Array.CreateInstance(arrLane.GetType().GetElementType(), 65024);
                    Array.Copy(arrLane, newArr, arrLane.Length);
                    fLane.SetValue(inst, newArr);
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized TrafficMeasurementManager.LaneTrafficData: {0} -> 65024", arrLane.Length));
                }

                FieldInfo fSeg = tmmType.GetField("<SegmentDirTrafficData>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                              ?? tmmType.GetField("SegmentDirTrafficData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fSeg != null && fSeg.GetValue(inst) is Array arrSeg && arrSeg.Length < 130048)
                {
                    Array newArr = Array.CreateInstance(arrSeg.GetType().GetElementType(), 130048);
                    int oldCap = arrSeg.Length / 2; // 36864
                    int newCap = 65024;
                    Array.Copy(arrSeg, 0, newArr, 0, oldCap);
                    Array.Copy(arrSeg, oldCap, newArr, newCap, oldCap);
                    fSeg.SetValue(inst, newArr);
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized TrafficMeasurementManager.SegmentDirTrafficData: {0} -> 130048 (split)", arrSeg.Length));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureTrafficMeasurementCapacity error: " + ex);
            }
        }

        public static void EnsureSpeedLimitManagerCapacity(Assembly tmpe)
        {
            try
            {
                if (tmpe == null) return;
                Type slmType = tmpe.GetType("TrafficManager.Manager.Impl.SpeedLimitManager");
                if (slmType == null) return;

                object inst = GetManagerInstance(slmType);
                if (inst == null) return;

                FieldInfo fCache = slmType.GetField("cachedLaneSpeedLimits_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fCache == null) return;

                float[] arr = fCache.GetValue(inst) as float[];
                int targetSize = 524288;
                if (arr == null || arr.Length < targetSize)
                {
                    float[] newArr = new float[targetSize];
                    if (arr != null)
                    {
                        Array.Copy(arr, newArr, arr.Length);
                    }
                    fCache.SetValue(inst, newArr);
                    arr = newArr;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Expanded SpeedLimitManager.cachedLaneSpeedLimits_: {0} -> {1}", arr != null ? arr.Length : 0, targetSize));
                }
                s_cachedLaneSpeedLimits = arr;

                // Populate all lanes with valid positive speed limits so NO vehicle ever stops dead
                if (NetManager.instance != null && NetManager.instance.m_segments != null && NetManager.instance.m_segments.m_buffer != null && NetManager.instance.m_lanes != null && NetManager.instance.m_lanes.m_buffer != null)
                {
                    NetSegment[] segBuffer = NetManager.instance.m_segments.m_buffer;
                    NetLane[] laneBuffer = NetManager.instance.m_lanes.m_buffer;
                    int filledCount = 0;

                    int maxSeg = Math.Min(segBuffer.Length, 65024);
                    for (ushort segId = 1; segId < maxSeg; segId++)
                    {
                        if ((segBuffer[segId].m_flags & NetSegment.Flags.Created) == 0) continue;
                        NetInfo info = segBuffer[segId].Info;
                        if (info == null || info.m_lanes == null) continue;

                        uint curLane = segBuffer[segId].m_lanes;
                        int laneIndex = 0;
                        while (curLane != 0 && curLane < (uint)arr.Length && laneIndex < info.m_lanes.Length)
                        {
                            NetInfo.Lane lInfo = info.m_lanes[laneIndex];
                            float defSpeed = (lInfo != null && lInfo.m_speedLimit > 0.001f) ? lInfo.m_speedLimit : 1.25f;

                            if (arr[curLane] <= 0.001f)
                            {
                                arr[curLane] = defSpeed;
                                filledCount++;
                            }

                            curLane = laneBuffer[curLane].m_nextLane;
                            laneIndex++;
                        }
                    }

                    int maxLane = Math.Min(laneBuffer.Length, arr.Length);
                    for (uint laneId = 1; laneId < (uint)maxLane; laneId++)
                    {
                        if (arr[laneId] <= 0.001f && laneBuffer[laneId].m_segment != 0)
                        {
                            ushort sId = laneBuffer[laneId].m_segment;
                            if (sId < (ushort)segBuffer.Length && (segBuffer[sId].m_flags & NetSegment.Flags.Created) != 0)
                            {
                                NetInfo sInfo = segBuffer[sId].Info;
                                if (sInfo != null && sInfo.m_lanes != null)
                                {
                                    float fallback = 1.25f;
                                    for (int li = 0; li < sInfo.m_lanes.Length; li++)
                                    {
                                        if (sInfo.m_lanes[li] != null && sInfo.m_lanes[li].m_speedLimit > 0.001f)
                                        {
                                            fallback = sInfo.m_lanes[li].m_speedLimit;
                                            break;
                                        }
                                    }
                                    arr[laneId] = fallback;
                                    filledCount++;
                                }
                            }
                        }
                    }

                    if (filledCount > 0)
                    {
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Initialized {0} lane speed limits in TM:PE cache", filledCount));
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureSpeedLimitManagerCapacity error: " + ex);
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

        public static void RecalculateLiveSegments(Assembly tmpe)
        {
            try
            {
                if (NetManager.instance == null || NetManager.instance.m_segments == null || NetManager.instance.m_segments.m_buffer == null)
                    return;

                Type esmType = tmpe.GetType("TrafficManager.Manager.Impl.ExtSegmentEndManager");
                if (esmType != null)
                {
                    object inst = GetManagerInstance(esmType);
                    if (inst != null)
                    {
                        MethodInfo recalc = esmType.GetMethod("Recalculate", new Type[] { typeof(ushort) })
                                           ?? esmType.GetMethod("Recalculate", new Type[] { typeof(uint) });
                        if (recalc != null)
                        {
                            int recalcCount = 0;
                            for (uint segId = 36864u; segId < (uint)Math.Min(NetManager.instance.m_segments.m_buffer.Length, 65024); segId++)
                            {
                                if ((NetManager.instance.m_segments.m_buffer[segId].m_flags & NetSegment.Flags.Created) != 0 && NetManager.instance.m_segments.m_buffer[segId].Info != null)
                                {
                                    try { recalc.Invoke(inst, new object[] { (ushort)segId }); recalcCount++; } catch { }
                                }
                            }
                            if (recalcCount > 0)
                                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Recalculated {0} existing segments >= 36864 in TM:PE ExtSegmentEndManager", recalcCount));
                        }
                    }
                }

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
                                if ((NetManager.instance.m_segments.m_buffer[segId].m_flags & NetSegment.Flags.Created) != 0 && NetManager.instance.m_segments.m_buffer[segId].Info != null)
                                {
                                    try { rmRecalc.Invoke(rmInst, new object[] { (ushort)segId }); rmCount++; } catch { }
                                }
                            }
                            if (rmCount > 0)
                                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Recalculated {0} existing segments >= 36864 in TM:PE RoutingManager", rmCount));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] RecalculateLiveSegments error: " + ex);
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
                            // Spatial / Geometry buckets (512, 576) to 1016 (65024 / 64)
                            else if ((arr.Length == 512 || arr.Length == 576) && arr.Length < 1016)
                            {
                                newSize = 1016;
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

        public static void SynchronizeSinglePathFind(object pf, NetNode[] liveNodes, NetSegment[] liveSegs, NetLane[] liveLanes)
        {
            if (pf == null) return;
            Type t = pf.GetType();

            // 1. Node buffer (all nodes synchronized)
            FieldInfo fn = t.GetField("nodeBuffer_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? t.GetField("m_nodeBuffer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fn != null && liveNodes != null)
            {
                fn.SetValue(pf, liveNodes);
            }

            // 2. Segment buffer
            FieldInfo fs = t.GetField("segmentBuffer_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? t.GetField("m_segmentBuffer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fs != null && liveSegs != null)
            {
                fs.SetValue(pf, liveSegs);
            }

            // 3. Lane buffer (vanilla PathFind)
            FieldInfo fl = t.GetField("m_laneBuffer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? t.GetField("laneBuffer_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fl != null && liveLanes != null)
            {
                fl.SetValue(pf, liveLanes);
            }

            // 4. LaneLocation (524288)
            FieldInfo fLoc = t.GetField("laneLocation_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                          ?? t.GetField("m_laneLocation", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fLoc != null && fLoc.GetValue(pf) is uint[] locArr && locArr.Length < 524288)
            {
                uint[] newLoc = new uint[524288];
                Array.Copy(locArr, newLoc, locArr.Length);
                fLoc.SetValue(pf, newLoc);
            }

            // 5. LaneTarget (524288)
            FieldInfo fTarg = t.GetField("laneTarget_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                           ?? t.GetField("m_laneTarget", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fTarg != null && fTarg.GetValue(pf) is Array targArr && targArr.Length < 524288)
            {
                Array newTarg = Array.CreateInstance(targArr.GetType().GetElementType(), 524288);
                Array.Copy(targArr, newTarg, targArr.Length);
                fTarg.SetValue(pf, newTarg);
            }
        }

        public static void EnsurePathFindCapacity()
        {
            try
            {
                NetManager nm = Singleton<NetManager>.instance;
                NetNode[] liveNodes = (nm != null && nm.m_nodes != null) ? nm.m_nodes.m_buffer : null;
                NetSegment[] liveSegs = (nm != null && nm.m_segments != null) ? nm.m_segments.m_buffer : null;
                NetLane[] liveLanes = (nm != null && nm.m_lanes != null) ? nm.m_lanes.m_buffer : null;

                Assembly tmpe = GetTMPEAssembly();
                if (tmpe != null)
                {
                    // Ensure TM:PE static extension buffers are pointing to live game buffers
                    if (liveNodes != null) SetStaticField(tmpe, "TrafficManager.Util.Extensions.NetNodeExtensions", "_nodeBuffer", liveNodes);
                    if (liveSegs != null) SetStaticField(tmpe, "TrafficManager.Util.Extensions.NetSegmentExtensions", "_segBuffer", liveSegs);
                    if (liveLanes != null) SetStaticField(tmpe, "TrafficManager.Util.Extensions.NetLaneExtensions", "_laneBuffer", liveLanes);
                }

                var visitedPathFinds = new HashSet<object>();

                // 1. Vanilla PathManager.m_pathfinds
                PathManager pm = Singleton<PathManager>.instance;
                if (pm != null)
                {
                    FieldInfo fPfs = typeof(PathManager).GetField("m_pathfinds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fPfs != null && fPfs.GetValue(pm) is Array pfsArr)
                    {
                        foreach (object pf in pfsArr)
                        {
                            if (pf != null && visitedPathFinds.Add(pf))
                            {
                                SynchronizeSinglePathFind(pf, liveNodes, liveSegs, liveLanes);
                            }
                        }
                    }

                    // Also scan all PathFind components on PathManager GameObject
                    if (pm.gameObject != null)
                    {
                        var comps = pm.gameObject.GetComponents<UnityEngine.Component>();
                        if (comps != null)
                        {
                            foreach (var comp in comps)
                            {
                                if (comp == null) continue;
                                string name = comp.GetType().Name;
                                if ((name.Contains("PathFind") || name.Contains("CustomPathFind")) && visitedPathFinds.Add(comp))
                                {
                                    SynchronizeSinglePathFind(comp, liveNodes, liveSegs, liveLanes);
                                }
                            }
                        }
                    }
                }

                // 2. TM:PE CustomPathManager._replacementPathFinds
                if (tmpe != null)
                {
                    Type cpmType = tmpe.GetType("TrafficManager.Custom.PathFinding.CustomPathManager");
                    if (cpmType != null)
                    {
                        FieldInfo fInst = cpmType.GetField("_instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        object cpmInst = fInst != null ? fInst.GetValue(null) : null;
                        if (cpmInst != null)
                        {
                            FieldInfo fRepl = cpmType.GetField("_replacementPathFinds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (fRepl != null && fRepl.GetValue(cpmInst) is Array replArr)
                            {
                                foreach (object cpf in replArr)
                                {
                                    if (cpf != null && visitedPathFinds.Add(cpf))
                                    {
                                        SynchronizeSinglePathFind(cpf, liveNodes, liveSegs, liveLanes);
                                    }
                                }
                            }

                            FieldInfo fQueue = cpmType.GetField("QueueItems", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (fQueue != null && fQueue.GetValue(cpmInst) is Array qArr && qArr.Length < 262144)
                            {
                                Array newQ = Array.CreateInstance(qArr.GetType().GetElementType(), 262144);
                                Array.Copy(qArr, newQ, qArr.Length);
                                fQueue.SetValue(cpmInst, newQ);
                            }
                        }
                    }
                }

                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Synchronized {0} pathfinders (PathFind and CustomPathFind) across all {1} nodes, {2} segments, {3} lanes.",
                    visitedPathFinds.Count,
                    liveNodes != null ? liveNodes.Length : 0,
                    liveSegs != null ? liveSegs.Length : 0,
                    liveLanes != null ? liveLanes.Length : 0));
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
        public static bool ForwardException_Prefix(Exception e)
        {
            if (e != null)
            {
                string msg = e.ToString();
                if (msg.Contains("Traffic Manager") ||
                    msg.Contains("timed traffic lights") ||
                    msg.Contains("otherwise your timed traffic lights") ||
                    msg.Contains("TrafficManager") ||
                    msg.Contains("IndexOutOfRangeException"))
                {
                    UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] Suppressed TM:PE error modal popup in UIView.ForwardException: " + e.Message);
                    return false;
                }
            }
            return true;
        }

        private static Action<object, ushort> _extSegRecalcDelegate = null;
        public static bool ExtSegmentManager_OnBeforeLoadData_Prefix(object __instance)
        {
            try
            {
                if (__instance != null)
                {
                    Type t = __instance.GetType();
                    var propExtSegs = t.GetProperty("ExtSegments", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (propExtSegs != null && propExtSegs.GetValue(__instance, null) is Array segsArr)
                    {
                        if (_extSegRecalcDelegate == null)
                        {
                            MethodInfo recalc = t.GetMethod("Recalculate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(ushort) }, null);
                            if (recalc != null)
                            {
                                var dm = new DynamicMethod("FastRecalcExtSeg", null, new Type[] { typeof(object), typeof(ushort) }, typeof(TMPEPatches).Module, true);
                                var il = dm.GetILGenerator();
                                il.Emit(OpCodes.Ldarg_0);
                                il.Emit(OpCodes.Castclass, t);
                                il.Emit(OpCodes.Ldarg_1);
                                il.Emit(OpCodes.Callvirt, recalc);
                                il.Emit(OpCodes.Ret);
                                _extSegRecalcDelegate = (Action<object, ushort>)dm.CreateDelegate(typeof(Action<object, ushort>));
                            }
                        }

                        if (_extSegRecalcDelegate != null)
                        {
                            NetManager nm = Singleton<NetManager>.instance;
                            int maxSeg = 65024;
                            if (nm != null && nm.m_segments != null && nm.m_segments.m_buffer != null)
                            {
                                maxSeg = Math.Min(maxSeg, nm.m_segments.m_buffer.Length);
                            }
                            int maxIdx = Math.Min(segsArr.Length, maxSeg);
                            for (int i = 1; i < maxIdx; i++)
                            {
                                if (nm != null && nm.m_segments != null && nm.m_segments.m_buffer != null && (nm.m_segments.m_buffer[i].m_flags & NetSegment.Flags.Created) == 0)
                                    continue;
                                try { _extSegRecalcDelegate(__instance, (ushort)i); } catch { }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] ExtSegmentManager_OnBeforeLoadData_Prefix error: " + ex);
            }
            return false;
        }

        public static bool ExtSegmentManager_Recalculate_ushort_Prefix(ushort segmentId)
        {
            if (segmentId >= 65024) return false;
            if (NetManager.instance != null && NetManager.instance.m_segments != null && NetManager.instance.m_segments.m_buffer != null)
            {
                if (segmentId >= NetManager.instance.m_segments.m_buffer.Length) return false;
            }
            return true;
        }

        public static bool ExtSegmentEndManager_OnLevelLoading_Prefix(object __instance)
        {
            try
            {
                if (__instance != null)
                {
                    Type t = __instance.GetType();
                    var propEnds = t.GetProperty("ExtSegmentEnds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (propEnds != null && propEnds.GetValue(__instance, null) is Array endsArr)
                    {
                        MethodInfo calcCorners = t.GetMethod("CalculateCorners", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(ushort), typeof(bool) }, null);
                        if (calcCorners != null)
                        {
                            int maxSeg = 65024;
                            if (NetManager.instance != null && NetManager.instance.m_segments != null && NetManager.instance.m_segments.m_buffer != null)
                            {
                                maxSeg = Math.Min(maxSeg, NetManager.instance.m_segments.m_buffer.Length);
                            }
                            int maxIdx = Math.Min(endsArr.Length, maxSeg * 2);
                            Type elemType = endsArr.GetType().GetElementType();
                            FieldInfo fSegId = elemType.GetField("segmentId");
                            FieldInfo fStartNode = elemType.GetField("startNode");
                            for (int i = 0; i < maxIdx; i++)
                            {
                                object end = endsArr.GetValue(i);
                                if (end != null && fSegId != null && fStartNode != null)
                                {
                                    ushort segId = Convert.ToUInt16(fSegId.GetValue(end));
                                    if (segId < maxSeg)
                                    {
                                        bool startNode = Convert.ToBoolean(fStartNode.GetValue(end));
                                        try { calcCorners.Invoke(__instance, new object[] { segId, startNode }); } catch { }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] ExtSegmentEndManager_OnLevelLoading_Prefix error: " + ex);
            }
            return false;
        }

        public static bool ExtSegmentEndManager_CalculateCorners_Prefix(ushort segmentId, bool startNode)
        {
            if (segmentId >= 65024) return false;
            if (NetManager.instance != null && NetManager.instance.m_segments != null && NetManager.instance.m_segments.m_buffer != null)
            {
                if (segmentId >= NetManager.instance.m_segments.m_buffer.Length) return false;
            }
            return true;
        }

        public static bool SegmentEndManager_UpdateSegmentEnd_Prefix(ushort segmentId, bool startNode, ref bool __result)
        {
            if (segmentId >= 65024)
            {
                __result = false;
                return false;
            }
            if (NetManager.instance != null && NetManager.instance.m_segments != null && NetManager.instance.m_segments.m_buffer != null)
            {
                if (segmentId >= NetManager.instance.m_segments.m_buffer.Length)
                {
                    __result = false;
                    return false;
                }
            }
            return true;
        }

        public static bool TrafficPriorityManager_RemovePrioritySignFromSegmentEnd_Prefix(object __instance, ushort segmentId, bool startNode)
        {
            if (segmentId >= 65024)
            {
                return false;
            }
            if (__instance != null)
            {
                FieldInfo f = __instance.GetType().GetField("PrioritySegments", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.GetValue(__instance) is Array arr && segmentId >= arr.Length)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool TrafficPriorityManager_HasSegmentPrioritySign_Prefix(object __instance, ushort segmentId, ref bool __result)
        {
            if (segmentId >= 65024)
            {
                __result = false;
                return false;
            }
            if (__instance != null)
            {
                FieldInfo f = __instance.GetType().GetField("PrioritySegments", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.GetValue(__instance) is Array arr && segmentId >= arr.Length)
                {
                    __result = false;
                    return false;
                }
            }
            return true;
        }

        public static bool TrafficPriorityManager_HasSegmentPrioritySign2_Prefix(object __instance, ushort segmentId, bool startNode, ref bool __result)
        {
            if (segmentId >= 65024)
            {
                __result = false;
                return false;
            }
            if (__instance != null)
            {
                FieldInfo f = __instance.GetType().GetField("PrioritySegments", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.GetValue(__instance) is Array arr && segmentId >= arr.Length)
                {
                    __result = false;
                    return false;
                }
            }
            return true;
        }

        public static bool RoadUI_OnLevelUnloading_Prefix(object __instance)
        {
            try
            {
                if (__instance != null)
                {
                    Type t = __instance.GetType();
                    FieldInfo f = t.GetField("VehicleInfoSignTextures", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null && f.GetValue(__instance) == null)
                    {
                        return false;
                    }
                }
            }
            catch { }
            return true;
        }

        public static bool PrioritySignsTool_RefreshCurrentPriorityNodeIds_Prefix(object __instance)
        {
            try
            {
                if (__instance == null) return false;
                Type t = __instance.GetType();
                FieldInfo fNodes = t.GetField("currentPriorityNodeIds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fNodes == null) return true;
                var set = fNodes.GetValue(__instance) as HashSet<ushort>;
                if (set == null) return true;
                set.Clear();

                Assembly tmpe = TMPECompatibility.GetTMPEAssembly();
                if (tmpe == null) return true;
                Type tpmType = tmpe.GetType("TrafficManager.Manager.Impl.TrafficPriorityManager");
                if (tpmType == null) return true;
                object tpmInst = TMPECompatibility.GetManagerInstance(tpmType);
                if (tpmInst == null) return true;

                MethodInfo mayHaveSigns = tpmType.GetMethod("MayNodeHavePrioritySigns", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(ushort) }, null);
                MethodInfo hasSigns = tpmType.GetMethod("HasNodePrioritySign", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(ushort) }, null);
                PropertyInfo pSelNode = t.GetProperty("SelectedNodeId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                ushort selectedNodeId = pSelNode != null ? (ushort)pSelNode.GetValue(__instance, null) : (ushort)0;

                int maxNode = 65024;
                if (NetManager.instance != null && NetManager.instance.m_nodes != null && NetManager.instance.m_nodes.m_buffer != null)
                    maxNode = Math.Min(maxNode, NetManager.instance.m_nodes.m_buffer.Length);

                for (int nodeId = 0; nodeId < maxNode; nodeId++)
                {
                    if (NetManager.instance != null && (NetManager.instance.m_nodes.m_buffer[nodeId].m_flags & NetNode.Flags.Created) != 0)
                    {
                        bool may = mayHaveSigns != null && (bool)mayHaveSigns.Invoke(tpmInst, new object[] { (ushort)nodeId });
                        if (may)
                        {
                            bool has = hasSigns != null && (bool)hasSigns.Invoke(tpmInst, new object[] { (ushort)nodeId });
                            if (has || (ushort)nodeId == selectedNodeId)
                            {
                                set.Add((ushort)nodeId);
                            }
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] PrioritySignsTool_RefreshCurrentPriorityNodeIds_Prefix error: " + ex);
                return true;
            }
        }

        public static bool JunctionRestrictionsTool_RefreshCurrentRestrictedNodeIds_Prefix(object __instance, ushort nodeId)
        {
            try
            {
                if (__instance == null) return false;
                Type t = __instance.GetType();
                FieldInfo fNodes = t.GetField("currentRestrictedNodeIds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fNodes == null) return true;
                var set = fNodes.GetValue(__instance) as HashSet<ushort>;
                if (set == null) return true;

                if (nodeId != 0)
                {
                    set.Remove(nodeId);
                }
                else
                {
                    set.Clear();
                }

                Assembly tmpe = TMPECompatibility.GetTMPEAssembly();
                if (tmpe == null) return true;
                Type jrmType = tmpe.GetType("TrafficManager.Manager.Impl.JunctionRestrictionsManager");
                if (jrmType == null) return true;
                object jrmInst = TMPECompatibility.GetManagerInstance(jrmType);
                if (jrmInst == null) return true;
                MethodInfo hasRestr = jrmType.GetMethod("HasJunctionRestrictions", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(ushort) }, null);
                if (hasRestr == null) return true;

                int minNode = (nodeId == 0 ? 1 : (int)nodeId);
                int maxNode = (nodeId == 0 ? 65024 : (int)nodeId + 1);
                if (NetManager.instance != null && NetManager.instance.m_nodes != null && NetManager.instance.m_nodes.m_buffer != null)
                    maxNode = Math.Min(maxNode, NetManager.instance.m_nodes.m_buffer.Length);

                for (int nid = minNode; nid < maxNode; nid++)
                {
                    if (NetManager.instance != null && (NetManager.instance.m_nodes.m_buffer[nid].m_flags & NetNode.Flags.Created) != 0)
                    {
                        if ((bool)hasRestr.Invoke(jrmInst, new object[] { (ushort)nid }))
                        {
                            set.Add((ushort)nid);
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] JunctionRestrictionsTool_RefreshCurrentRestrictedNodeIds_Prefix error: " + ex);
                return true;
            }
        }

        public static void SerializableDataExtension_Load_Prefix()
        {
            TMPECompatibility.SynchronizeTMPEBuffersAndArrays("SerializableDataExtension.Load.Prefix");
        }

        public static void SerializableDataExtension_Load_Postfix()
        {
            TMPECompatibility.SynchronizeTMPEBuffersAndArrays("SerializableDataExtension.Load.Postfix");
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

        public static void SerializableDataExtension_LoadDataState_Postfix(ref bool error)
        {
            if (error)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] Clearing TM:PE LoadDataState error flag to prevent modal popup.");
                error = false;
            }
        }

        public static void GenericManager_LoadData_Postfix(ref bool __result)
        {
            if (!__result)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] TM:PE Manager LoadData reported false. Overriding to true to prevent save load abortion.");
                __result = true;
            }
        }

        public static void CustomSegmentLightsManager_OnLevelUnloading_Postfix()
        {
            try
            {
                Assembly tmpe = TMPECompatibility.GetTMPEAssembly();
                if (tmpe != null)
                {
                    TMPECompatibility.EnsureCustomSegmentLightsCapacity(tmpe);
                }
            }
            catch { }
        }

        public static bool CustomSegmentLightsManager_EnsureCapacity_Prefix(ushort segmentId)
        {
            try
            {
                Assembly tmpe = TMPECompatibility.GetTMPEAssembly();
                if (tmpe != null)
                {
                    TMPECompatibility.EnsureCustomSegmentLightsCapacity(tmpe);
                }
            }
            catch { }
            return true;
        }

        public static bool CustomSegmentLightsManager_IsSegmentLight_Prefix(object __instance, ushort segmentId, ref bool __result)
        {
            try
            {
                if (__instance != null)
                {
                    FieldInfo f = __instance.GetType().GetField("customSegments_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null && f.GetValue(__instance) is Array arr)
                    {
                        if (segmentId >= arr.Length)
                        {
                            __result = false;
                            return false;
                        }
                    }
                }
            }
            catch { }
            return true;
        }

        public static bool CustomSegmentLightsManager_RemoveSegmentLights_Prefix(object __instance, ushort segmentId)
        {
            try
            {
                if (__instance != null)
                {
                    FieldInfo f = __instance.GetType().GetField("customSegments_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null && f.GetValue(__instance) is Array arr)
                    {
                        if (segmentId >= arr.Length)
                        {
                            return false;
                        }
                    }
                }
            }
            catch { }
            return true;
        }

        public static bool CustomSegmentLightsManager_RemoveSegmentLight_Prefix(object __instance, ushort segmentId)
        {
            try
            {
                if (__instance != null)
                {
                    FieldInfo f = __instance.GetType().GetField("customSegments_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null && f.GetValue(__instance) is Array arr)
                    {
                        if (segmentId >= arr.Length)
                        {
                            return false;
                        }
                    }
                }
            }
            catch { }
            return true;
        }

        public static bool VehicleRestrictionsManager_EnsureCapacity_Prefix()
        {
            try
            {
                Assembly tmpe = TMPECompatibility.GetTMPEAssembly();
                if (tmpe != null)
                {
                    TMPECompatibility.EnsureVehicleRestrictionsCapacity(tmpe);
                }
            }
            catch { }
            return true;
        }

        public static bool TrafficLightSimulationManager_EnsureCapacity_Prefix(ushort nodeId)
        {
            try
            {
                Assembly tmpe = TMPECompatibility.GetTMPEAssembly();
                if (tmpe != null)
                {
                    TMPECompatibility.EnsureTrafficLightSimulationCapacity(tmpe);
                    TMPECompatibility.EnsureCustomSegmentLightsCapacity(tmpe);
                }
            }
            catch { }
            return true;
        }

        public static bool TrafficLightSimulationManager_LoadData_Prefix(object __instance)
        {
            try
            {
                Assembly tmpe = TMPECompatibility.GetTMPEAssembly();
                if (tmpe != null)
                {
                    TMPECompatibility.EnsureTrafficLightSimulationCapacity(tmpe);
                    TMPECompatibility.EnsureCustomSegmentLightsCapacity(tmpe);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TrafficLightSimulationManager_LoadData_Prefix error: " + ex);
            }
            return true;
        }

        public static bool Flags_SetLaneAllowedVehicleTypes_Prefix(ushort segmentId)
        {
            try
            {
                Assembly tmpe = TMPECompatibility.GetTMPEAssembly();
                if (tmpe != null)
                {
                    Type flagsType = tmpe.GetType("TrafficManager.State.Flags");
                    if (flagsType != null)
                    {
                        FieldInfo f = flagsType.GetField("laneAllowedVehicleTypesArray", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (f != null && f.GetValue(null) is Array arr && arr.Length < 65024)
                        {
                            Array newArr = Array.CreateInstance(arr.GetType().GetElementType(), 65024);
                            Array.Copy(arr, newArr, arr.Length);
                            f.SetValue(null, newArr);
                        }
                    }
                }
            }
            catch { }
            return true;
        }

        public static void ParkingRestrictionsManager_OnBeforeLoadData_Postfix(object __instance)
        {
            TMPECompatibility.EnsureParkingRestrictionsCapacity(__instance, 65024);
        }

        public static bool ParkingRestrictionsManager_SetParkingAllowed_Prefix(object __instance, ushort segmentId, ref bool __result)
        {
            try
            {
                TMPECompatibility.EnsureParkingRestrictionsCapacity(__instance, Math.Max((int)segmentId + 1, 65024));
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
            __result = (int)segmentId + (startNode ? 0 : 65024);
            return false;
        }

        public static bool SegmentEndManager_GetIndex_ushort_Prefix(ushort segmentId, bool startNode, ref int __result)
        {
            __result = (int)segmentId + (startNode ? 0 : 65024);
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
            __result = (int)segmentId + (startNode ? 0 : 65024);
            return false;
        }

        public static bool TurnOnRedManager_GetIndex_ushort_Prefix(ushort segmentId, bool startNode, ref int __result)
        {
            __result = (int)segmentId + (startNode ? 0 : 65024);
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
                    if (NetManager.instance != null && NetManager.instance.m_nodes != null && NetManager.instance.m_nodes.m_buffer != null && i < NetManager.instance.m_nodes.m_buffer.Length)
                    {
                        if ((NetManager.instance.m_nodes.m_buffer[i].m_flags & NetNode.Flags.Created) == 0)
                            continue;
                    }

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
                Assembly tmpe = TMPECompatibility.GetTMPEAssembly();
                if (tmpe != null)
                {
                    TMPECompatibility.RecalculateLiveSegments(tmpe);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TMPELifecycle_Load_Postfix error: " + ex);
            }
        }

        public static bool SpeedLimitManager_GetGameSpeedLimit_Prefix(uint laneId, NetInfo.Lane laneInfo, ref float __result)
        {
            float speed = 0f;
            if (TMPECompatibility.s_cachedLaneSpeedLimits != null && laneId < (uint)TMPECompatibility.s_cachedLaneSpeedLimits.Length)
            {
                speed = TMPECompatibility.s_cachedLaneSpeedLimits[laneId];
            }
            if (speed <= 0.001f && laneInfo != null)
            {
                speed = laneInfo.m_speedLimit;
            }
            if (speed <= 0.001f)
            {
                speed = 1.25f;
            }
            __result = speed;
            return false;
        }

        public static bool SpeedLimitManager_GetGameSpeedLimit4_Prefix(ushort segmentId, byte laneIndex, uint laneId, NetInfo.Lane laneInfo, ref float __result)
        {
            float speed = 0f;
            if (TMPECompatibility.s_cachedLaneSpeedLimits != null && laneId < (uint)TMPECompatibility.s_cachedLaneSpeedLimits.Length)
            {
                speed = TMPECompatibility.s_cachedLaneSpeedLimits[laneId];
            }
            if (speed <= 0.001f && laneInfo != null)
            {
                speed = laneInfo.m_speedLimit;
            }
            if (speed <= 0.001f)
            {
                speed = 1.25f;
            }
            __result = speed;
            return false;
        }

        public static bool SpeedLimitManager_GetGameSpeedLimit1_Prefix(uint laneId, ref float __result)
        {
            float speed = 0f;
            if (TMPECompatibility.s_cachedLaneSpeedLimits != null && laneId < (uint)TMPECompatibility.s_cachedLaneSpeedLimits.Length)
            {
                speed = TMPECompatibility.s_cachedLaneSpeedLimits[laneId];
            }
            if (speed <= 0.001f)
            {
                speed = 1.25f;
            }
            __result = speed;
            return false;
        }


        public static void CustomPathManager_UpdateWithPathManagerValues_Postfix()
        {
            TMPECompatibility.EnsurePathFindCapacity();
        }

        public static void CustomPathFind_Awake_Postfix(object __instance)
        {
            try
            {
                if (__instance == null) return;
                NetManager nm = Singleton<NetManager>.instance;
                NetNode[] liveNodes = (nm != null && nm.m_nodes != null) ? nm.m_nodes.m_buffer : null;
                NetSegment[] liveSegs = (nm != null && nm.m_segments != null) ? nm.m_segments.m_buffer : null;
                NetLane[] liveLanes = (nm != null && nm.m_lanes != null) ? nm.m_lanes.m_buffer : null;
                TMPECompatibility.SynchronizeSinglePathFind(__instance, liveNodes, liveSegs, liveLanes);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] CustomPathFind_Awake_Postfix error: " + ex);
            }
        }

        public static bool CustomPathFind_PathFindImplementation_Prefix(object __instance, uint unit)
        {
            if (unit == 0 || unit >= 524288) return false;
            try
            {
                if (__instance != null)
                {
                    NetManager nm = Singleton<NetManager>.instance;
                    if (nm != null && nm.m_segments != null && nm.m_nodes != null && nm.m_lanes != null)
                    {
                        Type t = __instance.GetType();
                        FieldInfo fs = t.GetField("segmentBuffer_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (fs != null && fs.GetValue(__instance) == null)
                        {
                            fs.SetValue(__instance, nm.m_segments.m_buffer);
                        }
                        FieldInfo fn = t.GetField("nodeBuffer_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (fn != null && fn.GetValue(__instance) == null)
                        {
                            fn.SetValue(__instance, nm.m_nodes.m_buffer);
                        }
                    }
                }
            }
            catch { }
            return true;
        }

        public static Exception CustomPathFind_PathFindImplementation_Finalizer(Exception __exception)
        {
            if (__exception != null)
            {
                // Silently absorb any pathfinding crash so the pathfind worker thread doesn't die and UIView.ForwardException modal isn't triggered
                return null;
            }
            return null;
        }

        public static bool CustomPathFind_GetLaneDirection_Prefix(
            PathUnit.Position pathPos,
            ref NetInfo.Direction direction,
            ref NetInfo.LaneType laneType,
            ref VehicleInfo.VehicleType vehicleType)
        {
            ushort segmentId = pathPos.m_segment;
            if (segmentId == 0 || segmentId >= 65024)
            {
                direction = NetInfo.Direction.None;
                laneType = NetInfo.LaneType.None;
                vehicleType = VehicleInfo.VehicleType.None;
                return false;
            }
            if (NetManager.instance == null || NetManager.instance.m_segments == null || NetManager.instance.m_segments.m_buffer == null || segmentId >= NetManager.instance.m_segments.m_buffer.Length)
            {
                direction = NetInfo.Direction.None;
                laneType = NetInfo.LaneType.None;
                vehicleType = VehicleInfo.VehicleType.None;
                return false;
            }

            NetSegment[] segments = NetManager.instance.m_segments.m_buffer;
            if ((segments[segmentId].m_flags & NetSegment.Flags.Created) == 0)
            {
                direction = NetInfo.Direction.None;
                laneType = NetInfo.LaneType.None;
                vehicleType = VehicleInfo.VehicleType.None;
                return false;
            }

            NetInfo info = segments[segmentId].Info;
            if (info == null || info.m_lanes == null || pathPos.m_lane >= info.m_lanes.Length)
            {
                direction = NetInfo.Direction.None;
                laneType = NetInfo.LaneType.None;
                vehicleType = VehicleInfo.VehicleType.None;
                return false;
            }

            NetInfo.Lane lane = info.m_lanes[pathPos.m_lane];
            if (lane == null)
            {
                direction = NetInfo.Direction.None;
                laneType = NetInfo.LaneType.None;
                vehicleType = VehicleInfo.VehicleType.None;
                return false;
            }

            direction = lane.m_finalDirection;
            laneType = lane.m_laneType;
            vehicleType = lane.m_vehicleType;

            if ((segments[segmentId].m_flags & NetSegment.Flags.Invert) != 0)
            {
                direction = NetInfo.InvertDirection(direction);
            }
            return false;
        }

        public static bool CustomPathFind_ProcessItemMain_Prefix(ref NetSegment prevSegment)
        {
            if (prevSegment.Info == null || prevSegment.Info.m_lanes == null)
            {
                return false;
            }
            return true;
        }

        public static bool VehicleAICommons_CustomCalculateTargetSpeed_Prefix(
            NetInfo info,
            uint laneId,
            ref float maxSpeed)
        {
            if (info == null || laneId == 0 || laneId >= 262144)
            {
                maxSpeed = 1f;
                return false;
            }
            return true;
        }

        public static bool TrafficMeasurementManager_AddTraffic_Prefix(object __instance, ushort segmentId)
        {
            if (segmentId >= 65024) return false;
            if (__instance != null)
            {
                FieldInfo f = __instance.GetType().GetField("<LaneTrafficData>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                           ?? __instance.GetType().GetField("LaneTrafficData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.GetValue(__instance) is Array arr && segmentId >= arr.Length)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool TrafficMeasurementManager_OnBeforeSimulationStep_Prefix(object __instance, ushort segmentId)
        {
            if (segmentId >= 65024) return false;
            if (__instance != null)
            {
                FieldInfo f = __instance.GetType().GetField("<LaneTrafficData>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                           ?? __instance.GetType().GetField("LaneTrafficData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.GetValue(__instance) is Array arr && segmentId >= arr.Length)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool TrafficMeasurementManager_DestroySegmentStats_Prefix(object __instance, ushort segmentId)
        {
            if (segmentId >= 65024) return false;
            if (__instance != null)
            {
                FieldInfo f = __instance.GetType().GetField("<LaneTrafficData>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                           ?? __instance.GetType().GetField("LaneTrafficData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.GetValue(__instance) is Array arr && segmentId >= arr.Length)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool TrafficPriorityManager_GetPrioritySign_Prefix(object __instance, ushort segmentId, bool startNode)
        {
            if (segmentId >= 65024)
            {
                return false;
            }
            if (__instance != null)
            {
                FieldInfo f = __instance.GetType().GetField("PrioritySegments", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.GetValue(__instance) is Array arr && segmentId >= arr.Length)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool JunctionRestrictionsManager_HasJunctionRestrictions_Prefix(object __instance, ushort nodeId, ref bool __result)
        {
            if (nodeId >= 65024)
            {
                __result = false;
                return false;
            }
            return true;
        }

        public static bool JunctionRestrictionsManager_IsEnteringBlockedJunctionAllowed_Prefix(object __instance, ushort segmentId, bool startNode, ref bool __result)
        {
            if (segmentId >= 65024)
            {
                __result = false;
                return false;
            }
            if (__instance != null)
            {
                FieldInfo f = __instance.GetType().GetField("segmentRestrictions", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.GetValue(__instance) is Array arr && segmentId >= arr.Length)
                {
                    __result = false;
                    return false;
                }
            }
            return true;
        }

        public static bool JunctionRestrictionsManager_IsPedestrianCrossingAllowed_Prefix(object __instance, ushort segmentId, bool startNode, ref bool __result)
        {
            if (segmentId >= 65024)
            {
                __result = true;
                return false;
            }
            if (__instance != null)
            {
                FieldInfo f = __instance.GetType().GetField("segmentRestrictions", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.GetValue(__instance) is Array arr && segmentId >= arr.Length)
                {
                    __result = true;
                    return false;
                }
            }
            return true;
        }

        public static bool JunctionRestrictionsManager_IsLaneChangingAllowedWhenGoingStraight_Prefix(object __instance, ushort segmentId, bool startNode, ref bool __result)
        {
            if (segmentId >= 65024)
            {
                __result = false;
                return false;
            }
            if (__instance != null)
            {
                FieldInfo f = __instance.GetType().GetField("segmentRestrictions", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.GetValue(__instance) is Array arr && segmentId >= arr.Length)
                {
                    __result = false;
                    return false;
                }
            }
            return true;
        }

        public static bool JunctionRestrictionsManager_IsUturnAllowed_Prefix(object __instance, ushort segmentId, bool startNode, ref bool __result)
        {
            if (segmentId >= 65024)
            {
                __result = false;
                return false;
            }
            if (__instance != null)
            {
                FieldInfo f = __instance.GetType().GetField("segmentRestrictions", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.GetValue(__instance) is Array arr && segmentId >= arr.Length)
                {
                    __result = false;
                    return false;
                }
            }
            return true;
        }

        public static bool ThreadingExtension_OnBeforeSimulationFrame_Prefix()
        {
            try
            {
                Assembly tmpe = TMPECompatibility.GetTMPEAssembly();
                if (tmpe == null) return false;

                Type sgoType = tmpe.GetType("TrafficManager.State.SavedGameOptions");
                if (sgoType != null)
                {
                    PropertyInfo pOpt = sgoType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    object opt = pOpt != null ? pOpt.GetValue(null, null) : null;
                    if (opt == null) return false;

                    FieldInfo fTimed = sgoType.GetField("timedLightsEnabled", BindingFlags.Public | BindingFlags.Instance);
                    if (fTimed != null && !(bool)fTimed.GetValue(opt))
                    {
                        return false;
                    }
                }

                Type tlsm = tmpe.GetType("TrafficManager.Manager.Impl.TrafficLightSimulationManager");
                if (tlsm != null)
                {
                    FieldInfo fInst = tlsm.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    PropertyInfo pInst = tlsm.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    object inst = fInst != null ? fInst.GetValue(null) : (pInst != null ? pInst.GetValue(null, null) : null);
                    if (inst == null)
                    {
                        return false;
                    }

                    try
                    {
                        MethodInfo simStep = tlsm.GetMethod("SimulationStep", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        if (simStep != null)
                        {
                            simStep.Invoke(inst, null);
                        }
                    }
                    catch { }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    // ============================================================================
    // NETMANAGER SIMULATION STEP PATCH (VANILLA SIMULATION FOR EXPANDED NODES & SEGMENTS)
    // ============================================================================
    public static class NetManagerSimulationPatches
    {
        private static bool loggedOnce = false;
        private static uint lastSimulatedFrameIndex = uint.MaxValue;

        public static void Postfix(NetManager __instance, int subStep)
        {
            if (__instance == null) return;
            if (subStep == 0) return;

            SimulationManager sm = Singleton<SimulationManager>.instance;
            if (sm == null) return;

            // Only simulate once per frame index to avoid wasteful duplicate simulation across substeps
            uint currentFrame = sm.m_currentFrameIndex;
            if (currentFrame == lastSimulatedFrameIndex) return;
            lastSimulatedFrameIndex = currentFrame;

            int frameIndex = (int)(currentFrame & 255u);

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
                        }
                    }
                }

                // Exactly like vanilla: only update segment colors when InfoManager mode != None or wetness changed
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
            if (__instance.m_segments.m_buffer.Length > 36864)
            {
                __instance.UpdateSegmentColors(36864, (ushort)(__instance.m_segments.m_buffer.Length - 1));
            }
        }
    }

    [HarmonyPatch(typeof(NetManager), "UpdateNodeColors", new Type[0])]
    public static class NetManagerUpdateNodeColorsZeroPatch
    {
        public static void Postfix(NetManager __instance)
        {
            if (__instance == null || __instance.m_nodes.m_buffer == null) return;
            if (__instance.m_nodes.m_buffer.Length > 32768)
            {
                __instance.UpdateNodeColors(32768, (ushort)(__instance.m_nodes.m_buffer.Length - 1));
            }
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

    [HarmonyPatch(typeof(SavePanel), "OnSavingComplete", new Type[] { typeof(bool), typeof(bool) })]
    public static class SavePanelOnSavingCompletePatch
    {
        private static bool s_isApplied = false;

        public static void ApplySavePanelPatch()
        {
            if (s_isApplied) return;
            try
            {
                var harmony = HarmonyInstance.Create("MoreNetworkCapacityRenew.SavePanelPatch");
                MethodInfo mTarget = typeof(SavePanel).GetMethod("OnSavingComplete", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(bool), typeof(bool) }, null);
                if (mTarget != null)
                {
                    var pre = typeof(SavePanelOnSavingCompletePatch).GetMethod(nameof(Prefix), BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(mTarget, prefix: new HarmonyMethod(pre));
                    s_isApplied = true;
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Explicitly applied SavePanel.OnSavingComplete safety patch!");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] ApplySavePanelPatch error: " + ex);
            }
        }

        public static bool Prefix(SavePanel __instance, bool quitAfterSaving, bool errorOnSaving)
        {
            try
            {
                FieldInfo fIsSaving = typeof(SavePanel).GetField("m_IsSaving", BindingFlags.NonPublic | BindingFlags.Static);
                if (fIsSaving != null) fIsSaving.SetValue(null, false);
                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] SavePanel.OnSavingComplete: errorOnSaving={0}, quitAfterSaving={1}", errorOnSaving, quitAfterSaving));
                if (errorOnSaving)
                {
                    UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] SavePanel reported errorOnSaving = true.");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SavePanelOnSavingComplete error: " + ex);
            }
            return true;
        }
    }

    // ============================================================================
    // MAIN LOADING EXTENSION
    // ============================================================================
    public class RenewLoading : LoadingExtensionBase
    {
        private static bool patchApplied = false;

        public static int SafePatchKianCommons(HarmonyInstance harmony, Type type, string methodName, string transpilerName)
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
                        if (parameters.Length == 1 && (parameters[0].ParameterType == typeof(ushort) || parameters[0].ParameterType == typeof(uint) || parameters[0].ParameterType == typeof(int)))
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
            SavePanelOnSavingCompletePatch.ApplySavePanelPatch();
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
                    // Dynamic hooks for third-party mods (Adaptive Roads, Network Skins, Node Controller, Move It, etc.)
                    ThirdPartyCompatibility.Apply(harmony);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] ThirdPartyCompatibility error: " + ex);
                }

                try
                {
                    // Transport Line & Public Transport safety hooks (IPT, GetNextStop, AfterDeserialize)
                    TransportLineCompatibility.Apply(harmony);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TransportLineCompatibility error: " + ex);
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
                            patchedCount += SafePatchKianCommons(harmony, netUtilType, "IsLaneValid", nameof(Transpilers.LaneLimitTranspiler));
                            patchedCount += SafePatchKianCommons(harmony, netUtilType, "AssertLaneValid", nameof(Transpilers.LaneLimitTranspiler));
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
                ThirdPartyCompatibility.SynchronizeThirdPartyMods("OnCreated");
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
                ThirdPartyCompatibility.SynchronizeThirdPartyMods("OnLevelLoaded");
                TMPECompatibility.EnsurePathFindCapacity();
                Assembly loadedTmpe = TMPECompatibility.GetTMPEAssembly();
                if (loadedTmpe != null)
                {
                    TMPECompatibility.RecalculateLiveSegments(loadedTmpe);
                }

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
                
                // Trigger full TM:PE routing recalculation for all segments to unfreeze pathfinding across the entire city
                try
                {
                    Assembly tmpeAsm = TMPECompatibility.GetTMPEAssembly();
                    if (tmpeAsm != null)
                    {
                        Type rmT = tmpeAsm.GetType("TrafficManager.Manager.Impl.RoutingManager");
                        if (rmT != null)
                        {
                            object rmInst = TMPECompatibility.GetManagerInstance(rmT);
                            if (rmInst != null)
                            {
                                MethodInfo reqFull = rmT.GetMethod("RequestFullRecalculation", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (reqFull != null) reqFull.Invoke(rmInst, new object[] { true });
                                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Requested full TM:PE routing recalculation on level load.");
                            }
                        }
                    }
                }
                catch { }

                // Report missing asset stats
                MissingAssetProtection.ReportFinalStats();

                // Initialize vehicle & transport repair runner (accessible via Ctrl+Shift+R / Ctrl+Shift+V hotkeys)
                VehicleAndTransportRepair.Initialize();

                // Automatically restore valid lines, purge ghost lines & unstick stranded vehicles
                try
                {
                    int restored = VehicleAndTransportRepair.RestoreAllTransportLines(out int deactivated);
                    if (deactivated > 0)
                    {
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Purged {0} corrupted ghost lines (restored {1} active lines); wiping stranded vehicles.", deactivated, restored));
                        VehicleAndTransportRepair.ClearAllVehicles();
                    }
                    else
                    {
                        VehicleAndTransportRepair.UnstickCitizens();
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Initial clear/restore error: " + ex);
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

        public static IEnumerable<CodeInstruction> LaneLimitTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int val && val == 262144)
                {
                    instruction.operand = 524288;
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

                try
                {
                    string[] targetModNames = new string[] {
                        "AdaptiveRoads",
                        "NodeControllerRenewal",
                        "NodeController",
                        "MoveIt",
                        "Picker",
                        "NetworkMultitool",
                        "IntersectionMarkingTool",
                        "ImprovedPublicTransport3",
                        "IPTEssentials",
                        "ImprovedPublicTransport2",
                        "ParallelRoadTool",
                        "AdvancedVehicleOptions",
                        "TrainCarSelector",
                        "NetworkAnarchy",
                        "AdvancedRoadTools",
                        "NetworkExtensions2",
                        "MoreTransportLines"
                    };

                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        string asmName = asm.GetName().Name;
                        if (Array.IndexOf(targetModNames, asmName) >= 0)
                        {
                            PatchAssembly(harmony, asm);
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] 3rd-party mod dynamic patch error: " + ex);
                }

                DiagnosticLogger.Checkpoint("DynamicPatcher.Complete");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] DynamicPatcher error: " + ex);
            }
        }

        public static void PatchAssembly(HarmonyInstance harmony, Assembly asm)
        {
            if (harmony == null || asm == null) return;
            try
            {
                // Verify all referenced assemblies are resolvable to prevent Mono SIGABRT
                try
                {
                    var refs = asm.GetReferencedAssemblies();
                    if (refs != null)
                    {
                        foreach (var r in refs)
                        {
                            try { Assembly.Load(r); }
                            catch
                            {
                                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Skipping dynamic patching for {0} because dependency {1} cannot be resolved.", asm.GetName().Name, r.Name));
                                return;
                            }
                        }
                    }
                }
                catch { return; }

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types != null ? Array.FindAll(ex.Types, t => t != null) : new Type[0];
                }
                catch
                {
                    types = new Type[0];
                }

                int modMethodCount = 0;
                int modConstCount = 0;
                foreach (Type type in types)
                {
                    int result = PatchType(harmony, type);
                    if (result > 0) modMethodCount++;
                    modConstCount += result;
                }
                if (modConstCount > 0)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Dynamically patched {0} constants in {1} methods of {2}", modConstCount, modMethodCount, asm.GetName().Name));
                }
            }
            catch { }
        }

        public static int PatchType(HarmonyInstance harmony, Type type)
        {
            if (type == null || type.IsInterface || type.IsGenericTypeDefinition || type.IsGenericType || type.IsEnum)
                return 0;

            if (type.Name.Contains("PathFind") || type.Name.Contains("CustomPathFind"))
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

                        try
                        {
                            Type ret = method.ReturnType;
                            var parms = method.GetParameters();
                        }
                        catch { continue; }

                        var body = method.GetMethodBody();
                        if (body == null) continue;
                        byte[] il = body.GetILAsByteArray();
                        if (il == null) continue;

                        bool hasConstant = false;
                        for (int i = 0; i < il.Length - 4; i++)
                        {
                            if (il[i] == 0x20) // ldc.i4
                            {
                                int val = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
                                if (val == 36864 || val == 32768 || val == 36863 || val == 32767 || val == 73728 || val == 73727 || val == 262144 || val == 262143)
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

                        bool hasConstant = false;
                        for (int i = 0; i < il.Length - 4; i++)
                        {
                            if (il[i] == 0x20) // ldc.i4
                            {
                                int val = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
                                if (val == 36864 || val == 32768 || val == 36863 || val == 32767 || val == 73728 || val == 73727 || val == 262144 || val == 262143)
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
                    if (val == 36864 || val == 32768 || val == 36863 || val == 32767 || val == 73728 || val == 73727 || val == 262144 || val == 262143)
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

    // ============================================================================
    // THIRD-PARTY MOD COMPATIBILITY ENGINE
    // ============================================================================
    public static class ThirdPartyCompatibility
    {
        private static bool _domainHooked = false;
        private static bool _adaptiveRoadsPatched = false;
        private static bool _networkSkinsPatched = false;
        private static bool _nodeControllerPatched = false;
        private static bool _toolControllerPatched = false;
        private static HarmonyInstance _harmonyInstance = null;

        public static void Apply(HarmonyInstance harmony)
        {
            _harmonyInstance = harmony;
            DiagnosticLogger.Checkpoint("ThirdPartyCompatibility.Apply.Start");
            try
            {
                PatchToolController(harmony);
                PatchThirdPartyMods(harmony);
                SynchronizeThirdPartyMods("Apply");

                if (!_domainHooked)
                {
                    AppDomain.CurrentDomain.AssemblyLoad += delegate(object sender, AssemblyLoadEventArgs args)
                    {
                        try
                        {
                            if (args != null && args.LoadedAssembly != null)
                            {
                                string name = args.LoadedAssembly.GetName().Name;
                                if (IsTargetModAssembly(name))
                                {
                                    SynchronizeThirdPartyMods("AssemblyLoad: " + name);
                                    if (_harmonyInstance != null)
                                    {
                                        PatchThirdPartyMods(_harmonyInstance);
                                        DynamicPatcher.PatchAssembly(_harmonyInstance, args.LoadedAssembly);
                                        if (name == "AdaptiveRoads")
                                        {
                                            Type netUtilType = args.LoadedAssembly.GetType("KianCommons.NetUtil");
                                            if (netUtilType != null)
                                            {
                                                RenewLoading.SafePatchKianCommons(_harmonyInstance, netUtilType, "IsNodeValid", nameof(Transpilers.NodeLimitTranspiler));
                                                RenewLoading.SafePatchKianCommons(_harmonyInstance, netUtilType, "IsSegmentValid", nameof(Transpilers.SegmentLimitTranspiler));
                                                RenewLoading.SafePatchKianCommons(_harmonyInstance, netUtilType, "AssertSegmentValid", nameof(Transpilers.SegmentLimitTranspiler));
                                                RenewLoading.SafePatchKianCommons(_harmonyInstance, netUtilType, "IsLaneValid", nameof(Transpilers.LaneLimitTranspiler));
                                                RenewLoading.SafePatchKianCommons(_harmonyInstance, netUtilType, "AssertLaneValid", nameof(Transpilers.LaneLimitTranspiler));
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    };
                    _domainHooked = true;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] ThirdPartyCompatibility.Apply error: " + ex);
            }
            DiagnosticLogger.Checkpoint("ThirdPartyCompatibility.Apply.Complete");
        }

        private static bool IsTargetModAssembly(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name == "AdaptiveRoads" ||
                   name == "NetworkSkins" ||
                   name == "NodeControllerRenewal" ||
                   name == "NodeController" ||
                   name == "MoveIt" ||
                   name == "Picker" ||
                   name == "NetworkMultitool" ||
                   name == "IntersectionMarkingTool" ||
                   name == "ImprovedPublicTransport3" ||
                   name == "IPTEssentials" ||
                   name == "ImprovedPublicTransport2" ||
                   name == "ParallelRoadTool" ||
                   name == "AdvancedVehicleOptions" ||
                   name == "TrainCarSelector" ||
                   name == "NetworkAnarchy" ||
                   name == "AdvancedRoadTools" ||
                   name == "NetworkExtensions2" ||
                   name == "MoreTransportLines";
        }

        public static void SynchronizeThirdPartyMods(string context)
        {
            try
            {
                EnsureToolControllerBuffers();
                EnsureAdaptiveRoadsBuffers(context);
                EnsureNetworkSkinsBuffers(context);
                EnsureNodeControllerBuffers(context);
                EnsureMoveItBuffers(context);
                EnsureKianCommonsBuffers(context);
                EnsureImprovedPublicTransportBuffers(context);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SynchronizeThirdPartyMods error: " + ex);
            }
        }

        private static void PatchToolController(HarmonyInstance harmony)
        {
            if (_toolControllerPatched || harmony == null) return;
            try
            {
                MethodInfo awake = typeof(ToolController).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
                if (awake != null)
                {
                    MethodInfo postfix = typeof(ThirdPartyCompatibility).GetMethod(nameof(ToolController_Awake_Postfix), BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(awake, postfix: new HarmonyMethod(postfix));
                    _toolControllerPatched = true;
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Successfully patched ToolController.Awake for 65K collision limits!");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] PatchToolController error: " + ex);
            }
        }

        public static void ToolController_Awake_Postfix(ToolController __instance)
        {
            if (__instance == null) return;
            try
            {
                int req = 1024;
                FieldInfo f1 = typeof(ToolController).GetField("m_collidingSegments1", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo f2 = typeof(ToolController).GetField("m_collidingSegments2", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f1 != null) f1.SetValue(__instance, new ulong[req]);
                if (f2 != null) f2.SetValue(__instance, new ulong[req]);

                FieldInfo b1 = typeof(ToolController).GetField("m_collidingBuildings1", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo b2 = typeof(ToolController).GetField("m_collidingBuildings2", BindingFlags.NonPublic | BindingFlags.Instance);
                if (b1 != null) b1.SetValue(__instance, new ulong[req]);
                if (b2 != null) b2.SetValue(__instance, new ulong[req]);
            }
            catch { }
        }

        public static void EnsureToolControllerBuffers()
        {
            try
            {
                var tm = Singleton<ToolManager>.instance;
                if (tm == null) return;
                var tc = tm.m_properties;
                if (tc == null) return;

                int req = 1024;
                FieldInfo f1 = typeof(ToolController).GetField("m_collidingSegments1", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo f2 = typeof(ToolController).GetField("m_collidingSegments2", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f1 != null && (f1.GetValue(tc) is ulong[] a1 && a1.Length < req)) f1.SetValue(tc, new ulong[req]);
                if (f2 != null && (f2.GetValue(tc) is ulong[] a2 && a2.Length < req)) f2.SetValue(tc, new ulong[req]);

                FieldInfo b1 = typeof(ToolController).GetField("m_collidingBuildings1", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo b2 = typeof(ToolController).GetField("m_collidingBuildings2", BindingFlags.NonPublic | BindingFlags.Instance);
                if (b1 != null && (b1.GetValue(tc) is ulong[] ba1 && ba1.Length < req)) b1.SetValue(tc, new ulong[req]);
                if (b2 != null && (b2.GetValue(tc) is ulong[] ba2 && ba2.Length < req)) b2.SetValue(tc, new ulong[req]);
            }
            catch { }
        }

        private static Action<Array, int, int> CreateFastInit16(Type structType, MethodInfo initMethod)
        {
            try
            {
                DynamicMethod dm = new DynamicMethod("FastInit16_" + structType.Name, typeof(void),
                    new Type[] { typeof(Array), typeof(int), typeof(int) }, typeof(ThirdPartyCompatibility).Module, true);
                ILGenerator il = dm.GetILGenerator();

                LocalBuilder locI = il.DeclareLocal(typeof(int));
                Label loopStart = il.DefineLabel();
                Label loopCheck = il.DefineLabel();

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stloc, locI);
                il.Emit(OpCodes.Br, loopCheck);

                il.MarkLabel(loopStart);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, structType.MakeArrayType());
                il.Emit(OpCodes.Ldloc, locI);
                il.Emit(OpCodes.Ldelema, structType);

                il.Emit(OpCodes.Ldloc, locI);
                il.Emit(OpCodes.Conv_U2);
                il.Emit(OpCodes.Call, initMethod);

                il.Emit(OpCodes.Ldloc, locI);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Stloc, locI);

                il.MarkLabel(loopCheck);
                il.Emit(OpCodes.Ldloc, locI);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Blt, loopStart);

                il.Emit(OpCodes.Ret);
                return (Action<Array, int, int>)dm.CreateDelegate(typeof(Action<Array, int, int>));
            }
            catch
            {
                return null;
            }
        }

        private static Action<Array, int, int> CreateFastInit32(Type structType, MethodInfo initMethod)
        {
            try
            {
                DynamicMethod dm = new DynamicMethod("FastInit32_" + structType.Name, typeof(void),
                    new Type[] { typeof(Array), typeof(int), typeof(int) }, typeof(ThirdPartyCompatibility).Module, true);
                ILGenerator il = dm.GetILGenerator();

                LocalBuilder locI = il.DeclareLocal(typeof(int));
                Label loopStart = il.DefineLabel();
                Label loopCheck = il.DefineLabel();

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stloc, locI);
                il.Emit(OpCodes.Br, loopCheck);

                il.MarkLabel(loopStart);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, structType.MakeArrayType());
                il.Emit(OpCodes.Ldloc, locI);
                il.Emit(OpCodes.Ldelema, structType);

                il.Emit(OpCodes.Ldloc, locI);
                il.Emit(OpCodes.Conv_U4);
                il.Emit(OpCodes.Call, initMethod);

                il.Emit(OpCodes.Ldloc, locI);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Stloc, locI);

                il.MarkLabel(loopCheck);
                il.Emit(OpCodes.Ldloc, locI);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Blt, loopStart);

                il.Emit(OpCodes.Ret);
                return (Action<Array, int, int>)dm.CreateDelegate(typeof(Action<Array, int, int>));
            }
            catch
            {
                return null;
            }
        }

        private static Action<Array, int, int> CreateFastInitSegmentEnd(Type structType, MethodInfo initMethod)
        {
            try
            {
                DynamicMethod dm = new DynamicMethod("FastInitSegEnd_" + structType.Name, typeof(void),
                    new Type[] { typeof(Array), typeof(int), typeof(int) }, typeof(ThirdPartyCompatibility).Module, true);
                ILGenerator il = dm.GetILGenerator();

                LocalBuilder locI = il.DeclareLocal(typeof(int));
                Label loopStart = il.DefineLabel();
                Label loopCheck = il.DefineLabel();

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stloc, locI);
                il.Emit(OpCodes.Br, loopCheck);

                il.MarkLabel(loopStart);

                // Start node: arr[i*2].Init((ushort)i, true)
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, structType.MakeArrayType());
                il.Emit(OpCodes.Ldloc, locI);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Shl);
                il.Emit(OpCodes.Ldelema, structType);

                il.Emit(OpCodes.Ldloc, locI);
                il.Emit(OpCodes.Conv_U2);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Call, initMethod);

                // End node: arr[i*2+1].Init((ushort)i, false)
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, structType.MakeArrayType());
                il.Emit(OpCodes.Ldloc, locI);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Shl);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Ldelema, structType);

                il.Emit(OpCodes.Ldloc, locI);
                il.Emit(OpCodes.Conv_U2);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Call, initMethod);

                // locI++
                il.Emit(OpCodes.Ldloc, locI);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Stloc, locI);

                il.MarkLabel(loopCheck);
                il.Emit(OpCodes.Ldloc, locI);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Blt, loopStart);

                il.Emit(OpCodes.Ret);
                return (Action<Array, int, int>)dm.CreateDelegate(typeof(Action<Array, int, int>));
            }
            catch
            {
                return null;
            }
        }

        private static bool _arBufferGuard = false;

        public static void EnsureAdaptiveRoadsBuffers(string context, object nem = null)
        {
            if (_arBufferGuard) return;
            _arBufferGuard = true;
            try
            {
                Type nemType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "AdaptiveRoads")
                    {
                        nemType = asm.GetType("AdaptiveRoads.Manager.NetworkExtensionManager");
                        break;
                    }
                }

                if (nemType == null) return;

                if (nem == null)
                {
                    FieldInfo instField = nemType.GetField("instance_", BindingFlags.NonPublic | BindingFlags.Static);
                    nem = instField != null ? instField.GetValue(null) : null;
                }
                if (nem == null)
                {
                    PropertyInfo rawProp = nemType.GetProperty("RawInstance", BindingFlags.Public | BindingFlags.Static);
                    if (rawProp != null) nem = rawProp.GetValue(null, null);
                }

                // Never call get_Instance here, as it recursively creates GameObject and triggers Awake/InitBuffers
                if (nem == null) return;

                bool resizedAny = false;

                // 1. NodeBuffer (NetNodeExt[65024])
                FieldInfo nf = nemType.GetField("NodeBuffer", BindingFlags.Public | BindingFlags.Instance);
                if (nf != null && nf.GetValue(nem) is Array nArr && nArr.Length < 65024)
                {
                    int oldLen = nArr.Length;
                    Type nType = nArr.GetType().GetElementType();
                    Array newArr = Array.CreateInstance(nType, 65024);
                    Array.Copy(nArr, newArr, oldLen);

                    MethodInfo initM = nType.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(ushort) }, null);
                    if (initM != null)
                    {
                        var fastInit = CreateFastInit16(nType, initM);
                        if (fastInit != null)
                        {
                            fastInit(newArr, oldLen, 65024);
                        }
                        else
                        {
                            for (int i = oldLen; i < 65024; i++)
                            {
                                object item = Activator.CreateInstance(nType);
                                initM.Invoke(item, new object[] { (ushort)i });
                                newArr.SetValue(item, i);
                            }
                        }
                    }
                    nf.SetValue(nem, newArr);
                    resizedAny = true;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized AdaptiveRoads NodeBuffer from {0} to 65024 ({1})", oldLen, context));
                }

                // 2. SegmentBuffer (NetSegmentExt[65024])
                FieldInfo sf = nemType.GetField("SegmentBuffer", BindingFlags.Public | BindingFlags.Instance);
                if (sf != null && sf.GetValue(nem) is Array sArr && sArr.Length < 65024)
                {
                    int oldLen = sArr.Length;
                    Type sType = sArr.GetType().GetElementType();
                    Array newArr = Array.CreateInstance(sType, 65024);
                    Array.Copy(sArr, newArr, oldLen);

                    MethodInfo initM = sType.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(ushort) }, null);
                    if (initM != null)
                    {
                        var fastInit = CreateFastInit16(sType, initM);
                        if (fastInit != null)
                        {
                            fastInit(newArr, oldLen, 65024);
                        }
                        else
                        {
                            for (int i = oldLen; i < 65024; i++)
                            {
                                object item = Activator.CreateInstance(sType);
                                initM.Invoke(item, new object[] { (ushort)i });
                                newArr.SetValue(item, i);
                            }
                        }
                    }
                    sf.SetValue(nem, newArr);
                    resizedAny = true;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized AdaptiveRoads SegmentBuffer from {0} to 65024 ({1})", oldLen, context));
                }

                // 3. SegmentEndBuffer (NetSegmentEnd[130048])
                FieldInfo sef = nemType.GetField("SegmentEndBuffer", BindingFlags.Public | BindingFlags.Instance);
                if (sef != null && sef.GetValue(nem) is Array seArr && seArr.Length < 130048)
                {
                    int oldLen = seArr.Length;
                    Type seType = seArr.GetType().GetElementType();
                    Array newArr = Array.CreateInstance(seType, 130048);
                    Array.Copy(seArr, newArr, oldLen);

                    MethodInfo initM = seType.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(ushort), typeof(bool) }, null);
                    if (initM != null)
                    {
                        var fastInit = CreateFastInitSegmentEnd(seType, initM);
                        if (fastInit != null)
                        {
                            fastInit(newArr, oldLen / 2, 65024);
                        }
                        else
                        {
                            for (int i = oldLen / 2; i < 65024; i++)
                            {
                                object startItem = Activator.CreateInstance(seType);
                                initM.Invoke(startItem, new object[] { (ushort)i, true });
                                newArr.SetValue(startItem, i * 2);

                                object endItem = Activator.CreateInstance(seType);
                                initM.Invoke(endItem, new object[] { (ushort)i, false });
                                newArr.SetValue(endItem, i * 2 + 1);
                            }
                        }
                    }
                    sef.SetValue(nem, newArr);
                    resizedAny = true;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized AdaptiveRoads SegmentEndBuffer from {0} to 130048 ({1})", oldLen, context));
                }

                // 4. LaneBuffer (NetLaneExt[524288])
                FieldInfo lf = nemType.GetField("LaneBuffer", BindingFlags.Public | BindingFlags.Instance);
                if (lf != null && lf.GetValue(nem) is Array lArr && lArr.Length < 524288)
                {
                    int oldLen = lArr.Length;
                    Type lType = lArr.GetType().GetElementType();
                    Array newArr = Array.CreateInstance(lType, 524288);
                    Array.Copy(lArr, newArr, oldLen);

                    MethodInfo initM = lType.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(uint) }, null);
                    if (initM != null)
                    {
                        var fastInit = CreateFastInit32(lType, initM);
                        if (fastInit != null)
                        {
                            fastInit(newArr, oldLen, 524288);
                        }
                        else
                        {
                            for (int i = oldLen; i < 524288; i++)
                            {
                                object item = Activator.CreateInstance(lType);
                                initM.Invoke(item, new object[] { (uint)i });
                                newArr.SetValue(item, i);
                            }
                        }
                    }
                    lf.SetValue(nem, newArr);
                    resizedAny = true;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized AdaptiveRoads LaneBuffer from {0} to 524288 ({1})", oldLen, context));
                }

                // 5. m_updatedNodes (ulong[1024])
                FieldInfo unf = nemType.GetField("m_updatedNodes", BindingFlags.NonPublic | BindingFlags.Instance);
                if (unf != null && unf.GetValue(nem) is ulong[] unArr && unArr.Length < 1024)
                {
                    Array.Resize(ref unArr, 1024);
                    unf.SetValue(nem, unArr);
                    resizedAny = true;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized AdaptiveRoads m_updatedNodes to 1024 ({0})", context));
                }

                // 6. m_updatedSegments (ulong[1024])
                FieldInfo usf = nemType.GetField("m_updatedSegments", BindingFlags.NonPublic | BindingFlags.Instance);
                if (usf != null && usf.GetValue(nem) is ulong[] usArr && usArr.Length < 1024)
                {
                    Array.Resize(ref usArr, 1024);
                    usf.SetValue(nem, usArr);
                    resizedAny = true;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized AdaptiveRoads m_updatedSegments to 1024 ({0})", context));
                }

                if (resizedAny)
                {
                    MemoryOptimizer.StrategicGarbageCollect("AfterAdaptiveRoadsBufferExpansion");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureAdaptiveRoadsBuffers error: " + ex);
            }
            finally
            {
                _arBufferGuard = false;
            }
        }

        public static void EnsureNetworkSkinsBuffers(string context)
        {
            try
            {
                Type nsmType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "NetworkSkins")
                    {
                        nsmType = asm.GetType("NetworkSkins.Skins.NetworkSkinManager");
                        break;
                    }
                }

                if (nsmType == null) return;

                bool resized = false;
                FieldInfo ssField = nsmType.GetField("SegmentSkins", BindingFlags.Public | BindingFlags.Static);
                if (ssField != null && ssField.GetValue(null) is Array ssArr && ssArr.Length < 65024)
                {
                    Type elem = ssArr.GetType().GetElementType();
                    Array newArr = Array.CreateInstance(elem, 65024);
                    Array.Copy(ssArr, newArr, ssArr.Length);
                    ssField.SetValue(null, newArr);
                    resized = true;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized NetworkSkins SegmentSkins from {0} to 65024 ({1})", ssArr.Length, context));
                }

                FieldInfo nsField = nsmType.GetField("NodeSkins", BindingFlags.Public | BindingFlags.Static);
                if (nsField != null && nsField.GetValue(null) is Array nsArr && nsArr.Length < 65024)
                {
                    Type elem = nsArr.GetType().GetElementType();
                    Array newArr = Array.CreateInstance(elem, 65024);
                    Array.Copy(nsArr, newArr, nsArr.Length);
                    nsField.SetValue(null, newArr);
                    resized = true;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized NetworkSkins NodeSkins from {0} to 65024 ({1})", nsArr.Length, context));
                }

                if (resized)
                {
                    MemoryOptimizer.StrategicGarbageCollect("AfterNetworkSkinsBufferExpansion");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureNetworkSkinsBuffers error: " + ex);
            }
        }

        public static void EnsureNodeControllerBuffers(string context)
        {
            try
            {
                Type ncMgrType = null;
                Assembly ncAsm = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "NodeControllerRenewal" || asm.GetName().Name == "NodeController")
                    {
                        ncAsm = asm;
                        ncMgrType = asm.GetType("NodeController.Manager");
                        if (ncMgrType != null) break;
                    }
                }

                if (ncMgrType == null || ncAsm == null) return;

                Type smType = ncAsm.GetType("ModsCommon.SingletonManager`1");
                if (smType != null)
                {
                    Type genType = smType.MakeGenericType(ncMgrType);
                    PropertyInfo instProp = genType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    object ncMgr = instProp != null ? instProp.GetValue(null, null) : null;

                    if (ncMgr != null)
                    {
                        PropertyInfo bufProp = ncMgrType.GetProperty("Buffer", BindingFlags.Public | BindingFlags.Instance);
                        if (bufProp != null)
                        {
                            Array buf = bufProp.GetValue(ncMgr, null) as Array;
                            if (buf != null && buf.Length < 65024)
                            {
                                Type elem = buf.GetType().GetElementType();
                                Array newBuf = Array.CreateInstance(elem, 65024);
                                Array.Copy(buf, newBuf, buf.Length);
                                bufProp.SetValue(ncMgr, newBuf, null);
                                MemoryOptimizer.StrategicGarbageCollect("AfterNodeControllerBufferExpansion");
                                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized NodeController Buffer from {0} to 65024 ({1})", buf.Length, context));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureNodeControllerBuffers error: " + ex);
            }
        }

        private static void SetStaticField(Type type, string fieldName, object value)
        {
            if (type == null || value == null) return;
            try
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null)
                {
                    field.SetValue(null, value);
                }
            }
            catch { }
        }

        public static void EnsureMoveItBuffers(string context)
        {
            try
            {
                var nm = Singleton<NetManager>.instance;
                if (nm == null) return;
                var bm = Singleton<BuildingManager>.instance;

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "MoveIt")
                    {
                        Type actionType = asm.GetType("MoveIt.Action");
                        if (actionType != null)
                        {
                            SetStaticField(actionType, "nodeBuffer", nm.m_nodes.m_buffer);
                            SetStaticField(actionType, "segmentBuffer", nm.m_segments.m_buffer);
                            if (bm != null) SetStaticField(actionType, "buildingBuffer", bm.m_buildings.m_buffer);
                        }

                        Type toolType = asm.GetType("MoveIt.MoveItTool");
                        if (toolType != null)
                        {
                            SetStaticField(toolType, "nodeBuffer", nm.m_nodes.m_buffer);
                            SetStaticField(toolType, "segmentBuffer", nm.m_segments.m_buffer);
                            if (bm != null) SetStaticField(toolType, "buildingBuffer", bm.m_buildings.m_buffer);
                        }

                        Type instType = asm.GetType("MoveIt.Instance");
                        if (instType != null)
                        {
                            SetStaticField(instType, "nodeBuffer", nm.m_nodes.m_buffer);
                            SetStaticField(instType, "segmentBuffer", nm.m_segments.m_buffer);
                            SetStaticField(instType, "laneBuffer", nm.m_lanes.m_buffer);
                            if (bm != null) SetStaticField(instType, "buildingBuffer", bm.m_buildings.m_buffer);
                        }

                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Synchronized MoveIt static network & building buffers ({0})", context));
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureMoveItBuffers error: " + ex);
            }
        }

        public static void EnsureKianCommonsBuffers(string context)
        {
            try
            {
                var nm = Singleton<NetManager>.instance;
                if (nm == null) return;

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type netUtilType = asm.GetType("KianCommons.NetUtil");
                    if (netUtilType != null)
                    {
                        SetStaticField(netUtilType, "nodeBuffer_", nm.m_nodes.m_buffer);
                        SetStaticField(netUtilType, "segmentBuffer_", nm.m_segments.m_buffer);
                        SetStaticField(netUtilType, "laneBuffer_", nm.m_lanes.m_buffer);
                        SetStaticField(netUtilType, "netMan", nm);
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Synchronized KianCommons.NetUtil buffers ({0})", context));
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureKianCommonsBuffers error: " + ex);
            }
        }

        public static void EnsureImprovedPublicTransportBuffers(string context)
        {
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string name = asm.GetName().Name;
                    if (name == "IPTEssentials" || name == "ImprovedPublicTransport3" || name == "ImprovedPublicTransport2")
                    {
                        Type[] types = new Type[] {
                            asm.GetType("ImprovedPublicTransport2.Data.CachedNodeData"),
                            asm.GetType("ImprovedPublicTransport.Data.CachedNodeData")
                        };
                        foreach (var type in types)
                        {
                            if (type != null)
                            {
                                FieldInfo field = type.GetField("m_cachedNodeData", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                                if (field != null)
                                {
                                    Array arr = field.GetValue(null) as Array;
                                    if (arr != null && arr.Length < 65024)
                                    {
                                        Type elem = arr.GetType().GetElementType();
                                        Array newArr = Array.CreateInstance(elem, 65024);
                                        Array.Copy(arr, newArr, arr.Length);
                                        field.SetValue(null, newArr);
                                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized {0}.m_cachedNodeData from {1} to 65024 ({2})", type.FullName, arr.Length, context));
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureImprovedPublicTransportBuffers error: " + ex);
            }
        }

        private static void PatchThirdPartyMods(HarmonyInstance harmony)
        {
            if (harmony == null) return;

            // 1. Adaptive Roads lifecycle hooks
            if (!_adaptiveRoadsPatched)
            {
                try
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (asm.GetName().Name == "AdaptiveRoads")
                        {
                            Type nemType = asm.GetType("AdaptiveRoads.Manager.NetworkExtensionManager");
                            if (nemType != null)
                            {
                                MethodInfo initBuf = nemType.GetMethod("InitBuffers", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                                if (initBuf != null)
                                {
                                    MethodInfo post = typeof(ThirdPartyCompatibility).GetMethod(nameof(AR_InitBuffers_Postfix), BindingFlags.Public | BindingFlags.Static);
                                    harmony.Patch(initBuf, postfix: new HarmonyMethod(post));
                                }

                                MethodInfo createNew = nemType.GetMethod("CreateNew", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                                if (createNew != null)
                                {
                                    MethodInfo post = typeof(ThirdPartyCompatibility).GetMethod(nameof(AR_CreateNew_Postfix), BindingFlags.Public | BindingFlags.Static);
                                    harmony.Patch(createNew, postfix: new HarmonyMethod(post));
                                }

                                MethodInfo awake = nemType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                                if (awake != null)
                                {
                                    MethodInfo post = typeof(ThirdPartyCompatibility).GetMethod(nameof(AR_Awake_Postfix), BindingFlags.Public | BindingFlags.Static);
                                    harmony.Patch(awake, postfix: new HarmonyMethod(post));
                                }

                                _adaptiveRoadsPatched = true;
                                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Successfully hooked AdaptiveRoads lifecycle for dynamic 65K buffers!");
                            }
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] AdaptiveRoads patching error: " + ex);
                }
            }

            // 2. Network Skins hooks
            if (!_networkSkinsPatched)
            {
                try
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (asm.GetName().Name == "NetworkSkins")
                        {
                            Type nsmType = asm.GetType("NetworkSkins.Skins.NetworkSkinManager");
                            if (nsmType != null)
                            {
                                MethodInfo awake = nsmType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                                if (awake != null)
                                {
                                    MethodInfo post = typeof(ThirdPartyCompatibility).GetMethod(nameof(NetworkSkinManager_Awake_Postfix), BindingFlags.Public | BindingFlags.Static);
                                    harmony.Patch(awake, postfix: new HarmonyMethod(post));
                                }

                                _networkSkinsPatched = true;
                                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Successfully hooked NetworkSkins.NetworkSkinManager.Awake for dynamic 65K buffers!");
                            }
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] NetworkSkins patching error: " + ex);
                }
            }

            // 3. Node Controller hooks
            if (!_nodeControllerPatched)
            {
                try
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (asm.GetName().Name == "NodeControllerRenewal" || asm.GetName().Name == "NodeController")
                        {
                            Type ncMgrType = asm.GetType("NodeController.Manager");
                            if (ncMgrType != null)
                            {
                                MethodInfo clear = ncMgrType.GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                                if (clear != null)
                                {
                                    MethodInfo post = typeof(ThirdPartyCompatibility).GetMethod(nameof(NodeController_Clear_Postfix), BindingFlags.Public | BindingFlags.Static);
                                    harmony.Patch(clear, postfix: new HarmonyMethod(post));
                                }

                                MethodInfo fromXml = ncMgrType.GetMethod("FromXml", BindingFlags.Public | BindingFlags.Instance);
                                if (fromXml != null)
                                {
                                    MethodInfo post = typeof(ThirdPartyCompatibility).GetMethod(nameof(NodeController_Clear_Postfix), BindingFlags.Public | BindingFlags.Static);
                                    harmony.Patch(fromXml, postfix: new HarmonyMethod(post));
                                }

                                _nodeControllerPatched = true;
                                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Successfully hooked NodeController.Manager.Clear and FromXml for dynamic 65K buffers!");
                            }
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] NodeController patching error: " + ex);
                }
            }
        }

        public static void AR_InitBuffers_Postfix(object __instance)
        {
            EnsureAdaptiveRoadsBuffers("AR.InitBuffers.Postfix", __instance);
        }

        public static void AR_CreateNew_Postfix()
        {
            EnsureAdaptiveRoadsBuffers("AR.CreateNew.Postfix", null);
        }

        public static void AR_Awake_Postfix(object __instance)
        {
            EnsureAdaptiveRoadsBuffers("AR.Awake.Postfix", __instance);
        }

        public static void NetworkSkinManager_Awake_Postfix()
        {
            EnsureNetworkSkinsBuffers("NS.Awake.Postfix");
        }

        public static void NodeController_Clear_Postfix()
        {
            EnsureNodeControllerBuffers("NC.Clear.Postfix");
        }
    }

    public static class VehicleAndTransportRepair
    {
        private static UnityEngine.GameObject _runnerGo = null;

        public static void Initialize()
        {
            try
            {
                if (_runnerGo == null)
                {
                    _runnerGo = new UnityEngine.GameObject("MNC_RepairRunner");
                    UnityEngine.Object.DontDestroyOnLoad(_runnerGo);
                    _runnerGo.AddComponent<RepairRunner>();
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Initialized RepairRunner GameObject.");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Initialize RepairRunner error: " + ex);
            }
        }

        public static int ClearAllVehicles()
        {
            int freed = 0;
            try
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Starting vehicle wipeout / despawn...");
                // 1. Try TM:PE ClearTraffic
                try
                {
                    Assembly tmpe = TMPECompatibility.GetTMPEAssembly();
                    if (tmpe != null)
                    {
                        Type umType = tmpe.GetType("TrafficManager.Manager.Impl.UtilityManager");
                        if (umType != null)
                        {
                            object umInst = TMPECompatibility.GetManagerInstance(umType);
                            MethodInfo ct = umType.GetMethod("ClearTraffic", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (ct != null && umInst != null)
                            {
                                ct.Invoke(umInst, new object[0]);
                                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Executed TM:PE UtilityManager.ClearTraffic().");
                            }
                        }
                    }
                }
                catch { }

                // 2. Direct clean pass on VehicleManager to release ALL active vehicles (including stuck/broken/orphans)
                VehicleManager vm = Singleton<VehicleManager>.instance;
                PathManager pm = Singleton<PathManager>.instance;
                if (vm != null && vm.m_vehicles != null && vm.m_vehicles.m_buffer != null)
                {
                    for (int i = 1; i < vm.m_vehicles.m_buffer.Length; i++)
                    {
                        ushort vId = (ushort)i;
                        if ((vm.m_vehicles.m_buffer[i].m_flags & Vehicle.Flags.Created) != 0)
                        {
                            if (vm.m_vehicles.m_buffer[i].m_path != 0 && pm != null)
                            {
                                try { pm.ReleasePath(vm.m_vehicles.m_buffer[i].m_path); } catch { }
                                vm.m_vehicles.m_buffer[i].m_path = 0;
                            }
                            vm.ReleaseVehicle(vId);
                            freed++;
                        }
                    }
                }

                // 3. Flush PathFind pending queues so worker threads instantly stop processing broken queries
                try
                {
                    FieldInfo pfArrayField = typeof(PathManager).GetField("m_pathfinds", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    Array pfArray = (pm != null && pfArrayField != null) ? pfArrayField.GetValue(pm) as Array : null;
                    if (pfArray != null)
                    {
                        for (int p = 0; p < pfArray.Length; p++)
                        {
                            object pf = pfArray.GetValue(p);
                            if (pf == null) continue;
                            Type pfType = pf.GetType();
                            FieldInfo qlField = pfType.GetField("queueLock_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                             ?? pfType.GetField("m_queueLock", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            FieldInfo qfField = pfType.GetField("queueFirst_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                             ?? pfType.GetField("m_queueFirst", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            FieldInfo qlLastField = pfType.GetField("queueLast_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                                 ?? pfType.GetField("m_queueLast", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (qlField != null && qfField != null && qlLastField != null)
                            {
                                object lockObj = qlField.GetValue(pf);
                                if (lockObj != null)
                                {
                                    lock (lockObj)
                                    {
                                        qfField.SetValue(pf, (uint)0);
                                        qlLastField.SetValue(pf, (uint)0);
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }

                // 4. Clean up citizen paths
                UnstickCitizens();

                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] ClearAllVehicles completed: Successfully cleared {0} vehicles.", freed));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] ClearAllVehicles error: " + ex);
            }
            return freed;
        }

        public static void RunFullRepair(bool verbose = true)
        {
            try
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Starting Full Vehicle & Transport Line Repair...");
                int cleared = ClearAllVehicles();
                int restoredLines = RestoreAllTransportLines();
                
                string msg = string.Format("MNC Renew: Repaired & cleared {0} vehicles and restored {1} transport lines!", cleared, restoredLines);
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] " + msg);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] RunFullRepair error: " + ex);
            }
        }

        public static int UnstickAllVehicles(bool releaseDeadlocked = true)
        {
            return ClearAllVehicles();
        }

        public static int UnstickCitizens()
        {
            int freed = 0;
            try
            {
                CitizenManager cm = Singleton<CitizenManager>.instance;
                PathManager pm = Singleton<PathManager>.instance;
                if (cm == null || cm.m_instances == null || cm.m_instances.m_buffer == null) return 0;

                for (int i = 1; i < cm.m_instances.m_buffer.Length; i++)
                {
                    if ((cm.m_instances.m_buffer[i].m_flags & CitizenInstance.Flags.Created) == 0) continue;

                    if ((cm.m_instances.m_buffer[i].m_flags & (CitizenInstance.Flags.WaitingTransport | CitizenInstance.Flags.WaitingPath)) != 0 && cm.m_instances.m_buffer[i].m_waitCounter >= 25)
                    {
                        if (cm.m_instances.m_buffer[i].m_path != 0 && pm != null)
                        {
                            pm.ReleasePath(cm.m_instances.m_buffer[i].m_path);
                            cm.m_instances.m_buffer[i].m_path = 0;
                        }
                        cm.m_instances.m_buffer[i].m_waitCounter = 0;
                        cm.m_instances.m_buffer[i].m_flags &= ~(CitizenInstance.Flags.WaitingTransport | CitizenInstance.Flags.WaitingPath);
                        freed++;
                    }
                }
                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] UnstickCitizens completed: Fixed {0} waiting citizens.", freed));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] UnstickCitizens error: " + ex);
            }
            return freed;
        }

        public static int RestoreAllTransportLines()
        {
            return RestoreAllTransportLines(out _);
        }

        public static int RestoreAllTransportLines(out int deactivated)
        {
            int count = 0;
            deactivated = 0;
            try
            {
                TransportManager tm = Singleton<TransportManager>.instance;
                NetManager nm = Singleton<NetManager>.instance;
                VehicleManager vm = Singleton<VehicleManager>.instance;
                if (tm == null || tm.m_lines == null || tm.m_lines.m_buffer == null || nm == null || nm.m_nodes == null) return 0;

                int nodeLimit = nm.m_nodes.m_buffer.Length;
                int prefabCount = PrefabCollection<TransportInfo>.PrefabCount();

                for (int lineID = 1; lineID < tm.m_lines.m_buffer.Length; lineID++)
                {
                    ushort lid = (ushort)lineID;
                    if ((tm.m_lines.m_buffer[lid].m_flags & TransportLine.Flags.Created) == 0) continue;

                    ushort infoIndex = tm.m_lines.m_buffer[lid].m_infoIndex;
                    ushort stops = tm.m_lines.m_buffer[lid].m_stops;
                    float len = tm.m_lines.m_buffer[lid].m_totalLength;

                    // If infoIndex invalid, stop node out of bounds, or length NaN: deactivate ghost line!
                    if (infoIndex >= prefabCount || PrefabCollection<TransportInfo>.GetPrefab(infoIndex) == null ||
                        stops >= 65024 || stops >= nodeLimit || float.IsNaN(len) || float.IsInfinity(len))
                    {
                        tm.m_lines.m_buffer[lid].m_flags = TransportLine.Flags.None;
                        tm.m_lines.m_buffer[lid].m_infoIndex = 0;
                        tm.m_lines.m_buffer[lid].m_stops = 0;
                        tm.m_lines.m_buffer[lid].m_totalLength = 0f;
                        tm.m_lines.m_buffer[lid].m_vehicles = 0;
                        deactivated++;
                        continue;
                    }

                    // Clean flags: clear Hidden and Invalid (64), set Complete
                    tm.m_lines.m_buffer[lid].m_flags &= ~((TransportLine.Flags)64 | TransportLine.Flags.Hidden);
                    tm.m_lines.m_buffer[lid].m_flags |= (TransportLine.Flags.Complete | TransportLine.Flags.CustomColor);

                    // Reset dangling vehicle pointers if head vehicle is non-existent
                    if (vm != null && vm.m_vehicles != null && vm.m_vehicles.m_buffer != null)
                    {
                        ushort vHead = tm.m_lines.m_buffer[lid].m_vehicles;
                        if (vHead != 0 && (vHead >= vm.m_vehicles.m_buffer.Length || (vm.m_vehicles.m_buffer[vHead].m_flags & Vehicle.Flags.Created) == 0))
                        {
                            tm.m_lines.m_buffer[lid].m_vehicles = 0;
                        }
                    }

                    // Verify and fix stops
                    ushort firstStop = stops;
                    ushort currStop = firstStop;
                    int iterations = 0;

                    while (currStop != 0 && currStop < nodeLimit && iterations < 500)
                    {
                        iterations++;
                        if ((nm.m_nodes.m_buffer[currStop].m_flags & NetNode.Flags.Created) != 0)
                        {
                            nm.m_nodes.m_buffer[currStop].m_transportLine = lid;
                        }

                        ushort nextStop = TransportLine.GetNextStop(currStop);
                        if (nextStop == 0 || nextStop >= nodeLimit)
                        {
                            for (int segIdx = 0; segIdx < 8; segIdx++)
                            {
                                ushort seg = nm.m_nodes.m_buffer[currStop].GetSegment(segIdx);
                                if (seg != 0 && seg < nm.m_segments.m_buffer.Length && (nm.m_segments.m_buffer[seg].m_flags & NetSegment.Flags.Created) != 0)
                                {
                                    if (nm.m_segments.m_buffer[seg].m_startNode == currStop)
                                    {
                                        ushort other = nm.m_segments.m_buffer[seg].m_endNode;
                                        if (other < nodeLimit && nm.m_nodes.m_buffer[other].m_transportLine == lid)
                                        {
                                            nextStop = other;
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        if (nextStop == 0 || nextStop == firstStop || nextStop >= nodeLimit) break;
                        currStop = nextStop;
                    }

                    try
                    {
                        tm.m_lines.m_buffer[lid].UpdatePaths(lid);
                        tm.m_lines.m_buffer[lid].UpdateMeshData(lid);
                        tm.UpdateLine(lid);
                        count++;
                    }
                    catch { }
                }

                tm.m_linesUpdated = true;
                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] RestoreAllTransportLines completed: Restored and updated {0} transport lines (deactivated {1} ghost lines).", count, deactivated));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] RestoreAllTransportLines error: " + ex);
            }
            return count;
        }

        public static void RestoreLinesFromBackupCRP()
        {
            string backupPath = "/Users/nqocduy/Library/Application Support/Colossal Order/Cities_Skylines/Saves/QuangNinh backup .crp";
            if (!File.Exists(backupPath))
            {
                try
                {
                    string saveFolder = ColossalFramework.IO.DataLocation.saveLocation;
                    string[] matches = Directory.GetFiles(saveFolder, "*QuangNinh*backup*.crp");
                    if (matches != null && matches.Length > 0)
                    {
                        backupPath = matches[0];
                    }
                }
                catch { }
            }

            if (!File.Exists(backupPath))
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Backup save not found at " + backupPath);
                return;
            }

            try
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Reading lines from backup save: " + backupPath);
                TransportManager tm = Singleton<TransportManager>.instance;
                if (tm == null) return;

                byte[] pattern = new byte[] { 0xD4, 0x1E, 0xEF, 0x0D, 0x05, 0xFD, 0xFF, 0x03, 0x02, 0xD9, 0x14, 0x0D, 0xF3, 0x06, 0xFE, 0x0D };
                byte[] all = File.ReadAllBytes(backupPath);
                int tmOffset = -1;
                for (int i = all.Length - pattern.Length - 1; i >= 0; i--)
                {
                    bool match = true;
                    for (int j = 0; j < pattern.Length; j++)
                    {
                        if (all[i + j] != pattern[j]) { match = false; break; }
                    }
                    if (match && i > 10000) { tmOffset = i; break; }
                }

                if (tmOffset < 0)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Could not find TransportManager data chunk in backup save.");
                    return;
                }

                using (FileStream fs = new FileStream(backupPath, FileMode.Open, FileAccess.Read))
                {
                    fs.Seek(tmOffset + 16, SeekOrigin.Begin);
                    ConstructorInfo ctor = typeof(ColossalFramework.IO.DataSerializer).GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)[0];
                    ColossalFramework.IO.DataSerializer ds = (ColossalFramework.IO.DataSerializer)ctor.Invoke(new object[] { fs, (ColossalFramework.IO.DataSerializer.Mode)1, false });
                    FieldInfo vFld = typeof(ColossalFramework.IO.DataSerializer).GetField("m_version", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (vFld != null) vFld.SetValue(ds, 121034u);

                    uint[] flags = new uint[256];
                    ColossalFramework.IO.EncodedArray.UInt rFlags = ColossalFramework.IO.EncodedArray.UInt.BeginRead(ds);
                    for (int i = 1; i < 256; i++) flags[i] = rFlags.Read();
                    rFlags.EndRead();

                    int restored = 0;
                    for (ushort i = 1; i < 256; i++)
                    {
                        if ((flags[i] & 1) != 0)
                        {
                            tm.m_lines.m_buffer[i].m_flags = (TransportLine.Flags)flags[i];
                            tm.m_lines.m_buffer[i].m_flags &= ~TransportLine.Flags.Hidden;
                            tm.m_lines.m_buffer[i].m_flags |= TransportLine.Flags.Complete;
                            restored++;
                        }
                    }
                    RestoreAllTransportLines();
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Successfully restored {0} lines from backup save file!", restored));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] RestoreLinesFromBackupCRP error: " + ex);
            }
        }
    }

    public static class TransportLineCompatibility
    {
        public static void Apply(HarmonyInstance harmony)
        {
            if (harmony == null) return;
            try
            {
                // 1. Patch TransportManager.Data.AfterDeserialize
                Type tmDataType = typeof(TransportManager).GetNestedType("Data", BindingFlags.NonPublic | BindingFlags.Public);
                if (tmDataType != null)
                {
                    MethodInfo afterDes = tmDataType.GetMethod("AfterDeserialize", BindingFlags.Public | BindingFlags.Instance);
                    if (afterDes != null)
                    {
                        MethodInfo post = typeof(TransportLineCompatibility).GetMethod(nameof(TransportManager_AfterDeserialize_Postfix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(afterDes, postfix: new HarmonyMethod(post));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TransportManager.Data.AfterDeserialize for line sanitization.");
                    }

                    // 1b. Patch TransportManager.Data.Serialize (Pre-Save sanitization)
                    MethodInfo tmSer = tmDataType.GetMethod("Serialize", BindingFlags.Public | BindingFlags.Instance);
                    if (tmSer != null)
                    {
                        MethodInfo pre = typeof(TransportLineCompatibility).GetMethod(nameof(TransportManager_Data_Serialize_Prefix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(tmSer, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TransportManager.Data.Serialize with pre-save sanitization.");
                    }
                }

                // 2. Patch SimulationManager.Data.Serialize (Universal Pre-Save Sanitization)
                Type simDataType = typeof(SimulationManager).GetNestedType("Data", BindingFlags.NonPublic | BindingFlags.Public);
                if (simDataType != null)
                {
                    MethodInfo simSer = simDataType.GetMethod("Serialize", BindingFlags.Public | BindingFlags.Instance);
                    if (simSer != null)
                    {
                        MethodInfo pre = typeof(TransportLineCompatibility).GetMethod(nameof(SimulationManager_Data_Serialize_Prefix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(simSer, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched SimulationManager.Data.Serialize with universal pre-save sanitization.");
                    }
                }

                // 3. Patch PrefabCollection<T>.Serialize bounds guards
                PatchPrefabCollectionSerialize<TransportInfo>(harmony, nameof(PrefabCollection_TransportInfo_Serialize_Prefix));
                PatchPrefabCollectionSerialize<VehicleInfo>(harmony, nameof(PrefabCollection_VehicleInfo_Serialize_Prefix));
                PatchPrefabCollectionSerialize<BuildingInfo>(harmony, nameof(PrefabCollection_BuildingInfo_Serialize_Prefix));
                PatchPrefabCollectionSerialize<NetInfo>(harmony, nameof(PrefabCollection_NetInfo_Serialize_Prefix));

                // 4. Patch TransportLine.GetNextStop
                MethodInfo getNextStop = typeof(TransportLine).GetMethod("GetNextStop", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(ushort) }, null);
                if (getNextStop != null)
                {
                    MethodInfo pre = typeof(TransportLineCompatibility).GetMethod(nameof(TransportLine_GetNextStop_Prefix), BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(getNextStop, prefix: new HarmonyMethod(pre));
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TransportLine.GetNextStop with bounds check.");
                }

                // 5. Patch ImprovedPublicTransport CachedTransportLineData.Init with prefix safety
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string aname = asm.GetName().Name;
                    if (aname == "ImprovedPublicTransport3" || aname == "ImprovedPublicTransport2" || aname == "IPTEssentials")
                    {
                        Type ctld = asm.GetType("ImprovedPublicTransport.Data.CachedTransportLineData")
                                 ?? asm.GetType("ImprovedPublicTransport2.Data.CachedTransportLineData");
                        if (ctld != null)
                        {
                            MethodInfo initM = ctld.GetMethod("Init", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                            if (initM != null)
                            {
                                MethodInfo pre = typeof(TransportLineCompatibility).GetMethod(nameof(IPT_Init_Prefix), BindingFlags.Public | BindingFlags.Static);
                                harmony.Patch(initM, prefix: new HarmonyMethod(pre));
                                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched ImprovedPublicTransport CachedTransportLineData.Init with safety prefix.");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TransportLineCompatibility error: " + ex);
            }
        }

        private static void PatchPrefabCollectionSerialize<T>(HarmonyInstance harmony, string prefixMethodName) where T : PrefabInfo
        {
            try
            {
                MethodInfo mTarget = typeof(PrefabCollection<T>).GetMethod("Serialize", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(uint) }, null);
                if (mTarget != null)
                {
                    MethodInfo pre = typeof(TransportLineCompatibility).GetMethod(prefixMethodName, BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(mTarget, prefix: new HarmonyMethod(pre));
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Patched PrefabCollection<{0}>.Serialize with bounds safety guard.", typeof(T).Name));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Error patching PrefabCollection<{0}>.Serialize: {1}", typeof(T).Name, ex));
            }
        }

        public static void SimulationManager_Data_Serialize_Prefix(object __instance, ColossalFramework.IO.DataSerializer s)
        {
            PreSaveSanitization();
        }

        public static void TransportManager_Data_Serialize_Prefix(object __instance, ColossalFramework.IO.DataSerializer s)
        {
            SanitizeTransportLines();
        }

        public static bool PrefabCollection_TransportInfo_Serialize_Prefix(uint index)
        {
            return GuardPrefabCollectionSerialize<TransportInfo>(index);
        }

        public static bool PrefabCollection_VehicleInfo_Serialize_Prefix(uint index)
        {
            return GuardPrefabCollectionSerialize<VehicleInfo>(index);
        }

        public static bool PrefabCollection_BuildingInfo_Serialize_Prefix(uint index)
        {
            return GuardPrefabCollectionSerialize<BuildingInfo>(index);
        }

        public static bool PrefabCollection_NetInfo_Serialize_Prefix(uint index)
        {
            return GuardPrefabCollectionSerialize<NetInfo>(index);
        }

        public static bool GuardPrefabCollectionSerialize<T>(uint index) where T : PrefabInfo
        {
            try
            {
                int count = PrefabCollection<T>.PrefabCount();
                if (index >= (uint)count || PrefabCollection<T>.GetPrefab(index) == null)
                {
                    UnityEngine.Debug.LogWarning(string.Format("[MoreNetworkCapacityRenew] Guarded PrefabCollection<{0}>.Serialize against out-of-bounds index: {1} (count: {2})", typeof(T).Name, index, count));
                    FieldInfo fEnc = typeof(PrefabCollection<T>).GetField("m_encodedArray", BindingFlags.NonPublic | BindingFlags.Static);
                    if (fEnc != null)
                    {
                        var enc = fEnc.GetValue(null) as ColossalFramework.IO.EncodedArray.UShort;
                        if (enc != null) enc.Write(0);
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] GuardPrefabCollectionSerialize<{0}> error: {1}", typeof(T).Name, ex));
            }
            return true;
        }

        public static void PreSaveSanitization()
        {
            try
            {
                SanitizeTransportLines();
                SanitizeVehicles();
                SanitizeBuildings();
                SanitizeNetElements();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] PreSaveSanitization error: " + ex);
            }
        }

        public static int SanitizeTransportLines()
        {
            int sanitized = 0;
            try
            {
                TransportManager tm = Singleton<TransportManager>.instance;
                if (tm == null || tm.m_lines == null || tm.m_lines.m_buffer == null) return 0;

                int prefabCount = PrefabCollection<TransportInfo>.PrefabCount();
                for (int i = 0; i < tm.m_lines.m_buffer.Length; i++)
                {
                    if (tm.m_lines.m_buffer[i].m_flags != TransportLine.Flags.None)
                    {
                        bool invalid = false;
                        ushort infoIndex = tm.m_lines.m_buffer[i].m_infoIndex;
                        if ((tm.m_lines.m_buffer[i].m_flags & TransportLine.Flags.Created) == 0)
                        {
                            invalid = true;
                        }
                        else if (infoIndex >= prefabCount || PrefabCollection<TransportInfo>.GetPrefab(infoIndex) == null)
                        {
                            invalid = true;
                        }
                        else if (float.IsNaN(tm.m_lines.m_buffer[i].m_totalLength) || float.IsInfinity(tm.m_lines.m_buffer[i].m_totalLength))
                        {
                            invalid = true;
                        }

                        if (invalid)
                        {
                            tm.m_lines.m_buffer[i].m_flags = TransportLine.Flags.None;
                            tm.m_lines.m_buffer[i].m_infoIndex = 0;
                            tm.m_lines.m_buffer[i].m_stops = 0;
                            tm.m_lines.m_buffer[i].m_totalLength = 0f;
                            tm.m_lines.m_buffer[i].m_vehicles = 0;
                            sanitized++;
                        }
                        else
                        {
                            if (tm.m_lines.m_buffer[i].m_stops >= 65024)
                            {
                                tm.m_lines.m_buffer[i].m_stops = 0;
                            }
                            VehicleManager vm = Singleton<VehicleManager>.instance;
                            if (vm != null && vm.m_vehicles != null && vm.m_vehicles.m_buffer != null)
                            {
                                if (tm.m_lines.m_buffer[i].m_vehicles >= vm.m_vehicles.m_buffer.Length)
                                {
                                    tm.m_lines.m_buffer[i].m_vehicles = 0;
                                }
                            }
                        }
                    }
                }
                if (sanitized > 0)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] SanitizeTransportLines: Cleared {0} corrupted/ghost transport lines.", sanitized));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeTransportLines error: " + ex);
            }
            return sanitized;
        }

        public static int SanitizeVehicles()
        {
            int cleared = 0;
            try
            {
                VehicleManager vm = Singleton<VehicleManager>.instance;
                if (vm == null || vm.m_vehicles == null || vm.m_vehicles.m_buffer == null) return 0;

                int prefabCount = PrefabCollection<VehicleInfo>.PrefabCount();
                for (int i = 1; i < vm.m_vehicles.m_buffer.Length; i++)
                {
                    if ((vm.m_vehicles.m_buffer[i].m_flags & Vehicle.Flags.Created) != 0)
                    {
                        ushort infoIndex = vm.m_vehicles.m_buffer[i].m_infoIndex;
                        if (infoIndex >= prefabCount || PrefabCollection<VehicleInfo>.GetPrefab(infoIndex) == null)
                        {
                            vm.ReleaseVehicle((ushort)i);
                            cleared++;
                        }
                    }
                }
                if (cleared > 0)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] SanitizeVehicles: Released {0} vehicles with invalid prefabs.", cleared));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeVehicles error: " + ex);
            }
            return cleared;
        }

        public static int SanitizeBuildings()
        {
            int cleared = 0;
            try
            {
                BuildingManager bm = Singleton<BuildingManager>.instance;
                if (bm == null || bm.m_buildings == null || bm.m_buildings.m_buffer == null) return 0;

                int prefabCount = PrefabCollection<BuildingInfo>.PrefabCount();
                for (int i = 1; i < bm.m_buildings.m_buffer.Length; i++)
                {
                    if ((bm.m_buildings.m_buffer[i].m_flags & Building.Flags.Created) != 0)
                    {
                        ushort infoIndex = bm.m_buildings.m_buffer[i].m_infoIndex;
                        if (infoIndex >= prefabCount || PrefabCollection<BuildingInfo>.GetPrefab(infoIndex) == null)
                        {
                            bm.m_buildings.m_buffer[i].m_flags = Building.Flags.None;
                            cleared++;
                        }
                    }
                }
                if (cleared > 0)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] SanitizeBuildings: Cleared {0} buildings with invalid prefabs.", cleared));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeBuildings error: " + ex);
            }
            return cleared;
        }

        public static int SanitizeNetElements()
        {
            int cleared = 0;
            try
            {
                NetManager nm = Singleton<NetManager>.instance;
                if (nm == null || nm.m_segments == null || nm.m_segments.m_buffer == null || nm.m_nodes == null || nm.m_nodes.m_buffer == null) return 0;

                int prefabCount = PrefabCollection<NetInfo>.PrefabCount();
                for (int i = 1; i < nm.m_segments.m_buffer.Length; i++)
                {
                    if ((nm.m_segments.m_buffer[i].m_flags & NetSegment.Flags.Created) != 0)
                    {
                        ushort infoIndex = nm.m_segments.m_buffer[i].m_infoIndex;
                        if (infoIndex >= prefabCount || PrefabCollection<NetInfo>.GetPrefab(infoIndex) == null)
                        {
                            nm.m_segments.m_buffer[i].m_flags = NetSegment.Flags.None;
                            cleared++;
                        }
                    }
                }
                for (int i = 1; i < nm.m_nodes.m_buffer.Length; i++)
                {
                    if ((nm.m_nodes.m_buffer[i].m_flags & NetNode.Flags.Created) != 0)
                    {
                        ushort infoIndex = nm.m_nodes.m_buffer[i].m_infoIndex;
                        if (infoIndex >= prefabCount || PrefabCollection<NetInfo>.GetPrefab(infoIndex) == null)
                        {
                            nm.m_nodes.m_buffer[i].m_flags = NetNode.Flags.None;
                            cleared++;
                        }
                    }
                }
                if (cleared > 0)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] SanitizeNetElements: Cleared {0} net elements with invalid prefabs.", cleared));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeNetElements error: " + ex);
            }
            return cleared;
        }

        public static void TransportManager_AfterDeserialize_Postfix()
        {
            try
            {
                TransportManager tm = Singleton<TransportManager>.instance;
                if (tm == null || tm.m_lines == null || tm.m_lines.m_buffer == null) return;
                
                int prefabCount = PrefabCollection<TransportInfo>.PrefabCount();
                int cleaned = 0;
                for (int i = 0; i < tm.m_lines.m_buffer.Length; i++)
                {
                    if (tm.m_lines.m_buffer[i].m_flags != TransportLine.Flags.None)
                    {
                        ushort stops = tm.m_lines.m_buffer[i].m_stops;
                        float len = tm.m_lines.m_buffer[i].m_totalLength;
                        ushort infoIndex = tm.m_lines.m_buffer[i].m_infoIndex;

                        bool invalid = false;
                        if ((tm.m_lines.m_buffer[i].m_flags & TransportLine.Flags.Created) == 0)
                        {
                            invalid = true;
                        }
                        else if (infoIndex >= prefabCount || PrefabCollection<TransportInfo>.GetPrefab(infoIndex) == null)
                        {
                            invalid = true;
                        }
                        else if (float.IsNaN(len) || float.IsInfinity(len))
                        {
                            invalid = true;
                        }

                        if (invalid)
                        {
                            tm.m_lines.m_buffer[i].m_flags = TransportLine.Flags.None;
                            tm.m_lines.m_buffer[i].m_infoIndex = 0;
                            tm.m_lines.m_buffer[i].m_stops = 0;
                            tm.m_lines.m_buffer[i].m_totalLength = 0f;
                            tm.m_lines.m_buffer[i].m_vehicles = 0;
                            cleaned++;
                        }
                        else if (stops >= 65024)
                        {
                            tm.m_lines.m_buffer[i].m_stops = 0;
                            cleaned++;
                        }
                    }
                }
                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Sanitized TransportManager lines in AfterDeserialize: Cleaned {0} invalid line entries.", cleaned));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TransportManager_AfterDeserialize_Postfix error: " + ex);
            }
        }

        public static bool TransportLine_GetNextStop_Prefix(ushort stop, ref ushort __result)
        {
            if (stop == 0 || stop >= 65024)
            {
                __result = 0;
                return false;
            }
            return true;
        }

        public static bool IPT_Init_Prefix()
        {
            try
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] IPT_Init_Prefix: Ensuring all transport line stops and buffers are safe before IPT initializes...");
                SanitizeTransportLines();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] IPT_Init_Prefix error: " + ex);
            }
            return true;
        }
    }

    public class RepairRunner : UnityEngine.MonoBehaviour
    {
        private void Update()
        {
            bool ctrl = UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftControl) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightControl) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftCommand) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightCommand);
            bool shift = UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftShift) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightShift);
            if (ctrl && shift && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.R))
            {
                VehicleAndTransportRepair.RunFullRepair(true);
            }
            if (ctrl && shift && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.V))
            {
                VehicleAndTransportRepair.ClearAllVehicles();
            }
        }
    }
}


