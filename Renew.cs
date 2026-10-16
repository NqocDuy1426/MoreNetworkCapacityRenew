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
using UnityEngine;

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
    // ARRAY GUARDIAN - Centralized Array Management & Validation (v4.0.0)
    // ============================================================================
    public static class ArrayGuardian
    {
        // Array tracking registry
        private static Dictionary<string, ArrayInfo> arrayRegistry = new Dictionary<string, ArrayInfo>();
        private static bool initialized = false;
        private static readonly object expandLock = new object();
        
        // Diagnostic counters
        public static int TotalExpansions = 0;
        public static int TotalValidations = 0;
        public static int ShrinkageEvents = 0;
        
        private class ArrayInfo
        {
            public Type Type;
            public string FieldName;
            public int TargetCapacity;
            public int LastKnownCapacity;
        }
        
        public static void Initialize()
        {
            if (initialized) return;
            
            try
            {
                // Register TM:PE Flags arrays (critical for IndexOutOfRangeException fix)
                RegisterArray("TrafficManager.State.Flags", "laneArrowFlags", 524288);
                RegisterArray("TrafficManager.State.Flags", "highwayLaneArrowFlags", 524288);
                RegisterArray("TrafficManager.State.Flags", "laneAllowedVehicleTypesArray", 65024);
                
                // Register TM:PE live extension buffers (must match live NetManager arrays)
                RegisterArray("TrafficManager.Util.Extensions.NetLaneExtensions", "_laneBuffer", 262144);
                RegisterArray("TrafficManager.Util.Extensions.NetSegmentExtensions", "_segBuffer", 65536);
                RegisterArray("TrafficManager.Util.Extensions.NetNodeExtensions", "_nodeBuffer", 65536);

                // Register game network arrays
                RegisterArray("NetManager", "m_segments", 65536);
                RegisterArray("NetManager", "m_nodes", 65536);
                RegisterArray("NetManager", "m_lanes", 262144);
                
                initialized = true;
                UnityEngine.Debug.Log(string.Format("[ArrayGuardian] Initialized tracking for {0} arrays", arrayRegistry.Count));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[ArrayGuardian] Initialize failed: " + ex);
            }
        }
        
        private static void RegisterArray(string typeName, string fieldName, int targetCapacity)
        {
            string key = typeName + "." + fieldName;
            arrayRegistry[key] = new ArrayInfo
            {
                Type = null,
                FieldName = fieldName,
                TargetCapacity = targetCapacity,
                LastKnownCapacity = 0
            };
        }
        
        public static bool ExpandAllArrays()
        {
            if (!initialized)
            {
                Initialize();
            }
            
            DiagnosticLogger.Checkpoint("ArrayGuardian.ExpandAll.Start");
            
            int successCount = 0;
            int failCount = 0;
            
            // Expand TM:PE Flags arrays and sync extension buffers first (highest priority)
            if (ExpandTMPEFlagsArrays())
            {
                successCount += 3;
            }
            else
            {
                failCount += 3;
            }
            
            UnityEngine.Debug.Log(string.Format("[ArrayGuardian] Expansion complete: {0} success, {1} failures", successCount, failCount));
            DiagnosticLogger.Checkpoint("ArrayGuardian.ExpandAll.Complete");
            
            return failCount == 0;
        }
        
        private static bool ExpandTMPEFlagsArrays()
        {
            try
            {
                Assembly tmpe = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "TrafficManager")
                    {
                        tmpe = asm;
                        break;
                    }
                }
                
                if (tmpe == null)
                {
                    return true; // TM:PE not loaded yet; not an error
                }
                
                Type flagsType = tmpe.GetType("TrafficManager.State.Flags");
                if (flagsType == null)
                {
                    return false;
                }
                
                // 1. Expand Flags arrays
                bool success = true;
                success &= ExpandStaticArray(flagsType, "laneArrowFlags", 524288);
                success &= ExpandStaticArray(flagsType, "highwayLaneArrowFlags", 524288);
                success &= ExpandStaticArray(flagsType, "laneAllowedVehicleTypesArray", 65024);
                
                // 2. Synchronize live game buffers to TM:PE extension buffers
                NetManager nm = Singleton<NetManager>.instance;
                if (nm != null)
                {
                    if (nm.m_lanes != null && nm.m_lanes.m_buffer != null)
                        SetStaticBuffer(tmpe, "TrafficManager.Util.Extensions.NetLaneExtensions", "_laneBuffer", nm.m_lanes.m_buffer, 262144);
                    if (nm.m_segments != null && nm.m_segments.m_buffer != null)
                        SetStaticBuffer(tmpe, "TrafficManager.Util.Extensions.NetSegmentExtensions", "_segBuffer", nm.m_segments.m_buffer, 65536);
                    if (nm.m_nodes != null && nm.m_nodes.m_buffer != null)
                        SetStaticBuffer(tmpe, "TrafficManager.Util.Extensions.NetNodeExtensions", "_nodeBuffer", nm.m_nodes.m_buffer, 65536);
                }

                return success;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[ArrayGuardian] ExpandTMPEFlagsArrays failed: " + ex);
                return false;
            }
        }
        
        private static void SetStaticBuffer(Assembly asm, string typeName, string fieldName, object liveBuffer, int expectedMinSize)
        {
            try
            {
                Type t = asm.GetType(typeName);
                if (t != null && liveBuffer is Array buf && buf.Length >= expectedMinSize)
                {
                    FieldInfo f = t.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (f != null)
                    {
                        object cur = f.GetValue(null);
                        if (cur != liveBuffer)
                        {
                            f.SetValue(null, liveBuffer);
                            UnityEngine.Debug.Log(string.Format("[ArrayGuardian] Synchronized {0}.{1} to live buffer ({2} elements) ✓", typeName, fieldName, buf.Length));
                        }
                    }
                }
            }
            catch { }
        }

        private static bool ExpandStaticArray(Type type, string fieldName, int targetSize)
        {
            try
            {
                lock (expandLock)
                {
                    FieldInfo field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
                    if (field == null)
                    {
                        UnityEngine.Debug.LogWarning(string.Format("[ArrayGuardian] Field {0}.{1} not found", type.Name, fieldName));
                        return false;
                    }
                    
                    object arrayObj = field.GetValue(null);
                    if (arrayObj == null || !(arrayObj is Array))
                    {
                        UnityEngine.Debug.LogWarning(string.Format("[ArrayGuardian] {0}.{1} is not an array", type.Name, fieldName));
                        return false;
                    }
                    
                    Array currentArray = (Array)arrayObj;
                    int currentLength = currentArray.Length;
                    
                    if (currentLength >= targetSize)
                    {
                        return true;
                    }
                    
                    // Create expanded array
                    Type elementType = currentArray.GetType().GetElementType();
                    Array expandedArray = Array.CreateInstance(elementType, targetSize);
                    
                    // Copy existing data
                    Array.Copy(currentArray, expandedArray, currentLength);
                    
                    // Set expanded array
                    field.SetValue(null, expandedArray);
                    
                    TotalExpansions++;
                    
                    UnityEngine.Debug.Log(string.Format("[ArrayGuardian] Expanded {0}.{1}: {2} → {3} ✓", type.Name, fieldName, currentLength, targetSize));
                    return true;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(string.Format("[ArrayGuardian] ExpandStaticArray {0}.{1} failed: {2}", type.Name, fieldName, ex));
                return false;
            }
        }
        
        public static bool ValidateAllArrays()
        {
            if (!initialized) return false;
            
            TotalValidations++;
            
            int validCount = 0;
            int totalCount = 0;
            
            // Validate TM:PE Flags arrays
            if (ValidateTMPEFlagsArrays())
            {
                validCount += 3;
            }
            totalCount += 3;
            
            UnityEngine.Debug.Log(string.Format("[ArrayGuardian] Validation: {0}/{1} arrays OK ✓", validCount, totalCount));
            
            return validCount == totalCount;
        }
        
        private static bool ValidateTMPEFlagsArrays()
        {
            try
            {
                Assembly tmpe = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "TrafficManager")
                    {
                        tmpe = asm;
                        break;
                    }
                }
                
                if (tmpe == null) return true;
                
                Type flagsType = tmpe.GetType("TrafficManager.State.Flags");
                if (flagsType == null) return true;
                
                bool valid = true;
                valid &= ValidateStaticArray(flagsType, "laneArrowFlags", 524288);
                valid &= ValidateStaticArray(flagsType, "highwayLaneArrowFlags", 524288);
                valid &= ValidateStaticArray(flagsType, "laneAllowedVehicleTypesArray", 65024);
                
                return valid;
            }
            catch
            {
                return false;
            }
        }
        
        private static bool ValidateStaticArray(Type type, string fieldName, int expectedSize)
        {
            try
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
                if (field == null) return false;
                
                object arrayObj = field.GetValue(null);
                if (arrayObj == null || !(arrayObj is Array)) return false;
                
                Array array = (Array)arrayObj;
                return array.Length >= expectedSize;
            }
            catch
            {
                return false;
            }
        }
        
        private static Assembly _cachedTmpe = null;
        private static Type _cachedFlagsType = null;
        private static FieldInfo _cachedFieldArrows = null;
        private static FieldInfo _cachedFieldHwy = null;
        private static FieldInfo _cachedFieldTypes = null;
        private static FieldInfo _cachedFieldBuf = null;

        public static bool HasShrinkage()
        {
            try
            {
                if (_cachedTmpe == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (asm.GetName().Name == "TrafficManager")
                        {
                            _cachedTmpe = asm;
                            break;
                        }
                    }
                }
                
                if (_cachedTmpe == null) return false;
                
                if (_cachedFlagsType == null)
                    _cachedFlagsType = _cachedTmpe.GetType("TrafficManager.State.Flags");
                if (_cachedFlagsType == null) return false;
                
                if (_cachedFieldArrows == null)
                    _cachedFieldArrows = _cachedFlagsType.GetField("laneArrowFlags", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
                if (_cachedFieldArrows != null)
                {
                    Array arr = _cachedFieldArrows.GetValue(null) as Array;
                    if (arr != null && arr.Length < 524288)
                    {
                        ShrinkageEvents++;
                        return true;
                    }
                }

                if (_cachedFieldHwy == null)
                    _cachedFieldHwy = _cachedFlagsType.GetField("highwayLaneArrowFlags", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
                if (_cachedFieldHwy != null)
                {
                    Array arr = _cachedFieldHwy.GetValue(null) as Array;
                    if (arr != null && arr.Length < 524288)
                    {
                        ShrinkageEvents++;
                        return true;
                    }
                }

                if (_cachedFieldTypes == null)
                    _cachedFieldTypes = _cachedFlagsType.GetField("laneAllowedVehicleTypesArray", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
                if (_cachedFieldTypes != null)
                {
                    Array arr = _cachedFieldTypes.GetValue(null) as Array;
                    if (arr != null && arr.Length < 65024)
                    {
                        ShrinkageEvents++;
                        return true;
                    }
                }

                if (_cachedFieldBuf == null)
                {
                    Type laneExt = _cachedTmpe.GetType("TrafficManager.Util.Extensions.NetLaneExtensions");
                    if (laneExt != null)
                        _cachedFieldBuf = laneExt.GetField("_laneBuffer", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
                }
                if (_cachedFieldBuf != null)
                {
                    Array arr = _cachedFieldBuf.GetValue(null) as Array;
                    if (arr != null && arr.Length < 262144)
                    {
                        ShrinkageEvents++;
                        return true;
                    }
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        public static bool RepairAllArrays()
        {
            UnityEngine.Debug.Log("[ArrayGuardian] RepairAllArrays triggered");
            bool res = ExpandAllArrays();
            try
            {
                TMPECompatibility.SynchronizeTMPEBuffersAndArrays("ArrayGuardian.Repair");
            }
            catch { }
            return res;
        }
        
        public static string GetValidationStatus()
        {
            return string.Format("{0} expansions, {1} validations, {2} shrinkage events",
                TotalExpansions, TotalValidations, ShrinkageEvents);
        }
    }

    // ============================================================================
    // SAFE FLAGS ACCESSOR - Bounds-Checked Array Access Wrapper (v4.0.0)
    // ============================================================================
    public static class SafeFlagsAccessor
    {
        private static long totalAccesses = 0;
        private static long boundsViolations = 0;
        private static long autoExpansions = 0;
        
        private static Type flagsType = null;
        private static bool initialized = false;
        
        public static void Initialize()
        {
            if (initialized) return;
            
            try
            {
                Assembly tmpe = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "TrafficManager")
                    {
                        tmpe = asm;
                        break;
                    }
                }
                
                if (tmpe != null)
                {
                    flagsType = tmpe.GetType("TrafficManager.State.Flags");
                    if (flagsType != null)
                    {
                        UnityEngine.Debug.Log("[SafeFlagsAccessor] Successfully initialized with TM:PE Flags type");
                        initialized = true;
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[SafeFlagsAccessor] Initialize failed: " + ex);
            }
        }
        
        public static bool CanHaveLaneArrows(uint laneId, bool? highway = null)
        {
            totalAccesses++;
            
            if (!initialized || flagsType == null)
            {
                return false;
            }
            
            try
            {
                if (!ValidateLaneIndex(laneId, "CanHaveLaneArrows"))
                {
                    return false;
                }
                
                MethodInfo method = flagsType.GetMethod("CanHaveLaneArrows", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    
                if (method != null)
                {
                    object[] parameters = new object[] { laneId, highway };
                    return (bool)method.Invoke(null, parameters);
                }
            }
            catch (IndexOutOfRangeException ex)
            {
                HandleBoundsViolation(laneId, "CanHaveLaneArrows", ex);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(string.Format("[SafeFlagsAccessor] CanHaveLaneArrows({0}) error: {1}", laneId, ex));
            }
            
            return false;
        }

        public static bool ApplyLaneArrowFlags(uint laneId, bool check = false)
        {
            totalAccesses++;
            
            if (!initialized || flagsType == null)
            {
                return false;
            }
            
            try
            {
                if (!ValidateLaneIndex(laneId, "ApplyLaneArrowFlags"))
                {
                    return false;
                }
                
                MethodInfo method = flagsType.GetMethod("ApplyLaneArrowFlags", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    
                if (method != null)
                {
                    object[] parameters = new object[] { laneId, check };
                    return (bool)method.Invoke(null, parameters);
                }
            }
            catch (IndexOutOfRangeException ex)
            {
                HandleBoundsViolation(laneId, "ApplyLaneArrowFlags", ex);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(string.Format("[SafeFlagsAccessor] ApplyLaneArrowFlags({0}) error: {1}", laneId, ex));
            }
            
            return false;
        }
        
        private static bool ValidateLaneIndex(uint laneId, string operation)
        {
            if (laneId >= NetManager.MAX_LANE_COUNT)
            {
                boundsViolations++;
                return false;
            }
            return true;
        }
        
        private static void HandleBoundsViolation(uint index, string operation, Exception ex)
        {
            boundsViolations++;
        }
        
        public static string GetDiagnostics()
        {
            return string.Format(
                "SafeFlagsAccessor: {0} accesses, {1} bounds violations ({2:F2}%), {3} auto-expansions",
                totalAccesses,
                boundsViolations,
                totalAccesses > 0 ? (boundsViolations * 100.0 / totalAccesses) : 0,
                autoExpansions);
        }
        
        public static void LogStatus()
        {
            UnityEngine.Debug.Log(GetDiagnostics());
            
            if (boundsViolations > 0)
            {
                UnityEngine.Debug.LogWarning(string.Format(
                    "[SafeFlagsAccessor] WARNING: {0} bounds violations detected - array expansion may be needed",
                    boundsViolations));
            }
        }
    }

    // ============================================================================
    // ARRAY GUARDIAN V2 - Real-Time Monitoring with Background Thread
    // ============================================================================
    /// <summary>
    /// Monitors critical arrays in background thread every 5 seconds.
    /// Auto-repairs shrinkage and prevents IndexOutOfRangeException.
    /// NEW in v4.1: Monitors SpeedLimitManager.speedLimitArray
    /// </summary>
    public static class ArrayGuardianV2
    {
        // ========================================================================
        // FIELDS
        // ========================================================================
        private static System.Threading.Thread monitorThread = null;
        private static bool isMonitoring = false;
        private static bool stopRequested = false;
        private static readonly object monitorLock = new object();
        
        // Configuration
        private static readonly int checkIntervalMs = 5000; // 5 seconds
        
        // Diagnostics
        private static DateTime lastCheckTime = DateTime.MinValue;
        private static int checksPerformed = 0;
        private static int repairsTriggered = 0;
        private static int speedLimitRepairs = 0;
        
        // Cached references for performance (avoid reflection every check)
        private static Assembly cachedTMPE = null;
        private static Type cachedFlagsType = null;
        private static Type cachedSpeedLimitManagerType = null;
        private static FieldInfo cachedLaneSpeedLimitsField = null;
        private static object cachedSpeedLimitManagerInstance = null;
        
        // ========================================================================
        // PUBLIC API
        // ========================================================================
        
        /// <summary>
        /// Start background monitoring thread. Call from RenewMod.OnEnabled()
        /// </summary>
        public static void StartMonitoring()
        {
            if (isMonitoring)
            {
                UnityEngine.Debug.Log("[ArrayGuardianV2] Already monitoring");
                return;
            }
            
            try
            {
                stopRequested = false;
                isMonitoring = true;
                
                monitorThread = new System.Threading.Thread(MonitorLoop);
                monitorThread.IsBackground = true; // Won't prevent app exit
                monitorThread.Priority = System.Threading.ThreadPriority.BelowNormal;
                monitorThread.Name = "ArrayGuardianV2_Monitor";
                monitorThread.Start();
                
                UnityEngine.Debug.Log("[ArrayGuardianV2] Monitoring started (check every 5s)");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[ArrayGuardianV2] StartMonitoring failed: " + ex);
                isMonitoring = false;
            }
        }
        
        /// <summary>
        /// Stop background monitoring thread. Call from OnLevelUnloading()
        /// </summary>
        public static void StopMonitoring()
        {
            if (!isMonitoring)
            {
                return;
            }
            
            try
            {
                stopRequested = true;
                
                // Wait max 2 seconds for thread to finish
                if (monitorThread != null && monitorThread.IsAlive)
                {
                    monitorThread.Join(2000);
                }
                
                isMonitoring = false;
                monitorThread = null;
                
                UnityEngine.Debug.Log(string.Format(
                    "[ArrayGuardianV2] Monitoring stopped. Stats: {0} checks, {1} repairs, {2} speed limit fixes",
                    checksPerformed, repairsTriggered, speedLimitRepairs));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[ArrayGuardianV2] StopMonitoring error: " + ex);
            }
        }
        
        /// <summary>
        /// Get current monitoring status for diagnostics UI
        /// </summary>
        public static string GetStatus()
        {
            if (!isMonitoring)
            {
                return "Monitoring: Stopped";
            }
            
            double secondsSinceLastCheck = (DateTime.Now - lastCheckTime).TotalSeconds;
            return string.Format(
                "Monitoring: Active | Last check: {0:F0}s ago | Checks: {1} | Repairs: {2} | Speed fixes: {3}",
                secondsSinceLastCheck, checksPerformed, repairsTriggered, speedLimitRepairs);
        }
        
        // ========================================================================
        // BACKGROUND MONITORING LOOP
        // ========================================================================
        
        private static void MonitorLoop()
        {
            UnityEngine.Debug.Log("[ArrayGuardianV2] Monitor thread started (ID: " + System.Threading.Thread.CurrentThread.ManagedThreadId + ")");
            
            while (!stopRequested)
            {
                try
                {
                    // Wait first to avoid immediate check on start
                    System.Threading.Thread.Sleep(checkIntervalMs);
                    
                    if (stopRequested) break;
                    
                    // Perform checks
                    lock (monitorLock)
                    {
                        PerformMonitoringChecks();
                    }
                }
                catch (System.Threading.ThreadAbortException)
                {
                    // Normal shutdown, exit gracefully
                    break;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError("[ArrayGuardianV2] Monitor loop error: " + ex);
                    // Continue monitoring despite errors
                }
            }
            
            UnityEngine.Debug.Log("[ArrayGuardianV2] Monitor thread stopped");
        }
        
        private static void PerformMonitoringChecks()
        {
            lastCheckTime = DateTime.Now;
            checksPerformed++;
            
            bool needsRepair = false;
            
            // Check 1: Existing ArrayGuardian shrinkage detection
            if (ArrayGuardian.HasShrinkage())
            {
                UnityEngine.Debug.LogWarning("[ArrayGuardianV2] Shrinkage detected in tracked arrays!");
                needsRepair = true;
            }
            
            // Check 2: Speed limit array (CRITICAL - prevents stuck vehicles)
            if (CheckSpeedLimitArray())
            {
                needsRepair = true;
            }
            
            // Check 3: TM:PE Flags arrays
            if (CheckTMPEFlagsArrays())
            {
                needsRepair = true;
            }
            
            // Trigger repair if any check failed
            if (needsRepair)
            {
                repairsTriggered++;
                PerformAutoRepair();
            }
        }
        
        // ========================================================================
        // CHECK: SPEED LIMIT ARRAY (Most Critical)
        // ========================================================================
        
        /// <summary>
        /// Check SpeedLimitManager.cachedLaneSpeedLimits_ size.
        /// This is THE critical check that prevents stuck vehicles.
        /// Returns true if repair needed.
        /// </summary>
        private static bool CheckSpeedLimitArray()
        {
            try
            {
                // Cache TMPE assembly reference
                if (cachedTMPE == null)
                {
                    cachedTMPE = TMPECompatibility.GetTMPEAssembly();
                }
                
                if (cachedTMPE == null)
                {
                    // TM:PE not loaded yet, not an error
                    return false;
                }
                
                // Cache SpeedLimitManager type
                if (cachedSpeedLimitManagerType == null)
                {
                    cachedSpeedLimitManagerType = cachedTMPE.GetType("TrafficManager.Manager.Impl.SpeedLimitManager");
                }
                
                if (cachedSpeedLimitManagerType == null)
                {
                    return false;
                }

                if (cachedSpeedLimitManagerInstance == null)
                {
                    cachedSpeedLimitManagerInstance = TMPECompatibility.GetManagerInstance(cachedSpeedLimitManagerType);
                }

                if (cachedSpeedLimitManagerInstance == null)
                {
                    return false;
                }
                
                // Cache cachedLaneSpeedLimits_ field
                if (cachedLaneSpeedLimitsField == null)
                {
                    cachedLaneSpeedLimitsField = cachedSpeedLimitManagerType.GetField("cachedLaneSpeedLimits_",
                        BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                }
                
                if (cachedLaneSpeedLimitsField == null)
                {
                    return false;
                }
                
                // Get current array
                float[] speedLimitArray = cachedLaneSpeedLimitsField.GetValue(cachedSpeedLimitManagerInstance) as float[];
                
                if (speedLimitArray == null)
                {
                    UnityEngine.Debug.LogWarning("[ArrayGuardianV2] cachedLaneSpeedLimits_ is null!");
                    return true; // Needs repair
                }
                
                // Check size
                const int REQUIRED_SIZE = 524288; // Must match lane count
                
                if (speedLimitArray.Length < REQUIRED_SIZE)
                {
                    UnityEngine.Debug.LogWarning(string.Format(
                        "[ArrayGuardianV2] CRITICAL: cachedLaneSpeedLimits_ shrunk! Current: {0}, Required: {1}",
                        speedLimitArray.Length, REQUIRED_SIZE));
                    return true; // Needs repair
                }
                
                // All OK
                return false;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[ArrayGuardianV2] CheckSpeedLimitArray error: " + ex);
                return false; // Don't trigger repair on error
            }
        }
        
        // ========================================================================
        // CHECK: TM:PE FLAGS ARRAYS
        // ========================================================================
        
        private static bool CheckTMPEFlagsArrays()
        {
            try
            {
                if (cachedTMPE == null || cachedFlagsType == null)
                {
                    if (cachedTMPE == null) cachedTMPE = TMPECompatibility.GetTMPEAssembly();
                    if (cachedTMPE != null)
                        cachedFlagsType = cachedTMPE.GetType("TrafficManager.State.Flags");
                }
                
                if (cachedFlagsType == null) return false;
                
                bool needsRepair = false;
                
                // Check laneArrowFlags (524,288 lanes)
                needsRepair |= !ValidateArraySize(cachedFlagsType, "laneArrowFlags", 524288);
                
                // Check highwayLaneArrowFlags (524,288 lanes)
                needsRepair |= !ValidateArraySize(cachedFlagsType, "highwayLaneArrowFlags", 524288);
                
                // Check laneAllowedVehicleTypesArray (65,024 segments)
                needsRepair |= !ValidateArraySize(cachedFlagsType, "laneAllowedVehicleTypesArray", 65024);
                
                return needsRepair;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[ArrayGuardianV2] CheckTMPEFlagsArrays error: " + ex);
                return false;
            }
        }
        
        private static bool ValidateArraySize(Type type, string fieldName, int requiredSize)
        {
            try
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
                if (field == null) return true; // Can't validate, assume OK
                
                Array array = field.GetValue(null) as Array;
                if (array == null) return false; // Needs repair
                
                if (array.Length < requiredSize)
                {
                    UnityEngine.Debug.LogWarning(string.Format(
                        "[ArrayGuardianV2] {0}.{1} shrunk: {2} < {3}",
                        type.Name, fieldName, array.Length, requiredSize));
                    return false; // Needs repair
                }
                
                return true; // OK
            }
            catch
            {
                return true; // Can't validate, assume OK
            }
        }
        
        // ========================================================================
        // REPAIR: AUTO-REPAIR ALL DETECTED ISSUES
        // ========================================================================
        
        private static void PerformAutoRepair()
        {
            try
            {
                UnityEngine.Debug.Log("[ArrayGuardianV2] Starting auto-repair...");
                
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                
                // Step 1: Repair tracked arrays via existing ArrayGuardian
                bool existingRepairOK = ArrayGuardian.RepairAllArrays();
                
                // Step 2: Repair speed limit array specifically
                bool speedLimitOK = RepairSpeedLimitArray();
                if (speedLimitOK)
                {
                    speedLimitRepairs++;
                }
                
                // Step 3: Sync TM:PE buffers to ensure consistency
                try
                {
                    TMPECompatibility.SynchronizeTMPEBuffersAndArrays("ArrayGuardianV2.AutoRepair");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError("[ArrayGuardianV2] Buffer sync error: " + ex);
                }
                
                sw.Stop();
                
                UnityEngine.Debug.Log(string.Format(
                    "[ArrayGuardianV2] Auto-repair completed in {0}ms. Tracked arrays: {1}, Speed limit: {2}",
                    sw.ElapsedMilliseconds, existingRepairOK ? "OK" : "FAILED", speedLimitOK ? "OK" : "FAILED"));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[ArrayGuardianV2] PerformAutoRepair error: " + ex);
            }
        }
        
        /// <summary>
        /// Repair speed limit array: ensure cachedLaneSpeedLimits_ capacity and defaults
        /// </summary>
        private static bool RepairSpeedLimitArray()
        {
            try
            {
                if (cachedTMPE == null)
                {
                    cachedTMPE = TMPECompatibility.GetTMPEAssembly();
                }
                
                if (cachedTMPE != null)
                {
                    TMPECompatibility.EnsureSpeedLimitManagerCapacity(cachedTMPE);
                    UnityEngine.Debug.Log("[ArrayGuardianV2] Restored and expanded TM:PE speed limit capacity to 524,288 lanes.");
                    return true;
                }
                
                UnityEngine.Debug.LogWarning("[ArrayGuardianV2] Cannot repair speed limit: TM:PE assembly not found.");
                return false;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[ArrayGuardianV2] RepairSpeedLimitArray error: " + ex);
                return false;
            }
        }
        
        // ========================================================================
        // DIAGNOSTICS & CLEANUP
        // ========================================================================
        
        /// <summary>
        /// Reset all statistics (for testing)
        /// </summary>
        public static void ResetStats()
        {
            checksPerformed = 0;
            repairsTriggered = 0;
            speedLimitRepairs = 0;
            lastCheckTime = DateTime.MinValue;
        }
        
        /// <summary>
        /// Force immediate check (for manual trigger from console)
        /// </summary>
        public static void ForceCheck()
        {
            if (!isMonitoring)
            {
                UnityEngine.Debug.LogWarning("[ArrayGuardianV2] Not monitoring, cannot force check");
                return;
            }
            
            try
            {
                lock (monitorLock)
                {
                    UnityEngine.Debug.Log("[ArrayGuardianV2] Manual check triggered");
                    PerformMonitoringChecks();
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[ArrayGuardianV2] ForceCheck error: " + ex);
            }
        }
    }

    // ============================================================================
    // MOD INFO (IUserMod)
    // ============================================================================
    public class RenewMod : IUserMod
    {
        public string Name => "More Network Capacity Renew v4.2.4 by NgocDuy";
        public string Description => "Expands network node and segment capacity to 64K with high simulation FPS, instant vehicle despawn/reroute on demolition, TM:PE pathfinding & Mac stability. Renewed by NgocDuy.";

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
                // NEW v4.0.0: Initialize ArrayGuardian EARLY (before game systems init)
                DiagnosticLogger.Checkpoint("RenewMod.OnEnabled.Start");
                ArrayGuardian.Initialize();
                ArrayGuardian.ExpandAllArrays();
                ArrayGuardian.ValidateAllArrays();
                
                // NEW v4.0.0: Initialize SafeFlagsAccessor for bounds-checked array access
                SafeFlagsAccessor.Initialize();
                DiagnosticLogger.Checkpoint("RenewMod.ArrayGuardian.Complete");
                
                // NEW v4.1: Start real-time array monitoring
                ArrayGuardianV2.StartMonitoring();
                
                // NEW v4.1: Initialize diagnostics UI
                DiagnosticsUI.Initialize();
                
                DiagnosticLogger.Checkpoint("RenewMod.VehicleFixSystems.Complete");
                
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

                UIHelperBase trafficGroup = helper.AddGroup("Traffic & City Logistics Diagnostics (NgocDuy)");
                trafficGroup.AddButton("Reactivate City Logistics & Unblock Spawns (Ctrl+Shift+V)", () => {
                    VehicleAndTransportRepair.ReactivateCityLogistics();
                });
                trafficGroup.AddButton("Clear All Traffic & Reactivate Logistics", () => {
                    VehicleAndTransportRepair.ClearAllVehicles();
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

        private static Assembly cachedTMPEApiAssembly = null;

        public static Assembly GetTMPEApiAssembly()
        {
            if (cachedTMPEApiAssembly != null) return cachedTMPEApiAssembly;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == "TMPE.API")
                {
                    cachedTMPEApiAssembly = asm;
                    return asm;
                }
            }
            try
            {
                cachedTMPEApiAssembly = Assembly.Load("TMPE.API");
            }
            catch { }
            return cachedTMPEApiAssembly ?? GetTMPEAssembly();
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

                // 3-7 & 9. Hot-path methods (SegmentEndManager.GetIndex, TurnOnRedManager.GetIndex,
                // TrafficMeasurementManager.GetDirIndex, RoutingManager.GetLaneEndRoutingIndex,
                // TrafficLightSimulationManager.SimulationStep, SpeedLimitManager.GetGameSpeedLimit)
                // are handled 100% natively and efficiently by MoreNetworkCapacity.Core transpilers
                // without redundant Harmony prefix/postfix overhead.

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
                Type tlsm = tmpe.GetType("TrafficManager.Manager.Impl.TrafficLightSimulationManager");
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

                // 16. LaneArrowManager.LoadData & OnBeforeSaveData
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

                        MethodInfo onBeforeSave = lamType.GetMethod("OnBeforeSaveData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (onBeforeSave != null)
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.LaneArrowManager_OnBeforeSaveData_Prefix));
                            harmony.Patch(onBeforeSave, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE LaneArrowManager.OnBeforeSaveData (safe Prefix)");
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

                // 18. SpeedLimitManager.LoadData & GetGameSpeedLimit safe fallbacks
                try
                {
                    Type slm = tmpe.GetType("TrafficManager.Manager.Impl.SpeedLimitManager");
                    if (slm != null)
                    {
                        foreach (var m in slm.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        {
                            if (m.Name == "LoadData")
                            {
                                var post = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.GenericManager_LoadData_Postfix));
                                harmony.Patch(m, postfix: new HarmonyMethod(post));
                                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE SpeedLimitManager.LoadData");
                            }
                            else if (m.Name == "GetGameSpeedLimit")
                            {
                                var p = m.GetParameters();
                                if (p.Length == 2 && p[0].ParameterType == typeof(uint) && p[1].ParameterType == typeof(NetInfo.Lane))
                                {
                                    var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.SpeedLimitManager_GetGameSpeedLimit_Prefix));
                                    harmony.Patch(m, prefix: new HarmonyMethod(pre));
                                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE SpeedLimitManager.GetGameSpeedLimit(uint, NetInfo.Lane)");
                                }
                                else if (p.Length == 4 && p[0].ParameterType == typeof(ushort) && p[2].ParameterType == typeof(uint))
                                {
                                    var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.SpeedLimitManager_GetGameSpeedLimit4_Prefix));
                                    harmony.Patch(m, prefix: new HarmonyMethod(pre));
                                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE SpeedLimitManager.GetGameSpeedLimit(ushort, byte, uint, NetInfo.Lane)");
                                }
                                else if (p.Length == 1 && p[0].ParameterType == typeof(uint))
                                {
                                    var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.SpeedLimitManager_GetGameSpeedLimit1_Prefix));
                                    harmony.Patch(m, prefix: new HarmonyMethod(pre));
                                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE SpeedLimitManager.GetGameSpeedLimit(uint)");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching SpeedLimitManager: " + ex);
                }

                // 19. Flags (SetLaneAllowedVehicleTypes, CanHaveLaneArrows, ApplyLaneArrowFlags, RemoveLaneArrowFlags, ResetLaneArrowFlags, ValidateLaneCustomArrows)
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

                        MethodInfo canHave = flagsType.GetMethod("CanHaveLaneArrows", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (canHave != null)
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.Flags_CanHaveLaneArrows_Prefix));
                            harmony.Patch(canHave, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE Flags.CanHaveLaneArrows (safety Prefix)");
                        }

                        MethodInfo applyArrows = flagsType.GetMethod("ApplyLaneArrowFlags", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (applyArrows != null)
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.Flags_ApplyLaneArrowFlags_Prefix));
                            harmony.Patch(applyArrows, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE Flags.ApplyLaneArrowFlags (safety Prefix)");
                        }

                        MethodInfo remArrows = flagsType.GetMethod("RemoveLaneArrowFlags", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (remArrows != null)
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.Flags_RemoveLaneArrowFlags_Prefix));
                            harmony.Patch(remArrows, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE Flags.RemoveLaneArrowFlags (safety Prefix)");
                        }

                        MethodInfo resetArrows = flagsType.GetMethod("ResetLaneArrowFlags", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (resetArrows != null)
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.Flags_ResetLaneArrowFlags_Prefix));
                            harmony.Patch(resetArrows, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE Flags.ResetLaneArrowFlags (safety Prefix)");
                        }

                        MethodInfo valArrows = flagsType.GetMethod("ValidateLaneCustomArrows", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (valArrows != null)
                        {
                            var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.Flags_ValidateLaneCustomArrows_Prefix));
                            harmony.Patch(valArrows, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE Flags.ValidateLaneCustomArrows (safety Prefix)");
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

                // 23. CustomPathFind (Awake capacity expansion only; inner-loop pathfinder methods run at full native speed)
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
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching CustomPathFind: " + ex);
                }

                // 27. UtilityManager.ClearTraffic & DespawnVehicles (City Logistics Reactivator & Traffic Spawning unblocker)
                try
                {
                    Type umType = tmpe.GetType("TrafficManager.Manager.Impl.UtilityManager");
                    if (umType != null)
                    {
                        var post = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.UtilityManager_DespawnVehicles_Postfix));
                        MethodInfo clearM = umType.GetMethod("ClearTraffic", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (clearM != null)
                        {
                            harmony.Patch(clearM, postfix: new HarmonyMethod(post));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE UtilityManager.ClearTraffic with Postfix logistics reactivator.");
                        }

                        MethodInfo[] methods = umType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        foreach (var m in methods)
                        {
                            if (m.Name == "DespawnVehicles")
                            {
                                harmony.Patch(m, postfix: new HarmonyMethod(post));
                                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TM:PE UtilityManager.DespawnVehicles with Postfix logistics reactivator.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching UtilityManager: " + ex);
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
                    if (nm.m_segments != null && nm.m_segments.m_buffer != null) SetStaticField(tmpe, "TrafficManager.Util.Extensions.NetSegmentExtensions", "_segBuffer", nm.m_segments.m_buffer);
                    if (nm.m_nodes != null && nm.m_nodes.m_buffer != null) SetStaticField(tmpe, "TrafficManager.Util.Extensions.NetNodeExtensions", "_nodeBuffer", nm.m_nodes.m_buffer);
                    if (nm.m_lanes != null && nm.m_lanes.m_buffer != null) SetStaticField(tmpe, "TrafficManager.Util.Extensions.NetLaneExtensions", "_laneBuffer", nm.m_lanes.m_buffer);
                }

                BuildingManager bm = Singleton<BuildingManager>.instance;
                if (bm != null && bm.m_buildings != null && bm.m_buildings.m_buffer != null)
                {
                    SetStaticField(tmpe, "TrafficManager.Util.Extensions.BuildingExtensions", "_buildingBuffer", bm.m_buildings.m_buffer);
                }

                VehicleManager vm = Singleton<VehicleManager>.instance;
                if (vm != null)
                {
                    if (vm.m_vehicles != null && vm.m_vehicles.m_buffer != null) SetStaticField(tmpe, "TrafficManager.Util.Extensions.VehicleExtensions", "_vehicleBuffer", vm.m_vehicles.m_buffer);
                    if (vm.m_parkedVehicles != null && vm.m_parkedVehicles.m_buffer != null) SetStaticField(tmpe, "TrafficManager.Util.Extensions.ParkedVehicleExtensions", "_parkedVehiclesBuffer", vm.m_parkedVehicles.m_buffer);
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
                EnsureSavedGameOptions(tmpe);
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

                // 5c. ExtLaneManager capacity (expand from 262,144 to 524,288 lanes) & Cache ExtVehicle fields
                EnsureExtLaneManagerCapacity(tmpe);
                CacheExtVehicleFields(tmpe);

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
        public static int s_laneTrafficDataLength = 65024;

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

        public static void EnsureSavedGameOptions(Assembly tmpe)
        {
            try
            {
                if (tmpe == null) return;
                Type sgoType = tmpe.GetType("TrafficManager.State.SavedGameOptions");
                if (sgoType == null) return;

                FieldInfo fInst = sgoType.GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);
                object inst = fInst != null ? fInst.GetValue(null) : null;
                if (inst == null)
                {
                    MethodInfo createM = sgoType.GetMethod("Create", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (createM != null)
                    {
                        createM.Invoke(null, null);
                    }
                    else
                    {
                        MethodInfo ensureM = sgoType.GetMethod("Ensure", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (ensureM != null)
                        {
                            ensureM.Invoke(null, null);
                        }
                        else
                        {
                            object newInst = Activator.CreateInstance(sgoType);
                            if (fInst != null) fInst.SetValue(null, newInst);
                        }
                    }
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Initialized TM:PE SavedGameOptions.Instance");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureSavedGameOptions error: " + ex);
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
                if (fLane != null && fLane.GetValue(inst) is Array arrLane)
                {
                    if (arrLane.Length < 65024)
                    {
                        Array newArr = Array.CreateInstance(arrLane.GetType().GetElementType(), 65024);
                        Array.Copy(arrLane, newArr, arrLane.Length);
                        fLane.SetValue(inst, newArr);
                        s_laneTrafficDataLength = 65024;
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized TrafficMeasurementManager.LaneTrafficData: {0} -> 65024", arrLane.Length));
                    }
                    else
                    {
                        s_laneTrafficDataLength = arrLane.Length;
                    }
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

        private static Array s_extVehiclesArray = null;
        private static FieldInfo s_extVehicleJunctionTransitStateField = null;
        private static FieldInfo s_extVehicleWaitTimeField = null;
        private static FieldInfo s_extVehicleLastTransitStateUpdateField = null;

        public static void CacheExtVehicleFields(Assembly tmpe)
        {
            try
            {
                if (tmpe == null) return;
                Type evmType = tmpe.GetType("TrafficManager.Manager.Impl.ExtVehicleManager");
                if (evmType == null) return;

                FieldInfo instField = evmType.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object inst = (instField != null) ? instField.GetValue(null) : null;
                if (inst == null) return;

                PropertyInfo extVehiclesProp = evmType.GetProperty("ExtVehicles", BindingFlags.Public | BindingFlags.Instance);
                if (extVehiclesProp != null)
                {
                    s_extVehiclesArray = extVehiclesProp.GetValue(inst, null) as Array;
                }

                Assembly apiAsm = GetTMPEApiAssembly() ?? tmpe;
                Type extVehicleType = apiAsm.GetType("TrafficManager.API.Traffic.Data.ExtVehicle");
                if (extVehicleType != null)
                {
                    s_extVehicleJunctionTransitStateField = extVehicleType.GetField("junctionTransitState", BindingFlags.Public | BindingFlags.Instance);
                    s_extVehicleWaitTimeField = extVehicleType.GetField("waitTime", BindingFlags.Public | BindingFlags.Instance);
                    s_extVehicleLastTransitStateUpdateField = extVehicleType.GetField("lastTransitStateUpdate", BindingFlags.Public | BindingFlags.Instance);
                }
            }
            catch { }
        }

        public static void UnstickVehicle(ushort vehicleId)
        {
            try
            {
                if (s_extVehiclesArray != null && vehicleId < s_extVehiclesArray.Length && s_extVehicleJunctionTransitStateField != null)
                {
                    object extVeh = s_extVehiclesArray.GetValue(vehicleId);
                    if (extVeh != null)
                    {
                        s_extVehicleJunctionTransitStateField.SetValue(extVeh, 3); // Leave (3)
                        if (s_extVehicleWaitTimeField != null) s_extVehicleWaitTimeField.SetValue(extVeh, 0);
                        if (s_extVehicleLastTransitStateUpdateField != null) s_extVehicleLastTransitStateUpdateField.SetValue(extVeh, 0u);
                        s_extVehiclesArray.SetValue(extVeh, vehicleId);
                    }
                }
            }
            catch { }
        }

        public static void EnsureExtLaneManagerCapacity(Assembly tmpe)
        {
            try
            {
                if (tmpe == null) return;
                Type elmType = tmpe.GetType("TrafficManager.Manager.Impl.ExtLaneManager");
                if (elmType == null) return;

                Type extLaneType = tmpe.GetType("TrafficManager.Manager.Impl.ExtLaneManager+ExtLane") ?? tmpe.GetType("TrafficManager.Manager.Impl.ExtLaneManager/ExtLane");
                if (extLaneType == null) return;

                FieldInfo instField = elmType.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object inst = (instField != null) ? instField.GetValue(null) : null;
                if (inst == null)
                {
                    PropertyInfo instProp = elmType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    if (instProp != null) inst = instProp.GetValue(null, null);
                }
                if (inst == null) return;

                FieldInfo lanesField = elmType.GetField("lanes", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (lanesField == null) return;

                Array oldLanes = lanesField.GetValue(inst) as Array;
                int targetSize = 524288;
                if (oldLanes == null || oldLanes.Length < targetSize)
                {
                    Array newLanes = Array.CreateInstance(extLaneType, targetSize);
                    int oldLen = 0;
                    if (oldLanes != null)
                    {
                        oldLen = oldLanes.Length;
                        Array.Copy(oldLanes, newLanes, oldLen);
                    }

                    FieldInfo laneIndexField = extLaneType.GetField("laneIndex", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    object item = Activator.CreateInstance(extLaneType);
                    if (laneIndexField != null)
                    {
                        laneIndexField.SetValue(item, -1);
                    }

                    for (int i = oldLen; i < targetSize; i++)
                    {
                        newLanes.SetValue(item, i);
                    }

                    lanesField.SetValue(inst, newLanes);
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Expanded TM:PE ExtLaneManager.lanes: {0} -> {1}", oldLen, targetSize));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error in EnsureExtLaneManagerCapacity: " + ex);
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

                    EnsureSavedGameOptions(tmpe);
                    EnsureSpeedLimitManagerCapacity(tmpe);
                    EnsureVehicleRestrictionsCapacity(tmpe);
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
                            int reqQueueLen = 524288;
                            PathManager pmInst = Singleton<PathManager>.instance;
                            if (pmInst != null && pmInst.m_pathUnits != null && pmInst.m_pathUnits.m_buffer != null)
                            {
                                reqQueueLen = Math.Max(reqQueueLen, pmInst.m_pathUnits.m_buffer.Length);
                            }
                            if (fQueue != null && fQueue.GetValue(cpmInst) is Array qArr && qArr.Length < reqQueueLen)
                            {
                                Array newQ = Array.CreateInstance(qArr.GetType().GetElementType(), reqQueueLen);
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

                if (NetManager.instance == null || NetManager.instance.m_nodes == null || NetManager.instance.m_nodes.m_buffer == null)
                    return true;

                int maxNode = Math.Min(65024, NetManager.instance.m_nodes.m_buffer.Length);
                NetNode[] nodeBuffer = NetManager.instance.m_nodes.m_buffer;

                for (int nodeId = 0; nodeId < maxNode; nodeId++)
                {
                    if ((nodeBuffer[nodeId].m_flags & NetNode.Flags.Created) != 0)
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

        public static bool JunctionRestrictionsTool_RefreshCurrentRestrictedNodeIds_Prefix(object __instance, ushort forceNodeId)
        {
            try
            {
                if (__instance == null) return false;
                Type t = __instance.GetType();
                FieldInfo fNodes = t.GetField("currentRestrictedNodeIds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fNodes == null) return true;
                var set = fNodes.GetValue(__instance) as HashSet<ushort>;
                if (set == null) return true;

                if (forceNodeId != 0)
                {
                    set.Remove(forceNodeId);
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

                if (NetManager.instance == null || NetManager.instance.m_nodes == null || NetManager.instance.m_nodes.m_buffer == null)
                    return true;

                int minNode = (forceNodeId == 0 ? 1 : (int)forceNodeId);
                int maxNode = (forceNodeId == 0 ? 65024 : (int)forceNodeId + 1);
                maxNode = Math.Min(maxNode, NetManager.instance.m_nodes.m_buffer.Length);
                NetNode[] nodeBuffer = NetManager.instance.m_nodes.m_buffer;

                for (int nid = minNode; nid < maxNode; nid++)
                {
                    if ((nodeBuffer[nid].m_flags & NetNode.Flags.Created) != 0)
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
                    TMPECompatibility.EnsureSavedGameOptions(tmpe);
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
                if (nodeId >= 32768) s_hasAnyUpperTimedLightSimulation = true;
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

        public static bool Flags_SetLaneAllowedVehicleTypes_Prefix()
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

        // ============================================================================
        // FLAGS & LANE ARROW HARMONY DEFENSE PATCHES (v4.0.0)
        // Eliminates IndexOutOfRangeException during CustomPathFind & Save
        // ============================================================================

        public static bool Flags_CanHaveLaneArrows_Prefix(uint laneId, ref bool __result)
        {
            try
            {
                if (laneId >= NetManager.MAX_LANE_COUNT)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
            catch
            {
                __result = false;
                return false;
            }
        }

        public static bool Flags_ApplyLaneArrowFlags_Prefix(uint laneId, bool check, ref bool __result)
        {
            try
            {
                if (laneId >= NetManager.MAX_LANE_COUNT)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
            catch
            {
                __result = false;
                return false;
            }
        }

        public static bool Flags_RemoveLaneArrowFlags_Prefix(uint laneId)
        {
            if (laneId >= 524288 || laneId >= NetManager.MAX_LANE_COUNT)
            {
                return false;
            }
            return true;
        }

        public static bool Flags_ResetLaneArrowFlags_Prefix(uint laneId, ref bool __result)
        {
            if (laneId >= 524288 || laneId >= NetManager.MAX_LANE_COUNT)
            {
                __result = false;
                return false;
            }
            return true;
        }

        public static bool Flags_ValidateLaneCustomArrows_Prefix(uint laneId)
        {
            if (laneId >= 524288 || laneId >= NetManager.MAX_LANE_COUNT)
            {
                return false;
            }
            return true;
        }

        public static bool LaneArrowManager_OnBeforeSaveData_Prefix(object __instance)
        {
            try
            {
                ArrayGuardian.ExpandAllArrays();
                if (__instance != null)
                {
                    MethodInfo applyFlags = __instance.GetType().GetMethod("ApplyFlags", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (applyFlags != null)
                    {
                        applyFlags.Invoke(__instance, null);
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] LaneArrowManager.OnBeforeSaveData exception safely handled: " + ex);
            }
            return false;
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
        public static bool s_hasAnyUpperTimedLightSimulation = false;

        public static void TrafficLightSimulationManager_SimulationStep_Postfix(object __instance)
        {
            if (!s_hasAnyUpperTimedLightSimulation || __instance == null) return;
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
                    TMPECompatibility.EnsureSavedGameOptions(tmpe);
                    TMPECompatibility.EnsureSpeedLimitManagerCapacity(tmpe);
                    TMPECompatibility.EnsureVehicleRestrictionsCapacity(tmpe);
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
            try
            {
                // 1. Check custom speed limits from TM:PE cache
                float[] cache = TMPECompatibility.s_cachedLaneSpeedLimits;
                if (cache != null && laneId < (uint)cache.Length)
                {
                    float cached = cache[laneId];
                    if (cached > 0.001f)
                    {
                        __result = cached;
                        return false;
                    }
                }

                // 2. Fall back to laneInfo default speed limit
                if (laneInfo != null && laneInfo.m_speedLimit > 0.001f)
                {
                    __result = laneInfo.m_speedLimit;
                    return false;
                }

                // 3. Fall back to NetSegment Info lane speed limit if laneInfo was null
                if (NetManager.instance != null && NetManager.instance.m_lanes != null && NetManager.instance.m_lanes.m_buffer != null &&
                    NetManager.instance.m_segments != null && NetManager.instance.m_segments.m_buffer != null &&
                    laneId < (uint)NetManager.instance.m_lanes.m_buffer.Length)
                {
                    ushort segId = NetManager.instance.m_lanes.m_buffer[laneId].m_segment;
                    if (segId != 0 && segId < NetManager.instance.m_segments.m_buffer.Length)
                    {
                        NetInfo segInfo = NetManager.instance.m_segments.m_buffer[segId].Info;
                        if (segInfo != null && segInfo.m_lanes != null)
                        {
                            uint curLane = NetManager.instance.m_segments.m_buffer[segId].m_lanes;
                            int laneIdx = 0;
                            while (curLane != 0 && curLane < (uint)NetManager.instance.m_lanes.m_buffer.Length && laneIdx < segInfo.m_lanes.Length)
                            {
                                if (curLane == laneId)
                                {
                                    NetInfo.Lane l = segInfo.m_lanes[laneIdx];
                                    if (l != null && l.m_speedLimit > 0.001f)
                                    {
                                        __result = l.m_speedLimit;
                                        return false;
                                    }
                                    break;
                                }
                                curLane = NetManager.instance.m_lanes.m_buffer[curLane].m_nextLane;
                                laneIdx++;
                            }
                        }
                    }
                }
            }
            catch { }

            // 4. Safe highway/road fallback so vehicles NEVER dead-stop
            __result = 1.25f;
            return false;
        }

        public static bool SpeedLimitManager_GetGameSpeedLimit4_Prefix(ushort segmentId, byte laneIndex, uint laneId, NetInfo.Lane laneInfo, ref float __result)
        {
            if (laneInfo == null && segmentId != 0 && NetManager.instance != null && NetManager.instance.m_segments != null && NetManager.instance.m_segments.m_buffer != null && segmentId < NetManager.instance.m_segments.m_buffer.Length)
            {
                NetInfo sInfo = NetManager.instance.m_segments.m_buffer[segmentId].Info;
                if (sInfo != null && sInfo.m_lanes != null && laneIndex < sInfo.m_lanes.Length)
                {
                    laneInfo = sInfo.m_lanes[laneIndex];
                }
            }
            return SpeedLimitManager_GetGameSpeedLimit_Prefix(laneId, laneInfo, ref __result);
        }

        public static bool SpeedLimitManager_GetGameSpeedLimit1_Prefix(uint laneId, ref float __result)
        {
            return SpeedLimitManager_GetGameSpeedLimit_Prefix(laneId, null, ref __result);
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


        public static bool VehicleAICommons_CustomCalculateTargetSpeed_Prefix(
            NetInfo info,
            uint laneId,
            ref float maxSpeed)
        {
            if (info == null || laneId == 0 || laneId >= 524288)
            {
                maxSpeed = 1f;
                return false;
            }
            return true;
        }

        public static bool TrafficMeasurementManager_AddTraffic_Prefix(ushort segmentId)
        {
            return segmentId < (ushort)TMPECompatibility.s_laneTrafficDataLength;
        }

        public static bool TrafficMeasurementManager_OnBeforeSimulationStep_Prefix(ushort segmentId)
        {
            return segmentId < (ushort)TMPECompatibility.s_laneTrafficDataLength;
        }

        public static bool TrafficMeasurementManager_DestroySegmentStats_Prefix(ushort segmentId)
        {
            return segmentId < (ushort)TMPECompatibility.s_laneTrafficDataLength;
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

        public static void VehicleBehaviorManager_MayDespawn_Postfix(ushort vehicleId, ref Vehicle vehicleData, ref bool __result)
        {
            try
            {
                // If already marked to despawn, honor it immediately
                if (__result) return;

                // Check created flag
                if ((vehicleData.m_flags & Vehicle.Flags.Created) == 0) return;

                NetManager nm = Singleton<NetManager>.instance;
                PathManager pm = Singleton<PathManager>.instance;
                uint pathId = vehicleData.m_path;

                // 1. Vehicle with path unit
                if (pathId != 0)
                {
                    if (pm != null && nm != null && pm.m_pathUnits != null && pm.m_pathUnits.m_buffer != null &&
                        nm.m_segments != null && nm.m_segments.m_buffer != null && pathId < pm.m_pathUnits.m_buffer.Length)
                    {
                        int posIdx = (int)(vehicleData.m_pathPositionIndex >> 1);
                        PathUnit.Position pPos;
                        if (pm.m_pathUnits.m_buffer[pathId].GetPosition(posIdx, out pPos))
                        {
                            ushort segId = pPos.m_segment;
                            if (segId == 0 || segId >= nm.m_segments.m_buffer.Length ||
                                (nm.m_segments.m_buffer[segId].m_flags & NetSegment.Flags.Created) == 0)
                            {
                                // Road segment under path is demolished / missing -> DESPAWN!
                                __result = true;
                                return;
                            }
                        }
                        else
                        {
                            __result = true;
                            return;
                        }

                        PathUnit.Position nextPos;
                        if (pm.m_pathUnits.m_buffer[pathId].GetNextPosition(posIdx, out nextPos))
                        {
                            ushort nextSegId = nextPos.m_segment;
                            if (nextSegId != 0 && (nextSegId >= nm.m_segments.m_buffer.Length ||
                                (nm.m_segments.m_buffer[nextSegId].m_flags & NetSegment.Flags.Created) == 0))
                            {
                                // Next segment in route is demolished -> DESPAWN!
                                __result = true;
                                return;
                            }
                        }

                        // If vehicle has been blocked for too long (blockCounter >= 100), allow it to despawn!
                        if (vehicleData.m_blockCounter >= 100)
                        {
                            __result = true;
                            return;
                        }

                        // If trapped in zero-velocity but recently blocked, try light unstick
                        if (vehicleData.m_targetPos0.w < 0.1f && vehicleData.GetLastFrameVelocity().sqrMagnitude < 0.001f && vehicleData.m_blockCounter < 50)
                        {
                            vehicleData.m_targetPos0.w = 1.5f;
                            vehicleData.m_targetPos1.w = 1.5f;
                            vehicleData.m_targetPos2.w = 1.5f;
                            vehicleData.m_targetPos3.w = 1.5f;

                            TMPECompatibility.UnstickVehicle(vehicleId);
                        }
                    }
                }
                else
                {
                    // 2. Vehicle without path unit
                    if ((vehicleData.m_flags & Vehicle.Flags.Spawned) != 0 &&
                        (vehicleData.m_flags & (Vehicle.Flags.WaitingPath | Vehicle.Flags.Parking | Vehicle.Flags.WaitingLoading)) == 0)
                    {
                        // Vehicle on road without path: if blocked or completely motionless, allow despawn!
                        if (vehicleData.m_blockCounter >= 60 || vehicleData.GetLastFrameVelocity().sqrMagnitude < 0.0001f)
                        {
                            __result = true;
                            return;
                        }
                    }
                }
            }
            catch { }
        }

        public static void UtilityManager_DespawnVehicles_Postfix()
        {
            try
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TM:PE DespawnVehicles completed. Invoking ReactivateCityLogistics to unblock traffic spawns...");
                VehicleAndTransportRepair.ReactivateCityLogistics();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] UtilityManager_DespawnVehicles_Postfix error: " + ex);
            }
        }

        public static void RestoreVehicleSpeedIfNeeded(
            ushort vehicleID, 
            ref Vehicle vehicleData, 
            PathUnit.Position position, 
            uint laneID, 
            PathUnit.Position prevPos, 
            ref float maxSpeed)
        {
            try
            {
                if (maxSpeed > 0.05f) return;

                if (vehicleData.m_path == 0) return;
                if ((vehicleData.m_flags & Vehicle.Flags.Spawned) == 0) return;

                // Legitimate stops: arriving at destination, parking, waiting for loading/cargo, stopped at station/stop, or waiting for path
                if ((vehicleData.m_flags & (Vehicle.Flags.Arriving | Vehicle.Flags.Parking | Vehicle.Flags.WaitingLoading | Vehicle.Flags.WaitingCargo | Vehicle.Flags.Stopped | Vehicle.Flags.WaitingPath)) != 0)
                {
                    return;
                }

                NetManager nm = Singleton<NetManager>.instance;
                if (nm == null || nm.m_segments == null || nm.m_segments.m_buffer == null || nm.m_lanes == null || nm.m_lanes.m_buffer == null)
                {
                    maxSpeed = 1.25f;
                    return;
                }

                ushort curSegId = position.m_segment;
                ushort prevSegId = prevPos.m_segment;
                ushort checkSegId = curSegId != 0 ? curSegId : prevSegId;
                if (checkSegId == 0 || checkSegId >= nm.m_segments.m_buffer.Length)
                {
                    maxSpeed = 1.25f;
                    return;
                }

                NetInfo info = nm.m_segments.m_buffer[checkSegId].Info;
                if (info == null)
                {
                    maxSpeed = 1.25f;
                    return;
                }

                bool isHighway = (info.m_netAI != null && info.m_netAI.IsHighway());
                if (isHighway)
                {
                    float segSpeed = 2.0f;
                    if (info.m_lanes != null && position.m_lane < info.m_lanes.Length && info.m_lanes[position.m_lane] != null)
                    {
                        segSpeed = UnityEngine.Mathf.Max(info.m_lanes[position.m_lane].m_speedLimit, 1.5f);
                    }
                    maxSpeed = segSpeed;
                    return;
                }

                // Check target node for genuine Red Traffic Light
                ushort startNode = nm.m_segments.m_buffer[checkSegId].m_startNode;
                ushort endNode = nm.m_segments.m_buffer[checkSegId].m_endNode;
                ushort targetNodeId = (position.m_offset > 128) ? endNode : startNode;

                if (targetNodeId != 0 && targetNodeId < nm.m_nodes.m_buffer.Length)
                {
                    NetNode.Flags nodeFlags = nm.m_nodes.m_buffer[targetNodeId].m_flags;
                    if ((nodeFlags & NetNode.Flags.TrafficLights) != 0)
                    {
                        RoadBaseAI.TrafficLightState vState, pState;
                        RoadBaseAI.GetTrafficLightState(targetNodeId, ref nm.m_segments.m_buffer[checkSegId], 
                            Singleton<SimulationManager>.instance.m_currentFrameIndex - (uint)(targetNodeId << 8) / 32768u, 
                            out vState, out pState);

                        if (vState == RoadBaseAI.TrafficLightState.Red || vState == RoadBaseAI.TrafficLightState.GreenToRed)
                        {
                            return;
                        }
                    }
                }

                // Not a red light: restore positive target speed so the vehicle accelerates
                float targetSpeed = 1.25f;
                if (info.m_lanes != null && position.m_lane < info.m_lanes.Length && info.m_lanes[position.m_lane] != null)
                {
                    targetSpeed = UnityEngine.Mathf.Max(info.m_lanes[position.m_lane].m_speedLimit, 1.0f);
                }
                maxSpeed = targetSpeed;
            }
            catch { }
        }

        public static void VehicleBehaviorManager_MayChangeSegment14_Postfix(
            ushort frontVehicleId, 
            ref Vehicle vehicleData, 
            float sqrVelocity, 
            ref PathUnit.Position prevPos, 
            ref NetSegment prevSegment, 
            ushort prevTargetNodeId, 
            uint prevLaneID, 
            ref PathUnit.Position position, 
            ushort targetNodeId, 
            ref NetNode targetNode, 
            uint laneID, 
            ref PathUnit.Position nextPosition, 
            ushort nextTargetNodeId, 
            ref float maxSpeed, 
            ref bool __result)
        {
            HandleMayChangeSegmentOverride(frontVehicleId, ref vehicleData, sqrVelocity, ref prevPos, ref prevSegment, prevTargetNodeId, prevLaneID, ref position, targetNodeId, ref targetNode, laneID, ref maxSpeed, ref __result);
        }

        public static void VehicleBehaviorManager_MayChangeSegment12_Postfix(
            ushort frontVehicleId, 
            ref Vehicle vehicleData, 
            float sqrVelocity, 
            ref PathUnit.Position prevPos, 
            ref NetSegment prevSegment, 
            ushort prevTargetNodeId, 
            uint prevLaneID, 
            ref PathUnit.Position position, 
            ushort targetNodeId, 
            ref NetNode targetNode, 
            uint laneID, 
            ref float maxSpeed, 
            ref bool __result)
        {
            HandleMayChangeSegmentOverride(frontVehicleId, ref vehicleData, sqrVelocity, ref prevPos, ref prevSegment, prevTargetNodeId, prevLaneID, ref position, targetNodeId, ref targetNode, laneID, ref maxSpeed, ref __result);
        }

        private static void HandleMayChangeSegmentOverride(
            ushort frontVehicleId, 
            ref Vehicle vehicleData, 
            float sqrVelocity, 
            ref PathUnit.Position prevPos, 
            ref NetSegment prevSegment, 
            ushort prevTargetNodeId, 
            uint prevLaneID, 
            ref PathUnit.Position position, 
            ushort targetNodeId, 
            ref NetNode targetNode, 
            uint laneID, 
            ref float maxSpeed, 
            ref bool __result)
        {
            try
            {
                if (__result) return;

                if (vehicleData.m_path == 0) return;
                if ((vehicleData.m_flags & Vehicle.Flags.Spawned) == 0) return;
                if ((vehicleData.m_flags & (Vehicle.Flags.Arriving | Vehicle.Flags.Parking | Vehicle.Flags.WaitingLoading | Vehicle.Flags.WaitingCargo | Vehicle.Flags.Stopped | Vehicle.Flags.WaitingPath)) != 0) return;

                NetInfo prevInfo = prevSegment.Info;
                bool isHighway = (prevInfo != null && prevInfo.m_netAI != null && prevInfo.m_netAI.IsHighway());
                if (isHighway)
                {
                    __result = true;
                    if (maxSpeed < 0.1f)
                    {
                        maxSpeed = (prevInfo.m_lanes != null && position.m_lane < prevInfo.m_lanes.Length && prevInfo.m_lanes[position.m_lane] != null)
                            ? UnityEngine.Mathf.Max(prevInfo.m_lanes[position.m_lane].m_speedLimit, 2.0f)
                            : 2.0f;
                    }
                    return;
                }

                if ((targetNode.m_flags & NetNode.Flags.TrafficLights) != 0)
                {
                    RoadBaseAI.TrafficLightState vState, pState;
                    RoadBaseAI.GetTrafficLightState(targetNodeId, ref prevSegment, 
                        Singleton<SimulationManager>.instance.m_currentFrameIndex - (uint)(targetNodeId << 8) / 32768u, 
                        out vState, out pState);

                    if (vState == RoadBaseAI.TrafficLightState.Red || vState == RoadBaseAI.TrafficLightState.GreenToRed)
                    {
                        return;
                    }
                }

                __result = true;
                if (maxSpeed < 0.1f)
                {
                    maxSpeed = (prevInfo != null && prevInfo.m_lanes != null && position.m_lane < prevInfo.m_lanes.Length && prevInfo.m_lanes[position.m_lane] != null)
                        ? UnityEngine.Mathf.Max(prevInfo.m_lanes[position.m_lane].m_speedLimit, 1.0f)
                        : 1.25f;
                }
            }
            catch { }
        }

        public static void CarAI_CalculateSegmentPosition13_Postfix(
            ushort vehicleID, 
            ref Vehicle vehicleData, 
            PathUnit.Position nextPosition, 
            PathUnit.Position position, 
            uint laneID, 
            byte offset, 
            PathUnit.Position prevPos, 
            uint prevLaneID, 
            byte prevOffset, 
            int index, 
            ref UnityEngine.Vector3 pos, 
            ref UnityEngine.Vector3 dir, 
            ref float maxSpeed)
        {
            RestoreVehicleSpeedIfNeeded(vehicleID, ref vehicleData, position, laneID, prevPos, ref maxSpeed);
        }

        public static void CarAI_CalculateSegmentPosition8_Postfix(
            ushort vehicleID, 
            ref Vehicle vehicleData, 
            PathUnit.Position position, 
            uint laneID, 
            byte offset, 
            ref UnityEngine.Vector3 pos, 
            ref UnityEngine.Vector3 dir, 
            ref float maxSpeed)
        {
            RestoreVehicleSpeedIfNeeded(vehicleID, ref vehicleData, position, laneID, position, ref maxSpeed);
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

            uint currentFrame = sm.m_currentFrameIndex;
            int frameIndex = (int)(currentFrame & 255u);

            if (!loggedOnce)
            {
                loggedOnce = true;
                int nodeCount = (__instance.m_nodes != null && __instance.m_nodes.m_buffer != null) ? __instance.m_nodes.m_buffer.Length : 0;
                int segCount = (__instance.m_segments != null && __instance.m_segments.m_buffer != null) ? __instance.m_segments.m_buffer.Length : 0;
                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] NetManager.SimulationStepImpl Postfix RUNNING! frameIndex={0} nodeBuffer={1} segBuffer={2}", frameIndex, nodeCount, segCount));
            }

            // 1. Simulate expanded nodes: 32768 .. 65023 (32256 nodes / 256 = 126 per frame)
            int nodeStart = 32768 + frameIndex * 126;
            int nodeEnd = nodeStart + 126;
            if (nodeEnd > 65024) nodeEnd = 65024;

            NetNode[] nodes = __instance.m_nodes != null ? __instance.m_nodes.m_buffer : null;
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

            NetSegment[] segments = __instance.m_segments != null ? __instance.m_segments.m_buffer : null;
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
                if (Singleton<InfoManager>.exists && Singleton<InfoManager>.instance != null)
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
    // Static color and render patches removed: Core already handles all color transpilation natively.
    // RenderManager.m_indices is pre-warmed to 262,144 slots at load time by EnsureRenderManagerCapacity.
    public static class NetManagerUpdateSegmentColorsZeroPatch
    {
        public static void Postfix(NetManager __instance)
        {
        }
    }

    public static class NetManagerUpdateNodeColorsZeroPatch
    {
        public static void Postfix(NetManager __instance)
        {
        }
    }

    public static class InfoManagerSetActualModePatch
    {
        public static void Postfix()
        {
        }
    }

    public static class RenderManagerUpdateInstancePatch
    {
        public static bool Prefix(RenderManager __instance, uint holder)
        {
            return true;
        }
    }

    // RenderManager.GetInstanceIndex patch removed for maximum rendering FPS and JIT inlining.
    // RenderManager.m_indices is pre-warmed to 262,144 slots at load time by EnsureRenderManagerCapacity.

    public static class SaveSimulationDataSafetyPatch
    {
        public static Exception LastSaveSimulationException = null;
        public static string CurrentSavingFilename = null;

        public static void LogSaveDebug(string text)
        {
            try
            {
                string logDir = ColossalFramework.IO.DataLocation.localApplicationData;
                if (!string.IsNullOrEmpty(logDir))
                {
                    string logPath = System.IO.Path.Combine(logDir, "MNC_Save_Debug.log");
                    string entry = string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}\n", DateTime.Now, text);
                    System.IO.File.AppendAllText(logPath, entry);
                }
            }
            catch { }
        }

        public static void Prefix(object __instance)
        {
            try
            {
                LastSaveSimulationException = null;
                if (__instance != null)
                {
                    FieldInfo fFilename = __instance.GetType().GetField("filename", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    string fname = (fFilename != null) ? (fFilename.GetValue(__instance) as string) : "unknown";
                    CurrentSavingFilename = fname;
                    LogSaveDebug("=== SaveSimulationData BEGIN: " + fname + " ===");
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] === SaveSimulationData BEGIN: " + fname + " ===");
                }

                // Universal pre-save data sanitization executed before file creation and manager serialization
                LogSaveDebug("Running PreSaveSanitization before serialization begins...");
                TransportLineCompatibility.PreSaveSanitization();
                LogSaveDebug("PreSaveSanitization finished cleanly.");
            }
            catch (Exception ex)
            {
                LogSaveDebug("SaveSimulationData prefix error: " + ex);
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SaveSimulationData prefix error: " + ex);
            }
        }

        public static void OnSaveSimulationException(Exception ex)
        {
            if (ex != null)
            {
                LastSaveSimulationException = ex;
                LogSaveDebug("CRITICAL: SaveSimulationData serialization caught unhandled exception:\n" + ex);
                UnityEngine.Debug.LogError("[MoreNetworkCapacityRenew] SaveSimulationData serialization caught unhandled exception:\n" + ex);
            }
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo mCapture = typeof(SaveSimulationDataSafetyPatch).GetMethod(nameof(OnSaveSimulationException), BindingFlags.Public | BindingFlags.Static);
            foreach (var inst in instructions)
            {
                if (inst.opcode == OpCodes.Pop)
                {
                    inst.opcode = OpCodes.Call;
                    inst.operand = mCapture;
                }
                yield return inst;
            }
        }

        public static void Postfix(object __instance, bool __result)
        {
            try
            {
                if (!__result)
                {
                    if (!string.IsNullOrEmpty(CurrentSavingFilename) && !System.IO.File.Exists(CurrentSavingFilename))
                    {
                        if (LastSaveSimulationException == null)
                        {
                            LastSaveSimulationException = new System.IO.IOException("Simulation file was deleted during save serialization (an unhandled exception occurred during manager serialization).");
                        }
                        LogSaveDebug("=== SaveSimulationData FAILED: Output file deleted during serialization! ===");
                        UnityEngine.Debug.LogError("[MoreNetworkCapacityRenew] === SaveSimulationData FAILED: Output file deleted during serialization! ===");
                    }
                    else
                    {
                        LogSaveDebug("=== SaveSimulationData completed successfully ===");
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] === SaveSimulationData completed successfully ===");
                        CurrentSavingFilename = null;
                    }
                }
            }
            catch (Exception ex)
            {
                LogSaveDebug("SaveSimulationData postfix error: " + ex);
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SaveSimulationData postfix log error: " + ex);
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

        public static bool Prefix(SavePanel __instance, bool quitAfterSaving, ref bool errorOnSaving)
        {
            try
            {
                FieldInfo fIsSaving = typeof(SavePanel).GetField("m_IsSaving", BindingFlags.NonPublic | BindingFlags.Static);
                if (fIsSaving != null) fIsSaving.SetValue(null, false);

                if (!string.IsNullOrEmpty(SaveSimulationDataSafetyPatch.CurrentSavingFilename) && !System.IO.File.Exists(SaveSimulationDataSafetyPatch.CurrentSavingFilename))
                {
                    errorOnSaving = true;
                    if (SaveSimulationDataSafetyPatch.LastSaveSimulationException == null)
                    {
                        SaveSimulationDataSafetyPatch.LastSaveSimulationException = new System.IO.IOException("Simulation file was deleted during save serialization (an unhandled exception aborted save serialization).");
                    }
                }

                if (SaveSimulationDataSafetyPatch.LastSaveSimulationException != null)
                {
                    errorOnSaving = true;
                    SaveSimulationDataSafetyPatch.LogSaveDebug("SavePanel.OnSavingComplete intercepted save failure: " + SaveSimulationDataSafetyPatch.LastSaveSimulationException);
                    UnityEngine.Debug.LogError("[MoreNetworkCapacityRenew] SavePanel detected failed save simulation:\n" + SaveSimulationDataSafetyPatch.LastSaveSimulationException);
                    try
                    {
                        var panel = ColossalFramework.UI.UIView.library.ShowModal<ExceptionPanel>("ExceptionPanel");
                        if (panel != null)
                        {
                            string logPath = System.IO.Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "MNC_Save_Debug.log");
                            panel.SetMessage("Game Save Failed", "Cities: Skylines was unable to save simulation data:\n\n" + SaveSimulationDataSafetyPatch.LastSaveSimulationException.Message + "\n\nPlease check MNC_Save_Debug.log and Player.log for full details.\nLog path: " + logPath, true);
                        }
                    }
                    catch { }
                }

                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] SavePanel.OnSavingComplete: errorOnSaving={0}, quitAfterSaving={1}", errorOnSaving, quitAfterSaving));
                if (errorOnSaving)
                {
                    UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] SavePanel reported errorOnSaving = true.");
                    return true;
                }

                if (quitAfterSaving && !errorOnSaving)
                {
                    try
                    {
                        if (Singleton<LoadingManager>.exists)
                        {
                            Singleton<LoadingManager>.instance.QuitApplication();
                        }
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] QuitApplication error: " + ex);
                    }
                    return false;
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
    // ECONOMY & PUBLIC TRANSIT MAINTENANCE SAFETY COMPATIBILITY
    // ============================================================================
    public static class EconomyCompatibility
    {
        public static void Apply(HarmonyInstance harmony)
        {
            if (harmony == null) return;
            try
            {
                Type econType = typeof(EconomyManager);
                MethodInfo preFetch = typeof(EconomyCompatibility).GetMethod(nameof(FetchResource_Prefix), BindingFlags.Public | BindingFlags.Static);
                MethodInfo preAdd = typeof(EconomyCompatibility).GetMethod(nameof(AddResource_Prefix), BindingFlags.Public | BindingFlags.Static);

                foreach (var m in econType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name == "FetchResource")
                    {
                        var p = m.GetParameters();
                        if (p.Length >= 2 && p[0].ParameterType == typeof(EconomyManager.Resource) && p[1].ParameterType == typeof(int))
                        {
                            harmony.Patch(m, prefix: new HarmonyMethod(preFetch));
                        }
                    }
                    else if (m.Name == "AddResource")
                    {
                        var p = m.GetParameters();
                        if (p.Length >= 2 && p[0].ParameterType == typeof(EconomyManager.Resource) && p[1].ParameterType == typeof(int))
                        {
                            harmony.Patch(m, prefix: new HarmonyMethod(preAdd));
                        }
                    }
                }
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Successfully patched EconomyManager FetchResource and AddResource to guard against negative maintenance!");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EconomyCompatibility.Apply error: " + ex);
            }
        }

        public static void FetchResource_Prefix(EconomyManager.Resource resource, ref int amount)
        {
            // FetchResource deducts money from treasury (m_cashAmount -= amount). Negative amount results in free cash printing.
            if (amount < 0)
            {
                amount = 0;
            }
        }

        public static void AddResource_Prefix(EconomyManager.Resource resource, ref int amount)
        {
            if (resource == EconomyManager.Resource.Maintenance && amount < 0)
            {
                amount = 0;
            }
        }

        public static void SanitizeEconomyAndTransport()
        {
            try
            {
                EconomyManager em = Singleton<EconomyManager>.instance;
                if (em != null)
                {
                    string[] expenseFieldNames = new string[] { "m_expenses", "m_totalExpenses", "m_policyExpenses", "m_loanExpenses" };
                    int fixedCount = 0;
                    foreach (string fname in expenseFieldNames)
                    {
                        FieldInfo fExp = typeof(EconomyManager).GetField(fname, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        long[] expenses = (fExp != null) ? fExp.GetValue(em) as long[] : null;
                        if (expenses != null)
                        {
                            for (int i = 0; i < expenses.Length; i++)
                            {
                                if (expenses[i] < 0)
                                {
                                    expenses[i] = 0;
                                    fixedCount++;
                                }
                            }
                        }
                    }
                    if (fixedCount > 0)
                    {
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Sanitized {0} negative expense entries across EconomyManager arrays.", fixedCount));
                    }
                }

                // Sanitize TransportInfo prefabs against negative maintenance or passenger cost
                int prefabCount = PrefabCollection<TransportInfo>.PrefabCount();
                for (uint i = 0; i < (uint)prefabCount; i++)
                {
                    TransportInfo tInfo = PrefabCollection<TransportInfo>.GetPrefab(i);
                    if (tInfo != null)
                    {
                        if (tInfo.m_maintenanceCostPerVehicle < 0) tInfo.m_maintenanceCostPerVehicle = 0;
                        if (tInfo.m_maintenanceCostPerPassenger < 0f || float.IsNaN(tInfo.m_maintenanceCostPerPassenger) || float.IsInfinity(tInfo.m_maintenanceCostPerPassenger))
                        {
                            tInfo.m_maintenanceCostPerPassenger = 0f;
                        }
                    }
                }

                // Sanitize BuildingInfo prefabs against negative maintenance
                int bCount = PrefabCollection<BuildingInfo>.PrefabCount();
                for (uint i = 0; i < (uint)bCount; i++)
                {
                    BuildingInfo bInfo = PrefabCollection<BuildingInfo>.GetPrefab(i);
                    if (bInfo != null)
                    {
                        PlayerBuildingAI pbAI = bInfo.m_buildingAI as PlayerBuildingAI;
                        if (pbAI != null && pbAI.m_maintenanceCost < 0)
                        {
                            pbAI.m_maintenanceCost = 0;
                        }
                    }
                }

                // Sanitize NetInfo prefabs against negative maintenance
                int netCount = PrefabCollection<NetInfo>.PrefabCount();
                for (uint i = 0; i < (uint)netCount; i++)
                {
                    NetInfo nInfo = PrefabCollection<NetInfo>.GetPrefab(i);
                    if (nInfo != null)
                    {
                        PlayerNetAI pnAI = nInfo.m_netAI as PlayerNetAI;
                        if (pnAI != null && pnAI.m_maintenanceCost < 0)
                        {
                            pnAI.m_maintenanceCost = 0;
                        }
                    }
                }

                // Sanitize active TransportLines
                TransportManager tm = Singleton<TransportManager>.instance;
                if (tm != null && tm.m_lines != null && tm.m_lines.m_buffer != null)
                {
                    for (int i = 1; i < tm.m_lines.m_buffer.Length; i++)
                    {
                        if (tm.m_lines.m_buffer[i].m_flags != TransportLine.Flags.None)
                        {
                            if (tm.m_lines.m_buffer[i].m_ticketPrice > 65000)
                            {
                                tm.m_lines.m_buffer[i].m_ticketPrice = 0;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeEconomyAndTransport error: " + ex);
            }
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
            ArrayGuardian.Initialize();
            ArrayGuardian.ExpandAllArrays();
            SafeFlagsAccessor.Initialize();
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
                    // Economy & Public Transit maintenance safety hooks
                    EconomyCompatibility.Apply(harmony);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EconomyCompatibility error: " + ex);
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



                // Network AI simulation slices (up to 65K nodes & segments) are handled 100% natively by MoreNetworkCapacity.Core (NetworkSimulationSlicePatch transpiler).
                // No duplicate C# reflection/substep simulation loop to ensure maximum FPS performance!

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
                ArrayGuardian.ExpandAllArrays();
                ArrayGuardian.ValidateAllArrays();
                // Synchronize TM:PE buffers on level load
                TMPECompatibility.SynchronizeTMPEBuffersAndArrays("OnLevelLoaded");
                ThirdPartyCompatibility.SynchronizeThirdPartyMods("OnLevelLoaded");
                
                // NEW v4.1: Initialize vehicle fix systems
                VehiclePoolMonitor.Initialize();
                TMPEBufferSync.Initialize();
                TMPEBufferSync.SyncAllBuffers("OnLevelLoaded");
                PathFindValidator.Initialize();
                PathFindValidator.ValidateAndCleanPaths();
                StuckVehicleRecovery.Initialize();
                
                // NEW v4.1: Install Harmony patches for vehicle monitoring
                try
                {
                    var harmony = HarmonyInstance.Create("com.ngocduyrenewal.vehiclefix");
                    VehicleFixPatches.InstallAllPatches(harmony);
                }
                catch (Exception harmEx)
                {
                    UnityEngine.Debug.LogError("[MoreNetworkCapacityRenew] Failed to install vehicle fix patches: " + harmEx);
                }
                
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
                        int requiredSize = 1016; // 65024 / 64 (MNC standard, avoids InvalidOperationException)

                        FieldInfo f1 = typeof(ToolController).GetField("m_collidingSegments1", BindingFlags.NonPublic | BindingFlags.Instance);
                        FieldInfo f2 = typeof(ToolController).GetField("m_collidingSegments2", BindingFlags.NonPublic | BindingFlags.Instance);
                        
                        if (f1 != null) f1.SetValue(tc, new ulong[requiredSize]);
                        if (f2 != null) f2.SetValue(tc, new ulong[requiredSize]);

                        FieldInfo b1 = typeof(ToolController).GetField("m_collidingBuildings1", BindingFlags.NonPublic | BindingFlags.Instance);
                        FieldInfo b2 = typeof(ToolController).GetField("m_collidingBuildings2", BindingFlags.NonPublic | BindingFlags.Instance);

                        if (b1 != null) b1.SetValue(tc, new ulong[768]);
                        if (b2 != null) b2.SetValue(tc, new ulong[768]);

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

                // Automatically restore valid lines, purge ghost lines & unstick stranded citizens
                try
                {
                    int restored = VehicleAndTransportRepair.RestoreAllTransportLines(out int deactivated);
                    if (deactivated > 0)
                    {
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Purged {0} corrupted ghost lines (restored {1} active lines).", deactivated, restored));
                    }
                    VehicleAndTransportRepair.UnstickCitizens();
                    TransportLineCompatibility.SanitizeCitizenUnits();
                    int cleanedGhosts = VehicleAndTransportRepair.CleanDemolishedRoadVehicles();
                    if (cleanedGhosts > 0)
                    {
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Cleaned {0} ghost vehicles stranded on demolished roads.", cleanedGhosts));
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Initial clear/restore error: " + ex);
                }

                // Sanitize economy and transport
                EconomyCompatibility.SanitizeEconomyAndTransport();

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
            try
            {
                // NEW v4.1: Stop monitoring systems
                ArrayGuardianV2.StopMonitoring();
                StuckVehicleRecovery.ClearCache();
                DiagnosticsUI.Cleanup();
                
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] OnLevelUnloading: Vehicle fix systems cleaned up");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[MoreNetworkCapacityRenew] OnLevelUnloading error: " + ex);
            }
        }

        private void SanitizeNetwork()
        {
            try
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Starting network sanitization...");
                int fixedCount = 0;
                NetManager nm = Singleton<NetManager>.instance;
                if (nm == null || nm.m_segments == null || nm.m_segments.m_buffer == null || nm.m_lanes == null || nm.m_lanes.m_buffer == null)
                    return;

                int segCount = Math.Min((int)nm.m_segments.m_size, nm.m_segments.m_buffer.Length);
                int laneCount = nm.m_lanes.m_buffer.Length;

                for (int i = 1; i < segCount; i++)
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

                        while (currentLane != 0 && currentLane < (uint)laneCount && count < maxLanes)
                        {
                            prevLane = currentLane;
                            currentLane = nm.m_lanes.m_buffer[currentLane].m_nextLane;
                            count++;
                        }

                        if (currentLane != 0 && prevLane != 0 && prevLane < (uint)laneCount)
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
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeNetwork error: " + ex);
            }
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
                // Lanes remain at standard 262,144
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
                // Assembly-CSharp and TM:PE constant transpilation is handled 100% natively
                // by MoreNetworkCapacity.Core transpilers. Blindly scanning them caused double-patching,
                // district grid corruption (512x512 = 262144), bitmask issues, and massive FPS drop.


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
                            _ = method.ReturnType;
                            _ = method.GetParameters();
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
                                if (val == 36864 || val == 32768 || val == 36863 || val == 32767 || val == 73728 || val == 73727)
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
                                if (val == 36864 || val == 32768 || val == 36863 || val == 32767 || val == 73728 || val == 73727)
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
                    if (val == 36864 || val == 32768 || val == 36863 || val == 32767 || val == 73728 || val == 73727)
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
        private static bool _eightyOne2Patched = false;
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
                   name == "MoreTransportLines" ||
                   name == "EightyOne2";
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
                int req = 1016; // 65024 / 64 (MNC standard)
                FieldInfo f1 = typeof(ToolController).GetField("m_collidingSegments1", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo f2 = typeof(ToolController).GetField("m_collidingSegments2", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f1 != null) f1.SetValue(__instance, new ulong[req]);
                if (f2 != null) f2.SetValue(__instance, new ulong[req]);

                FieldInfo b1 = typeof(ToolController).GetField("m_collidingBuildings1", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo b2 = typeof(ToolController).GetField("m_collidingBuildings2", BindingFlags.NonPublic | BindingFlags.Instance);
                if (b1 != null) b1.SetValue(__instance, new ulong[768]);
                if (b2 != null) b2.SetValue(__instance, new ulong[768]);
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

                int req = 1016; // 65024 / 64 (MNC standard)
                FieldInfo f1 = typeof(ToolController).GetField("m_collidingSegments1", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo f2 = typeof(ToolController).GetField("m_collidingSegments2", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f1 != null && (f1.GetValue(tc) is ulong[] a1 && a1.Length < req)) f1.SetValue(tc, new ulong[req]);
                if (f2 != null && (f2.GetValue(tc) is ulong[] a2 && a2.Length < req)) f2.SetValue(tc, new ulong[req]);

                FieldInfo b1 = typeof(ToolController).GetField("m_collidingBuildings1", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo b2 = typeof(ToolController).GetField("m_collidingBuildings2", BindingFlags.NonPublic | BindingFlags.Instance);
                if (b1 != null && (b1.GetValue(tc) is ulong[] ba1 && ba1.Length < 768)) b1.SetValue(tc, new ulong[768]);
                if (b2 != null && (b2.GetValue(tc) is ulong[] ba2 && ba2.Length < 768)) b2.SetValue(tc, new ulong[768]);
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

                NetNode[] nodes = (nm.m_nodes != null) ? nm.m_nodes.m_buffer : null;
                NetSegment[] segments = (nm.m_segments != null) ? nm.m_segments.m_buffer : null;
                NetLane[] lanes = (nm.m_lanes != null) ? nm.m_lanes.m_buffer : null;
                Building[] buildings = (bm != null && bm.m_buildings != null) ? bm.m_buildings.m_buffer : null;

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "MoveIt")
                    {
                        Type actionType = asm.GetType("MoveIt.Action");
                        if (actionType != null)
                        {
                            if (nodes != null) SetStaticField(actionType, "nodeBuffer", nodes);
                            if (segments != null) SetStaticField(actionType, "segmentBuffer", segments);
                            if (buildings != null) SetStaticField(actionType, "buildingBuffer", buildings);
                        }

                        Type toolType = asm.GetType("MoveIt.MoveItTool");
                        if (toolType != null)
                        {
                            if (nodes != null) SetStaticField(toolType, "nodeBuffer", nodes);
                            if (segments != null) SetStaticField(toolType, "segmentBuffer", segments);
                            if (buildings != null) SetStaticField(toolType, "buildingBuffer", buildings);
                        }

                        Type instType = asm.GetType("MoveIt.Instance");
                        if (instType != null)
                        {
                            if (nodes != null) SetStaticField(instType, "nodeBuffer", nodes);
                            if (segments != null) SetStaticField(instType, "segmentBuffer", segments);
                            if (lanes != null) SetStaticField(instType, "laneBuffer", lanes);
                            if (buildings != null) SetStaticField(instType, "buildingBuffer", buildings);
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

                NetNode[] nodes = (nm.m_nodes != null) ? nm.m_nodes.m_buffer : null;
                NetSegment[] segments = (nm.m_segments != null) ? nm.m_segments.m_buffer : null;
                NetLane[] lanes = (nm.m_lanes != null) ? nm.m_lanes.m_buffer : null;

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type netUtilType = asm.GetType("KianCommons.NetUtil");
                    if (netUtilType != null)
                    {
                        if (nodes != null) SetStaticField(netUtilType, "nodeBuffer_", nodes);
                        if (segments != null) SetStaticField(netUtilType, "segmentBuffer_", segments);
                        if (lanes != null) SetStaticField(netUtilType, "laneBuffer_", lanes);
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

            // 4. EightyOne2 (81 Tiles 2) serialization safety hooks
            PatchEightyOne2(harmony);
        }

        public static void EnsureEightyOne2Patched()
        {
            if (_eightyOne2Patched) return;
            try
            {
                var harmony = HarmonyInstance.Create("MoreNetworkCapacityRenew.ThirdParty");
                PatchEightyOne2(harmony);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureEightyOne2Patched error: " + ex);
            }
        }

        private static void PatchEightyOne2(HarmonyInstance harmony)
        {
            if (_eightyOne2Patched || harmony == null) return;
            try
            {
                Assembly eightyOneAsm = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "EightyOne2")
                    {
                        eightyOneAsm = asm;
                        break;
                    }
                }
                if (eightyOneAsm == null) return;

                int patchedCount = 0;

                // 1. Patch ElectricityManagerDataPatches.CustomSerialize
                try
                {
                    Type elecPatches = eightyOneAsm.GetType("EightyOne2.Patches.ElectricityManagerDataPatches");
                    if (elecPatches != null)
                    {
                        MethodInfo mCustomSerialize = elecPatches.GetMethod("CustomSerialize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (mCustomSerialize != null)
                        {
                            MethodInfo pre = typeof(ThirdPartyCompatibility).GetMethod(nameof(EightyOne2_Electricity_CustomSerialize_Prefix), BindingFlags.Public | BindingFlags.Static);
                            harmony.Patch(mCustomSerialize, prefix: new HarmonyMethod(pre));
                            patchedCount++;
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Hooked EightyOne2.ElectricityManagerDataPatches.CustomSerialize with bounds-safe prefix!");
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patch EightyOne2 Electricity error: " + ex);
                }

                // 2. Patch WaterManagerDataPatches.CustomSerialize
                try
                {
                    Type waterPatches = eightyOneAsm.GetType("EightyOne2.Patches.WaterManagerDataPatches");
                    if (waterPatches != null)
                    {
                        MethodInfo mCustomSerialize = waterPatches.GetMethod("CustomSerialize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (mCustomSerialize != null)
                        {
                            MethodInfo pre = typeof(ThirdPartyCompatibility).GetMethod(nameof(EightyOne2_Water_CustomSerialize_Prefix), BindingFlags.Public | BindingFlags.Static);
                            harmony.Patch(mCustomSerialize, prefix: new HarmonyMethod(pre));
                            patchedCount++;
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Hooked EightyOne2.WaterManagerDataPatches.CustomSerialize with bounds-safe prefix!");
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patch EightyOne2 Water error: " + ex);
                }

                // 3. Patch DistrictManagerDataPatches.CustomSerialize
                try
                {
                    Type distPatches = eightyOneAsm.GetType("EightyOne2.Patches.DistrictManagerDataPatches");
                    if (distPatches != null)
                    {
                        MethodInfo mCustomSerialize = distPatches.GetMethod("CustomSerialize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (mCustomSerialize != null)
                        {
                            MethodInfo pre = typeof(ThirdPartyCompatibility).GetMethod(nameof(EightyOne2_District_CustomSerialize_Prefix), BindingFlags.Public | BindingFlags.Static);
                            harmony.Patch(mCustomSerialize, prefix: new HarmonyMethod(pre));
                            patchedCount++;
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Hooked EightyOne2.DistrictManagerDataPatches.CustomSerialize with bounds-safe prefix!");
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patch EightyOne2 District error: " + ex);
                }

                // 4. Patch GameAreaManagerDataPatches.CustomSerialize
                try
                {
                    Type areaPatches = eightyOneAsm.GetType("EightyOne2.Patches.GameAreaManagerDataPatches");
                    if (areaPatches != null)
                    {
                        MethodInfo mCustomSerialize = areaPatches.GetMethod("CustomSerialize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (mCustomSerialize != null)
                        {
                            MethodInfo pre = typeof(ThirdPartyCompatibility).GetMethod(nameof(EightyOne2_GameArea_CustomSerialize_Prefix), BindingFlags.Public | BindingFlags.Static);
                            harmony.Patch(mCustomSerialize, prefix: new HarmonyMethod(pre));
                            patchedCount++;
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Hooked EightyOne2.GameAreaManagerDataPatches.CustomSerialize with bounds-safe prefix!");
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patch EightyOne2 GameArea error: " + ex);
                }

                // 5. Patch ImmaterialResourceManagerDataPatches.CustomSerialize
                try
                {
                    Type imresPatches = eightyOneAsm.GetType("EightyOne2.Patches.ImmaterialResourceManagerDataPatches");
                    if (imresPatches != null)
                    {
                        MethodInfo mCustomSerialize = imresPatches.GetMethod("CustomSerialize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (mCustomSerialize != null)
                        {
                            MethodInfo pre = typeof(ThirdPartyCompatibility).GetMethod(nameof(EightyOne2_Immaterial_CustomSerialize_Prefix), BindingFlags.Public | BindingFlags.Static);
                            harmony.Patch(mCustomSerialize, prefix: new HarmonyMethod(pre));
                            patchedCount++;
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Hooked EightyOne2.ImmaterialResourceManagerDataPatches.CustomSerialize with bounds-safe prefix!");
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patch EightyOne2 Immaterial error: " + ex);
                }

                if (patchedCount > 0)
                {
                    _eightyOne2Patched = true;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Successfully applied {0} EightyOne2 serialization safety patches!", patchedCount));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] PatchEightyOne2 error: " + ex);
            }
        }

        // Delegate types for fast dynamic invocation of EightyOne2 expanded arrays
        private delegate void CopyExpPulseUnitsDelegate(Array expUnits, Array targetUnits, int count);
        private delegate void CopyExpPulseGroupsDelegate(Array expGroups, Array targetGroups, int count);

        private static bool s_eightyOne2DelegatesInitialized = false;
        private static MethodInfo s_mGetExpElecPulseGroups = null;
        private static MethodInfo s_mGetExpElecPulseUnits = null;
        private static MethodInfo s_mGetExpWaterPulseUnits = null;
        private static MethodInfo s_mGetExpSewagePulseUnits = null;
        private static MethodInfo s_mGetExpHeatingPulseUnits = null;

        private static CopyExpPulseGroupsDelegate s_copyElecGroups = null;
        private static CopyExpPulseUnitsDelegate s_copyElecUnits = null;
        private static CopyExpPulseUnitsDelegate s_copyWaterUnits = null;

        private static void InitEightyOne2Delegates()
        {
            if (s_eightyOne2DelegatesInitialized) return;
            s_eightyOne2DelegatesInitialized = true;
            try
            {
                Assembly eightyOneAsm = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "EightyOne2")
                    {
                        eightyOneAsm = asm;
                        break;
                    }
                }
                if (eightyOneAsm == null) return;

                Type expElec = eightyOneAsm.GetType("EightyOne2.Patches.ExpandedElectricityManager");
                if (expElec != null)
                {
                    s_mGetExpElecPulseGroups = expElec.GetMethod("get_PulseGroups", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    s_mGetExpElecPulseUnits = expElec.GetMethod("get_PulseUnits", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                    if (s_mGetExpElecPulseUnits != null)
                    {
                        Type expUnitType = s_mGetExpElecPulseUnits.ReturnType.GetElementType();
                        Type emUnitType = typeof(ElectricityManager.PulseUnit);
                        s_copyElecUnits = CreateCopyPulseUnitsDynamicMethod(expUnitType, emUnitType);
                    }
                    if (s_mGetExpElecPulseGroups != null)
                    {
                        Type expGroupType = s_mGetExpElecPulseGroups.ReturnType.GetElementType();
                        Type emGroupType = typeof(ElectricityManager.PulseGroup);
                        s_copyElecGroups = CreateCopyPulseGroupsDynamicMethod(expGroupType, emGroupType);
                    }
                }

                Type expWater = eightyOneAsm.GetType("EightyOne2.Patches.ExpandedWaterManager");
                if (expWater != null)
                {
                    s_mGetExpWaterPulseUnits = expWater.GetMethod("get_WaterPulseUnits", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    s_mGetExpSewagePulseUnits = expWater.GetMethod("get_SewagePulseUnits", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    s_mGetExpHeatingPulseUnits = expWater.GetMethod("get_HeatingPulseUnits", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                    if (s_mGetExpWaterPulseUnits != null)
                    {
                        Type expWaterUnitType = s_mGetExpWaterPulseUnits.ReturnType.GetElementType();
                        Type wmUnitType = typeof(WaterManager.PulseUnit);
                        s_copyWaterUnits = CreateCopyPulseUnitsDynamicMethod(expWaterUnitType, wmUnitType);
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] InitEightyOne2Delegates warning: " + ex);
            }
        }

        private static CopyExpPulseUnitsDelegate CreateCopyPulseUnitsDynamicMethod(Type expUnitType, Type targetUnitType)
        {
            try
            {
                var fExpX = expUnitType.GetField("m_x", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fExpZ = expUnitType.GetField("m_z", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fTargetX = targetUnitType.GetField("m_x", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fTargetZ = targetUnitType.GetField("m_z", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var clamp = typeof(UnityEngine.Mathf).GetMethod("Clamp", new Type[] { typeof(int), typeof(int), typeof(int) });

                DynamicMethod dm = new DynamicMethod("CopyPulseUnits_" + targetUnitType.Name, null, new Type[] { typeof(Array), typeof(Array), typeof(int) }, typeof(ThirdPartyCompatibility).Module);
                var il = dm.GetILGenerator();
                var loopStart = il.DefineLabel();
                var loopCond = il.DefineLabel();
                var locI = il.DeclareLocal(typeof(int));

                Type expArrayType = expUnitType.MakeArrayType();
                Type targetArrayType = targetUnitType.MakeArrayType();

                il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_0);
                il.Emit(System.Reflection.Emit.OpCodes.Stloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Br, loopCond);

                il.MarkLabel(loopStart);
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
                il.Emit(System.Reflection.Emit.OpCodes.Castclass, targetArrayType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Ldelema, targetUnitType);

                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                il.Emit(System.Reflection.Emit.OpCodes.Castclass, expArrayType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Ldelema, expUnitType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldfld, fExpX);
                il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_0);
                il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4, 255);
                il.Emit(System.Reflection.Emit.OpCodes.Call, clamp);
                il.Emit(System.Reflection.Emit.OpCodes.Conv_U1);
                il.Emit(System.Reflection.Emit.OpCodes.Stfld, fTargetX);

                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
                il.Emit(System.Reflection.Emit.OpCodes.Castclass, targetArrayType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Ldelema, targetUnitType);

                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                il.Emit(System.Reflection.Emit.OpCodes.Castclass, expArrayType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Ldelema, expUnitType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldfld, fExpZ);
                il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_0);
                il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4, 255);
                il.Emit(System.Reflection.Emit.OpCodes.Call, clamp);
                il.Emit(System.Reflection.Emit.OpCodes.Conv_U1);
                il.Emit(System.Reflection.Emit.OpCodes.Stfld, fTargetZ);

                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_1);
                il.Emit(System.Reflection.Emit.OpCodes.Add);
                il.Emit(System.Reflection.Emit.OpCodes.Stloc, locI);

                il.MarkLabel(loopCond);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_2);
                il.Emit(System.Reflection.Emit.OpCodes.Blt, loopStart);
                il.Emit(System.Reflection.Emit.OpCodes.Ret);

                return (CopyExpPulseUnitsDelegate)dm.CreateDelegate(typeof(CopyExpPulseUnitsDelegate));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] CreateCopyPulseUnitsDynamicMethod error: " + ex);
                return null;
            }
        }

        private static CopyExpPulseGroupsDelegate CreateCopyPulseGroupsDynamicMethod(Type expGroupType, Type targetGroupType)
        {
            try
            {
                var fExpOrig = expGroupType.GetField("m_origCharge", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fExpCur = expGroupType.GetField("m_curCharge", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fExpMergeIdx = expGroupType.GetField("m_mergeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fExpMergeCnt = expGroupType.GetField("m_mergeCount", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fExpX = expGroupType.GetField("m_x", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fExpZ = expGroupType.GetField("m_z", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                var fTargetOrig = targetGroupType.GetField("m_origCharge", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fTargetCur = targetGroupType.GetField("m_curCharge", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fTargetMergeIdx = targetGroupType.GetField("m_mergeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fTargetMergeCnt = targetGroupType.GetField("m_mergeCount", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fTargetX = targetGroupType.GetField("m_x", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fTargetZ = targetGroupType.GetField("m_z", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                var clamp = typeof(UnityEngine.Mathf).GetMethod("Clamp", new Type[] { typeof(int), typeof(int), typeof(int) });

                DynamicMethod dm = new DynamicMethod("CopyPulseGroups", null, new Type[] { typeof(Array), typeof(Array), typeof(int) }, typeof(ThirdPartyCompatibility).Module);
                var il = dm.GetILGenerator();
                var loopStart = il.DefineLabel();
                var loopCond = il.DefineLabel();
                var locI = il.DeclareLocal(typeof(int));

                Type expArrayType = expGroupType.MakeArrayType();
                Type targetArrayType = targetGroupType.MakeArrayType();

                il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_0);
                il.Emit(System.Reflection.Emit.OpCodes.Stloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Br, loopCond);

                il.MarkLabel(loopStart);

                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
                il.Emit(System.Reflection.Emit.OpCodes.Castclass, targetArrayType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Ldelema, targetGroupType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                il.Emit(System.Reflection.Emit.OpCodes.Castclass, expArrayType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Ldelema, expGroupType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldfld, fExpOrig);
                il.Emit(System.Reflection.Emit.OpCodes.Stfld, fTargetOrig);

                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
                il.Emit(System.Reflection.Emit.OpCodes.Castclass, targetArrayType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Ldelema, targetGroupType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                il.Emit(System.Reflection.Emit.OpCodes.Castclass, expArrayType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Ldelema, expGroupType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldfld, fExpCur);
                il.Emit(System.Reflection.Emit.OpCodes.Stfld, fTargetCur);

                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
                il.Emit(System.Reflection.Emit.OpCodes.Castclass, targetArrayType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Ldelema, targetGroupType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                il.Emit(System.Reflection.Emit.OpCodes.Castclass, expArrayType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Ldelema, expGroupType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldfld, fExpMergeIdx);
                il.Emit(System.Reflection.Emit.OpCodes.Stfld, fTargetMergeIdx);

                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
                il.Emit(System.Reflection.Emit.OpCodes.Castclass, targetArrayType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Ldelema, targetGroupType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                il.Emit(System.Reflection.Emit.OpCodes.Castclass, expArrayType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Ldelema, expGroupType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldfld, fExpMergeCnt);
                il.Emit(System.Reflection.Emit.OpCodes.Stfld, fTargetMergeCnt);

                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
                il.Emit(System.Reflection.Emit.OpCodes.Castclass, targetArrayType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Ldelema, targetGroupType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                il.Emit(System.Reflection.Emit.OpCodes.Castclass, expArrayType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Ldelema, expGroupType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldfld, fExpX);
                il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_0);
                il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4, 255);
                il.Emit(System.Reflection.Emit.OpCodes.Call, clamp);
                il.Emit(System.Reflection.Emit.OpCodes.Conv_U1);
                il.Emit(System.Reflection.Emit.OpCodes.Stfld, fTargetX);

                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
                il.Emit(System.Reflection.Emit.OpCodes.Castclass, targetArrayType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Ldelema, targetGroupType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                il.Emit(System.Reflection.Emit.OpCodes.Castclass, expArrayType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Ldelema, expGroupType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldfld, fExpZ);
                il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_0);
                il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4, 255);
                il.Emit(System.Reflection.Emit.OpCodes.Call, clamp);
                il.Emit(System.Reflection.Emit.OpCodes.Conv_U1);
                il.Emit(System.Reflection.Emit.OpCodes.Stfld, fTargetZ);

                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_1);
                il.Emit(System.Reflection.Emit.OpCodes.Add);
                il.Emit(System.Reflection.Emit.OpCodes.Stloc, locI);

                il.MarkLabel(loopCond);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locI);
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_2);
                il.Emit(System.Reflection.Emit.OpCodes.Blt, loopStart);
                il.Emit(System.Reflection.Emit.OpCodes.Ret);

                return (CopyExpPulseGroupsDelegate)dm.CreateDelegate(typeof(CopyExpPulseGroupsDelegate));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] CreateCopyPulseGroupsDynamicMethod error: " + ex);
                return null;
            }
        }

        private static void SafeCopyPulseUnits(Array expUnits, Array targetUnits, int count)
        {
            if (expUnits == null || targetUnits == null || count <= 0) return;
            if (s_copyElecUnits != null && targetUnits is ElectricityManager.PulseUnit[])
            {
                s_copyElecUnits(expUnits, targetUnits, count);
                return;
            }
            if (s_copyWaterUnits != null && targetUnits is WaterManager.PulseUnit[])
            {
                s_copyWaterUnits(expUnits, targetUnits, count);
                return;
            }

            try
            {
                Type expElem = expUnits.GetType().GetElementType();
                Type targetElem = targetUnits.GetType().GetElementType();
                var fExpX = expElem.GetField("m_x", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fExpZ = expElem.GetField("m_z", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fTargetX = targetElem.GetField("m_x", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fTargetZ = targetElem.GetField("m_z", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                for (int i = 0; i < count; i++)
                {
                    object src = expUnits.GetValue(i);
                    object dst = targetUnits.GetValue(i);
                    ushort x = (ushort)fExpX.GetValue(src);
                    ushort z = (ushort)fExpZ.GetValue(src);
                    fTargetX.SetValue(dst, (byte)UnityEngine.Mathf.Clamp((int)x, 0, 255));
                    fTargetZ.SetValue(dst, (byte)UnityEngine.Mathf.Clamp((int)z, 0, 255));
                    targetUnits.SetValue(dst, i);
                }
            }
            catch { }
        }

        private static void SafeCopyPulseGroups(Array expGroups, ElectricityManager.PulseGroup[] targetGroups, int count)
        {
            if (expGroups == null || targetGroups == null || count <= 0) return;
            if (s_copyElecGroups != null)
            {
                s_copyElecGroups(expGroups, targetGroups, count);
                return;
            }

            try
            {
                Type expElem = expGroups.GetType().GetElementType();
                var fExpOrig = expElem.GetField("m_origCharge", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fExpCur = expElem.GetField("m_curCharge", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fExpMergeIdx = expElem.GetField("m_mergeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fExpMergeCnt = expElem.GetField("m_mergeCount", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fExpX = expElem.GetField("m_x", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fExpZ = expElem.GetField("m_z", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                for (int i = 0; i < count; i++)
                {
                    object src = expGroups.GetValue(i);
                    targetGroups[i].m_origCharge = (uint)fExpOrig.GetValue(src);
                    targetGroups[i].m_curCharge = (uint)fExpCur.GetValue(src);
                    targetGroups[i].m_mergeIndex = (ushort)fExpMergeIdx.GetValue(src);
                    targetGroups[i].m_mergeCount = (ushort)fExpMergeCnt.GetValue(src);
                    ushort x = (ushort)fExpX.GetValue(src);
                    ushort z = (ushort)fExpZ.GetValue(src);
                    targetGroups[i].m_x = (byte)UnityEngine.Mathf.Clamp((int)x, 0, 255);
                    targetGroups[i].m_z = (byte)UnityEngine.Mathf.Clamp((int)z, 0, 255);
                }
            }
            catch { }
        }

        public static bool EightyOne2_Electricity_CustomSerialize_Prefix(ElectricityManager.Cell[] electricityGrid, ElectricityManager.PulseGroup[] pulseGroups, ElectricityManager.PulseUnit[] pulseUnits, ref ElectricityManager.Cell[] __result)
        {
            try
            {
                InitEightyOne2Delegates();

                ElectricityManager.Cell[] safeCells = new ElectricityManager.Cell[65536];
                if (electricityGrid != null)
                {
                    if (electricityGrid.Length >= 213444)
                    {
                        for (int z = 0; z < 256; z++)
                        {
                            for (int x = 0; x < 256; x++)
                            {
                                int src = (z + 103) * 462 + x + 103;
                                int dst = z * 256 + x;
                                safeCells[dst] = electricityGrid[src];
                            }
                        }
                    }
                    else
                    {
                        int len = Math.Min(electricityGrid.Length, safeCells.Length);
                        Array.Copy(electricityGrid, safeCells, len);
                    }
                }

                // Sync PulseGroups safely
                if (pulseGroups != null && s_mGetExpElecPulseGroups != null)
                {
                    Array expGroups = s_mGetExpElecPulseGroups.Invoke(null, null) as Array;
                    if (expGroups != null)
                    {
                        int gCount = Math.Min(pulseGroups.Length, expGroups.Length);
                        SafeCopyPulseGroups(expGroups, pulseGroups, gCount);
                    }
                }

                // Sync PulseUnits safely (bypasses EightyOne2's IndexOutOfRangeException)
                if (pulseUnits != null && s_mGetExpElecPulseUnits != null)
                {
                    Array expUnits = s_mGetExpElecPulseUnits.Invoke(null, null) as Array;
                    if (expUnits != null)
                    {
                        int uCount = Math.Min(pulseUnits.Length, expUnits.Length);
                        SafeCopyPulseUnits(expUnits, pulseUnits, uCount);
                        for (int i = uCount; i < pulseUnits.Length; i++)
                        {
                            pulseUnits[i].m_x = 0;
                            pulseUnits[i].m_z = 0;
                        }
                    }
                }

                __result = safeCells;
                return false;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[MoreNetworkCapacityRenew] EightyOne2_Electricity_CustomSerialize_Prefix error: " + ex);
                __result = new ElectricityManager.Cell[65536];
                return false;
            }
        }

        public static bool EightyOne2_Water_CustomSerialize_Prefix(WaterManager.Cell[] waterGrid, WaterManager.PulseUnit[] waterPulseUnits, WaterManager.PulseUnit[] sewagePulseUnits, WaterManager.PulseUnit[] heatingPulseUnits, ref WaterManager.Cell[] __result)
        {
            try
            {
                InitEightyOne2Delegates();

                WaterManager.Cell[] safeCells = new WaterManager.Cell[65536];
                if (waterGrid != null)
                {
                    if (waterGrid.Length >= 213444)
                    {
                        for (int z = 0; z < 256; z++)
                        {
                            for (int x = 0; x < 256; x++)
                            {
                                int src = (z + 103) * 462 + x + 103;
                                int dst = z * 256 + x;
                                safeCells[dst] = waterGrid[src];
                            }
                        }
                    }
                    else
                    {
                        int len = Math.Min(waterGrid.Length, safeCells.Length);
                        Array.Copy(waterGrid, safeCells, len);
                    }
                }

                // 1. Water PulseUnits
                if (waterPulseUnits != null && s_mGetExpWaterPulseUnits != null)
                {
                    Array expUnits = s_mGetExpWaterPulseUnits.Invoke(null, null) as Array;
                    if (expUnits != null)
                    {
                        int uCount = Math.Min(waterPulseUnits.Length, expUnits.Length);
                        SafeCopyPulseUnits(expUnits, waterPulseUnits, uCount);
                        for (int i = uCount; i < waterPulseUnits.Length; i++)
                        {
                            waterPulseUnits[i].m_x = 0;
                            waterPulseUnits[i].m_z = 0;
                        }
                    }
                }

                // 2. Sewage PulseUnits
                if (sewagePulseUnits != null && s_mGetExpSewagePulseUnits != null)
                {
                    Array expUnits = s_mGetExpSewagePulseUnits.Invoke(null, null) as Array;
                    if (expUnits != null)
                    {
                        int uCount = Math.Min(sewagePulseUnits.Length, expUnits.Length);
                        SafeCopyPulseUnits(expUnits, sewagePulseUnits, uCount);
                        for (int i = uCount; i < sewagePulseUnits.Length; i++)
                        {
                            sewagePulseUnits[i].m_x = 0;
                            sewagePulseUnits[i].m_z = 0;
                        }
                    }
                }

                // 3. Heating PulseUnits
                if (heatingPulseUnits != null && s_mGetExpHeatingPulseUnits != null)
                {
                    Array expUnits = s_mGetExpHeatingPulseUnits.Invoke(null, null) as Array;
                    if (expUnits != null)
                    {
                        int uCount = Math.Min(heatingPulseUnits.Length, expUnits.Length);
                        SafeCopyPulseUnits(expUnits, heatingPulseUnits, uCount);
                        for (int i = uCount; i < heatingPulseUnits.Length; i++)
                        {
                            heatingPulseUnits[i].m_x = 0;
                            heatingPulseUnits[i].m_z = 0;
                        }
                    }
                }

                __result = safeCells;
                return false;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[MoreNetworkCapacityRenew] EightyOne2_Water_CustomSerialize_Prefix error: " + ex);
                __result = new WaterManager.Cell[65536];
                return false;
            }
        }

        public static bool EightyOne2_District_CustomSerialize_Prefix(ColossalFramework.IO.EncodedArray.Byte encodedArray, DistrictManager.Cell[] districtCellArray)
        {
            try
            {
                if (encodedArray == null) return false;

                if (districtCellArray != null && districtCellArray.Length >= 810000)
                {
                    for (int z = 0; z < 512; z++)
                    {
                        for (int x = 0; x < 512; x++)
                        {
                            int idx = (z + 194) * 900 + x + 194;
                            encodedArray.Write(districtCellArray[idx].m_district1);
                        }
                    }
                    for (int z = 0; z < 512; z++)
                    {
                        for (int x = 0; x < 512; x++)
                        {
                            int idx = (z + 194) * 900 + x + 194;
                            encodedArray.Write(districtCellArray[idx].m_district2);
                        }
                    }
                    for (int z = 0; z < 512; z++)
                    {
                        for (int x = 0; x < 512; x++)
                        {
                            int idx = (z + 194) * 900 + x + 194;
                            encodedArray.Write(districtCellArray[idx].m_district3);
                        }
                    }
                    for (int z = 0; z < 512; z++)
                    {
                        for (int x = 0; x < 512; x++)
                        {
                            int idx = (z + 194) * 900 + x + 194;
                            encodedArray.Write(districtCellArray[idx].m_district4);
                        }
                    }
                    for (int z = 0; z < 512; z++)
                    {
                        for (int x = 0; x < 512; x++)
                        {
                            int idx = (z + 194) * 900 + x + 194;
                            encodedArray.Write(districtCellArray[idx].m_alpha1);
                        }
                    }
                    for (int z = 0; z < 512; z++)
                    {
                        for (int x = 0; x < 512; x++)
                        {
                            int idx = (z + 194) * 900 + x + 194;
                            encodedArray.Write(districtCellArray[idx].m_alpha2);
                        }
                    }
                    for (int z = 0; z < 512; z++)
                    {
                        for (int x = 0; x < 512; x++)
                        {
                            int idx = (z + 194) * 900 + x + 194;
                            encodedArray.Write(districtCellArray[idx].m_alpha3);
                        }
                    }
                    for (int z = 0; z < 512; z++)
                    {
                        for (int x = 0; x < 512; x++)
                        {
                            int idx = (z + 194) * 900 + x + 194;
                            encodedArray.Write(districtCellArray[idx].m_alpha4);
                        }
                    }
                    return false;
                }

                int limit = Math.Min(districtCellArray != null ? districtCellArray.Length : 0, 262144);
                // 1
                for (int i = 0; i < limit; i++) encodedArray.Write(districtCellArray[i].m_district1);
                for (int i = limit; i < 262144; i++) encodedArray.Write((byte)0);
                // 2
                for (int i = 0; i < limit; i++) encodedArray.Write(districtCellArray[i].m_district2);
                for (int i = limit; i < 262144; i++) encodedArray.Write((byte)0);
                // 3
                for (int i = 0; i < limit; i++) encodedArray.Write(districtCellArray[i].m_district3);
                for (int i = limit; i < 262144; i++) encodedArray.Write((byte)0);
                // 4
                for (int i = 0; i < limit; i++) encodedArray.Write(districtCellArray[i].m_district4);
                for (int i = limit; i < 262144; i++) encodedArray.Write((byte)0);
                // 5
                for (int i = 0; i < limit; i++) encodedArray.Write(districtCellArray[i].m_alpha1);
                for (int i = limit; i < 262144; i++) encodedArray.Write((byte)0);
                // 6
                for (int i = 0; i < limit; i++) encodedArray.Write(districtCellArray[i].m_alpha2);
                for (int i = limit; i < 262144; i++) encodedArray.Write((byte)0);
                // 7
                for (int i = 0; i < limit; i++) encodedArray.Write(districtCellArray[i].m_alpha3);
                for (int i = limit; i < 262144; i++) encodedArray.Write((byte)0);
                // 8
                for (int i = 0; i < limit; i++) encodedArray.Write(districtCellArray[i].m_alpha4);
                for (int i = limit; i < 262144; i++) encodedArray.Write((byte)0);

                return false;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[MoreNetworkCapacityRenew] EightyOne2_District_CustomSerialize_Prefix error: " + ex);
                return false;
            }
        }

        public static bool EightyOne2_GameArea_CustomSerialize_Prefix(ColossalFramework.IO.EncodedArray.Byte encodedArray, GameAreaManager instance)
        {
            try
            {
                if (encodedArray == null) return false;
                if (instance != null && instance.m_areaGrid != null && instance.m_areaGrid.Length >= 81)
                {
                    for (int z = 0; z < 5; z++)
                    {
                        for (int x = 0; x < 5; x++)
                        {
                            int idx = (z + 2) * 9 + x + 2;
                            encodedArray.Write((byte)instance.m_areaGrid[idx]);
                        }
                    }
                    return false;
                }

                int limit = instance != null && instance.m_areaGrid != null ? Math.Min(instance.m_areaGrid.Length, 25) : 0;
                for (int i = 0; i < limit; i++) encodedArray.Write((byte)instance.m_areaGrid[i]);
                for (int i = limit; i < 25; i++) encodedArray.Write((byte)0);
                return false;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[MoreNetworkCapacityRenew] EightyOne2_GameArea_CustomSerialize_Prefix error: " + ex);
                return false;
            }
        }

        public static bool EightyOne2_Immaterial_CustomSerialize_Prefix(ColossalFramework.IO.EncodedArray.UShort encodedArray, ushort[] localResourceArray)
        {
            try
            {
                if (encodedArray == null) return false;
                if (localResourceArray != null && localResourceArray.Length >= 7087500)
                {
                    for (int z = 0; z < 256; z++)
                    {
                        for (int x = 0; x < 256; x++)
                        {
                            int baseIdx = ((z + 97) * 450 + x + 97) * 35;
                            for (int r = 0; r < 35; r++)
                            {
                                encodedArray.Write(localResourceArray[baseIdx + r]);
                            }
                        }
                    }
                    return false;
                }

                int limit = localResourceArray != null ? Math.Min(localResourceArray.Length, 2293760) : 0;
                for (int i = 0; i < limit; i++) encodedArray.Write(localResourceArray[i]);
                for (int i = limit; i < 2293760; i++) encodedArray.Write((ushort)0);
                return false;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[MoreNetworkCapacityRenew] EightyOne2_Immaterial_CustomSerialize_Prefix error: " + ex);
                return false;
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

                // 3. Clean up citizen paths
                UnstickCitizens();

                // 3b. Clear stuck vehicle recovery tracking
                StuckVehicleRecovery.ClearCache();

                // 4. Reactivate City Logistics & Spawning Pipelines
                ReactivateCityLogistics();

                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] ClearAllVehicles completed: Cleared {0} vehicles.", freed));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] ClearAllVehicles error: " + ex);
            }
            return freed;
        }

        public static int ReactivateCityLogistics()
        {
            int repaired = 0;
            try
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Reactivating city logistics and unblocking traffic spawns...");
                VehicleManager vm = Singleton<VehicleManager>.instance;
                BuildingManager bm = Singleton<BuildingManager>.instance;
                TransportManager tm = Singleton<TransportManager>.instance;
                CitizenManager cm = Singleton<CitizenManager>.instance;
                PathManager pm = Singleton<PathManager>.instance;
                if (pm != null)
                {
                    try { pm.WaitForAllPaths(); } catch { }
                }

                int vBufLen = (vm != null && vm.m_vehicles != null && vm.m_vehicles.m_buffer != null) ? vm.m_vehicles.m_buffer.Length : 0;

                // 1. Repair Building vehicle lists (m_ownVehicles, m_guestVehicles)
                if (bm != null && bm.m_buildings != null && bm.m_buildings.m_buffer != null && vBufLen > 0)
                {
                    for (int i = 1; i < bm.m_buildings.m_buffer.Length; i++)
                    {
                        if (bm.m_buildings.m_buffer[i].m_flags == Building.Flags.None)
                        {
                            if (bm.m_buildings.m_buffer[i].m_ownVehicles != 0 || bm.m_buildings.m_buffer[i].m_guestVehicles != 0)
                            {
                                bm.m_buildings.m_buffer[i].m_ownVehicles = 0;
                                bm.m_buildings.m_buffer[i].m_guestVehicles = 0;
                                repaired++;
                            }
                            continue;
                        }

                        // Check m_ownVehicles
                        ushort ownHead = bm.m_buildings.m_buffer[i].m_ownVehicles;
                        if (ownHead != 0)
                        {
                            if (ownHead >= vBufLen || (vm.m_vehicles.m_buffer[ownHead].m_flags & Vehicle.Flags.Created) == 0)
                            {
                                bm.m_buildings.m_buffer[i].m_ownVehicles = 0;
                                repaired++;
                            }
                            else
                            {
                                ushort prev = 0;
                                ushort curr = ownHead;
                                int steps = 0;
                                while (curr != 0 && steps++ < 16384)
                                {
                                    if (curr >= vBufLen || (vm.m_vehicles.m_buffer[curr].m_flags & Vehicle.Flags.Created) == 0)
                                    {
                                        if (prev == 0) bm.m_buildings.m_buffer[i].m_ownVehicles = 0;
                                        else vm.m_vehicles.m_buffer[prev].m_nextOwnVehicle = 0;
                                        repaired++;
                                        break;
                                    }
                                    prev = curr;
                                    curr = vm.m_vehicles.m_buffer[curr].m_nextOwnVehicle;
                                }
                                if (steps >= 16384 && prev != 0)
                                {
                                    vm.m_vehicles.m_buffer[prev].m_nextOwnVehicle = 0;
                                    repaired++;
                                }
                            }
                        }

                        // Check m_guestVehicles
                        ushort guestHead = bm.m_buildings.m_buffer[i].m_guestVehicles;
                        if (guestHead != 0)
                        {
                            if (guestHead >= vBufLen || (vm.m_vehicles.m_buffer[guestHead].m_flags & Vehicle.Flags.Created) == 0)
                            {
                                bm.m_buildings.m_buffer[i].m_guestVehicles = 0;
                                repaired++;
                            }
                            else
                            {
                                ushort prev = 0;
                                ushort curr = guestHead;
                                int steps = 0;
                                while (curr != 0 && steps++ < 16384)
                                {
                                    if (curr >= vBufLen || (vm.m_vehicles.m_buffer[curr].m_flags & Vehicle.Flags.Created) == 0)
                                    {
                                        if (prev == 0) bm.m_buildings.m_buffer[i].m_guestVehicles = 0;
                                        else vm.m_vehicles.m_buffer[prev].m_nextGuestVehicle = 0;
                                        repaired++;
                                        break;
                                    }
                                    prev = curr;
                                    curr = vm.m_vehicles.m_buffer[curr].m_nextGuestVehicle;
                                }
                                if (steps >= 16384 && prev != 0)
                                {
                                    vm.m_vehicles.m_buffer[prev].m_nextGuestVehicle = 0;
                                    repaired++;
                                }
                            }
                        }
                    }
                }

                // 2. Repair TransportLine vehicle lists (m_vehicles)
                if (tm != null && tm.m_lines != null && tm.m_lines.m_buffer != null && vBufLen > 0)
                {
                    for (int i = 1; i < tm.m_lines.m_buffer.Length; i++)
                    {
                        if (tm.m_lines.m_buffer[i].m_flags == TransportLine.Flags.None)
                        {
                            if (tm.m_lines.m_buffer[i].m_vehicles != 0)
                            {
                                tm.m_lines.m_buffer[i].m_vehicles = 0;
                                repaired++;
                            }
                            continue;
                        }

                        ushort lineVehHead = tm.m_lines.m_buffer[i].m_vehicles;
                        if (lineVehHead != 0)
                        {
                            if (lineVehHead >= vBufLen || (vm.m_vehicles.m_buffer[lineVehHead].m_flags & Vehicle.Flags.Created) == 0)
                            {
                                tm.m_lines.m_buffer[i].m_vehicles = 0;
                                repaired++;
                            }
                            else
                            {
                                ushort prev = 0;
                                ushort curr = lineVehHead;
                                int steps = 0;
                                while (curr != 0 && steps++ < 16384)
                                {
                                    if (curr >= vBufLen || (vm.m_vehicles.m_buffer[curr].m_flags & Vehicle.Flags.Created) == 0)
                                    {
                                        if (prev == 0) tm.m_lines.m_buffer[i].m_vehicles = 0;
                                        else vm.m_vehicles.m_buffer[prev].m_nextLineVehicle = 0;
                                        repaired++;
                                        break;
                                    }
                                    prev = curr;
                                    curr = vm.m_vehicles.m_buffer[curr].m_nextLineVehicle;
                                }
                                if (steps >= 16384 && prev != 0)
                                {
                                    vm.m_vehicles.m_buffer[prev].m_nextLineVehicle = 0;
                                    repaired++;
                                }
                            }
                        }
                    }
                }

                // 3. Recalculate VehicleManager.m_vehicleCount accurately
                if (vm != null && vm.m_vehicles != null && vm.m_vehicles.m_buffer != null)
                {
                    int active = 0;
                    for (int i = 1; i < vm.m_vehicles.m_buffer.Length; i++)
                    {
                        if ((vm.m_vehicles.m_buffer[i].m_flags & Vehicle.Flags.Created) != 0)
                        {
                            active++;
                        }
                    }
                    vm.m_vehicleCount = active;
                    VehiclePoolMonitor.RebuildInfoCounts(vm);
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Recalculated VehicleManager.m_vehicleCount = {0} and rebuilt m_infoCount (max capacity = {1})", active, vm.m_vehicles.m_buffer.Length));
                }

                // 4. Release stuck citizens and dead path references
                if (cm != null && cm.m_instances != null && cm.m_instances.m_buffer != null)
                {
                    int unstuck = 0;
                    for (int i = 1; i < cm.m_instances.m_buffer.Length; i++)
                    {
                        if ((cm.m_instances.m_buffer[i].m_flags & CitizenInstance.Flags.Created) == 0) continue;

                        CitizenInstance.Flags flags = cm.m_instances.m_buffer[i].m_flags;
                        if ((flags & (CitizenInstance.Flags.WaitingTransport | CitizenInstance.Flags.WaitingPath | CitizenInstance.Flags.EnteringVehicle | CitizenInstance.Flags.BoredOfWaiting)) != 0)
                        {
                            if (cm.m_instances.m_buffer[i].m_path != 0 && pm != null)
                            {
                                try { pm.ReleasePath(cm.m_instances.m_buffer[i].m_path); } catch { }
                                cm.m_instances.m_buffer[i].m_path = 0;
                            }
                            cm.m_instances.m_buffer[i].m_waitCounter = 0;
                            try
                            {
                                cm.ReleaseCitizenInstance((ushort)i);
                                unstuck++;
                            }
                            catch { }
                        }
                    }
                    if (unstuck > 0)
                    {
                        repaired += unstuck;
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] ReactivateCityLogistics: Reset {0} citizen instances in transit.", unstuck));
                    }
                }

                // 5. Clean up any orphaned PathUnits in PathManager
                if (pm != null && pm.m_pathUnits != null && pm.m_pathUnits.m_buffer != null)
                {
                    int freedPaths = 0;
                    for (uint i = 1; i < pm.m_pathUnits.m_buffer.Length; i++)
                    {
                        if (pm.m_pathUnits.m_buffer[i].m_pathFindFlags != 0 &&
                            pm.m_pathUnits.m_buffer[i].m_referenceCount == 0)
                        {
                            try
                            {
                                pm.ReleasePath(i);
                                freedPaths++;
                            }
                            catch { }
                        }
                    }
                    if (freedPaths > 0)
                    {
                        repaired += freedPaths;
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] ReactivateCityLogistics: Released {0} orphaned PathUnits.", freedPaths));
                    }
                }

                // 6. Request TM:PE RoutingManager full recalculation to unblock vehicle paths
                try
                {
                    Assembly tmpe = TMPECompatibility.GetTMPEAssembly();
                    if (tmpe != null)
                    {
                        Type rmType = tmpe.GetType("TrafficManager.Manager.Impl.RoutingManager");
                        if (rmType != null)
                        {
                            object rmInst = TMPECompatibility.GetManagerInstance(rmType);
                            MethodInfo req = rmType.GetMethod("RequestFullRecalculation", BindingFlags.Public | BindingFlags.Instance);
                            if (req != null && rmInst != null)
                            {
                                req.Invoke(rmInst, new object[] { true });
                                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Requested TM:PE RoutingManager full recalculation.");
                            }
                        }
                    }
                }
                catch { }

                // 7. Ensure TM:PE buffers and speed limits are live
                TMPEBufferSync.SyncAllBuffers("ReactivateCityLogistics");

                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] ReactivateCityLogistics completed successfully ({0} repairs applied). Spawning is unblocked.", repaired));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] ReactivateCityLogistics error: " + ex);
            }
            return repaired;
        }

        public static void RunFullRepair(bool verbose = true)
        {
            try
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Starting Transport Line Repair (vehicles preserved)...");
                int restoredLines = RestoreAllTransportLines();
                int freedUnits = TransportLineCompatibility.SanitizeCitizenUnits();
                
                string msg = string.Format("MNC Renew: Restored {0} transport lines and cleaned {1} phantom citizen units (all vehicles preserved)!", restoredLines, freedUnits);
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
                            try { pm.ReleasePath(cm.m_instances.m_buffer[i].m_path); } catch { }
                            cm.m_instances.m_buffer[i].m_path = 0;
                        }
                        cm.m_instances.m_buffer[i].m_waitCounter = 0;

                        try
                        {
                            cm.ReleaseCitizenInstance((ushort)i);
                        }
                        catch { }
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
                if (tm == null || tm.m_lines == null || tm.m_lines.m_buffer == null ||
                    nm == null || nm.m_nodes == null || nm.m_nodes.m_buffer == null ||
                    nm.m_segments == null || nm.m_segments.m_buffer == null) return 0;

                int nodeLimit = nm.m_nodes.m_buffer.Length;
                int prefabCount = PrefabCollection<TransportInfo>.PrefabCount();

                for (int lineID = 1; lineID < tm.m_lines.m_buffer.Length; lineID++)
                {
                    ushort lid = (ushort)lineID;
                    if ((tm.m_lines.m_buffer[lid].m_flags & TransportLine.Flags.Created) == 0) continue;

                    ushort infoIndex = tm.m_lines.m_buffer[lid].m_infoIndex;
                    ushort stops = tm.m_lines.m_buffer[lid].m_stops;
                    float len = tm.m_lines.m_buffer[lid].m_totalLength;
                    // Fix NaN / Infinity length so it doesn't cause false line deactivation
                    if (float.IsNaN(len) || float.IsInfinity(len))
                    {
                        tm.m_lines.m_buffer[lid].m_totalLength = 0f;
                        len = 0f;
                    }

                    // Only deactivate if prefab is missing/invalid
                    if (infoIndex >= prefabCount || PrefabCollection<TransportInfo>.GetPrefab(infoIndex) == null)
                    {
                        tm.m_lines.m_buffer[lid].m_flags = TransportLine.Flags.None;
                        tm.m_lines.m_buffer[lid].m_infoIndex = 0;
                        tm.m_lines.m_buffer[lid].m_stops = 0;
                        tm.m_lines.m_buffer[lid].m_totalLength = 0f;
                        tm.m_lines.m_buffer[lid].m_vehicles = 0;
                        deactivated++;
                        continue;
                    }

                    if (stops >= nodeLimit || stops >= 65024)
                    {
                        if (stops == ushort.MaxValue)
                        {
                            tm.m_lines.m_buffer[lid].m_stops = 0;
                            stops = 0;
                        }
                    }

                    // Clean flags: clear Hidden and Invalid (64), set Complete
                    tm.m_lines.m_buffer[lid].m_flags &= ~((TransportLine.Flags)64 | TransportLine.Flags.Hidden);
                    tm.m_lines.m_buffer[lid].m_flags |= TransportLine.Flags.Complete;

                    // Fix line colors: if pure black or uninitialized, generate a distinct vibrant color
                    UnityEngine.Color32 curCol = tm.m_lines.m_buffer[lid].m_color;
                    bool isBlack = (curCol.r == 0 && curCol.g == 0 && curCol.b == 0);
                    if (isBlack)
                    {
                        float hue = ((float)(lid * 137.508) % 360f) / 360f;
                        UnityEngine.Color genColor = UnityEngine.Color.HSVToRGB(hue, 0.78f, 0.95f);
                        tm.m_lines.m_buffer[lid].m_color = (UnityEngine.Color32)genColor;
                    }
                    tm.m_lines.m_buffer[lid].m_flags |= TransportLine.Flags.CustomColor;

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
                            Notification.ProblemStruct oldProb = nm.m_nodes.m_buffer[currStop].m_problems;
                            Notification.ProblemStruct newProb = Notification.RemoveProblems(oldProb, (Notification.Problem1)131072);
                            newProb = Notification.RemoveProblems(newProb, (Notification.Problem1)Notification.Problem1.FatalProblem);
                            if (oldProb != newProb)
                            {
                                nm.m_nodes.m_buffer[currStop].m_problems = newProb;
                                nm.UpdateNodeNotifications(currStop, oldProb, newProb);
                            }
                        }

                        for (int s = 0; s < 8; s++)
                        {
                            ushort segId = nm.m_nodes.m_buffer[currStop].GetSegment(s);
                            if (segId != 0 && segId < nm.m_segments.m_buffer.Length && (nm.m_segments.m_buffer[segId].m_flags & NetSegment.Flags.Created) != 0)
                            {
                                nm.m_segments.m_buffer[segId].m_flags &= ~NetSegment.Flags.PathFailed;
                            }
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

        public static void NetManager_ReleaseSegmentImplementation_Postfix(ushort segment)
        {
            if (segment == 0 || Singleton<NetManager>.instance == null || Singleton<NetManager>.instance.m_segments == null) return;
            NetManager_ReleaseSegmentImplementation_Postfix(segment, ref Singleton<NetManager>.instance.m_segments.m_buffer[segment], false);
        }

        public static void NetManager_ReleaseSegmentImplementation_Postfix(ushort segment, ref NetSegment data, bool keepNodes)
        {
            VehicleFixPatches.NetManager_ReleaseSegmentImplementation_Postfix(segment, ref data, keepNodes);
        }

        public static int CleanDemolishedRoadVehicles()
        {
            int released = 0;
            try
            {
                VehicleManager vm = Singleton<VehicleManager>.instance;
                PathManager pm = Singleton<PathManager>.instance;
                NetManager nm = Singleton<NetManager>.instance;
                if (vm == null || vm.m_vehicles == null || vm.m_vehicles.m_buffer == null ||
                    nm == null || nm.m_segments == null || nm.m_segments.m_buffer == null ||
                    pm == null || pm.m_pathUnits == null || pm.m_pathUnits.m_buffer == null)
                    return 0;

                int vCount = vm.m_vehicles.m_buffer.Length;
                int segCount = nm.m_segments.m_buffer.Length;
                int pathCount = pm.m_pathUnits.m_buffer.Length;

                for (int i = 1; i < vCount; i++)
                {
                    Vehicle.Flags flags = vm.m_vehicles.m_buffer[i].m_flags;
                    if ((flags & (Vehicle.Flags.Created | Vehicle.Flags.Spawned)) != (Vehicle.Flags.Created | Vehicle.Flags.Spawned))
                        continue;

                    if (vm.m_vehicles.m_buffer[i].m_leadingVehicle != 0)
                        continue;

                    VehicleInfo info = vm.m_vehicles.m_buffer[i].Info;
                    if (info == null || (info.m_vehicleType & (VehicleInfo.VehicleType.Plane | VehicleInfo.VehicleType.Ship | VehicleInfo.VehicleType.Rocket | VehicleInfo.VehicleType.Meteor)) != 0)
                        continue;

                    uint pathId = vm.m_vehicles.m_buffer[i].m_path;
                    byte pathIdx = vm.m_vehicles.m_buffer[i].m_pathPositionIndex;
                    bool shouldRelease = false;

                    if (pathId != 0 && pathId < (uint)pathCount)
                    {
                        int posIdx = (int)(pathIdx >> 1);
                        PathUnit.Position pPos;
                        if (pm.m_pathUnits.m_buffer[pathId].GetPosition(posIdx, out pPos))
                        {
                            if (pPos.m_segment == 0 || pPos.m_segment >= segCount ||
                                (nm.m_segments.m_buffer[pPos.m_segment].m_flags & NetSegment.Flags.Created) == 0)
                            {
                                shouldRelease = true;
                            }
                        }
                        else
                        {
                            shouldRelease = true;
                        }
                    }
                    else if (pathId == 0 && (flags & (Vehicle.Flags.Parking | Vehicle.Flags.WaitingLoading)) == 0)
                    {
                        if (vm.m_vehicles.m_buffer[i].GetLastFrameVelocity().sqrMagnitude < 0.001f && vm.m_vehicles.m_buffer[i].m_blockCounter >= 150)
                        {
                            shouldRelease = true;
                        }
                    }

                    if (shouldRelease)
                    {
                        if (pathId != 0)
                        {
                            try { pm.ReleasePath(pathId); } catch { }
                            vm.m_vehicles.m_buffer[i].m_path = 0;
                        }
                        vm.ReleaseVehicle((ushort)i);
                        released++;
                    }
                }
            }
            catch { }
            return released;
        }

        public static int UnstickFrozenVehicles()
        {
            int unblocked = 0;
            try
            {
                VehicleManager vm = Singleton<VehicleManager>.instance;
                PathManager pm = Singleton<PathManager>.instance;
                NetManager nm = Singleton<NetManager>.instance;
                if (vm == null || vm.m_vehicles == null || vm.m_vehicles.m_buffer == null ||
                    nm == null || nm.m_segments == null || nm.m_segments.m_buffer == null ||
                    pm == null || pm.m_pathUnits == null || pm.m_pathUnits.m_buffer == null)
                    return 0;

                int vCount = vm.m_vehicles.m_buffer.Length;
                int segCount = nm.m_segments.m_buffer.Length;
                int pathCount = pm.m_pathUnits.m_buffer.Length;

                for (ushort i = 1; i < vCount; i++)
                {
                    Vehicle.Flags flags = vm.m_vehicles.m_buffer[i].m_flags;
                    if ((flags & (Vehicle.Flags.Created | Vehicle.Flags.Spawned)) != (Vehicle.Flags.Created | Vehicle.Flags.Spawned))
                        continue;

                    if (vm.m_vehicles.m_buffer[i].m_leadingVehicle != 0)
                        continue;

                    VehicleInfo info = vm.m_vehicles.m_buffer[i].Info;
                    if (info == null || (info.m_vehicleType & (VehicleInfo.VehicleType.Plane | VehicleInfo.VehicleType.Ship | VehicleInfo.VehicleType.Rocket | VehicleInfo.VehicleType.Meteor)) != 0)
                        continue;

                    // Legitimate stops: parking, waiting to load/cargo, stopped at station/stop, arriving, or waiting for pathfind
                    if ((flags & (Vehicle.Flags.Parking | Vehicle.Flags.WaitingLoading | Vehicle.Flags.WaitingCargo | Vehicle.Flags.Stopped | Vehicle.Flags.Arriving | Vehicle.Flags.WaitingPath)) != 0)
                        continue;

                    uint pathId = vm.m_vehicles.m_buffer[i].m_path;
                    if (pathId != 0 && pathId < (uint)pathCount)
                    {
                        if (vm.m_vehicles.m_buffer[i].GetLastFrameVelocity().sqrMagnitude < 0.001f &&
                            (vm.m_vehicles.m_buffer[i].m_targetPos0.w < 0.1f || vm.m_vehicles.m_buffer[i].m_blockCounter > 10))
                        {
                            int posIdx = (int)(vm.m_vehicles.m_buffer[i].m_pathPositionIndex >> 1);
                            PathUnit.Position pPos;
                            if (pm.m_pathUnits.m_buffer[pathId].GetPosition(posIdx, out pPos))
                            {
                                if (pPos.m_segment != 0 && pPos.m_segment < segCount &&
                                    (nm.m_segments.m_buffer[pPos.m_segment].m_flags & NetSegment.Flags.Created) != 0)
                                {
                                    // Do not unstick if legitimately waiting at a red traffic light
                                    ushort startNode = nm.m_segments.m_buffer[pPos.m_segment].m_startNode;
                                    ushort endNode = nm.m_segments.m_buffer[pPos.m_segment].m_endNode;
                                    ushort targetNodeId = (pPos.m_offset > 128) ? endNode : startNode;
                                    if (targetNodeId != 0 && targetNodeId < nm.m_nodes.m_buffer.Length)
                                    {
                                        if ((nm.m_nodes.m_buffer[targetNodeId].m_flags & NetNode.Flags.TrafficLights) != 0)
                                        {
                                            RoadBaseAI.TrafficLightState vState, pState;
                                            RoadBaseAI.GetTrafficLightState(targetNodeId, ref nm.m_segments.m_buffer[pPos.m_segment], 
                                                Singleton<SimulationManager>.instance.m_currentFrameIndex - (uint)(targetNodeId << 8) / 32768u, 
                                                out vState, out pState);
                                            if (vState == RoadBaseAI.TrafficLightState.Red || vState == RoadBaseAI.TrafficLightState.GreenToRed)
                                            {
                                                continue; // Legitimate red light wait -> DO NOT touch!
                                            }
                                        }
                                    }

                                    vm.m_vehicles.m_buffer[i].m_blockCounter = 0;
                                    if (vm.m_vehicles.m_buffer[i].m_targetPos0.w < 0.1f) vm.m_vehicles.m_buffer[i].m_targetPos0.w = 1.5f;
                                    if (vm.m_vehicles.m_buffer[i].m_targetPos1.w < 0.1f) vm.m_vehicles.m_buffer[i].m_targetPos1.w = 1.5f;
                                    if (vm.m_vehicles.m_buffer[i].m_targetPos2.w < 0.1f) vm.m_vehicles.m_buffer[i].m_targetPos2.w = 1.5f;
                                    if (vm.m_vehicles.m_buffer[i].m_targetPos3.w < 0.1f) vm.m_vehicles.m_buffer[i].m_targetPos3.w = 1.5f;

                                    TMPECompatibility.UnstickVehicle(i);
                                    unblocked++;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return unblocked;
        }
    }

    public class MNC_ThreadingExtension : ThreadingExtensionBase
    {
        public override void OnUpdate(float realTimeDelta, float simulationTimeDelta)
        {
            // Hotkey: Ctrl + Shift + V (or Cmd + Shift + V on Mac) to clean stranded vehicles, unblock frozen queues, and reactivate city logistics
            if ((UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftControl) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightControl) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftCommand) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightCommand))
                && (UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftShift) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightShift))
                && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.V))
            {
                int cleaned = VehicleAndTransportRepair.CleanDemolishedRoadVehicles();
                int unblocked = VehicleAndTransportRepair.UnstickFrozenVehicles();
                int reactivated = VehicleAndTransportRepair.ReactivateCityLogistics();
                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Manual hotkey (Ctrl+Shift+V) executed: cleaned {0} dead vehicles, unblocked {1} frozen vehicles, reactivated {2} logistics entities.", cleaned, unblocked, reactivated));
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

                // 2b. Patch LoadingManager.<SaveSimulationData>c__IteratorC.MoveNext (Save diagnostics & exception tracking)
                try
                {
                    Type saveSimIterType = typeof(LoadingManager).GetNestedType("<SaveSimulationData>c__IteratorC", BindingFlags.NonPublic | BindingFlags.Public);
                    if (saveSimIterType != null)
                    {
                        MethodInfo moveNext = saveSimIterType.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.Instance);
                        if (moveNext != null)
                        {
                            MethodInfo pre = typeof(SaveSimulationDataSafetyPatch).GetMethod(nameof(SaveSimulationDataSafetyPatch.Prefix), BindingFlags.Public | BindingFlags.Static);
                            MethodInfo post = typeof(SaveSimulationDataSafetyPatch).GetMethod(nameof(SaveSimulationDataSafetyPatch.Postfix), BindingFlags.Public | BindingFlags.Static);
                            MethodInfo trans = typeof(SaveSimulationDataSafetyPatch).GetMethod(nameof(SaveSimulationDataSafetyPatch.Transpiler), BindingFlags.Public | BindingFlags.Static);
                            harmony.Patch(moveNext, prefix: new HarmonyMethod(pre), postfix: new HarmonyMethod(post), transpiler: new HarmonyMethod(trans));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched LoadingManager.<SaveSimulationData>c__IteratorC.MoveNext with diagnostic exception tracker and interceptor.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching SaveSimulationData iterator: " + ex);
                }

                // 2c. Patch DataSerializer.WriteUInt24 to clamp any value > 16,777,215 to 16,777,215
                try
                {
                    MethodInfo writeUInt24M = typeof(ColossalFramework.IO.DataSerializer).GetMethod("WriteUInt24", BindingFlags.Public | BindingFlags.Instance);
                    if (writeUInt24M != null)
                    {
                        MethodInfo pre = typeof(TransportLineCompatibility).GetMethod(nameof(DataSerializer_WriteUInt24_Prefix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(writeUInt24M, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched DataSerializer.WriteUInt24 with bounds clamp guard.");
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching DataSerializer.WriteUInt24: " + ex);
                }

                // 3. PreSaveSanitization in SimulationManager.Data.Serialize already sanitizes all Prefab indices before saving.
                // Do NOT patch PrefabCollection<T>.Serialize directly with Harmony because Mono generic sharing causes
                // cross-type trampoline collision where PrefabCollection<NetInfo>.m_encodedArray is null during BuildingManager serialization.

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

                // 6. CarAI and TramBaseAI SimulationStep: Native vehicle physics, braking, and TM:PE integration are untouched.

                // 7. Patch TransportLineAI.CheckNodeProblems to suppress false LineNotConnected warnings on stops
                try
                {
                    MethodInfo checkNodeProbM = typeof(TransportLineAI).GetMethod("CheckNodeProblems", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                    if (checkNodeProbM != null)
                    {
                        MethodInfo pre = typeof(TransportLineCompatibility).GetMethod(nameof(TransportLineAI_CheckNodeProblems_Prefix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(checkNodeProbM, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TransportLineAI.CheckNodeProblems (suppress LineNotConnected).");
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching TransportLineAI.CheckNodeProblems: " + ex);
                }

                // 8. Patch TransportLineAI.CheckSegmentProblems
                try
                {
                    MethodInfo checkSegProbM = typeof(TransportLineAI).GetMethod("CheckSegmentProblems", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                    if (checkSegProbM != null)
                    {
                        MethodInfo pre = typeof(TransportLineCompatibility).GetMethod(nameof(TransportLineAI_CheckSegmentProblems_Prefix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(checkSegProbM, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TransportLineAI.CheckSegmentProblems.");
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching TransportLineAI.CheckSegmentProblems: " + ex);
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
            try
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SimulationManager.Data.Serialize Prefix: Running PreSaveSanitization...");
                PreSaveSanitization();
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] PreSaveSanitization completed cleanly.");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[MoreNetworkCapacityRenew] PreSaveSanitization top-level exception: " + ex);
            }
        }

        public static Exception SimulationManager_Data_Serialize_Finalizer(Exception __exception)
        {
            if (__exception != null)
            {
                SaveSimulationDataSafetyPatch.LastSaveSimulationException = __exception;
                SaveSimulationDataSafetyPatch.LogSaveDebug("CRITICAL: Exception inside SimulationManager.Data.Serialize: " + __exception);
                UnityEngine.Debug.LogError("[MoreNetworkCapacityRenew] CRITICAL: Exception inside SimulationManager.Data.Serialize: " + __exception);
            }
            return __exception;
        }

        public static Exception DistrictManager_Data_Serialize_Finalizer(Exception __exception)
        {
            if (__exception != null)
            {
                SaveSimulationDataSafetyPatch.LastSaveSimulationException = __exception;
                SaveSimulationDataSafetyPatch.LogSaveDebug("CRITICAL: Exception inside DistrictManager.Data.Serialize: " + __exception);
                UnityEngine.Debug.LogError("[MoreNetworkCapacityRenew] CRITICAL: Exception inside DistrictManager.Data.Serialize: " + __exception);
            }
            return __exception;
        }

        public static void DataSerializer_WriteUInt24_Prefix(ref uint value)
        {
            if (value > 16777215u)
            {
                value = 16777215u;
            }
        }

        public static void TransportManager_Data_Serialize_Prefix(object __instance, ColossalFramework.IO.DataSerializer s)
        {
            try
            {
                SanitizeTransportLines();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TransportManager_Data_Serialize_Prefix error: " + ex);
            }
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
            try { ThirdPartyCompatibility.EnsureEightyOne2Patched(); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] PreSave EnsureEightyOne2 error: " + ex); }
            try { ArrayGuardian.ExpandAllArrays(); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] PreSave ArrayGuardian error: " + ex); }
            try { SanitizeDistricts(); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeDistricts error: " + ex); }
            try { SanitizeTransportLines(); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeTransportLines error: " + ex); }
            try { SanitizeVehicles(); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeVehicles error: " + ex); }
            try { SanitizeBuildings(); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeBuildings error: " + ex); }
            try { SanitizeNetElements(); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeNetElements error: " + ex); }
            try { SanitizeProps(); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeProps error: " + ex); }
            try { SanitizeTrees(); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeTrees error: " + ex); }
            try { SanitizeCitizenInstances(); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeCitizenInstances error: " + ex); }
            try { EconomyCompatibility.SanitizeEconomyAndTransport(); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] PreSave SanitizeEconomy error: " + ex); }
        }

        private static void ClampAgeData(ref DistrictAgeData data, ref int sanitized)
        {
            if (data.m_tempCount > 16777215u) { data.m_tempCount = 16777215u; sanitized++; }
            if (data.m_finalCount > 16777215u) { data.m_finalCount = 16777215u; sanitized++; }
        }

        private static void ClampPopData(ref DistrictPopulationData data, ref int sanitized)
        {
            if (data.m_tempCount > 16777215u) { data.m_tempCount = 16777215u; sanitized++; }
            if (data.m_finalCount > 16777215u) { data.m_finalCount = 16777215u; sanitized++; }
        }

        private static void ClampPrivateData(ref DistrictPrivateData data, ref int sanitized)
        {
            if (data.m_tempLevel1 > 16777215u) { data.m_tempLevel1 = 16777215u; sanitized++; }
            if (data.m_tempLevel2 > 16777215u) { data.m_tempLevel2 = 16777215u; sanitized++; }
            if (data.m_tempLevel3 > 16777215u) { data.m_tempLevel3 = 16777215u; sanitized++; }
            if (data.m_tempLevel4 > 16777215u) { data.m_tempLevel4 = 16777215u; sanitized++; }
            if (data.m_tempLevel5 > 16777215u) { data.m_tempLevel5 = 16777215u; sanitized++; }
            if (data.m_tempBuildingArea > 16777215u) { data.m_tempBuildingArea = 16777215u; sanitized++; }
            if (data.m_finalBuildingArea > 16777215u) { data.m_finalBuildingArea = 16777215u; sanitized++; }
            if (data.m_tempAliveCount > 16777215u) { data.m_tempAliveCount = 16777215u; sanitized++; }
            if (data.m_finalAliveCount > 16777215u) { data.m_finalAliveCount = 16777215u; sanitized++; }
        }

        private static void ClampEducationData(ref DistrictEducationData data, ref int sanitized)
        {
            if (data.m_tempCount > 16777215u) { data.m_tempCount = 16777215u; sanitized++; }
            if (data.m_tempUnemployed > 16777215u) { data.m_tempUnemployed = 16777215u; sanitized++; }
            if (data.m_tempHomeless > 16777215u) { data.m_tempHomeless = 16777215u; sanitized++; }
            if (data.m_finalCount > 16777215u) { data.m_finalCount = 16777215u; sanitized++; }
            if (data.m_finalUnemployed > 16777215u) { data.m_finalUnemployed = 16777215u; sanitized++; }
            if (data.m_finalHomeless > 16777215u) { data.m_finalHomeless = 16777215u; sanitized++; }
            if (data.m_tempEligibleWorkers > 16777215u) { data.m_tempEligibleWorkers = 16777215u; sanitized++; }
            if (data.m_finalEligibleWorkers > 16777215u) { data.m_finalEligibleWorkers = 16777215u; sanitized++; }
        }

        private static void ClampTouristData(ref DistrictTouristData data, ref int sanitized)
        {
            if (data.m_tempVisits > 16777215u) { data.m_tempVisits = 16777215u; sanitized++; }
            if (data.m_finalVisits > 16777215u) { data.m_finalVisits = 16777215u; sanitized++; }
            if (data.m_averageVisits > 16777215u) { data.m_averageVisits = 16777215u; sanitized++; }
            if (data.m_tempCount > 16777215u) { data.m_tempCount = 16777215u; sanitized++; }
            if (data.m_finalCount > 16777215u) { data.m_finalCount = 16777215u; sanitized++; }
        }

        public static int SanitizeDistricts()
        {
            int sanitized = 0;
            try
            {
                DistrictManager dm = Singleton<DistrictManager>.instance;
                if (dm == null || dm.m_districts == null || dm.m_districts.m_buffer == null) return 0;

                for (int i = 0; i < dm.m_districts.m_buffer.Length; i++)
                {
                    if (dm.m_districts.m_buffer[i].m_flags != District.Flags.None)
                    {
                        // All 23 DistrictAgeData fields
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_childData, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_teenData, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_youngData, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_adultData, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_seniorData, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_childHealthData, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_seniorHealthData, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_childSickData, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_seniorSickData, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_deadSeniorsData, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_ageAtDeathData, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_elementaryEligibleData, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_highschoolEligibleData, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_universityEligibleData, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_birthData, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_deathData, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_education1Data, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_education2Data, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_education3Data, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_student1Data, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_student2Data, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_student3Data, ref sanitized);
                        ClampAgeData(ref dm.m_districts.m_buffer[i].m_libraryVisitorData, ref sanitized);

                        // Population Data
                        ClampPopData(ref dm.m_districts.m_buffer[i].m_populationData, ref sanitized);

                        // Education Data (4 fields)
                        ClampEducationData(ref dm.m_districts.m_buffer[i].m_educated0Data, ref sanitized);
                        ClampEducationData(ref dm.m_districts.m_buffer[i].m_educated1Data, ref sanitized);
                        ClampEducationData(ref dm.m_districts.m_buffer[i].m_educated2Data, ref sanitized);
                        ClampEducationData(ref dm.m_districts.m_buffer[i].m_educated3Data, ref sanitized);

                        // Tourist Data (3 fields)
                        ClampTouristData(ref dm.m_districts.m_buffer[i].m_tourist1Data, ref sanitized);
                        ClampTouristData(ref dm.m_districts.m_buffer[i].m_tourist2Data, ref sanitized);
                        ClampTouristData(ref dm.m_districts.m_buffer[i].m_tourist3Data, ref sanitized);

                        // Private Data (6 fields)
                        ClampPrivateData(ref dm.m_districts.m_buffer[i].m_residentialData, ref sanitized);
                        ClampPrivateData(ref dm.m_districts.m_buffer[i].m_commercialData, ref sanitized);
                        ClampPrivateData(ref dm.m_districts.m_buffer[i].m_visitorData, ref sanitized);
                        ClampPrivateData(ref dm.m_districts.m_buffer[i].m_industrialData, ref sanitized);
                        ClampPrivateData(ref dm.m_districts.m_buffer[i].m_officeData, ref sanitized);
                        ClampPrivateData(ref dm.m_districts.m_buffer[i].m_playerData, ref sanitized);
                    }
                }
                if (sanitized > 0)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] SanitizeDistricts clamped {0} out-of-bounds UInt24 counters in districts.", sanitized));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeDistricts error: " + ex);
            }
            return sanitized;
        }

        public static int SanitizeTransportLines()
        {
            int sanitized = 0;
            try
            {
                TransportManager tm = Singleton<TransportManager>.instance;
                if (tm == null || tm.m_lines == null || tm.m_lines.m_buffer == null) return 0;

                int prefabCount = PrefabCollection<TransportInfo>.PrefabCount();
                int vCount = PrefabCollection<VehicleInfo>.PrefabCount();

                // Sanitize vehicle selection indices to prevent NRE during serialization
                FieldInfo fTLIndices = typeof(TransportManager).GetField("m_transportLineVehicleSelectionIndices", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var tlIndices = (fTLIndices != null) ? fTLIndices.GetValue(tm) as Dictionary<ushort, int> : null;
                FieldInfo fTLNames = typeof(TransportManager).GetField("m_transportLineVehicleSelection", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var tlNames = (fTLNames != null) ? fTLNames.GetValue(tm) as Dictionary<ushort, string> : null;
                if (tlIndices != null)
                {
                    List<ushort> toRemove = null;
                    foreach (var kvp in tlIndices)
                    {
                        if (kvp.Value < 0 || kvp.Value >= vCount || PrefabCollection<VehicleInfo>.GetPrefab((uint)kvp.Value) == null)
                        {
                            if (toRemove == null) toRemove = new List<ushort>();
                            toRemove.Add(kvp.Key);
                        }
                    }
                    if (toRemove != null)
                    {
                        foreach (var key in toRemove)
                        {
                            tlIndices.Remove(key);
                            if (tlNames != null) tlNames.Remove(key);
                            sanitized++;
                        }
                    }
                }

                VehicleManager vm = Singleton<VehicleManager>.instance;
                int vBufLen = (vm != null && vm.m_vehicles != null && vm.m_vehicles.m_buffer != null) ? vm.m_vehicles.m_buffer.Length : 0;

                for (int i = 0; i < tm.m_lines.m_buffer.Length; i++)
                {
                    if (tm.m_lines.m_buffer[i].m_flags != TransportLine.Flags.None)
                    {
                        bool invalid = false;
                        ushort infoIndex = tm.m_lines.m_buffer[i].m_infoIndex;
                        if (infoIndex >= prefabCount || PrefabCollection<TransportInfo>.GetPrefab(infoIndex) == null)
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
                            if (vBufLen > 0)
                            {
                                ushort vHead = tm.m_lines.m_buffer[i].m_vehicles;
                                if (vHead != 0)
                                {
                                    if (vHead >= vBufLen || (vm.m_vehicles.m_buffer[vHead].m_flags & Vehicle.Flags.Created) == 0)
                                    {
                                        tm.m_lines.m_buffer[i].m_vehicles = 0;
                                        sanitized++;
                                    }
                                }
                            }
                        }
                    }
                }
                if (sanitized > 0)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] SanitizeTransportLines: Cleared/sanitized {0} corrupted transport line items.", sanitized));
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
                    if (vm.m_vehicles.m_buffer[i].m_flags != 0)
                    {
                        ushort infoIndex = vm.m_vehicles.m_buffer[i].m_infoIndex;
                        if (infoIndex >= prefabCount || PrefabCollection<VehicleInfo>.GetPrefab(infoIndex) == null)
                        {
                            try { vm.m_vehicles.m_buffer[i].Unspawn((ushort)i); } catch { }
                            vm.m_vehicles.m_buffer[i].m_flags = (Vehicle.Flags)0;
                            vm.m_vehicles.m_buffer[i].m_infoIndex = 0;
                            cleared++;
                        }
                    }
                }

                if (vm.m_parkedVehicles != null && vm.m_parkedVehicles.m_buffer != null)
                {
                    for (int i = 1; i < vm.m_parkedVehicles.m_buffer.Length; i++)
                    {
                        if (vm.m_parkedVehicles.m_buffer[i].m_flags != 0)
                        {
                            ushort infoIndex = vm.m_parkedVehicles.m_buffer[i].m_infoIndex;
                            if (infoIndex >= prefabCount || PrefabCollection<VehicleInfo>.GetPrefab(infoIndex) == null)
                            {
                                vm.m_parkedVehicles.m_buffer[i].m_flags = 0;
                                vm.m_parkedVehicles.m_buffer[i].m_infoIndex = 0;
                                cleared++;
                            }
                        }
                    }
                }

                if (cleared > 0)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] SanitizeVehicles: Cleared {0} vehicles/parked with invalid prefabs.", cleared));
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
                int vCount = PrefabCollection<VehicleInfo>.PrefabCount();

                // Sanitize vehicle selection indices to prevent serialization crashes
                FieldInfo fSBIndices = typeof(BuildingManager).GetField("m_serviceBuildingVehicleSelectionIndices", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var sbIndices = (fSBIndices != null) ? fSBIndices.GetValue(bm) as Dictionary<ushort, int> : null;
                FieldInfo fSBNames = typeof(BuildingManager).GetField("m_serviceBuildingVehicleSelection", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var sbNames = (fSBNames != null) ? fSBNames.GetValue(bm) as Dictionary<ushort, string> : null;
                if (sbIndices != null)
                {
                    List<ushort> toRemove = null;
                    foreach (var kvp in sbIndices)
                    {
                        if (kvp.Value < 0 || kvp.Value >= vCount || PrefabCollection<VehicleInfo>.GetPrefab((uint)kvp.Value) == null)
                        {
                            if (toRemove == null) toRemove = new List<ushort>();
                            toRemove.Add(kvp.Key);
                        }
                    }
                    if (toRemove != null)
                    {
                        foreach (var key in toRemove)
                        {
                            sbIndices.Remove(key);
                            if (sbNames != null) sbNames.Remove(key);
                            cleared++;
                        }
                    }
                }

                VehicleManager vm = Singleton<VehicleManager>.instance;
                int vBufLen = (vm != null && vm.m_vehicles != null && vm.m_vehicles.m_buffer != null) ? vm.m_vehicles.m_buffer.Length : 0;

                for (int i = 1; i < bm.m_buildings.m_buffer.Length; i++)
                {
                    if (bm.m_buildings.m_buffer[i].m_flags != Building.Flags.None)
                    {
                        ushort infoIndex = bm.m_buildings.m_buffer[i].m_infoIndex;
                        if (infoIndex >= prefabCount || PrefabCollection<BuildingInfo>.GetPrefab(infoIndex) == null)
                        {
                            bm.m_buildings.m_buffer[i].m_flags = Building.Flags.None;
                            bm.m_buildings.m_buffer[i].m_infoIndex = 0;
                            cleared++;
                        }
                        else if (vBufLen > 0)
                        {
                            ushort own = bm.m_buildings.m_buffer[i].m_ownVehicles;
                            if (own != 0 && (own >= vBufLen || (vm.m_vehicles.m_buffer[own].m_flags & Vehicle.Flags.Created) == 0))
                            {
                                bm.m_buildings.m_buffer[i].m_ownVehicles = 0;
                                cleared++;
                            }
                            ushort guest = bm.m_buildings.m_buffer[i].m_guestVehicles;
                            if (guest != 0 && (guest >= vBufLen || (vm.m_vehicles.m_buffer[guest].m_flags & Vehicle.Flags.Created) == 0))
                            {
                                bm.m_buildings.m_buffer[i].m_guestVehicles = 0;
                                cleared++;
                            }
                        }
                    }
                }
                if (cleared > 0)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] SanitizeBuildings: Cleared/sanitized {0} building items.", cleared));
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

                int netCount = PrefabCollection<NetInfo>.PrefabCount();
                int treeCount = PrefabCollection<TreeInfo>.PrefabCount();

                for (int i = 1; i < nm.m_segments.m_buffer.Length; i++)
                {
                    if (nm.m_segments.m_buffer[i].m_flags != NetSegment.Flags.None)
                    {
                        ushort infoIndex = nm.m_segments.m_buffer[i].m_infoIndex;
                        if (infoIndex >= netCount || PrefabCollection<NetInfo>.GetPrefab(infoIndex) == null)
                        {
                            nm.m_segments.m_buffer[i].m_flags = NetSegment.Flags.None;
                            nm.m_segments.m_buffer[i].m_infoIndex = 0;
                            cleared++;
                        }
                        else
                        {
                            if (nm.m_segments.m_buffer[i].m_treeInfoIndex != 65535)
                            {
                                if (nm.m_segments.m_buffer[i].m_treeInfoIndex >= treeCount || PrefabCollection<TreeInfo>.GetPrefab(nm.m_segments.m_buffer[i].m_treeInfoIndex) == null)
                                {
                                    nm.m_segments.m_buffer[i].m_treeInfoIndex = 65535;
                                    cleared++;
                                }
                            }
                            if (nm.m_segments.m_buffer[i].m_leftFenceInfoIndex != 65535)
                            {
                                if (nm.m_segments.m_buffer[i].m_leftFenceInfoIndex >= netCount || PrefabCollection<NetInfo>.GetPrefab(nm.m_segments.m_buffer[i].m_leftFenceInfoIndex) == null)
                                {
                                    nm.m_segments.m_buffer[i].m_leftFenceInfoIndex = 65535;
                                    cleared++;
                                }
                            }
                            if (nm.m_segments.m_buffer[i].m_rightFenceInfoIndex != 65535)
                            {
                                if (nm.m_segments.m_buffer[i].m_rightFenceInfoIndex >= netCount || PrefabCollection<NetInfo>.GetPrefab(nm.m_segments.m_buffer[i].m_rightFenceInfoIndex) == null)
                                {
                                    nm.m_segments.m_buffer[i].m_rightFenceInfoIndex = 65535;
                                    cleared++;
                                }
                            }
                        }
                    }
                }

                for (int i = 1; i < nm.m_nodes.m_buffer.Length; i++)
                {
                    if (nm.m_nodes.m_buffer[i].m_flags != NetNode.Flags.None)
                    {
                        ushort infoIndex = nm.m_nodes.m_buffer[i].m_infoIndex;
                        if (infoIndex >= netCount || PrefabCollection<NetInfo>.GetPrefab(infoIndex) == null)
                        {
                            nm.m_nodes.m_buffer[i].m_flags = NetNode.Flags.None;
                            nm.m_nodes.m_buffer[i].m_infoIndex = 0;
                            cleared++;
                        }
                    }
                }

                if (cleared > 0)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] SanitizeNetElements: Cleared/sanitized {0} net elements with invalid prefabs or fence indices.", cleared));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeNetElements error: " + ex);
            }
            return cleared;
        }

        public static int SanitizeProps()
        {
            int cleared = 0;
            try
            {
                PropManager pm = Singleton<PropManager>.instance;
                if (pm == null || pm.m_props == null || pm.m_props.m_buffer == null) return 0;

                int propCount = PrefabCollection<PropInfo>.PrefabCount();
                for (int i = 1; i < pm.m_props.m_buffer.Length; i++)
                {
                    if (pm.m_props.m_buffer[i].m_flags != 0)
                    {
                        ushort infoIndex = pm.m_props.m_buffer[i].m_infoIndex;
                        if (infoIndex >= propCount || PrefabCollection<PropInfo>.GetPrefab(infoIndex) == null)
                        {
                            pm.m_props.m_buffer[i].m_flags = 0;
                            pm.m_props.m_buffer[i].m_infoIndex = 0;
                            cleared++;
                        }
                    }
                }
                if (cleared > 0)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] SanitizeProps: Cleared {0} props with invalid prefabs.", cleared));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeProps error: " + ex);
            }
            return cleared;
        }

        public static int SanitizeTrees()
        {
            int cleared = 0;
            try
            {
                TreeManager tm = Singleton<TreeManager>.instance;
                if (tm == null || tm.m_trees == null || tm.m_trees.m_buffer == null) return 0;

                int treeCount = PrefabCollection<TreeInfo>.PrefabCount();
                for (int i = 1; i < tm.m_trees.m_buffer.Length; i++)
                {
                    if (tm.m_trees.m_buffer[i].m_flags != 0)
                    {
                        ushort infoIndex = tm.m_trees.m_buffer[i].m_infoIndex;
                        if (infoIndex >= treeCount || PrefabCollection<TreeInfo>.GetPrefab(infoIndex) == null)
                        {
                            tm.m_trees.m_buffer[i].m_flags = 0;
                            tm.m_trees.m_buffer[i].m_infoIndex = 0;
                            cleared++;
                        }
                    }
                }
                if (cleared > 0)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] SanitizeTrees: Cleared {0} trees with invalid prefabs.", cleared));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeTrees error: " + ex);
            }
            return cleared;
        }

        public static int SanitizeCitizenInstances()
        {
            int cleared = 0;
            try
            {
                CitizenManager cm = Singleton<CitizenManager>.instance;
                if (cm == null || cm.m_instances == null || cm.m_instances.m_buffer == null) return 0;

                int citizenInfoCount = PrefabCollection<CitizenInfo>.PrefabCount();
                for (int i = 1; i < cm.m_instances.m_buffer.Length; i++)
                {
                    if (cm.m_instances.m_buffer[i].m_flags != CitizenInstance.Flags.None)
                    {
                        ushort infoIndex = cm.m_instances.m_buffer[i].m_infoIndex;
                        if (infoIndex >= citizenInfoCount || PrefabCollection<CitizenInfo>.GetPrefab(infoIndex) == null)
                        {
                            cm.m_instances.m_buffer[i].m_flags = CitizenInstance.Flags.None;
                            cm.m_instances.m_buffer[i].m_infoIndex = 0;
                            cleared++;
                        }
                    }
                }
                if (cleared > 0)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] SanitizeCitizenInstances: Cleared {0} citizen instances with invalid prefabs.", cleared));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeCitizenInstances error: " + ex);
            }
            return cleared;
        }

        public static int SanitizeCitizenUnits()
        {
            return 0;
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
                            VehicleManager vm = Singleton<VehicleManager>.instance;
                            if (vm != null && vm.m_vehicles != null && vm.m_vehicles.m_buffer != null)
                            {
                                ushort vHead = tm.m_lines.m_buffer[i].m_vehicles;
                                int vCount = 0;
                                while (vHead != 0 && vHead < vm.m_vehicles.m_buffer.Length && ++vCount < 16384)
                                {
                                    ushort nextV = vm.m_vehicles.m_buffer[vHead].m_nextLineVehicle;
                                    try { vm.ReleaseVehicle(vHead); } catch { }
                                    vHead = nextV;
                                }
                            }
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
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] IPT_Init_Prefix: Running expanded initialization for ImprovedPublicTransport...");
                SanitizeTransportLines();

                // 1. Get CachedTransportLineData type
                Type ctld = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string aname = asm.GetName().Name;
                    if (aname == "ImprovedPublicTransport3" || aname == "ImprovedPublicTransport2" || aname == "IPTEssentials")
                    {
                        ctld = asm.GetType("ImprovedPublicTransport.Data.CachedTransportLineData")
                            ?? asm.GetType("ImprovedPublicTransport2.Data.CachedTransportLineData");
                        if (ctld != null) break;
                    }
                }
                if (ctld == null) return true;

                FieldInfo lineDataField = ctld.GetField("_lineData", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
                FieldInfo initField = ctld.GetField("_init", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
                if (lineDataField == null) return true;

                TransportManager tm = Singleton<TransportManager>.instance;
                NetManager nm = Singleton<NetManager>.instance;
                int targetLen = (tm != null && tm.m_lines != null && tm.m_lines.m_buffer != null) ? tm.m_lines.m_buffer.Length : 1024;
                if (targetLen < 256) targetLen = 256;

                // Call TryLoadData(ref _lineData)
                MethodInfo tryLoadMethod = ctld.GetMethod("TryLoadData", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
                Array lineData = lineDataField.GetValue(null) as Array;
                object[] args = new object[] { lineData };
                bool loaded = false;
                if (tryLoadMethod != null)
                {
                    try
                    {
                        loaded = (bool)tryLoadMethod.Invoke(null, args);
                        lineData = args[0] as Array;
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] IPT TryLoadData error: " + ex.Message);
                    }
                }

                // Expand lineData array to targetLen (1024)
                Type elemType = lineDataField.FieldType.GetElementType();
                Array expandedData = Array.CreateInstance(elemType, targetLen);
                if (lineData != null)
                {
                    int copyLen = Math.Min(lineData.Length, targetLen);
                    Array.Copy(lineData, expandedData, copyLen);
                }
                lineDataField.SetValue(null, expandedData);

                // Get Options
                int defaultVehicleCount = 5;
                bool defaultBudgetControl = false;
                bool defaultUnbunching = true;
                try
                {
                    Type optionsWrapperType = ctld.Assembly.GetType("ImprovedPublicTransport.OptionsFramework.OptionsWrapper`1");
                    Type settingsType = ctld.Assembly.GetType("ImprovedPublicTransport.Settings.Settings");
                    if (optionsWrapperType != null && settingsType != null)
                    {
                        Type specificWrapper = optionsWrapperType.MakeGenericType(settingsType);
                        PropertyInfo optionsProp = specificWrapper.GetProperty("Options", BindingFlags.Public | BindingFlags.Static);
                        object options = optionsProp != null ? optionsProp.GetValue(null, null) : null;
                        if (options != null)
                        {
                            PropertyInfo dvc = settingsType.GetProperty("DefaultVehicleCount");
                            if (dvc != null) defaultVehicleCount = (int)dvc.GetValue(options, null);
                            PropertyInfo bc = settingsType.GetProperty("BudgetControl");
                            if (bc != null) defaultBudgetControl = ((int)bc.GetValue(options, null)) == 1;
                            PropertyInfo ub = settingsType.GetProperty("Unbunching");
                            if (ub != null) defaultUnbunching = (bool)ub.GetValue(options, null);
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] IPT Options read warning: " + ex.Message);
                }

                MethodInfo setTargetVehCount = elemType.GetMethod("set_TargetVehicleCount");
                MethodInfo setBudgetControl = elemType.GetMethod("set_BudgetControl");
                MethodInfo setDepot = elemType.GetMethod("set_Depot");
                MethodInfo setUnbunching = elemType.GetMethod("set_Unbunching");
                MethodInfo getPrefabs = elemType.GetMethod("get_Prefabs");
                MethodInfo setPrefabs = elemType.GetMethod("set_Prefabs");
                MethodInfo getQueuedVehicles = elemType.GetMethod("get_QueuedVehicles");
                MethodInfo setQueuedVehicles = elemType.GetMethod("set_QueuedVehicles");

                Type hashSetType = typeof(HashSet<string>);
                Type queueType = typeof(Queue<string>);

                Type tlUtil = ctld.Assembly.GetType("ImprovedPublicTransport.Util.TransportLineUtil");
                MethodInfo countVehMethod = tlUtil != null ? tlUtil.GetMethod("CountLineActiveVehicles", BindingFlags.Public | BindingFlags.Static) : null;
                Type depotUtil = ctld.Assembly.GetType("ImprovedPublicTransport.Util.DepotUtil");
                MethodInfo closestDepotMethod = depotUtil != null ? depotUtil.GetMethod("GetClosestDepot", BindingFlags.Public | BindingFlags.Static) : null;

                int startIndex = loaded ? (lineData != null ? lineData.Length : 256) : 0;
                if (!loaded)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Initializing default transport line data for {0} slots.", targetLen));
                }
                else
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Preserving loaded transport line data (0..{0}) and initializing slots {0}..{1}.", startIndex, targetLen));
                }

                for (int i = startIndex; i < targetLen; i++)
                {
                    try
                    {
                        object entry = expandedData.GetValue(i);
                        if (entry == null && elemType != null)
                        {
                            entry = Activator.CreateInstance(elemType);
                        }
                        if (entry == null) continue;

                        ushort lid = (ushort)i;
                        bool isComplete = tm != null && tm.m_lines != null && tm.m_lines.m_buffer != null && i < tm.m_lines.m_buffer.Length && tm.m_lines.m_buffer[i].Complete;

                        int targetVehCount = defaultVehicleCount;
                        if (isComplete && countVehMethod != null)
                        {
                            try
                            {
                                object[] countArgs = new object[] { lid, 0, null };
                                int active = (int)countVehMethod.Invoke(null, countArgs);
                                if (active > 0) targetVehCount = active;
                                else targetVehCount = Math.Max(defaultVehicleCount, 1);
                            }
                            catch { }
                        }

                        ushort depot = 0;
                        if (closestDepotMethod != null && isComplete && nm != null && nm.m_nodes != null && nm.m_nodes.m_buffer != null)
                        {
                            try
                            {
                                ushort stop = tm.m_lines.m_buffer[i].GetStop(0);
                                if (stop < nm.m_nodes.m_buffer.Length)
                                {
                                    UnityEngine.Vector3 pos = nm.m_nodes.m_buffer[stop].m_position;
                                    depot = (ushort)closestDepotMethod.Invoke(null, new object[] { lid, pos });
                                }
                            }
                            catch { }
                        }

                        if (setTargetVehCount != null) setTargetVehCount.Invoke(entry, new object[] { targetVehCount });
                        if (setBudgetControl != null) setBudgetControl.Invoke(entry, new object[] { defaultBudgetControl });
                        if (setDepot != null) setDepot.Invoke(entry, new object[] { depot });
                        if (setUnbunching != null) setUnbunching.Invoke(entry, new object[] { defaultUnbunching });
                        if (getPrefabs != null && setPrefabs != null && getPrefabs.Invoke(entry, null) == null)
                        {
                            setPrefabs.Invoke(entry, new object[] { Activator.CreateInstance(hashSetType) });
                        }
                        if (getQueuedVehicles != null && setQueuedVehicles != null && getQueuedVehicles.Invoke(entry, null) == null)
                        {
                            setQueuedVehicles.Invoke(entry, new object[] { Activator.CreateInstance(queueType) });
                        }

                        expandedData.SetValue(entry, i);
                    }
                    catch { }
                }

                // Also ensure entries 0..startIndex have non-null Prefabs and QueuedVehicles
                for (int i = 0; i < startIndex; i++)
                {
                    try
                    {
                        object entry = expandedData.GetValue(i);
                        if (entry == null) continue;

                        bool modified = false;
                        if (getPrefabs != null && setPrefabs != null && getPrefabs.Invoke(entry, null) == null)
                        {
                            setPrefabs.Invoke(entry, new object[] { Activator.CreateInstance(hashSetType) });
                            modified = true;
                        }
                        if (getQueuedVehicles != null && setQueuedVehicles != null && getQueuedVehicles.Invoke(entry, null) == null)
                        {
                            setQueuedVehicles.Invoke(entry, new object[] { Activator.CreateInstance(queueType) });
                            modified = true;
                        }
                        if (modified) expandedData.SetValue(entry, i);
                    }
                    catch { }
                }

                // Register OnSaveData event
                Type sdeType = ctld.Assembly.GetType("ImprovedPublicTransport.Data.SerializableDataExtension");
                if (sdeType != null)
                {
                    FieldInfo instField = sdeType.GetField("instance", BindingFlags.Public | BindingFlags.Static);
                    object sdeInst = instField != null ? instField.GetValue(null) : null;
                    if (sdeInst != null)
                    {
                        MethodInfo onSave = ctld.GetMethod("OnSaveData", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
                        System.Reflection.EventInfo evSave = sdeType.GetEvent("EventSaveData");
                        if (onSave != null && evSave != null)
                        {
                            Delegate del = Delegate.CreateDelegate(evSave.EventHandlerType, onSave);
                            evSave.AddEventHandler(sdeInst, del);
                        }
                    }
                }

                if (initField != null) initField.SetValue(null, true);
                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Successfully initialized ImprovedPublicTransport for all {0} lines!", targetLen));
                return false; // Skip the original broken Init()
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] IPT_Init_Prefix custom init error: " + ex);
                return true; // fallback
            }
        }


        public static bool TransportLineAI_CheckNodeProblems_Prefix(ushort nodeID, ref NetNode data)
        {
            if (data.m_transportLine != 0)
            {
                data.m_problems = Notification.RemoveProblems(data.m_problems, (Notification.Problem1)131072);
                data.m_problems = Notification.RemoveProblems(data.m_problems, (Notification.Problem1)Notification.Problem1.FatalProblem);
                return false;
            }
            return true;
        }

        public static bool TransportLineAI_CheckSegmentProblems_Prefix(ushort segmentID, ref NetSegment data)
        {
            data.m_flags &= ~NetSegment.Flags.PathFailed;
            return true;
        }
    }

    // ============================================================================
    // VEHICLE POOL MONITOR - Vehicle Count Health Checking
    // ============================================================================
    /// <summary>
    /// Monitors VehicleManager.m_vehicleCount for corruption.
    /// Detects spawn throttling and repairs vehicle count mismatches.
    /// Runs every 60 simulation frames (~1 second @ 60fps)
    /// </summary>
    public static class VehiclePoolMonitor
    {
        // ========================================================================
        // FIELDS
        // ========================================================================
        
        // State tracking
        private static int lastActualCount = 0;
        private static uint lastCheckFrame = 0;
        private static int consecutiveStaticChecks = 0;
        
        // Configuration
        private static readonly int checkIntervalFrames = 1800; // Check every 30 seconds
        private static readonly int staticThreshold = 120; // 120 consecutive static counts (~2 mins)
        
        // Diagnostics
        private static int totalChecks = 0;
        private static int countMismatches = 0;
        private static int throttlingDetections = 0;
        private static int repairsPerformed = 0;
        private static DateTime lastRepairTime = DateTime.MinValue;
        
        // ========================================================================
        // PUBLIC API
        // ========================================================================
        
        /// <summary>
        /// Initialize and perform initial check
        /// </summary>
        public static void Initialize()
        {
            try
            {
                VehicleManager vm = Singleton<VehicleManager>.instance;
                if (vm == null) return;
                
                int actualCount = CountActualVehicles(vm);
                int reportedCount = vm.m_vehicleCount;
                
                UnityEngine.Debug.Log(string.Format(
                    "[VehiclePoolMonitor] Initialized. Reported={0}, Actual={1}, Buffer size={2}",
                    reportedCount, actualCount, vm.m_vehicles.m_buffer.Length));
                
                if (Math.Abs(actualCount - reportedCount) > 10)
                {
                    UnityEngine.Debug.LogWarning("[VehiclePoolMonitor] Initial count mismatch detected! Repairing...");
                    HandleCountMismatch(vm, actualCount, reportedCount);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[VehiclePoolMonitor] Initialize error: " + ex);
            }
        }
        
        /// <summary>
        /// Check and repair vehicle count. Called from SimulationStep Harmony patch.
        /// </summary>
        public static void CheckAndRepair()
        {
            try
            {
                VehicleManager vm = Singleton<VehicleManager>.instance;
                if (vm == null) return;
                
                SimulationManager sm = Singleton<SimulationManager>.instance;
                if (sm == null) return;
                
                uint currentFrame = sm.m_currentFrameIndex;
                
                // Only check every N frames
                if (currentFrame - lastCheckFrame < checkIntervalFrames)
                {
                    return;
                }
                
                lastCheckFrame = currentFrame;
                totalChecks++;
                
                // Perform checks
                int actualCount = CountActualVehicles(vm);
                int reportedCount = vm.m_vehicleCount;
                
                // Check 1: Count mismatch (only repair if significant divergence > 10)
                if (Math.Abs(actualCount - reportedCount) > 10)
                {
                    HandleCountMismatch(vm, actualCount, reportedCount);
                }
                
                // Check 2: Spawn throttling detection
                if (DetectSpawnThrottling(vm, actualCount))
                {
                    HandleSpawnThrottling(vm);
                }
                
                lastActualCount = actualCount;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[VehiclePoolMonitor] CheckAndRepair error: " + ex);
            }
        }
        
        /// <summary>
        /// Get status for diagnostics UI
        /// </summary>
        public static string GetStatus()
        {
            VehicleManager vm = Singleton<VehicleManager>.instance;
            if (vm == null) return "Vehicle Manager: Not loaded";
            
            int actual = CountActualVehicles(vm);
            int reported = vm.m_vehicleCount;
            bool mismatch = actual != reported;
            
            return string.Format(
                "Vehicles: {0} / {1} {2} | Checks: {3} | Mismatches: {4} | Throttling: {5} | Last repair: {6}",
                actual,
                vm.m_vehicles.m_buffer.Length,
                mismatch ? "⚠ MISMATCH" : "✓",
                totalChecks,
                countMismatches,
                throttlingDetections,
                lastRepairTime == DateTime.MinValue ? "Never" : FormatTimeSince(lastRepairTime));
        }
        
        // ========================================================================
        // COUNT ACTUAL VEHICLES
        // ========================================================================
        
        /// <summary>
        /// Count vehicles with Created flag in buffer.
        /// This is the ground truth vs VehicleManager.m_vehicleCount.
        /// Slot 0 is reserved/null in Cities: Skylines, so start at 1.
        /// </summary>
        public static int CountActualVehicles(VehicleManager vm)
        {
            int count = 0;
            
            try
            {
                if (vm != null && vm.m_vehicles != null && vm.m_vehicles.m_buffer != null)
                {
                    Vehicle[] buffer = vm.m_vehicles.m_buffer;
                    for (int i = 1; i < buffer.Length; i++)
                    {
                        if ((buffer[i].m_flags & Vehicle.Flags.Created) != 0)
                        {
                            count++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[VehiclePoolMonitor] CountActualVehicles error: " + ex);
            }
            
            return count;
        }
        
        // ========================================================================
        // MISMATCH HANDLING
        // ========================================================================
        
        /// <summary>
        /// Handle mismatch between reported and actual count
        /// </summary>
        private static void HandleCountMismatch(VehicleManager vm, int actual, int reported)
        {
            countMismatches++;
            
            UnityEngine.Debug.LogWarning(string.Format(
                "[VehiclePoolMonitor] Count mismatch detected! Reported: {0}, Actual: {1}, Diff: {2}",
                reported, actual, reported - actual));
            
            try
            {
                vm.m_vehicleCount = actual;
                RebuildInfoCounts(vm);
                
                repairsPerformed++;
                lastRepairTime = DateTime.Now;
                
                UnityEngine.Debug.Log(string.Format(
                    "[VehiclePoolMonitor] Count repaired: {0} → {1} and synced m_infoCount", reported, actual));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[VehiclePoolMonitor] HandleCountMismatch repair error: " + ex);
            }
        }
        
        // ========================================================================
        // SPAWN THROTTLING DETECTION
        // ========================================================================
        
        /// <summary>
        /// Detect if spawn throttling is occurring
        /// </summary>
        private static bool DetectSpawnThrottling(VehicleManager vm, int currentCount)
        {
            // Check if count is static
            if (currentCount == lastActualCount)
            {
                consecutiveStaticChecks++;
            }
            else
            {
                consecutiveStaticChecks = 0;
                return false; // Count is changing, no throttling
            }
            
            // Check if count has been static for threshold checks (~2 minutes)
            if (consecutiveStaticChecks < staticThreshold)
            {
                return false;
            }
            
            // Only flag throttling if count is unusually low (< 50) while city should have traffic
            if (currentCount < 50)
            {
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Handle spawn throttling by safely rebuilding internal counters
        /// </summary>
        private static void HandleSpawnThrottling(VehicleManager vm)
        {
            throttlingDetections++;
            
            UnityEngine.Debug.LogWarning(string.Format(
                "[VehiclePoolMonitor] Spawn throttling check triggered: Count static at {0} for {1} checks. Rebuilding info quotas...",
                lastActualCount, consecutiveStaticChecks));
            
            try
            {
                RebuildInfoCounts(vm);
                consecutiveStaticChecks = 0;
                repairsPerformed++;
                lastRepairTime = DateTime.Now;
                
                UnityEngine.Debug.Log("[VehiclePoolMonitor] Spawn throttling recovery completed");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[VehiclePoolMonitor] HandleSpawnThrottling error: " + ex);
            }
        }
        
        // ========================================================================
        // INTERNAL COUNTER REPAIR
        // ========================================================================
        
        /// <summary>
        /// Rebuild m_infoCount array (tracks count per vehicle prefab) from actual live vehicles.
        /// NEVER zeroes out blindly, preventing underflow and spawn locks.
        /// </summary>
        public static void RebuildInfoCounts(VehicleManager vm)
        {
            try
            {
                if (vm == null) return;

                FieldInfo infoCountField = typeof(VehicleManager).GetField("m_infoCount",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (infoCountField != null)
                {
                    Array infoCount = infoCountField.GetValue(vm) as Array;
                    if (infoCount != null)
                    {
                        int[] counts = new int[infoCount.Length];
                        Vehicle[] buffer = vm.m_vehicles.m_buffer;
                        
                        if (buffer != null)
                        {
                            for (int i = 1; i < buffer.Length; i++)
                            {
                                if ((buffer[i].m_flags & Vehicle.Flags.Created) != 0)
                                {
                                    ushort infoIndex = buffer[i].m_infoIndex;
                                    if (infoIndex < counts.Length)
                                    {
                                        counts[infoIndex]++;
                                    }
                                }
                            }
                        }
                        
                        for (int i = 0; i < infoCount.Length; i++)
                        {
                            infoCount.SetValue(counts[i], i);
                        }
                        
                        UnityEngine.Debug.Log(string.Format(
                            "[VehiclePoolMonitor] Rebuilt m_infoCount array ({0} entries from live vehicles)", infoCount.Length));
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[VehiclePoolMonitor] RebuildInfoCounts error: " + ex);
            }
        }
        
        // ========================================================================
        // DIAGNOSTICS
        // ========================================================================
        
        /// <summary>
        /// Force immediate repair (for console command)
        /// </summary>
        public static void ForceRepair()
        {
            try
            {
                VehicleManager vm = Singleton<VehicleManager>.instance;
                if (vm == null)
                {
                    UnityEngine.Debug.LogWarning("[VehiclePoolMonitor] VehicleManager not loaded");
                    return;
                }
                
                UnityEngine.Debug.Log("[VehiclePoolMonitor] Manual repair triggered");
                
                int actual = CountActualVehicles(vm);
                vm.m_vehicleCount = actual;
                
                // Rebuild info counts safely from live vehicles
                RebuildInfoCounts(vm);
                
                UnityEngine.Debug.Log("[VehiclePoolMonitor] Manual repair completed");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[VehiclePoolMonitor] ForceRepair error: " + ex);
            }
        }
        
        private static string FormatTimeSince(DateTime time)
        {
            TimeSpan span = DateTime.Now - time;
            if (span.TotalSeconds < 60)
                return string.Format("{0:F0}s ago", span.TotalSeconds);
            if (span.TotalMinutes < 60)
                return string.Format("{0:F0}m ago", span.TotalMinutes);
            return string.Format("{0:F0}h ago", span.TotalHours);
        }
    }

    // ============================================================================
    // TM:PE BUFFER AUTO-SYNC - Keep Extension Buffers in Sync
    // ============================================================================
    /// <summary>
    /// Automatically synchronizes TM:PE's extension buffers with game's live buffers.
    /// Prevents desync when roads are built/demolished or after save load.
    /// </summary>
    public static class TMPEBufferSync
    {
        // ========================================================================
        // FIELDS
        // ========================================================================
        
        private static int syncCount = 0;
        private static DateTime lastSyncTime = DateTime.MinValue;
        private static string lastSyncReason = "";
        
        // ========================================================================
        // PUBLIC API
        // ========================================================================
        
        /// <summary>
        /// Initialize (placeholder for future use)
        /// </summary>
        public static void Initialize()
        {
            UnityEngine.Debug.Log("[TMPEBufferSync] Initialized");
        }

        private static uint lastNetworkSyncFrame = 0;

        /// <summary>
        /// Called when road network changes (e.g. road built or demolished)
        /// </summary>
        public static void OnNetworkModified()
        {
            try
            {
                SimulationManager sm = Singleton<SimulationManager>.instance;
                uint currentFrame = sm != null ? sm.m_currentFrameIndex : 0;
                // Debounce to at most once per 60 frames (~1s)
                if (currentFrame - lastNetworkSyncFrame < 60) return;
                lastNetworkSyncFrame = currentFrame;
                SyncAllBuffers("NetworkModified");
            }
            catch { }
        }
        
        /// <summary>
        /// Sync all TM:PE extension buffers with game's live buffers.
        /// This is the CORE fix for buffer desync causing routing errors.
        /// </summary>
        public static void SyncAllBuffers(string reason)
        {
            try
            {
                lastSyncReason = reason;
                lastSyncTime = DateTime.Now;
                syncCount++;
                
                Assembly tmpe = TMPECompatibility.GetTMPEAssembly();
                if (tmpe == null)
                {
                    // TM:PE not loaded, nothing to sync
                    return;
                }
                
                NetManager nm = Singleton<NetManager>.instance;
                if (nm == null) return;
                
                // Sync 1: Lane buffer
                if (nm.m_lanes != null && nm.m_lanes.m_buffer != null)
                {
                    SyncBuffer(tmpe, "TrafficManager.Util.Extensions.NetLaneExtensions",
                        "_laneBuffer", nm.m_lanes.m_buffer);
                }
                
                // Sync 2: Segment buffer
                if (nm.m_segments != null && nm.m_segments.m_buffer != null)
                {
                    SyncBuffer(tmpe, "TrafficManager.Util.Extensions.NetSegmentExtensions",
                        "_segBuffer", nm.m_segments.m_buffer);
                }
                
                // Sync 3: Node buffer
                if (nm.m_nodes != null && nm.m_nodes.m_buffer != null)
                {
                    SyncBuffer(tmpe, "TrafficManager.Util.Extensions.NetNodeExtensions",
                        "_nodeBuffer", nm.m_nodes.m_buffer);
                }
                
                UnityEngine.Debug.Log(string.Format(
                    "[TMPEBufferSync] Buffers synced (reason: {0}). Total syncs: {1}",
                    reason, syncCount));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[TMPEBufferSync] SyncAllBuffers error: " + ex);
            }
        }
        
        /// <summary>
        /// Sync individual buffer by setting static field reference
        /// </summary>
        private static void SyncBuffer(Assembly tmpeAssembly, string typeName, string fieldName, object liveBuffer)
        {
            try
            {
                Type type = tmpeAssembly.GetType(typeName);
                if (type == null)
                {
                    return;
                }
                
                FieldInfo field = type.GetField(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                
                if (field == null)
                {
                    return;
                }
                
                // Get current buffer
                object currentBuffer = field.GetValue(null);
                
                // Check if already pointing to same buffer (optimization)
                if (currentBuffer == liveBuffer)
                {
                    // Already synced, no action needed
                    return;
                }
                
                // Set new buffer reference (NOT a copy, just a reference)
                field.SetValue(null, liveBuffer);
                
                Array arr = liveBuffer as Array;
                int length = arr != null ? arr.Length : -1;
                
                UnityEngine.Debug.Log(string.Format(
                    "[TMPEBufferSync] Synced {0}.{1} → live buffer ({2} elements)",
                    type.Name, fieldName, length));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(string.Format(
                    "[TMPEBufferSync] SyncBuffer error for {0}.{1}: {2}",
                    typeName, fieldName, ex));
            }
        }
        
        /// <summary>
        /// Get status for diagnostics
        /// </summary>
        public static string GetStatus()
        {
            string timeSince = lastSyncTime == DateTime.MinValue
                ? "Never"
                : FormatTimeSince(lastSyncTime);
            
            return string.Format(
                "Buffer Sync: Last: {0} ({1}) | Total: {2}",
                timeSince, lastSyncReason, syncCount);
        }
        
        private static string FormatTimeSince(DateTime time)
        {
            TimeSpan span = DateTime.Now - time;
            if (span.TotalSeconds < 60)
                return string.Format("{0:F0}s ago", span.TotalSeconds);
            return string.Format("{0:F0}m ago", span.TotalMinutes);
        }
    }

    // ============================================================================
    // PATHFIND VALIDATOR - Clean Stale Path Cache
    // ============================================================================
    /// <summary>
    /// Validates PathManager path cache and removes stale paths.
    /// Stale paths reference lanes/segments that no longer exist,
    /// causing vehicles to follow invalid routes and get stuck.
    /// </summary>
    public static class PathFindValidator
    {
        // ========================================================================
        // FIELDS
        // ========================================================================
        
        private static DateTime lastValidationTime = DateTime.MinValue;
        private static int pathsCleaned = 0;
        private static int totalValidations = 0;
        private static bool validationInProgress = false;
        private static readonly object validationLock = new object();
        
        // ========================================================================
        // PUBLIC API
        // ========================================================================
        
        /// <summary>
        /// Initialize (placeholder for future use)
        /// </summary>
        public static void Initialize()
        {
            UnityEngine.Debug.Log("[PathFindValidator] Initialized");
        }
        
        /// <summary>
        /// Validate and clean all paths in PathManager.
        /// WARNING: This can take 100-200ms, don't call every frame!
        /// </summary>
        public static void ValidateAndCleanPaths()
        {
            // Prevent concurrent validations
            if (validationInProgress)
            {
                UnityEngine.Debug.LogWarning("[PathFindValidator] Validation already in progress");
                return;
            }
            
            lock (validationLock)
            {
                validationInProgress = true;
            }
            
            try
            {
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                
                PathManager pm = Singleton<PathManager>.instance;
                NetManager nm = Singleton<NetManager>.instance;
                
                if (pm == null || nm == null)
                {
                    UnityEngine.Debug.LogWarning("[PathFindValidator] Managers not loaded");
                    return;
                }
                
                totalValidations++;
                lastValidationTime = DateTime.Now;
                
                int cleaned = 0;
                int scanned = 0;
                
                // Scan all path units (slot 0 is unused in Cities: Skylines)
                for (uint i = 1; i < pm.m_pathUnits.m_buffer.Length; i++)
                {
                    byte flags = pm.m_pathUnits.m_buffer[i].m_pathFindFlags;
                    if (flags != 0)
                    {
                        // Skip paths still calculating (FLAG_WAITING); only validate completed/ready paths
                        if ((flags & PathUnit.FLAG_READY) == 0)
                        {
                            continue;
                        }

                        scanned++;
                        
                        if (!IsPathValid(pm, nm, i))
                        {
                            // Invalid path referencing demolished road, release it
                            pm.ReleasePath(i);
                            cleaned++;
                        }
                    }
                }
                
                sw.Stop();
                pathsCleaned += cleaned;
                
                UnityEngine.Debug.Log(string.Format(
                    "[PathFindValidator] Validation complete: Scanned {0} ready paths, cleaned {1} stale paths in {2}ms",
                    scanned, cleaned, sw.ElapsedMilliseconds));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[PathFindValidator] ValidateAndCleanPaths error: " + ex);
            }
            finally
            {
                lock (validationLock)
                {
                    validationInProgress = false;
                }
            }
        }
        
        /// <summary>
        /// Get status for diagnostics
        /// </summary>
        public static string GetStatus()
        {
            string timeSince = lastValidationTime == DateTime.MinValue
                ? "Never"
                : FormatTimeSince(lastValidationTime);
            
            return string.Format(
                "PathFind Validator: {0} validations, {1} paths cleaned | Last: {2}",
                totalValidations, pathsCleaned, timeSince);
        }
        
        // ========================================================================
        // VALIDATION LOGIC
        // ========================================================================
        
        /// <summary>
        /// Check if a path is valid (all referenced segments and lanes exist and are created)
        /// </summary>
        private static bool IsPathValid(PathManager pm, NetManager nm, uint pathId)
        {
            try
            {
                PathUnit pathUnit = pm.m_pathUnits.m_buffer[pathId];
                if (pathUnit.m_positionCount == 0 || pathUnit.m_positionCount > 12)
                {
                    return false;
                }
                
                // Check all positions in path
                for (int i = 0; i < pathUnit.m_positionCount; i++)
                {
                    PathUnit.Position pos;
                    if (pathUnit.GetPosition(i, out pos))
                    {
                        ushort segId = pos.m_segment;
                        if (segId == 0 || segId >= nm.m_segments.m_buffer.Length)
                        {
                            return false;
                        }
                        
                        if ((nm.m_segments.m_buffer[segId].m_flags & NetSegment.Flags.Created) == 0)
                        {
                            return false;
                        }
                        
                        NetInfo info = nm.m_segments.m_buffer[segId].Info;
                        if (info == null || info.m_lanes == null || pos.m_lane >= info.m_lanes.Length)
                        {
                            return false;
                        }
                    }
                }
                
                return true;
            }
            catch
            {
                return true; // Keep path on unexpected exception
            }
        }
        
        /// <summary>
        /// Check if a segment ID is valid
        /// </summary>
        private static bool IsSegmentValid(NetManager nm, ushort segmentId)
        {
            try
            {
                // Check bounds
                if (segmentId >= nm.m_segments.m_buffer.Length)
                {
                    return false;
                }
                
                // Check Created flag
                if ((nm.m_segments.m_buffer[segmentId].m_flags & NetSegment.Flags.Created) == 0)
                {
                    return false;
                }
                
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        private static string FormatTimeSince(DateTime time)
        {
            TimeSpan span = DateTime.Now - time;
            if (span.TotalSeconds < 60)
                return string.Format("{0:F0}s ago", span.TotalSeconds);
            return string.Format("{0:F0}m ago", span.TotalMinutes);
        }
    }

    // ============================================================================
    // STUCK VEHICLE RECOVERY - Emergency Unstuck System
    // ============================================================================
    /// <summary>
    /// Detects vehicles that haven't moved for extended periods and applies
    /// escalating recovery methods: path reset → teleport → despawn/respawn.
    /// Runs every 64 simulation frames.
    /// </summary>
    public static class StuckVehicleRecovery
    {
        // ========================================================================
        // FIELDS & STRUCTS
        // ========================================================================
        
        private class VehicleStuckInfo
        {
            public Vector3 LastPosition;
            public int StuckFrames;
            public uint PathUnit;
            public int RecoveryLevel; // 0=none, 1=path reset, 2=teleport, 3=despawn
        }
        
        private static Dictionary<ushort, VehicleStuckInfo> stuckVehicles = 
            new Dictionary<ushort, VehicleStuckInfo>();
        
        // Configuration
        private static readonly int scanIntervalFrames = 256; // ~4.2 seconds
        private static readonly float movementThreshold = 0.1f; // meters
        private static readonly float velocityThreshold = 0.1f; // m/s
        
        // Recovery thresholds (in frames @ 60fps)
        private static readonly int level1Threshold = 600;  // 10 seconds
        private static readonly int level2Threshold = 900;  // 15 seconds
        private static readonly int level3Threshold = 1200; // 20 seconds
        
        // State
        private static uint lastScanFrame = 0;
        private static int totalScans = 0;
        private static int level1Recoveries = 0;
        private static int level2Recoveries = 0;
        private static int level3Recoveries = 0;
        
        // ========================================================================
        // PUBLIC API
        // ========================================================================
        
        /// <summary>
        /// Initialize (placeholder for future use)
        /// </summary>
        public static void Initialize()
        {
            UnityEngine.Debug.Log("[StuckVehicleRecovery] Initialized");
        }
        
        /// <summary>
        /// Scan all vehicles and recover stuck ones. Called from SimulationStep.
        /// </summary>
        public static void ScanAndRecover()
        {
            try
            {
                SimulationManager sm = Singleton<SimulationManager>.instance;
                if (sm == null) return;
                
                uint currentFrame = sm.m_currentFrameIndex;
                
                // Only scan every N frames
                if (currentFrame - lastScanFrame < scanIntervalFrames)
                {
                    return;
                }
                
                lastScanFrame = currentFrame;
                totalScans++;
                
                VehicleManager vm = Singleton<VehicleManager>.instance;
                if (vm == null) return;
                
                int recoveredThisScan = 0;
                List<ushort> toRemove = new List<ushort>();
                
                // Scan all vehicles in buffer
                for (ushort i = 1; i < vm.m_vehicles.m_buffer.Length; i++)
                {
                    if ((vm.m_vehicles.m_buffer[i].m_flags & Vehicle.Flags.Created) == 0)
                    {
                        // Vehicle doesn't exist, remove from tracking
                        if (stuckVehicles.ContainsKey(i))
                        {
                            toRemove.Add(i);
                        }
                        continue;
                    }
                    
                    Vehicle vehicle = vm.m_vehicles.m_buffer[i];
                    
                    // Skip vehicles that are not spawned, or are currently parking / loading
                    if ((vehicle.m_flags & Vehicle.Flags.Spawned) == 0 ||
                        (vehicle.m_flags & (Vehicle.Flags.Parking | Vehicle.Flags.WaitingLoading)) != 0)
                    {
                        if (stuckVehicles.ContainsKey(i)) toRemove.Add(i);
                        continue;
                    }
                    
                    // Fast bypass: if vehicle is unblocked and not waiting path, it's driving normally!
                    if (vehicle.m_blockCounter < 50 && (vehicle.m_flags & Vehicle.Flags.WaitingPath) == 0)
                    {
                        if (stuckVehicles.ContainsKey(i)) toRemove.Add(i);
                        continue;
                    }
                    
                    // Check if road segment under the vehicle or in its path is demolished
                    if (IsVehicleOnDestroyedSegment(vm, i, vehicle))
                    {
                        // Clean despawn immediately when road is demolished!
                        vm.ReleaseVehicle(i);
                        toRemove.Add(i);
                        recoveredThisScan++;
                        continue;
                    }
                    
                    // Check if vehicle is stuck
                    if (CheckVehicleMovement(i, vehicle))
                    {
                        // Vehicle moved, no action needed
                        continue;
                    }
                    
                    // Vehicle is stuck, apply recovery if needed
                    if (ApplyRecoveryIfNeeded(vm, i))
                    {
                        recoveredThisScan++;
                    }
                }
                
                // Clean up tracking for deleted vehicles
                foreach (ushort id in toRemove)
                {
                    stuckVehicles.Remove(id);
                }
                
                if (recoveredThisScan > 0)
                {
                    UnityEngine.Debug.Log(string.Format(
                        "[StuckVehicleRecovery] Recovered {0} vehicles. Tracking: {1} potentially stuck",
                        recoveredThisScan, stuckVehicles.Count));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[StuckVehicleRecovery] ScanAndRecover error: " + ex);
            }
        }
        
        /// <summary>
        /// Clear all tracking (on level unload)
        /// </summary>
        public static void ClearCache()
        {
            stuckVehicles.Clear();
            UnityEngine.Debug.Log("[StuckVehicleRecovery] Cache cleared");
        }
        
        /// <summary>
        /// Get status for diagnostics
        /// </summary>
        public static string GetStatus()
        {
            return string.Format(
                "Stuck Recovery: Tracking {0} | Scans: {1} | Recoveries: L1={2} L2={3} L3={4}",
                stuckVehicles.Count, totalScans, level1Recoveries, level2Recoveries, level3Recoveries);
        }
        
        /// <summary>
        /// Force recovery of all currently stuck vehicles (console command)
        /// </summary>
        public static void ForceRecoverAll()
        {
            try
            {
                VehicleManager vm = Singleton<VehicleManager>.instance;
                if (vm == null) return;
                
                int recovered = 0;
                List<ushort> vehicleIds = new List<ushort>(stuckVehicles.Keys);
                
                foreach (ushort vehicleId in vehicleIds)
                {
                    // Force Level 1 recovery for all
                    if (ApplyLevel1Recovery(vm, vehicleId))
                    {
                        recovered++;
                        stuckVehicles.Remove(vehicleId);
                    }
                }
                
                UnityEngine.Debug.Log(string.Format(
                    "[StuckVehicleRecovery] Force recovery: {0} vehicles recovered", recovered));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[StuckVehicleRecovery] ForceRecoverAll error: " + ex);
            }
        }
        
        // ========================================================================
        // MOVEMENT CHECKING & DEMOLISHED ROAD DETECTION
        // ========================================================================
        
        /// <summary>
        /// Check if a vehicle is situated on or routed through a demolished road segment
        /// </summary>
        public static bool IsVehicleOnDestroyedSegment(VehicleManager vm, ushort vehicleId, Vehicle vehicle)
        {
            try
            {
                NetManager nm = Singleton<NetManager>.instance;
                PathManager pm = Singleton<PathManager>.instance;
                if (nm == null || nm.m_segments == null || nm.m_segments.m_buffer == null) return false;

                if (vehicle.m_path != 0 && pm != null && pm.m_pathUnits != null && pm.m_pathUnits.m_buffer != null &&
                    vehicle.m_path < pm.m_pathUnits.m_buffer.Length)
                {
                    int posIdx = (int)(vehicle.m_pathPositionIndex >> 1);
                    PathUnit.Position pPos;
                    if (pm.m_pathUnits.m_buffer[vehicle.m_path].GetPosition(posIdx, out pPos))
                    {
                        ushort segId = pPos.m_segment;
                        if (segId == 0 || segId >= nm.m_segments.m_buffer.Length ||
                            (nm.m_segments.m_buffer[segId].m_flags & NetSegment.Flags.Created) == 0)
                        {
                            return true; // Road in route demolished!
                        }
                    }
                }

                Vector3 pos = vehicle.GetLastFramePosition();
                if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z) || pos.y < -50f || pos.y > 2000f)
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if vehicle has moved since last check.
        /// Returns true if moved, false if stuck.
        /// </summary>
        private static bool CheckVehicleMovement(ushort vehicleId, Vehicle vehicle)
        {
            try
            {
                Vector3 currentPos = vehicle.GetLastFramePosition();
                Vector3 velocity = vehicle.GetLastFrameVelocity();
                
                if (!stuckVehicles.ContainsKey(vehicleId))
                {
                    // First time seeing this vehicle, start tracking
                    stuckVehicles[vehicleId] = new VehicleStuckInfo
                    {
                        LastPosition = currentPos,
                        StuckFrames = 0,
                        PathUnit = vehicle.m_path,
                        RecoveryLevel = 0
                    };
                    return true; // Not stuck yet
                }
                
                VehicleStuckInfo info = stuckVehicles[vehicleId];
                
                // Check if moved
                float distMoved = Vector3.Distance(currentPos, info.LastPosition);
                float speed = velocity.magnitude;
                
                if (distMoved > movementThreshold || speed > velocityThreshold)
                {
                    // Vehicle moved! Reset tracking
                    info.LastPosition = currentPos;
                    info.StuckFrames = 0;
                    info.RecoveryLevel = 0;
                    return true; // Not stuck
                }
                
                // Vehicle didn't move, increment stuck counter
                info.StuckFrames += scanIntervalFrames;
                info.LastPosition = currentPos;
                
                return false; // Still stuck
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(string.Format(
                    "[StuckVehicleRecovery] CheckVehicleMovement error for vehicle {0}: {1}",
                    vehicleId, ex));
                return true; // Assume not stuck on error
            }
        }
        
        // ========================================================================
        // RECOVERY APPLICATION
        // ========================================================================
        
        /// <summary>
        /// Apply recovery if vehicle has been stuck long enough.
        /// Returns true if recovery was applied.
        /// </summary>
        private static bool ApplyRecoveryIfNeeded(VehicleManager vm, ushort vehicleId)
        {
            if (!stuckVehicles.ContainsKey(vehicleId))
            {
                return false;
            }
            
            VehicleStuckInfo info = stuckVehicles[vehicleId];
            
            // Determine recovery level based on stuck duration
            if (info.StuckFrames >= level3Threshold && info.RecoveryLevel < 3)
            {
                // Level 3: Despawn/respawn (last resort)
                return ApplyLevel3Recovery(vm, vehicleId, info);
            }
            else if (info.StuckFrames >= level2Threshold && info.RecoveryLevel < 2)
            {
                // Level 2: Teleport
                return ApplyLevel2Recovery(vm, vehicleId, info);
            }
            else if (info.StuckFrames >= level1Threshold && info.RecoveryLevel < 1)
            {
                // Level 1: Path reset
                return ApplyLevel1Recovery(vm, vehicleId, info);
            }
            
            return false; // Not ready for recovery yet
        }
        
        /// <summary>
        /// Level 1 Recovery: Reset path, vehicle will request new pathfind
        /// </summary>
        private static bool ApplyLevel1Recovery(VehicleManager vm, ushort vehicleId)
        {
            VehicleStuckInfo info = stuckVehicles.ContainsKey(vehicleId) ? stuckVehicles[vehicleId] : null;
            return ApplyLevel1Recovery(vm, vehicleId, info);
        }
        
        private static bool ApplyLevel1Recovery(VehicleManager vm, ushort vehicleId, VehicleStuckInfo info)
        {
            try
            {
                Vehicle vehicle = vm.m_vehicles.m_buffer[vehicleId];
                
                UnityEngine.Debug.LogWarning(string.Format(
                    "[StuckVehicleRecovery] Vehicle {0} stuck for {1:F0}s. Applying Level 1 Recovery (canonical InvalidPath reroute)",
                    vehicleId, (info != null ? info.StuckFrames : 600) / 60.0f));
                
                VehicleInfo vInfo = vehicle.Info;
                if (vInfo != null && vInfo.m_vehicleAI != null)
                {
                    vInfo.m_vehicleAI.InvalidatePath(vehicleId, ref vm.m_vehicles.m_buffer[vehicleId], vehicleId, ref vm.m_vehicles.m_buffer[vehicleId]);
                }
                else
                {
                    vm.ReleaseVehicle(vehicleId);
                }
                
                if (info != null)
                {
                    info.RecoveryLevel = 1;
                    info.StuckFrames = 0; // Reset counter to give it time
                }
                
                level1Recoveries++;
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(string.Format(
                    "[StuckVehicleRecovery] Level 1 recovery error for vehicle {0}: {1}",
                    vehicleId, ex));
                return false;
            }
        }
        
        /// <summary>
        /// Level 2 Recovery: Path re-evaluation or clean despawn if permanently blocked
        /// </summary>
        private static bool ApplyLevel2Recovery(VehicleManager vm, ushort vehicleId, VehicleStuckInfo info)
        {
            try
            {
                Vehicle vehicle = vm.m_vehicles.m_buffer[vehicleId];
                
                UnityEngine.Debug.LogWarning(string.Format(
                    "[StuckVehicleRecovery] Vehicle {0} stuck for {1:F0}s. Applying Level 2 Recovery (path re-evaluation & unblock)",
                    vehicleId, (info != null ? info.StuckFrames : 900) / 60.0f));
                
                VehicleInfo vInfo = vehicle.Info;
                if (vInfo != null && vInfo.m_vehicleAI != null)
                {
                    vInfo.m_vehicleAI.InvalidatePath(vehicleId, ref vm.m_vehicles.m_buffer[vehicleId], vehicleId, ref vm.m_vehicles.m_buffer[vehicleId]);
                }
                
                // If still stuck with high block counter (> 150), despawn to prevent traffic gridlock
                if (vm.m_vehicles.m_buffer[vehicleId].m_blockCounter > 150)
                {
                    vm.ReleaseVehicle(vehicleId);
                    stuckVehicles.Remove(vehicleId);
                }
                else if (info != null)
                {
                    info.RecoveryLevel = 2;
                    info.StuckFrames = 0;
                }
                
                level2Recoveries++;
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(string.Format(
                    "[StuckVehicleRecovery] Level 2 recovery error for vehicle {0}: {1}",
                    vehicleId, ex));
                return false;
            }
        }
        
        /// <summary>
        /// Level 3 Recovery: Despawn stuck vehicle cleanly via VehicleManager
        /// </summary>
        private static bool ApplyLevel3Recovery(VehicleManager vm, ushort vehicleId, VehicleStuckInfo info)
        {
            try
            {
                UnityEngine.Debug.LogWarning(string.Format(
                    "[StuckVehicleRecovery] Vehicle {0} stuck for {1:F0}s. Applying Level 3 Recovery (clean despawn)",
                    vehicleId, (info != null ? info.StuckFrames : 1200) / 60.0f));
                
                // Release vehicle cleanly via VehicleManager
                vm.ReleaseVehicle(vehicleId);
                
                // Remove from tracking
                stuckVehicles.Remove(vehicleId);
                
                level3Recoveries++;
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(string.Format(
                    "[StuckVehicleRecovery] Level 3 recovery error for vehicle {0}: {1}",
                    vehicleId, ex));
                return false;
            }
        }
        
        // ========================================================================
        // DIAGNOSTICS
        // ========================================================================
        
        /// <summary>
        /// Reset all statistics
        /// </summary>
        public static void ResetStats()
        {
            totalScans = 0;
            level1Recoveries = 0;
            level2Recoveries = 0;
            level3Recoveries = 0;
        }
    }

    // ============================================================================
    // DIAGNOSTICS UI - Console Commands & Status Panel
    // ============================================================================
    /// <summary>
    /// Provides in-game diagnostics via console commands and hotkey commands.
    /// Simplified implementation without UI panel (can be added later).
    /// </summary>
    public static class DiagnosticsUI
    {
        // ========================================================================
        // FIELDS
        // ========================================================================
        
        private static bool verboseLogging = false;
        
        // ========================================================================
        // PUBLIC API
        // ========================================================================
        
        /// <summary>
        /// Initialize diagnostics system
        /// </summary>
        public static void Initialize()
        {
            try
            {
                UnityEngine.Debug.Log("[DiagnosticsUI] Initialized. Use debug console commands: mnc.status, mnc.repair, mnc.help");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[DiagnosticsUI] Initialize error: " + ex);
            }
        }
        
        /// <summary>
        /// Execute status command
        /// </summary>
        public static void ExecuteStatusCommand()
        {
            UnityEngine.Debug.Log("========================================");
            UnityEngine.Debug.Log("MORE NETWORK CAPACITY RENEW - STATUS");
            UnityEngine.Debug.Log("========================================");
            
            // ArrayGuardian status
            UnityEngine.Debug.Log("\n[ARRAY MONITORING]");
            UnityEngine.Debug.Log(ArrayGuardian.GetValidationStatus());
            UnityEngine.Debug.Log(ArrayGuardianV2.GetStatus());
            
            // Vehicle status
            UnityEngine.Debug.Log("\n[VEHICLE POOL]");
            UnityEngine.Debug.Log(VehiclePoolMonitor.GetStatus());
            
            // Buffer sync status
            UnityEngine.Debug.Log("\n[BUFFER SYNC]");
            UnityEngine.Debug.Log(TMPEBufferSync.GetStatus());
            
            // PathFind status
            UnityEngine.Debug.Log("\n[PATHFIND CACHE]");
            UnityEngine.Debug.Log(PathFindValidator.GetStatus());
            
            // Stuck vehicles status
            UnityEngine.Debug.Log("\n[STUCK RECOVERY]");
            UnityEngine.Debug.Log(StuckVehicleRecovery.GetStatus());
            
            UnityEngine.Debug.Log("========================================");
        }
        
        /// <summary>
        /// Execute repair command
        /// </summary>
        public static void ExecuteRepairCommand()
        {
            UnityEngine.Debug.Log("[DiagnosticsUI] Executing full repair...");
            
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            
            try
            {
                // Repair 1: Arrays
                bool arraysOK = ArrayGuardian.RepairAllArrays();
                UnityEngine.Debug.Log(string.Format("  Arrays repair: {0}", arraysOK ? "OK" : "FAILED"));
                
                // Repair 2: Buffers
                TMPEBufferSync.SyncAllBuffers("ManualRepair");
                UnityEngine.Debug.Log("  Buffer sync: OK");
                
                // Repair 3: Vehicle counts
                VehiclePoolMonitor.ForceRepair();
                UnityEngine.Debug.Log("  Vehicle counts: OK");
                
                // Repair 4: Path cache
                PathFindValidator.ValidateAndCleanPaths();
                UnityEngine.Debug.Log("  Path cache cleaned: OK");
                
                sw.Stop();
                UnityEngine.Debug.Log(string.Format("[DiagnosticsUI] Full repair completed in {0}ms", sw.ElapsedMilliseconds));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[DiagnosticsUI] Repair command error: " + ex);
            }
        }
        
        /// <summary>
        /// Cleanup on level unload
        /// </summary>
        public static void Cleanup()
        {
            UnityEngine.Debug.Log("[DiagnosticsUI] Cleanup completed");
        }
        
        public static bool VerboseLogging
        {
            get { return verboseLogging; }
            set { verboseLogging = value; }
        }
    }

    // ============================================================================
    // VEHICLE FIX PATCHES - Harmony Integration Points
    // ============================================================================
    /// <summary>
    /// Harmony patches to integrate monitoring systems into game lifecycle.
    /// All patches are Postfix to avoid conflicts with other mods.
    /// </summary>
    public static class VehicleFixPatches
    {
        private static uint lastSimulationFrame = 0;
        
        // ========================================================================
        // PATCH: SimulationManager.SimulationStep (Postfix)
        // ========================================================================
        /// <summary>
        /// Called every simulation frame. Used to trigger periodic checks.
        /// </summary>
        public static void SimulationStep_Postfix()
        {
            try
            {
                SimulationManager sm = Singleton<SimulationManager>.instance;
                if (sm == null) return;
                
                uint currentFrame = sm.m_currentFrameIndex;
                
                // Prevent double-execution if called multiple times per frame
                if (currentFrame == lastSimulationFrame)
                {
                    return;
                }
                lastSimulationFrame = currentFrame;
                
                // Every 1800 frames (~30 seconds): Check vehicle pool
                if (currentFrame % 1800 == 0)
                {
                    VehiclePoolMonitor.CheckAndRepair();
                }
                
                // Every 256 frames (~4 seconds): Scan stuck vehicles
                if (currentFrame % 256 == 0)
                {
                    StuckVehicleRecovery.ScanAndRecover();
                }
            }
            catch (Exception ex)
            {
                if (DiagnosticsUI.VerboseLogging)
                {
                    UnityEngine.Debug.LogError("[VehicleFixPatches] SimulationStep error: " + ex);
                }
            }
        }
        
        // ========================================================================
        // PATCH: NetManager.UpdateSegmentRenderer (Postfix)
        // ========================================================================
        /// <summary>
        /// Called when road network changes (roads placed, updated, demolished).
        /// Automatically keeps TM:PE extension buffers synced with live game buffers.
        /// </summary>
        public static void NetManager_UpdateSegmentRenderer_Postfix(ushort segment, bool updateGroup)
        {
            try
            {
                TMPEBufferSync.OnNetworkModified();
            }
            catch { }
        }

        // ========================================================================
        // PATCH: NetManager.ReleaseSegmentImplementation (Postfix)
        // ========================================================================
        /// <summary>
        /// When a road segment is demolished, cleanly invalidate paths or release
        /// vehicles traversing it so they reroute or despawn immediately.
        /// </summary>
        public static void NetManager_ReleaseSegmentImplementation_Postfix(ushort segment, ref NetSegment data, bool keepNodes)
        {
            if (segment == 0) return;
            try
            {
                VehicleManager vm = Singleton<VehicleManager>.instance;
                PathManager pm = Singleton<PathManager>.instance;
                if (vm == null || vm.m_vehicles == null || vm.m_vehicles.m_buffer == null) return;

                Vehicle[] buffer = vm.m_vehicles.m_buffer;
                int len = buffer.Length;
                for (ushort i = 1; i < len; i++)
                {
                    if ((buffer[i].m_flags & (Vehicle.Flags.Created | Vehicle.Flags.Spawned)) != (Vehicle.Flags.Created | Vehicle.Flags.Spawned))
                        continue;

                    if (buffer[i].m_leadingVehicle != 0)
                        continue;

                    uint pathId = buffer[i].m_path;
                    bool onSegment = false;
                    if (pathId != 0 && pm != null && pm.m_pathUnits != null && pm.m_pathUnits.m_buffer != null && pathId < pm.m_pathUnits.m_buffer.Length)
                    {
                        byte pathIdx = buffer[i].m_pathPositionIndex;
                        int posIdx = (int)(pathIdx >> 1);
                        PathUnit.Position pPos;
                        if (pm.m_pathUnits.m_buffer[pathId].GetPosition(posIdx, out pPos))
                        {
                            if (pPos.m_segment == segment)
                            {
                                onSegment = true;
                            }
                        }
                    }

                    if (onSegment)
                    {
                        VehicleInfo info = buffer[i].Info;
                        if (info != null && info.m_vehicleAI != null)
                        {
                            info.m_vehicleAI.InvalidatePath(i, ref buffer[i], i, ref buffer[i]);
                        }
                        else
                        {
                            vm.ReleaseVehicle(i);
                        }

                        if ((buffer[i].m_flags & Vehicle.Flags.Spawned) != 0 && buffer[i].m_path == 0)
                        {
                            vm.ReleaseVehicle(i);
                        }
                    }
                }
            }
            catch { }
        }

        // ========================================================================
        // PATCH: NetManager.ReleaseNodeImplementation (Postfix)
        // ========================================================================
        /// <summary>
        /// When a node is demolished, ensure vehicles adjacent to the node reroute or despawn cleanly.
        /// </summary>
        public static void NetManager_ReleaseNodeImplementation_Postfix(ushort node, ref NetNode data)
        {
            if (node == 0) return;
            try
            {
                VehicleManager vm = Singleton<VehicleManager>.instance;
                if (vm == null || vm.m_vehicles == null || vm.m_vehicles.m_buffer == null) return;

                Vehicle[] buffer = vm.m_vehicles.m_buffer;
                int len = buffer.Length;
                Vector3 nodePos = data.m_position;

                for (ushort i = 1; i < len; i++)
                {
                    if ((buffer[i].m_flags & (Vehicle.Flags.Created | Vehicle.Flags.Spawned)) != (Vehicle.Flags.Created | Vehicle.Flags.Spawned))
                        continue;

                    if (buffer[i].m_leadingVehicle != 0)
                        continue;

                    Vector3 pos = buffer[i].GetLastFramePosition();
                    if (Vector3.SqrMagnitude(pos - nodePos) < 900f) // within 30m radius
                    {
                        VehicleInfo info = buffer[i].Info;
                        if (info != null && info.m_vehicleAI != null)
                        {
                            info.m_vehicleAI.InvalidatePath(i, ref buffer[i], i, ref buffer[i]);
                        }
                        else
                        {
                            vm.ReleaseVehicle(i);
                        }

                        if ((buffer[i].m_flags & Vehicle.Flags.Spawned) != 0 && buffer[i].m_path == 0)
                        {
                            vm.ReleaseVehicle(i);
                        }
                    }
                }
            }
            catch { }
        }
        
        // ========================================================================
        // UTILITY: Install All Patches
        // ========================================================================
        /// <summary>
        /// Install all Harmony patches. Call from LoadingExtension.OnLevelLoaded()
        /// </summary>
        public static void InstallAllPatches(HarmonyInstance harmony)
        {
            try
            {
                // Patch 1: SimulationManager.SimulationStep
                MethodInfo simStep = typeof(SimulationManager).GetMethod("SimulationStep",
                    BindingFlags.Public | BindingFlags.Instance);
                
                if (simStep != null)
                {
                    MethodInfo postfix = typeof(VehicleFixPatches).GetMethod(
                        nameof(SimulationStep_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    
                    harmony.Patch(simStep, postfix: new HarmonyMethod(postfix));
                    UnityEngine.Debug.Log("[VehicleFixPatches] Patched SimulationManager.SimulationStep");
                }

                // Patch 2: NetManager.UpdateSegmentRenderer
                MethodInfo usr = typeof(NetManager).GetMethod("UpdateSegmentRenderer",
                    BindingFlags.Public | BindingFlags.Instance);
                if (usr != null)
                {
                    MethodInfo usrPostfix = typeof(VehicleFixPatches).GetMethod(
                        nameof(NetManager_UpdateSegmentRenderer_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    
                    harmony.Patch(usr, postfix: new HarmonyMethod(usrPostfix));
                    UnityEngine.Debug.Log("[VehicleFixPatches] Patched NetManager.UpdateSegmentRenderer");
                }

                // Patch 3: NetManager.ReleaseSegmentImplementation
                MethodInfo relSeg = typeof(NetManager).GetMethod("ReleaseSegmentImplementation",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(ushort), typeof(NetSegment).MakeByRefType(), typeof(bool) },
                    null);
                if (relSeg != null)
                {
                    MethodInfo relSegPostfix = typeof(VehicleFixPatches).GetMethod(
                        nameof(NetManager_ReleaseSegmentImplementation_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(relSeg, postfix: new HarmonyMethod(relSegPostfix));
                    UnityEngine.Debug.Log("[VehicleFixPatches] Patched NetManager.ReleaseSegmentImplementation");
                }

                // Patch 4: NetManager.ReleaseNodeImplementation
                MethodInfo relNode = typeof(NetManager).GetMethod("ReleaseNodeImplementation",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(ushort), typeof(NetNode).MakeByRefType() },
                    null);
                if (relNode != null)
                {
                    MethodInfo relNodePostfix = typeof(VehicleFixPatches).GetMethod(
                        nameof(NetManager_ReleaseNodeImplementation_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(relNode, postfix: new HarmonyMethod(relNodePostfix));
                    UnityEngine.Debug.Log("[VehicleFixPatches] Patched NetManager.ReleaseNodeImplementation");
                }
                
                UnityEngine.Debug.Log("[VehicleFixPatches] All patches installed");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[VehicleFixPatches] InstallAllPatches error: " + ex);
            }
        }
    }
}


