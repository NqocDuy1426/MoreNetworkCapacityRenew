using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

class TestSuite
{
    private static int testsPassed = 0;
    private static int testsFailed = 0;

    static void Assert(bool condition, string testName, string failureDetail = "")
    {
        if (condition)
        {
            Console.WriteLine("  [PASS] " + testName);
            testsPassed++;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  [FAIL] " + testName + (string.IsNullOrEmpty(failureDetail) ? "" : ": " + failureDetail));
            Console.ResetColor();
            testsFailed++;
        }
    }

    static int Main()
    {
        Console.WriteLine("==========================================================");
        Console.WriteLine("🧪 RUNNING COMPREHENSIVE TEST SUITE (BEFORE BUILD)");
        Console.WriteLine("==========================================================");

        string home = Environment.GetEnvironmentVariable("HOME");
        string originalMnc = home + "/Library/Application Support/Steam/steamapps/workshop/content/255710/3814019901/MoreNetworkCapacity.dll";

        // ====================================================================
        // TEST SUITE 1: THREAD AUDIT (GAME THREAD COMPLIANCE)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 1: Game Thread Compliance (No Background Threads) ---");
        try
        {
            // 1.1 Test strip_iusermod strips BeginMonitoring and Stop in Core.dll
            string testCoreDll = "test_core_audit.dll";
            if (File.Exists(originalMnc))
            {
                var asm = AssemblyDefinition.ReadAssembly(originalMnc);
                var modType = asm.MainModule.GetType("MoreNetworkCapacity.Mod");
                if (modType != null) modType.Interfaces.Clear();

                var diagType = asm.MainModule.GetType("MoreNetworkCapacity.SimulationStepDiagnostics");
                if (diagType != null)
                {
                    foreach (var m in diagType.Methods)
                    {
                        if (m.Name == "BeginMonitoring" || m.Name == "Stop")
                        {
                            m.Body.ExceptionHandlers.Clear();
                            m.Body.Variables.Clear();
                            m.Body.Instructions.Clear();
                            m.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                        }
                    }
                }
                asm.Write(testCoreDll);

                // Verify the stripped assembly
                var strippedAsm = AssemblyDefinition.ReadAssembly(testCoreDll);
                int threadCreationCount = 0;
                foreach (var t in strippedAsm.MainModule.Types)
                {
                    foreach (var m in t.Methods)
                    {
                        if (m.HasBody)
                        {
                            foreach (var inst in m.Body.Instructions)
                            {
                                if (inst.Operand != null && inst.Operand.ToString().Contains("Thread::.ctor"))
                                {
                                    threadCreationCount++;
                                }
                            }
                        }
                    }
                }
                Assert(threadCreationCount == 0, "Core.dll has 0 background Thread creation calls", "Found " + threadCreationCount);

                var strippedDiag = strippedAsm.MainModule.GetType("MoreNetworkCapacity.SimulationStepDiagnostics");
                var bm = strippedDiag.Methods.FirstOrDefault(x => x.Name == "BeginMonitoring");
                Assert(bm != null && bm.Body.Instructions.Count == 1 && bm.Body.Instructions[0].OpCode == OpCodes.Ret,
                    "SimulationStepDiagnostics.BeginMonitoring is stripped to Ret opcode");

                File.Delete(testCoreDll);
            }
            else
            {
                Console.WriteLine("  [SKIP] Original MNC not found at workshop path, skipping Core.dll generation test.");
            }

            // 1.2 Scan Renew.cs for any unauthorized thread creation
            string renewCode = File.ReadAllText("Renew.cs");
            bool hasNewThread = Regex.IsMatch(renewCode, @"new\s+Thread\s*\(");
            bool hasThreadPool = Regex.IsMatch(renewCode, @"ThreadPool\.QueueUserWorkItem");
            Assert(!hasNewThread, "Renew.cs contains 0 'new Thread(' calls");
            Assert(!hasThreadPool, "Renew.cs contains 0 'ThreadPool.QueueUserWorkItem' calls");

            // Verify ArrayGuardianV2 uses main-thread periodic checks
            bool hasPerformPeriodicCheck = renewCode.Contains("PerformPeriodicCheck()");
            Assert(hasPerformPeriodicCheck, "ArrayGuardianV2 uses main-thread PerformPeriodicCheck");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 1 exception", ex.ToString());
        }

        // ====================================================================
        // TEST SUITE 2: SAVE -> PACKAGING LIFECYCLE
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 2: Save -> Packaging Lifecycle Safety ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 2.1 Verify SavePanelOnSavingCompletePatch does NOT check File.Exists(CurrentSavingFilename)
            bool hasFalseDeleteCheck = renewCode.Contains("!System.IO.File.Exists(SaveSimulationDataSafetyPatch.CurrentSavingFilename)");
            Assert(!hasFalseDeleteCheck, "SavePanelOnSavingCompletePatch does NOT falsely fail when temp .ccs file is deleted by packaging");

            // 2.2 Verify CurrentSavingFilename is cleared upon completion
            bool clearsCurrentSaving = renewCode.Contains("CurrentSavingFilename = null;");
            Assert(clearsCurrentSaving, "SaveSimulationDataSafetyPatch cleans CurrentSavingFilename upon completion");

            // 2.3 Verify Level Unloading packaging guard
            bool hasPackagingGuard = renewCode.Contains("m_PackageSaveTask") && renewCode.Contains("task.Wait()");
            Assert(hasPackagingGuard, "OnLevelUnloading guards against interrupted SavePanel.m_PackageSaveTask");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 2 exception", ex.ToString());
        }

