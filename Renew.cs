using ICities;
using ColossalFramework;
using System.Reflection;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using Harmony;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using ColossalFramework.Plugins;
using UnityEngine;
using System.Runtime.Serialization.Formatters.Binary;

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

        public static void Reset()
        {
            lastCheckpoint = DateTime.Now;
        }
    }

    // ============================================================================
    // UTF-16 SANITIZER & SURROGATE GUARDIAN
    // ============================================================================
    public static class Utf16Sanitizer
    {
        public static string Sanitize(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            char[] chars = null;
            int len = str.Length;
            for (int i = 0; i < len; i++)
            {
                char c = str[i];
                if (c >= 0xD800 && c <= 0xDBFF) // High surrogate
                {
                    if (i + 1 < len && str[i + 1] >= 0xDC00 && str[i + 1] <= 0xDFFF)
                    {
                        // Valid surrogate pair: skip low surrogate
                        i++;
                        continue;
                    }
                    // Unpaired high surrogate
                    if (chars == null) chars = str.ToCharArray();
                    chars[i] = '\uFFFD';
                }
                else if (c >= 0xDC00 && c <= 0xDFFF) // Low surrogate without preceding high surrogate
                {
                    // Unpaired low surrogate (missing surrogate head)
                    if (chars == null) chars = str.ToCharArray();
                    chars[i] = '\uFFFD';
                }
            }
            return chars != null ? new string(chars) : str;
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
    // CANONICAL MNC CORE CAPACITY CONSTANTS
    // ============================================================================
    public static class MNCConstants
    {
        public const int CORE_NET_CAPACITY = 65024; // 0xFE00 (MNC Core network capacity: Nodes and Segments)
        public const int CORE_SPLIT_CAPACITY = 130048; // 65024 * 2 (MNC Core split array capacity: SegmentEnds)
        public const int CORE_BUCKET_CAPACITY = 1016; // 65024 / 64 (MNC Core bucket array capacity: 64-bit masks)
        public const int LANE_CAPACITY = 524288; // 262144 * 2 (Expanded lanes capacity)
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
                RegisterArray("TrafficManager.State.Flags", "laneAllowedVehicleTypesArray", MNCConstants.CORE_NET_CAPACITY);
                
                // Register TM:PE live extension buffers (must match live NetManager arrays)
                RegisterArray("TrafficManager.Util.Extensions.NetLaneExtensions", "_laneBuffer", 262144);
                RegisterArray("TrafficManager.Util.Extensions.NetSegmentExtensions", "_segBuffer", MNCConstants.CORE_NET_CAPACITY);
                RegisterArray("TrafficManager.Util.Extensions.NetNodeExtensions", "_nodeBuffer", MNCConstants.CORE_NET_CAPACITY);

                // Register game network arrays
                RegisterArray("NetManager", "m_segments", MNCConstants.CORE_NET_CAPACITY);
                RegisterArray("NetManager", "m_nodes", MNCConstants.CORE_NET_CAPACITY);
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
                success &= ExpandStaticArray(flagsType, "laneAllowedVehicleTypesArray", MNCConstants.CORE_NET_CAPACITY);
                
                // 2. Synchronize live game buffers to TM:PE extension buffers (Only if NetManager already exists!)
                if (Singleton<NetManager>.exists)
                {
                    NetManager nm = Singleton<NetManager>.instance;
                    if (nm != null)
                    {
                        if (nm.m_lanes != null && nm.m_lanes.m_buffer != null)
                            SetStaticBuffer(tmpe, "TrafficManager.Util.Extensions.NetLaneExtensions", "_laneBuffer", nm.m_lanes.m_buffer, 262144);
                        if (nm.m_segments != null && nm.m_segments.m_buffer != null)
                            SetStaticBuffer(tmpe, "TrafficManager.Util.Extensions.NetSegmentExtensions", "_segBuffer", nm.m_segments.m_buffer, MNCConstants.CORE_NET_CAPACITY);
                        if (nm.m_nodes != null && nm.m_nodes.m_buffer != null)
                            SetStaticBuffer(tmpe, "TrafficManager.Util.Extensions.NetNodeExtensions", "_nodeBuffer", nm.m_nodes.m_buffer, MNCConstants.CORE_NET_CAPACITY);
                    }
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
                valid &= ValidateStaticArray(flagsType, "laneAllowedVehicleTypesArray", MNCConstants.CORE_NET_CAPACITY);
                
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
                    if (arr != null && arr.Length < MNCConstants.CORE_NET_CAPACITY)
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
        private static bool isMonitoring = false;
        private static bool stopRequested = false;
        private static readonly object monitorLock = new object();
        

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
        /// Enable array monitoring. Safely checked periodically on main simulation thread.
        /// </summary>
        public static void StartMonitoring()
        {
            isMonitoring = true;
            stopRequested = false;
            UnityEngine.Debug.Log("[ArrayGuardianV2] Monitoring enabled (main-thread periodic checks)");
        }
        
        /// <summary>
        /// Stop monitoring. Call from OnLevelUnloading()
        /// </summary>
        public static void StopMonitoring()
        {
            isMonitoring = false;
            stopRequested = true;
            UnityEngine.Debug.Log(string.Format(
                "[ArrayGuardianV2] Monitoring stopped. Stats: {0} checks, {1} repairs, {2} speed limit fixes",
                checksPerformed, repairsTriggered, speedLimitRepairs));
        }

        /// <summary>
        /// Periodic check executed safely on main simulation thread (e.g. every ~60s).
        /// Zero thread contention, zero race conditions.
        /// </summary>
        public static void PerformPeriodicCheck()
        {
            if (!isMonitoring || stopRequested) return;
            try
            {
                lock (monitorLock)
                {
                    PerformMonitoringChecks();
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[ArrayGuardianV2] PerformPeriodicCheck error: " + ex);
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
                needsRepair |= !ValidateArraySize(cachedFlagsType, "laneAllowedVehicleTypesArray", MNCConstants.CORE_NET_CAPACITY);
                
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
        public string Name => "More Nodes/Segments v5.0 by NgocDuy";
        public string Description => "Expands network node and segment capacity to 64K with high simulation FPS, native TM:PE pathfinding, building simulation armor & Mac stability. Renewed by NgocDuy.";

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
                
                // Ensure ALL Harmony patches are applied at game startup / mod enabled
                RenewLoading.EnsureAllPatchesApplied();
                
                AppDomain.CurrentDomain.AssemblyLoad += (sender, args) =>
                {
                    try
                    {
                        if (args.LoadedAssembly != null)
                        {
                            string aName = args.LoadedAssembly.GetName().Name;
                            if (aName == "MoreNetworkCapacity")
                            {
                                EnsureCoreActive();
                            }
                            else if (aName == "MorePathUnits" || aName == "TrafficManager")
                            {
                                ThirdPartyCompatibility.EnsureMorePathUnitsPatched();
                                ThirdPartyCompatibility.SynchronizeMorePathUnits("AssemblyLoad." + aName);
                            }
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
        private const int EXPANDED_SIZE = 65536;
        
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

            try
            {
                Type valType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "MoreNetworkCapacity.Core")
                    {
                        valType = asm.GetType("MoreNetworkCapacity.Compatibility.TMPEBufferValidation");
                        break;
                    }
                }
                if (valType != null)
                {
                    MethodInfo valMethod = valType.GetMethod("Validate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (valMethod != null)
                    {
                        MethodInfo pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.TMPEBufferValidation_Validate_Prefix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (pre != null)
                        {
                            harmony.Patch(valMethod, prefix: new HarmonyMethod(pre));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Armored TMPEBufferValidation.Validate with Harmony prefix.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching TMPEBufferValidation.Validate: " + ex);
            }

            try
            {
                Type sdwType = typeof(SimulationManager).Assembly.GetType("SerializableDataWrapper");
                if (sdwType != null)
                {
                    MethodInfo loadDataM = sdwType.GetMethod("LoadData", new Type[] { typeof(string) });
                    if (loadDataM != null)
                    {
                        var pre = typeof(TMPECompatibility).GetMethod(nameof(TMPECompatibility.SerializableDataWrapper_LoadData_Prefix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(loadDataM, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched SerializableDataWrapper.LoadData (pre-TMPE buffer expansion hook)");
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching SerializableDataWrapper.LoadData: " + ex);
            }

            Assembly tmpe = GetTMPEAssembly();
            if (tmpe == null)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TM:PE assembly not loaded yet; synchronization will apply when available.");
                return;
            }

            try
            {
                // Vanilla game safety: NetSegment.GetLeftAndRightLanes null Info guard
                try
                {
                    MethodInfo glrM = typeof(NetSegment).GetMethod("GetLeftAndRightLanes", BindingFlags.Public | BindingFlags.Instance);
                    if (glrM != null)
                    {
                        var pre = typeof(TMPEPatches).GetMethod(nameof(TMPEPatches.NetSegment_GetLeftAndRightLanes_Prefix));
                        harmony.Patch(glrM, prefix: new HarmonyMethod(pre));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched NetSegment.GetLeftAndRightLanes (null Info defense)");
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching NetSegment.GetLeftAndRightLanes: " + ex);
                }

                // ========================================================================
                // ZERO HARMONY HOOKS INTO TM:PE METHODS ARCHITECTURE (v4.3.40)
                // ========================================================================
                // TM:PE is left 100% native (zero Harmony hooks into TM:PE methods).
                // All integration is achieved via direct memory buffer synchronization and official ThreadingExtensionBase.
                // - VehicleAICommons: Left 100% native to TM:PE for natural physics & coordinates
                // - VehicleBehaviorManager: Left 100% native to TM:PE for native despawn and segment transitions
                // - VehicleBehaviorManager left 100% native to TM:PE (MayChangeSegment is intentionally NOT patched, do NOT override __result or transitState)
                // - TM:PE JunctionRestrictionsManager maintains native authority over junction permissions (JunctionRestrictionsManager.LoadData, JunctionRestrictionsManager.IsEnteringBlockedJunctionAllowed is intentionally left 100% native to TM:PE)
                // - CustomPathFind methods left 100% native to TM:PE without Harmony reflection overhead
                // - CustomPathFind internal routing methods are left 100% native to TM:PE to eliminate thread NREs
                // - CustomPathFind.PathFindImplementation and WaitForAllPaths are intentionally left 100% native
                // - CustomPathFind inner-loop methods (ProcessItemMain, ProcessItemCosts, ProcessItemRouted, etc.) are intentionally NOT hooked with Harmony prefixes to ensure 100% native C# path computation speed
                // - SegmentEndManager, TurnOnRedManager, TrafficMeasurementManager left 100% native (arrays resized to 131,072 contiguously)
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TM:PE left 100% native (zero Harmony hooks into TM:PE methods). Applying memory buffer and array synchronization...");
                SynchronizeTMPEBuffersAndArrays("PatchTMPE");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TMPE initialization error: " + ex);
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
                if (Singleton<NetManager>.exists)
                {
                    NetManager nm = Singleton<NetManager>.instance;
                    if (nm != null)
                    {
                        if (nm.m_segments != null && nm.m_segments.m_buffer != null) SetStaticField(tmpe, "TrafficManager.Util.Extensions.NetSegmentExtensions", "_segBuffer", nm.m_segments.m_buffer);
                        if (nm.m_nodes != null && nm.m_nodes.m_buffer != null) SetStaticField(tmpe, "TrafficManager.Util.Extensions.NetNodeExtensions", "_nodeBuffer", nm.m_nodes.m_buffer);
                        if (nm.m_lanes != null && nm.m_lanes.m_buffer != null) SetStaticField(tmpe, "TrafficManager.Util.Extensions.NetLaneExtensions", "_laneBuffer", nm.m_lanes.m_buffer);
                    }
                }

                if (Singleton<BuildingManager>.exists)
                {
                    BuildingManager bm = Singleton<BuildingManager>.instance;
                    if (bm != null && bm.m_buildings != null && bm.m_buildings.m_buffer != null)
                    {
                        SetStaticField(tmpe, "TrafficManager.Util.Extensions.BuildingExtensions", "_buildingBuffer", bm.m_buildings.m_buffer);
                    }
                }

                if (Singleton<VehicleManager>.exists)
                {
                    VehicleManager vm = Singleton<VehicleManager>.instance;
                    if (vm != null)
                    {
                        if (vm.m_vehicles != null && vm.m_vehicles.m_buffer != null) SetStaticField(tmpe, "TrafficManager.Util.Extensions.VehicleExtensions", "_vehicleBuffer", vm.m_vehicles.m_buffer);
                        if (vm.m_parkedVehicles != null && vm.m_parkedVehicles.m_buffer != null) SetStaticField(tmpe, "TrafficManager.Util.Extensions.ParkedVehicleExtensions", "_parkedVehiclesBuffer", vm.m_parkedVehicles.m_buffer);
                    }
                }

                // 2. Flags static arrays
                Type flagsType = tmpe.GetType("TrafficManager.State.Flags");
                if (flagsType != null)
                {
                    ResizeStaticArray(flagsType, "laneArrowFlags", 524288);
                    ResizeStaticArray(flagsType, "highwayLaneArrowFlags", 524288);
                    ResizeStaticArray(flagsType, "laneAllowedVehicleTypesArray", MNCConstants.CORE_NET_CAPACITY);
                }

                // 3. ParkingRestrictionsManager
                EnsureParkingRestrictionsCapacity(MNCConstants.CORE_NET_CAPACITY);

                // 4. ExtNodeManager & ExtSegmentManager & ExtBuildingManager (Ensure all nodes, segments, and buildings are initialized)
                EnsureExtNodeManagerCapacity(tmpe);
                EnsureExtSegmentManagerCapacity(tmpe);
                EnsureExtBuildingManagerCapacity(tmpe);

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

                // 4d. LaneConnectionManager (Guarantee Road and Track connectionDataBase_ is instantiated)
                EnsureLaneConnectionManagerCapacity(tmpe);

                // 5. Manager arrays
                ResizeAllManagerArrays(tmpe);

                // 5b. SpeedLimitManager capacity & initialize default speeds
                EnsureSpeedLimitManagerCapacity(tmpe);

                // 5c. ExtLaneManager capacity (expand from 262,144 to 524,288 lanes) & ExtVehicleManager (expand from 16,384 to 65,536)
                EnsureExtLaneManagerCapacity(tmpe);
                EnsureExtVehicleManagerCapacity(tmpe);
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
                int targetSize = Math.Max(minSize, MNCConstants.CORE_NET_CAPACITY);
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
        public static int s_laneTrafficDataLength = MNCConstants.CORE_NET_CAPACITY;

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
                int targetLen = MNCConstants.CORE_NET_CAPACITY;
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
                int targetLen = MNCConstants.CORE_NET_CAPACITY;
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
                    FieldInfo fSegId = elemType.GetField("segmentId");
                    for (int i = 0; i < targetArr.Length; i++)
                    {
                        object segObj = targetArr.GetValue(i);
                        bool needsInit = false;
                        if (segObj == null)
                        {
                            needsInit = true;
                        }
                        else if (i > 0 && fSegId != null)
                        {
                            uint currentId = Convert.ToUInt32(fSegId.GetValue(segObj));
                            if (currentId != (uint)i)
                            {
                                needsInit = true;
                            }
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
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Initialized {0} uninitialized ExtSegments in TM:PE ExtSegmentManager", countInitialized));
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureExtSegmentManagerCapacity error: " + ex);
            }
        }

        public static Array s_extBuildingsArray = null;

        public static void EnsureExtBuildingManagerCapacity(Assembly tmpe)
        {
            try
            {
                if (tmpe == null) return;
                Type ebmType = tmpe.GetType("TrafficManager.Manager.Impl.ExtBuildingManager");
                if (ebmType == null) return;
                object inst = GetManagerInstance(ebmType);
                if (inst == null) return;

                FieldInfo fExt = ebmType.GetField("<ExtBuildings>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                              ?? ebmType.GetField("ExtBuildings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fExt != null)
                {
                    Array arr = fExt.GetValue(inst) as Array;
                    if (arr == null || arr.Length < 49152)
                    {
                        Type extBuildingType = fExt.FieldType.GetElementType();
                        if (extBuildingType != null)
                        {
                            Array newArr = Array.CreateInstance(extBuildingType, 49152);
                            ConstructorInfo ctor = extBuildingType.GetConstructor(new Type[] { typeof(ushort) });
                            for (int i = 0; i < 49152; i++)
                            {
                                if (ctor != null)
                                {
                                    newArr.SetValue(ctor.Invoke(new object[] { (ushort)i }), i);
                                }
                            }
                            fExt.SetValue(inst, newArr);
                            arr = newArr;
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Initialized and assigned TM:PE ExtBuildingManager.ExtBuildings (capacity 49152)");
                        }
                    }
                    s_extBuildingsArray = arr;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureExtBuildingManagerCapacity error: " + ex);
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
                if (fSeg != null && fSeg.GetValue(inst) is ulong[] segBuckets && segBuckets.Length < MNCConstants.CORE_BUCKET_CAPACITY)
                {
                    ulong[] newBuckets = new ulong[MNCConstants.CORE_BUCKET_CAPACITY];
                    int copyLen = Math.Min(segBuckets.Length, MNCConstants.CORE_BUCKET_CAPACITY);
                    Array.Copy(segBuckets, newBuckets, copyLen);
                    fSeg.SetValue(inst, newBuckets);
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized GeometryManager.updatedSegmentBuckets: {0} -> 1016", segBuckets.Length));
                }

                FieldInfo fNode = gmType.GetField("updatedNodeBuckets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fNode != null && fNode.GetValue(inst) is ulong[] nodeBuckets && nodeBuckets.Length < MNCConstants.CORE_BUCKET_CAPACITY)
                {
                    ulong[] newBuckets = new ulong[MNCConstants.CORE_BUCKET_CAPACITY];
                    int copyLen = Math.Min(nodeBuckets.Length, MNCConstants.CORE_BUCKET_CAPACITY);
                    Array.Copy(nodeBuckets, newBuckets, copyLen);
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

                // 1. updatedSegmentBuckets (size 576 -> 1016, strictly matches 65,024 segments: 1016 * 64 = 65,024)
                FieldInfo fBuckets = rmType.GetField("updatedSegmentBuckets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fBuckets != null && fBuckets.GetValue(inst) is ulong[] buckets && buckets.Length < MNCConstants.CORE_BUCKET_CAPACITY)
                {
                    ulong[] newBuckets = new ulong[1016];
                    int copyLen = Math.Min(buckets.Length, MNCConstants.CORE_BUCKET_CAPACITY);
                    Array.Copy(buckets, newBuckets, copyLen);
                    fBuckets.SetValue(inst, newBuckets);
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized RoutingManager.updatedSegmentBuckets: {0} -> 1016", buckets.Length));
                }

                // 2. SegmentRoutings (size 36864 -> 65024)
                FieldInfo fSegRoutings = rmType.GetField("<SegmentRoutings>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                      ?? rmType.GetField("SegmentRoutings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fSegRoutings != null && fSegRoutings.GetValue(inst) is Array segRoutings && segRoutings.Length < MNCConstants.CORE_NET_CAPACITY)
                {
                    Array newSegRoutings = Array.CreateInstance(segRoutings.GetType().GetElementType(), MNCConstants.CORE_NET_CAPACITY);
                    Array.Copy(segRoutings, newSegRoutings, segRoutings.Length);
                    fSegRoutings.SetValue(inst, newSegRoutings);
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized RoutingManager.SegmentRoutings: {0} -> 65024", segRoutings.Length));
                }

                // 3. LaneEndBackwardRoutings & LaneEndForwardRoutings:
                // NetManager.MAX_LANE_COUNT in Cities: Skylines is 262,144. TM:PE indexes: GetLaneEndRoutingIndex(laneId, startNode) = laneId + (startNode ? 0u : 262144u).
                // Array size MUST be exactly 524,288 with offset 262,144 so end-nodes are looked up correctly.
                FieldInfo fBack = rmType.GetField("<LaneEndBackwardRoutings>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                               ?? rmType.GetField("LaneEndBackwardRoutings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fBack != null && fBack.GetValue(inst) is Array backArr)
                {
                    if (backArr.Length > 524288)
                    {
                        Array normBack = Array.CreateInstance(backArr.GetType().GetElementType(), 524288);
                        Array.Copy(backArr, 0, normBack, 0, 262144);
                        Array.Copy(backArr, 524288, normBack, 262144, 262144);
                        fBack.SetValue(inst, normBack);
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Normalized RoutingManager.LaneEndBackwardRoutings: {0} -> 524288 (offset 262144 restored)", backArr.Length));
                    }
                    else if (backArr.Length < 524288)
                    {
                        Array normBack = Array.CreateInstance(backArr.GetType().GetElementType(), 524288);
                        Array.Copy(backArr, normBack, backArr.Length);
                        fBack.SetValue(inst, normBack);
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Initialized RoutingManager.LaneEndBackwardRoutings: {0} -> 524288", backArr.Length));
                    }
                }

                FieldInfo fFwd = rmType.GetField("<LaneEndForwardRoutings>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                              ?? rmType.GetField("LaneEndForwardRoutings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fFwd != null && fFwd.GetValue(inst) is Array fwdArr)
                {
                    if (fwdArr.Length > 524288)
                    {
                        Array normFwd = Array.CreateInstance(fwdArr.GetType().GetElementType(), 524288);
                        Array.Copy(fwdArr, 0, normFwd, 0, 262144);
                        Array.Copy(fwdArr, 524288, normFwd, 262144, 262144);
                        fFwd.SetValue(inst, normFwd);
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Normalized RoutingManager.LaneEndForwardRoutings: {0} -> 524288 (offset 262144 restored)", fwdArr.Length));
                    }
                    else if (fwdArr.Length < 524288)
                    {
                        Array normFwd = Array.CreateInstance(fwdArr.GetType().GetElementType(), 524288);
                        Array.Copy(fwdArr, normFwd, fwdArr.Length);
                        fFwd.SetValue(inst, normFwd);
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Initialized RoutingManager.LaneEndForwardRoutings: {0} -> 524288", fwdArr.Length));
                    }
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
                    int targetSize = MNCConstants.CORE_NET_CAPACITY;
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

        public static MethodInfo CreateDynamicGetterPrefix(Type targetType, MethodInfo helperMethod)
        {
            try
            {
                if (targetType == null || helperMethod == null) return null;
                string asmNameStr = "MNC_DynamicPatch_" + targetType.Name;
                var asmName = new AssemblyName(asmNameStr);
                var asmBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);
                var modBuilder = asmBuilder.DefineDynamicModule(asmNameStr + "_Module");
                var typeBuilder = modBuilder.DefineType(asmNameStr + "_Type", TypeAttributes.Public);

                Type refTargetType = targetType.MakeByRefType();
                var methodBuilder = typeBuilder.DefineMethod(
                    "Prefix",
                    MethodAttributes.Public | MethodAttributes.Static,
                    typeof(bool),
                    new Type[] { refTargetType }
                );
                methodBuilder.DefineParameter(1, ParameterAttributes.None, "__result");

                var il = methodBuilder.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0); // push &__result
                il.Emit(OpCodes.Call, helperMethod); // calls EnsureAndGet...() returning object
                il.Emit(OpCodes.Castclass, targetType);
                il.Emit(OpCodes.Stind_Ref); // *__result = (targetType)obj
                il.Emit(OpCodes.Ldc_I4_0); // return false (skip original getter)
                il.Emit(OpCodes.Ret);

                Type dynamicType = typeBuilder.CreateType();
                return dynamicType.GetMethod("Prefix");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] CreateDynamicGetterPrefix for " + (targetType != null ? targetType.Name : "null") + " error: " + ex);
                return null;
            }
        }

        private static object s_persistentSavedGameOptions = null;
        private static FieldInfo s_sgoBackingField = null;
        private static FieldInfo s_sgoAvailableField = null;

        public static object EnsureAndGetSavedGameOptions()
        {
            try
            {
                Assembly tmpe = GetTMPEAssembly();
                if (tmpe == null) return s_persistentSavedGameOptions;
                Type sgoType = tmpe.GetType("TrafficManager.State.SavedGameOptions");
                if (sgoType == null) return s_persistentSavedGameOptions;

                if (s_sgoBackingField == null)
                {
                    s_sgoBackingField = sgoType.GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);
                }
                if (s_sgoAvailableField == null)
                {
                    s_sgoAvailableField = sgoType.GetField("<Available>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);
                }

                object inst = s_sgoBackingField != null ? s_sgoBackingField.GetValue(null) : null;
                if (inst != null)
                {
                    s_persistentSavedGameOptions = inst;
                    if (s_sgoAvailableField != null) s_sgoAvailableField.SetValue(null, true);
                    return inst;
                }

                if (s_persistentSavedGameOptions != null)
                {
                    if (s_sgoBackingField != null) s_sgoBackingField.SetValue(null, s_persistentSavedGameOptions);
                    if (s_sgoAvailableField != null) s_sgoAvailableField.SetValue(null, true);
                    return s_persistentSavedGameOptions;
                }

                MethodInfo createM = sgoType.GetMethod("Create", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (createM != null)
                {
                    createM.Invoke(null, null);
                }
                inst = s_sgoBackingField != null ? s_sgoBackingField.GetValue(null) : null;
                if (inst == null)
                {
                    inst = Activator.CreateInstance(sgoType);
                    if (s_sgoBackingField != null) s_sgoBackingField.SetValue(null, inst);
                }
                s_persistentSavedGameOptions = inst;
                if (s_sgoAvailableField != null) s_sgoAvailableField.SetValue(null, true);
                return inst;
            }
            catch (Exception ex)
            {
                SaveSimulationDataSafetyPatch.LogSaveDebug("EnsureAndGetSavedGameOptions error: " + ex);
                return s_persistentSavedGameOptions;
            }
        }

        private static object s_persistentGlobalConfig = null;
        private static FieldInfo s_gcInstanceField = null;

        public static object EnsureAndGetGlobalConfig()
        {
            try
            {
                Assembly tmpe = GetTMPEAssembly();
                if (tmpe == null) return s_persistentGlobalConfig;
                Type gcType = tmpe.GetType("TrafficManager.State.GlobalConfig");
                if (gcType == null) return s_persistentGlobalConfig;

                if (s_gcInstanceField == null)
                {
                    s_gcInstanceField = gcType.GetField("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                }

                object inst = s_gcInstanceField != null ? s_gcInstanceField.GetValue(null) : null;
                if (inst != null)
                {
                    s_persistentGlobalConfig = inst;
                    return inst;
                }

                if (s_persistentGlobalConfig != null)
                {
                    if (s_gcInstanceField != null) s_gcInstanceField.SetValue(null, s_persistentGlobalConfig);
                    return s_persistentGlobalConfig;
                }

                MethodInfo reloadM = gcType.GetMethod("Reload", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (reloadM != null)
                {
                    reloadM.Invoke(null, new object[] { false });
                }
                inst = s_gcInstanceField != null ? s_gcInstanceField.GetValue(null) : null;
                if (inst == null)
                {
                    inst = Activator.CreateInstance(gcType);
                    if (s_gcInstanceField != null) s_gcInstanceField.SetValue(null, inst);
                }
                s_persistentGlobalConfig = inst;
                return inst;
            }
            catch (Exception ex)
            {
                SaveSimulationDataSafetyPatch.LogSaveDebug("EnsureAndGetGlobalConfig error: " + ex);
                return s_persistentGlobalConfig;
            }
        }

        public static void EnsureSavedGameOptions(Assembly tmpe)
        {
            EnsureAndGetSavedGameOptions();
            EnsureAndGetGlobalConfig();
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
                    int targetSize = MNCConstants.CORE_NET_CAPACITY;
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
                    int targetSize = MNCConstants.CORE_NET_CAPACITY;
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

                SanitizeAndRepairTrafficLightSimulations(tmpe, false);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureTrafficLightSimulationCapacity error: " + ex);
            }
        }

        public static void SanitizeAndRepairTrafficLightSimulations(Assembly tmpe, bool cleanupEmptyOnLoad = false)
        {
            if (tmpe == null) return;
            try
            {
                Type tlsmType = tmpe.GetType("TrafficManager.Manager.Impl.TrafficLightSimulationManager");
                if (tlsmType == null) return;
                object tlsm = GetManagerInstance(tlsmType);
                if (tlsm == null) return;

                FieldInfo fSims = tlsmType.GetField("<TrafficLightSimulations>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                               ?? tlsmType.GetField("TrafficLightSimulations", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                Array sims = fSims != null ? fSims.GetValue(tlsm) as Array : null;
                if (sims == null) return;

                Type tlsType = tmpe.GetType("TrafficManager.TrafficLight.Impl.TrafficLightSimulation");
                if (tlsType == null) return;
                FieldInfo fType = tlsType.GetField("type", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo fTimedLight = tlsType.GetField("timedLight", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fType == null || fTimedLight == null) return;

                MethodInfo mRemove = tlsmType.GetMethod("RemoveNodeFromSimulation", new Type[] { typeof(ushort) });
                Type stepType = tmpe.GetType("TrafficManager.TrafficLight.Impl.TimedTrafficLightsStep");
                Type stepListType = stepType != null ? typeof(List<>).MakeGenericType(stepType) : null;
                Type endType = tmpe.GetType("TrafficManager.API.Traffic.ISegmentEndId");
                Type setType = endType != null ? typeof(HashSet<>).MakeGenericType(endType) : null;

                int healedCount = 0;
                int cleanedCount = 0;

                for (int i = 0; i < sims.Length; i++)
                {
                    object sim = sims.GetValue(i);
                    if (sim == null) continue;

                    int typeInt = Convert.ToInt32(fType.GetValue(sim));
                    if (typeInt == 2) // TrafficLightSimulationType.Timed
                    {
                        object tl = fTimedLight.GetValue(sim);
                        if (tl == null)
                        {
                            fType.SetValue(sim, Enum.ToObject(fType.FieldType, 0));
                            sims.SetValue(sim, i);
                            if (mRemove != null)
                            {
                                try { mRemove.Invoke(tlsm, new object[] { (ushort)i }); } catch { }
                            }
                            cleanedCount++;
                            continue;
                        }

                        FieldInfo fSteps = tl.GetType().GetField("Steps", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        IList stepsList = fSteps != null ? fSteps.GetValue(tl) as IList : null;

                        PropertyInfo pGroup = tl.GetType().GetProperty("NodeGroup");
                        IList groupList = pGroup != null ? pGroup.GetValue(tl, null) as IList : null;

                        if (cleanupEmptyOnLoad && (stepsList == null || stepsList.Count == 0))
                        {
                            fType.SetValue(sim, Enum.ToObject(fType.FieldType, 0));
                            fTimedLight.SetValue(sim, null);
                            sims.SetValue(sim, i);
                            if (mRemove != null)
                            {
                                try { mRemove.Invoke(tlsm, new object[] { (ushort)i }); } catch { }
                            }
                            cleanedCount++;
                            continue;
                        }

                        if (stepsList == null)
                        {
                            if (stepListType != null && fSteps != null)
                            {
                                IList newSteps = (IList)Activator.CreateInstance(stepListType);
                                fSteps.SetValue(tl, newSteps);
                                healedCount++;
                            }
                        }

                        if (groupList == null || groupList.Count == 0)
                        {
                            List<ushort> newGroup = new List<ushort> { (ushort)i };
                            if (pGroup != null && pGroup.CanWrite)
                            {
                                pGroup.SetValue(tl, newGroup, null);
                            }
                            else
                            {
                                FieldInfo fGroup = tl.GetType().GetField("<NodeGroup>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (fGroup != null) fGroup.SetValue(tl, newGroup);
                            }
                            healedCount++;
                        }

                        FieldInfo fEnds = tl.GetType().GetField("segmentEndIds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (fEnds != null && fEnds.GetValue(tl) == null && setType != null)
                        {
                            fEnds.SetValue(tl, Activator.CreateInstance(setType));
                        }

                        FieldInfo fLock = tl.GetType().GetField("rotateLock_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (fLock != null && fLock.GetValue(tl) == null)
                        {
                            fLock.SetValue(tl, new object());
                        }
                    }
                }

                if (healedCount > 0 || cleanedCount > 0)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] SanitizeAndRepairTrafficLightSimulations: healed {0} corrupted timed lights, cleaned {1} orphaned simulations.", healedCount, cleanedCount));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] SanitizeAndRepairTrafficLightSimulations error: " + ex);
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
                    if (f != null && f.GetValue(inst) is Array arr && arr.Length < MNCConstants.CORE_NET_CAPACITY)
                    {
                        Array newArr = Array.CreateInstance(arr.GetType().GetElementType(), MNCConstants.CORE_NET_CAPACITY);
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
                    if (f != null && f.GetValue(inst) is Array arr && arr.Length < MNCConstants.CORE_NET_CAPACITY)
                    {
                        Array newArr = Array.CreateInstance(arr.GetType().GetElementType(), MNCConstants.CORE_NET_CAPACITY);
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

        public static void EnsureJunctionRestrictionsDefaults64k(Assembly tmpe)
        {
            try
            {
                if (tmpe == null || !Singleton<NetManager>.exists || NetManager.instance == null || NetManager.instance.m_segments == null || NetManager.instance.m_segments.m_buffer == null)
                    return;

                Type jrmType = tmpe.GetType("TrafficManager.Manager.Impl.JunctionRestrictionsManager");
                Type esmType = tmpe.GetType("TrafficManager.Manager.Impl.ExtSegmentManager");
                if (jrmType == null || esmType == null) return;

                object jrmInst = GetManagerInstance(jrmType);
                object esmInst = GetManagerInstance(esmType);
                if (jrmInst == null || esmInst == null) return;

                MethodInfo updateDefaultsM = jrmType.GetMethod("HandleValidSegment", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                          ?? jrmType.GetMethod("UpdateDefaults", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                PropertyInfo pExtSegs = esmType.GetProperty("ExtSegments", BindingFlags.Public | BindingFlags.Instance);
                Array extSegs = pExtSegs != null ? pExtSegs.GetValue(esmInst, null) as Array : null;

                if (updateDefaultsM != null && extSegs != null)
                {
                    int jrmCount = 0;
                    uint maxSeg = (uint)Math.Min(extSegs.Length, NetManager.instance.m_segments.m_buffer.Length);
                    for (uint segId = 36864u; segId < maxSeg; segId++)
                    {
                        if ((NetManager.instance.m_segments.m_buffer[segId].m_flags & NetSegment.Flags.Created) != 0 &&
                            NetManager.instance.m_segments.m_buffer[segId].Info != null)
                        {
                            try
                            {
                                object segObj = extSegs.GetValue(segId);
                                if (segObj != null)
                                {
                                    updateDefaultsM.Invoke(jrmInst, new object[] { segObj });
                                    jrmCount++;
                                }
                            }
                            catch { }
                        }
                    }
                    if (jrmCount > 0)
                    {
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Initialized TM:PE native junction defaults for {0} segments >= 36864", jrmCount));
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureJunctionRestrictionsDefaults64k error: " + ex);
            }
        }

        public static void SanitizeRoutingManager(Assembly tmpe)
        {
            try
            {
                if (tmpe == null) return;
                Type rmType = tmpe.GetType("TrafficManager.Manager.Impl.RoutingManager");
                if (rmType == null) return;
                object inst = GetManagerInstance(rmType);
                if (inst == null) return;

                NetManager nm = Singleton<NetManager>.instance;
                if (nm == null || nm.m_segments == null || nm.m_segments.m_buffer == null) return;

                int cleanedTransitions = 0;
                string[] routingFields = new string[] { "<LaneEndBackwardRoutings>k__BackingField", "LaneEndBackwardRoutings", "<LaneEndForwardRoutings>k__BackingField", "LaneEndForwardRoutings" };
                var inspectedArrays = new HashSet<Array>();

                foreach (string fname in routingFields)
                {
                    FieldInfo f = rmType.GetField(fname, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null && f.GetValue(inst) is Array arr && inspectedArrays.Add(arr))
                    {
                        Type elemType = arr.GetType().GetElementType();
                        FieldInfo fRouted = elemType.GetField("routed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        FieldInfo fTransitions = elemType.GetField("transitions", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                        if (fRouted == null || fTransitions == null) continue;

                        int len = arr.Length;
                        for (int i = 0; i < len; i++)
                        {
                            object routingData = arr.GetValue(i);
                            if (routingData == null) continue;

                            bool routed = (bool)fRouted.GetValue(routingData);
                            if (!routed) continue;

                            Array transitions = fTransitions.GetValue(routingData) as Array;
                            if (transitions == null || transitions.Length == 0) continue;

                            Type transType = transitions.GetType().GetElementType();
                            FieldInfo fSegId = transType.GetField("segmentId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (fSegId == null) continue;

                            int validCount = 0;
                            int transLen = transitions.Length;
                            for (int t = 0; t < transLen; t++)
                            {
                                object transItem = transitions.GetValue(t);
                                if (transItem == null) continue;
                                ushort segId = Convert.ToUInt16(fSegId.GetValue(transItem));
                                if (segId > 0 && segId < nm.m_segments.m_buffer.Length &&
                                    (nm.m_segments.m_buffer[segId].m_flags & NetSegment.Flags.Created) != 0 &&
                                    nm.m_segments.m_buffer[segId].Info != null)
                                {
                                    validCount++;
                                }
                            }

                            if (validCount < transLen)
                            {
                                cleanedTransitions += (transLen - validCount);
                                if (validCount == 0)
                                {
                                    fRouted.SetValue(routingData, false);
                                    fTransitions.SetValue(routingData, null);
                                }
                                else
                                {
                                    Array newTrans = Array.CreateInstance(transType, validCount);
                                    int destIdx = 0;
                                    for (int t = 0; t < transLen; t++)
                                    {
                                        object transItem = transitions.GetValue(t);
                                        if (transItem == null) continue;
                                        ushort segId = Convert.ToUInt16(fSegId.GetValue(transItem));
                                        if (segId > 0 && segId < nm.m_segments.m_buffer.Length &&
                                            (nm.m_segments.m_buffer[segId].m_flags & NetSegment.Flags.Created) != 0 &&
                                            nm.m_segments.m_buffer[segId].Info != null)
                                        {
                                            newTrans.SetValue(transItem, destIdx++);
                                        }
                                    }
                                    fTransitions.SetValue(routingData, newTrans);
                                }
                                arr.SetValue(routingData, i);
                            }
                        }
                    }
                }

                if (cleanedTransitions > 0)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Sanitized RoutingManager: Purged {0} stale/corrupted transitions.", cleanedTransitions));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeRoutingManager error: " + ex);
            }
        }

        public static void RequestFullRecalculation64k(Assembly tmpe)
        {
            try
            {
                if (tmpe == null) return;
                Type rmType = tmpe.GetType("TrafficManager.Manager.Impl.RoutingManager");
                if (rmType == null) return;
                object inst = GetManagerInstance(rmType);
                if (inst == null) return;

                FieldInfo fBuckets = rmType.GetField("updatedSegmentBuckets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo fUpdated = rmType.GetField("segmentsUpdated", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo fLock = rmType.GetField("updateLock", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (fBuckets != null && fUpdated != null && fLock != null)
                {
                    object lockObj = fLock.GetValue(inst);
                    ulong[] buckets = fBuckets.GetValue(inst) as ulong[];
                    if (lockObj != null && buckets != null && buckets.Length >= MNCConstants.CORE_BUCKET_CAPACITY)
                    {
                        lock (lockObj)
                        {
                            NetManager nm = Singleton<NetManager>.instance;
                            for (uint segId = 1; segId < nm.m_segments.m_buffer.Length; segId++)
                            {
                                if (nm != null && nm.m_segments != null && (nm.m_segments.m_buffer[segId].m_flags & NetSegment.Flags.Created) != 0)
                                {
                                    buckets[segId >> 6] |= 1uL << (int)(segId & 63);
                                }
                            }
                            fUpdated.SetValue(inst, true);
                        }
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] RequestFullRecalculation64k: Queued all network segments for TM:PE recalculation.");
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] RequestFullRecalculation64k error: " + ex);
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
                if (f != null && f.GetValue(inst) is Array arr && arr.Length < MNCConstants.CORE_SPLIT_CAPACITY)
                {
                    Array newArr = Array.CreateInstance(arr.GetType().GetElementType(), MNCConstants.CORE_SPLIT_CAPACITY);
                    Array.Copy(arr, newArr, arr.Length);
                    f.SetValue(inst, newArr);
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized TurnOnRedManager.TurnOnRedSegments: {0} -> 130048", arr.Length));
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
                    if (arrLane.Length < MNCConstants.CORE_NET_CAPACITY)
                    {
                        Array newArr = Array.CreateInstance(arrLane.GetType().GetElementType(), MNCConstants.CORE_NET_CAPACITY);
                        Array.Copy(arrLane, newArr, arrLane.Length);
                        fLane.SetValue(inst, newArr);
                        s_laneTrafficDataLength = MNCConstants.CORE_NET_CAPACITY;
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized TrafficMeasurementManager.LaneTrafficData: {0} -> 65024", arrLane.Length));
                    }
                    else
                    {
                        s_laneTrafficDataLength = arrLane.Length;
                    }
                }

                FieldInfo fSeg = tmmType.GetField("<SegmentDirTrafficData>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                              ?? tmmType.GetField("SegmentDirTrafficData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fSeg != null && fSeg.GetValue(inst) is Array arrSeg && arrSeg.Length < MNCConstants.CORE_SPLIT_CAPACITY)
                {
                    Array newArr = Array.CreateInstance(arrSeg.GetType().GetElementType(), MNCConstants.CORE_SPLIT_CAPACITY);
                    Array.Copy(arrSeg, newArr, arrSeg.Length);
                    fSeg.SetValue(inst, newArr);
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized TrafficMeasurementManager.SegmentDirTrafficData: {0} -> 130048", arrSeg.Length));
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

                // TM:PE manages speed limits naturally via CacheSegmentSpeeds and CalculateGameSpeedLimit.
                // No artificial default assignment is applied so non-road or custom speeds are preserved.
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
                if (s_extVehiclesArray == null)
                {
                    CacheExtVehicleFields(GetTMPEAssembly());
                }
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

        public static void EnsureExtVehicleManagerCapacity(Assembly tmpe)
        {
            try
            {
                if (tmpe == null) return;
                Type evmType = tmpe.GetType("TrafficManager.Manager.Impl.ExtVehicleManager");
                if (evmType == null) return;

                FieldInfo instField = evmType.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object inst = (instField != null) ? instField.GetValue(null) : null;
                if (inst == null)
                {
                    PropertyInfo instProp = evmType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    if (instProp != null) inst = instProp.GetValue(null, null);
                }
                if (inst == null) return;

                PropertyInfo extVehiclesProp = evmType.GetProperty("ExtVehicles", BindingFlags.Public | BindingFlags.Instance);
                FieldInfo extVehiclesField = evmType.GetField("<ExtVehicles>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    ?? evmType.GetField("_extVehicles", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                VehicleManager vm = Singleton<VehicleManager>.instance;
                int bufferLen = (vm != null && vm.m_vehicles != null && vm.m_vehicles.m_buffer != null) ? vm.m_vehicles.m_buffer.Length : 16384;
                int targetSize = Math.Max(16384, bufferLen);

                Array oldVehicles = null;
                if (extVehiclesField != null) oldVehicles = extVehiclesField.GetValue(inst) as Array;
                if (oldVehicles == null && extVehiclesProp != null) oldVehicles = extVehiclesProp.GetValue(inst, null) as Array;

                if (oldVehicles == null || oldVehicles.Length < targetSize)
                {
                    Assembly apiAsm = GetTMPEApiAssembly() ?? tmpe;
                    Type extVehicleType = apiAsm.GetType("TrafficManager.API.Traffic.Data.ExtVehicle") ?? tmpe.GetType("TrafficManager.API.Traffic.Data.ExtVehicle");
                    if (extVehicleType == null) return;

                    Array newVehicles = Array.CreateInstance(extVehicleType, targetSize);
                    int oldLen = 0;
                    if (oldVehicles != null)
                    {
                        oldLen = oldVehicles.Length;
                        Array.Copy(oldVehicles, newVehicles, oldLen);
                    }

                    ConstructorInfo ctor = extVehicleType.GetConstructor(new Type[] { typeof(ushort) });
                    for (int i = oldLen; i < targetSize; i++)
                    {
                        if (ctor != null)
                        {
                            object item = ctor.Invoke(new object[] { (ushort)i });
                            newVehicles.SetValue(item, i);
                        }
                    }

                    if (extVehiclesField != null) extVehiclesField.SetValue(inst, newVehicles);
                    if (extVehiclesProp != null && extVehiclesProp.CanWrite) extVehiclesProp.SetValue(inst, newVehicles, null);

                    s_extVehiclesArray = newVehicles;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Expanded TM:PE ExtVehicleManager.ExtVehicles: {0} -> {1}", oldLen, targetSize));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error in EnsureExtVehicleManagerCapacity: " + ex);
            }
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
                            if (arr.Length < MNCConstants.CORE_SPLIT_CAPACITY)
                            {
                                Array newArr = Array.CreateInstance(elemType, MNCConstants.CORE_SPLIT_CAPACITY);
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
                                FieldInfo fNodeId = elemType.GetField("nodeId");
                                NetManager nm = Singleton<NetManager>.instance;
                                bool hasNet = (nm != null && nm.m_segments != null && nm.m_segments.m_buffer != null);

                                int countFixed = 0;
                                for (uint segId = 0; segId < MNCConstants.CORE_NET_CAPACITY; segId++)
                                {
                                    int idxStart = (int)(segId * 2);
                                    int idxEnd = idxStart + 1;
                                    object argId = isUint ? (object)segId : (object)(ushort)segId;

                                    ushort sNode = 0;
                                    ushort eNode = 0;
                                    bool isCreated = false;
                                    if (hasNet && segId < nm.m_segments.m_buffer.Length)
                                    {
                                        isCreated = (nm.m_segments.m_buffer[segId].m_flags & NetSegment.Flags.Created) != 0;
                                        if (isCreated)
                                        {
                                            sNode = nm.m_segments.m_buffer[segId].m_startNode;
                                            eNode = nm.m_segments.m_buffer[segId].m_endNode;
                                        }
                                    }

                                    object s0 = targetArr.GetValue(idxStart);
                                    if (s0 == null || (segId > 0 && fSegId != null && Convert.ToUInt32(fSegId.GetValue(s0)) != segId))
                                    {
                                        s0 = ctor.Invoke(new object[] { argId, true });
                                        countFixed++;
                                    }
                                    if (isCreated && sNode != 0 && fNodeId != null)
                                    {
                                        ushort currNode = Convert.ToUInt16(fNodeId.GetValue(s0));
                                        if (currNode == 0 || currNode != sNode)
                                        {
                                            fNodeId.SetValue(s0, sNode);
                                        }
                                    }
                                    targetArr.SetValue(s0, idxStart);

                                    object s1 = targetArr.GetValue(idxEnd);
                                    if (s1 == null || (segId > 0 && fSegId != null && Convert.ToUInt32(fSegId.GetValue(s1)) != segId))
                                    {
                                        s1 = ctor.Invoke(new object[] { argId, false });
                                        countFixed++;
                                    }
                                    if (isCreated && eNode != 0 && fNodeId != null)
                                    {
                                        ushort currNode = Convert.ToUInt16(fNodeId.GetValue(s1));
                                        if (currNode == 0 || currNode != eNode)
                                        {
                                            fNodeId.SetValue(s1, eNode);
                                        }
                                    }
                                    targetArr.SetValue(s1, idxEnd);
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
                        if (f != null && f.GetValue(inst) is Array arr && arr.Length < MNCConstants.CORE_SPLIT_CAPACITY)
                        {
                            Type elemType = arr.GetType().GetElementType();
                            Array newArr = Array.CreateInstance(elemType, MNCConstants.CORE_SPLIT_CAPACITY);
                            Array.Copy(arr, newArr, arr.Length);
                            f.SetValue(inst, newArr);
                            UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Expanded SegmentEndManager.SegmentEnds: {0} -> 130048", arr.Length));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureSegmentEndManagersCapacity error: " + ex);
            }
        }

        public static void EnsureLaneConnectionManagerCapacity(Assembly tmpe)
        {
            try
            {
                if (tmpe == null) return;
                Type lcmType = tmpe.GetType("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionManager");
                if (lcmType == null) return;

                object lcm = GetManagerInstance(lcmType);
                if (lcm == null) return;

                Type cdbType = tmpe.GetType("TrafficManager.Manager.Impl.LaneConnection.ConnectionDataBase");
                if (cdbType == null) return;

                string[] subNames = new string[] { "Road", "Track" };
                foreach (string subName in subNames)
                {
                    FieldInfo fSub = lcmType.GetField(subName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fSub == null) continue;
                    object subMgr = fSub.GetValue(lcm);
                    if (subMgr == null) continue;

                    Type subType = subMgr.GetType();
                    FieldInfo fCdb = subType.GetField("connectionDataBase_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fCdb != null)
                    {
                        object cdb = fCdb.GetValue(subMgr);
                        if (cdb == null)
                        {
                            object newCdb = Activator.CreateInstance(cdbType);
                            fCdb.SetValue(subMgr, newCdb);
                            UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Initialized TM:PE LaneConnectionSubManager.{0}.connectionDataBase_", subName));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureLaneConnectionManagerCapacity error: " + ex);
            }
        }

        // ========================================================================
        // TM:PE SUBTOOL UPPER-NETWORK SYNCHRONIZER (ZERO HARMONY HOOKS)
        // ========================================================================
        private static int s_toolSyncFrameCounter = 0;
        private static MethodInfo s_mGetLaneEnds = null;

        public static void SynchronizeActiveTool()
        {
            s_toolSyncFrameCounter++;
            if ((s_toolSyncFrameCounter & 15) != 0) return;

            ToolController tc = ToolsModifierControl.toolController;
            if (tc == null) return;
            ToolBase currentTool = tc.CurrentTool;
            if (currentTool == null || currentTool.GetType().Name != "TrafficManagerTool")
                return;

            Assembly tmpe = GetTMPEAssembly();
            if (tmpe == null) return;

            Type tmToolType = currentTool.GetType();
            FieldInfo fLegacySubTool = tmToolType.GetField("activeLegacySubTool_", BindingFlags.NonPublic | BindingFlags.Instance);
            object activeSubTool = fLegacySubTool != null ? fLegacySubTool.GetValue(currentTool) : null;
            if (activeSubTool == null)
            {
                FieldInfo fActiveSubTool = tmToolType.GetField("activeSubTool_", BindingFlags.NonPublic | BindingFlags.Instance);
                activeSubTool = fActiveSubTool != null ? fActiveSubTool.GetValue(currentTool) : null;
            }
            if (activeSubTool == null) return;

            Type subToolType = activeSubTool.GetType();
            string subToolName = subToolType.Name;

            ExpandSubToolCaches(activeSubTool, subToolType);

            switch (subToolName)
            {
                case "JunctionRestrictionsTool":
                    SynchronizeJunctionRestrictionsTool(activeSubTool, subToolType, tmpe);
                    break;
                case "TimedTrafficLightsTool":
                    SynchronizeTimedTrafficLightsTool(activeSubTool, subToolType, tmpe);
                    break;
                case "PrioritySignsTool":
                    SynchronizePrioritySignsTool(activeSubTool, subToolType, tmpe);
                    break;
                case "VehicleRestrictionsTool":
                    SynchronizeVehicleRestrictionsTool(activeSubTool, subToolType, tmpe);
                    break;
                case "LaneConnectorTool":
                    SynchronizeLaneConnectorTool(activeSubTool, subToolType, tmpe);
                    break;
                case "ToggleTrafficLightsTool":
                    SynchronizeToggleTrafficLightsTool(activeSubTool, subToolType);
                    break;
                case "SpeedLimitsTool":
                    SynchronizeSpeedLimitsTool(activeSubTool, subToolType);
                    break;
                case "ParkingRestrictionsTool":
                    SynchronizeParkingRestrictionsTool(activeSubTool, subToolType);
                    break;
            }
        }

        private static void ExpandSubToolCaches(object subTool, Type subToolType)
        {
            try
            {
                FieldInfo[] fields = subToolType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo f = fields[i];
                    if (f.FieldType.Name.StartsWith("GenericArrayCache"))
                    {
                        object cacheObj = f.GetValue(subTool);
                        if (cacheObj != null)
                        {
                            EnsureGenericArrayCache64k(cacheObj);
                        }
                    }
                    else if (f.Name == "overlay_" && f.GetValue(subTool) is object overlayObj)
                    {
                        ExpandSubToolCaches(overlayObj, overlayObj.GetType());
                    }
                }
            }
            catch { }
        }

        private static void EnsureGenericArrayCache64k(object cacheObj)
        {
            try
            {
                Type cType = cacheObj.GetType();
                FieldInfo fValues = cType.GetField("Values", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo fMaxSize = cType.GetField("maxSize_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fValues != null && fMaxSize != null)
                {
                    Array arr = fValues.GetValue(cacheObj) as Array;
                    if (arr != null && arr.Length < MNCConstants.CORE_NET_CAPACITY)
                    {
                        Array newArr = Array.CreateInstance(arr.GetType().GetElementType(), MNCConstants.CORE_NET_CAPACITY);
                        Array.Copy(arr, newArr, arr.Length);
                        fValues.SetValue(cacheObj, newArr);
                        fMaxSize.SetValue(cacheObj, MNCConstants.CORE_NET_CAPACITY);
                    }
                }
            }
            catch { }
        }

        private static void SynchronizeJunctionRestrictionsTool(object tool, Type toolType, Assembly tmpe)
        {
            try
            {
                FieldInfo f = toolType.GetField("currentRestrictedNodeIds", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null) return;
                HashSet<ushort> set = f.GetValue(tool) as HashSet<ushort>;
                if (set == null) return;

                Type jrmType = tmpe.GetType("TrafficManager.Manager.Impl.JunctionRestrictionsManager");
                if (jrmType == null) return;
                object jrm = GetManagerInstance(jrmType);
                if (jrm == null) return;

                MethodInfo mHasJR = jrmType.GetMethod("HasJunctionRestrictions", new Type[] { typeof(ushort) });
                if (mHasJR == null) return;

                NetManager nm = Singleton<NetManager>.instance;
                if (nm == null || nm.m_nodes == null || nm.m_nodes.m_buffer == null) return;
                NetNode[] nodeBuffer = nm.m_nodes.m_buffer;

                object[] args = new object[1];
                for (int nodeId = 32768; nodeId < 65536 && nodeId < nodeBuffer.Length; nodeId++)
                {
                    if ((nodeBuffer[nodeId].m_flags & NetNode.Flags.Created) == 0) continue;
                    args[0] = (ushort)nodeId;
                    if ((bool)mHasJR.Invoke(jrm, args))
                    {
                        set.Add((ushort)nodeId);
                    }
                }
            }
            catch { }
        }

        private static void SynchronizeTimedTrafficLightsTool(object tool, Type toolType, Assembly tmpe)
        {
            try
            {
                SanitizeAndRepairTrafficLightSimulations(tmpe, false);

                FieldInfo f = toolType.GetField("currentTimedNodeIds", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null) return;
                HashSet<ushort> set = f.GetValue(tool) as HashSet<ushort>;
                if (set == null) return;

                Type tlsmType = tmpe.GetType("TrafficManager.Manager.Impl.TrafficLightSimulationManager");
                if (tlsmType == null) return;
                object tlsm = GetManagerInstance(tlsmType);
                if (tlsm == null) return;

                MethodInfo mHasTimed = tlsmType.GetMethod("HasTimedSimulation", new Type[] { typeof(ushort) });
                if (mHasTimed == null) return;

                NetManager nm = Singleton<NetManager>.instance;
                if (nm == null || nm.m_nodes == null || nm.m_nodes.m_buffer == null) return;
                NetNode[] nodeBuffer = nm.m_nodes.m_buffer;

                object[] args = new object[1];
                for (int nodeId = 32768; nodeId < 65536 && nodeId < nodeBuffer.Length; nodeId++)
                {
                    if ((nodeBuffer[nodeId].m_flags & NetNode.Flags.Created) == 0) continue;
                    args[0] = (ushort)nodeId;
                    if ((bool)mHasTimed.Invoke(tlsm, args))
                    {
                        set.Add((ushort)nodeId);
                    }
                }
            }
            catch { }
        }

        private static void SynchronizePrioritySignsTool(object tool, Type toolType, Assembly tmpe)
        {
            try
            {
                FieldInfo f = toolType.GetField("currentPriorityNodeIds", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null) return;
                HashSet<ushort> set = f.GetValue(tool) as HashSet<ushort>;
                if (set == null) return;

                Type tpmType = tmpe.GetType("TrafficManager.Manager.Impl.TrafficPriorityManager");
                if (tpmType == null) return;
                object tpm = GetManagerInstance(tpmType);
                if (tpm == null) return;

                MethodInfo mHasSign = tpmType.GetMethod("HasNodePrioritySign", new Type[] { typeof(ushort) });
                if (mHasSign == null) return;

                NetManager nm = Singleton<NetManager>.instance;
                if (nm == null || nm.m_nodes == null || nm.m_nodes.m_buffer == null) return;
                NetNode[] nodeBuffer = nm.m_nodes.m_buffer;

                object[] args = new object[1];
                for (int nodeId = 32768; nodeId < 65536 && nodeId < nodeBuffer.Length; nodeId++)
                {
                    if ((nodeBuffer[nodeId].m_flags & NetNode.Flags.Created) == 0) continue;
                    args[0] = (ushort)nodeId;
                    if ((bool)mHasSign.Invoke(tpm, args))
                    {
                        set.Add((ushort)nodeId);
                    }
                }
            }
            catch { }
        }

        private static void SynchronizeVehicleRestrictionsTool(object tool, Type toolType, Assembly tmpe)
        {
            try
            {
                FieldInfo f = toolType.GetField("currentRestrictedSegmentIds", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null) return;
                HashSet<ushort> set = f.GetValue(tool) as HashSet<ushort>;
                if (set == null) return;

                Type vrmType = tmpe.GetType("TrafficManager.Manager.Impl.VehicleRestrictionsManager");
                if (vrmType == null) return;
                object vrm = GetManagerInstance(vrmType);
                if (vrm == null) return;

                MethodInfo mHasSeg = vrmType.GetMethod("HasSegmentRestrictions", new Type[] { typeof(ushort) });
                if (mHasSeg == null) return;

                NetManager nm = Singleton<NetManager>.instance;
                if (nm == null || nm.m_segments == null || nm.m_segments.m_buffer == null) return;
                NetSegment[] segBuffer = nm.m_segments.m_buffer;

                object[] args = new object[1];
                for (int segId = 36864; segId < 65536 && segId < segBuffer.Length; segId++)
                {
                    if ((segBuffer[segId].m_flags & NetSegment.Flags.Created) == 0) continue;
                    args[0] = (ushort)segId;
                    if ((bool)mHasSeg.Invoke(vrm, args))
                    {
                        set.Add((ushort)segId);
                    }
                }
            }
            catch { }
        }

        private static void SynchronizeLaneConnectorTool(object tool, Type toolType, Assembly tmpe)
        {
            try
            {
                FieldInfo fCurrent = toolType.GetField("currentLaneEnds_", BindingFlags.NonPublic | BindingFlags.Instance);
                if (fCurrent == null) return;
                IDictionary dict = fCurrent.GetValue(tool) as IDictionary;
                if (dict == null) return;

                Type lcmType = tmpe.GetType("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionManager");
                if (lcmType == null) return;
                object lcm = GetManagerInstance(lcmType);
                if (lcm == null) return;

                MethodInfo mHasNodeConn = lcmType.GetMethod("HasNodeConnections", new Type[] { typeof(ushort) });
                if (mHasNodeConn == null) return;

                if (s_mGetLaneEnds == null)
                {
                    s_mGetLaneEnds = toolType.GetMethod("GetLaneEnds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                }
                if (s_mGetLaneEnds == null) return;

                NetManager nm = Singleton<NetManager>.instance;
                if (nm == null || nm.m_nodes == null || nm.m_nodes.m_buffer == null) return;
                NetNode[] nodeBuffer = nm.m_nodes.m_buffer;

                FieldInfo fCache = toolType.GetField("<CachedVisibleNodeIds>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
                object cacheObj = fCache != null ? fCache.GetValue(tool) : null;
                MethodInfo mAdd = cacheObj != null ? cacheObj.GetType().GetMethod("Add") : null;

                Vector3 camPos = Vector3.zero;
                if (Singleton<SimulationManager>.instance != null)
                {
                    camPos = Singleton<SimulationManager>.instance.m_simulationView.m_position;
                }

                object[] hasConnArgs = new object[1];
                object[] gleArgs = new object[3];
                object[] addArgs = new object[1];

                for (int nodeId = 32768; nodeId < 65536 && nodeId < nodeBuffer.Length; nodeId++)
                {
                    if ((nodeBuffer[nodeId].m_flags & NetNode.Flags.Created) == 0) continue;
                    hasConnArgs[0] = (ushort)nodeId;
                    if ((bool)mHasNodeConn.Invoke(lcm, hasConnArgs))
                    {
                        ushort usNodeId = (ushort)nodeId;
                        if (!dict.Contains(usNodeId))
                        {
                            gleArgs[0] = usNodeId;
                            gleArgs[1] = nodeBuffer[nodeId];
                            gleArgs[2] = null;
                            object laneEnds = s_mGetLaneEnds.Invoke(null, gleArgs);
                            if (laneEnds != null)
                            {
                                dict[usNodeId] = laneEnds;
                            }
                        }

                        if (mAdd != null && camPos != Vector3.zero)
                        {
                            Vector3 pos = nodeBuffer[nodeId].m_position;
                            if ((pos - camPos).sqrMagnitude <= 640000f)
                            {
                                addArgs[0] = usNodeId;
                                try { mAdd.Invoke(cacheObj, addArgs); } catch { }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static void SynchronizeToggleTrafficLightsTool(object tool, Type toolType)
        {
            try
            {
                Assembly tmpe = GetTMPEAssembly();
                if (tmpe != null)
                {
                    SanitizeAndRepairTrafficLightSimulations(tmpe, false);
                }

                FieldInfo fCache = toolType.GetField("<CachedVisibleNodeIds>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
                object cacheObj = fCache != null ? fCache.GetValue(tool) : null;
                if (cacheObj == null) return;
                MethodInfo mAdd = cacheObj.GetType().GetMethod("Add");
                if (mAdd == null) return;

                NetManager nm = Singleton<NetManager>.instance;
                if (nm == null || nm.m_nodes == null || nm.m_nodes.m_buffer == null) return;
                NetNode[] nodeBuffer = nm.m_nodes.m_buffer;

                Vector3 camPos = Vector3.zero;
                if (Singleton<SimulationManager>.instance != null)
                {
                    camPos = Singleton<SimulationManager>.instance.m_simulationView.m_position;
                }
                if (camPos == Vector3.zero) return;

                object[] addArgs = new object[1];
                for (int nodeId = 32768; nodeId < 65536 && nodeId < nodeBuffer.Length; nodeId++)
                {
                    if ((nodeBuffer[nodeId].m_flags & NetNode.Flags.Created) == 0) continue;
                    if (nodeBuffer[nodeId].Info != null && nodeBuffer[nodeId].Info.m_netAI is RoadBaseAI)
                    {
                        Vector3 pos = nodeBuffer[nodeId].m_position;
                        if ((pos - camPos).sqrMagnitude <= 640000f)
                        {
                            addArgs[0] = (ushort)nodeId;
                            try { mAdd.Invoke(cacheObj, addArgs); } catch { }
                        }
                    }
                }
            }
            catch { }
        }

        private static void SynchronizeSpeedLimitsTool(object tool, Type toolType)
        {
            try
            {
                FieldInfo fOverlay = toolType.GetField("overlay_", BindingFlags.NonPublic | BindingFlags.Instance);
                object overlay = fOverlay != null ? fOverlay.GetValue(tool) : null;
                if (overlay == null) return;

                Type oType = overlay.GetType();
                FieldInfo fCache = oType.GetField("cachedVisibleSegmentIds_", BindingFlags.NonPublic | BindingFlags.Instance);
                object cacheObj = fCache != null ? fCache.GetValue(overlay) : null;
                if (cacheObj == null) return;

                Type cachedSegType = oType.Assembly.GetType("TrafficManager.UI.SubTools.SpeedLimits.Overlay.SpeedLimitsOverlay+CachedSegment");
                if (cachedSegType == null) return;

                MethodInfo mAdd = cacheObj.GetType().GetMethod("Add");
                if (mAdd == null) return;

                NetManager nm = Singleton<NetManager>.instance;
                if (nm == null || nm.m_segments == null || nm.m_segments.m_buffer == null) return;
                NetSegment[] segBuffer = nm.m_segments.m_buffer;

                Vector3 camPos = Vector3.zero;
                if (Singleton<SimulationManager>.instance != null)
                {
                    camPos = Singleton<SimulationManager>.instance.m_simulationView.m_position;
                }
                if (camPos == Vector3.zero) return;

                FieldInfo fId = cachedSegType.GetField("id_");
                FieldInfo fCenter = cachedSegType.GetField("center_");
                object[] addArgs = new object[1];

                for (int segId = 36864; segId < 65536 && segId < segBuffer.Length; segId++)
                {
                    if ((segBuffer[segId].m_flags & NetSegment.Flags.Created) == 0) continue;
                    if (segBuffer[segId].Info != null && segBuffer[segId].Info.m_netAI is RoadBaseAI)
                    {
                        Vector3 center = segBuffer[segId].m_bounds.center;
                        if ((center - camPos).sqrMagnitude <= 640000f)
                        {
                            object item = Activator.CreateInstance(cachedSegType);
                            if (fId != null) fId.SetValue(item, (ushort)segId);
                            if (fCenter != null) fCenter.SetValue(item, center);
                            addArgs[0] = item;
                            try { mAdd.Invoke(cacheObj, addArgs); } catch { }
                        }
                    }
                }
            }
            catch { }
        }

        private static void SynchronizeParkingRestrictionsTool(object tool, Type toolType)
        {
            try
            {
                FieldInfo fCache = toolType.GetField("<CachedVisibleSegmentIds>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
                object cacheObj = fCache != null ? fCache.GetValue(tool) : null;
                if (cacheObj == null) return;
                MethodInfo mAdd = cacheObj.GetType().GetMethod("Add");
                if (mAdd == null) return;

                NetManager nm = Singleton<NetManager>.instance;
                if (nm == null || nm.m_segments == null || nm.m_segments.m_buffer == null) return;
                NetSegment[] segBuffer = nm.m_segments.m_buffer;

                Vector3 camPos = Vector3.zero;
                if (Singleton<SimulationManager>.instance != null)
                {
                    camPos = Singleton<SimulationManager>.instance.m_simulationView.m_position;
                }
                if (camPos == Vector3.zero) return;

                object[] addArgs = new object[1];
                for (int segId = 36864; segId < 65536 && segId < segBuffer.Length; segId++)
                {
                    if ((segBuffer[segId].m_flags & NetSegment.Flags.Created) == 0) continue;
                    if (segBuffer[segId].Info != null && segBuffer[segId].Info.m_netAI is RoadBaseAI)
                    {
                        Vector3 pos = segBuffer[segId].m_bounds.center;
                        if ((pos - camPos).sqrMagnitude <= 640000f)
                        {
                            addArgs[0] = (ushort)segId;
                            try { mAdd.Invoke(cacheObj, addArgs); } catch { }
                        }
                    }
                }
            }
            catch { }
        }

        public static Assembly GetTMPECommonsAssembly()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == "CSUtil.Commons") return asm;
            }
            return null;
        }

        public static Assembly GetTMPEAPIAssembly()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == "TMPE.API") return asm;
            }
            return null;
        }

        private static byte[] s_pendingHighIdTMPEData;

        public static void SetPendingHighIdTMPEData(byte[] data)
        {
            s_pendingHighIdTMPEData = data;
        }

        public static void SerializableDataWrapper_LoadData_Prefix(string id)
        {
            string name = id;
            if (name == "TrafficManager_v1.0" || name == "TrafficManager_VersionInfo_v1.0")
            {
                try
                {
                    Assembly tmpe = GetTMPEAssembly();
                    if (tmpe != null)
                    {
                        EnsureParkingRestrictionsCapacity(MNCConstants.CORE_NET_CAPACITY);
                        EnsureTrafficLightSimulationCapacity(tmpe);
                        EnsureTurnOnRedCapacity(tmpe);
                        EnsureSegmentEndManagersCapacity(tmpe);
                        SynchronizeTMPEBuffersAndArrays("Pre-TMPE-Load");
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] SerializableDataWrapper_LoadData_Prefix error: " + ex);
                }
            }
        }

        public static void EnsureExpandedSave()
        {
            try
            {
                var sm = Singleton<SimulationManager>.instance;
                if (sm != null && sm.m_metaData != null)
                {
                    lock (sm.m_metaData)
                    {
                        if (sm.m_metaData.m_modOverride == null)
                        {
                            sm.m_metaData.m_modOverride = new Dictionary<string, bool>();
                        }
                        sm.m_metaData.m_modOverride["User/MoreNetworkCapacity/64K-v1"] = true;
                    }
                }

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "MoreNetworkCapacity" || asm.GetName().Name.StartsWith("MoreNetworkCapacity.Core"))
                    {
                        Type metaType = asm.GetType("MoreNetworkCapacity.Metadata");
                        if (metaType != null)
                        {
                            MethodInfo mSet = metaType.GetMethod("SetExpandedSave", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                            if (mSet != null) mSet.Invoke(null, null);
                        }
                        Type patchStateType = asm.GetType("MoreNetworkCapacity.PatchState");
                        if (patchStateType != null)
                        {
                            MethodInfo mMark = patchStateType.GetMethod("MarkSaveExpanded", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                            if (mMark != null) mMark.Invoke(null, null);
                        }
                    }
                }
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureExpandedSave: Confirmed vanilla save mode is EXPANDED (all nodes/segments up to 65,536).");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] EnsureExpandedSave error: " + ex);
            }
        }

        public static byte[] SerializeHighIdTMPEData()
        {
            try
            {
                // Guarantee vanilla expanded save state is locked in whenever TM:PE data is saved
                EnsureExpandedSave();

                Assembly tmpe = GetTMPEAssembly();
                if (tmpe == null) return null;

                using (MemoryStream ms = new MemoryStream())
                using (BinaryWriter bw = new BinaryWriter(ms))
                {
                    bw.Write((uint)0x4D4E4354); // Magic: 'MNCT'
                    bw.Write((byte)2);          // Version 2: Complete Payload

                    // ================================================================
                    // 1. Junction Restrictions (Segments 36,864 .. 65,535)
                    // ================================================================
                    Type jrmType = tmpe.GetType("TrafficManager.Manager.Impl.JunctionRestrictionsManager");
                    object jrm = jrmType != null ? GetManagerInstance(jrmType) : null;
                    List<byte[]> jrRecords = new List<byte[]>();

                    if (jrm != null)
                    {
                        MethodInfo mGetUturn = jrmType.GetMethod("GetUturnAllowed", new Type[] { typeof(ushort), typeof(bool) });
                        MethodInfo mGetBlocked = jrmType.GetMethod("GetEnteringBlockedJunctionAllowed", new Type[] { typeof(ushort), typeof(bool) });
                        MethodInfo mGetPed = jrmType.GetMethod("GetPedestrianCrossingAllowed", new Type[] { typeof(ushort), typeof(bool) });
                        MethodInfo mGetStraight = jrmType.GetMethod("GetLaneChangingAllowedWhenGoingStraight", new Type[] { typeof(ushort), typeof(bool) });
                        MethodInfo mGetNear = jrmType.GetMethod("GetNearTurnOnRedAllowed", new Type[] { typeof(ushort), typeof(bool) });
                        MethodInfo mGetFar = jrmType.GetMethod("GetFarTurnOnRedAllowed", new Type[] { typeof(ushort), typeof(bool) });

                        var nm = Singleton<NetManager>.instance;
                        NetSegment[] segBuffer = (nm != null && nm.m_segments != null) ? nm.m_segments.m_buffer : null;

                        if (segBuffer != null && mGetUturn != null)
                        {
                            object[] args = new object[2];
                            for (int s = 1; s < 65536 && s < segBuffer.Length; s++)
                            {
                                if ((segBuffer[s].m_flags & NetSegment.Flags.Created) == 0) continue;
                                bool isExpanded = (s >= 36864 || segBuffer[s].m_startNode >= 32768 || segBuffer[s].m_endNode >= 32768);
                                if (!isExpanded) continue;

                                for (int end = 0; end < 2; end++)
                                {
                                    bool startNode = (end == 0);
                                    args[0] = (ushort)s;
                                    args[1] = startNode;

                                    byte uturn = Convert.ToByte(mGetUturn.Invoke(jrm, args));
                                    byte blocked = mGetBlocked != null ? Convert.ToByte(mGetBlocked.Invoke(jrm, args)) : (byte)0;
                                    byte ped = mGetPed != null ? Convert.ToByte(mGetPed.Invoke(jrm, args)) : (byte)0;
                                    byte straight = mGetStraight != null ? Convert.ToByte(mGetStraight.Invoke(jrm, args)) : (byte)0;
                                    byte near = mGetNear != null ? Convert.ToByte(mGetNear.Invoke(jrm, args)) : (byte)0;
                                    byte far = mGetFar != null ? Convert.ToByte(mGetFar.Invoke(jrm, args)) : (byte)0;

                                    if (uturn != 0 || blocked != 0 || ped != 0 || straight != 0 || near != 0 || far != 0)
                                    {
                                        using (MemoryStream rMs = new MemoryStream())
                                        using (BinaryWriter rBw = new BinaryWriter(rMs))
                                        {
                                            rBw.Write((ushort)s);
                                            rBw.Write(startNode);
                                            rBw.Write(uturn);
                                            rBw.Write(blocked);
                                            rBw.Write(ped);
                                            rBw.Write(straight);
                                            rBw.Write(near);
                                            rBw.Write(far);
                                            jrRecords.Add(rMs.ToArray());
                                        }
                                    }
                                }
                            }
                        }
                    }

                    bw.Write((int)jrRecords.Count);
                    for (int i = 0; i < jrRecords.Count; i++)
                    {
                        bw.Write(jrRecords[i]);
                    }

                    // ================================================================
                    // 2. Priority Signs (Segments touching expanded network)
                    // ================================================================
                    Type tpmType = tmpe.GetType("TrafficManager.Manager.Impl.TrafficPriorityManager");
                    object tpm = tpmType != null ? GetManagerInstance(tpmType) : null;
                    List<byte[]> psRecords = new List<byte[]>();

                    if (tpm != null)
                    {
                        MethodInfo mGetSign = tpmType.GetMethod("GetPrioritySign", new Type[] { typeof(ushort), typeof(bool) });
                        var nm = Singleton<NetManager>.instance;
                        NetSegment[] segBuffer = (nm != null && nm.m_segments != null) ? nm.m_segments.m_buffer : null;

                        if (segBuffer != null && mGetSign != null)
                        {
                            object[] args = new object[2];
                            for (int s = 1; s < 65536 && s < segBuffer.Length; s++)
                            {
                                if ((segBuffer[s].m_flags & NetSegment.Flags.Created) == 0) continue;
                                bool isExpanded = (s >= 36864 || segBuffer[s].m_startNode >= 32768 || segBuffer[s].m_endNode >= 32768);
                                if (!isExpanded) continue;

                                for (int end = 0; end < 2; end++)
                                {
                                    bool startNode = (end == 0);
                                    args[0] = (ushort)s;
                                    args[1] = startNode;

                                    object signObj = mGetSign.Invoke(tpm, args);
                                    byte sign = Convert.ToByte(signObj);
                                    if (sign != 0)
                                    {
                                        using (MemoryStream rMs = new MemoryStream())
                                        using (BinaryWriter rBw = new BinaryWriter(rMs))
                                        {
                                            rBw.Write((ushort)s);
                                            rBw.Write(startNode);
                                            rBw.Write(sign);
                                            psRecords.Add(rMs.ToArray());
                                        }
                                    }
                                }
                            }
                        }
                    }

                    bw.Write((int)psRecords.Count);
                    for (int i = 0; i < psRecords.Count; i++)
                    {
                        bw.Write(psRecords[i]);
                    }

                    // ================================================================
                    // 3. Parking Restrictions (Segments touching expanded network)
                    // ================================================================
                    Type prmType = tmpe.GetType("TrafficManager.Manager.Impl.ParkingRestrictionsManager");
                    object prm = prmType != null ? GetManagerInstance(prmType) : null;
                    List<byte[]> prRecords = new List<byte[]>();

                    if (prm != null)
                    {
                        MethodInfo mIsParking = prmType.GetMethod("IsParkingAllowed", new Type[] { typeof(uint), typeof(NetInfo.Direction) })
                                             ?? prmType.GetMethod("IsParkingAllowed", new Type[] { typeof(ushort), typeof(NetInfo.Direction) });

                        var nm = Singleton<NetManager>.instance;
                        NetSegment[] segBuffer = (nm != null && nm.m_segments != null) ? nm.m_segments.m_buffer : null;

                        if (segBuffer != null && mIsParking != null)
                        {
                            object[] pArgsFwd = new object[] { 0u, NetInfo.Direction.Forward };
                            object[] pArgsBwd = new object[] { 0u, NetInfo.Direction.Backward };
                            bool useUshort = mIsParking.GetParameters()[0].ParameterType == typeof(ushort);

                            for (int s = 1; s < 65536 && s < segBuffer.Length; s++)
                            {
                                if ((segBuffer[s].m_flags & NetSegment.Flags.Created) == 0) continue;
                                bool isExpanded = (s >= 36864 || segBuffer[s].m_startNode >= 32768 || segBuffer[s].m_endNode >= 32768);
                                if (!isExpanded) continue;

                                if (useUshort)
                                {
                                    pArgsFwd[0] = (ushort)s;
                                    pArgsBwd[0] = (ushort)s;
                                }
                                else
                                {
                                    pArgsFwd[0] = (uint)s;
                                    pArgsBwd[0] = (uint)s;
                                }

                                bool fwdAllowed = true;
                                bool bwdAllowed = true;
                                try { fwdAllowed = (bool)mIsParking.Invoke(prm, pArgsFwd); } catch { }
                                try { bwdAllowed = (bool)mIsParking.Invoke(prm, pArgsBwd); } catch { }

                                if (!fwdAllowed || !bwdAllowed)
                                {
                                    using (MemoryStream rMs = new MemoryStream())
                                    using (BinaryWriter rBw = new BinaryWriter(rMs))
                                    {
                                        rBw.Write((ushort)s);
                                        rBw.Write(fwdAllowed);
                                        rBw.Write(bwdAllowed);
                                        prRecords.Add(rMs.ToArray());
                                    }
                                }
                            }
                        }
                    }

                    bw.Write((int)prRecords.Count);
                    for (int i = 0; i < prRecords.Count; i++)
                    {
                        bw.Write(prRecords[i]);
                    }

                    // ================================================================
                    // 4. Lane Connections (Upper Lanes >= 262,144)
                    // ================================================================
                    Type lcmType = tmpe.GetType("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionManager");
                    object lcm = lcmType != null ? GetManagerInstance(lcmType) : null;
                    List<byte[]> lcRecords = new List<byte[]>();

                    if (lcm != null)
                    {
                        MethodInfo mSaveConn = lcmType.GetMethod("SaveData");
                        if (mSaveConn != null)
                        {
                            object[] connArgs = new object[] { false };
                            IList connList = mSaveConn.Invoke(lcm, connArgs) as IList;
                            if (connList != null)
                            {
                                foreach (var conn in connList)
                                {
                                    if (conn == null) continue;
                                    Type cType = conn.GetType();
                                    uint src = Convert.ToUInt32(cType.GetField("sourceLaneId").GetValue(conn));
                                    uint dst = Convert.ToUInt32(cType.GetField("targetLaneId").GetValue(conn));
                                    bool sn = Convert.ToBoolean(cType.GetField("sourceStartNode").GetValue(conn));
                                    byte grp = Convert.ToByte(cType.GetField("group").GetValue(conn));

                                    if (src >= 262144 || dst >= 262144)
                                    {
                                        using (MemoryStream rMs = new MemoryStream())
                                        using (BinaryWriter rBw = new BinaryWriter(rMs))
                                        {
                                            rBw.Write(src);
                                            rBw.Write(dst);
                                            rBw.Write(sn);
                                            rBw.Write(grp);
                                            lcRecords.Add(rMs.ToArray());
                                        }
                                    }
                                }
                            }
                        }
                    }

                    bw.Write((int)lcRecords.Count);
                    for (int i = 0; i < lcRecords.Count; i++)
                    {
                        bw.Write(lcRecords[i]);
                    }

                    // ================================================================
                    // 5. Lane Speed Limits (Upper Lanes >= 262,144)
                    // ================================================================
                    Type slmType = tmpe.GetType("TrafficManager.Manager.Impl.SpeedLimitManager");
                    object slm = slmType != null ? GetManagerInstance(slmType) : null;
                    List<byte[]> slRecords = new List<byte[]>();

                    if (slm != null)
                    {
                        MethodInfo mGetSpeeds = slmType.GetMethod("GetAllLaneSpeedLimits", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (mGetSpeeds != null)
                        {
                            IDictionary speedDict = mGetSpeeds.Invoke(slm, null) as IDictionary;
                            if (speedDict != null)
                            {
                                foreach (DictionaryEntry de in speedDict)
                                {
                                    uint lId = Convert.ToUInt32(de.Key);
                                    float spd = Convert.ToSingle(de.Value);
                                    if (lId >= 262144 && spd > 0f)
                                    {
                                        using (MemoryStream rMs = new MemoryStream())
                                        using (BinaryWriter rBw = new BinaryWriter(rMs))
                                        {
                                            rBw.Write(lId);
                                            rBw.Write(spd);
                                            slRecords.Add(rMs.ToArray());
                                        }
                                    }
                                }
                            }
                        }
                    }

                    bw.Write((int)slRecords.Count);
                    for (int i = 0; i < slRecords.Count; i++)
                    {
                        bw.Write(slRecords[i]);
                    }

                    // ================================================================
                    // 6. Lane Arrows (Upper Lanes >= 262,144)
                    // ================================================================
                    Type flagsType = tmpe.GetType("TrafficManager.State.Flags");
                    List<byte[]> laRecords = new List<byte[]>();

                    if (flagsType != null)
                    {
                        MethodInfo mGetArrows = flagsType.GetMethod("GetLaneArrowFlags", new Type[] { typeof(uint) });
                        if (mGetArrows != null)
                        {
                            object[] aArgs = new object[1];
                            for (uint l = 262144; l < 524288; l++)
                            {
                                aArgs[0] = l;
                                object arrObj = mGetArrows.Invoke(null, aArgs);
                                if (arrObj != null)
                                {
                                    uint arrVal = Convert.ToUInt32(arrObj);
                                    if (arrVal != 0)
                                    {
                                        using (MemoryStream rMs = new MemoryStream())
                                        using (BinaryWriter rBw = new BinaryWriter(rMs))
                                        {
                                            rBw.Write(l);
                                            rBw.Write(arrVal);
                                            laRecords.Add(rMs.ToArray());
                                        }
                                    }
                                }
                            }
                        }
                    }

                    bw.Write((int)laRecords.Count);
                    for (int i = 0; i < laRecords.Count; i++)
                    {
                        bw.Write(laRecords[i]);
                    }

                    // ================================================================
                    // 7. Vehicle Restrictions (Upper Lanes >= 262,144)
                    // ================================================================
                    List<byte[]> vrRecords = new List<byte[]>();

                    if (flagsType != null)
                    {
                        MethodInfo mGetVeh = flagsType.GetMethod("GetAllLaneAllowedVehicleTypes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (mGetVeh != null)
                        {
                            IEnumerable vehEnum = mGetVeh.Invoke(null, null) as IEnumerable;
                            if (vehEnum != null)
                            {
                                foreach (var item in vehEnum)
                                {
                                    if (item == null) continue;
                                    PropertyInfo pKey = item.GetType().GetProperty("Key");
                                    PropertyInfo pVal = item.GetType().GetProperty("Value");
                                    if (pKey != null && pVal != null)
                                    {
                                        uint lId = Convert.ToUInt32(pKey.GetValue(item, null));
                                        uint vTypes = Convert.ToUInt32(pVal.GetValue(item, null));
                                        if (lId >= 262144)
                                        {
                                            using (MemoryStream rMs = new MemoryStream())
                                            using (BinaryWriter rBw = new BinaryWriter(rMs))
                                            {
                                                rBw.Write(lId);
                                                rBw.Write(vTypes);
                                                vrRecords.Add(rMs.ToArray());
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    bw.Write((int)vrRecords.Count);
                    for (int i = 0; i < vrRecords.Count; i++)
                    {
                        bw.Write(vrRecords[i]);
                    }

                    // ================================================================
                    // 8. Timed Traffic Lights (Nodes 0 .. 65,023 with mixed/high groups)
                    // ================================================================
                    Type tlsmType = tmpe.GetType("TrafficManager.Manager.Impl.TrafficLightSimulationManager");
                    object tlsm = tlsmType != null ? GetManagerInstance(tlsmType) : null;
                    byte[] ttlBytes = null;

                    if (tlsm != null)
                    {
                        SanitizeAndRepairTrafficLightSimulations(tmpe, false);

                        Type ttlCfgType = tmpe.GetType("TrafficManager.Configuration+TimedTrafficLights");
                        Type stepCfgType = tmpe.GetType("TrafficManager.Configuration+TimedTrafficLightsStep");
                        Type cslCfgType = tmpe.GetType("TrafficManager.Configuration+CustomSegmentLights");
                        Type cslLightCfgType = tmpe.GetType("TrafficManager.Configuration+CustomSegmentLight");

                        FieldInfo fSims = tlsmType.GetField("<TrafficLightSimulations>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                       ?? tlsmType.GetField("TrafficLightSimulations", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        Array sims = fSims != null ? fSims.GetValue(tlsm) as Array : null;

                        MethodInfo mHasTimed = tlsmType.GetMethod("HasTimedSimulation", new Type[] { typeof(ushort) });

                        if (sims != null && mHasTimed != null && ttlCfgType != null && stepCfgType != null && cslCfgType != null && cslLightCfgType != null)
                        {
                            Type listType = typeof(List<>).MakeGenericType(ttlCfgType);
                            IList ttlList = (IList)Activator.CreateInstance(listType);

                            Type tlsType = tmpe.GetType("TrafficManager.TrafficLight.Impl.TrafficLightSimulation");
                            FieldInfo fTimedLight = tlsType != null ? tlsType.GetField("timedLight", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) : null;

                            object[] hasArgs = new object[1];
                            for (int n = 0; n < 65536 && n < sims.Length; n++)
                            {
                                try
                                {
                                    hasArgs[0] = (ushort)n;
                                    bool has = (bool)mHasTimed.Invoke(tlsm, hasArgs);
                                    if (!has) continue;

                                    object sim = sims.GetValue(n);
                                    if (sim == null) continue;

                                    object timedLight = fTimedLight != null ? fTimedLight.GetValue(sim) : null;
                                    if (timedLight == null) continue;

                                    ushort masterNodeId = Convert.ToUInt16(timedLight.GetType().GetProperty("NodeId").GetValue(timedLight, null));
                                    if (masterNodeId != (ushort)n)
                                    {
                                        // Not the master node of this group, skip to avoid duplicate serialization
                                        continue;
                                    }

                                    FieldInfo fSteps = timedLight.GetType().GetField("Steps", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    IList stepsList = fSteps != null ? fSteps.GetValue(timedLight) as IList : null;
                                    if (stepsList == null) continue;

                                    PropertyInfo pGroup = timedLight.GetType().GetProperty("NodeGroup");
                                    IEnumerable groupEnum = pGroup != null ? pGroup.GetValue(timedLight, null) as IEnumerable : null;
                                    List<ushort> groupList = new List<ushort>();
                                    if (groupEnum != null)
                                    {
                                        foreach (var gId in groupEnum) groupList.Add(Convert.ToUInt16(gId));
                                    }
                                    if (groupList.Count == 0) groupList.Add((ushort)n);

                                    object ttlObj = Activator.CreateInstance(ttlCfgType);
                                    ttlCfgType.GetField("nodeId").SetValue(ttlObj, (ushort)masterNodeId);
                                    ttlCfgType.GetField("nodeGroup").SetValue(ttlObj, groupList);

                                    MethodInfo mIsStarted = timedLight.GetType().GetMethod("IsStarted", Type.EmptyTypes);
                                    bool isStarted = mIsStarted != null && (bool)mIsStarted.Invoke(timedLight, null);
                                    ttlCfgType.GetField("started").SetValue(ttlObj, isStarted);

                                    PropertyInfo pCurStep = timedLight.GetType().GetProperty("CurrentStep");
                                    int curStep = pCurStep != null ? (int)pCurStep.GetValue(timedLight, null) : 0;
                                    ttlCfgType.GetField("currentStep").SetValue(ttlObj, curStep);

                                    Type stepListType = typeof(List<>).MakeGenericType(stepCfgType);
                                    IList stepList = (IList)Activator.CreateInstance(stepListType);
                                    ttlCfgType.GetField("timedSteps").SetValue(ttlObj, stepList);

                                    MethodInfo mNumSteps = timedLight.GetType().GetMethod("NumSteps", Type.EmptyTypes);
                                    MethodInfo mGetStep = timedLight.GetType().GetMethod("GetStep", new Type[] { typeof(int) });
                                    int numSteps = mNumSteps != null ? (int)mNumSteps.Invoke(timedLight, null) : 0;

                                    for (int stepIdx = 0; stepIdx < numSteps; stepIdx++)
                                    {
                                        object step = mGetStep.Invoke(timedLight, new object[] { stepIdx });
                                        if (step == null) continue;

                                        object stepObj = Activator.CreateInstance(stepCfgType);
                                        stepCfgType.GetField("minTime").SetValue(stepObj, Convert.ToInt32(step.GetType().GetProperty("MinTime").GetValue(step, null)));
                                        stepCfgType.GetField("maxTime").SetValue(stepObj, Convert.ToInt32(step.GetType().GetProperty("MaxTime").GetValue(step, null)));
                                        stepCfgType.GetField("changeMetric").SetValue(stepObj, Convert.ToInt32(step.GetType().GetProperty("ChangeMetric").GetValue(step, null)));
                                        stepCfgType.GetField("waitFlowBalance").SetValue(stepObj, Convert.ToSingle(step.GetType().GetProperty("WaitFlowBalance").GetValue(step, null)));

                                        Type cslDictType = typeof(Dictionary<,>).MakeGenericType(typeof(ushort), cslCfgType);
                                        IDictionary cslDict = (IDictionary)Activator.CreateInstance(cslDictType);
                                        stepCfgType.GetField("segmentLights").SetValue(stepObj, cslDict);

                                        PropertyInfo pCsl = step.GetType().GetProperty("CustomSegmentLights");
                                        IEnumerable cslEnum = pCsl != null ? pCsl.GetValue(step, null) as IEnumerable : null;
                                        if (cslEnum != null)
                                        {
                                            foreach (var cslItem in cslEnum)
                                            {
                                                PropertyInfo pVal = cslItem.GetType().GetProperty("Value");
                                                object csl = pVal != null ? pVal.GetValue(cslItem, null) : cslItem;
                                                if (csl == null) continue;

                                                ushort cslNodeId = Convert.ToUInt16(csl.GetType().GetProperty("NodeId").GetValue(csl, null));
                                                ushort cslSegId = Convert.ToUInt16(csl.GetType().GetProperty("SegmentId").GetValue(csl, null));

                                                object cslObj = Activator.CreateInstance(cslCfgType);
                                                cslCfgType.GetField("nodeId").SetValue(cslObj, (ushort)cslNodeId);
                                                cslCfgType.GetField("segmentId").SetValue(cslObj, (ushort)cslSegId);
                                                cslCfgType.GetField("manualPedestrianMode").SetValue(cslObj, Convert.ToBoolean(csl.GetType().GetProperty("ManualPedestrianMode").GetValue(csl, null)));

                                                object pedState = csl.GetType().GetProperty("PedestrianLightState").GetValue(csl, null);
                                                cslCfgType.GetField("pedestrianLightState").SetValue(cslObj, pedState);

                                                FieldInfo fCustomLights = cslCfgType.GetField("customLights");
                                                IDictionary lightDict = (IDictionary)Activator.CreateInstance(fCustomLights.FieldType);
                                                cslCfgType.GetField("customLights").SetValue(cslObj, lightDict);

                                                Type extVehType = fCustomLights.FieldType.GetGenericArguments()[0];

                                                PropertyInfo pLights = csl.GetType().GetProperty("CustomLights");
                                                IEnumerable lightsEnum = pLights != null ? pLights.GetValue(csl, null) as IEnumerable : null;
                                                if (lightsEnum != null)
                                                {
                                                    foreach (var lightEntry in lightsEnum)
                                                    {
                                                        object key = lightEntry.GetType().GetProperty("Key").GetValue(lightEntry, null);
                                                        object light = lightEntry.GetType().GetProperty("Value").GetValue(lightEntry, null);
                                                        if (light == null) continue;

                                                        int mode = Convert.ToInt32(light.GetType().GetProperty("CurrentMode").GetValue(light, null));
                                                        object leftLight = light.GetType().GetProperty("LightLeft").GetValue(light, null);
                                                        object mainLight = light.GetType().GetProperty("LightMain").GetValue(light, null);
                                                        object rightLight = light.GetType().GetProperty("LightRight").GetValue(light, null);

                                                        object lightObj = Activator.CreateInstance(cslLightCfgType);
                                                        cslLightCfgType.GetField("nodeId").SetValue(lightObj, (ushort)cslNodeId);
                                                        cslLightCfgType.GetField("segmentId").SetValue(lightObj, (ushort)cslSegId);
                                                        cslLightCfgType.GetField("currentMode").SetValue(lightObj, mode);
                                                        cslLightCfgType.GetField("leftLight").SetValue(lightObj, leftLight);
                                                        cslLightCfgType.GetField("mainLight").SetValue(lightObj, mainLight);
                                                        cslLightCfgType.GetField("rightLight").SetValue(lightObj, rightLight);

                                                        object keyObj = Enum.ToObject(extVehType, Convert.ToInt32(key));
                                                        lightDict.Add(keyObj, lightObj);
                                                    }
                                                }

                                                if (!cslDict.Contains(cslSegId))
                                                {
                                                    cslDict.Add(cslSegId, cslObj);
                                                }
                                            }
                                        }

                                        stepList.Add(stepObj);
                                    }

                                    ttlList.Add(ttlObj);
                                }
                                catch (Exception exNode)
                                {
                                    UnityEngine.Debug.LogWarning(string.Format("[MoreNetworkCapacityRenew] SerializeHighIdTMPEData node {0} error: {1}", n, exNode.Message));
                                }
                            }

                            if (ttlList.Count > 0)
                            {
                                using (MemoryStream ttlStream = new MemoryStream())
                                {
                                    BinaryFormatter bf = new BinaryFormatter();
                                    bf.Serialize(ttlStream, ttlList);
                                    ttlBytes = ttlStream.ToArray();
                                }
                            }
                        }
                    }

                    if (ttlBytes != null && ttlBytes.Length > 0)
                    {
                        bw.Write((int)ttlBytes.Length);
                        bw.Write(ttlBytes);
                    }
                    else
                    {
                        bw.Write((int)0);
                    }

                    if (jrRecords.Count == 0 && psRecords.Count == 0 && prRecords.Count == 0 &&
                        lcRecords.Count == 0 && slRecords.Count == 0 && laRecords.Count == 0 &&
                        vrRecords.Count == 0 && (ttlBytes == null || ttlBytes.Length == 0))
                    {
                        return null;
                    }

                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] SerializeHighIdTMPEData error: " + ex);
                return null;
            }
        }

        public static void RestorePendingHighIdTMPEData()
        {
            if (s_pendingHighIdTMPEData == null || s_pendingHighIdTMPEData.Length < 5)
                return;

            try
            {
                Assembly tmpe = GetTMPEAssembly();
                if (tmpe == null) return;

                using (MemoryStream ms = new MemoryStream(s_pendingHighIdTMPEData))
                using (BinaryReader br = new BinaryReader(ms))
                {
                    uint magic = br.ReadUInt32();
                    if (magic != 0x4D4E4354) return;
                    byte ver = br.ReadByte();

                    if (ver == 1)
                    {
                        // 1. Restore Junction Restrictions (Legacy v1)
                        int jrCount = br.ReadInt32();
                        Type jrmType = tmpe.GetType("TrafficManager.Manager.Impl.JunctionRestrictionsManager");
                        object jrm = jrmType != null ? GetManagerInstance(jrmType) : null;
                        Assembly commons = GetTMPECommonsAssembly();
                        Type tbType = commons != null ? commons.GetType("CSUtil.Commons.TernaryBool") : null;

                        MethodInfo mSetUturn = (jrmType != null && tbType != null) ? jrmType.GetMethod("SetUturnAllowed", new Type[] { typeof(ushort), typeof(bool), tbType }) : null;
                        MethodInfo mSetBlocked = (jrmType != null && tbType != null) ? jrmType.GetMethod("SetEnteringBlockedJunctionAllowed", new Type[] { typeof(ushort), typeof(bool), tbType }) : null;
                        MethodInfo mSetPed = (jrmType != null && tbType != null) ? jrmType.GetMethod("SetPedestrianCrossingAllowed", new Type[] { typeof(ushort), typeof(bool), tbType }) : null;
                        MethodInfo mSetStraight = (jrmType != null && tbType != null) ? jrmType.GetMethod("SetLaneChangingAllowedWhenGoingStraight", new Type[] { typeof(ushort), typeof(bool), tbType }) : null;
                        MethodInfo mSetNear = (jrmType != null && tbType != null) ? jrmType.GetMethod("SetNearTurnOnRedAllowed", new Type[] { typeof(ushort), typeof(bool), tbType }) : null;
                        MethodInfo mSetFar = (jrmType != null && tbType != null) ? jrmType.GetMethod("SetFarTurnOnRedAllowed", new Type[] { typeof(ushort), typeof(bool), tbType }) : null;

                        object[] setArgs = new object[3];
                        for (int i = 0; i < jrCount; i++)
                        {
                            ushort segId = br.ReadUInt16();
                            bool startNode = br.ReadBoolean();
                            byte uturn = br.ReadByte();
                            byte blocked = br.ReadByte();
                            byte ped = br.ReadByte();
                            byte straight = br.ReadByte();
                            byte near = br.ReadByte();
                            byte far = br.ReadByte();

                            if (jrm != null && tbType != null)
                            {
                                setArgs[0] = segId;
                                setArgs[1] = startNode;

                                if (uturn != 0 && mSetUturn != null) { setArgs[2] = Enum.ToObject(tbType, uturn); mSetUturn.Invoke(jrm, setArgs); }
                                if (blocked != 0 && mSetBlocked != null) { setArgs[2] = Enum.ToObject(tbType, blocked); mSetBlocked.Invoke(jrm, setArgs); }
                                if (ped != 0 && mSetPed != null) { setArgs[2] = Enum.ToObject(tbType, ped); mSetPed.Invoke(jrm, setArgs); }
                                if (straight != 0 && mSetStraight != null) { setArgs[2] = Enum.ToObject(tbType, straight); mSetStraight.Invoke(jrm, setArgs); }
                                if (near != 0 && mSetNear != null) { setArgs[2] = Enum.ToObject(tbType, near); mSetNear.Invoke(jrm, setArgs); }
                                if (far != 0 && mSetFar != null) { setArgs[2] = Enum.ToObject(tbType, far); mSetFar.Invoke(jrm, setArgs); }
                            }
                        }

                        // 2. Restore Priority Signs (Legacy v1)
                        int psCount = br.ReadInt32();
                        Type tpmType = tmpe.GetType("TrafficManager.Manager.Impl.TrafficPriorityManager");
                        object tpm = tpmType != null ? GetManagerInstance(tpmType) : null;
                        Assembly apiAsm = GetTMPEAPIAssembly();
                        Type ptType = apiAsm != null ? apiAsm.GetType("TrafficManager.API.Traffic.Enums.PriorityType") : null;

                        MethodInfo mSetSign = (tpmType != null && ptType != null) ? tpmType.GetMethod("SetPrioritySign", new Type[] { typeof(ushort), typeof(bool), ptType }) : null;
                        object[] psArgs = new object[3];
                        for (int i = 0; i < psCount; i++)
                        {
                            ushort segId = br.ReadUInt16();
                            bool startNode = br.ReadBoolean();
                            byte sign = br.ReadByte();

                            if (tpm != null && ptType != null && mSetSign != null)
                            {
                                psArgs[0] = segId;
                                psArgs[1] = startNode;
                                psArgs[2] = Enum.ToObject(ptType, sign);
                                try { mSetSign.Invoke(tpm, psArgs); } catch { }
                            }
                        }

                        // 3. Restore Timed Traffic Lights (Legacy v1)
                        int ttlLen = br.ReadInt32();
                        int ttlCount = 0;
                        if (ttlLen > 0)
                        {
                            byte[] ttlBytes = br.ReadBytes(ttlLen);
                            Type tlsmType = tmpe.GetType("TrafficManager.Manager.Impl.TrafficLightSimulationManager");
                            object tlsm = tlsmType != null ? GetManagerInstance(tlsmType) : null;

                            if (tlsm != null)
                            {
                                Type ttlCfgType = tmpe.GetType("TrafficManager.Configuration+TimedTrafficLights");
                                Type listType = typeof(List<>).MakeGenericType(ttlCfgType);
                                MethodInfo mLoad = tlsmType.GetMethod("LoadData", new Type[] { listType });

                                if (mLoad != null)
                                {
                                    using (MemoryStream ttlStream = new MemoryStream(ttlBytes))
                                    {
                                        BinaryFormatter bf = new BinaryFormatter();
                                        IList ttlList = (IList)bf.Deserialize(ttlStream);
                                        if (ttlList != null)
                                        {
                                            ttlCount = ttlList.Count;
                                            mLoad.Invoke(tlsm, new object[] { ttlList });
                                            SanitizeAndRepairTrafficLightSimulations(tmpe, cleanupEmptyOnLoad: false);
                                        }
                                    }
                                }
                            }
                        }

                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Successfully restored High-ID TM:PE Payload v1: {0} Junction Restrictions, {1} Priority Signs, {2} Timed Traffic Lights.",
                            jrCount, psCount, ttlCount));
                    }
                    else if (ver == 2)
                    {
                        // 1. Restore Junction Restrictions
                        int jrCount = br.ReadInt32();
                        Type jrmType = tmpe.GetType("TrafficManager.Manager.Impl.JunctionRestrictionsManager");
                        object jrm = jrmType != null ? GetManagerInstance(jrmType) : null;
                        Assembly commons = GetTMPECommonsAssembly();
                        Type tbType = commons != null ? commons.GetType("CSUtil.Commons.TernaryBool") : null;

                        MethodInfo mSetUturn = (jrmType != null && tbType != null) ? jrmType.GetMethod("SetUturnAllowed", new Type[] { typeof(ushort), typeof(bool), tbType }) : null;
                        MethodInfo mSetBlocked = (jrmType != null && tbType != null) ? jrmType.GetMethod("SetEnteringBlockedJunctionAllowed", new Type[] { typeof(ushort), typeof(bool), tbType }) : null;
                        MethodInfo mSetPed = (jrmType != null && tbType != null) ? jrmType.GetMethod("SetPedestrianCrossingAllowed", new Type[] { typeof(ushort), typeof(bool), tbType }) : null;
                        MethodInfo mSetStraight = (jrmType != null && tbType != null) ? jrmType.GetMethod("SetLaneChangingAllowedWhenGoingStraight", new Type[] { typeof(ushort), typeof(bool), tbType }) : null;
                        MethodInfo mSetNear = (jrmType != null && tbType != null) ? jrmType.GetMethod("SetNearTurnOnRedAllowed", new Type[] { typeof(ushort), typeof(bool), tbType }) : null;
                        MethodInfo mSetFar = (jrmType != null && tbType != null) ? jrmType.GetMethod("SetFarTurnOnRedAllowed", new Type[] { typeof(ushort), typeof(bool), tbType }) : null;

                        object[] setArgs = new object[3];
                        for (int i = 0; i < jrCount; i++)
                        {
                            ushort segId = br.ReadUInt16();
                            bool startNode = br.ReadBoolean();
                            byte uturn = br.ReadByte();
                            byte blocked = br.ReadByte();
                            byte ped = br.ReadByte();
                            byte straight = br.ReadByte();
                            byte near = br.ReadByte();
                            byte far = br.ReadByte();

                            if (jrm != null && tbType != null)
                            {
                                setArgs[0] = segId;
                                setArgs[1] = startNode;

                                if (uturn != 0 && mSetUturn != null) { setArgs[2] = Enum.ToObject(tbType, uturn); mSetUturn.Invoke(jrm, setArgs); }
                                if (blocked != 0 && mSetBlocked != null) { setArgs[2] = Enum.ToObject(tbType, blocked); mSetBlocked.Invoke(jrm, setArgs); }
                                if (ped != 0 && mSetPed != null) { setArgs[2] = Enum.ToObject(tbType, ped); mSetPed.Invoke(jrm, setArgs); }
                                if (straight != 0 && mSetStraight != null) { setArgs[2] = Enum.ToObject(tbType, straight); mSetStraight.Invoke(jrm, setArgs); }
                                if (near != 0 && mSetNear != null) { setArgs[2] = Enum.ToObject(tbType, near); mSetNear.Invoke(jrm, setArgs); }
                                if (far != 0 && mSetFar != null) { setArgs[2] = Enum.ToObject(tbType, far); mSetFar.Invoke(jrm, setArgs); }
                            }
                        }

                        // 2. Restore Priority Signs
                        int psCount = br.ReadInt32();
                        Type tpmType = tmpe.GetType("TrafficManager.Manager.Impl.TrafficPriorityManager");
                        object tpm = tpmType != null ? GetManagerInstance(tpmType) : null;
                        Assembly apiAsm = GetTMPEAPIAssembly();
                        Type ptType = apiAsm != null ? apiAsm.GetType("TrafficManager.API.Traffic.Enums.PriorityType") : null;

                        MethodInfo mSetSign = (tpmType != null && ptType != null) ? tpmType.GetMethod("SetPrioritySign", new Type[] { typeof(ushort), typeof(bool), ptType }) : null;
                        object[] psArgs = new object[3];
                        for (int i = 0; i < psCount; i++)
                        {
                            ushort segId = br.ReadUInt16();
                            bool startNode = br.ReadBoolean();
                            byte sign = br.ReadByte();

                            if (tpm != null && ptType != null && mSetSign != null)
                            {
                                psArgs[0] = segId;
                                psArgs[1] = startNode;
                                psArgs[2] = Enum.ToObject(ptType, sign);
                                try { mSetSign.Invoke(tpm, psArgs); } catch { }
                            }
                        }

                        // 3. Restore Parking Restrictions
                        int prCount = br.ReadInt32();
                        Type prmType = tmpe.GetType("TrafficManager.Manager.Impl.ParkingRestrictionsManager");
                        object prm = prmType != null ? GetManagerInstance(prmType) : null;
                        MethodInfo mSetParking = prmType != null ? (prmType.GetMethod("SetParkingAllowed", new Type[] { typeof(uint), typeof(NetInfo.Direction), typeof(bool) })
                                                                 ?? prmType.GetMethod("SetParkingAllowed", new Type[] { typeof(ushort), typeof(NetInfo.Direction), typeof(bool) })) : null;
                        bool useUshortParking = mSetParking != null && mSetParking.GetParameters()[0].ParameterType == typeof(ushort);
                        object[] prArgsFwd = new object[3];
                        object[] prArgsBwd = new object[3];
                        prArgsFwd[1] = NetInfo.Direction.Forward;
                        prArgsBwd[1] = NetInfo.Direction.Backward;

                        for (int i = 0; i < prCount; i++)
                        {
                            ushort segId = br.ReadUInt16();
                            bool fwd = br.ReadBoolean();
                            bool bwd = br.ReadBoolean();

                            if (prm != null && mSetParking != null)
                            {
                                object sObj = useUshortParking ? (object)segId : (object)(uint)segId;
                                prArgsFwd[0] = sObj;
                                prArgsFwd[2] = fwd;
                                prArgsBwd[0] = sObj;
                                prArgsBwd[2] = bwd;
                                try { mSetParking.Invoke(prm, prArgsFwd); } catch { }
                                try { mSetParking.Invoke(prm, prArgsBwd); } catch { }
                            }
                        }

                        // 4. Restore Lane Connections
                        int lcCount = br.ReadInt32();
                        Type lcmType = tmpe.GetType("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionManager");
                        object lcm = lcmType != null ? GetManagerInstance(lcmType) : null;
                        Type groupType = apiAsm != null ? apiAsm.GetType("TrafficManager.API.Traffic.Enums.LaneEndTransitionGroup") : null;
                        MethodInfo mAddConn = (lcmType != null && groupType != null) ? lcmType.GetMethod("AddLaneConnection", new Type[] { typeof(uint), typeof(uint), typeof(bool), groupType }) : null;
                        object[] lcArgs = new object[4];

                        for (int i = 0; i < lcCount; i++)
                        {
                            uint src = br.ReadUInt32();
                            uint dst = br.ReadUInt32();
                            bool sn = br.ReadBoolean();
                            byte grp = br.ReadByte();

                            if (lcm != null && mAddConn != null && groupType != null)
                            {
                                lcArgs[0] = src;
                                lcArgs[1] = dst;
                                lcArgs[2] = sn;
                                lcArgs[3] = Enum.ToObject(groupType, grp);
                                try { mAddConn.Invoke(lcm, lcArgs); } catch { }
                            }
                        }

                        // 5. Restore Lane Speed Limits
                        int slCount = br.ReadInt32();
                        Type slmType = tmpe.GetType("TrafficManager.Manager.Impl.SpeedLimitManager");
                        object slm = slmType != null ? GetManagerInstance(slmType) : null;
                        Type laneSpeedLimitType = tmpe.GetType("TrafficManager.Configuration+LaneSpeedLimit");
                        Type speedValType = apiAsm != null ? apiAsm.GetType("TrafficManager.API.Traffic.Data.SpeedValue") : null;
                        MethodInfo mSlLoad = null;
                        IList slList = null;
                        if (slm != null && laneSpeedLimitType != null && speedValType != null)
                        {
                            Type slListType = typeof(List<>).MakeGenericType(laneSpeedLimitType);
                            slList = (IList)Activator.CreateInstance(slListType);
                            mSlLoad = slmType.GetMethod("LoadData", new Type[] { slListType });
                        }

                        for (int i = 0; i < slCount; i++)
                        {
                            uint lId = br.ReadUInt32();
                            float spd = br.ReadSingle();

                            if (slList != null && speedValType != null && laneSpeedLimitType != null)
                            {
                                try
                                {
                                    object speedValObj = Activator.CreateInstance(speedValType, new object[] { spd });
                                    object slObj = Activator.CreateInstance(laneSpeedLimitType, new object[] { lId, speedValObj });
                                    slList.Add(slObj);
                                }
                                catch { }
                            }
                        }

                        if (slm != null && mSlLoad != null && slList != null && slList.Count > 0)
                        {
                            try { mSlLoad.Invoke(slm, new object[] { slList }); } catch { }
                        }

                        // 6. Restore Lane Arrows
                        int laCount = br.ReadInt32();
                        Type flagsType = tmpe.GetType("TrafficManager.State.Flags");
                        Type laneArrowsType = tmpe.GetType("TrafficManager.Traffic.Enums.LaneArrows")
                                           ?? tmpe.GetType("TrafficManager.API.Traffic.Enums.LaneArrows");
                        MethodInfo mSetArrows = (flagsType != null && laneArrowsType != null) ? flagsType.GetMethod("SetLaneArrowFlags", new Type[] { typeof(uint), laneArrowsType, typeof(bool) }) : null;
                        object[] laArgs = new object[3];
                        laArgs[2] = true;

                        for (int i = 0; i < laCount; i++)
                        {
                            uint lId = br.ReadUInt32();
                            uint arrows = br.ReadUInt32();

                            if (mSetArrows != null && laneArrowsType != null)
                            {
                                laArgs[0] = lId;
                                laArgs[1] = Enum.ToObject(laneArrowsType, arrows);
                                try { mSetArrows.Invoke(null, laArgs); } catch { }
                            }
                        }

                        // 7. Restore Vehicle Restrictions
                        int vrCount = br.ReadInt32();
                        Type extVehType = tmpe.GetType("TrafficManager.Traffic.ExtVehicleType")
                                       ?? tmpe.GetType("TrafficManager.API.Traffic.Enums.ExtVehicleType");
                        MethodInfo mSetVeh = (flagsType != null && extVehType != null) ? flagsType.GetMethod("SetLaneAllowedVehicleTypes", new Type[] { typeof(uint), extVehType }) : null;
                        object[] vrArgs = new object[2];

                        for (int i = 0; i < vrCount; i++)
                        {
                            uint lId = br.ReadUInt32();
                            uint vTypes = br.ReadUInt32();

                            if (mSetVeh != null && extVehType != null)
                            {
                                vrArgs[0] = lId;
                                vrArgs[1] = Enum.ToObject(extVehType, vTypes);
                                try { mSetVeh.Invoke(null, vrArgs); } catch { }
                            }
                        }

                        // 8. Restore Timed Traffic Lights
                        int ttlLen = br.ReadInt32();
                        int ttlCount = 0;
                        if (ttlLen > 0)
                        {
                            byte[] ttlBytes = br.ReadBytes(ttlLen);
                            Type tlsmType = tmpe.GetType("TrafficManager.Manager.Impl.TrafficLightSimulationManager");
                            object tlsm = tlsmType != null ? GetManagerInstance(tlsmType) : null;

                            if (tlsm != null)
                            {
                                Type ttlCfgType = tmpe.GetType("TrafficManager.Configuration+TimedTrafficLights");
                                Type listType = typeof(List<>).MakeGenericType(ttlCfgType);
                                MethodInfo mLoad = tlsmType.GetMethod("LoadData", new Type[] { listType });

                                if (mLoad != null)
                                {
                                    using (MemoryStream ttlStream = new MemoryStream(ttlBytes))
                                    {
                                        BinaryFormatter bf = new BinaryFormatter();
                                        IList ttlList = (IList)bf.Deserialize(ttlStream);
                                        if (ttlList != null && ttlList.Count > 0)
                                        {
                                            FieldInfo fSims = tlsmType.GetField("<TrafficLightSimulations>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                                           ?? tlsmType.GetField("TrafficLightSimulations", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                            Array sims = fSims != null ? fSims.GetValue(tlsm) as Array : null;

                                            MethodInfo mRemoveNode = tlsmType.GetMethod("RemoveNodeFromSimulation", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(ushort) }, null)
                                                                  ?? tlsmType.GetMethod("RemoveNodeFromSimulation", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(uint) }, null);

                                            if (sims != null)
                                            {
                                                Type tlsType = tmpe.GetType("TrafficManager.TrafficLight.Impl.TrafficLightSimulation");
                                                FieldInfo fType = tlsType != null ? tlsType.GetField("type", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) : null;
                                                FieldInfo fTimedLight = tlsType != null ? tlsType.GetField("timedLight", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) : null;

                                                foreach (var item in ttlList)
                                                {
                                                    uint nId = Convert.ToUInt32(ttlCfgType.GetField("nodeId").GetValue(item));
                                                    List<ushort> nGroup = ttlCfgType.GetField("nodeGroup") != null ? ttlCfgType.GetField("nodeGroup").GetValue(item) as List<ushort> : null;
                                                    List<ushort> allNodesToClean = new List<ushort>();
                                                    allNodesToClean.Add((ushort)nId);
                                                    if (nGroup != null)
                                                    {
                                                        foreach (ushort gn in nGroup)
                                                        {
                                                            if (!allNodesToClean.Contains(gn)) allNodesToClean.Add(gn);
                                                        }
                                                    }

                                                    foreach (ushort cleanNodeId in allNodesToClean)
                                                    {
                                                        if (cleanNodeId < sims.Length)
                                                        {
                                                            if (mRemoveNode != null)
                                                            {
                                                                try { mRemoveNode.Invoke(tlsm, new object[] { (ushort)cleanNodeId }); } catch { }
                                                            }

                                                            object sim = sims.GetValue((int)cleanNodeId);
                                                            if (sim != null)
                                                            {
                                                                if (fTimedLight != null)
                                                                {
                                                                    object tl = fTimedLight.GetValue(sim);
                                                                    if (tl != null)
                                                                    {
                                                                        MethodInfo mDestroy = tl.GetType().GetMethod("Destroy", Type.EmptyTypes);
                                                                        if (mDestroy != null) mDestroy.Invoke(tl, null);
                                                                        fTimedLight.SetValue(sim, null);
                                                                    }
                                                                }
                                                                if (fType != null)
                                                                {
                                                                    fType.SetValue(sim, Enum.ToObject(fType.FieldType, 0));
                                                                }
                                                                sims.SetValue(sim, (int)cleanNodeId);
                                                            }
                                                        }
                                                    }
                                                }
                                            }

                                            mLoad.Invoke(tlsm, new object[] { ttlList });
                                            SanitizeAndRepairTrafficLightSimulations(tmpe, cleanupEmptyOnLoad: false);
                                            ttlCount = ttlList.Count;
                                        }
                                    }
                                }
                            }
                        }

                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Successfully restored High-ID TM:PE Payload v2: {0} Junction Restrictions, {1} Priority Signs, {2} Parking Restrictions, {3} Lane Connections, {4} Speed Limits, {5} Lane Arrows, {6} Vehicle Restrictions, {7} Timed Traffic Lights.",
                            jrCount, psCount, prCount, lcCount, slCount, laCount, vrCount, ttlCount));
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] RestorePendingHighIdTMPEData error: " + ex);
            }
            finally
            {
                s_pendingHighIdTMPEData = null;
            }
        }

        public static void ClearPendingTMPEData()
        {
            s_pendingHighIdTMPEData = null;
        }

        public static void RecalculateLiveSegments(Assembly tmpe)
        {
            try
            {
                if (!Singleton<NetManager>.exists || NetManager.instance == null || NetManager.instance.m_segments == null || NetManager.instance.m_segments.m_buffer == null)
                    return;

                Type esemType = tmpe.GetType("TrafficManager.Manager.Impl.ExtSegmentEndManager");
                if (esemType != null)
                {
                    object inst = GetManagerInstance(esemType);
                    if (inst != null)
                    {
                        MethodInfo recalc = esemType.GetMethod("Recalculate", new Type[] { typeof(ushort) });
                         if (recalc != null)
                        {
                            int recalcCount = 0;
                            uint maxSeg = (uint)Math.Min((int)MNCConstants.CORE_NET_CAPACITY, NetManager.instance.m_segments.m_buffer.Length);
                            for (uint segId = 36864; segId < maxSeg; segId++)
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
                            uint maxSeg = (uint)Math.Min((int)MNCConstants.CORE_NET_CAPACITY, NetManager.instance.m_segments.m_buffer.Length);
                            for (uint segId = 36864; segId < maxSeg; segId++)
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

                EnsureJunctionRestrictionsDefaults64k(tmpe);
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

                            // Only resize original node/segment arrays (32768, 36864) to CORE_NET_CAPACITY (65024)
                            if ((arr.Length == 32768 || arr.Length == 36864 || arr.Length == 32767 || arr.Length == 36863) && arr.Length < MNCConstants.CORE_NET_CAPACITY)
                            {
                                newSize = MNCConstants.CORE_NET_CAPACITY;
                            }
                            // Only resize original SegmentEnd arrays (73728 = 36864 * 2) to CORE_SPLIT_CAPACITY (130048)
                            else if ((arr.Length == 73728 || arr.Length == 73727) && arr.Length < MNCConstants.CORE_SPLIT_CAPACITY)
                            {
                                newSize = MNCConstants.CORE_SPLIT_CAPACITY;
                                if (t.Name != "ExtSegmentEndManager")
                                {
                                    isSplit = true;
                                    splitOldCap = arr.Length / 2;
                                    splitNewCap = MNCConstants.CORE_NET_CAPACITY;
                                }
                            }
                            // Spatial / Geometry buckets (512, 576) to CORE_BUCKET_CAPACITY (1016 = 65024 / 64)
                            else if ((arr.Length == 512 || arr.Length == 576) && arr.Length < MNCConstants.CORE_BUCKET_CAPACITY)
                            {
                                newSize = MNCConstants.CORE_BUCKET_CAPACITY;
                            }
                            // Lane arrays (262144) to LANE_CAPACITY (524288)
                            else if ((arr.Length == 262144 || arr.Length == 262143) && arr.Length < MNCConstants.LANE_CAPACITY)
                            {
                                newSize = MNCConstants.LANE_CAPACITY;
                            }
                            // Note: LaneEnd backward/forward routings in RoutingManager MUST remain exactly 524288 (262144 * 2)
                            // because TM:PE's compiled GetLaneEndRoutingIndex hardcodes NetManager.MAX_LANE_COUNT (262144).


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
                                if (maxField != null) maxField.SetValue(cacheObj, MNCConstants.CORE_NET_CAPACITY);
                                if (valuesField != null && valuesField.GetValue(cacheObj) is Array valArr && valArr.Length < MNCConstants.CORE_NET_CAPACITY)
                                {
                                    Array newValArr = Array.CreateInstance(valArr.GetType().GetElementType(), MNCConstants.CORE_NET_CAPACITY);
                                    Array.Copy(valArr, newValArr, valArr.Length);
                                    valuesField.SetValue(cacheObj, newValArr);
                                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Expanded GenericArrayCache in {0}.{1}: {2} -> {3}", t.Name, f.Name, valArr.Length, MNCConstants.CORE_NET_CAPACITY));
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

            // 4. LaneLocation & LaneTarget (preserved at TM:PE native 262,144 / NetManager.MAX_LANE_COUNT to preserve thread safety and clearing loop)
            FieldInfo fLoc = t.GetField("laneLocation_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                          ?? t.GetField("m_laneLocation", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fLoc != null && fLoc.GetValue(pf) == null)
            {
                fLoc.SetValue(pf, new uint[262144]);
            }

            FieldInfo fTarg = t.GetField("laneTarget_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                           ?? t.GetField("m_laneTarget", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fTarg != null && fTarg.GetValue(pf) == null)
            {
                Type targetElemType = fTarg.FieldType.GetElementType() ?? typeof(PathUnit.Position);
                fTarg.SetValue(pf, Array.CreateInstance(targetElemType, 262144));
            }

            // 6. savedGameOptions_ (guarantee non-null options on pathfinder)
            FieldInfo fSgo = t.GetField("savedGameOptions_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fSgo != null && fSgo.GetValue(pf) == null)
            {
                fSgo.SetValue(pf, EnsureAndGetSavedGameOptions());
            }

            // 7. globalConf_ (guarantee non-null global config on pathfinder)
            FieldInfo fGc = t.GetField("globalConf_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fGc != null && fGc.GetValue(pf) == null)
            {
                fGc.SetValue(pf, EnsureAndGetGlobalConfig());
            }

            // 8. pathUnits_ (guarantee valid PathManager.m_pathUnits on pathfinder)
            FieldInfo fPu = t.GetField("pathUnits_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fPu != null && fPu.GetValue(pf) == null && Singleton<PathManager>.exists)
            {
                fPu.SetValue(pf, Singleton<PathManager>.instance.m_pathUnits);
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

        public delegate void FastRecalcExtSegDelegate(object instance, ushort segmentId);
        private static FastRecalcExtSegDelegate _extSegRecalcDelegate = null;
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
                                _extSegRecalcDelegate = (FastRecalcExtSegDelegate)dm.CreateDelegate(typeof(FastRecalcExtSegDelegate));
                            }
                        }

                        TMPECompatibility.EnsureExtSegmentManagerCapacity(TMPECompatibility.GetTMPEAssembly());
                        TMPECompatibility.EnsureSegmentEndManagersCapacity(TMPECompatibility.GetTMPEAssembly());

                        if (_extSegRecalcDelegate != null)
                        {
                            NetManager nm = Singleton<NetManager>.instance;
                            int maxSeg = MNCConstants.CORE_NET_CAPACITY;
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

        public static void RoutingManager_RecalculateAll_Postfix(object __instance)
        {
        }

        public static bool ExtSegmentManager_Recalculate_ushort_Prefix(ushort segmentId)
        {
            if (segmentId >= MNCConstants.CORE_NET_CAPACITY) return false;
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
                            int maxSeg = MNCConstants.CORE_NET_CAPACITY;
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
            if (segmentId >= MNCConstants.CORE_NET_CAPACITY) return false;
            if (NetManager.instance != null && NetManager.instance.m_segments != null && NetManager.instance.m_segments.m_buffer != null)
            {
                if (segmentId >= NetManager.instance.m_segments.m_buffer.Length) return false;
            }
            return true;
        }

        public static bool SegmentEndManager_UpdateSegmentEnd_Prefix(ushort segmentId, bool startNode, ref bool __result)
        {
            if (segmentId >= MNCConstants.CORE_NET_CAPACITY)
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
            if (segmentId >= MNCConstants.CORE_NET_CAPACITY)
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
            if (segmentId >= MNCConstants.CORE_NET_CAPACITY)
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
            if (segmentId >= MNCConstants.CORE_NET_CAPACITY)
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

                int maxNode = Math.Min((int)MNCConstants.CORE_NET_CAPACITY, NetManager.instance.m_nodes.m_buffer.Length);
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
                int maxNode = (forceNodeId == 0 ? (int)MNCConstants.CORE_NET_CAPACITY : (int)forceNodeId + 1);
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
            try
            {
                Assembly tmpe = TMPECompatibility.GetTMPEAssembly();
                if (tmpe != null)
                {
                    TMPECompatibility.RecalculateLiveSegments(tmpe);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SerializableDataExtension_Load_Postfix error: " + ex);
            }
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
                s_hasAnyUpperTimedLightSimulation = true;
                Assembly tmpe = TMPECompatibility.GetTMPEAssembly();
                if (tmpe != null)
                {
                    TMPECompatibility.EnsureTrafficLightSimulationCapacity(tmpe);
                    TMPECompatibility.EnsureCustomSegmentLightsCapacity(tmpe);
                    TMPECompatibility.SanitizeAndRepairTrafficLightSimulations(tmpe, true);
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
                        if (f != null && f.GetValue(null) is Array arr && arr.Length < MNCConstants.CORE_NET_CAPACITY)
                        {
                            Array newArr = Array.CreateInstance(arr.GetType().GetElementType(), MNCConstants.CORE_NET_CAPACITY);
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
            TMPECompatibility.EnsureParkingRestrictionsCapacity(__instance, MNCConstants.CORE_NET_CAPACITY);
        }

        public static bool ParkingRestrictionsManager_SetParkingAllowed_Prefix(object __instance, ushort segmentId, ref bool __result)
        {
            try
            {
                TMPECompatibility.EnsureParkingRestrictionsCapacity(__instance, Math.Max((int)segmentId + 1, MNCConstants.CORE_NET_CAPACITY));
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
            __result = (int)segmentId + (startNode ? 0 : MNCConstants.CORE_NET_CAPACITY);
            return false;
        }

        public static bool SegmentEndManager_GetIndex_ushort_Prefix(ushort segmentId, bool startNode, ref int __result)
        {
            __result = (int)segmentId + (startNode ? 0 : MNCConstants.CORE_NET_CAPACITY);
            return false;
        }

        public static bool SegmentEndManager_GetSegmentAndNodeFromIndex_Prefix(int index, out uint segmentId, out bool startNode)
        {
            if (index < 0 || index >= MNCConstants.CORE_SPLIT_CAPACITY)
            {
                segmentId = 0u;
                startNode = false;
                return false;
            }
            startNode = index < MNCConstants.CORE_NET_CAPACITY;
            segmentId = (uint)(startNode ? index : (index - MNCConstants.CORE_NET_CAPACITY));
            return false;
        }

        public static bool SegmentEndManager_GetSegmentAndNodeFromIndex_ushort_Prefix(int index, out ushort segmentId, out bool startNode)
        {
            if (index < 0 || index >= MNCConstants.CORE_SPLIT_CAPACITY)
            {
                segmentId = 0;
                startNode = false;
                return false;
            }
            startNode = index < MNCConstants.CORE_NET_CAPACITY;
            segmentId = (ushort)(startNode ? index : (index - MNCConstants.CORE_NET_CAPACITY));
            return false;
        }

        public static bool TurnOnRedManager_GetIndex_Prefix(uint segmentId, bool startNode, ref int __result)
        {
            __result = (int)segmentId + (startNode ? 0 : MNCConstants.CORE_NET_CAPACITY);
            return false;
        }

        public static bool TurnOnRedManager_GetIndex_ushort_Prefix(ushort segmentId, bool startNode, ref int __result)
        {
            __result = (int)segmentId + (startNode ? 0 : MNCConstants.CORE_NET_CAPACITY);
            return false;
        }

        public static bool TrafficMeasurementManager_GetDirIndex_Prefix(uint segmentId, NetInfo.Direction dir, ref int __result)
        {
            __result = (int)segmentId + (dir == NetInfo.Direction.Backward ? MNCConstants.CORE_NET_CAPACITY : 0);
            return false;
        }

        public static bool TrafficMeasurementManager_GetDirIndex_ushort_Prefix(ushort segmentId, NetInfo.Direction dir, ref int __result)
        {
            __result = (int)segmentId + (dir == NetInfo.Direction.Backward ? MNCConstants.CORE_NET_CAPACITY : 0);
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
        public static bool s_hasAnyUpperTimedLightSimulation = true;

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

                NetNode[] nodes = null;
                if (Singleton<NetManager>.exists)
                {
                    var nm = Singleton<NetManager>.instance;
                    if (nm != null && nm.m_nodes != null) nodes = nm.m_nodes.m_buffer;
                }

                for (int i = start; i < end; i++)
                {
                    if (nodes != null && i < nodes.Length)
                    {
                        if ((nodes[i].m_flags & NetNode.Flags.Created) == 0)
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
            __result = (laneInfo != null && laneInfo.m_speedLimit > 0.001f) ? laneInfo.m_speedLimit : 1.0f;
            return false;
        }

        public static bool SpeedLimitManager_GetGameSpeedLimit4_Prefix_Uint(uint segmentId, byte laneIndex, uint laneId, NetInfo.Lane laneInfo, ref float __result)
        {
            if (laneInfo == null && segmentId != 0 && NetManager.instance != null && NetManager.instance.m_segments != null && NetManager.instance.m_segments.m_buffer != null && segmentId < (uint)NetManager.instance.m_segments.m_buffer.Length)
            {
                NetInfo sInfo = NetManager.instance.m_segments.m_buffer[segmentId].Info;
                if (sInfo != null && sInfo.m_lanes != null && laneIndex < sInfo.m_lanes.Length)
                {
                    laneInfo = sInfo.m_lanes[laneIndex];
                }
            }
            return SpeedLimitManager_GetGameSpeedLimit_Prefix(laneId, laneInfo, ref __result);
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

        public static bool CustomPathFind_GetLaneDirection_Prefix(
            PathUnit.Position pathPos,
            ref NetInfo.Direction direction,
            ref NetInfo.LaneType laneType,
            ref VehicleInfo.VehicleType vehicleType)
        {
            ushort segmentId = pathPos.m_segment;
            if (segmentId == 0 || segmentId >= MNCConstants.CORE_NET_CAPACITY)
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

        public static bool CustomPathFind_ProcessItemMain_Prefix(
            ref NetSegment prevSegment,
            ushort nextNodeId,
            ref NetNode nextNode)
        {
            if ((prevSegment.m_flags & NetSegment.Flags.Created) == 0 || prevSegment.Info == null || prevSegment.Info.m_lanes == null)
            {
                return false;
            }
            if (nextNodeId == 0 || (nextNode.m_flags & NetNode.Flags.Created) == 0 || nextNode.Info == null || nextNode.Info.m_class == null)
            {
                return false;
            }
            return true;
        }

        public static bool CustomPathFind_CanUseLane_Prefix(
            ushort segmentId,
            NetInfo segmentInfo,
            int laneIndex,
            NetInfo.Lane laneInfo,
            uint laneId,
            ref bool __result)
        {
            if (segmentInfo == null || laneInfo == null)
            {
                __result = true;
                return false;
            }
            return true;
        }

        public static bool CustomPathFind_ProcessItemPublicTransport_Prefix(
            ref NetSegment prevSegment,
            ref NetSegment nextSegment)
        {
            if ((prevSegment.m_flags & NetSegment.Flags.Created) == 0 || prevSegment.Info == null || prevSegment.Info.m_lanes == null ||
                (nextSegment.m_flags & NetSegment.Flags.Created) == 0 || nextSegment.Info == null || nextSegment.Info.m_lanes == null)
            {
                return false;
            }
            return true;
        }

        public static bool CustomPathFind_ProcessItemCosts_Prefix(
            ref NetSegment prevSegment,
            ref NetSegment nextSegment,
            ref bool __result)
        {
            if ((prevSegment.m_flags & NetSegment.Flags.Created) == 0 || prevSegment.Info == null || prevSegment.Info.m_lanes == null ||
                (nextSegment.m_flags & NetSegment.Flags.Created) == 0 || nextSegment.Info == null || nextSegment.Info.m_lanes == null)
            {
                __result = false;
                return false;
            }
            return true;
        }

        public static bool CustomPathFind_ProcessItemRouted_Prefix(
            ref NetSegment prevSegment,
            ref NetNode nextNode,
            ref bool __result)
        {
            if ((prevSegment.m_flags & NetSegment.Flags.Created) == 0 || prevSegment.Info == null || prevSegment.Info.m_lanes == null ||
                (nextNode.m_flags & NetNode.Flags.Created) == 0 || nextNode.Info == null)
            {
                __result = false;
                return false;
            }
            return true;
        }

        public static bool CustomPathFind_ProcessItemPedBicycle_Prefix(
            ref NetSegment prevSegment,
            ref NetSegment nextSegment)
        {
            if ((prevSegment.m_flags & NetSegment.Flags.Created) == 0 || prevSegment.Info == null || prevSegment.Info.m_lanes == null ||
                (nextSegment.m_flags & NetSegment.Flags.Created) == 0 || nextSegment.Info == null || nextSegment.Info.m_lanes == null)
            {
                return false;
            }
            return true;
        }

        public static bool TMPEBufferValidation_Validate_Prefix(Assembly assembly)
        {
            try
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Armored TMPEBufferValidation.Validate: bypassed fatal exception to protect simulation loading.");
                return false;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TMPEBufferValidation_Validate_Prefix error: " + ex);
                return false;
            }
        }

        public static bool ExtLane_GetLaneInfo_Prefix(uint laneId, ref NetInfo.Lane __result)
        {
            if (laneId == 0 || NetManager.instance == null || NetManager.instance.m_lanes == null || NetManager.instance.m_lanes.m_buffer == null)
            {
                __result = null;
                return false;
            }
            if (laneId >= NetManager.instance.m_lanes.m_buffer.Length)
            {
                __result = null;
                return false;
            }
            if (NetManager.instance.m_segments == null || NetManager.instance.m_segments.m_buffer == null)
            {
                __result = null;
                return false;
            }
            ushort segId = NetManager.instance.m_lanes.m_buffer[laneId].m_segment;
            if (segId == 0 || segId >= NetManager.instance.m_segments.m_buffer.Length)
            {
                __result = null;
                return false;
            }
            NetSegment seg = NetManager.instance.m_segments.m_buffer[segId];
            if ((seg.m_flags & NetSegment.Flags.Created) == 0 || seg.Info == null || seg.Info.m_lanes == null)
            {
                __result = null;
                return false;
            }

            uint curLane = seg.m_lanes;
            int curIndex = 0;
            NetInfo.Lane foundLane = null;
            while (curLane != 0 && curIndex < seg.Info.m_lanes.Length)
            {
                if (curLane == laneId)
                {
                    foundLane = seg.Info.m_lanes[curIndex];
                    break;
                }
                curLane = NetManager.instance.m_lanes.m_buffer[curLane].m_nextLane;
                curIndex++;
            }

            __result = foundLane;
            return false;
        }

        public static bool CommonBuildingAI_SimulationStepPatch_Prefix_Guard(ushort buildingID)
        {
            if (buildingID == 0 || buildingID >= 49152) return false;
            if (TMPECompatibility.s_extBuildingsArray == null) return false;
            return true;
        }

        public static bool ExtBuildingManager_OnBeforeSimulationStep_Prefix(object __instance, ushort buildingId)
        {
            if (__instance == null || buildingId == 0 || buildingId >= 49152)
            {
                return false;
            }
            Array arr = TMPECompatibility.s_extBuildingsArray;
            if (arr == null || buildingId >= arr.Length)
            {
                return false;
            }
            return true;
        }

        public static bool RenderOverlayPatch_Postfix_Prefix()
        {
            try
            {
                Type rspType = Type.GetType("TrafficManager.UI.RoadSelectionPanels, TrafficManager");
                if (rspType != null)
                {
                    PropertyInfo p = rspType.GetProperty("Root", BindingFlags.Public | BindingFlags.Static);
                    if (p != null && p.GetValue(null, null) == null)
                    {
                        return false;
                    }
                }
            }
            catch
            {
                return false;
            }
            return true;
        }

        public static bool NetSegment_GetLeftAndRightLanes_Prefix(
            ref NetSegment __instance,
            ref int leftIndex,
            ref int rightIndex,
            ref uint leftLane,
            ref uint rightLane)
        {
            if (__instance.Info == null || __instance.Info.m_lanes == null)
            {
                leftIndex = -1;
                rightIndex = -1;
                leftLane = 0;
                rightLane = 0;
                return false;
            }
            return true;
        }

        public static bool WaitForAllPaths_Prefix(object __instance)
        {
            if (__instance == null) return false;
            try
            {
                Type t = __instance.GetType();
                FieldInfo fLock = t.GetField("queueLock_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                               ?? t.GetField("m_queueLock", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo fFirst = t.GetField("queueFirst_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                ?? t.GetField("m_queueFirst", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo fCalc = t.GetField("calculating_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                               ?? t.GetField("m_calculating", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo fTerm = t.GetField("terminated_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                               ?? t.GetField("m_terminated", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (fLock == null) return true;
                object lockObj = fLock.GetValue(__instance);
                if (lockObj == null) return false;

                if (!System.Threading.Monitor.TryEnter(lockObj, 2000))
                {
                    SaveSimulationDataSafetyPatch.LogSaveDebug("WaitForAllPaths_Prefix: lock timeout on " + t.Name + ", continuing safely.");
                    return false;
                }

                try
                {
                    int loop = 0;
                    while (loop++ < 15)
                    {
                        uint qFirst = fFirst != null ? Convert.ToUInt32(fFirst.GetValue(__instance)) : 0;
                        uint calc = fCalc != null ? Convert.ToUInt32(fCalc.GetValue(__instance)) : 0;
                        bool term = fTerm != null && Convert.ToBoolean(fTerm.GetValue(__instance));

                        if (term || (qFirst == 0 && calc == 0))
                        {
                            break;
                        }

                        System.Threading.Monitor.Wait(lockObj, 100);
                    }
                }
                finally
                {
                    System.Threading.Monitor.Exit(lockObj);
                }

                return false;
            }
            catch (Exception ex)
            {
                SaveSimulationDataSafetyPatch.LogSaveDebug("WaitForAllPaths_Prefix error: " + ex);
                return false;
            }
        }

        public static bool CustomPathFind_PathFindImplementation_Prefix(uint unit, ref PathUnit data)
        {
            if (unit == 0) return false;

            if (NetManager.instance == null || NetManager.instance.m_segments == null || NetManager.instance.m_segments.m_buffer == null)
                return true;

            NetSegment[] segBuffer = NetManager.instance.m_segments.m_buffer;
            int segCount = segBuffer.Length;

            ushort seg00 = data.m_position00.m_segment;
            ushort seg01 = data.m_position01.m_segment;
            ushort seg02 = data.m_position02.m_segment;
            ushort seg03 = data.m_position03.m_segment;

            if (seg00 >= segCount || seg01 >= segCount || seg02 >= segCount || seg03 >= segCount)
            {
                data.m_pathFindFlags |= PathUnit.FLAG_FAILED;
                return false;
            }

            return true;
        }

        public static void SavedGameOptions_get_Instance_Postfix(ref object __result)
        {
            if (__result == null)
            {
                __result = TMPECompatibility.EnsureAndGetSavedGameOptions();
            }
        }

        public static bool SavedGameOptions_Release_Prefix()
        {
            // Skip Release() to prevent TM:PE from wiping options to null and breaking pathfinding threads
            return false;
        }

        public static void SavedGameOptions_Release_Postfix()
        {
            TMPECompatibility.EnsureAndGetSavedGameOptions();
        }

        public static void GlobalConfig_get_Instance_Postfix(ref object __result)
        {
            if (__result == null)
            {
                __result = TMPECompatibility.EnsureAndGetGlobalConfig();
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

        public static bool VehicleAICommons_CalculateSegmentPosition_Prefix(
            PathUnit.Position position,
            uint laneId,
            ref Vector3 pos,
            ref Vector3 dir,
            ref float maxSpeed)
        {
            // Pure pass-through: TM:PE natively calculates all segment positions.
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
            if (segmentId >= MNCConstants.CORE_NET_CAPACITY)
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
            if (nodeId >= MNCConstants.CORE_NET_CAPACITY)
            {
                __result = false;
                return false;
            }
            return true;
        }

        public static bool JunctionRestrictionsManager_IsEnteringBlockedJunctionAllowed_Prefix(object __instance, ushort segmentId, bool startNode, ref bool __result)
        {
            if (segmentId == 0 || segmentId >= MNCConstants.CORE_NET_CAPACITY)
            {
                __result = false;
                return false;
            }

            try
            {
                NetManager nm = Singleton<NetManager>.instance;
                if (nm != null && nm.m_segments != null && nm.m_segments.m_buffer != null && segmentId < nm.m_segments.m_buffer.Length)
                {
                    ushort nodeId = startNode ? nm.m_segments.m_buffer[segmentId].m_startNode : nm.m_segments.m_buffer[segmentId].m_endNode;
                    if (nodeId != 0 && nm.m_nodes != null && nm.m_nodes.m_buffer != null && nodeId < nm.m_nodes.m_buffer.Length)
                    {
                        // Continuous road corridor (highway, avenue, transition with 2 or fewer segments):
                        // There is NO cross-traffic to block, so entering must ALWAYS be allowed.
                        if (nm.m_nodes.m_buffer[nodeId].CountSegments() <= 2)
                        {
                            __result = true;
                            return false;
                        }
                    }
                }
            }
            catch { }

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
            if (segmentId >= MNCConstants.CORE_NET_CAPACITY)
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
            if (segmentId >= MNCConstants.CORE_NET_CAPACITY)
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
            if (segmentId >= MNCConstants.CORE_NET_CAPACITY)
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

        private static bool s_threadingReflectionInitialized = false;
        private static PropertyInfo s_sgoInstanceProp = null;
        private static FieldInfo s_sgoTimedLightsField = null;
        private static FieldInfo s_tlsmInstanceField = null;
        private static PropertyInfo s_tlsmInstanceProp = null;
        private static MethodInfo s_tlsmSimStepMethod = null;

        public static bool ThreadingExtension_OnBeforeSimulationFrame_Prefix()
        {
            try
            {
                if (!s_threadingReflectionInitialized)
                {
                    Assembly tmpe = TMPECompatibility.GetTMPEAssembly();
                    if (tmpe != null)
                    {
                        Type sgoType = tmpe.GetType("TrafficManager.State.SavedGameOptions");
                        if (sgoType != null)
                        {
                            s_sgoInstanceProp = sgoType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                            s_sgoTimedLightsField = sgoType.GetField("timedLightsEnabled", BindingFlags.Public | BindingFlags.Instance);
                        }
                        Type tlsm = tmpe.GetType("TrafficManager.Manager.Impl.TrafficLightSimulationManager");
                        if (tlsm != null)
                        {
                            s_tlsmInstanceField = tlsm.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                            s_tlsmInstanceProp = tlsm.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                            s_tlsmSimStepMethod = tlsm.GetMethod("SimulationStep", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        }
                        s_threadingReflectionInitialized = true;
                    }
                }

                if (s_sgoInstanceProp != null && s_sgoTimedLightsField != null)
                {
                    object opt = s_sgoInstanceProp.GetValue(null, null);
                    if (opt == null || !(bool)s_sgoTimedLightsField.GetValue(opt))
                    {
                        return false;
                    }
                }

                if (s_tlsmSimStepMethod != null)
                {
                    object inst = s_tlsmInstanceField != null ? s_tlsmInstanceField.GetValue(null) : (s_tlsmInstanceProp != null ? s_tlsmInstanceProp.GetValue(null, null) : null);
                    if (inst != null)
                    {
                        s_tlsmSimStepMethod.Invoke(inst, null);
                    }
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

        public static void RestoreVehicleSpeedIfNeeded(
            ushort vehicleID, 
            ref Vehicle vehicleData, 
            PathUnit.Position position, 
            uint laneID, 
            PathUnit.Position prevPos, 
            ref float maxSpeed)
        {
            // Natural vehicle speed and following distance preserved. 
            // Anti-deadlock is handled via IsEnteringBlockedJunctionAllowed and StuckVehicleRecovery.
        }

        // VehicleBehaviorManager.MayChangeSegment is left 100% native to TM:PE to preserve natural traffic yielding, lane arrows, and turn directions.

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

        public static bool LaneConnectionSubManager_HasNodeConnections_Prefix(object __instance, ushort nodeId, ref bool __result)
        {
            if (__instance == null || nodeId == 0)
            {
                __result = false;
                return false;
            }

            try
            {
                if (NetManager.instance == null || NetManager.instance.m_nodes == null || NetManager.instance.m_nodes.m_buffer == null)
                {
                    __result = false;
                    return false;
                }

                if (nodeId >= NetManager.instance.m_nodes.m_buffer.Length)
                {
                    __result = false;
                    return false;
                }

                if ((NetManager.instance.m_nodes.m_buffer[nodeId].m_flags & NetNode.Flags.Created) == 0)
                {
                    __result = false;
                    return false;
                }
            }
            catch
            {
                __result = false;
                return false;
            }

            return true;
        }

        public static bool LaneConnectionSubManager_HasConnections_Prefix(object __instance, uint laneId, bool startNode, ref bool __result)
        {
            if (__instance == null || laneId == 0)
            {
                __result = false;
                return false;
            }

            try
            {
                if (NetManager.instance != null && NetManager.instance.m_lanes != null && NetManager.instance.m_lanes.m_buffer != null)
                {
                    if (laneId >= NetManager.instance.m_lanes.m_buffer.Length)
                    {
                        __result = false;
                        return false;
                    }
                }
            }
            catch
            {
                __result = false;
                return false;
            }

            return true;
        }

        public static bool LaneConnectionSubManager_AreLanesConnected_Prefix(object __instance, uint sourceLaneId, uint targetLaneId, ushort nodeId, ref bool __result)
        {
            return true;
        }

        public static void InitVehicleBehaviorDelegates(Assembly tmpe, Assembly api)
        {
        }

        public static void VehicleBehaviorManager_MayChangeSegment_Postfix(
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
            // Left 100% native to TM:PE: do NOT override __result or transitState.
            // Overriding MayChangeSegment causes vehicle bezier rollback/rewind oscillation loops (Issue #1).
        }
    }

    // ============================================================================
    // NETMANAGER SIMULATION STEP (HANDLED NATIVELY BY 64K TRANSPILERS)
    // ============================================================================
    public static class NetManagerSimulationPatches
    {
        public static void Postfix(NetManager __instance, int subStep)
        {
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
                if (__instance != null)
                {
                    FieldInfo fPC = __instance.GetType().GetField("$PC", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fPC != null)
                    {
                        int pc = Convert.ToInt32(fPC.GetValue(__instance));
                        if (pc != 0) return; // Only execute on the first MoveNext invocation (before serialization begins)
                    }

                    LastSaveSimulationException = null;
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
                if (__result)
                {
                    LogSaveDebug("=== SaveSimulationData STEP YIELDED (result=true) ===");
                }
                else
                {
                    if (LastSaveSimulationException != null)
                    {
                        LogSaveDebug("=== SaveSimulationData FAILED with exception: " + LastSaveSimulationException.Message + " ===");
                        UnityEngine.Debug.LogError("[MoreNetworkCapacityRenew] === SaveSimulationData FAILED with exception: " + LastSaveSimulationException);
                    }
                    else
                    {
                        LogSaveDebug("=== SaveSimulationData COMPLETED CLEANLY! (result=false) ===");
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] === SaveSimulationData COMPLETED CLEANLY! ===");
                    }
                    CurrentSavingFilename = null;
                }
            }
            catch (Exception ex)
            {
                LogSaveDebug("SaveSimulationData postfix error: " + ex);
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SaveSimulationData postfix log error: " + ex);
            }
        }
    }

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

                bool effectiveError = errorOnSaving;
                if (SaveSimulationDataSafetyPatch.LastSaveSimulationException != null)
                {
                    effectiveError = true;
                    SaveSimulationDataSafetyPatch.LogSaveDebug("SavePanel.OnSavingComplete intercepted save failure: " + SaveSimulationDataSafetyPatch.LastSaveSimulationException);
                    UnityEngine.Debug.LogError("[MoreNetworkCapacityRenew] SavePanel detected failed save simulation:\n" + SaveSimulationDataSafetyPatch.LastSaveSimulationException);
                    try
                    {
                        ColossalFramework.Threading.ThreadHelper.dispatcher.Dispatch(() =>
                        {
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
                        });
                    }
                    catch { }
                }

                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] SavePanel.OnSavingComplete: errorOnSaving={0}, quitAfterSaving={1}", effectiveError, quitAfterSaving));
                if (effectiveError)
                {
                    UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] SavePanel reported errorOnSaving = true. Quitting was cancelled to prevent data loss.");
                    return false;
                }

                // If save completed cleanly, let vanilla OnSavingComplete execute.
                // Vanilla cleanly resets m_IsSaving = false and, if quitAfterSaving is true,
                // safely dispatches QuitApplication() to the Main Thread via ThreadHelper.dispatcher.Dispatch().
                // Calling QuitApplication() directly here on the background TaskWorker thread causes
                // immediate off-thread level unloading and throws NullReferenceException in Unity UI/AdaptiveRoads!
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SavePanelOnSavingComplete error: " + ex);
                return true;
            }
        }
    }



    // ============================================================================
    // MAIN LOADING EXTENSION
    // ============================================================================
    public class RenewLoading : LoadingExtensionBase
    {
        private static bool patchApplied = false;
        private static readonly object s_patchLock = new object();

        public static void EnsureAllPatchesApplied()
        {
            if (patchApplied) return;
            lock (s_patchLock)
            {
                if (patchApplied) return;
                DiagnosticLogger.Checkpoint("EnsureAllPatchesApplied.Start");
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


                // Road & node demolition handling is natively delegated to vanilla game & TM:PE.
                // We do NOT hook NetManager.ReleaseSegmentImplementation so vehicles are repathed naturally.

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
                    // Economy protection (prevents negative maintenance / skyrocketing city budget)
                    EconomyProtection.Apply(harmony);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EconomyProtection error: " + ex);
                }

                try
                {
                    // Message / Chirper protection (prevents ArgumentNullException on null message queue)
                    MessageProtection.Apply(harmony);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] MessageProtection error: " + ex);
                }

                try
                {
                    // Traffic Light Synchronization and Deadlock Prevention (Vanilla & TM:PE Default Lights for nodes 1..65535)
                    TrafficLightPatches.Apply(harmony);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TrafficLightPatches error: " + ex);
                }

                try
                {
                    // Water Transport Compatibility (allows boat/ferry line creation on Ship Paths & connects Ferry/Ship Docks)
                    WaterTransportCompatibility.Apply(harmony);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] WaterTransportCompatibility error: " + ex);
                }

                try
                {
                    // Game Loading Armor (guards against corrupt saves: negative triggerCount, missing radio station, broken TLM vehicles)
                    GameLoadingArmor.Apply(harmony);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] GameLoadingArmor error: " + ex);
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

                patchApplied = true;
                DiagnosticLogger.Checkpoint("EnsureAllPatchesApplied.Complete");

                try { TMPECompatibility.SynchronizeTMPEBuffersAndArrays("EnsureAllPatchesApplied"); } catch {}
                try { ThirdPartyCompatibility.SynchronizeThirdPartyMods("EnsureAllPatchesApplied"); } catch {}
                try { RenewMod.EnsureRenderManagerCapacity(); } catch {}
                try { InstanceManagerCompatibility.EnsureCompatibility(); } catch {}
            }
        }

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
            
            EnsureAllPatchesApplied();
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
                TMPECompatibility.EnsurePathFindCapacity();
                Assembly loadedTmpe = TMPECompatibility.GetTMPEAssembly();
                if (loadedTmpe != null)
                {
                    TMPECompatibility.RecalculateLiveSegments(loadedTmpe);
                }
                MNCTrafficLightStepper.ResetStepper();
                TMPECompatibility.RestorePendingHighIdTMPEData();

                // ToolController collision arrays
                var tm = Singleton<ToolManager>.instance;
                if (tm != null && tm.m_properties != null)
                {
                    var tc = tm.m_properties;
                    int requiredSize = MNCConstants.CORE_BUCKET_CAPACITY; // 65024 / 64 = 1016 (MNC standard, avoids InvalidOperationException)

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
                
                // Network sanitization
                SanitizeNetwork();

                // Vehicle grid sanitization (breaks any circular vehicle loops)
                SanitizeVehicleGrid();

                // Economy sanitization: repair any existing negative expenses/budget corruption
                EconomyProtection.SanitizeEconomy();

                // Transport line sanitization (cycle-check and sanitize vehicle chains)
                TransportLineCompatibility.SanitizeTransportLines();
                
                // Report missing asset stats
                MissingAssetProtection.ReportFinalStats();

                try
                {
                    NetManager nm = Singleton<NetManager>.instance;
                    if (nm != null)
                    {
                        nm.UpdateSegmentColors(0, 0);
                        nm.UpdateNodeColors(0, 0);
                    }
                }
                catch { }

                try
                {
                    WaterTransportCompatibility.SynchronizeWaterTransportNetworks();
                    WaterTransportCompatibility.ReconnectWaterDocks();
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] WaterTransportCompatibility initialization error: " + ex);
                }

                try
                {
                    ThirdPartyCompatibility.Harmonize81Tiles();
                }
                catch { }

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
                // Ensure any ongoing packaging task completes before unloading level
                try
                {
                    SavePanel sp = UnityEngine.Object.FindObjectOfType<SavePanel>();
                    if (sp != null)
                    {
                        FieldInfo fTask = typeof(SavePanel).GetField("m_PackageSaveTask", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (fTask != null)
                        {
                            var task = fTask.GetValue(sp) as ColossalFramework.Threading.Task;
                            if (task != null && !task.hasEnded)
                            {
                                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Waiting for SavePanel.m_PackageSaveTask to finish packaging before level unload...");
                                task.Wait();
                                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SavePanel.m_PackageSaveTask packaging completed.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Guard SavePanel packaging task error: " + ex);
                }

                // Stop monitoring systems
                ArrayGuardianV2.StopMonitoring();

                // Level Unload State Sanitizer (NgocDuy):
                // Clear pending TM:PE serialized buffers, save diagnostics, and force garbage collection to prevent blue screen on reload
                try
                {
                    TMPECompatibility.ClearPendingTMPEData();
                    DiagnosticLogger.Reset();

                    MemoryOptimizer.StrategicGarbageCollect("OnLevelUnloading");
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Level Unload Sanitizer: Freed unreferenced heaps & reset serialization state ✓");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] Level Unload Sanitizer warning: " + ex.Message);
                }
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
    // TM:PE & FULL-MAP NETWORK STEPPER (ZERO HARMONY HOOKS)
    // ============================================================================
    public class MNCTrafficLightStepper : ThreadingExtensionBase
    {
        private delegate void StepDelegate(Array array, int index);
        private static StepDelegate s_stepDelegate;
        private static Array s_simulationsArray;

        private delegate bool IsRunningDelegate(Array array, int index);
        private static IsRunningDelegate s_isRunningDelegate;

        public static bool IsTMPETimedLightRunning(ushort nodeId)
        {
            if (s_isRunningDelegate != null && s_simulationsArray != null && nodeId < s_simulationsArray.Length)
            {
                try
                {
                    return s_isRunningDelegate(s_simulationsArray, (int)nodeId);
                }
                catch { }
            }
            return false;
        }

        private delegate void TMMStepDelegate(object inst, ushort segId, ref NetSegment seg);
        private static TMMStepDelegate s_tmmStepDelegate;
        private static object s_tmmInstance;

        private static bool s_initialized;
        private static bool s_impatientCommutersChecked;
        private static bool s_tmpeVRMChecked;

        public static void ResetStepper()
        {
            s_simulationsArray = null;
            s_stepDelegate = null;
            s_isRunningDelegate = null;
            s_tmmStepDelegate = null;
            s_tmmInstance = null;
            s_initialized = false;
            s_impatientCommutersChecked = false;
            s_tmpeVRMChecked = false;
        }

        public static void Initialize()
        {
            try
            {
                Assembly tmpe = TMPECompatibility.GetTMPEAssembly();
                if (tmpe != null)
                {
                    TMPECompatibility.EnsureVehicleRestrictionsCapacity(tmpe);
                    TMPECompatibility.EnsureCustomSegmentLightsCapacity(tmpe);
                    TMPECompatibility.EnsureTrafficPriorityCapacity(tmpe);
                    TMPECompatibility.EnsureJunctionRestrictionsCapacity(tmpe);
                    TMPECompatibility.EnsureParkingRestrictionsCapacity(MNCConstants.CORE_NET_CAPACITY);

                    Type tlsmType = tmpe.GetType("TrafficManager.Manager.Impl.TrafficLightSimulationManager");
                    if (tlsmType != null)
                    {
                        object inst = TMPECompatibility.GetManagerInstance(tlsmType);
                        if (inst != null)
                        {
                            FieldInfo f = tlsmType.GetField("<TrafficLightSimulations>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                       ?? tlsmType.GetField("TrafficLightSimulations", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (f != null)
                            {
                                s_simulationsArray = f.GetValue(inst) as Array;
                            }
                        }
                    }

                    Type tlsType = tmpe.GetType("TrafficManager.TrafficLight.Impl.TrafficLightSimulation");
                    if (tlsType != null)
                    {
                        MethodInfo stepMethod = tlsType.GetMethod("SimulationStep", BindingFlags.Public | BindingFlags.Instance);
                        if (stepMethod != null)
                        {
                            DynamicMethod dm = new DynamicMethod(
                                "MNC_StepTLS",
                                typeof(void),
                                new Type[] { typeof(Array), typeof(int) },
                                typeof(MNCTrafficLightStepper).Module,
                                true);

                            ILGenerator il = dm.GetILGenerator();
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Castclass, tlsType.MakeArrayType());
                            il.Emit(OpCodes.Ldarg_1);
                            il.Emit(OpCodes.Ldelema, tlsType);
                            il.Emit(OpCodes.Call, stepMethod);
                            il.Emit(OpCodes.Ret);

                            s_stepDelegate = (StepDelegate)dm.CreateDelegate(typeof(StepDelegate));
                        }

                        MethodInfo isRunningMethod = tlsType.GetMethod("IsSimulationRunning", BindingFlags.Public | BindingFlags.Instance);
                        if (isRunningMethod != null)
                        {
                            DynamicMethod dm2 = new DynamicMethod(
                                "MNC_IsSimulationRunning",
                                typeof(bool),
                                new Type[] { typeof(Array), typeof(int) },
                                typeof(MNCTrafficLightStepper).Module,
                                true);

                            ILGenerator il2 = dm2.GetILGenerator();
                            il2.Emit(OpCodes.Ldarg_0);
                            il2.Emit(OpCodes.Castclass, tlsType.MakeArrayType());
                            il2.Emit(OpCodes.Ldarg_1);
                            il2.Emit(OpCodes.Ldelema, tlsType);
                            il2.Emit(OpCodes.Call, isRunningMethod);
                            il2.Emit(OpCodes.Ret);

                            s_isRunningDelegate = (IsRunningDelegate)dm2.CreateDelegate(typeof(IsRunningDelegate));
                        }
                    }

                    Type tmmType = tmpe.GetType("TrafficManager.Manager.Impl.TrafficMeasurementManager");
                    if (tmmType != null)
                    {
                        MethodInfo tmmMethod = tmmType.GetMethod("OnBeforeSimulationStep", BindingFlags.Public | BindingFlags.Instance);
                        s_tmmInstance = TMPECompatibility.GetManagerInstance(tmmType);
                        if (tmmMethod != null && s_tmmInstance != null)
                        {
                            DynamicMethod dm = new DynamicMethod(
                                "MNC_StepTMM",
                                typeof(void),
                                new Type[] { typeof(object), typeof(ushort), typeof(NetSegment).MakeByRefType() },
                                typeof(MNCTrafficLightStepper).Module,
                                true);

                            ILGenerator il = dm.GetILGenerator();
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Castclass, tmmType);
                            il.Emit(OpCodes.Ldarg_1);
                            il.Emit(OpCodes.Ldarg_2);
                            il.Emit(OpCodes.Callvirt, tmmMethod);
                            il.Emit(OpCodes.Ret);

                            s_tmmStepDelegate = (TMMStepDelegate)dm.CreateDelegate(typeof(TMMStepDelegate));
                        }
                    }
                }

                s_initialized = true;
                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] MNCTrafficLightStepper initialized successfully (TLS array: {0}, TMM: {1})",
                    s_simulationsArray != null ? s_simulationsArray.Length : 0, s_tmmStepDelegate != null));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] MNCTrafficLightStepper.Initialize error: " + ex);
            }
        }

        public override void OnCreated(IThreading threading)
        {
            base.OnCreated(threading);
            ResetStepper();
        }

        public override void OnReleased()
        {
            base.OnReleased();
            ResetStepper();
        }

        public override void OnUpdate(float realTimeDelta, float simulationTimeDelta)
        {
            base.OnUpdate(realTimeDelta, simulationTimeDelta);
            try
            {
                TMPECompatibility.SynchronizeActiveTool();
            }
            catch { }
        }

        public override void OnBeforeSimulationTick()
        {
            base.OnBeforeSimulationTick();
            if (!s_impatientCommutersChecked)
            {
                ThirdPartyCompatibility.EnsureImpatientCommutersBuffers("MNCTrafficLightStepper.OnBeforeSimulationTick");
                s_impatientCommutersChecked = true;
            }
            if (!s_tmpeVRMChecked)
            {
                Assembly tmpe = TMPECompatibility.GetTMPEAssembly();
                if (tmpe != null)
                {
                    TMPECompatibility.EnsureVehicleRestrictionsCapacity(tmpe);
                }
                s_tmpeVRMChecked = true;
            }
        }

        public override void OnAfterSimulationFrame()
        {
            base.OnAfterSimulationFrame();

            if (!s_initialized)
            {
                Initialize();
            }

            var sm = Singleton<SimulationManager>.instance;
            if (sm == null) return;

            var nm = Singleton<NetManager>.instance;
            if (nm == null) return;

            NetNode[] nodeBuffer = nm.m_nodes != null ? nm.m_nodes.m_buffer : null;
            NetSegment[] segBuffer = nm.m_segments != null ? nm.m_segments.m_buffer : null;

            uint frame256 = sm.m_currentFrameIndex & 255;
            uint frame64 = sm.m_currentFrameIndex & 63;

            // 1. Vanilla NetAI Upper Node Stepping (32,768 .. 65,535) across 256 frames (128 nodes per frame)
            // Matches vanilla NetManager 256-frame cadence: cycles vanilla traffic lights and pedestrian signals at normal speed.
            if (nodeBuffer != null)
            {
                int nStart256 = 32768 + (int)frame256 * 128;
                int nEnd256 = Math.Min(nStart256 + 128, nodeBuffer.Length);

                for (int i = nStart256; i < nEnd256; i++)
                {
                    if ((nodeBuffer[i].m_flags & NetNode.Flags.Created) == 0)
                        continue;

                    try
                    {
                        NetInfo info = nodeBuffer[i].Info;
                        if (info != null && info.m_netAI != null)
                        {
                            info.m_netAI.SimulationStep((ushort)i, ref nodeBuffer[i]);
                        }
                    }
                    catch { }
                }

                try
                {
                    var im = Singleton<InfoManager>.instance;
                    if (im != null && im.CurrentMode != InfoManager.InfoMode.None)
                    {
                        nm.UpdateNodeColors((ushort)nStart256, (ushort)(nEnd256 - 1));
                    }
                }
                catch { }
            }

            // 2. TM:PE Timed Traffic Lights Stepping (32,768 .. 65,535) across 64 frames (512 nodes per frame)
            // Matches TM:PE's native 64-frame simulation cadence.
            if (s_stepDelegate != null && s_simulationsArray != null)
            {
                int ttlStart = 32768 + (int)frame64 * 512;
                int ttlEnd = Math.Min(ttlStart + 512, s_simulationsArray.Length);

                for (int i = ttlStart; i < ttlEnd; i++)
                {
                    try
                    {
                        s_stepDelegate(s_simulationsArray, i);
                    }
                    catch { }
                }
            }

            // 3. Upper Segment Stepping (36,864 .. 65,535) across 256 frames (112 segments per frame)
            // Matches vanilla NetManager 256-frame cadence: m_trafficBuffer accumulates vehicles for the full 256 frames,
            // correctly reporting accurate congestion density (up to 100% bright red) and updating noise/wear.
            if (segBuffer != null)
            {
                int sStart = 36864 + (int)frame256 * 112;
                int sEnd = Math.Min(sStart + 112, segBuffer.Length);

                for (int s = sStart; s < sEnd; s++)
                {
                    if ((segBuffer[s].m_flags & NetSegment.Flags.Created) == 0)
                        continue;

                    try
                    {
                        NetInfo info = segBuffer[s].Info;
                        if (info != null && info.m_netAI != null)
                        {
                            // TM:PE Traffic Measurement if available
                            if (s_tmmStepDelegate != null && s_tmmInstance != null)
                            {
                                try { s_tmmStepDelegate(s_tmmInstance, (ushort)s, ref segBuffer[s]); } catch { }
                            }

                            // NetAI SimulationStep: converts m_trafficBuffer -> m_trafficDensity, resets m_trafficBuffer, updates noise & wear
                            info.m_netAI.SimulationStep((ushort)s, ref segBuffer[s]);
                        }
                    }
                    catch { }
                }

                try
                {
                    var im = Singleton<InfoManager>.instance;
                    if (im != null && im.CurrentMode != InfoManager.InfoMode.None)
                    {
                        nm.UpdateSegmentColors((ushort)sStart, (ushort)(sEnd - 1));
                    }
                }
                catch { }
            }

            // 4. Color Map Synchronization for Upper Network
            // Every 64 frames (approx once per sec), refresh upper colors if an Info Mode is active
            if (frame64 == 0)
            {
                try
                {
                    var im = Singleton<InfoManager>.instance;
                    if (im != null && im.CurrentMode != InfoManager.InfoMode.None)
                    {
                        nm.UpdateSegmentColors(36864, (ushort)Math.Min(65535, segBuffer.Length - 1));
                        nm.UpdateNodeColors(32768, (ushort)Math.Min(65535, nodeBuffer.Length - 1));
                    }
                }
                catch { }
            }
        }
    }

    // ============================================================================
    // UNIVERSAL TRAFFIC LIGHT HARMONY PATCHES (NODES 1..65,535 & ZERO TM:PE HOOKS)
    // Synchronizes dual banks (eliminating bank inversion desync on high nodes)
    // Ensures robust, deadlock-free cycling for vanilla and TM:PE default traffic lights
    // ============================================================================
    public static class TrafficLightPatches
    {
        public static void Apply(HarmonyInstance harmony)
        {
            if (harmony == null) return;
            try
            {
                // 1. RoadBaseAI.SetTrafficLightState: dual-bank synchronization
                MethodInfo setM = typeof(RoadBaseAI).GetMethod(
                    "SetTrafficLightState",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] {
                        typeof(ushort),
                        typeof(NetSegment).MakeByRefType(),
                        typeof(uint),
                        typeof(RoadBaseAI.TrafficLightState),
                        typeof(RoadBaseAI.TrafficLightState),
                        typeof(bool),
                        typeof(bool)
                    },
                    null);
                if (setM != null)
                {
                    MethodInfo pre = typeof(TrafficLightPatches).GetMethod(nameof(SetTrafficLightState_Prefix), BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setM, prefix: new HarmonyMethod(pre));
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched RoadBaseAI.SetTrafficLightState (dual-bank synchronization)");
                }

                // 2. RoadBaseAI.GetTrafficLightState (5-param overload)
                MethodInfo get5M = typeof(RoadBaseAI).GetMethod(
                    "GetTrafficLightState",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] {
                        typeof(ushort),
                        typeof(NetSegment).MakeByRefType(),
                        typeof(uint),
                        typeof(RoadBaseAI.TrafficLightState).MakeByRefType(),
                        typeof(RoadBaseAI.TrafficLightState).MakeByRefType()
                    },
                    null);
                if (get5M != null)
                {
                    MethodInfo pre = typeof(TrafficLightPatches).GetMethod(nameof(GetTrafficLightState5_Prefix), BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(get5M, prefix: new HarmonyMethod(pre));
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched RoadBaseAI.GetTrafficLightState (5-param overload)");
                }

                // 3. RoadBaseAI.GetTrafficLightState (7-param overload)
                MethodInfo get7M = typeof(RoadBaseAI).GetMethod(
                    "GetTrafficLightState",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] {
                        typeof(ushort),
                        typeof(NetSegment).MakeByRefType(),
                        typeof(uint),
                        typeof(RoadBaseAI.TrafficLightState).MakeByRefType(),
                        typeof(RoadBaseAI.TrafficLightState).MakeByRefType(),
                        typeof(bool).MakeByRefType(),
                        typeof(bool).MakeByRefType()
                    },
                    null);
                if (get7M != null)
                {
                    MethodInfo pre = typeof(TrafficLightPatches).GetMethod(nameof(GetTrafficLightState7_Prefix), BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(get7M, prefix: new HarmonyMethod(pre));
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched RoadBaseAI.GetTrafficLightState (7-param overload)");
                }

                // 4. RoadBaseAI.TrafficLightSimulationStep: robust cycling logic for default lights
                MethodInfo stepM = typeof(RoadBaseAI).GetMethod(
                    "TrafficLightSimulationStep",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] {
                        typeof(ushort),
                        typeof(NetNode).MakeByRefType()
                    },
                    null);
                if (stepM != null)
                {
                    MethodInfo pre = typeof(TrafficLightPatches).GetMethod(nameof(TrafficLightSimulationStep_Prefix), BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(stepM, prefix: new HarmonyMethod(pre));
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched RoadBaseAI.TrafficLightSimulationStep (robust deadlock-free cycling)");
                }

                // 5. RoadBaseAI.GetTrafficLightNodeState: clean visual rendering on all nodes
                MethodInfo nodeStateM = typeof(RoadBaseAI).GetMethod(
                    "GetTrafficLightNodeState",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] {
                        typeof(ushort),
                        typeof(NetNode).MakeByRefType(),
                        typeof(ushort),
                        typeof(NetSegment).MakeByRefType(),
                        typeof(NetNode.FlagsLong).MakeByRefType(),
                        typeof(Color).MakeByRefType()
                    },
                    null);
                if (nodeStateM != null)
                {
                    MethodInfo pre = typeof(TrafficLightPatches).GetMethod(nameof(GetTrafficLightNodeState_Prefix), BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(nodeStateM, prefix: new HarmonyMethod(pre));
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched RoadBaseAI.GetTrafficLightNodeState (cadence visual alignment)");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TrafficLightPatches.Apply error: " + ex);
            }
        }

        public static bool SetTrafficLightState_Prefix(ushort nodeID, ref NetSegment segmentData, uint frame, RoadBaseAI.TrafficLightState vehicleLightState, RoadBaseAI.TrafficLightState pedestrianLightState, bool vehicles, bool pedestrians)
        {
            int encoded = ((int)pedestrianLightState << 2) | (int)vehicleLightState;
            if (segmentData.m_startNode == nodeID)
            {
                segmentData.m_trafficLightState0 = (segmentData.m_trafficLightState0 & ~15) | encoded;
                segmentData.m_trafficLightState1 = (segmentData.m_trafficLightState1 & ~15) | encoded;
                if (vehicles) segmentData.m_flags |= NetSegment.Flags.TrafficStart;
                else segmentData.m_flags &= ~NetSegment.Flags.TrafficStart;
                if (pedestrians) segmentData.m_flags |= NetSegment.Flags.CrossingStart;
                else segmentData.m_flags &= ~NetSegment.Flags.CrossingStart;
            }
            else
            {
                segmentData.m_trafficLightState0 = (segmentData.m_trafficLightState0 & ~240) | (encoded << 4);
                segmentData.m_trafficLightState1 = (segmentData.m_trafficLightState1 & ~240) | (encoded << 4);
                if (vehicles) segmentData.m_flags |= NetSegment.Flags.TrafficEnd;
                else segmentData.m_flags &= ~NetSegment.Flags.TrafficEnd;
                if (pedestrians) segmentData.m_flags |= NetSegment.Flags.CrossingEnd;
                else segmentData.m_flags &= ~NetSegment.Flags.CrossingEnd;
            }
            return false;
        }

        public static bool GetTrafficLightState5_Prefix(ushort nodeID, ref NetSegment segmentData, uint frame, out RoadBaseAI.TrafficLightState vehicleLightState, out RoadBaseAI.TrafficLightState pedestrianLightState)
        {
            int state = ((frame >> 8) & 1) == 0 ? segmentData.m_trafficLightState0 : segmentData.m_trafficLightState1;
            if (segmentData.m_startNode == nodeID)
            {
                state &= 15;
            }
            else
            {
                state = (state >> 4) & 15;
            }
            vehicleLightState = (RoadBaseAI.TrafficLightState)(state & 3);
            pedestrianLightState = (RoadBaseAI.TrafficLightState)(state >> 2);
            return false;
        }

        public static bool GetTrafficLightState7_Prefix(ushort nodeID, ref NetSegment segmentData, uint frame, out RoadBaseAI.TrafficLightState vehicleLightState, out RoadBaseAI.TrafficLightState pedestrianLightState, out bool vehicles, out bool pedestrians)
        {
            int state = ((frame >> 8) & 1) == 0 ? segmentData.m_trafficLightState0 : segmentData.m_trafficLightState1;
            if (segmentData.m_startNode == nodeID)
            {
                state &= 15;
                vehicles = (segmentData.m_flags & NetSegment.Flags.TrafficStart) != 0;
                pedestrians = (segmentData.m_flags & NetSegment.Flags.CrossingStart) != 0;
            }
            else
            {
                state = (state >> 4) & 15;
                vehicles = (segmentData.m_flags & NetSegment.Flags.TrafficEnd) != 0;
                pedestrians = (segmentData.m_flags & NetSegment.Flags.CrossingEnd) != 0;
            }
            vehicleLightState = (RoadBaseAI.TrafficLightState)(state & 3);
            pedestrianLightState = (RoadBaseAI.TrafficLightState)(state >> 2);
            return false;
        }

        public static bool TrafficLightSimulationStep_Prefix(ushort nodeID, ref NetNode data)
        {
            // If TM:PE timed light is running, let TM:PE's prefix and simulation handle it natively
            if (MNCTrafficLightStepper.IsTMPETimedLightRunning(nodeID))
            {
                return true;
            }

            NetManager nm = Singleton<NetManager>.instance;
            SimulationManager sm = Singleton<SimulationManager>.instance;
            if (nm == null || sm == null) return true;

            uint currentFrameIndex = sm.m_currentFrameIndex;
            int clock = data.m_maxWaitTime & 3;
            int step = (data.m_maxWaitTime >> 2) & 7;
            int pedStep = data.m_maxWaitTime >> 5;

            int currentGreenWithVehicles = -1;
            int currentGreen = -1;
            int firstRedWithVehicles = -1;
            int firstPedWithPedestrians = -1;
            int selectedRed = -1;
            int selectedPed = -1;

            int incomingMask = 0;
            int outgoingMask = 0;
            int redCount = 0;
            int incomingCount = 0;
            int pedCount = 0;

            int firstIncomingRed = -1;
            int nextIncomingRed = -1;

            for (int i = 0; i < 8; i++)
            {
                ushort segId = data.GetSegment(i);
                if (segId == 0) continue;

                int forwardLanes = 0;
                int backwardLanes = 0;
                nm.m_segments.m_buffer[segId].CountLanes(segId, NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle, VehicleInfo.VehicleType.Car | VehicleInfo.VehicleType.Tram, VehicleInfo.VehicleCategory.All, ref forwardLanes, ref backwardLanes);

                bool isStartNode = (nm.m_segments.m_buffer[segId].m_startNode == nodeID);
                bool canEnter = isStartNode ? (backwardLanes > 0) : (forwardLanes > 0);
                bool canLeave = isStartNode ? (forwardLanes > 0) : (backwardLanes > 0);

                if (canEnter) incomingMask |= (1 << i);
                if (canLeave)
                {
                    outgoingMask |= (1 << i);
                    incomingCount++;
                }

                RoadBaseAI.TrafficLightState vehState;
                RoadBaseAI.TrafficLightState pedState;
                bool vehicles;
                bool pedestrians;
                RoadBaseAI.GetTrafficLightState(nodeID, ref nm.m_segments.m_buffer[segId], currentFrameIndex - 256u, out vehState, out pedState, out vehicles, out pedestrians);

                if ((pedState & RoadBaseAI.TrafficLightState.Red) != 0)
                {
                    if (pedestrians)
                    {
                        if (firstPedWithPedestrians == -1) firstPedWithPedestrians = i;
                        if (selectedPed == -1 && pedCount >= pedStep) selectedPed = i;
                    }
                    pedCount++;
                }

                if (canEnter)
                {
                    if ((vehState & RoadBaseAI.TrafficLightState.Red) == 0)
                    {
                        currentGreen = i;
                        if (vehicles) currentGreenWithVehicles = i;
                    }
                    else
                    {
                        if (firstIncomingRed == -1) firstIncomingRed = i;
                        if (nextIncomingRed == -1 && redCount >= step) nextIncomingRed = i;

                        if (vehicles)
                        {
                            if (firstRedWithVehicles == -1) firstRedWithVehicles = i;
                            if (selectedRed == -1 && redCount >= step) selectedRed = i;
                        }
                        redCount++;
                    }
                }
            }

            if (selectedRed == -1) selectedRed = firstRedWithVehicles;
            if (selectedPed == -1) selectedPed = firstPedWithPedestrians;
            if (nextIncomingRed == -1) nextIncomingRed = firstIncomingRed;

            // Robust cycling decision:
            bool changePhase = false;
            if (currentGreen != -1)
            {
                if (selectedRed != -1)
                {
                    // Demand detected on red: give active green vehicles at least 2 steps (clock <= 1) before switching
                    if (currentGreenWithVehicles != -1 && clock <= 1)
                    {
                        clock++;
                    }
                    else
                    {
                        changePhase = true;
                    }
                }
                else if (clock >= 3)
                {
                    // Automatic cycle timeout (~16-20s): force cycle to next incoming red phase
                    if (nextIncomingRed != -1)
                    {
                        selectedRed = nextIncomingRed;
                        changePhase = true;
                    }
                    else
                    {
                        clock = 0;
                    }
                }
                else
                {
                    clock++;
                }
            }
            else
            {
                // No green currently (startup / all red) -> assign green to first available red
                if (selectedRed == -1) selectedRed = nextIncomingRed;
                changePhase = true;
            }

            if (!changePhase)
            {
                selectedRed = -1;
                selectedPed = -1;
            }

            if (selectedPed != -1 && selectedRed != -1 && (sm.m_randomizer.Int32(3) == 0))
            {
                selectedPed = -1;
            }

            if (selectedRed != -1)
            {
                currentGreen = selectedRed;
            }

            if (selectedPed == currentGreen)
            {
                selectedPed = -1;
            }

            Vector3 currentGreenDir = Vector3.zero;
            if (selectedPed != -1)
            {
                ushort pedSeg = data.GetSegment(selectedPed);
                if (pedSeg != 0)
                {
                    currentGreenDir = nm.m_segments.m_buffer[pedSeg].GetDirection(nodeID);
                    if (currentGreen != -1)
                    {
                        ushort greenSeg = data.GetSegment(currentGreen);
                        if (greenSeg != 0)
                        {
                            Vector3 greenDir = nm.m_segments.m_buffer[greenSeg].GetDirection(nodeID);
                            if (greenDir.x * currentGreenDir.x + greenDir.z * currentGreenDir.z < -0.5f)
                            {
                                currentGreen = -1;
                            }
                        }
                    }
                    if (currentGreen == -1)
                    {
                        for (int j = 0; j < 8; j++)
                        {
                            if (j == selectedPed) continue;
                            if ((incomingMask & (1 << j)) == 0) continue;
                            ushort segId = data.GetSegment(j);
                            if (segId == 0) continue;
                            Vector3 dir = nm.m_segments.m_buffer[segId].GetDirection(nodeID);
                            if (dir.x * currentGreenDir.x + dir.z * currentGreenDir.z >= -0.5f)
                            {
                                currentGreen = j;
                                break;
                            }
                        }
                    }
                }
            }

            int oppositeGreen = -1;
            Vector3 dirGreen = Vector3.zero;
            Vector3 dirOpposite = Vector3.zero;

            if (currentGreen != -1)
            {
                ushort segGreen = data.GetSegment(currentGreen);
                if (segGreen != 0)
                {
                    dirGreen = nm.m_segments.m_buffer[segGreen].GetDirection(nodeID);

                    if ((incomingMask & outgoingMask & (1 << currentGreen)) != 0)
                    {
                        for (int k = 0; k < 8; k++)
                        {
                            if (k == currentGreen || k == selectedPed) continue;
                            if ((incomingMask & outgoingMask & (1 << k)) == 0) continue;

                            ushort segK = data.GetSegment(k);
                            if (segK == 0) continue;

                            dirOpposite = nm.m_segments.m_buffer[segK].GetDirection(nodeID);
                            if (selectedPed != -1 && (dirOpposite.x * currentGreenDir.x + dirOpposite.z * currentGreenDir.z < -0.5f))
                            {
                                continue;
                            }
                            if (incomingCount == 2)
                            {
                                oppositeGreen = k;
                                break;
                            }
                            if (dirOpposite.x * dirGreen.x + dirOpposite.z * dirGreen.z < -0.9396926f)
                            {
                                oppositeGreen = k;
                                break;
                            }
                        }
                    }
                }
            }

            bool[] bollardsArray = new bool[8];

            for (int s = 0; s < 8; s++)
            {
                ushort segId = data.GetSegment(s);
                if (segId == 0) continue;

                RoadBaseAI.TrafficLightState vehState;
                RoadBaseAI.TrafficLightState pedState;
                bool dummyPed;
                RoadBaseAI.GetTrafficLightState(nodeID, ref nm.m_segments.m_buffer[segId], currentFrameIndex - 256u, out vehState, out pedState, out bollardsArray[s], out dummyPed);

                vehState = (RoadBaseAI.TrafficLightState)((int)vehState & ~1);
                pedState = (RoadBaseAI.TrafficLightState)((int)pedState & ~1);

                if (s == currentGreen || s == oppositeGreen)
                {
                    if ((vehState & RoadBaseAI.TrafficLightState.Red) != 0)
                    {
                        vehState = RoadBaseAI.TrafficLightState.RedToGreen;
                        clock = 0;
                        step++;
                        if (step >= redCount) step = 0;
                    }

                    if ((pedState & RoadBaseAI.TrafficLightState.Red) == 0)
                    {
                        pedState = RoadBaseAI.TrafficLightState.GreenToRed;
                    }
                }
                else
                {
                    if ((vehState & RoadBaseAI.TrafficLightState.Red) == 0)
                    {
                        vehState = RoadBaseAI.TrafficLightState.GreenToRed;
                    }

                    Vector3 segDir = nm.m_segments.m_buffer[segId].GetDirection(nodeID);
                    bool pedCrossingAllowed = false;

                    if ((outgoingMask & (1 << s)) != 0 && s != selectedPed)
                    {
                        if (currentGreen == -1 || (segDir.x * dirGreen.x + segDir.z * dirGreen.z >= -0.5f))
                        {
                            if (oppositeGreen == -1 || (segDir.x * dirOpposite.x + dirOpposite.z * dirOpposite.z >= -0.5f))
                            {
                                pedCrossingAllowed = true;
                            }
                        }
                    }

                    if (pedCrossingAllowed)
                    {
                        if ((pedState & RoadBaseAI.TrafficLightState.Red) != 0)
                        {
                            pedState = RoadBaseAI.TrafficLightState.RedToGreen;
                            pedStep++;
                            if (pedStep >= pedCount) pedStep = 0;
                        }
                    }
                    else
                    {
                        if ((pedState & RoadBaseAI.TrafficLightState.Red) == 0)
                        {
                            pedState = RoadBaseAI.TrafficLightState.GreenToRed;
                        }
                    }
                }

                RoadBaseAI.SetTrafficLightState(nodeID, ref nm.m_segments.m_buffer[segId], currentFrameIndex, vehState, pedState, false, false);
            }

            if ((data.flags & NetNode.FlagsLong.PedestrianBollards) != 0)
            {
                PedestrianZoneRoadAI.BollardsSimulationStep(nodeID, ref data, bollardsArray);
            }

            data.m_maxWaitTime = (byte)((pedStep << 5) | (step << 2) | (clock & 3));
            return false;
        }

        public static bool GetTrafficLightNodeState_Prefix(ushort nodeID, ref NetNode nodeData, ushort segmentID, ref NetSegment segmentData, ref NetNode.FlagsLong flags, ref Color color)
        {
            if (MNCTrafficLightStepper.IsTMPETimedLightRunning(nodeID))
            {
                return true;
            }

            uint simFrame = Singleton<SimulationManager>.instance.m_referenceFrameIndex - 15u;
            uint frameOffset = (uint)(nodeID >> 7) & 255u;
            uint subFrame = (simFrame - frameOffset) & 255u;

            RoadBaseAI.TrafficLightState vehState;
            RoadBaseAI.TrafficLightState pedState;
            RoadBaseAI.GetTrafficLightState(nodeID, ref segmentData, simFrame - frameOffset, out vehState, out pedState);

            color.a = 0.5f;
            switch (vehState)
            {
                case RoadBaseAI.TrafficLightState.Green:
                    color.r = 0f;
                    color.g = 1f;
                    break;
                case RoadBaseAI.TrafficLightState.RedToGreen:
                    if (subFrame < 45u)
                    {
                        color.r = 0f;
                        color.g = 0f;
                    }
                    else if (subFrame < 60u)
                    {
                        color.r = 1f;
                        color.g = 0f;
                    }
                    else
                    {
                        color.r = 1f;
                        color.g = 1f;
                    }
                    break;
                case RoadBaseAI.TrafficLightState.Red:
                    color.r = 0f;
                    color.g = 0f;
                    break;
                case RoadBaseAI.TrafficLightState.GreenToRed:
                    if (subFrame < 45u)
                    {
                        color.r = 1f;
                        color.g = 0f;
                    }
                    else
                    {
                        color.r = 0f;
                        color.g = 0f;
                    }
                    break;
            }

            switch (pedState)
            {
                case RoadBaseAI.TrafficLightState.Green:
                    color.b = 1f;
                    break;
                case RoadBaseAI.TrafficLightState.RedToGreen:
                    if (subFrame < 45u)
                    {
                        color.b = 0f;
                    }
                    else
                    {
                        color.b = 1f;
                    }
                    break;
                case RoadBaseAI.TrafficLightState.Red:
                    color.b = 0f;
                    break;
                case RoadBaseAI.TrafficLightState.GreenToRed:
                    if (subFrame < 45u)
                    {
                        color.b = ((subFrame / 8u) & 1u) == 1u ? 1f : 0f;
                    }
                    else
                    {
                        color.b = 0f;
                    }
                    break;
            }

            return false;
        }
    }

    // ============================================================================
    // MNC SERIALIZABLE DATA EXTENSION (ZERO HARMONY HOOKS)
    // Saves & Restores High-ID TM:PE Junction Restrictions, Priority Signs & Timed Lights
    // ============================================================================
    public class MNCSerializableData : SerializableDataExtensionBase
    {
        public const string DATA_KEY = "MoreNetworkCapacity_HighID_TMPE_v1";

        public override void OnSaveData()
        {
            base.OnSaveData();
            if (serializableDataManager == null) return;
            try
            {
                byte[] bytes = TMPECompatibility.SerializeHighIdTMPEData();
                if (bytes != null && bytes.Length > 0)
                {
                    serializableDataManager.SaveData(DATA_KEY, bytes);
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Successfully saved {0} bytes of High-ID TM:PE data (JunctionRestrictions, PrioritySigns, TimedLights).", bytes.Length));
                }
                else
                {
                    serializableDataManager.EraseData(DATA_KEY);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] MNCSerializableData.OnSaveData error: " + ex);
            }
        }

        public override void OnLoadData()
        {
            base.OnLoadData();
            if (serializableDataManager == null) return;
            try
            {
                byte[] bytes = serializableDataManager.LoadData(DATA_KEY);
                TMPECompatibility.SetPendingHighIdTMPEData(bytes);
                if (bytes != null && bytes.Length > 0)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Loaded {0} bytes of High-ID TM:PE data from savegame.", bytes.Length));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] MNCSerializableData.OnLoadData error: " + ex);
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
                    instruction.operand = MNCConstants.CORE_NET_CAPACITY;
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
                    instruction.operand = MNCConstants.CORE_NET_CAPACITY;
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
                        "MoreTransportLines",
                        "TransferManagerCE",
                        "TransferManagerExtended",
                        "RebalancedIndustriesRevisited"
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

            if (type.Name.Contains("PathFind") || type.Name.Contains("CustomPathFind") || (type.FullName != null && type.FullName.StartsWith("TrafficManager.Custom.PathFinding.")))
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
                            if (val == 32768 || val == 36864) expectedVal = MNCConstants.CORE_NET_CAPACITY;
                            else if (val == 32767 || val == 36863) expectedVal = MNCConstants.CORE_NET_CAPACITY - 1;
                            else if (val == 73728) expectedVal = MNCConstants.CORE_SPLIT_CAPACITY;
                            else if (val == 73727) expectedVal = MNCConstants.CORE_SPLIT_CAPACITY - 1;

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
        private static bool _morePathUnitsPatched = false;
        private static bool _transferManagerPatched = false;
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
                                else if (!UniversalModHarmonizer.IsIgnoredAssembly(name))
                                {
                                    UniversalModHarmonizer.HarmonizeAssembly(args.LoadedAssembly, "AssemblyLoad: " + name);
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
                   name == "EightyOne2" ||
                   name == "TransferManagerCE" ||
                   name == "TransferManagerExtended" ||
                   name == "RebalancedIndustriesRevisited" ||
                   name == "MorePathUnits" ||
                   name == "ImpatientCommuters";
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
                EnsureTransferManagerBuffers(context);
                EnsureImpatientCommutersBuffers(context);
                SynchronizeMorePathUnits(context);
                Harmonize81Tiles();
                UniversalModHarmonizer.HarmonizeActiveMods(context);
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
                int req = MNCConstants.CORE_BUCKET_CAPACITY; // 65024 / 64 = 1016 (MNC standard)
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

                int req = MNCConstants.CORE_BUCKET_CAPACITY; // 65024 / 64 = 1016 (MNC standard)
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

        public delegate void FastInitArrayDelegate(Array array, int startIndex, int endIndex);

        private static FastInitArrayDelegate CreateFastInit16(Type structType, MethodInfo initMethod)
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
                return (FastInitArrayDelegate)dm.CreateDelegate(typeof(FastInitArrayDelegate));
            }
            catch
            {
                return null;
            }
        }

        private static FastInitArrayDelegate CreateFastInit32(Type structType, MethodInfo initMethod)
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
                return (FastInitArrayDelegate)dm.CreateDelegate(typeof(FastInitArrayDelegate));
            }
            catch
            {
                return null;
            }
        }

        private static FastInitArrayDelegate CreateFastInitSegmentEnd(Type structType, MethodInfo initMethod)
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
                return (FastInitArrayDelegate)dm.CreateDelegate(typeof(FastInitArrayDelegate));
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
                if (nf != null && nf.GetValue(nem) is Array nArr && nArr.Length < MNCConstants.CORE_NET_CAPACITY)
                {
                    int oldLen = nArr.Length;
                    Type nType = nArr.GetType().GetElementType();
                    Array newArr = Array.CreateInstance(nType, MNCConstants.CORE_NET_CAPACITY);
                    Array.Copy(nArr, newArr, oldLen);

                    MethodInfo initM = nType.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(ushort) }, null);
                    if (initM != null)
                    {
                        var fastInit = CreateFastInit16(nType, initM);
                        if (fastInit != null)
                        {
                            fastInit(newArr, oldLen, MNCConstants.CORE_NET_CAPACITY);
                        }
                        else
                        {
                            for (int i = oldLen; i < MNCConstants.CORE_NET_CAPACITY; i++)
                            {
                                object item = Activator.CreateInstance(nType);
                                initM.Invoke(item, new object[] { (ushort)i });
                                newArr.SetValue(item, i);
                            }
                        }
                    }
                    nf.SetValue(nem, newArr);
                    resizedAny = true;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized AdaptiveRoads NodeBuffer from {0} to {1} ({2})", oldLen, MNCConstants.CORE_NET_CAPACITY, context));
                }

                // 2. SegmentBuffer (NetSegmentExt[65024])
                FieldInfo sf = nemType.GetField("SegmentBuffer", BindingFlags.Public | BindingFlags.Instance);
                if (sf != null && sf.GetValue(nem) is Array sArr && sArr.Length < MNCConstants.CORE_NET_CAPACITY)
                {
                    int oldLen = sArr.Length;
                    Type sType = sArr.GetType().GetElementType();
                    Array newArr = Array.CreateInstance(sType, MNCConstants.CORE_NET_CAPACITY);
                    Array.Copy(sArr, newArr, oldLen);

                    MethodInfo initM = sType.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(ushort) }, null);
                    if (initM != null)
                    {
                        var fastInit = CreateFastInit16(sType, initM);
                        if (fastInit != null)
                        {
                            fastInit(newArr, oldLen, MNCConstants.CORE_NET_CAPACITY);
                        }
                        else
                        {
                            for (int i = oldLen; i < MNCConstants.CORE_NET_CAPACITY; i++)
                            {
                                object item = Activator.CreateInstance(sType);
                                initM.Invoke(item, new object[] { (ushort)i });
                                newArr.SetValue(item, i);
                            }
                        }
                    }
                    sf.SetValue(nem, newArr);
                    resizedAny = true;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized AdaptiveRoads SegmentBuffer from {0} to {1} ({2})", oldLen, MNCConstants.CORE_NET_CAPACITY, context));
                }

                // 3. SegmentEndBuffer (NetSegmentEnd[130048])
                FieldInfo sef = nemType.GetField("SegmentEndBuffer", BindingFlags.Public | BindingFlags.Instance);
                if (sef != null && sef.GetValue(nem) is Array seArr && seArr.Length < MNCConstants.CORE_SPLIT_CAPACITY)
                {
                    int oldLen = seArr.Length;
                    Type seType = seArr.GetType().GetElementType();
                    Array newArr = Array.CreateInstance(seType, MNCConstants.CORE_SPLIT_CAPACITY);
                    Array.Copy(seArr, newArr, oldLen);

                    MethodInfo initM = seType.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(ushort), typeof(bool) }, null);
                    if (initM != null)
                    {
                        var fastInit = CreateFastInitSegmentEnd(seType, initM);
                        if (fastInit != null)
                        {
                            fastInit(newArr, oldLen / 2, MNCConstants.CORE_NET_CAPACITY);
                        }
                        else
                        {
                            for (int i = oldLen / 2; i < MNCConstants.CORE_NET_CAPACITY; i++)
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
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized AdaptiveRoads SegmentEndBuffer from {0} to {1} ({2})", oldLen, MNCConstants.CORE_SPLIT_CAPACITY, context));
                }

                // 4. LaneBuffer (NetLaneExt[524288])
                FieldInfo lf = nemType.GetField("LaneBuffer", BindingFlags.Public | BindingFlags.Instance);
                if (lf != null && lf.GetValue(nem) is Array lArr && lArr.Length < MNCConstants.LANE_CAPACITY)
                {
                    int oldLen = lArr.Length;
                    Type lType = lArr.GetType().GetElementType();
                    Array newArr = Array.CreateInstance(lType, MNCConstants.LANE_CAPACITY);
                    Array.Copy(lArr, newArr, oldLen);

                    MethodInfo initM = lType.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(uint) }, null);
                    if (initM != null)
                    {
                        var fastInit = CreateFastInit32(lType, initM);
                        if (fastInit != null)
                        {
                            fastInit(newArr, oldLen, MNCConstants.LANE_CAPACITY);
                        }
                        else
                        {
                            for (int i = oldLen; i < MNCConstants.LANE_CAPACITY; i++)
                            {
                                object item = Activator.CreateInstance(lType);
                                initM.Invoke(item, new object[] { (uint)i });
                                newArr.SetValue(item, i);
                            }
                        }
                    }
                    lf.SetValue(nem, newArr);
                    resizedAny = true;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized AdaptiveRoads LaneBuffer from {0} to {1} ({2})", oldLen, MNCConstants.LANE_CAPACITY, context));
                }

                // 5. m_updatedNodes (ulong[1016])
                FieldInfo unf = nemType.GetField("m_updatedNodes", BindingFlags.NonPublic | BindingFlags.Instance);
                if (unf != null && unf.GetValue(nem) is ulong[] unArr && unArr.Length < MNCConstants.CORE_BUCKET_CAPACITY)
                {
                    Array.Resize(ref unArr, MNCConstants.CORE_BUCKET_CAPACITY);
                    unf.SetValue(nem, unArr);
                    resizedAny = true;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized AdaptiveRoads m_updatedNodes to {0} ({1})", MNCConstants.CORE_BUCKET_CAPACITY, context));
                }

                // 6. m_updatedSegments (ulong[1016])
                FieldInfo usf = nemType.GetField("m_updatedSegments", BindingFlags.NonPublic | BindingFlags.Instance);
                if (usf != null && usf.GetValue(nem) is ulong[] usArr && usArr.Length < MNCConstants.CORE_BUCKET_CAPACITY)
                {
                    Array.Resize(ref usArr, MNCConstants.CORE_BUCKET_CAPACITY);
                    usf.SetValue(nem, usArr);
                    resizedAny = true;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized AdaptiveRoads m_updatedSegments to {0} ({1})", MNCConstants.CORE_BUCKET_CAPACITY, context));
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
                if (ssField != null && ssField.GetValue(null) is Array ssArr && ssArr.Length < MNCConstants.CORE_NET_CAPACITY)
                {
                    Type elem = ssArr.GetType().GetElementType();
                    Array newArr = Array.CreateInstance(elem, MNCConstants.CORE_NET_CAPACITY);
                    Array.Copy(ssArr, newArr, ssArr.Length);
                    ssField.SetValue(null, newArr);
                    resized = true;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized NetworkSkins SegmentSkins from {0} to {1} ({2})", ssArr.Length, MNCConstants.CORE_NET_CAPACITY, context));
                }

                FieldInfo nsField = nsmType.GetField("NodeSkins", BindingFlags.Public | BindingFlags.Static);
                if (nsField != null && nsField.GetValue(null) is Array nsArr && nsArr.Length < MNCConstants.CORE_NET_CAPACITY)
                {
                    Type elem = nsArr.GetType().GetElementType();
                    Array newArr = Array.CreateInstance(elem, MNCConstants.CORE_NET_CAPACITY);
                    Array.Copy(nsArr, newArr, nsArr.Length);
                    nsField.SetValue(null, newArr);
                    resized = true;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized NetworkSkins NodeSkins from {0} to {1} ({2})", nsArr.Length, MNCConstants.CORE_NET_CAPACITY, context));
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
                            if (buf != null && buf.Length < MNCConstants.CORE_NET_CAPACITY)
                            {
                                Type elem = buf.GetType().GetElementType();
                                Array newBuf = Array.CreateInstance(elem, MNCConstants.CORE_NET_CAPACITY);
                                Array.Copy(buf, newBuf, buf.Length);
                                bufProp.SetValue(ncMgr, newBuf, null);
                                MemoryOptimizer.StrategicGarbageCollect("AfterNodeControllerBufferExpansion");
                                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized NodeController Buffer from {0} to {1} ({2})", buf.Length, MNCConstants.CORE_NET_CAPACITY, context));
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
                        // 1. Expand CachedNodeData.m_cachedNodeData (stops and stations)
                        Type[] nodeTypes = new Type[] {
                            asm.GetType("ImprovedPublicTransport2.Data.CachedNodeData"),
                            asm.GetType("ImprovedPublicTransport.Data.CachedNodeData")
                        };
                        foreach (var type in nodeTypes)
                        {
                            if (type != null)
                            {
                                FieldInfo field = type.GetField("m_cachedNodeData", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                                if (field != null)
                                {
                                    Array arr = field.GetValue(null) as Array;
                                    if (arr != null && arr.Length < MNCConstants.CORE_NET_CAPACITY)
                                    {
                                        Type elem = arr.GetType().GetElementType();
                                        Array newArr = Array.CreateInstance(elem, MNCConstants.CORE_NET_CAPACITY);
                                        Array.Copy(arr, newArr, arr.Length);
                                        field.SetValue(null, newArr);
                                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized {0}.m_cachedNodeData from {1} to {2} ({3})", type.FullName, arr.Length, MNCConstants.CORE_NET_CAPACITY, context));
                                    }
                                }
                            }
                        }

                        // 2. Expand CachedVehicleData.m_cachedVehicleData (prevent IndexOutOfRange for vehicle ID >= 16384)
                        Type[] vehicleTypes = new Type[] {
                            asm.GetType("ImprovedPublicTransport2.Data.CachedVehicleData"),
                            asm.GetType("ImprovedPublicTransport.Data.CachedVehicleData")
                        };
                        foreach (var type in vehicleTypes)
                        {
                            if (type != null)
                            {
                                FieldInfo field = type.GetField("m_cachedVehicleData", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                                if (field != null)
                                {
                                    Array arr = field.GetValue(null) as Array;
                                    if (arr != null && arr.Length < 65536)
                                    {
                                        Type elem = arr.GetType().GetElementType();
                                        Array newArr = Array.CreateInstance(elem, 65536);
                                        Array.Copy(arr, newArr, arr.Length);
                                        field.SetValue(null, newArr);
                                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Resized {0}.m_cachedVehicleData from {1} to 65536 ({2})", type.FullName, arr.Length, context));
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

            // 1. Adaptive Roads lifecycle & deserialization hooks (armored stream detection & 65K expansion)
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
                                // Patch static Deserialize(SimpleDataSerializer s) with safe, armored prefix
                                MethodInfo deser = null;
                                foreach (var m in nemType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                                {
                                    if (m.Name == "Deserialize" && m.GetParameters().Length == 1)
                                    {
                                        deser = m;
                                        break;
                                    }
                                }
                                if (deser != null)
                                {
                                    MethodInfo pre = typeof(ThirdPartyCompatibility).GetMethod(nameof(AR_Deserialize_Prefix), BindingFlags.Public | BindingFlags.Static);
                                    harmony.Patch(deser, prefix: new HarmonyMethod(pre));
                                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Successfully hooked AdaptiveRoads.Deserialize with armored prefix!");
                                }

                                // Hook Create with postfix (expands if already expanded save or editor)
                                MethodInfo create = nemType.GetMethod("Create", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
                                if (create != null)
                                {
                                    MethodInfo post = typeof(ThirdPartyCompatibility).GetMethod(nameof(AR_Create_Postfix), BindingFlags.Public | BindingFlags.Static);
                                    harmony.Patch(create, postfix: new HarmonyMethod(post));
                                }

                                MethodInfo createNew = nemType.GetMethod("CreateNew", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                                if (createNew != null)
                                {
                                    MethodInfo post = typeof(ThirdPartyCompatibility).GetMethod(nameof(AR_CreateNew_Postfix), BindingFlags.Public | BindingFlags.Static);
                                    harmony.Patch(createNew, postfix: new HarmonyMethod(post));
                                }

                                _adaptiveRoadsPatched = true;
                                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Successfully hooked AdaptiveRoads lifecycle for dynamic 65K buffers & safe deserialization!");
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

            // 5. MorePathUnits load synchronization & safety hooks
            PatchMorePathUnits(harmony);

            // 6. Transfer Manager Extended (TransferManagerCE) path distance & 65K node safety hooks
            PatchTransferManager(harmony);
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

                // 6. Hook NetManager.Awake to immediately harmonize m_tileNodesCount to 3402
                try
                {
                    MethodInfo nmAwake = typeof(NetManager).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (nmAwake != null)
                    {
                        MethodInfo post = typeof(ThirdPartyCompatibility).GetMethod(nameof(NetManager_Awake_Postfix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(nmAwake, postfix: new HarmonyMethod(post));
                        patchedCount++;
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Hooked NetManager.Awake for 81 Tiles 2 harmonization.");
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Hook NetManager.Awake error: " + ex);
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

        public static void Harmonize81Tiles()
        {
            try
            {
                if (!Singleton<NetManager>.exists) return;
                NetManager nm = Singleton<NetManager>.instance;
                if (nm == null) return;

                bool has81Tiles = false;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string name = asm.GetName().Name;
                    if (name == "EightyOne2" || name == "EightyOne" || name.Contains("81Tiles"))
                    {
                        has81Tiles = true;
                        break;
                    }
                }

                if (has81Tiles)
                {
                    const int EIGHTY_ONE_TILES_NODES_COUNT = 3402; // 81 tiles * 42
                    FieldInfo fTileNodes = typeof(NetManager).GetField("m_tileNodesCount", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fTileNodes != null && fTileNodes.GetValue(nm) is int[] tileNodes && tileNodes.Length < EIGHTY_ONE_TILES_NODES_COUNT)
                    {
                        int oldLen = tileNodes.Length;
                        int[] expandedTileNodes = new int[EIGHTY_ONE_TILES_NODES_COUNT];
                        Array.Copy(tileNodes, expandedTileNodes, oldLen);
                        fTileNodes.SetValue(nm, expandedTileNodes);
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Harmonized 81 Tiles 2: Resized NetManager.m_tileNodesCount from {0} to {1} slots ✓", oldLen, EIGHTY_ONE_TILES_NODES_COUNT));
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Harmonize81Tiles error: " + ex);
            }
        }

        public static void NetManager_Awake_Postfix(NetManager __instance)
        {
            if (__instance == null) return;
            try
            {
                Harmonize81Tiles();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] NetManager_Awake_Postfix error: " + ex);
            }
        }

        // ====================================================================
        // MORE PATH UNITS COMPATIBILITY & DESERIALIZATION SYNCHRONIZATION
        // ====================================================================
        public static void EnsureMorePathUnitsPatched()
        {
            if (_morePathUnitsPatched) return;
            try
            {
                var harmony = HarmonyInstance.Create("MoreNetworkCapacityRenew.MorePathUnits");
                PatchMorePathUnits(harmony);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureMorePathUnitsPatched error: " + ex);
            }
        }

        public static void PatchMorePathUnits(HarmonyInstance harmony)
        {
            if (_morePathUnitsPatched || harmony == null) return;
            try
            {
                Assembly mpuAsm = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "MorePathUnits")
                    {
                        mpuAsm = asm;
                        break;
                    }
                }
                if (mpuAsm == null) return;

                int patchedCount = 0;

                // 1. Patch MorePathUnits.MetaData.get_LoadingExtended (Getter)
                try
                {
                    Type metaType = mpuAsm.GetType("MorePathUnits.MetaData");
                    if (metaType != null)
                    {
                        PropertyInfo prop = metaType.GetProperty("LoadingExtended", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        MethodInfo getter = (prop != null) ? prop.GetGetMethod(true) : null;
                        if (getter != null)
                        {
                            MethodInfo pre = typeof(ThirdPartyCompatibility).GetMethod(nameof(MorePathUnits_LoadingExtended_Prefix), BindingFlags.Public | BindingFlags.Static);
                            harmony.Patch(getter, prefix: new HarmonyMethod(pre) { prioritiy = Harmony.Priority.First });
                            patchedCount++;
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Hooked MorePathUnits.MetaData.LoadingExtended getter with expanded-save detection (Priority.First)!");
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patch MorePathUnits.MetaData error: " + ex);
                }

                // 2. Patch MorePathUnits.PathDeserialize.get_DeserializeSize (Getter)
                try
                {
                    Type pathDesType = mpuAsm.GetType("MorePathUnits.PathDeserialize");
                    if (pathDesType != null)
                    {
                        PropertyInfo prop = pathDesType.GetProperty("DeserializeSize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        MethodInfo getter = (prop != null) ? prop.GetGetMethod(true) : null;
                        if (getter != null)
                        {
                            MethodInfo pre = typeof(ThirdPartyCompatibility).GetMethod(nameof(MorePathUnits_DeserializeSize_Prefix), BindingFlags.Public | BindingFlags.Static);
                            harmony.Patch(getter, prefix: new HarmonyMethod(pre) { prioritiy = Harmony.Priority.First });
                            patchedCount++;
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Hooked MorePathUnits.PathDeserialize.DeserializeSize getter with 524,288 safety clamp (Priority.First)!");
                        }

                        // 3. Patch MorePathUnits.PathDeserialize.Prefix (Pre-prefix)
                        MethodInfo prefixM = pathDesType.GetMethod("Prefix", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (prefixM != null)
                        {
                            MethodInfo pre = typeof(ThirdPartyCompatibility).GetMethod(nameof(MorePathUnits_PathDeserialize_Prefix_Prefix), BindingFlags.Public | BindingFlags.Static);
                            harmony.Patch(prefixM, prefix: new HarmonyMethod(pre) { prioritiy = Harmony.Priority.First });
                            patchedCount++;
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Hooked MorePathUnits.PathDeserialize.Prefix with modOverride safety synchronization (Priority.First)!");
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patch MorePathUnits.PathDeserialize error: " + ex);
                }

                if (patchedCount > 0)
                {
                    _morePathUnitsPatched = true;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Successfully applied {0} MorePathUnits safety patches!", patchedCount));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] PatchMorePathUnits error: " + ex);
            }
        }

        public static bool IsPathUnitsExpandedSave(SimulationMetaData meta = null)
        {
            try
            {
                if (meta != null && meta.m_modOverride != null)
                {
                    if (meta.m_modOverride.TryGetValue("User/More PathUnits x2", out bool puExp) && puExp)
                    {
                        return true;
                    }
                }
                var sm = Singleton<SimulationManager>.instance;
                if (sm != null && sm.m_metaData != null && sm.m_metaData.m_modOverride != null)
                {
                    if (sm.m_metaData.m_modOverride.TryGetValue("User/More PathUnits x2", out bool puExp) && puExp)
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public static void SynchronizeMorePathUnits(string context)
        {
            try
            {
                bool isExp = IsPathUnitsExpandedSave();
                var sm = Singleton<SimulationManager>.instance;
                if (sm != null && sm.m_metaData != null)
                {
                    if (sm.m_metaData.m_modOverride == null)
                    {
                        sm.m_metaData.m_modOverride = new Dictionary<string, bool>();
                    }
                    if (context == "PreSaveSanitization")
                    {
                        var pmInst = Singleton<PathManager>.instance;
                        if (pmInst != null && pmInst.m_pathUnits != null && pmInst.m_pathUnits.m_buffer != null && pmInst.m_pathUnits.m_buffer.Length >= 524288)
                        {
                            isExp = true;
                        }
                    }
                    if (isExp)
                    {
                        sm.m_metaData.m_modOverride["User/More PathUnits x2"] = true;
                    }
                }

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "MorePathUnits")
                    {
                        Type pdType = asm.GetType("MorePathUnits.PathDeserialize");
                        if (pdType != null)
                        {
                            FieldInfo fExp = pdType.GetField("s_loadingExpanded", BindingFlags.NonPublic | BindingFlags.Static);
                            if (fExp != null)
                            {
                                fExp.SetValue(null, isExp);
                            }
                        }
                        break;
                    }
                }

                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] SynchronizeMorePathUnits: Guaranteed s_loadingExpanded={0} and modOverride['User/More PathUnits x2']={1} ({2})", isExp, isExp, context));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SynchronizeMorePathUnits error: " + ex);
            }
        }

        public static bool MorePathUnits_LoadingExtended_Prefix(ref bool __result)
        {
            try
            {
                if (IsPathUnitsExpandedSave())
                {
                    __result = true;
                    return false;
                }
                __result = false;
                return false;
            }
            catch {}
            return true;
        }

        public static bool MorePathUnits_DeserializeSize_Prefix(ref int __result)
        {
            try
            {
                if (IsPathUnitsExpandedSave())
                {
                    __result = 524288;
                    return false;
                }
                __result = 262144;
                return false;
            }
            catch {}
            return true;
        }

        public static void MorePathUnits_PathDeserialize_Prefix_Prefix()
        {
            SynchronizeMorePathUnits("MorePathUnits.PathDeserialize.Prefix.Prefix");
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
                try
                {
                    s_copyWaterUnits(expUnits, targetUnits, count);
                    return;
                }
                catch { }
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

        public static bool IsExpandedSave()
        {
            try
            {
                var sm = Singleton<SimulationManager>.instance;
                if (sm != null && sm.m_metaData != null && sm.m_metaData.m_modOverride != null)
                {
                    if (sm.m_metaData.m_modOverride.TryGetValue("User/MoreNetworkCapacity/64K-v1", out bool val))
                    {
                        return val;
                    }
                }
            }
            catch {}
            return false;
        }

        public static bool IsExpandedARStream(object s)
        {
            if (s == null) return false;
            try
            {
                FieldInfo sf = s.GetType().GetField("Stream", BindingFlags.Public | BindingFlags.Instance);
                if (sf != null && sf.GetValue(s) is MemoryStream stream)
                {
                    long remaining = stream.Length - stream.Position;
                    // Standard 36K saves require ~868KB to 1.16MB base. Total stream is < 1.45MB.
                    // Expanded 65K saves require at least 1.57MB base (65536*4 + 131072*8 + 65536*4) + 520KB user data = 2.09MB base before lanes!
                    if (remaining < 1500000)
                    {
                        return false;
                    }
                    return IsExpandedSave();
                }
            }
            catch {}
            return IsExpandedSave();
        }

        public static void EnsureAdaptiveRoadsStandardBuffers(object nem)
        {
            if (nem == null) return;
            try
            {
                Type nemType = nem.GetType();

                // 1. NodeBuffer -> 32768
                FieldInfo nf = nemType.GetField("NodeBuffer", BindingFlags.Public | BindingFlags.Instance);
                if (nf != null && nf.GetValue(nem) is Array nArr && nArr.Length > 32768)
                {
                    Type nType = nArr.GetType().GetElementType();
                    Array newArr = Array.CreateInstance(nType, 32768);
                    Array.Copy(nArr, newArr, 32768);
                    nf.SetValue(nem, newArr);
                }

                // 2. SegmentBuffer -> 36864
                FieldInfo sf = nemType.GetField("SegmentBuffer", BindingFlags.Public | BindingFlags.Instance);
                if (sf != null && sf.GetValue(nem) is Array sArr && sArr.Length > 36864)
                {
                    Type sType = sArr.GetType().GetElementType();
                    Array newArr = Array.CreateInstance(sType, 36864);
                    Array.Copy(sArr, newArr, 36864);
                    sf.SetValue(nem, newArr);
                }

                // 3. SegmentEndBuffer -> 73728
                FieldInfo sef = nemType.GetField("SegmentEndBuffer", BindingFlags.Public | BindingFlags.Instance);
                if (sef != null && sef.GetValue(nem) is Array seArr && seArr.Length > 73728)
                {
                    Type seType = seArr.GetType().GetElementType();
                    Array newArr = Array.CreateInstance(seType, 73728);
                    Array.Copy(seArr, newArr, 73728);
                    sef.SetValue(nem, newArr);
                }

                // 4. LaneBuffer -> 262144
                FieldInfo lf = nemType.GetField("LaneBuffer", BindingFlags.Public | BindingFlags.Instance);
                if (lf != null && lf.GetValue(nem) is Array lArr && lArr.Length > 262144)
                {
                    Type lType = lArr.GetType().GetElementType();
                    Array newArr = Array.CreateInstance(lType, 262144);
                    Array.Copy(lArr, newArr, 262144);
                    lf.SetValue(nem, newArr);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureAdaptiveRoadsStandardBuffers error: " + ex);
            }
        }

        public static bool AR_Deserialize_Prefix(object s)
        {
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
                if (nemType == null) return true;

                MethodInfo createM = nemType.GetMethod("Create", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
                object inst = createM != null ? createM.Invoke(null, null) : null;
                FieldInfo instF = nemType.GetField("instance_", BindingFlags.NonPublic | BindingFlags.Static);
                if (instF != null && inst != null)
                {
                    instF.SetValue(null, inst);
                }

                if (inst != null && s != null)
                {
                    bool isExpanded = IsExpandedARStream(s);
                    if (!isExpanded)
                    {
                        EnsureAdaptiveRoadsStandardBuffers(inst);
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] AR Deserialize: Standard 36K save detected. Buffers pre-set to vanilla sizes to prevent stream overflow.");
                    }
                    else
                    {
                        EnsureAdaptiveRoadsBuffers("AR.Deserialize.Expanded", inst);
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] AR Deserialize: Expanded 65K save detected. Buffers pre-expanded to 65K.");
                    }

                    MethodInfo deserImp = null;
                    foreach (var m in nemType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (m.Name == "DeserializeImp" && m.GetParameters().Length == 1)
                        {
                            deserImp = m;
                            break;
                        }
                    }

                    if (deserImp != null)
                    {
                        try
                        {
                            deserImp.Invoke(inst, new object[] { s });
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] AR DeserializeImp completed cleanly!");
                        }
                        catch (Exception deserEx)
                        {
                            UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] Handled exception during AR DeserializeImp: " + deserEx);
                        }
                    }

                    // Always ensure buffers are 65K for active gameplay!
                    EnsureAdaptiveRoadsBuffers("AR.Deserialize.Finished", inst);
                }
                else if (inst != null)
                {
                    EnsureAdaptiveRoadsBuffers("AR.Deserialize.NullData", inst);
                }

                return false; // Skip original method so no assertion dialog can ever be shown!
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] AR_Deserialize_Prefix error: " + ex);
                return true;
            }
        }

        public static void AR_CreateNew_Postfix()
        {
            if (IsExpandedSave())
            {
                EnsureAdaptiveRoadsBuffers("AR.CreateNew.Postfix", null);
            }
        }

        public static void AR_Create_Postfix(object __result)
        {
            if (__result != null && IsExpandedSave())
            {
                EnsureAdaptiveRoadsBuffers("AR.Create.Postfix", __result);
            }
        }

        public static void AR_InitBuffers_Postfix(object __instance)
        {
            // Intentionally no-op to prevent premature 65K buffer resizing before save deserialization
        }

        public static void AR_Awake_Postfix(object __instance)
        {
            // Intentionally no-op to prevent premature 65K buffer resizing before save deserialization
        }

        public static void NetworkSkinManager_Awake_Postfix()
        {
            EnsureNetworkSkinsBuffers("NS.Awake.Postfix");
        }

        public static void NodeController_Clear_Postfix()
        {
            EnsureNodeControllerBuffers("NC.Clear.Postfix");
        }

        // ====================================================================
        // TRANSFER MANAGER EXTENDED (TRANSFERMANAGERCE) 65K COMPATIBILITY
        // ====================================================================
        public static void EnsureTransferManagerPatched()
        {
            if (_transferManagerPatched) return;
            try
            {
                var harmony = HarmonyInstance.Create("MoreNetworkCapacityRenew.TransferManager");
                PatchTransferManager(harmony);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureTransferManagerPatched error: " + ex);
            }
        }

        public static void PatchTransferManager(HarmonyInstance harmony)
        {
            if (_transferManagerPatched || harmony == null) return;
            try
            {
                Assembly tmeAsm = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "TransferManagerCE" || asm.GetName().Name == "TransferManagerExtended")
                    {
                        tmeAsm = asm;
                        break;
                    }
                }
                if (tmeAsm == null) return;

                int patchedCount = 0;
                Type pdType = tmeAsm.GetType("TransferManagerCore.PathDistance");
                if (pdType != null)
                {
                    // 1. Hook .ctor to expand m_visited to 65536
                    foreach (var ctor in pdType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        MethodInfo post = typeof(ThirdPartyCompatibility).GetMethod(nameof(TransferManager_PathDistance_Ctor_Postfix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(ctor, postfix: new HarmonyMethod(post));
                        patchedCount++;
                    }

                    // 2. Hook ResetArrays to ensure m_visited has 65536 capacity
                    MethodInfo resetArrays = pdType.GetMethod("ResetArrays", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (resetArrays != null)
                    {
                        MethodInfo pre = typeof(ThirdPartyCompatibility).GetMethod(nameof(TransferManager_PathDistance_ResetArrays_Prefix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(resetArrays, prefix: new HarmonyMethod(pre));
                        patchedCount++;
                    }

                    // 3. Hook UpdateNode to protect against out-of-range node IDs and ensure m_visited bounds
                    MethodInfo updateNode = pdType.GetMethod("UpdateNode", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(ushort), typeof(ushort), typeof(float) }, null);
                    if (updateNode != null)
                    {
                        MethodInfo pre = typeof(ThirdPartyCompatibility).GetMethod(nameof(TransferManager_PathDistance_UpdateNode_Prefix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(updateNode, prefix: new HarmonyMethod(pre));
                        patchedCount++;
                    }
                }

                if (patchedCount > 0)
                {
                    _transferManagerPatched = true;
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Successfully applied {0} TransferManagerCE 65K node expansion & safety patches!", patchedCount));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] PatchTransferManager error: " + ex);
            }
        }

        public static void EnsureTransferManagerBuffers(string context)
        {
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "TransferManagerCE" || asm.GetName().Name == "TransferManagerExtended")
                    {
                        Type pathNodeType = asm.GetType("TransferManagerCore.PathNode");
                        if (pathNodeType != null && Singleton<NetManager>.instance != null)
                        {
                            NetManager nm = Singleton<NetManager>.instance;
                            if (nm.m_segments != null)
                            {
                                FieldInfo fSegs = pathNodeType.GetField("NetSegments", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                                if (fSegs != null)
                                {
                                    fSegs.SetValue(null, nm.m_segments.m_buffer);
                                }
                            }
                            if (nm.m_nodes != null)
                            {
                                FieldInfo fNodes = pathNodeType.GetField("NetNodes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                                if (fNodes != null)
                                {
                                    fNodes.SetValue(null, nm.m_nodes.m_buffer);
                                }
                            }
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureTransferManagerBuffers error: " + ex);
            }
        }

        public static void TransferManager_PathDistance_Ctor_Postfix(object __instance)
        {
            try
            {
                if (__instance != null)
                {
                    FieldInfo fVisited = __instance.GetType().GetField("m_visited", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fVisited != null)
                    {
                        fVisited.SetValue(__instance, new System.Collections.BitArray(MNCConstants.CORE_NET_CAPACITY));
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TransferManager_PathDistance_Ctor_Postfix error: " + ex);
            }
        }

        public static void TransferManager_PathDistance_ResetArrays_Prefix(object __instance)
        {
            try
            {
                if (__instance != null)
                {
                    FieldInfo fVisited = __instance.GetType().GetField("m_visited", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fVisited != null && fVisited.GetValue(__instance) is System.Collections.BitArray ba && ba.Length < MNCConstants.CORE_NET_CAPACITY)
                    {
                        fVisited.SetValue(__instance, new System.Collections.BitArray(MNCConstants.CORE_NET_CAPACITY));
                    }
                }
            }
            catch { }
        }

        public static bool TransferManager_PathDistance_UpdateNode_Prefix(object __instance, ushort nodeId, ushort prevId, float travelTime)
        {
            if (nodeId == 0 || nodeId >= MNCConstants.CORE_NET_CAPACITY || travelTime <= 0f)
            {
                return false;
            }

            try
            {
                if (__instance != null)
                {
                    FieldInfo fVisited = __instance.GetType().GetField("m_visited", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fVisited != null && fVisited.GetValue(__instance) is System.Collections.BitArray ba && nodeId >= ba.Length)
                    {
                        System.Collections.BitArray newBa = new System.Collections.BitArray(MNCConstants.CORE_NET_CAPACITY);
                        for (int i = 0; i < ba.Length; i++)
                        {
                            newBa[i] = ba[i];
                        }
                        fVisited.SetValue(__instance, newBa);
                    }
                }
            }
            catch { }

            return true;
        }

        public static void EnsureImpatientCommutersBuffers(string context)
        {
            try
            {
                Type limiterType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "ImpatientCommuters")
                    {
                        limiterType = asm.GetType("ImpatientCommuters.CommuterImpatienceLimiter");
                        break;
                    }
                }
                if (limiterType == null) return;

                if (Singleton<SimulationManager>.instance != null)
                {
                    var sm = Singleton<SimulationManager>.instance;
                    FieldInfo twField = typeof(SimulationManager).GetField("m_ThreadingWrapper", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (twField != null)
                    {
                        object tw = twField.GetValue(sm);
                        if (tw != null)
                        {
                            FieldInfo listField = tw.GetType().GetField("m_ThreadingExtensions", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (listField != null && listField.GetValue(tw) is System.Collections.IList list)
                            {
                                for (int i = 0; i < list.Count; i++)
                                {
                                    object ext = list[i];
                                    if (ext != null && ext.GetType() == limiterType)
                                    {
                                        ExpandImpatientCommutersInstance(ext, context);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EnsureImpatientCommutersBuffers error: " + ex);
            }
        }

        public static void ExpandImpatientCommutersInstance(object instance, string context = "")
        {
            if (instance == null) return;
            try
            {
                Type limiterType = instance.GetType();
                const int targetCapacity = 65536;
                bool resizedAny = false;

                // 1. Expand node index arrays to 65536 (eliminates IndexOutOfRangeException for all ushort values 0..65535)
                string[] ushortArrayFields = new string[] { "_passengerCount", "_capacityThreshold", "_vehicleCount", "_stopCount" };
                for (int i = 0; i < ushortArrayFields.Length; i++)
                {
                    FieldInfo f = limiterType.GetField(ushortArrayFields[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null && f.GetValue(instance) is ushort[] arr && arr.Length < targetCapacity)
                    {
                        ushort[] newArr = new ushort[targetCapacity];
                        Array.Copy(arr, newArr, arr.Length);
                        f.SetValue(instance, newArr);
                        resizedAny = true;
                    }
                }

                FieldInfo fBool = limiterType.GetField("_hasAlternativeLine", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fBool != null && fBool.GetValue(instance) is bool[] bArr && bArr.Length < targetCapacity)
                {
                    bool[] newBArr = new bool[targetCapacity];
                    Array.Copy(bArr, newBArr, bArr.Length);
                    fBool.SetValue(instance, newBArr);
                    resizedAny = true;
                }

                // 2. Synchronize game buffer references to active expanded buffers
                if (Singleton<NetManager>.instance != null)
                {
                    NetManager nm = Singleton<NetManager>.instance;
                    if (nm.m_nodes != null && nm.m_nodes.m_buffer != null)
                    {
                        FieldInfo fNodes = limiterType.GetField("_nodes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (fNodes != null && !object.ReferenceEquals(fNodes.GetValue(instance), nm.m_nodes.m_buffer))
                        {
                            fNodes.SetValue(instance, nm.m_nodes.m_buffer);
                        }
                    }
                    if (nm.m_segments != null && nm.m_segments.m_buffer != null)
                    {
                        FieldInfo fSegs = limiterType.GetField("_segments", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (fSegs != null && !object.ReferenceEquals(fSegs.GetValue(instance), nm.m_segments.m_buffer))
                        {
                            fSegs.SetValue(instance, nm.m_segments.m_buffer);
                        }
                    }
                }

                if (Singleton<PathManager>.instance != null && Singleton<PathManager>.instance.m_pathUnits != null && Singleton<PathManager>.instance.m_pathUnits.m_buffer != null)
                {
                    FieldInfo fPaths = limiterType.GetField("_pathUnits", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fPaths != null && !object.ReferenceEquals(fPaths.GetValue(instance), Singleton<PathManager>.instance.m_pathUnits.m_buffer))
                    {
                        fPaths.SetValue(instance, Singleton<PathManager>.instance.m_pathUnits.m_buffer);
                    }
                }

                if (Singleton<CitizenManager>.instance != null)
                {
                    CitizenManager cm = Singleton<CitizenManager>.instance;
                    if (cm.m_instances != null && cm.m_instances.m_buffer != null)
                    {
                        FieldInfo fInst = limiterType.GetField("_instances", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (fInst != null && !object.ReferenceEquals(fInst.GetValue(instance), cm.m_instances.m_buffer))
                        {
                            fInst.SetValue(instance, cm.m_instances.m_buffer);
                        }
                    }
                    if (cm.m_citizens != null && cm.m_citizens.m_buffer != null)
                    {
                        FieldInfo fCit = limiterType.GetField("_citizens", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (fCit != null && !object.ReferenceEquals(fCit.GetValue(instance), cm.m_citizens.m_buffer))
                        {
                            fCit.SetValue(instance, cm.m_citizens.m_buffer);
                        }
                    }
                }

                if (Singleton<TransportManager>.instance != null && Singleton<TransportManager>.instance.m_lines != null && Singleton<TransportManager>.instance.m_lines.m_buffer != null)
                {
                    FieldInfo fLines = limiterType.GetField("_lines", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fLines != null && !object.ReferenceEquals(fLines.GetValue(instance), Singleton<TransportManager>.instance.m_lines.m_buffer))
                    {
                        fLines.SetValue(instance, Singleton<TransportManager>.instance.m_lines.m_buffer);
                    }
                }

                if (Singleton<VehicleManager>.instance != null && Singleton<VehicleManager>.instance.m_vehicles != null && Singleton<VehicleManager>.instance.m_vehicles.m_buffer != null)
                {
                    FieldInfo fVeh = limiterType.GetField("_vehicles", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fVeh != null && !object.ReferenceEquals(fVeh.GetValue(instance), Singleton<VehicleManager>.instance.m_vehicles.m_buffer))
                    {
                        fVeh.SetValue(instance, Singleton<VehicleManager>.instance.m_vehicles.m_buffer);
                    }
                }

                if (resizedAny)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Successfully expanded Impatient Commuters node arrays to {0} without any hooks ({1})", targetCapacity, context));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] ExpandImpatientCommutersInstance error: " + ex);
            }
        }
    }

    // ============================================================================
    // UNIVERSAL MOD HARMONIZER (v5.0)
    // Dynamic Runtime Array Expansion & Auto-Harmonization for Future/Untested Mods
    // ============================================================================
    public static class UniversalModHarmonizer
    {
        private static readonly HashSet<string> s_inspectedAssemblies = new HashSet<string>();
        private static readonly object s_lock = new object();

        public static bool IsIgnoredAssembly(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            if (name.StartsWith("System") ||
                name.StartsWith("mscorlib") ||
                name.StartsWith("UnityEngine") ||
                name.StartsWith("ColossalManaged") ||
                name.StartsWith("Mono.") ||
                name.StartsWith("Harmony") ||
                name.StartsWith("0Harmony") ||
                name.StartsWith("CitiesHarmony") ||
                name.StartsWith("ICities") ||
                name.StartsWith("Assembly-CSharp") ||
                name == "MoreNodesSegments")
            {
                return true;
            }

            // Strictly ignore known UI, asset, search, graphics, camera, audio, tool mods to prevent touching asset/texture loaders
            string lower = name.ToLowerInvariant();
            return lower.Contains("findit") ||
                   lower.Contains("picker") ||
                   lower.Contains("search") ||
                   lower.Contains("camera") ||
                   lower.Contains("theme") ||
                   lower.Contains("render") ||
                   lower.Contains("relight") ||
                   lower.Contains("lut") ||
                   lower.Contains("font") ||
                   lower.Contains("ui") ||
                   lower.Contains("asset") ||
                   lower.Contains("tree") ||
                   lower.Contains("prop") ||
                   lower.Contains("audio") ||
                   lower.Contains("music") ||
                   lower.Contains("sound") ||
                   lower.Contains("fps") ||
                   lower.Contains("loading") ||
                   lower.Contains("chirp") ||
                   lower.Contains("achievement") ||
                   lower.Contains("unlock");
        }

        public static void HarmonizeActiveMods(string context)
        {
            try
            {
                // CRITICAL SAFETY SHIELD: Never inspect or touch other mods during Main Menu, Options panel, or before level is loaded!
                if (!Singleton<NetManager>.exists || NetManager.instance == null ||
                    !Singleton<LoadingManager>.exists || !LoadingManager.instance.m_loadingComplete)
                {
                    return;
                }

                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (Assembly asm in assemblies)
                {
                    if (asm == null) continue;
                    string asmName = asm.GetName().Name;
                    if (IsIgnoredAssembly(asmName)) continue;

                    lock (s_lock)
                    {
                        if (s_inspectedAssemblies.Contains(asmName)) continue;
                        s_inspectedAssemblies.Add(asmName);
                    }

                    HarmonizeAssembly(asm, context);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] UniversalModHarmonizer.HarmonizeActiveMods error: " + ex);
            }
        }

        private static bool IsCandidateType(Type type)
        {
            if (type == null || type.IsInterface || type.IsEnum || type.IsValueType) return false;
            string name = type.Name.ToLowerInvariant();
            string ns = (type.Namespace ?? "").ToLowerInvariant();

            return name.Contains("net") || name.Contains("node") || name.Contains("segment") ||
                   name.Contains("lane") || name.Contains("road") || name.Contains("path") ||
                   name.Contains("traffic") || name.Contains("tile") || name.Contains("transport") ||
                   ns.Contains("net") || ns.Contains("road") || ns.Contains("traffic") || ns.Contains("path");
        }

        public static int HarmonizeAssembly(Assembly asm, string context)
        {
            if (asm == null) return 0;
            // Also ensure we are in level
            if (!Singleton<NetManager>.exists || NetManager.instance == null) return 0;

            int resizedCount = 0;
            try
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch
                {
                    return 0;
                }

                if (types == null) return 0;

                foreach (Type type in types)
                {
                    if (type == null || !IsCandidateType(type)) continue;
                    try
                    {
                        resizedCount += ScanAndHarmonizeType(type, null, context);

                        object instance = FindSingletonInstance(type);
                        if (instance != null)
                        {
                            resizedCount += ScanAndHarmonizeType(type, instance, context);
                        }
                    }
                    catch
                    {
                        // Safely continue next type
                    }
                }
            }
            catch
            {
                // Safely continue
            }
            return resizedCount;
        }

        private static object FindSingletonInstance(Type type)
        {
            try
            {
                // CRITICAL: Never trigger a type initializer (.cctor) reflectively!
                if (type.TypeInitializer != null) return null;

                string[] instanceFieldNames = new string[] { "instance", "Instance", "s_instance", "_instance", "RawInstance" };
                foreach (string name in instanceFieldNames)
                {
                    FieldInfo f = type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null && f.FieldType == type)
                    {
                        return f.GetValue(null);
                    }
                }

                foreach (string name in instanceFieldNames)
                {
                    PropertyInfo p = type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null && p.CanRead && p.PropertyType == type)
                    {
                        return p.GetValue(null, null);
                    }
                }
            }
            catch { }
            return null;
        }

        public static int ScanAndHarmonizeType(Type type, object instance, string context)
        {
            int count = 0;
            try
            {
                // CRITICAL: If type has a static constructor that hasn't run, DO NOT access static fields reflectively!
                if (instance == null && type.TypeInitializer != null) return 0;

                BindingFlags flags = instance == null
                    ? (BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    : (BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                FieldInfo[] fields = type.GetFields(flags);
                foreach (FieldInfo field in fields)
                {
                    try
                    {
                        if (!field.FieldType.IsArray) continue;

                        Array arr = field.GetValue(instance) as Array;
                        if (arr == null) continue;

                        int len = arr.Length;
                        int targetLen = 0;

                        // 1. Vanilla 32K or intermediate 36K/49K limits -> expand to 65024 (MNCConstants.CORE_NET_CAPACITY)
                        if (len == 32768 || len == 36864 || len == 49152)
                        {
                            string fName = field.Name.ToLowerInvariant();
                            string tName = type.Name.ToLowerInvariant();
                            if (len == 36864 ||
                                fName.Contains("node") || fName.Contains("segment") || fName.Contains("lane") ||
                                fName.Contains("net") || fName.Contains("path") || fName.Contains("traffic") ||
                                tName.Contains("node") || tName.Contains("segment") || tName.Contains("net") ||
                                tName.Contains("road") || tName.Contains("path"))
                            {
                                targetLen = MNCConstants.CORE_NET_CAPACITY;
                            }
                        }
                        // 2. Old 25-tiles node count (1050 = 25 * 42) -> expand to 3402 (81 * 42)
                        else if (len == 1050)
                        {
                            string fName = field.Name.ToLowerInvariant();
                            string tName = type.Name.ToLowerInvariant();
                            if (fName.Contains("tile") || fName.Contains("node") || tName.Contains("tile") || tName.Contains("eighty"))
                            {
                                targetLen = 3402;
                            }
                        }

                        if (targetLen > len)
                        {
                            Type elemType = field.FieldType.GetElementType();
                            Array newArr = Array.CreateInstance(elemType, targetLen);
                            Array.Copy(arr, newArr, len);
                            field.SetValue(instance, newArr);
                            count++;
                            UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] UniversalModHarmonizer: Resized {0}.{1} ({2}[{3}] -> [{4}]) ({5})",
                                type.FullName, field.Name, elemType.Name, len, targetLen, context));
                        }
                    }
                    catch
                    {
                        // Ignore individual field errors
                    }
                }
            }
            catch
            {
                // Ignore type scanning errors
            }
            return count;
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

                    // 1b. Patch TransportManager.Data.Serialize (Pre-Save sanitization & diagnostics)
                    MethodInfo tmSer = tmDataType.GetMethod("Serialize", BindingFlags.Public | BindingFlags.Instance);
                    if (tmSer != null)
                    {
                        MethodInfo pre = typeof(TransportLineCompatibility).GetMethod(nameof(TransportManager_Data_Serialize_Prefix), BindingFlags.Public | BindingFlags.Static);
                        MethodInfo post = typeof(TransportLineCompatibility).GetMethod(nameof(TransportManager_Data_Serialize_Postfix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(tmSer, prefix: new HarmonyMethod(pre), postfix: new HarmonyMethod(post));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TransportManager.Data.Serialize with pre-save sanitization and diagnostic tracker.");
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
                        harmony.Patch(simSer, prefix: new HarmonyMethod(pre) { prioritiy = Harmony.Priority.First });
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched SimulationManager.Data.Serialize with universal pre-save sanitization (Priority.First).");
                    }

                    // 2b. Patch SimulationManager.Data.Deserialize (Early Save Metadata & Mod Override Synchronization)
                    MethodInfo simDes = simDataType.GetMethod("Deserialize", BindingFlags.Public | BindingFlags.Instance);
                    if (simDes != null)
                    {
                        MethodInfo preDes = typeof(TransportLineCompatibility).GetMethod(nameof(SimulationManager_Data_Deserialize_Prefix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(simDes, prefix: new HarmonyMethod(preDes) { prioritiy = Harmony.Priority.First });
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched SimulationManager.Data.Deserialize with early metadata synchronization (Priority.First).");
                    }
                }

                // Enforce vanilla SimulationManager.SYNCHRONIZE_TIMEOUT = 0 to prevent simulation thread blocking
                try
                {
                    SimulationManager.SYNCHRONIZE_TIMEOUT = 0;
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Verified SimulationManager.SYNCHRONIZE_TIMEOUT = 0 (vanilla instant lock acquisition).");
                }
                catch { }

                // 3. Patch PathManager.WaitForAllPaths (Deadlock defense)
                try
                {
                    MethodInfo wfap = typeof(PathManager).GetMethod("WaitForAllPaths", BindingFlags.Public | BindingFlags.Instance);
                    if (wfap != null)
                    {
                        MethodInfo pre = typeof(TransportLineCompatibility).GetMethod(nameof(PathManager_WaitForAllPaths_Prefix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(wfap, prefix: new HarmonyMethod(pre) { prioritiy = Harmony.Priority.First });
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched PathManager.WaitForAllPaths with safe bounded timeout defense (Priority.First).");
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching PathManager WaitForAllPaths: " + ex);
                }

                // 3b. Patch PathManager.Data.Deserialize (MorePathUnits load synchronization & safety)
                try
                {
                    Type pmDataType = typeof(PathManager).GetNestedType("Data", BindingFlags.NonPublic | BindingFlags.Public);
                    if (pmDataType != null)
                    {
                        MethodInfo pmDes = pmDataType.GetMethod("Deserialize", BindingFlags.Public | BindingFlags.Instance);
                        if (pmDes != null)
                        {
                            MethodInfo pre = typeof(TransportLineCompatibility).GetMethod(nameof(PathManager_Data_Deserialize_Prefix), BindingFlags.Public | BindingFlags.Static);
                            harmony.Patch(pmDes, prefix: new HarmonyMethod(pre) { prioritiy = Harmony.Priority.First });
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched PathManager.Data.Deserialize with load synchronization (Priority.First).");
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching PathManager.Data.Deserialize: " + ex);
                }

                // 3b-2. Patch CitizenManager.Data.Deserialize (Universal Dynamic Buffer Guard for CitizenUnits)
                try
                {
                    Type cmDataType = typeof(CitizenManager).GetNestedType("Data", BindingFlags.NonPublic | BindingFlags.Public);
                    if (cmDataType != null)
                    {
                        MethodInfo cmDes = cmDataType.GetMethod("Deserialize", BindingFlags.Public | BindingFlags.Instance);
                        if (cmDes != null)
                        {
                            MethodInfo pre = typeof(TransportLineCompatibility).GetMethod(nameof(CitizenManager_Data_Deserialize_Prefix), BindingFlags.Public | BindingFlags.Static);
                            harmony.Patch(cmDes, prefix: new HarmonyMethod(pre) { prioritiy = Harmony.Priority.First });
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched CitizenManager.Data.Deserialize with Universal Dynamic Buffer Guard (Priority.First).");
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching CitizenManager.Data.Deserialize: " + ex);
                }

                // 3c. Patch LoadingProfiler.BeginDeserialize & EndDeserialize (Universal Stream Armor against tag mismatches)
                try
                {
                    MethodInfo beginDes = typeof(LoadingProfiler).GetMethod("BeginDeserialize", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(ColossalFramework.IO.DataSerializer), typeof(string) }, null);
                    if (beginDes != null)
                    {
                        MethodInfo preBegin = typeof(TransportLineCompatibility).GetMethod(nameof(LoadingProfiler_BeginDeserialize_Prefix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(beginDes, prefix: new HarmonyMethod(preBegin));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched LoadingProfiler.BeginDeserialize with Universal Stream Armor.");
                    }

                    MethodInfo endDes = typeof(LoadingProfiler).GetMethod("EndDeserialize", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(ColossalFramework.IO.DataSerializer), typeof(string) }, null);
                    if (endDes != null)
                    {
                        MethodInfo preEnd = typeof(TransportLineCompatibility).GetMethod(nameof(LoadingProfiler_EndDeserialize_Prefix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(endDes, prefix: new HarmonyMethod(preEnd));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched LoadingProfiler.EndDeserialize with Universal Stream Armor.");
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching LoadingProfiler tags: " + ex);
                }

                // 3d. Patch SimulationMetaData.Merge (Preserve save modOverride dictionary from being clobbered by ngs)
                try
                {
                    MethodInfo smdMerge = typeof(SimulationMetaData).GetMethod("Merge", BindingFlags.Public | BindingFlags.Instance);
                    if (smdMerge != null)
                    {
                        MethodInfo pre = typeof(TransportLineCompatibility).GetMethod(nameof(SimulationMetaData_Merge_Prefix), BindingFlags.Public | BindingFlags.Static);
                        MethodInfo post = typeof(TransportLineCompatibility).GetMethod(nameof(SimulationMetaData_Merge_Postfix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(smdMerge, prefix: new HarmonyMethod(pre) { prioritiy = Harmony.Priority.First }, postfix: new HarmonyMethod(post));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched SimulationMetaData.Merge with modOverride preservation (Priority.First).");
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching SimulationMetaData.Merge: " + ex);
                }

                // 3e. Patch LoadingManager.LoadSimulationData (Seed expanded modOverride flags before simulation load starts)
                try
                {
                    MethodInfo lmLoadSim = typeof(LoadingManager).GetMethod("LoadSimulationData", BindingFlags.Public | BindingFlags.Instance);
                    if (lmLoadSim != null)
                    {
                        MethodInfo pre = typeof(TransportLineCompatibility).GetMethod(nameof(LoadingManager_LoadSimulationData_Prefix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(lmLoadSim, prefix: new HarmonyMethod(pre) { prioritiy = Harmony.Priority.First });
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched LoadingManager.LoadSimulationData for early modOverride safety (Priority.First).");
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching LoadingManager.LoadSimulationData: " + ex);
                }

                // 3f. Patch CombinedMilestone.CombinedData.Deserialize (Defensive milestone deserialization guard)
                try
                {
                    Type cmDataType = typeof(CombinedMilestone).GetNestedType("CombinedData", BindingFlags.Public | BindingFlags.NonPublic);
                    if (cmDataType != null)
                    {
                        MethodInfo cmDes = cmDataType.GetMethod("Deserialize", BindingFlags.Public | BindingFlags.Instance);
                        if (cmDes != null)
                        {
                            MethodInfo pre = typeof(TransportLineCompatibility).GetMethod(nameof(CombinedMilestone_CombinedData_Deserialize_Prefix), BindingFlags.Public | BindingFlags.Static);
                            harmony.Patch(cmDes, prefix: new HarmonyMethod(pre) { prioritiy = Harmony.Priority.First });
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched CombinedMilestone.CombinedData.Deserialize with defensive bounds guard.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching CombinedMilestone.CombinedData.Deserialize: " + ex);
                }

                // 3g. Patch DataSerializer.ReadSharedType (Guard against corrupt type index crashes)
                try
                {
                    Type dsType = typeof(ColossalFramework.IO.DataSerializer);
                    MethodInfo rst1 = dsType.GetMethod("ReadSharedType", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(ColossalFramework.IO.DataSerializer.LegacyResolverHandler) }, null);
                    MethodInfo rst0 = dsType.GetMethod("ReadSharedType", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    MethodInfo pre1 = typeof(TransportLineCompatibility).GetMethod(nameof(DataSerializer_ReadSharedType_Prefix), BindingFlags.Public | BindingFlags.Static);
                    MethodInfo pre0 = typeof(TransportLineCompatibility).GetMethod(nameof(DataSerializer_ReadSharedType_NoArg_Prefix), BindingFlags.Public | BindingFlags.Static);
                    if (rst1 != null && pre1 != null)
                    {
                        harmony.Patch(rst1, prefix: new HarmonyMethod(pre1) { prioritiy = Harmony.Priority.First });
                    }
                    if (rst0 != null && pre0 != null)
                    {
                        harmony.Patch(rst0, prefix: new HarmonyMethod(pre0) { prioritiy = Harmony.Priority.First });
                    }
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched DataSerializer.ReadSharedType with bounds exception guard.");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching DataSerializer.ReadSharedType: " + ex);
                }

                // 3i. Patch DebugLogHandler.LogFormat & LogException (Universal UTF-16 Log Armor against P/Invoke ArgumentException crashes)
                try
                {
                    Type dlhType = typeof(UnityEngine.Debug).Assembly.GetType("UnityEngine.DebugLogHandler");
                    if (dlhType != null)
                    {
                        MethodInfo logFormat = dlhType.GetMethod("LogFormat", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        MethodInfo preFormat = typeof(TransportLineCompatibility).GetMethod(nameof(DebugLogHandler_LogFormat_Prefix), BindingFlags.Public | BindingFlags.Static);
                        if (logFormat != null && preFormat != null)
                        {
                            harmony.Patch(logFormat, prefix: new HarmonyMethod(preFormat) { prioritiy = Harmony.Priority.First });
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched DebugLogHandler.LogFormat with Universal UTF-16 Log Armor (Priority.First).");
                        }

                        MethodInfo logEx = dlhType.GetMethod("LogException", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        MethodInfo preEx = typeof(TransportLineCompatibility).GetMethod(nameof(DebugLogHandler_LogException_Prefix), BindingFlags.Public | BindingFlags.Static);
                        if (logEx != null && preEx != null)
                        {
                            harmony.Patch(logEx, prefix: new HarmonyMethod(preEx) { prioritiy = Harmony.Priority.First });
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched DebugLogHandler.LogException with Safe Log Armor (Priority.First).");
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching DebugLogHandler: " + ex);
                }

                // 3k. Patch VehicleManager.Data.AfterDeserialize (Deadlock-Free Orphan/Broken Vehicle Neutralizer)
                try
                {
                    Type vmDataType = typeof(VehicleManager).GetNestedType("Data", BindingFlags.Public | BindingFlags.NonPublic);
                    if (vmDataType != null)
                    {
                        MethodInfo vmAfterDes = vmDataType.GetMethod("AfterDeserialize", BindingFlags.Public | BindingFlags.Instance);
                        MethodInfo post = typeof(TransportLineCompatibility).GetMethod(nameof(VehicleManager_Data_AfterDeserialize_Postfix), BindingFlags.Public | BindingFlags.Static);
                        if (vmAfterDes != null && post != null)
                        {
                            harmony.Patch(vmAfterDes, postfix: new HarmonyMethod(post));
                            UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched VehicleManager.Data.AfterDeserialize with Deadlock-Free Orphan Vehicle Neutralizer.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching VehicleManager.Data.AfterDeserialize: " + ex);
                }

                // Do NOT patch external manager Data.Serialize methods (TreeManager, VehicleManager, BuildingManager,
                // CitizenManager, DistrictManager, PathManager) with Harmony! Mods like TreeUnlimiter (Unlimited Trees),
                // More Vehicles, and More Buildings detour these methods using RedirectionHelper native jumps.
                // Applying Harmony hooks clobbers their detours and breaks custom serialization (causing EndDeserialize tag mismatch).

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

        private static FieldInfo s_fBuildIndexHistory = null;
        private static FieldInfo s_fManagers = null;
        private static bool s_simFieldsReflected = false;

        private static void EnsureSimulationFields()
        {
            if (s_simFieldsReflected) return;
            try
            {
                s_fBuildIndexHistory = typeof(SimulationManager).GetField("m_buildIndexHistory", BindingFlags.NonPublic | BindingFlags.Instance);
                s_fManagers = typeof(SimulationManager).GetField("m_managers", BindingFlags.NonPublic | BindingFlags.Static);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[MoreNetworkCapacityRenew] Error reflecting SimulationManager fields: " + ex);
            }
            finally
            {
                s_simFieldsReflected = true;
            }
        }

        private static uint[] GetBuildIndexHistory(SimulationManager sim)
        {
            EnsureSimulationFields();
            if (s_fBuildIndexHistory != null && sim != null)
            {
                return s_fBuildIndexHistory.GetValue(sim) as uint[];
            }
            return null;
        }

        private static FastList<ISimulationManager> GetSimulationManagers()
        {
            EnsureSimulationFields();
            if (s_fManagers != null)
            {
                return s_fManagers.GetValue(null) as FastList<ISimulationManager>;
            }
            return null;
        }

        private static void WriteSerializableDataStorageSafely(ColossalFramework.IO.DataSerializer s, SimulationManager sim)
        {
            if (sim == null || sim.m_serializableDataStorage == null)
            {
                s.WriteUInt32(0u);
                return;
            }

            var storage = sim.m_serializableDataStorage;
            bool lockTaken = false;
            try
            {
                SaveSimulationDataSafetyPatch.LogSaveDebug("m_serializableDataStorage: Acquiring lock (timeout 2000ms)...");
                lockTaken = System.Threading.Monitor.TryEnter(storage, 2000);
                if (!lockTaken)
                {
                    string warn = "[MoreNetworkCapacityRenew] Notice: Monitor.TryEnter on m_serializableDataStorage timed out after 2000ms. Snapshotting safely to prevent save freeze!";
                    SaveSimulationDataSafetyPatch.LogSaveDebug(warn);
                    UnityEngine.Debug.LogWarning(warn);
                }
                else
                {
                    SaveSimulationDataSafetyPatch.LogSaveDebug("m_serializableDataStorage: Lock acquired successfully.");
                }

                KeyValuePair<string, byte[]>[] entries = null;
                try
                {
                    entries = new KeyValuePair<string, byte[]>[storage.Count];
                    int idx = 0;
                    foreach (var kvp in storage)
                    {
                        if (idx < entries.Length)
                        {
                            entries[idx++] = kvp;
                        }
                    }
                }
                catch (Exception ex)
                {
                    SaveSimulationDataSafetyPatch.LogSaveDebug("Notice: direct storage enumeration threw: " + ex.Message + ". Falling back to Enumerable snapshot.");
                    entries = System.Linq.Enumerable.ToArray(storage);
                }

                int count = entries != null ? entries.Length : 0;
                s.WriteUInt32((uint)count);
                for (int i = 0; i < count; i++)
                {
                    s.WriteUniqueString(entries[i].Key ?? string.Empty);
                    s.WriteByteArray(entries[i].Value ?? new byte[0]);
                }

                string msg = string.Format("Successfully serialized {0} custom mod data items from m_serializableDataStorage.", count);
                SaveSimulationDataSafetyPatch.LogSaveDebug(msg);
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] " + msg);
            }
            finally
            {
                if (lockTaken)
                {
                    System.Threading.Monitor.Exit(storage);
                    SaveSimulationDataSafetyPatch.LogSaveDebug("m_serializableDataStorage: Lock released.");
                }
            }
        }

        public static bool SimulationManager_Data_Serialize_Prefix(object __instance, ColossalFramework.IO.DataSerializer s)
        {
            try
            {
                SaveSimulationDataSafetyPatch.LogSaveDebug("SimulationManager.Data.Serialize Prefix: Running PreSaveSanitization...");
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SimulationManager.Data.Serialize Prefix: Running PreSaveSanitization...");
                PreSaveSanitization();
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] PreSaveSanitization completed cleanly.");
                SaveSimulationDataSafetyPatch.LogSaveDebug("PreSaveSanitization completed cleanly.");

                LoadingManager lm = Singleton<LoadingManager>.instance;
                if (lm != null && lm.m_loadingProfilerSimulation != null)
                {
                    lm.m_loadingProfilerSimulation.BeginSerialize(s, "SimulationManager");
                }

                SimulationManager sim = Singleton<SimulationManager>.instance;
                if (sim != null)
                {
                    SaveSimulationDataSafetyPatch.LogSaveDebug("Invoking ISerializableDataExtension.OnSaveData()...");
                    if (sim.m_SerializableDataWrapper != null)
                    {
                        sim.m_SerializableDataWrapper.OnSaveData();
                    }
                    SaveSimulationDataSafetyPatch.LogSaveDebug("OnSaveData() completed.");

                    s.WriteUInt32(sim.m_currentTickIndex);
                    s.WriteUInt32(sim.m_currentFrameIndex);
                    s.WriteUInt32(sim.m_currentBuildIndex);
                    s.WriteULong64(sim.m_randomizer.seed);
                    s.WriteUInt32(sim.m_dayTimeOffsetFrames);

                    uint[] buildIndexHistory = GetBuildIndexHistory(sim);
                    if (buildIndexHistory != null)
                    {
                        for (int i = 0; i < buildIndexHistory.Length; i++)
                        {
                            s.WriteUInt32(buildIndexHistory[i]);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < 1000; i++)
                        {
                            s.WriteUInt32(0);
                        }
                    }

                    SaveSimulationDataSafetyPatch.LogSaveDebug("Gathering simulation manager data containers...");
                    FastList<ColossalFramework.IO.IDataContainer> dataContainers = new FastList<ColossalFramework.IO.IDataContainer>();
                    FastList<ISimulationManager> managers = GetSimulationManagers();
                    if (managers != null)
                    {
                        for (int j = 0; j < managers.m_size; j++)
                        {
                            if (managers.m_buffer[j] != null)
                            {
                                managers.m_buffer[j].GetData(dataContainers);
                            }
                        }
                    }
                    SaveSimulationDataSafetyPatch.LogSaveDebug(string.Format("Writing {0} simulation manager data containers...", dataContainers.m_size));
                    s.WriteObjectArray<ColossalFramework.IO.IDataContainer>(dataContainers.ToArray());
                    SaveSimulationDataSafetyPatch.LogSaveDebug("Manager data containers serialized successfully.");

                    // Safely write m_serializableDataStorage without infinite spin loop
                    WriteSerializableDataStorageSafely(s, sim);
                }

                if (lm != null && lm.m_loadingProfilerSimulation != null)
                {
                    lm.m_loadingProfilerSimulation.EndSerialize(s, "SimulationManager");
                }

                SaveSimulationDataSafetyPatch.LogSaveDebug("SimulationManager.Data.Serialize Prefix: COMPLETED CLEANLY!");
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SimulationManager.Data.Serialize Prefix: COMPLETED CLEANLY!");
                return false; // Skip original method containing buggy infinite spin loop
            }
            catch (Exception ex)
            {
                SaveSimulationDataSafetyPatch.LogSaveDebug("CRITICAL: Exception in SimulationManager_Data_Serialize_Prefix: " + ex);
                UnityEngine.Debug.LogError("[MoreNetworkCapacityRenew] CRITICAL: Exception in SimulationManager_Data_Serialize_Prefix: " + ex);
                return true; // Fallback to original method if something unexpected failed
            }
        }

        public static void DataSerializer_WriteUInt24_Prefix(ref uint value)
        {
            if (value > 16777215u)
            {
                value = 16777215u;
            }
        }

        public static void SafeWaitForAllPaths(object pf)
        {
            if (pf == null) return;
            try
            {
                Type t = pf.GetType();
                FieldInfo fLock = t.GetField("queueLock_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                               ?? t.GetField("m_queueLock", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo fFirst = t.GetField("queueFirst_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                ?? t.GetField("m_queueFirst", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo fCalc = t.GetField("calculating_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                               ?? t.GetField("m_calculating", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo fTerm = t.GetField("terminated_", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                               ?? t.GetField("m_terminated", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (fLock == null) return;
                object lockObj = fLock.GetValue(pf);
                if (lockObj == null) return;

                if (!System.Threading.Monitor.TryEnter(lockObj, 2000))
                {
                    SaveSimulationDataSafetyPatch.LogSaveDebug("SafeWaitForAllPaths: lock timeout on " + t.Name + ", skipping wait safely.");
                    return;
                }

                try
                {
                    int loop = 0;
                    while (loop++ < 15)
                    {
                        uint qFirst = fFirst != null ? Convert.ToUInt32(fFirst.GetValue(pf)) : 0;
                        uint calc = fCalc != null ? Convert.ToUInt32(fCalc.GetValue(pf)) : 0;
                        bool term = fTerm != null && Convert.ToBoolean(fTerm.GetValue(pf));

                        if (term || (qFirst == 0 && calc == 0))
                        {
                            break;
                        }

                        System.Threading.Monitor.Wait(lockObj, 100);
                    }
                }
                finally
                {
                    System.Threading.Monitor.Exit(lockObj);
                }
            }
            catch (Exception ex)
            {
                SaveSimulationDataSafetyPatch.LogSaveDebug("SafeWaitForAllPaths exception: " + ex);
            }
        }

        public static bool PathManager_WaitForAllPaths_Prefix(PathManager __instance)
        {
            try
            {
                SaveSimulationDataSafetyPatch.LogSaveDebug("PathManager.WaitForAllPaths: BEGIN safe wait...");
                if (__instance != null)
                {
                    FieldInfo fPfs = typeof(PathManager).GetField("m_pathfinds", BindingFlags.NonPublic | BindingFlags.Instance);
                    Array pfs = fPfs != null ? fPfs.GetValue(__instance) as Array : null;
                    if (pfs != null)
                    {
                        for (int i = 0; i < pfs.Length; i++)
                        {
                            SafeWaitForAllPaths(pfs.GetValue(i));
                        }
                    }
                }

                Assembly tmpe = TMPECompatibility.GetTMPEAssembly();
                if (tmpe != null)
                {
                    Type cpmType = tmpe.GetType("TrafficManager.Custom.PathFinding.CustomPathManager");
                    if (cpmType != null)
                    {
                        FieldInfo fInst = cpmType.GetField("_instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        object cpm = fInst != null ? fInst.GetValue(null) : null;
                        if (cpm != null)
                        {
                            FieldInfo fPfs = cpmType.GetField("_replacementPathFinds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            Array pfs = fPfs != null ? fPfs.GetValue(cpm) as Array : null;
                            if (pfs != null)
                            {
                                for (int i = 0; i < pfs.Length; i++)
                                {
                                    SafeWaitForAllPaths(pfs.GetValue(i));
                                }
                            }
                        }
                    }
                }

                SaveSimulationDataSafetyPatch.LogSaveDebug("PathManager.WaitForAllPaths: COMPLETED CLEANLY!");
                return false; // Skip original unconstrained spin loop!
            }
            catch (Exception ex)
            {
                SaveSimulationDataSafetyPatch.LogSaveDebug("PathManager.WaitForAllPaths prefix exception: " + ex);
                return false;
            }
        }

        public static void PathManager_Data_Serialize_Prefix()
        {
            SaveSimulationDataSafetyPatch.LogSaveDebug("PathManager.Data.Serialize: BEGIN");
        }

        public static void PathManager_Data_Serialize_Postfix()
        {
            SaveSimulationDataSafetyPatch.LogSaveDebug("PathManager.Data.Serialize: COMPLETED CLEANLY!");
        }

        public static void VehicleManager_Data_Serialize_Prefix()
        {
            SaveSimulationDataSafetyPatch.LogSaveDebug("VehicleManager.Data.Serialize: BEGIN");
        }

        public static void VehicleManager_Data_Serialize_Postfix()
        {
            SaveSimulationDataSafetyPatch.LogSaveDebug("VehicleManager.Data.Serialize: COMPLETED CLEANLY!");
        }

        public static void TreeManager_Data_Serialize_Prefix()
        {
            SaveSimulationDataSafetyPatch.LogSaveDebug("TreeManager.Data.Serialize: BEGIN");
        }

        public static void TreeManager_Data_Serialize_Postfix()
        {
            SaveSimulationDataSafetyPatch.LogSaveDebug("TreeManager.Data.Serialize: COMPLETED CLEANLY!");
        }

        public static void TransportManager_Data_Serialize_Prefix(object __instance, ColossalFramework.IO.DataSerializer s)
        {
            try
            {
                SaveSimulationDataSafetyPatch.LogSaveDebug("TransportManager.Data.Serialize: BEGIN");
                SanitizeTransportLines();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] TransportManager_Data_Serialize_Prefix error: " + ex);
            }
        }

        public static void TransportManager_Data_Serialize_Postfix()
        {
            SaveSimulationDataSafetyPatch.LogSaveDebug("TransportManager.Data.Serialize: COMPLETED CLEANLY!");
        }

        public static void BuildingManager_Data_Serialize_Prefix()
        {
            SaveSimulationDataSafetyPatch.LogSaveDebug("BuildingManager.Data.Serialize: BEGIN");
        }

        public static void BuildingManager_Data_Serialize_Postfix()
        {
            SaveSimulationDataSafetyPatch.LogSaveDebug("BuildingManager.Data.Serialize: COMPLETED CLEANLY!");
        }

        public static void CitizenManager_Data_Serialize_Prefix()
        {
            SaveSimulationDataSafetyPatch.LogSaveDebug("CitizenManager.Data.Serialize: BEGIN");
        }

        public static void CitizenManager_Data_Serialize_Postfix()
        {
            SaveSimulationDataSafetyPatch.LogSaveDebug("CitizenManager.Data.Serialize: COMPLETED CLEANLY!");
        }

        public static void DistrictManager_Data_Serialize_Prefix()
        {
            SaveSimulationDataSafetyPatch.LogSaveDebug("DistrictManager.Data.Serialize: BEGIN");
        }

        public static void DistrictManager_Data_Serialize_Postfix()
        {
            SaveSimulationDataSafetyPatch.LogSaveDebug("DistrictManager.Data.Serialize: COMPLETED CLEANLY!");
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
            try { ThirdPartyCompatibility.EnsureMorePathUnitsPatched(); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] PreSave EnsureMorePathUnits error: " + ex); }
            try { ThirdPartyCompatibility.SynchronizeMorePathUnits("PreSaveSanitization"); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] PreSave MorePathUnits sync error: " + ex); }
            try { ThirdPartyCompatibility.EnsureImpatientCommutersBuffers("PreSaveSanitization"); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] PreSave ImpatientCommuters sync error: " + ex); }
            try { ArrayGuardian.ExpandAllArrays(); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] PreSave ArrayGuardian error: " + ex); }
            try { SanitizeDistricts(); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeDistricts error: " + ex); }
            try { SanitizeTransportLines(); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeTransportLines error: " + ex); }
            try { SanitizeVehicles(); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeVehicles error: " + ex); }
            try { SanitizeBuildings(); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeBuildings error: " + ex); }
            try { SanitizeNetElements(); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeNetElements error: " + ex); }
            try { SanitizeProps(); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeProps error: " + ex); }
            try { SanitizeTrees(); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeTrees error: " + ex); }
            try { SanitizeCitizenInstances(); } catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeCitizenInstances error: " + ex); }
            try
            {
                Assembly tmpe = TMPECompatibility.GetTMPEAssembly();
                if (tmpe != null) TMPECompatibility.SanitizeAndRepairTrafficLightSimulations(tmpe, false);
            }
            catch (Exception ex) { UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] PreSave TMPE sanitization error: " + ex); }
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
                            if (tm.m_lines.m_buffer[i].m_stops >= MNCConstants.CORE_NET_CAPACITY)
                            {
                                tm.m_lines.m_buffer[i].m_stops = 0;
                            }
                            if (tm.m_lines.m_buffer[i].m_ticketPrice > 50000)
                            {
                                tm.m_lines.m_buffer[i].m_ticketPrice = 0;
                                sanitized++;
                            }
                            if (float.IsNaN(tm.m_lines.m_buffer[i].m_totalLength) || float.IsInfinity(tm.m_lines.m_buffer[i].m_totalLength) || tm.m_lines.m_buffer[i].m_totalLength < 0f)
                            {
                                tm.m_lines.m_buffer[i].m_totalLength = 0f;
                                sanitized++;
                            }
                            if (vBufLen > 0)
                            {
                                ushort curVehicle = tm.m_lines.m_buffer[i].m_vehicles;
                                ushort prevVehicle = 0;
                                HashSet<ushort> visitedVehicles = new HashSet<ushort>();
                                while (curVehicle != 0)
                                {
                                    if (curVehicle >= vBufLen ||
                                        (vm.m_vehicles.m_buffer[curVehicle].m_flags & Vehicle.Flags.Created) == 0 ||
                                        !visitedVehicles.Add(curVehicle))
                                    {
                                        if (prevVehicle != 0)
                                        {
                                            vm.m_vehicles.m_buffer[prevVehicle].m_nextLineVehicle = 0;
                                        }
                                        else
                                        {
                                            tm.m_lines.m_buffer[i].m_vehicles = 0;
                                        }
                                        sanitized++;
                                        break;
                                    }
                                    prevVehicle = curVehicle;
                                    curVehicle = vm.m_vehicles.m_buffer[curVehicle].m_nextLineVehicle;
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
                                while (vHead != 0 && vHead < vm.m_vehicles.m_buffer.Length && ++vCount < vm.m_vehicles.m_buffer.Length)
                                {
                                    ushort nextV = vm.m_vehicles.m_buffer[vHead].m_nextLineVehicle;
                                    // In-memory neutralization without calling ReleaseVehicle (prevents cross-thread deadlock)
                                    vm.m_vehicles.m_buffer[vHead].m_flags = 0;
                                    vm.m_vehicles.m_buffer[vHead].m_infoIndex = 0;
                                    vm.m_vehicles.m_buffer[vHead].m_leadingVehicle = 0;
                                    vm.m_vehicles.m_buffer[vHead].m_trailingVehicle = 0;
                                    vm.m_vehicles.m_buffer[vHead].m_nextLineVehicle = 0;
                                    vm.m_vehicles.m_buffer[vHead].m_transportLine = 0;
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
                        else if (stops >= MNCConstants.CORE_NET_CAPACITY)
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

        public static void SimulationManager_Data_Deserialize_Prefix()
        {
            try
            {
                ThirdPartyCompatibility.EnsureMorePathUnitsPatched();
                ThirdPartyCompatibility.SynchronizeMorePathUnits("SimulationManager.Data.Deserialize.Prefix");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SimulationManager_Data_Deserialize_Prefix error: " + ex);
            }
        }

        public static void CitizenManager_Data_Deserialize_Prefix(CitizenManager.Data __instance, ColossalFramework.IO.DataSerializer s)
        {
            try
            {
                var cm = Singleton<CitizenManager>.instance;
                if (cm == null) return;

                var sm = Singleton<SimulationManager>.instance;
                bool isExpandedSave = sm != null && sm.m_metaData != null && sm.m_metaData.m_modOverride != null &&
                    sm.m_metaData.m_modOverride.TryGetValue("User/More CitizenUnits x2", out bool cuExp) && cuExp;

                bool hasMoreCitizenUnitsMod = false;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "MoreCitizenUnits")
                    {
                        hasMoreCitizenUnitsMod = true;
                        break;
                    }
                }

                // ONLY expand if this save was saved with More CitizenUnits x2 or mod is loaded in AppDomain
                if (isExpandedSave || hasMoreCitizenUnitsMod)
                {
                    const int EXPANDED_CITIZEN_UNITS = 1048576;
                    if (sm != null && sm.m_metaData != null)
                    {
                        if (sm.m_metaData.m_modOverride == null)
                        {
                            sm.m_metaData.m_modOverride = new Dictionary<string, bool>();
                        }
                        sm.m_metaData.m_modOverride["User/More CitizenUnits x2"] = true;
                        sm.m_metaData.m_modOverride["User/MoreNetworkCapacity/64K-v1"] = true;
                    }

                    if (cm.m_units == null || cm.m_units.m_buffer == null || cm.m_units.m_buffer.Length < EXPANDED_CITIZEN_UNITS)
                    {
                        cm.m_units = new Array32<CitizenUnit>((uint)EXPANDED_CITIZEN_UNITS);
                        cm.m_units.CreateItem(out _); // Reserve item 0
                        UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Universal Buffer Guard: Pre-expanded CitizenManager.m_units to {0} slots ✓", EXPANDED_CITIZEN_UNITS));
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] CitizenManager_Data_Deserialize_Prefix error: " + ex);
            }
        }

        public static bool PathManager_Data_Deserialize_Prefix(PathManager.Data __instance, ColossalFramework.IO.DataSerializer s)
        {
            try
            {
                ThirdPartyCompatibility.EnsureMorePathUnitsPatched();
                ThirdPartyCompatibility.SynchronizeMorePathUnits("PathManager.Data.Deserialize.Prefix");

                var pm = Singleton<PathManager>.instance;
                if (pm == null) return true;

                const int memoryUnitsCount = 524288;
                if (pm.m_pathUnits == null || pm.m_pathUnits.m_buffer == null || pm.m_pathUnits.m_buffer.Length < memoryUnitsCount)
                {
                    pm.m_pathUnits = new Array32<PathUnit>((uint)memoryUnitsCount);
                    pm.m_pathUnits.CreateItem(out _);
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Universal Buffer Guard: Pre-expanded PathUnit buffer to {0}.", memoryUnitsCount));
                }

                var smInst = Singleton<SimulationManager>.instance;
                if (smInst != null && smInst.m_metaData != null)
                {
                    if (smInst.m_metaData.m_modOverride == null)
                    {
                        smInst.m_metaData.m_modOverride = new Dictionary<string, bool>();
                    }
                    smInst.m_metaData.m_modOverride["User/More PathUnits x2"] = true;
                    smInst.m_metaData.m_modOverride["User/MoreNetworkCapacity/64K-v1"] = true;
                }

                // If MorePathUnits mod is present, let it run natively without interference!
                bool hasMorePathUnitsMod = false;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "MorePathUnits")
                    {
                        hasMorePathUnitsMod = true;
                        break;
                    }
                }

                if (hasMorePathUnitsMod)
                {
                    return true; // Let MorePathUnits prefix/transpiler/postfix execute!
                }

                try
                {
                    LoadingManager.instance.m_loadingProfilerSimulation.BeginDeserialize(s, "PathManager");
                }
                catch {}

                pm.WaitForAllPaths();

                bool isExpSave = ThirdPartyCompatibility.IsPathUnitsExpandedSave();
                int saveUnitsCount = isExpSave ? 524288 : 262144;
                PathUnit[] buffer = pm.m_pathUnits.m_buffer;
                int length = saveUnitsCount;
                pm.m_pathUnits.ClearUnused();

                uint version = s.version;
                if (version < 123u)
                {
                    length = 131072;
                }

                if (version >= 121011u)
                {
                    var uShort = ColossalFramework.IO.EncodedArray.UShort.BeginRead(s);
                    for (int i = 1; i < length; i++)
                    {
                        buffer[i].m_simulationFlags = (PathUnit.SimulationFlags)uShort.Read();
                    }
                    uShort.EndRead();
                }
                else if (version >= 79u)
                {
                    var uByte = ColossalFramework.IO.EncodedArray.Byte.BeginRead(s);
                    for (int i = 1; i < length; i++)
                    {
                        buffer[i].m_simulationFlags = (PathUnit.SimulationFlags)uByte.Read();
                    }
                    uByte.EndRead();
                }
                else
                {
                    for (int i = 1; i < length; i++)
                    {
                        buffer[i].m_simulationFlags = 0;
                    }
                }

                int activeCount = 0;
                for (int j = 1; j < length; j++)
                {
                    buffer[j].m_referenceCount = 0;
                    if (buffer[j].m_simulationFlags != 0)
                    {
                        buffer[j].m_pathFindFlags = (byte)s.ReadUInt8();
                        if (version >= 49u)
                        {
                            buffer[j].m_laneTypes = (byte)s.ReadUInt8();
                        }
                        else
                        {
                            buffer[j].m_laneTypes = 3;
                        }

                        if (version >= 113001u)
                        {
                            buffer[j].m_vehicleTypes = s.ReadUInt32();
                        }
                        else if (version >= 311u)
                        {
                            buffer[j].m_vehicleTypes = (uint)s.ReadUInt16();
                        }
                        else
                        {
                            buffer[j].m_vehicleTypes = (uint)s.ReadUInt8();
                        }

                        buffer[j].m_positionCount = (byte)s.ReadUInt8();
                        buffer[j].m_nextPathUnit = s.ReadUInt24();
                        buffer[j].m_buildIndex = s.ReadUInt32();
                        buffer[j].m_length = s.ReadFloat();

                        if (version >= 111012u || (version >= 110026u && version < 111000u))
                        {
                            buffer[j].m_speed = (byte)s.ReadUInt8();
                        }
                        else
                        {
                            buffer[j].m_speed = 100;
                        }

                        byte posCount = buffer[j].m_positionCount;
                        for (int k = 0; k < posCount; k++)
                        {
                            PathUnit.Position pos;
                            pos.m_segment = (ushort)s.ReadUInt16();
                            pos.m_offset = (byte)s.ReadUInt8();
                            pos.m_lane = (byte)s.ReadUInt8();
                            buffer[j].SetPosition(k, pos);
                        }
                        activeCount++;
                    }
                    else
                    {
                        buffer[j].m_pathFindFlags = 0;
                        buffer[j].m_laneTypes = 0;
                        buffer[j].m_vehicleTypes = 0;
                        buffer[j].m_vehicleCategories = 0L;
                        buffer[j].m_positionCount = 0;
                        buffer[j].m_nextPathUnit = 0;
                        buffer[j].m_buildIndex = 0;
                        buffer[j].m_length = 0f;
                        buffer[j].m_speed = 0;
                        pm.m_pathUnits.ReleaseItem((uint)j);
                    }
                }

                // Initialize and release all buffer items beyond what was in the save
                for (int rem = length; rem < buffer.Length; rem++)
                {
                    buffer[rem].m_referenceCount = 0;
                    buffer[rem].m_simulationFlags = 0;
                    buffer[rem].m_pathFindFlags = 0;
                    buffer[rem].m_laneTypes = 0;
                    buffer[rem].m_vehicleTypes = 0;
                    buffer[rem].m_vehicleCategories = 0L;
                    buffer[rem].m_positionCount = 0;
                    buffer[rem].m_nextPathUnit = 0;
                    buffer[rem].m_buildIndex = 0;
                    buffer[rem].m_length = 0f;
                    buffer[rem].m_speed = 0;
                    pm.m_pathUnits.ReleaseItem((uint)rem);
                }

                // Align stream to EndDeserialize tag "PathManager"
                try
                {
                    var fiStream = typeof(ColossalFramework.IO.DataSerializer).GetField("m_stream", BindingFlags.NonPublic | BindingFlags.Instance);
                    var fiStrings = typeof(ColossalFramework.IO.DataSerializer).GetField("m_stringList", BindingFlags.NonPublic | BindingFlags.Instance);
                    Stream st = fiStream != null ? (Stream)fiStream.GetValue(s) : null;
                    List<string> strList = fiStrings != null ? (List<string>)fiStrings.GetValue(s) : null;

                    if (st != null && st.CanSeek && strList != null)
                    {
                        int pmTagIdx = strList.IndexOf("PathManager");
                        if (pmTagIdx >= 0)
                        {
                            byte b1 = (byte)(pmTagIdx >> 8);
                            byte b2 = (byte)(pmTagIdx & 0xFF);

                            int extraUnits = 0;
                            while (st.Position < st.Length - 2 && extraUnits <= 500)
                            {
                                long chkPos = st.Position;
                                int p1 = st.ReadByte();
                                int p2 = st.ReadByte();
                                st.Seek(chkPos, SeekOrigin.Begin);

                                if (p1 == b1 && p2 == b2)
                                {
                                    break;
                                }

                                s.ReadUInt8(); // pathFindFlags
                                if (version >= 49u) s.ReadUInt8(); // laneTypes
                                if (version >= 113001u) s.ReadUInt32();
                                else if (version >= 311u) s.ReadUInt16();
                                else s.ReadUInt8();

                                byte pc = (byte)s.ReadUInt8();
                                s.ReadUInt24();
                                s.ReadUInt32();
                                s.ReadFloat();
                                if (version >= 111012u || (version >= 110026u && version < 111000u)) s.ReadUInt8();
                                for (int k = 0; k < pc; k++)
                                {
                                    s.ReadUInt16();
                                    s.ReadUInt8();
                                    s.ReadUInt8();
                                }
                                extraUnits++;
                            }

                            if (extraUnits > 0)
                            {
                                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Aligned PathManager stream: read {0} extra units to end tag at pos {1}.", extraUnits, st.Position));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] PathManager stream alignment exception: " + ex);
                }

                try
                {
                    LoadingManager.instance.m_loadingProfilerSimulation.EndDeserialize(s, "PathManager");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] Guarded LoadingProfiler.EndDeserialize PathManager: " + ex.Message);
                }

                smInst = Singleton<SimulationManager>.instance;
                if (smInst != null && smInst.m_metaData != null)
                {
                    if (smInst.m_metaData.m_modOverride == null)
                    {
                        smInst.m_metaData.m_modOverride = new Dictionary<string, bool>();
                    }
                    smInst.m_metaData.m_modOverride["User/More PathUnits x2"] = true;
                    smInst.m_metaData.m_modOverride["User/MoreNetworkCapacity/64K-v1"] = true;
                }

                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] PathManager.Data.Deserialize completed flawlessly: {0} units deserialized from save ({1} active), memory capacity {2}.", length, activeCount, buffer.Length));
                return false; // Skip original method and all transpilers!
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[MoreNetworkCapacityRenew] PathManager_Data_Deserialize_Prefix error: " + ex);
                return true;
            }
        }

        public static bool LoadingProfiler_BeginDeserialize_Prefix(LoadingProfiler __instance, ColossalFramework.IO.DataSerializer s, string name)
        {
            return SafeHandleDeserializeTag(__instance, s, name, isBegin: true);
        }

        public static bool LoadingProfiler_EndDeserialize_Prefix(LoadingProfiler __instance, ColossalFramework.IO.DataSerializer s, string name)
        {
            return SafeHandleDeserializeTag(__instance, s, name, isBegin: false);
        }

        private static bool SafeHandleDeserializeTag(LoadingProfiler profiler, ColossalFramework.IO.DataSerializer s, string name, bool isBegin)
        {
            try
            {
                if (profiler != null)
                {
                    try
                    {
                        FieldInfo fEvents = typeof(LoadingProfiler).GetField("m_events", BindingFlags.NonPublic | BindingFlags.Instance);
                        FieldInfo fStopwatch = typeof(LoadingProfiler).GetField("m_stopwatch", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (fEvents != null && fStopwatch != null)
                        {
                            var stopwatch = fStopwatch.GetValue(profiler) as System.Diagnostics.Stopwatch;
                            long elapsed = stopwatch != null ? stopwatch.ElapsedMilliseconds : 0L;
                            var events = fEvents.GetValue(profiler);
                            if (events != null)
                            {
                                MethodInfo mAdd = events.GetType().GetMethod("Add", BindingFlags.Public | BindingFlags.Instance);
                                Type evType = typeof(LoadingProfiler).GetNestedType("Event", BindingFlags.NonPublic | BindingFlags.Public);
                                if (evType != null && mAdd != null)
                                {
                                    object ev = Activator.CreateInstance(evType, new object[] { isBegin ? 6 : 7, isBegin ? name : null, elapsed });
                                    mAdd.Invoke(events, new object[] { ev });
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (s != null && s.version >= 130u)
                {
                    var fiStrings = typeof(ColossalFramework.IO.DataSerializer).GetField("m_stringList", BindingFlags.NonPublic | BindingFlags.Instance);
                    var fiStream = typeof(ColossalFramework.IO.DataSerializer).GetField("m_stream", BindingFlags.NonPublic | BindingFlags.Instance);
                    Stream st = fiStream != null ? (Stream)fiStream.GetValue(s) : null;
                    List<string> strList = fiStrings != null ? (List<string>)fiStrings.GetValue(s) : null;

                    if (st != null && st.CanSeek)
                    {
                        int tagIdx = strList != null ? strList.IndexOf(name) : -1;
                        if (tagIdx >= 0)
                        {
                            byte b1 = (byte)(tagIdx >> 8);
                            byte b2 = (byte)(tagIdx & 0xFF);

                            long curPos = st.Position;
                            int p1 = st.ReadByte();
                            int p2 = st.ReadByte();
                            st.Seek(curPos, SeekOrigin.Begin);

                            if (p1 == b1 && p2 == b2)
                            {
                                s.ReadSharedString(); // Clean match!
                                return false;
                            }

                            // Misaligned stream: search forward up to 8MB (covers up to 65k excess vehicles/citizens/units)
                            long foundPos = -1;
                            long maxSearch = Math.Min(st.Length - 2, curPos + 8 * 1024 * 1024);
                            for (long pos = curPos; pos < maxSearch; pos++)
                            {
                                int s1 = st.ReadByte();
                                if (s1 == b1)
                                {
                                    int s2 = st.ReadByte();
                                    if (s2 == b2)
                                    {
                                        foundPos = st.Position;
                                        break;
                                    }
                                    st.Seek(-1, SeekOrigin.Current);
                                }
                            }

                            if (foundPos != -1)
                            {
                                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Universal Stream Armor: Recovered and aligned {0}('{1}') from offset {2} to {3} ✓",
                                    isBegin ? "BeginDeserialize" : "EndDeserialize", name, curPos, foundPos));
                                return false;
                            }

                            st.Seek(curPos, SeekOrigin.Begin);
                        }

                        // Fallback: Read tag safely without throwing
                        try
                        {
                            string readTag = s.ReadSharedString();
                            UnityEngine.Debug.LogWarning(string.Format("[MoreNetworkCapacityRenew] Universal Stream Armor: Suppressed {0}('{1}') tag mismatch (read '{2}'). Proceeding safely ✓",
                                isBegin ? "BeginDeserialize" : "EndDeserialize", name, readTag));
                        }
                        catch { }
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning(string.Format("[MoreNetworkCapacityRenew] Universal Stream Armor: Guarded {0}('{1}') error: {2}",
                    isBegin ? "BeginDeserialize" : "EndDeserialize", name, ex.Message));
            }
            return false;
        }

        private static MilestoneInfo.Data[] SafeReadMilestoneDataArray(ColossalFramework.IO.DataSerializer s)
        {
            try
            {
                var fiStream = typeof(ColossalFramework.IO.DataSerializer).GetField("m_stream", BindingFlags.NonPublic | BindingFlags.Instance);
                Stream st = fiStream != null ? (Stream)fiStream.GetValue(s) : null;
                if (st == null) return null;

                int b = st.ReadByte();
                if (b < 0 || b == 255) return null;

                int b2 = st.ReadByte();
                int b3 = st.ReadByte();
                if (b2 < 0 || b3 < 0) return null;

                int count = (b << 16) | (b2 << 8) | b3;
                if (count > 5000 || count < 0)
                {
                    UnityEngine.Debug.LogWarning(string.Format("[MoreNetworkCapacityRenew] CombinedMilestone: Guarded excessive count {0}, capped to empty array.", count));
                    return new MilestoneInfo.Data[0];
                }

                var arr = new MilestoneInfo.Data[count];
                for (int i = 0; i < count; i++)
                {
                    arr[i] = s.ReadObject<MilestoneInfo.Data>();
                }
                return arr;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] SafeReadMilestoneDataArray error: " + ex.Message);
                return new MilestoneInfo.Data[0];
            }
        }

        public static bool CombinedMilestone_CombinedData_Deserialize_Prefix(object __instance, ColossalFramework.IO.DataSerializer s)
        {
            try
            {
                if (__instance == null || s == null) return true;

                var t = __instance.GetType();
                var fPassedCount = t.GetField("m_passedCount", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fProgress = t.GetField("m_progress", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fRequirePassed = t.GetField("m_requirePassed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fForbidPassed = t.GetField("m_forbidPassed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fStartFrame = t.GetField("m_startFrame", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                int passedCount = s.ReadInt32();
                long progress = s.ReadLong64();
                if (fPassedCount != null) fPassedCount.SetValue(__instance, passedCount);
                if (fProgress != null) fProgress.SetValue(__instance, progress);

                // Safe ReadObjectArray for m_requirePassed & m_forbidPassed (byte-accurate protocol)
                if (fRequirePassed != null) fRequirePassed.SetValue(__instance, SafeReadMilestoneDataArray(s) ?? new MilestoneInfo.Data[0]);
                if (fForbidPassed != null) fForbidPassed.SetValue(__instance, SafeReadMilestoneDataArray(s) ?? new MilestoneInfo.Data[0]);

                if (s.version >= 111003u && fStartFrame != null)
                {
                    fStartFrame.SetValue(__instance, s.ReadUInt32());
                }

                return false; // Skip original method
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] Guarded CombinedMilestone.CombinedData.Deserialize: " + ex.Message);
                try
                {
                    var t = __instance.GetType();
                    var fRequirePassed = t.GetField("m_requirePassed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var fForbidPassed = t.GetField("m_forbidPassed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fRequirePassed != null && fRequirePassed.GetValue(__instance) == null)
                    {
                        fRequirePassed.SetValue(__instance, new MilestoneInfo.Data[0]);
                    }
                    if (fForbidPassed != null && fForbidPassed.GetValue(__instance) == null)
                    {
                        fForbidPassed.SetValue(__instance, new MilestoneInfo.Data[0]);
                    }
                }
                catch {}
                return false;
            }
        }

        public static bool DataSerializer_ReadSharedType_Prefix(ColossalFramework.IO.DataSerializer __instance, ColossalFramework.IO.DataSerializer.LegacyResolverHandler customResolver, ref Type __result)
        {
            try
            {
                var fiStream = typeof(ColossalFramework.IO.DataSerializer).GetField("m_stream", BindingFlags.NonPublic | BindingFlags.Instance);
                var fiTypes = typeof(ColossalFramework.IO.DataSerializer).GetField("m_typeList", BindingFlags.NonPublic | BindingFlags.Instance);
                Stream st = fiStream != null ? (Stream)fiStream.GetValue(__instance) : null;
                List<Type> typeList = fiTypes != null ? (List<Type>)fiTypes.GetValue(__instance) : null;

                if (st == null || typeList == null) return true;

                int num = st.ReadByte();
                if (num == 255)
                {
                    __result = null;
                    return false;
                }
                if (num == 254)
                {
                    string typeName = __instance.ReadUniqueString();
                    Type type = null;
                    if (customResolver != null)
                    {
                        try { type = customResolver(typeName); } catch {}
                    }
                    if (type == null)
                    {
                        try { type = Type.GetType(typeName); } catch {}
                    }
                    typeList.Add(type);
                    __result = type;
                    return false;
                }

                int num2 = st.ReadByte();
                int index = (num << 8) | num2;
                if (index >= 0 && index < typeList.Count)
                {
                    __result = typeList[index];
                }
                else
                {
                    UnityEngine.Debug.LogWarning(string.Format("[MoreNetworkCapacityRenew] Guarded ReadSharedType: index {0} out of bounds (count={1}), returning null.", index, typeList.Count));
                    __result = null;
                }
                return false;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] DataSerializer_ReadSharedType_Prefix error: " + ex.Message);
                __result = null;
                return false;
            }
        }

        public static bool DataSerializer_ReadSharedType_NoArg_Prefix(ColossalFramework.IO.DataSerializer __instance, ref Type __result)
        {
            return DataSerializer_ReadSharedType_Prefix(__instance, null, ref __result);
        }

        public static void DataSerializer_ReadUniqueString_Postfix(ref string __result)
        {
            if (__result != null)
            {
                __result = Utf16Sanitizer.Sanitize(__result);
            }
        }

        private static MethodInfo s_internalLogMethod;
        private static MethodInfo s_internalLogExceptionMethod;

        public static bool DebugLogHandler_LogFormat_Prefix(UnityEngine.LogType logType, UnityEngine.Object context, ref string format, ref object[] args)
        {
            try
            {
                if (format != null) format = Utf16Sanitizer.Sanitize(format);
                if (args != null)
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (args[i] is string s)
                        {
                            args[i] = Utf16Sanitizer.Sanitize(s);
                        }
                    }
                }

                string text;
                try
                {
                    text = (args != null && args.Length > 0) ? string.Format(format, args) : format;
                }
                catch
                {
                    text = format ?? string.Empty;
                }
                text = Utf16Sanitizer.Sanitize(text);

                if (s_internalLogMethod == null)
                {
                    Type dlhType = typeof(UnityEngine.Debug).Assembly.GetType("UnityEngine.DebugLogHandler");
                    s_internalLogMethod = dlhType?.GetMethod("Internal_Log", BindingFlags.Static | BindingFlags.NonPublic);
                }

                try
                {
                    s_internalLogMethod?.Invoke(null, new object[] { logType, text, context });
                }
                catch {}

                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool DebugLogHandler_LogException_Prefix(Exception exception, UnityEngine.Object context)
        {
            try
            {
                if (s_internalLogExceptionMethod == null)
                {
                    Type dlhType = typeof(UnityEngine.Debug).Assembly.GetType("UnityEngine.DebugLogHandler");
                    s_internalLogExceptionMethod = dlhType?.GetMethod("Internal_LogException", BindingFlags.Static | BindingFlags.NonPublic);
                }

                try
                {
                    s_internalLogExceptionMethod?.Invoke(null, new object[] { exception, context });
                }
                catch {}

                return false;
            }
            catch
            {
                return false;
            }
        }

        public static void PrefabCollection_VehicleInfo_BindPrefabs_Prefix()
        {
            try
            {
                var fi = typeof(PrefabCollection<VehicleInfo>).GetField("m_simulationPrefabs", BindingFlags.NonPublic | BindingFlags.Static);
                if (fi == null) return;
                var simPrefabs = fi.GetValue(null);
                if (simPrefabs == null) return;
                var fiBuffer = simPrefabs.GetType().GetField("m_buffer", BindingFlags.Public | BindingFlags.Instance);
                var fiSize = simPrefabs.GetType().GetField("m_size", BindingFlags.Public | BindingFlags.Instance);
                if (fiBuffer == null || fiSize == null) return;
                Array buffer = fiBuffer.GetValue(simPrefabs) as Array;
                int size = (int)fiSize.GetValue(simPrefabs);
                if (buffer == null || size <= 0) return;

                Type elemType = buffer.GetType().GetElementType();
                var fiName = elemType.GetField("m_name", BindingFlags.Public | BindingFlags.Instance);
                if (fiName == null) return;

                for (int i = 0; i < size && i < buffer.Length; i++)
                {
                    object entry = buffer.GetValue(i);
                    if (entry != null)
                    {
                        string name = fiName.GetValue(entry) as string;
                        if (name != null)
                        {
                            string sanitized = Utf16Sanitizer.Sanitize(name);
                            if (!object.ReferenceEquals(name, sanitized))
                            {
                                fiName.SetValue(entry, sanitized);
                                buffer.SetValue(entry, i);
                            }
                        }
                    }
                }
            }
            catch {}
        }

        public static void VehicleManager_Data_AfterDeserialize_Postfix()
        {
            try
            {
                VehicleManager vm = Singleton<VehicleManager>.instance;
                if (vm == null || vm.m_vehicles == null || vm.m_vehicles.m_buffer == null) return;

                int neutralizedVehicles = 0;
                int neutralizedParked = 0;

                int prefabCount = PrefabCollection<VehicleInfo>.PrefabCount();
                bool[] validPrefabs = new bool[prefabCount];
                for (int p = 0; p < prefabCount; p++)
                {
                    try
                    {
                        validPrefabs[p] = (PrefabCollection<VehicleInfo>.GetPrefab((uint)p) != null);
                    }
                    catch
                    {
                        validPrefabs[p] = false;
                    }
                }

                int vLen = vm.m_vehicles.m_buffer.Length;
                for (int i = 1; i < vLen; i++)
                {
                    if (vm.m_vehicles.m_buffer[i].m_flags != 0)
                    {
                        ushort infoIndex = vm.m_vehicles.m_buffer[i].m_infoIndex;
                        if (infoIndex == 0 || infoIndex >= prefabCount || !validPrefabs[infoIndex])
                        {
                            // Safely neutralize orphan vehicle in-place WITHOUT cross-thread dispatch, locks, or overflow
                            vm.m_vehicles.m_buffer[i].m_flags = 0;
                            vm.m_vehicles.m_buffer[i].m_flags2 = 0;
                            vm.m_vehicles.m_buffer[i].m_infoIndex = 0;
                            vm.m_vehicles.m_buffer[i].m_leadingVehicle = 0;
                            vm.m_vehicles.m_buffer[i].m_trailingVehicle = 0;
                            vm.m_vehicles.m_buffer[i].m_cargoParent = 0;
                            vm.m_vehicles.m_buffer[i].m_firstCargo = 0;
                            vm.m_vehicles.m_buffer[i].m_nextCargo = 0;
                            vm.m_vehicles.m_buffer[i].m_nextGridVehicle = 0;
                            vm.m_vehicles.m_buffer[i].m_path = 0;
                            vm.m_vehicles.m_buffer[i].m_citizenUnits = 0;
                            vm.m_vehicles.m_buffer[i].m_targetBuilding = 0;
                            vm.m_vehicles.m_buffer[i].m_sourceBuilding = 0;
                            vm.m_vehicles.m_buffer[i].m_transportLine = 0;
                            neutralizedVehicles++;
                        }
                    }
                }

                if (vm.m_parkedVehicles != null && vm.m_parkedVehicles.m_buffer != null)
                {
                    int pLen = vm.m_parkedVehicles.m_buffer.Length;
                    for (int i = 1; i < pLen; i++)
                    {
                        if (vm.m_parkedVehicles.m_buffer[i].m_flags != 0)
                        {
                            ushort infoIndex = vm.m_parkedVehicles.m_buffer[i].m_infoIndex;
                            if (infoIndex == 0 || infoIndex >= prefabCount || !validPrefabs[infoIndex])
                            {
                                vm.m_parkedVehicles.m_buffer[i].m_flags = 0;
                                vm.m_parkedVehicles.m_buffer[i].m_infoIndex = 0;
                                vm.m_parkedVehicles.m_buffer[i].m_nextGridParked = 0;
                                vm.m_parkedVehicles.m_buffer[i].m_ownerCitizen = 0;
                                neutralizedParked++;
                            }
                        }
                    }
                }

                if (neutralizedVehicles > 0 || neutralizedParked > 0)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Orphan Vehicle Neutralizer: Safely cleared {0} vehicles and {1} parked vehicles with missing/corrupt prefabs (deadlock-free).", neutralizedVehicles, neutralizedParked));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] VehicleManager_Data_AfterDeserialize_Postfix error: " + ex);
            }
        }

        public static void SimulationMetaData_Merge_Prefix(SimulationMetaData __instance, SimulationMetaData source)
        {
            try
            {
                if (__instance == null || source == null) return;
                // Bidirectional merge to guarantee no key from savegame is EVER lost!
                if (__instance.m_modOverride != null && source.m_modOverride != null)
                {
                    foreach (var kvp in __instance.m_modOverride)
                    {
                        if (!source.m_modOverride.ContainsKey(kvp.Key))
                        {
                            source.m_modOverride[kvp.Key] = kvp.Value;
                        }
                    }
                    foreach (var kvp in source.m_modOverride)
                    {
                        if (!__instance.m_modOverride.ContainsKey(kvp.Key))
                        {
                            __instance.m_modOverride[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SimulationMetaData_Merge_Prefix error: " + ex);
            }
        }

        public static void SimulationMetaData_Merge_Postfix(SimulationMetaData __instance, SimulationMetaData source)
        {
            try
            {
                if (__instance != null)
                {
                    if (__instance.m_modOverride == null)
                    {
                        __instance.m_modOverride = new Dictionary<string, bool>();
                    }
                    __instance.m_modOverride["User/MoreNetworkCapacity/64K-v1"] = true;
                }
                ThirdPartyCompatibility.EnsureMorePathUnitsPatched();
                ThirdPartyCompatibility.SynchronizeMorePathUnits("SimulationMetaData.Merge.Postfix");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SimulationMetaData_Merge_Postfix error: " + ex);
            }
        }

        public static void LoadingManager_LoadSimulationData_Prefix(ColossalFramework.Packaging.Package.Asset asset, SimulationMetaData ngs)
        {
            try
            {
                RenewLoading.EnsureAllPatchesApplied();
                ThirdPartyCompatibility.EnsureMorePathUnitsPatched();
                ThirdPartyCompatibility.SynchronizeMorePathUnits("LoadingManager.LoadSimulationData.Prefix");
                // ONLY append if ngs already had an initialized modOverride dictionary!
                // If ngs.m_modOverride is null, LEAVE IT NULL so vanilla Merge does not wipe save metadata!
                if (ngs != null && ngs.m_modOverride != null)
                {
                    if (ThirdPartyCompatibility.IsPathUnitsExpandedSave(ngs))
                    {
                        ngs.m_modOverride["User/More PathUnits x2"] = true;
                    }
                    ngs.m_modOverride["User/MoreNetworkCapacity/64K-v1"] = true;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] LoadingManager_LoadSimulationData_Prefix error: " + ex);
            }
        }

         public static bool TransportLine_GetNextStop_Prefix(ushort stop, ref ushort __result)
        {
            if (stop == 0 || stop >= MNCConstants.CORE_NET_CAPACITY)
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

    // ========================================================================
    // ========================================================================
    // ECONOMY PROTECTION & REPAIR (v4.3.41)
    // ========================================================================
    /// <summary>
    /// Armor against corrupted economy data across all public transport (Train, Metro, Tram, Bus, etc.) & city services:
    /// 1. Universal FetchResource shield: Prevents negative maintenance/expenses from corrupting m_expenses and inflating city funds.
    /// 2. Universal AddResource & AddPrivateIncome shields: Prevents negative income/ticket prices from corrupting m_income.
    /// 3. Track/Road Maintenance shield: Protects PlayerNetAI.GetMaintenanceCost against NaN/Infinity/overflow from invalid nodes/segments.
    /// 4. Save Repair: Restores and sanitizes any existing negative entries in EconomyManager arrays (expenses, totalExpenses, income, totalIncome).
    /// </summary>
    public static class EconomyProtection
    {
        public static void Apply(HarmonyInstance harmony)
        {
            try
            {
                Type emType = typeof(EconomyManager);
                Type resType = typeof(EconomyManager.Resource);
                Type servType = typeof(ItemClass.Service);
                Type subType = typeof(ItemClass.SubService);
                Type levType = typeof(ItemClass.Level);
                Type taxType = typeof(DistrictPolicies.Taxation);

                // 1. Universal shield: Patch EconomyManager.FetchResource(Resource, int, Service, SubService, Level)
                MethodInfo fetchResource = emType.GetMethod(
                    "FetchResource",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new Type[] { resType, typeof(int), servType, subType, levType },
                    null);

                if (fetchResource != null)
                {
                    MethodInfo pre = typeof(EconomyProtection).GetMethod(nameof(EconomyManager_FetchResource_Prefix), BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(fetchResource, prefix: new HarmonyMethod(pre));
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched EconomyManager.FetchResource with non-negative guard.");
                }

                // 2. Universal shield: Patch EconomyManager.AddResource(Resource, int, Service, SubService, Level, Taxation)
                MethodInfo addResource = emType.GetMethod(
                    "AddResource",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new Type[] { resType, typeof(int), servType, subType, levType, taxType },
                    null);

                if (addResource != null)
                {
                    MethodInfo pre = typeof(EconomyProtection).GetMethod(nameof(EconomyManager_AddResource_Prefix), BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(addResource, prefix: new HarmonyMethod(pre));
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched EconomyManager.AddResource with non-negative guard.");
                }

                // 3. Shield: Patch EconomyManager.AddPrivateIncome(int, Service, SubService, Level, int)
                MethodInfo addPriv = emType.GetMethod(
                    "AddPrivateIncome",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(int), servType, subType, levType, typeof(int) },
                    null);

                if (addPriv != null)
                {
                    MethodInfo pre = typeof(EconomyProtection).GetMethod(nameof(EconomyManager_AddPrivateIncome_Prefix), BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(addPriv, prefix: new HarmonyMethod(pre));
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched EconomyManager.AddPrivateIncome with non-negative guard.");
                }

                // 4. Track & segment maintenance shield: Patch PlayerNetAI.GetMaintenanceCost(Vector3, Vector3)
                MethodInfo getMaintCost = typeof(PlayerNetAI).GetMethod(
                    "GetMaintenanceCost",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(Vector3), typeof(Vector3) },
                    null);

                if (getMaintCost != null)
                {
                    MethodInfo pre = typeof(EconomyProtection).GetMethod(nameof(PlayerNetAI_GetMaintenanceCost_Prefix), BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(getMaintCost, prefix: new HarmonyMethod(pre));
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched PlayerNetAI.GetMaintenanceCost with NaN/overflow safety.");
                }

                // 5. Investment deserialization shield: Protect against corrupted -1 ticks / ArgumentOutOfRangeException
                MethodInfo desInvest = emType.GetMethod("DeserializeInvestments", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (desInvest != null)
                {
                    MethodInfo transpiler = typeof(EconomyProtection).GetMethod(nameof(DeserializeInvestments_Transpiler), BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(desInvest, transpiler: new HarmonyMethod(transpiler));
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched EconomyManager.DeserializeInvestments with SafeCreateDateTime armor.");
                }

                // 6. EconomyManager.Data.Deserialize shield: Catch any unexpected investment exception to prevent load failure
                Type emDataType = emType.GetNestedType("Data", BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo dataDes = emDataType?.GetMethod("Deserialize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (dataDes != null)
                {
                    MethodInfo transpiler = typeof(EconomyProtection).GetMethod(nameof(EconomyManager_Data_Deserialize_Transpiler), BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(dataDes, transpiler: new HarmonyMethod(transpiler));
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched EconomyManager.Data.Deserialize with SafeDeserializeInvestments armor.");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] EconomyProtection.Apply error: " + ex);
            }
        }

        public static bool EconomyManager_FetchResource_Prefix(
            EconomyManager __instance,
            EconomyManager.Resource resource,
            ref int amount,
            ItemClass.Service service,
            ItemClass.SubService subService,
            ItemClass.Level level,
            ref int __result)
        {
            if (amount < 0)
            {
                // Expenditures and maintenance can NEVER be negative.
                // A negative amount corrupts m_expenses into negative and subtracts a negative number from m_cashAmount (skyrocketing city funds).
                amount = 0;
                __result = 0;
                return false;
            }
            return true;
        }

        public static bool EconomyManager_AddResource_Prefix(
            EconomyManager __instance,
            EconomyManager.Resource resource,
            ref int amount,
            ItemClass.Service service,
            ItemClass.SubService subService,
            ItemClass.Level level,
            DistrictPolicies.Taxation taxationPolicies,
            ref int __result)
        {
            if (amount < 0)
            {
                // Revenues, ticket fares, and income can NEVER be negative.
                amount = 0;
                __result = 0;
                return false;
            }
            return true;
        }

        public static bool EconomyManager_AddPrivateIncome_Prefix(
            EconomyManager __instance,
            ref int amount,
            ItemClass.Service service,
            ItemClass.SubService subService,
            ItemClass.Level level,
            int taxRate,
            ref int __result)
        {
            if (amount < 0)
            {
                amount = 0;
                __result = 0;
                return false;
            }
            return true;
        }

        public static bool PlayerNetAI_GetMaintenanceCost_Prefix(
            PlayerNetAI __instance,
            Vector3 startPos,
            Vector3 endPos,
            ref int __result)
        {
            if (float.IsNaN(startPos.x) || float.IsNaN(startPos.y) || float.IsNaN(startPos.z) ||
                float.IsNaN(endPos.x) || float.IsNaN(endPos.y) || float.IsNaN(endPos.z) ||
                float.IsInfinity(startPos.x) || float.IsInfinity(startPos.y) || float.IsInfinity(startPos.z) ||
                float.IsInfinity(endPos.x) || float.IsInfinity(endPos.y) || float.IsInfinity(endPos.z))
            {
                __result = 0;
                return false;
            }

            float len = ColossalFramework.Math.VectorUtils.LengthXZ(endPos - startPos);
            if (float.IsNaN(len) || float.IsInfinity(len) || len < 0f)
            {
                __result = 0;
                return false;
            }

            int num = Mathf.RoundToInt(len / 8f + 0.1f);
            if (num < 0) num = 0;

            long calc = (long)__instance.m_maintenanceCost * (long)num;
            if (calc < 0L || calc > (long)int.MaxValue)
            {
                __result = 0;
                return false;
            }

            int cost = (int)calc;
            if (Singleton<EconomyManager>.exists && Singleton<EconomyManager>.instance.m_EconomyWrapper != null && __instance.m_info != null && __instance.m_info.m_class != null)
            {
                Singleton<EconomyManager>.instance.m_EconomyWrapper.OnGetMaintenanceCost(
                    ref cost,
                    __instance.m_info.m_class.m_service,
                    __instance.m_info.m_class.m_subService,
                    __instance.m_info.m_class.m_level);
            }

            if (cost < 0) cost = 0;
            __result = cost;
            return false;
        }

        private static FieldInfo s_fExpenses = null;
        private static FieldInfo s_fTotalExpenses = null;
        private static FieldInfo s_fIncome = null;
        private static FieldInfo s_fTotalIncome = null;
        private static FieldInfo s_fPolicyExpenses = null;
        private static FieldInfo s_fLoanExpenses = null;
        private static bool s_econFieldsReflected = false;

        public static int SanitizeEconomy()
        {
            int repaired = 0;
            try
            {
                if (!Singleton<EconomyManager>.exists) return 0;
                EconomyManager em = Singleton<EconomyManager>.instance;
                if (em == null) return 0;

                if (!s_econFieldsReflected)
                {
                    BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                    s_fExpenses = typeof(EconomyManager).GetField("m_expenses", flags);
                    s_fTotalExpenses = typeof(EconomyManager).GetField("m_totalExpenses", flags);
                    s_fIncome = typeof(EconomyManager).GetField("m_income", flags);
                    s_fTotalIncome = typeof(EconomyManager).GetField("m_totalIncome", flags);
                    s_fPolicyExpenses = typeof(EconomyManager).GetField("m_policyExpenses", flags);
                    s_fLoanExpenses = typeof(EconomyManager).GetField("m_loanExpenses", flags);
                    s_econFieldsReflected = true;
                }

                repaired += SanitizeArray(s_fExpenses, em);
                repaired += SanitizeArray(s_fTotalExpenses, em);
                repaired += SanitizeArray(s_fIncome, em);
                repaired += SanitizeArray(s_fTotalIncome, em);
                repaired += SanitizeArray(s_fPolicyExpenses, em);
                repaired += SanitizeArray(s_fLoanExpenses, em);

                if (repaired > 0)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] SanitizeEconomy: Repaired {0} negative economic entries in EconomyManager.", repaired));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SanitizeEconomy error: " + ex);
            }
            return repaired;
        }

        private static int SanitizeArray(FieldInfo fi, EconomyManager em)
        {
            if (fi == null || em == null) return 0;
            long[] arr = fi.GetValue(em) as long[];
            if (arr == null) return 0;
            int count = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] < 0L)
                {
                    arr[i] = 0L;
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Armor against ArgumentOutOfRangeException: Value -1 is outside the valid range [0, 3155378975999999999]
        /// when deserializing uninitialized or -1 ticks in EconomyManager investment dates.
        /// </summary>
        public static DateTime SafeCreateDateTime(long ticks)
        {
            if (ticks < 0L || ticks > 3155378975999999999L)
            {
                return DateTime.MinValue;
            }
            return new DateTime(ticks);
        }

        /// <summary>
        /// Replaces newobj DateTime::.ctor(long) with SafeCreateDateTime(long) in EconomyManager.DeserializeInvestments.
        /// </summary>
        public static IEnumerable<CodeInstruction> DeserializeInvestments_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo safeMethod = typeof(EconomyProtection).GetMethod(nameof(SafeCreateDateTime), BindingFlags.Public | BindingFlags.Static);
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Newobj && instruction.operand is ConstructorInfo ci)
                {
                    if (ci.DeclaringType == typeof(DateTime))
                    {
                        var pars = ci.GetParameters();
                        if (pars != null && pars.Length == 1 && pars[0].ParameterType == typeof(long))
                        {
                            instruction.opcode = OpCodes.Call;
                            instruction.operand = safeMethod;
                            count++;
                        }
                    }
                }
                yield return instruction;
            }
            UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Injected SafeCreateDateTime into EconomyManager.DeserializeInvestments ({0} sites).", count));
        }

        /// <summary>
        /// Safe wrapper around EconomyManager.DeserializeInvestments to guarantee simulation load never crashes on corrupt investments.
        /// </summary>
        public static void SafeDeserializeInvestments(EconomyManager em, ColossalFramework.IO.DataSerializer s)
        {
            try
            {
                if (em != null && s != null)
                {
                    em.DeserializeInvestments(s);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] Recovered from corrupted EconomyManager investment data: " + ex.Message);
                try
                {
                    if (em != null)
                    {
                        em.ResetAllInvestments();
                    }
                }
                catch {}
            }
        }

        /// <summary>
        /// Replaces callvirt EconomyManager::DeserializeInvestments with SafeDeserializeInvestments in EconomyManager.Data.Deserialize.
        /// </summary>
        public static IEnumerable<CodeInstruction> EconomyManager_Data_Deserialize_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo safeMethod = typeof(EconomyProtection).GetMethod(nameof(SafeDeserializeInvestments), BindingFlags.Public | BindingFlags.Static);
            int count = 0;
            foreach (var instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && instruction.operand is MethodInfo mi)
                {
                    if (mi.Name == "DeserializeInvestments")
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = safeMethod;
                        count++;
                    }
                }
                yield return instruction;
            }
            UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] Injected SafeDeserializeInvestments into EconomyManager.Data.Deserialize ({0} sites).", count));
        }
    }

    // ========================================================================
    // MESSAGE & CHIRPER PROTECTION (v5.0)
    // Armor against ArgumentNullException: Argument cannot be null.
    // when deserializing null message queue or empty Chirper data in MessageManager.Data.Deserialize
    // ========================================================================
    public static class MessageProtection
    {
        private static FieldInfo s_fMessageQueue;
        private static FieldInfo s_fRecentMessages;
        private static FieldInfo s_fRecentMessageIndex;
        private static FieldInfo s_fMessageData;
        private static FieldInfo s_fLastRandomMessageFrame;
        private static FieldInfo s_fRandomMessageTimers;
        private static bool s_fieldsCached = false;

        public static void Apply(HarmonyInstance harmony)
        {
            try
            {
                Type mmType = typeof(MessageManager);
                Type mmDataType = mmType.GetNestedType("Data", BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo des = mmDataType?.GetMethod("Deserialize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (des != null)
                {
                    MethodInfo pre = typeof(MessageProtection).GetMethod(nameof(MessageManager_Data_Deserialize_Prefix), BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(des, prefix: new HarmonyMethod(pre));
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched MessageManager.Data.Deserialize with null-safe armor.");
                }

                MethodInfo getRecent = mmType?.GetMethod("GetRecentMessages", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo getRecentPre = typeof(MessageProtection).GetMethod(nameof(MessageManager_GetRecentMessages_Prefix), BindingFlags.Public | BindingFlags.Static);
                if (getRecent != null && getRecentPre != null)
                {
                    harmony.Patch(getRecent, prefix: new HarmonyMethod(getRecentPre) { prioritiy = Harmony.Priority.First });
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched MessageManager.GetRecentMessages with bounds guard.");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] MessageProtection.Apply error: " + ex);
            }
        }

        [HarmonyPriority(Priority.First)]
        public static bool MessageManager_GetRecentMessages_Prefix(MessageManager __instance, ref MessageBase[] __result)
        {
            try
            {
                CacheFields();
                if (__instance == null || s_fRecentMessages == null)
                {
                    __result = new MessageBase[0];
                    return false;
                }
                MessageBase[] recent = s_fRecentMessages.GetValue(__instance) as MessageBase[];
                if (recent == null || recent.Length == 0)
                {
                    __result = new MessageBase[0];
                    return false;
                }
                if (s_fRecentMessageIndex != null)
                {
                    int idx = (int)s_fRecentMessageIndex.GetValue(__instance);
                    if (idx < 0 || idx >= recent.Length)
                    {
                        s_fRecentMessageIndex.SetValue(__instance, 0);
                    }
                }
                return true;
            }
            catch
            {
                __result = new MessageBase[0];
                return false;
            }
        }

        private static void CacheFields()
        {
            if (s_fieldsCached) return;
            s_fieldsCached = true;
            try
            {
                Type mmType = typeof(MessageManager);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                s_fMessageQueue = mmType.GetField("m_messageQueue", flags);
                s_fRecentMessages = mmType.GetField("m_recentMessages", flags);
                s_fRecentMessageIndex = mmType.GetField("m_recentMessageIndex", flags);
                s_fMessageData = mmType.GetField("m_messageData", flags);
                s_fLastRandomMessageFrame = mmType.GetField("m_lastRandomMessageFrame", flags);
                s_fRandomMessageTimers = mmType.GetField("m_randomMessageTimers", flags);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] MessageProtection.CacheFields error: " + ex);
            }
        }

        public static bool MessageManager_Data_Deserialize_Prefix(object __instance, ColossalFramework.IO.DataSerializer s)
        {
            LoadingProfiler profiler = Singleton<LoadingManager>.instance.m_loadingProfilerSimulation;
            if (profiler != null)
            {
                profiler.BeginDeserialize(s, "MessageManager");
            }

            MessageManager mm = Singleton<MessageManager>.instance;
            CacheFields();

            try
            {
                if (mm != null)
                {
                    // 1. Read message queue safely (handles null array cleanly)
                    MessageBase[] queueArr = s.ReadObjectArray<MessageBase>();
                    Queue<MessageBase> queue = (queueArr != null)
                        ? new Queue<MessageBase>(queueArr)
                        : new Queue<MessageBase>();
                    if (s_fMessageQueue != null) s_fMessageQueue.SetValue(mm, queue);

                    // 2. Read recent messages safely (handles null cleanly)
                    MessageBase[] recent = s.ReadObjectArray<MessageBase>();
                    if (recent == null) recent = new MessageBase[16];
                    if (s_fRecentMessages != null) s_fRecentMessages.SetValue(mm, recent);

                    // 3. Recent message index
                    int recentIndex = 0;
                    if (s.version >= 85)
                    {
                        recentIndex = s.ReadInt16();
                    }
                    if (s.version < 149)
                    {
                        for (int i = 0; i < recent.Length; i++)
                        {
                            recent[i] = null;
                        }
                        recentIndex = 0;
                    }
                    if (recentIndex < 0 || (recent != null && recentIndex >= recent.Length))
                    {
                        recentIndex = 0;
                    }
                    if (s_fRecentMessageIndex != null) s_fRecentMessageIndex.SetValue(mm, recentIndex);

                    // 4. Message data dictionary (handles null keys cleanly)
                    Dictionary<string, MessageManager.MessageData> dict = (s_fMessageData != null ? s_fMessageData.GetValue(mm) as Dictionary<string, MessageManager.MessageData> : null);
                    if (dict == null)
                    {
                        dict = new Dictionary<string, MessageManager.MessageData>();
                        if (s_fMessageData != null) s_fMessageData.SetValue(mm, dict);
                    }
                    else
                    {
                        dict.Clear();
                    }

                    if (s.version >= 89)
                    {
                        uint count = s.ReadUInt24();
                        for (uint i = 0; i < count; i++)
                        {
                            string text = s.ReadUniqueString();
                            MessageManager.MessageData data = default(MessageManager.MessageData);
                            data.m_lastCreateFrame = s.ReadUInt32();
                            if (s.version >= 149 && !string.IsNullOrEmpty(text))
                            {
                                dict[text] = data;
                            }
                        }
                    }

                    // 5. Random message timers
                    byte[] timers = (s_fRandomMessageTimers != null ? s_fRandomMessageTimers.GetValue(mm) as byte[] : null);
                    if (timers == null || timers.Length != 27)
                    {
                        timers = new byte[27];
                        if (s_fRandomMessageTimers != null) s_fRandomMessageTimers.SetValue(mm, timers);
                    }

                    uint lastRandomFrame = 0;
                    if (s.version >= 155)
                    {
                        lastRandomFrame = s.ReadUInt32();
                        int timerCount = s.ReadInt16();
                        for (int j = 0; j < timerCount; j++)
                        {
                            byte b = (byte)s.ReadUInt8();
                            if (j < timers.Length)
                            {
                                timers[j] = b;
                            }
                        }
                        for (int k = timerCount; k < 27; k++)
                        {
                            if (k < timers.Length)
                            {
                                timers[k] = 0;
                            }
                        }
                    }
                    else
                    {
                        for (int l = 0; l < 27; l++)
                        {
                            timers[l] = 0;
                        }
                    }
                    if (s_fLastRandomMessageFrame != null) s_fLastRandomMessageFrame.SetValue(mm, lastRandomFrame);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] Recovered from corrupted MessageManager data: " + ex);
                if (mm != null)
                {
                    try
                    {
                        if (s_fMessageQueue != null && s_fMessageQueue.GetValue(mm) == null)
                            s_fMessageQueue.SetValue(mm, new Queue<MessageBase>());
                        if (s_fRecentMessages != null && s_fRecentMessages.GetValue(mm) == null)
                            s_fRecentMessages.SetValue(mm, new MessageBase[16]);
                        if (s_fRecentMessageIndex != null)
                            s_fRecentMessageIndex.SetValue(mm, 0);
                        if (s_fMessageData != null && s_fMessageData.GetValue(mm) == null)
                            s_fMessageData.SetValue(mm, new Dictionary<string, MessageManager.MessageData>());
                        if (s_fRandomMessageTimers != null && s_fRandomMessageTimers.GetValue(mm) == null)
                            s_fRandomMessageTimers.SetValue(mm, new byte[27]);
                    }
                    catch {}
                }
            }
            finally
            {
                if (profiler != null)
                {
                    profiler.EndDeserialize(s, "MessageManager");
                }
            }

            return false; // Skip buggy vanilla Deserialize
        }
    }

    // ====================================================================
    // WATER TRANSPORT COMPATIBILITY (v4.3.47)
    // Fixes boat / ferry transit lines on Ship Paths & Harbor/Dock connectivity
    // ====================================================================
    public static class WaterTransportCompatibility
    {
        private static bool s_applied = false;
        private static TransportInfo s_cachedFerryInfo = null;

        public static void Apply(HarmonyInstance harmony)
        {
            if (s_applied) return;
            try
            {
                // 1. Patch NetInfo.InitializePrefab (dynamically configures newly initialized water networks)
                try
                {
                    MethodInfo initPrefab = typeof(NetInfo).GetMethod("InitializePrefab", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (initPrefab != null)
                    {
                        MethodInfo post = typeof(WaterTransportCompatibility).GetMethod(nameof(NetInfo_InitializePrefab_Postfix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(initPrefab, postfix: new HarmonyMethod(post));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched NetInfo.InitializePrefab for Water Transport Compatibility.");
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching NetInfo.InitializePrefab: " + ex);
                }

                // 2. Patch ShipDockAI.FindFerryPath (reconnects ferry docks/piers with extended 128m radius, Ferry only)
                try
                {
                    MethodInfo findFerry = typeof(ShipDockAI).GetMethod("FindFerryPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (findFerry != null)
                    {
                        MethodInfo post = typeof(WaterTransportCompatibility).GetMethod(nameof(ShipDockAI_FindFerryPath_Postfix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(findFerry, postfix: new HarmonyMethod(post));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched ShipDockAI.FindFerryPath for Water Transport Compatibility.");
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching ShipDockAI.FindFerryPath: " + ex);
                }

                // 3. Patch ShipDockAI.FindConnectionPath (extended 128m search for ship docks, Ship only)
                try
                {
                    MethodInfo findConn = typeof(ShipDockAI).GetMethod("FindConnectionPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (findConn != null)
                    {
                        MethodInfo post = typeof(WaterTransportCompatibility).GetMethod(nameof(ShipDockAI_FindConnectionPath_Postfix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(findConn, postfix: new HarmonyMethod(post));
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched ShipDockAI.FindConnectionPath for Water Transport Compatibility.");
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Error patching ShipDockAI.FindConnectionPath: " + ex);
                }

                // ZERO HOOKS INTO TRAFFIC / TRANSIT SIMULATION (v5.0.2)
                // In strict accordance with traffic preservation rules:
                // No Harmony hooks into VehicleAI, ShipAI, CarAI, PathFind, or TransportStationAI.
                // Traffic simulation and transit routing remain 100% native (both vanilla and TM:PE).

                s_applied = true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] WaterTransportCompatibility.Apply error: " + ex);
            }
        }

        public static bool IsFerryNetwork(NetInfo info)
        {
            if (info == null) return false;
            if (info.m_class != null && info.m_class.m_layer == ItemClass.Layer.FerryPaths) return true;
            if (info.name != null)
            {
                string lower = info.name.ToLower();
                if (lower.Contains("ferry") || lower.Contains("canal"))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsShipPathNetwork(NetInfo info)
        {
            if (info == null) return false;
            // Any ferry or canal network is strictly NOT a ship path
            if (IsFerryNetwork(info)) return false;

            if (info.m_class != null && info.m_class.m_layer == ItemClass.Layer.ShipPaths) return true;
            if (info.m_netAI is ShipPathAI) return true;
            if (info.name != null)
            {
                string lower = info.name.ToLower();
                if (lower.Contains("ship path") || lower.Contains("ship connection") || lower == "ship")
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsWaterNetwork(NetInfo info)
        {
            return IsFerryNetwork(info) || IsShipPathNetwork(info);
        }

        public static void ConfigureWaterNetInfo(NetInfo info)
        {
            if (info == null) return;

            if (IsFerryNetwork(info))
            {
                // FERRY PATHS & CANALS:
                // STRICTLY Ferry ONLY. NEVER allow VehicleType.Ship on ferry paths or canals!
                // Large ships have massive momentum, low turning rate, and large length (150-200m).
                // Allowing them onto narrow ferry paths with sharp curves causes glitchy navigation,
                // drifting through shorelines/piers, or becoming permanently stuck.
                info.m_vehicleTypes &= ~VehicleInfo.VehicleType.Ship;
                info.m_vehicleTypes |= VehicleInfo.VehicleType.Ferry;
                info.m_availableIn = ItemClass.Availability.All;

                if (info.m_lanes != null)
                {
                    for (int j = 0; j < info.m_lanes.Length; j++)
                    {
                        var lane = info.m_lanes[j];
                        if (lane == null) continue;
                        if (lane.m_laneType == NetInfo.LaneType.Vehicle || 
                            (lane.m_vehicleType & (VehicleInfo.VehicleType.Ship | VehicleInfo.VehicleType.Ferry)) != 0)
                        {
                            lane.m_vehicleType &= ~VehicleInfo.VehicleType.Ship;
                            lane.m_vehicleType |= VehicleInfo.VehicleType.Ferry;
                        }
                    }
                }
            }
            else if (IsShipPathNetwork(info))
            {
                // SHIP PATHS (Open water & sea routes):
                // Support both Ship (large cargo/cruise ships) AND Ferry (passenger ferry transit lines).
                // This allows ferry lines to navigate open water across bays and oceans,
                // while ensuring large ships remain strictly in open water lanes.
                info.m_vehicleTypes |= (VehicleInfo.VehicleType.Ship | VehicleInfo.VehicleType.Ferry);
                info.m_availableIn = ItemClass.Availability.All;

                if (info.m_lanes != null)
                {
                    for (int j = 0; j < info.m_lanes.Length; j++)
                    {
                        var lane = info.m_lanes[j];
                        if (lane == null) continue;
                        if (lane.m_laneType == NetInfo.LaneType.Vehicle || 
                            (lane.m_vehicleType & (VehicleInfo.VehicleType.Ship | VehicleInfo.VehicleType.Ferry)) != 0)
                        {
                            lane.m_vehicleType |= (VehicleInfo.VehicleType.Ship | VehicleInfo.VehicleType.Ferry);
                        }
                    }
                }
            }
        }

        public static void SynchronizeWaterTransportNetworks()
        {
            try
            {
                int count = PrefabCollection<NetInfo>.LoadedCount();
                int shipCount = 0;
                int ferryCount = 0;
                for (uint i = 0; i < count; i++)
                {
                    NetInfo info = PrefabCollection<NetInfo>.GetLoaded(i);
                    if (info == null) continue;
                    if (IsShipPathNetwork(info))
                    {
                        ConfigureWaterNetInfo(info);
                        shipCount++;
                    }
                    else if (IsFerryNetwork(info))
                    {
                        ConfigureWaterNetInfo(info);
                        ferryCount++;
                    }
                }
                UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] WaterTransportCompatibility: Synchronized {0} ship paths (dual Ship|Ferry) and {1} ferry paths (strictly Ferry only).", shipCount, ferryCount));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] SynchronizeWaterTransportNetworks error: " + ex);
            }
        }

        public static void ReconnectWaterDocks()
        {
            try
            {
                NetManager nm = Singleton<NetManager>.instance;
                if (nm == null || nm.m_nodes == null || nm.m_nodes.m_buffer == null) return;

                int reconnected = 0;
                int bufferLen = nm.m_nodes.m_buffer.Length;
                for (int i = 0; i < bufferLen; i++)
                {
                    if ((nm.m_nodes.m_buffer[i].m_flags & NetNode.Flags.Created) == 0) continue;
                    NetInfo info = nm.m_nodes.m_buffer[i].Info;
                    if (info == null || !(info.m_netAI is ShipDockAI dockAI)) continue;

                    bool shouldUpdate = (nm.m_nodes.m_buffer[i].m_lane == 0);
                    if (!shouldUpdate)
                    {
                        uint laneID = nm.m_nodes.m_buffer[i].m_lane;
                        if (laneID < nm.m_lanes.m_buffer.Length)
                        {
                            ushort segId = nm.m_lanes.m_buffer[laneID].m_segment;
                            if (segId != 0 && segId < nm.m_segments.m_buffer.Length)
                            {
                                var segInfo = nm.m_segments.m_buffer[segId].Info;
                                if (segInfo != null)
                                {
                                    bool isFerryDock = (info.name != null && info.name.ToLower().Contains("ferry")) ||
                                                       (dockAI.m_transportInfo != null && dockAI.m_transportInfo.m_vehicleType == VehicleInfo.VehicleType.Ferry);
                                    if (!isFerryDock)
                                    {
                                        // Large ship harbor (Cargo Harbor, Cruise Harbor):
                                        // Must NOT be connected to a Ferry-only path or canal!
                                        if (IsFerryNetwork(segInfo) || (segInfo.m_vehicleTypes & VehicleInfo.VehicleType.Ship) == 0)
                                        {
                                            shouldUpdate = true;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (shouldUpdate)
                    {
                        uint oldLane = nm.m_nodes.m_buffer[i].m_lane;
                        dockAI.UpdateLaneConnection((ushort)i, ref nm.m_nodes.m_buffer[i]);
                        if (nm.m_nodes.m_buffer[i].m_lane != oldLane)
                        {
                            reconnected++;
                        }
                    }
                }

                if (reconnected > 0)
                {
                    UnityEngine.Debug.Log(string.Format("[MoreNetworkCapacityRenew] WaterTransportCompatibility: Reconnected {0} water docks/piers to ship/ferry paths.", reconnected));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] ReconnectWaterDocks error: " + ex);
            }
        }

        public static void NetInfo_InitializePrefab_Postfix(NetInfo __instance)
        {
            try
            {
                if (__instance != null && IsWaterNetwork(__instance))
                {
                    ConfigureWaterNetInfo(__instance);
                }
            }
            catch { }
        }

        public static void ShipDockAI_FindFerryPath_Postfix(ref Vector3 position, ref uint laneID, ref byte offset, ref bool __result)
        {
            if (!__result)
            {
                try
                {
                    PathUnit.Position posA;
                    PathUnit.Position posB;
                    float distA, distB;

                    // Ferry docks: search strictly for VehicleType.Ferry
                    if (PathManager.FindPathPosition(
                        position,
                        ItemClass.Service.PublicTransport,
                        NetInfo.LaneType.Vehicle,
                        VehicleInfo.VehicleType.Ferry,
                        VehicleInfo.VehicleCategory.All,
                        false,
                        true,
                        128f,
                        false,
                        false,
                        out posA,
                        out posB,
                        out distA,
                        out distB))
                    {
                        laneID = PathManager.GetLaneID(posA);
                        offset = posA.m_offset;
                        __result = true;
                    }
                }
                catch { }
            }
        }

        public static void ShipDockAI_FindConnectionPath_Postfix(ref Vector3 position, ref uint laneID, ref byte offset, bool laneConnection, ref bool __result)
        {
            if (!__result)
            {
                try
                {
                    PathUnit.Position posA;
                    PathUnit.Position posB;
                    float distA, distB;

                    // Large Ship docks (Harbor, Cargo Harbor): search strictly for VehicleType.Ship on legitimate Ship Paths
                    if (PathManager.FindPathPosition(
                        position,
                        ItemClass.Service.PublicTransport,
                        NetInfo.LaneType.Vehicle,
                        VehicleInfo.VehicleType.Ship,
                        VehicleInfo.VehicleCategory.All,
                        false,
                        true,
                        128f,
                        false,
                        false,
                        out posA,
                        out posB,
                        out distA,
                        out distB))
                    {
                        NetManager nm = Singleton<NetManager>.instance;
                        if (nm != null && posA.m_segment < nm.m_segments.m_buffer.Length)
                        {
                            var segInfo = nm.m_segments.m_buffer[posA.m_segment].Info;
                            if (segInfo != null && !IsFerryNetwork(segInfo))
                            {
                                laneID = PathManager.GetLaneID(posA);
                                offset = posA.m_offset;
                                __result = true;
                            }
                        }
                    }
                }
                catch { }
            }
        }

        public static TransportInfo GetWaterTransportInfo()
        {
            if (s_cachedFerryInfo != null) return s_cachedFerryInfo;
            try
            {
                s_cachedFerryInfo = PrefabCollection<TransportInfo>.FindLoaded("Ferry")
                                 ?? PrefabCollection<TransportInfo>.FindLoaded("Ship");
                if (s_cachedFerryInfo == null)
                {
                    int count = PrefabCollection<TransportInfo>.LoadedCount();
                    for (uint i = 0; i < count; i++)
                    {
                        var ti = PrefabCollection<TransportInfo>.GetLoaded(i);
                        if (ti != null && (ti.m_transportType == TransportInfo.TransportType.Ship || 
                                           ti.m_vehicleType == VehicleInfo.VehicleType.Ferry ||
                                           ti.m_vehicleType == VehicleInfo.VehicleType.Ship))
                        {
                            s_cachedFerryInfo = ti;
                            break;
                        }
                    }
                }
            }
            catch { }
            return s_cachedFerryInfo;
        }
    }

    // ============================================================================
    // GAME LOADING ARMOR (Corrupted Save & Mod Loading Protection)
    // ============================================================================
    public static class GameLoadingArmor
    {
        public static void Apply(HarmonyInstance harmony)
        {
            // 1. UnlockManager.Data.RefreshScenarioConditions (prevents OverflowException on negative triggerCount)
            try
            {
                Type umType = typeof(UnlockManager);
                Type umDataType = umType.GetNestedType("Data", BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo refreshMethod = umDataType?.GetMethod("RefreshScenarioConditions", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo refreshPre = typeof(GameLoadingArmor).GetMethod(nameof(UnlockManager_Data_RefreshScenarioConditions_Prefix), BindingFlags.Public | BindingFlags.Static);
                if (refreshMethod != null && refreshPre != null)
                {
                    harmony.Patch(refreshMethod, prefix: new HarmonyMethod(refreshPre) { prioritiy = Harmony.Priority.First });
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched UnlockManager.Data.RefreshScenarioConditions with negative overflow armor.");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] GameLoadingArmor: Error patching UnlockManager: " + ex);
            }

            // 2. RadioPanel.AssignStationToButton (prevents NullReferenceException on missing/null radio stations)
            try
            {
                Type rpType = typeof(RadioPanel);
                MethodInfo assignMethod = rpType?.GetMethod("AssignStationToButton", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo assignPre = typeof(GameLoadingArmor).GetMethod(nameof(RadioPanel_AssignStationToButton_Prefix), BindingFlags.Public | BindingFlags.Static);
                if (assignMethod != null && assignPre != null)
                {
                    harmony.Patch(assignMethod, prefix: new HarmonyMethod(assignPre) { prioritiy = Harmony.Priority.First });
                    UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched RadioPanel.AssignStationToButton with null station guard.");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] GameLoadingArmor: Error patching RadioPanel: " + ex);
            }

            // 3. TransportLinesManager.Overrides.TransportLineOverrides.RemoveVehicle (prevents NullReferenceException on vehicles with missing prefabs)
            try
            {
                Type tlmOverrides = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "TransportLinesManager")
                    {
                        tlmOverrides = asm.GetType("TransportLinesManager.Overrides.TransportLineOverrides");
                        break;
                    }
                }
                if (tlmOverrides != null)
                {
                    MethodInfo removeVeh = tlmOverrides.GetMethod("RemoveVehicle", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    MethodInfo pre = typeof(GameLoadingArmor).GetMethod(nameof(TLM_TransportLineOverrides_RemoveVehicle_Prefix), BindingFlags.Public | BindingFlags.Static);
                    if (removeVeh != null && pre != null)
                    {
                        harmony.Patch(removeVeh, prefix: new HarmonyMethod(pre) { prioritiy = Harmony.Priority.First });
                        UnityEngine.Debug.Log("[MoreNetworkCapacityRenew] Patched TransportLinesManager.Overrides.TransportLineOverrides.RemoveVehicle with null vehicle guard.");
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[MoreNetworkCapacityRenew] GameLoadingArmor: Error patching TransportLinesManager: " + ex);
            }
        }

        [HarmonyPriority(Priority.First)]
        public static void UnlockManager_Data_RefreshScenarioConditions_Prefix(ref int triggerCount)
        {
            if (triggerCount < 0)
            {
                triggerCount = 0;
            }
        }

        [HarmonyPriority(Priority.First)]
        public static bool RadioPanel_AssignStationToButton_Prefix(ColossalFramework.UI.UIButton button, RadioChannelInfo station)
        {
            if (button == null || station == null)
            {
                return false;
            }
            return true;
        }

        [HarmonyPriority(Priority.First)]
        public static bool TLM_TransportLineOverrides_RemoveVehicle_Prefix(ushort lineID, ref Vehicle vehicleData)
        {
            try
            {
                if (vehicleData.Info == null)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
            return true;
        }
    }

    public static class DataSerializerReadUniqueStringPatch
    {
        public static void Postfix(ref string __result)
        {
            TransportLineCompatibility.DataSerializer_ReadUniqueString_Postfix(ref __result);
        }
    }

    public static class VehicleManagerDataAfterDeserializePatch
    {
        public static void Postfix()
        {
            TransportLineCompatibility.VehicleManager_Data_AfterDeserialize_Postfix();
        }
    }
}