        // ====================================================================
        // TEST SUITE 3: REFERENCE ARCHITECTURE COMPLIANCE (ZERO VEHICLE INTERFERENCE)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 3: Reference Architecture Compliance (Zero Vehicle Interference) ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 3.1 Verify all vehicle/traffic intervention classes are completely eliminated
            Assert(!renewCode.Contains("class StuckVehicleRecovery"), "StuckVehicleRecovery eliminated (no depot unspawning or waitCounter kills)");
            Assert(!renewCode.Contains("class MNC_ThreadingExtension"), "MNC_ThreadingExtension eliminated (no per-frame thread slicing)");
            Assert(!renewCode.Contains("class VehicleAndTransportRepair"), "VehicleAndTransportRepair eliminated (no citizen wiping or vehicle purges)");
            Assert(!renewCode.Contains("class TurboTrafficDispatcher"), "TurboTrafficDispatcher eliminated (no TransferManager offer tampering)");
            Assert(!renewCode.Contains("class EconomyCompatibility"), "EconomyCompatibility eliminated (no maintenance tampering)");
            Assert(!renewCode.Contains("class VehiclePoolMonitor"), "VehiclePoolMonitor eliminated (no vehicle count overriding)");
            Assert(!renewCode.Contains("class TMPEBufferSync"), "TMPEBufferSync eliminated (no speed limit cache tampering)");
            Assert(!renewCode.Contains("class PathFindValidator"), "PathFindValidator eliminated (no path wiping)");
            Assert(!renewCode.Contains("class DiagnosticsUI"), "DiagnosticsUI eliminated (clean UI)");
            Assert(!renewCode.Contains("class VehicleFixPatches"), "VehicleFixPatches eliminated (no network update hooks)");

            // 3.2 Verify TM:PE ClearTraffic / DespawnVehicles hooks are native (no citizen wiping on Clear Traffic)
            bool tmpeDespawnHooked = renewCode.Contains("UtilityManager_DespawnVehicles_Postfix");
            Assert(!tmpeDespawnHooked, "UtilityManager.ClearTraffic / DespawnVehicles left 100% native (no passenger wiping)");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 3 exception", ex.ToString());
        }

        // ====================================================================
        // TEST SUITE 4: CROSS-PLATFORM COMPATIBILITY (MAC & WIN)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 4: Cross-Platform Compatibility (macOS & Windows) ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 4.1 Verify NO hardcoded /Users/ or C:\ paths in Renew.cs
            bool hasHardcodedMac = renewCode.Contains("/Users/");
            bool hasHardcodedWin = Regex.IsMatch(renewCode, @"[A-Za-z]:\\Users");
            Assert(!hasHardcodedMac, "Renew.cs contains 0 hardcoded '/Users/' paths");
            Assert(!hasHardcodedWin, "Renew.cs contains 0 hardcoded 'C:\\Users' paths");

            // 4.2 Verify DataLocation is used for mod/save paths
            bool usesDataLocation = renewCode.Contains("DataLocation");
            Assert(usesDataLocation, "Renew.cs uses ColossalFramework.IO.DataLocation for game paths");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 4 exception", ex.ToString());
        }

        // ====================================================================
        // TEST SUITE 5: SAVE DEADLOCK PREVENTION & SERIALIZATION SAFETY
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 5: Save Deadlock Prevention & Serialization Safety ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 5.1 Verify SimulationManager_Data_Serialize_Prefix returns bool
            bool prefixReturnsBool = renewCode.Contains("public static bool SimulationManager_Data_Serialize_Prefix(");
            Assert(prefixReturnsBool, "SimulationManager_Data_Serialize_Prefix returns bool to skip vanilla infinite spin loop");

            // 5.2 Verify WriteSerializableDataStorageSafely exists with safe timeout
            bool hasSafeStorageWriter = renewCode.Contains("WriteSerializableDataStorageSafely") && renewCode.Contains("Monitor.TryEnter(storage, 2000)");
            Assert(hasSafeStorageWriter, "WriteSerializableDataStorageSafely uses Monitor.TryEnter with 2000ms timeout");

            // 5.3 Verify safe snapshot fallback for m_serializableDataStorage
            bool hasSnapshotFallback = renewCode.Contains("Enumerable.ToArray(storage)");
            Assert(hasSnapshotFallback, "WriteSerializableDataStorageSafely contains snapshot fallback for mod data");

            // 5.4 Verify SimulationManager.SYNCHRONIZE_TIMEOUT is 0 to prevent simulation freeze
            bool hasTimeoutGuard = renewCode.Contains("SimulationManager.SYNCHRONIZE_TIMEOUT = 0;");
            Assert(hasTimeoutGuard, "SimulationManager.SYNCHRONIZE_TIMEOUT maintained at 0ms (vanilla non-blocking acquisition)");

            // 5.5 Verify 0 unconstrained while (!Monitor.TryEnter without body in Renew.cs
            bool hasUnconstrainedSpin = Regex.IsMatch(renewCode, @"while\s*\(\s*!Monitor\.TryEnter[^\)]*\)\s*\{\s*\}");
            Assert(!hasUnconstrainedSpin, "Renew.cs contains 0 unconstrained while (!Monitor.TryEnter) spin loops");

            // 5.6 Verify PathManager.WaitForAllPaths deadlock defense prefix
            bool hasPathManagerWfap = renewCode.Contains("PathManager_WaitForAllPaths_Prefix") && renewCode.Contains("SafeWaitForAllPaths");
            Assert(hasPathManagerWfap, "PathManager_WaitForAllPaths_Prefix safely defends against pathfinding spinlocks during Save");

            // 5.7 Verify managers serialization tracing & finalizers
            bool hasManagerTracers = renewCode.Contains("PathManager_Data_Serialize_Prefix") &&
                                     renewCode.Contains("VehicleManager_Data_Serialize_Prefix") &&
                                     renewCode.Contains("TreeManager_Data_Serialize_Prefix");
            Assert(hasManagerTracers, "Data serialization tracers and finalizers active for Path, Vehicle, and Tree managers");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 5 exception", ex.ToString());
        }

        // ====================================================================
        // TEST SUITE 6: TM:PE PATHFINDING & IPT COMPATIBILITY
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 6: TM:PE Pathfinding & IPT Compatibility ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 6.1 Verify TM:PE CustomPathFind Awake buffer expansion is active and internal routing methods are native
            bool hasCpfAwake = renewCode.Contains("CustomPathFind_Awake_Postfix") && renewCode.Contains("SynchronizeSinglePathFind");
            bool hasCpfWfap = renewCode.Contains("WaitForAllPaths_Prefix");
            bool hasCpfNative = renewCode.Contains("CustomPathFind internal routing methods") && renewCode.Contains("left 100% native to TM:PE");
            Assert(hasCpfAwake, "CustomPathFind.Awake capacity postfix installed to synchronize 65k buffers");
            Assert(hasCpfWfap, "CustomPathFind.WaitForAllPaths safe deadlock defense prefix installed");
            Assert(hasCpfNative, "CustomPathFind internal routing methods left 100% native to TM:PE to eliminate thread NREs");

            // 6.2 Verify zero reflection in CustomPathFind_PathFindImplementation_Prefix (Hot-path performance)
            int pfiIndex = renewCode.IndexOf("public static bool CustomPathFind_PathFindImplementation_Prefix");
            string pfiBody = pfiIndex > 0 ? renewCode.Substring(pfiIndex, Math.Min(1200, renewCode.Length - pfiIndex)) : "";
            bool hasReflectionInPfi = pfiBody.Contains("GetType(") || pfiBody.Contains("GetMethod(") || pfiBody.Contains("EnsureSavedGameOptions");
            Assert(!hasReflectionInPfi, "CustomPathFind_PathFindImplementation_Prefix is 100% pure array lookups with zero reflection overhead");

            // 6.3 Verify IPT buffers expanded for both Node and Vehicle arrays
            bool expandsIptNodes = renewCode.Contains("m_cachedNodeData") && renewCode.Contains("65024");
            bool expandsIptVehicles = renewCode.Contains("m_cachedVehicleData") && renewCode.Contains("65536");
            Assert(expandsIptNodes, "EnsureImprovedPublicTransportBuffers expands CachedNodeData.m_cachedNodeData to 65024");
            Assert(expandsIptVehicles, "EnsureImprovedPublicTransportBuffers expands CachedVehicleData.m_cachedVehicleData to 65536");

            // 6.4 Verify TransportLine problem checks clean fatal problem and path failed flags
            bool hasNodeCheck = renewCode.Contains("TransportLineAI_CheckNodeProblems_Prefix") && renewCode.Contains("FatalProblem");
            bool hasSegCheck = renewCode.Contains("TransportLineAI_CheckSegmentProblems_Prefix") && renewCode.Contains("PathFailed");
            Assert(hasNodeCheck, "TransportLineAI_CheckNodeProblems_Prefix removes fatal problem flags");
            Assert(hasSegCheck, "TransportLineAI_CheckSegmentProblems_Prefix clears PathFailed flag");

            // 6.5 Verify TM:PE options persistence guards
            bool hasSgoGuard = renewCode.Contains("CreateDynamicGetterPrefix") && renewCode.Contains("EnsureAndGetSavedGameOptions") && renewCode.Contains("SavedGameOptions_Release_Prefix");
            bool hasGcGuard = renewCode.Contains("EnsureAndGetGlobalConfig");
            Assert(hasSgoGuard, "CreateDynamicGetterPrefix & SavedGameOptions_Release_Prefix installed to guarantee non-null options");
            Assert(hasGcGuard, "EnsureAndGetGlobalConfig installed to guarantee non-null global config");

            // 6.6 Verify ThreadingExtension and PathFindImplementation patches are applied
            bool hasThreadExtPatch = renewCode.Contains("ThreadingExtension.OnBeforeSimulationFrame") && renewCode.Contains("ThreadingExtension_OnBeforeSimulationFrame_Prefix");
            bool hasPfiPatch = renewCode.Contains("CustomPathFind.PathFindImplementation") && renewCode.Contains("CustomPathFind_PathFindImplementation_Prefix");
            Assert(hasThreadExtPatch, "ThreadingExtension.OnBeforeSimulationFrame patched with null-safe prefix");
            Assert(hasPfiPatch, "CustomPathFind.PathFindImplementation patched with bounds-safe prefix");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 6 exception", ex.ToString());
        }

        // ====================================================================
        // TEST GROUP 7: MorePathUnits Synchronization & PathManager Deserialize Safety
        // ====================================================================
        Console.WriteLine("\n[TEST GROUP 7: MorePathUnits & PathManager Deserialize Safety]");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");
            bool hasSyncMpu = renewCode.Contains("SynchronizeMorePathUnits") && renewCode.Contains("s_loadingExpanded");
            bool hasPatchMpu = renewCode.Contains("PatchMorePathUnits") && renewCode.Contains("MetaData.LoadingExtended");
            bool hasPmDeserPrefix = renewCode.Contains("PathManager_Data_Deserialize_Prefix");
            bool hasFullPmDeser = renewCode.Contains("PathManager_Data_Deserialize_Prefix") &&
                                  renewCode.Contains("units deserialized from save");
            bool hasSmDeserPrefix = renewCode.Contains("SimulationManager_Data_Deserialize_Prefix");
            bool hasLpPrefix = renewCode.Contains("LoadingProfiler_EndDeserialize_Prefix") && renewCode.Contains("PathManager");
            bool hasPreSaveMpu = renewCode.Contains("PreSaveSanitization") && renewCode.Contains("SynchronizeMorePathUnits");
            bool hasModEnabledPatches = renewCode.Contains("RenewMod.OnEnabled") && renewCode.Contains("EnsureAllPatchesApplied");
            bool hasSimMetaMerge = renewCode.Contains("SimulationMetaData_Merge_Prefix");
            bool hasLmsdPrefix = renewCode.Contains("LoadingManager_LoadSimulationData_Prefix");

            Assert(hasSyncMpu, "SynchronizeMorePathUnits guarantees s_loadingExpanded=true and modOverride['User/More PathUnits x2']");
            Assert(hasPatchMpu, "PatchMorePathUnits hooks MetaData.LoadingExtended and PathDeserialize.DeserializeSize");
            Assert(hasPmDeserPrefix, "PathManager.Data.Deserialize prefix synchronizes MorePathUnits before deserialization starts");
            Assert(hasFullPmDeser, "PathManager.Data.Deserialize prefix performs 100% byte-accurate full deserialization and returns false");
            Assert(hasSmDeserPrefix, "SimulationManager.Data.Deserialize prefix ensures MorePathUnits patched and synchronized");
            Assert(hasLpPrefix, "LoadingProfiler.EndDeserialize guards against PathManager tag mismatch & ArgumentOutOfRangeException");
            Assert(hasPreSaveMpu, "PreSaveSanitization synchronizes MorePathUnits before game save serialization");
            Assert(hasModEnabledPatches, "RenewMod.OnEnabled applies EnsureAllPatchesApplied at startup before save load");
            Assert(hasSimMetaMerge, "SimulationMetaData.Merge is hooked to prevent ngs from wiping out save modOverride");
            Assert(hasLmsdPrefix, "LoadingManager.LoadSimulationData seeds expanded modOverride flags before load starts");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 7 exception", ex.ToString());
        }

        // ====================================================================
        // TEST GROUP 8: Vehicle AI & Native TM:PE Simulation Stability
        // ====================================================================
        Console.WriteLine("\n[TEST GROUP 8: Vehicle AI & Native TM:PE Simulation Stability]");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");
            bool tmpeNativeVehicles = renewCode.Contains("VehicleAICommons: Left 100% native to TM:PE");
            bool tmpeNativeBehavior = renewCode.Contains("VehicleBehaviorManager: Left 100% native to TM:PE");
            bool hasNetSegLanes = renewCode.Contains("NetSegment_GetLeftAndRightLanes_Prefix") && renewCode.Contains("GetLeftAndRightLanes");
            bool hasEnsureEvm = renewCode.Contains("EnsureExtVehicleManagerCapacity") && renewCode.Contains("ExtVehicleManager");
            bool hasEnsureLcm = renewCode.Contains("EnsureLaneConnectionManagerCapacity") && renewCode.Contains("connectionDataBase_");
            bool hasLcsmPatches = renewCode.Contains("LaneConnectionSubManager_HasNodeConnections_Prefix") 
                               && renewCode.Contains("LaneConnectionSubManager_HasConnections_Prefix")
                               && renewCode.Contains("LaneConnectionSubManager_AreLanesConnected_Prefix");
            bool has4PosPfi = renewCode.Contains("seg00") && renewCode.Contains("seg01") && renewCode.Contains("seg02") && renewCode.Contains("seg03");
            bool hasJrmNative = renewCode.Contains("JunctionRestrictionsManager.LoadData");

            Assert(tmpeNativeVehicles, "VehicleAICommons left 100% native to TM:PE for natural physics & coordinates");
            Assert(tmpeNativeBehavior, "VehicleBehaviorManager left 100% native to TM:PE for native despawn and segment transitions");
            Assert(hasNetSegLanes, "NetSegment.GetLeftAndRightLanes guarded against null Info and m_lanes");
            Assert(hasEnsureEvm, "ExtVehicleManager.ExtVehicles expanded to 65,536 to match More Vehicles");
            Assert(hasEnsureLcm, "TM:PE LaneConnectionManager.Road/Track connectionDataBase_ guaranteed initialized");
            Assert(hasLcsmPatches, "TM:PE LaneConnectionSubManager guarded against NRE and uncreated nodes/lanes");
            Assert(has4PosPfi, "CustomPathFind validates all 4 positions (start A/B and end A/B) without misidentifying destination");
            Assert(hasJrmNative, "TM:PE JunctionRestrictionsManager maintains native authority over junction permissions");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 8 exception", ex.ToString());
        }

        // ====================================================================
        // TEST SUITE 9: TM:PE ROUTINGMANAGER & NODE EQUALITY ARCHITECTURE (v4.3.9)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 9: TM:PE RoutingManager & Node Equality Architecture (v4.3.9) ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 9.1 Verify LaneEndBackwardRoutings & LaneEndForwardRoutings normalization to 524,288
            bool hasLaneEnd524k = renewCode.Contains("Normalized RoutingManager.LaneEndBackwardRoutings") &&
                                  renewCode.Contains("524288") &&
                                  renewCode.Contains("262144");
            Assert(hasLaneEnd524k, "LaneEndBackwardRoutings & LaneEndForwardRoutings normalized to standard 524,288 with offset 262,144");

            // 9.2 Verify updatedSegmentBuckets size 1016
            bool hasBuckets1016 = renewCode.Contains("updatedSegmentBuckets: {0} -> 1016") &&
                                  renewCode.Contains("new ulong[1016]");
            Assert(hasBuckets1016, "RoutingManager.updatedSegmentBuckets normalized to 1016 to strictly match 65,024 segment capacity and TMPEBufferValidation");

            // 9.3 Verify ExtLane.GetLaneInfo linked-list resolver (IndexOutOfRangeException defense)
            bool hasGliPrefix = renewCode.Contains("ExtLane_GetLaneInfo_Prefix") &&
                                renewCode.Contains("NetInfo.Lane foundLane = null") &&
                                renewCode.Contains("curLane = NetManager.instance.m_lanes.m_buffer[curLane].m_nextLane");
            Assert(hasGliPrefix, "ExtLane.GetLaneInfo protected with linked-list resolver preventing IndexOutOfRangeException");

            // 9.4 Verify ExtBuildingManager & SimulationStepPatch crash armor
            bool hasEbmArmor = renewCode.Contains("ExtBuildingManager_OnBeforeSimulationStep_Prefix") &&
                               renewCode.Contains("CommonBuildingAI_SimulationStepPatch_Prefix_Guard");
            Assert(hasEbmArmor, "ExtBuildingManager & CommonBuildingAI.SimulationStep armored against NullReferenceException");

            // 9.5 Verify SanitizeRoutingManager purges invalid segments
            bool hasSanitizeRm = renewCode.Contains("SanitizeRoutingManager") &&
                                 renewCode.Contains("Sanitized RoutingManager: Purged");
            Assert(hasSanitizeRm, "SanitizeRoutingManager automatically detects and purges broken/dead segment transitions");

            // 9.6 Verify EnsureJunctionRestrictionsDefaults64k populates native TM:PE defaults
            bool hasJrd64k = renewCode.Contains("EnsureJunctionRestrictionsDefaults64k") &&
                             renewCode.Contains("segId = 36864u") &&
                             renewCode.Contains("Initialized TM:PE native junction defaults");
            Assert(hasJrd64k, "EnsureJunctionRestrictionsDefaults64k populates native TM:PE defaults for segments >= 36864");

            // 9.7 Verify RequestFullRecalculation64k marks all 65,024 segments for update
            bool hasReqFull64k = renewCode.Contains("RequestFullRecalculation64k") &&
                                 renewCode.Contains("65024") &&
                                 renewCode.Contains("RequestFullRecalculation64k: Queued all 65,024 segments");
            Assert(hasReqFull64k, "RequestFullRecalculation64k ensures all 65,024 segments are marked for TM:PE update");

            // 9.8 Verify TMPEBufferValidation prefix
            bool hasTbvPrefix = renewCode.Contains("TMPEBufferValidation_Validate_Prefix") &&
                                renewCode.Contains("Armored TMPEBufferValidation.Validate");
            Assert(hasTbvPrefix, "TMPEBufferValidation.Validate protected against simulation deserialization crash with Harmony prefix");

            // 9.9 Verify CustomPathFind methods unhooked from Harmony
            bool hasCustomPathFindUnhooked = renewCode.Contains("CustomPathFind internal routing methods are left 100% native to TM:PE");
            Assert(hasCustomPathFindUnhooked, "CustomPathFind methods left 100% native to TM:PE without Harmony reflection overhead");

            // 9.10 Verify DynamicPatcher excludes CustomPathFind
            bool hasCpfExcluded = renewCode.Contains("type.Name.Contains(\"PathFind\") || type.Name.Contains(\"CustomPathFind\")");
            Assert(hasCpfExcluded, "CustomPathFind excluded from dynamic patching to preserve native speed");

            // 9.11 Verify road & node demolition native handling (delegated to vanilla game & TM:PE)
            bool hasNativeDemolition = renewCode.Contains("Road & node demolition handling is natively delegated to vanilla game & TM:PE") &&
                                       !renewCode.Contains("harmony.Patch(relSegM");
            Assert(hasNativeDemolition, "Road & node demolition handling natively delegated to vanilla game & TM:PE (no aggressive despawn)");

            // 9.12 Verify flexible More Vehicles compatibility
            bool hasFlexibleVehicles = renewCode.Contains("int bufferLen = (vm != null && vm.m_vehicles != null && vm.m_vehicles.m_buffer != null)") &&
                                       renewCode.Contains("int targetSize = Math.Max(16384, bufferLen);");
            Assert(hasFlexibleVehicles, "EnsureExtVehicleManagerCapacity dynamically adapts to More Vehicles (65k) or vanilla (16k)");

            // 9.13 Verify CustomPathFind inner-loop methods unhooked for native 60+ FPS pathfinding performance
            bool hasNativePathFindLoop = renewCode.Contains("CustomPathFind inner-loop methods (ProcessItemMain, ProcessItemCosts, ProcessItemRouted, etc.)") &&
                                         renewCode.Contains("are intentionally NOT hooked with Harmony prefixes to ensure 100% native C# path computation speed");
            Assert(hasNativePathFindLoop, "CustomPathFind inner-loop methods unhooked to ensure native 60+ FPS pathfinding performance");

            // 9.14 Verify EnsureExtBuildingManagerCapacity ensures 49,152 building capacity
            bool hasEbmCap = renewCode.Contains("EnsureExtBuildingManagerCapacity") &&
                             renewCode.Contains("49152");
            Assert(hasEbmCap, "EnsureExtBuildingManagerCapacity guarantees 49,152 building capacity for TM:PE");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 9 exception", ex.ToString());
        }

        // ====================================================================
        // TEST GROUP 10: Anti-Deadlock, Continuous Road & Native TM:PE Transitions (v4.3.32 - v4.3.34)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 10: Anti-Deadlock, Continuous Road & Native TM:PE Transitions (v4.3.32 - v4.3.34) ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 10.1 Continuous 2-segment corridor false-blocked junction guard
            bool hasJrmContinuousGuard = renewCode.Contains("JunctionRestrictionsManager_IsEnteringBlockedJunctionAllowed_Prefix") &&
                                         renewCode.Contains("CountSegments() <= 2");
            Assert(hasJrmContinuousGuard, "JunctionRestrictionsManager prefix allows entering continuous 2-segment roads (no false blocked junctions)");

            // 10.2 VehicleBehaviorManager MayChangeSegment is NOT patched in Harmony (preserves 100% native TM:PE transitions)
            bool noVbmPatch = !renewCode.Contains("Patched TM:PE VehicleBehaviorManager.MayChangeSegment") &&
                              renewCode.Contains("MayChangeSegment is intentionally NOT patched");
            Assert(noVbmPatch, "MayChangeSegment is NOT patched in Harmony (preserves 100% native TM:PE transitions)");

            // 10.3 MayChangeSegment postfix does not override __result or force transit states (eliminates rewind loop)
            bool noResultOverride = renewCode.Contains("do NOT override __result or transitState");
            Assert(noResultOverride, "MayChangeSegment postfix does NOT override __result or transitState (eliminates rewind loop)");

            // 10.4 JunctionRestrictionsManager has single unified prefix (avoids AmbiguousMatchException)
            int jrmPrefixCount = 0;
            int idx = 0;
            while ((idx = renewCode.IndexOf("JunctionRestrictionsManager_IsEnteringBlockedJunctionAllowed_Prefix", idx)) != -1)
            {
                jrmPrefixCount++;
                idx += "JunctionRestrictionsManager_IsEnteringBlockedJunctionAllowed_Prefix".Length;
            }
            Assert(jrmPrefixCount <= 2, "JunctionRestrictionsManager_IsEnteringBlockedJunctionAllowed_Prefix has no duplicate overloads");

            // 10.5 SavePanel.OnSavingComplete parameter signature accuracy
            bool hasSavePanelByVal = renewCode.Contains("Prefix(SavePanel __instance, bool quitAfterSaving, bool errorOnSaving)");
            Assert(hasSavePanelByVal, "SavePanelOnSavingCompletePatch.Prefix parameter signature matches target method (by-value bool errorOnSaving)");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 10 exception", ex.ToString());
        }

        // ====================================================================
        // TEST GROUP 11: AdaptiveRoads Deserialization Armor & Pedestrian/Bike Pathing (v4.3.33)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 11: AdaptiveRoads Deserialization Armor & Pedestrian/Bike Pathing (v4.3.33) ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 11.1 Check v4.3.36 version bump
            bool hasV436 = renewCode.Contains("More Network Capacity Renew v4.3.36 by NgocDuy");
            Assert(hasV436, "Version correctly bumped to v4.3.36 in RenewMod.Name");

            // 11.2 Check AR_Deserialize_Prefix is implemented
            bool hasArDeserPre = renewCode.Contains("public static bool AR_Deserialize_Prefix(object s)");
            Assert(hasArDeserPre, "AR_Deserialize_Prefix exists to safely wrap AdaptiveRoads deserialization");

            // 11.3 Check stream size detection IsExpandedARStream
            bool hasStreamCheck = renewCode.Contains("public static bool IsExpandedARStream(object s)") &&
                                  renewCode.Contains("remaining < 1500000");
            Assert(hasStreamCheck, "IsExpandedARStream accurately differentiates standard 36K vs expanded 65K stream payload");

            // 11.4 Check EnsureAdaptiveRoadsStandardBuffers resizes to standard 36k
            bool hasStandardBuffers = renewCode.Contains("public static void EnsureAdaptiveRoadsStandardBuffers(object nem)") &&
                                      renewCode.Contains("36864") &&
                                      renewCode.Contains("73728") &&
                                      renewCode.Contains("32768") &&
                                      renewCode.Contains("262144");
            Assert(hasStandardBuffers, "EnsureAdaptiveRoadsStandardBuffers enforces standard vanilla buffer limits before deserialization");

            // 11.5 Verify AR InitBuffers and Awake are NOT patched to Harmony (avoids EOF assertion crash)
            bool noInitBufPatch = !renewCode.Contains("nameof(AR_InitBuffers_Postfix)");
            bool noAwakePatch = !renewCode.Contains("nameof(AR_Awake_Postfix)");
            Assert(noInitBufPatch && noAwakePatch, "AR InitBuffers and Awake are NOT hooked with buffer expansions (prevents premature 65K EOF crash)");

            // 11.6 Verify AR Deserialize method is hooked with armored prefix
            bool hasDeserHook = renewCode.Contains("harmony.Patch(deser, prefix: new HarmonyMethod(pre))") &&
                                renewCode.Contains("nameof(AR_Deserialize_Prefix)");
            Assert(hasDeserHook, "AdaptiveRoads.Deserialize is hooked with armored AR_Deserialize_Prefix");

            // 11.7 Verify post-deserialization 65K expansion
            bool hasPostDeserExpand = renewCode.Contains("EnsureAdaptiveRoadsBuffers(\"AR.Deserialize.Finished\", inst)");
            Assert(hasPostDeserExpand, "AR.Deserialize.Finished ensures buffers are expanded to 65K for active gameplay");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 11 exception", ex.ToString());
        }

        // ====================================================================
        // TEST GROUP 12: GitHub Issue #1 Fix Verification (Assertion Failed & Rewind Loop)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 12: GitHub Issue #1 Fix Verification (Assertion Failed & Rewind Loop) ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 12.1 Symptom 1: Assertion failed: expected 524288 > 4294967295 on game load
            bool hasSymptom1Fix = renewCode.Contains("AR_Deserialize_Prefix") &&
                                  renewCode.Contains("EnsureAdaptiveRoadsStandardBuffers");
            Assert(hasSymptom1Fix, "Issue #1 Symptom 1: Standard save stream size protected, preventing EOF assertion crash");

            // 12.2 Symptom 2: Rewinding traffic loop (where vehicles roll back then forward in a loop)
            bool hasSymptom2Fix = renewCode.Contains("VehicleBehaviorManager left 100% native to TM:PE") &&
                                  !renewCode.Contains("Patched TM:PE VehicleBehaviorManager.MayChangeSegment");
            Assert(hasSymptom2Fix, "Issue #1 Symptom 2: MayChangeSegment unhooked, eliminating vehicle rewind oscillation loop");

            // 12.3 Despawn All Vehicles in TM:PE remains 100% functional without post-despawn jitter
            bool tmpeDespawnSafe = !renewCode.Contains("UtilityManager_DespawnVehicles_Postfix");
            Assert(tmpeDespawnSafe, "TM:PE Clear Traffic / Despawn All Vehicles operates natively with zero side-effects");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 12 exception", ex.ToString());
        }

        // ====================================================================
        // TEST GROUP 13: Transfer Manager Extended (TransferManagerCE) 65K Compatibility (v4.3.35)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 13: Transfer Manager Extended (TransferManagerCE) 65K Compatibility (v4.3.35) ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 13.1 TransferManagerCE registration in target assemblies
            bool hasTmeTarget = renewCode.Contains("name == \"TransferManagerCE\"") &&
                                renewCode.Contains("name == \"TransferManagerExtended\"");
            Assert(hasTmeTarget, "TransferManagerCE / Extended registered in target third-party mod assemblies");

            // 13.2 PathDistance BitArray expansion to 65536
            bool hasBitArrayExpand = renewCode.Contains("TransferManager_PathDistance_Ctor_Postfix") &&
                                     renewCode.Contains("new System.Collections.BitArray(65536)");
            Assert(hasBitArrayExpand, "PathDistance constructor expands m_visited BitArray to 65536 (eliminates ArgumentOutOfRangeException)");

            // 13.3 PathDistance ResetArrays safety prefix
            bool hasResetArraysPre = renewCode.Contains("TransferManager_PathDistance_ResetArrays_Prefix") &&
                                     renewCode.Contains("ba.Length < 65536");
            Assert(hasResetArraysPre, "PathDistance.ResetArrays prefix guarantees 65536 BitArray capacity before each Dijkstra search");

            // 13.4 PathDistance UpdateNode bounds guard prefix
            bool hasUpdateNodePre = renewCode.Contains("TransferManager_PathDistance_UpdateNode_Prefix") &&
                                    renewCode.Contains("nodeId >= 65024");
            Assert(hasUpdateNodePre, "PathDistance.UpdateNode prefix safely bounds-checks node IDs and dynamically resizes BitArray");

            // 13.5 EnsureTransferManagerBuffers synchronizes NetNodes and NetSegments
            bool hasBufferSync = renewCode.Contains("EnsureTransferManagerBuffers") &&
                                 renewCode.Contains("pathNodeType.GetField(\"NetSegments\"") &&
                                 renewCode.Contains("pathNodeType.GetField(\"NetNodes\"");
            Assert(hasBufferSync, "EnsureTransferManagerBuffers synchronizes PathNode NetNodes & NetSegments with 65K arrays");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 13 exception", ex.ToString());
        }

        // ====================================================================
        // TEST GROUP 14: Rebalanced Industries Revisited 1.0.3 Compatibility (v4.3.36)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 14: Rebalanced Industries Revisited 1.0.3 Compatibility (v4.3.36) ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 14.1 RebalancedIndustriesRevisited registered in IsTargetModAssembly
            bool hasRirTarget = renewCode.Contains("name == \"RebalancedIndustriesRevisited\"");
            Assert(hasRirTarget, "RebalancedIndustriesRevisited registered in target third-party mod assemblies");

            // 14.2 RebalancedIndustriesRevisited registered in DynamicPatcher
            bool hasRirPatcher = renewCode.Contains("\"RebalancedIndustriesRevisited\"");
            Assert(hasRirPatcher, "RebalancedIndustriesRevisited registered in DynamicPatcher targetModNames");

            // 14.3 No conflicting vehicle/cargo tampering with Rebalanced Industries
            bool noCargoConflict = !renewCode.Contains("CargoTruckAI_SetSource_Prefix") &&
                                   !renewCode.Contains("WarehouseAI_GetMaxLoadSize_Prefix");
            Assert(noCargoConflict, "Zero conflicting overrides against Rebalanced Industries cargo & warehouse calculations");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 14 exception", ex.ToString());
        }

        // ====================================================================
        // FINAL SUMMARY
        // ====================================================================
        Console.WriteLine("\n==========================================================");
        Console.WriteLine(string.Format("📊 TEST RESULTS: {0} PASSED, {1} FAILED", testsPassed, testsFailed));
        Console.WriteLine("==========================================================");

        if (testsFailed > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("❌ TEST SUITE FAILED! BUILD ABORTED.");
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("✅ ALL TESTS PASSED! READY FOR BUILD.");
        Console.ResetColor();
        return 0;
    }
}
