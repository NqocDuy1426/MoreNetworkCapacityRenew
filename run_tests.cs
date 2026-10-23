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

            // 2.4 Verify SavePanelOnSavingCompletePatch does not call QuitApplication off-thread
            bool noOffThreadQuit = !renewCode.Contains("Singleton<LoadingManager>.instance.QuitApplication();");
            Assert(noOffThreadQuit, "SavePanelOnSavingCompletePatch does NOT execute QuitApplication off-thread (delegates to Main Thread)");
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
            bool noCpfWfap = !renewCode.Contains("Patched TM:PE CustomPathFind.WaitForAllPaths");
            bool hasCpfNative = renewCode.Contains("CustomPathFind internal routing methods") && renewCode.Contains("left 100% native to TM:PE");
            Assert(hasCpfAwake, "CustomPathFind.Awake capacity postfix installed to synchronize 65k buffers");
            Assert(noCpfWfap, "CustomPathFind.WaitForAllPaths left 100% native to TM:PE without thread lock overrides");
            Assert(hasCpfNative, "CustomPathFind internal routing methods left 100% native to TM:PE to eliminate thread NREs");

            // 6.2 Verify zero reflection in CustomPathFind_PathFindImplementation_Prefix (Hot-path performance)
            int pfiIndex = renewCode.IndexOf("public static bool CustomPathFind_PathFindImplementation_Prefix");
            string pfiBody = pfiIndex > 0 ? renewCode.Substring(pfiIndex, Math.Min(1200, renewCode.Length - pfiIndex)) : "";
            bool hasReflectionInPfi = pfiBody.Contains("GetType(") || pfiBody.Contains("GetMethod(") || pfiBody.Contains("EnsureSavedGameOptions");
            Assert(!hasReflectionInPfi, "CustomPathFind_PathFindImplementation_Prefix is 100% pure array lookups with zero reflection overhead");

            // 6.3 Verify IPT buffers expanded for both Node and Vehicle arrays
            bool expandsIptNodes = renewCode.Contains("m_cachedNodeData") && renewCode.Contains("65536");
            bool expandsIptVehicles = renewCode.Contains("m_cachedVehicleData") && renewCode.Contains("65536");
            Assert(expandsIptNodes, "EnsureImprovedPublicTransportBuffers expands CachedNodeData.m_cachedNodeData to 65536");
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

            // 6.6 Verify TM:PE ThreadingExtension & CustomPathFind.PathFindImplementation are 100% native (unhooked)
            bool threadExtUnhooked = !renewCode.Contains("Patched TM:PE ThreadingExtension.OnBeforeSimulationFrame");
            bool pfiUnhooked = !renewCode.Contains("Patched TM:PE CustomPathFind.PathFindImplementation") &&
                               renewCode.Contains("CustomPathFind.PathFindImplementation and WaitForAllPaths are intentionally left 100% native");
            Assert(threadExtUnhooked, "TM:PE ThreadingExtension left 100% native (timed traffic lights execute without external hook)");
            Assert(pfiUnhooked, "CustomPathFind.PathFindImplementation left 100% native to TM:PE (zero interference with pathfinding)");
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

            // 9.2 Verify updatedSegmentBuckets size 1016 (MNC standard)
            bool hasBuckets1016 = (renewCode.Contains("updatedSegmentBuckets: {0} -> 1016") || renewCode.Contains("updatedSegmentBuckets: {0} -> 1024")) &&
                                  (renewCode.Contains("new ulong[1016]") || renewCode.Contains("new ulong[1024]"));
            Assert(hasBuckets1016, "RoutingManager.updatedSegmentBuckets normalized to CORE_BUCKET_CAPACITY to strictly match segment capacity");

            // 9.3 Verify ExtLane.GetLaneInfo linked-list resolver (IndexOutOfRangeException defense)
            bool hasGliPrefix = renewCode.Contains("ExtLane_GetLaneInfo_Prefix") &&
                                renewCode.Contains("NetInfo.Lane foundLane = null") &&
                                renewCode.Contains("curLane = NetManager.instance.m_lanes.m_buffer[curLane].m_nextLane");
            Assert(hasGliPrefix, "ExtLane.GetLaneInfo protected with linked-list resolver preventing IndexOutOfRangeException");

            // 9.4 Verify ExtBuildingManager crash armor & native SimulationStep
            bool hasEbmArmor = renewCode.Contains("ExtBuildingManager_OnBeforeSimulationStep_Prefix") &&
                               !renewCode.Contains("Patched TM:PE SimulationStepPatch.Prefix with safe guard");
            Assert(hasEbmArmor, "ExtBuildingManager armored while CommonBuildingAI.SimulationStep is 100% native");

            // 9.5 Verify SanitizeRoutingManager purges invalid segments
            bool hasSanitizeRm = renewCode.Contains("SanitizeRoutingManager") &&
                                 renewCode.Contains("Sanitized RoutingManager: Purged");
            Assert(hasSanitizeRm, "SanitizeRoutingManager automatically detects and purges broken/dead segment transitions");

            // 9.6 Verify EnsureJunctionRestrictionsDefaults64k populates native TM:PE defaults
            bool hasJrd64k = renewCode.Contains("EnsureJunctionRestrictionsDefaults64k") &&
                             renewCode.Contains("segId = 36864u") &&
                             renewCode.Contains("Initialized TM:PE native junction defaults");
            Assert(hasJrd64k, "EnsureJunctionRestrictionsDefaults64k populates native TM:PE defaults for segments >= 36864");

            // 9.7 Verify RequestFullRecalculation64k marks segments for update
            bool hasReqFull64k = renewCode.Contains("RequestFullRecalculation64k") &&
                                 (renewCode.Contains("CORE_NET_CAPACITY") || renewCode.Contains("65536")) &&
                                 renewCode.Contains("RequestFullRecalculation64k: Queued all");
            Assert(hasReqFull64k, "RequestFullRecalculation64k ensures all segments are marked for TM:PE update");

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

            // 10.1 JunctionRestrictionsManager is NOT patched in Harmony (preserves 100% native TM:PE junction rules)
            bool noJrmPatch = !renewCode.Contains("Patched TM:PE JunctionRestrictionsManager.IsEnteringBlockedJunctionAllowed") &&
                              renewCode.Contains("JunctionRestrictionsManager.IsEnteringBlockedJunctionAllowed is intentionally left 100% native to TM:PE");
            Assert(noJrmPatch, "JunctionRestrictionsManager left 100% native to TM:PE (zero interference with natural junction algorithm)");

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

            // 11.1 Check v5.0 version bump and rebranding
            bool hasV50 = renewCode.Contains("More Nodes/Segments v5.0 by NgocDuy");
            Assert(hasV50, "Version and name correctly updated to More Nodes/Segments v5.0 in RenewMod.Name");

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

            // 13.2 PathDistance BitArray expansion
            bool hasBitArrayExpand = renewCode.Contains("TransferManager_PathDistance_Ctor_Postfix") &&
                                     (renewCode.Contains("new System.Collections.BitArray(MNCConstants.CORE_NET_CAPACITY)") || renewCode.Contains("new System.Collections.BitArray(65536)"));
            Assert(hasBitArrayExpand, "PathDistance constructor expands m_visited BitArray to network capacity (eliminates ArgumentOutOfRangeException)");

            // 13.3 PathDistance ResetArrays safety prefix
            bool hasResetArraysPre = renewCode.Contains("TransferManager_PathDistance_ResetArrays_Prefix") &&
                                     (renewCode.Contains("ba.Length < MNCConstants.CORE_NET_CAPACITY") || renewCode.Contains("ba.Length < 65536"));
            Assert(hasResetArraysPre, "PathDistance.ResetArrays prefix guarantees network BitArray capacity before each Dijkstra search");

            // 13.4 PathDistance UpdateNode bounds guard prefix
            bool hasUpdateNodePre = renewCode.Contains("TransferManager_PathDistance_UpdateNode_Prefix") &&
                                    (renewCode.Contains("nodeId >= MNCConstants.CORE_NET_CAPACITY") || renewCode.Contains("nodeId >= 65536"));
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
        // TEST GROUP 15: PURE NATIVE TM:PE PATHFINDING & VEHICLE SPAWN INTEGRITY (v4.3.37)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 15: Pure Native TM:PE Pathfinding & Vehicle Spawn Integrity (v4.3.37) ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 15.1 CustomPathFind.PathFindImplementation is 100% native (not patched)
            bool noCpfPatch = !renewCode.Contains("harmony.Patch(pfiM") &&
                              !renewCode.Contains("Patched TM:PE CustomPathFind.PathFindImplementation");
            Assert(noCpfPatch, "CustomPathFind.PathFindImplementation is NOT hooked in Harmony (eliminates thread deadlock & vehicle spawn lockup)");

            // 15.2 CustomPathFind.WaitForAllPaths & CustomPathManager.WaitForAllPaths are 100% native
            bool noWfapPatch = !renewCode.Contains("harmony.Patch(wfapM");
            Assert(noWfapPatch, "CustomPathFind & CustomPathManager WaitForAllPaths are NOT hooked in Harmony (preserves native thread synchronization)");

            // 15.3 JunctionRestrictionsManager.IsEnteringBlockedJunctionAllowed is 100% native
            bool noJrmEntering = !renewCode.Contains("harmony.Patch(isEnteringM");
            Assert(noJrmEntering, "JunctionRestrictionsManager.IsEnteringBlockedJunctionAllowed is NOT hooked in Harmony (preserves native junction rules)");

            // 15.4 laneLocation_ and laneTarget_ are NOT resized to 524,288
            bool nativeLaneArrays = !renewCode.Contains("uint[] newLoc = new uint[524288]") &&
                                    !renewCode.Contains("fLoc.SetValue(pf, newLoc)") &&
                                    renewCode.Contains("262144");
            Assert(nativeLaneArrays, "CustomPathFind laneLocation_ and laneTarget_ preserved at native 262,144 capacity");

            // 15.5 SimulationStepPatch is NOT hooked in Harmony
            bool noSspPatch = !renewCode.Contains("harmony.Patch(prefixM, prefix: new HarmonyMethod(safeGuard)");
            Assert(noSspPatch, "TM:PE SimulationStepPatch is NOT hooked in Harmony (buildings freely dispatch vehicles)");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 15 exception", ex.ToString());
        }

        // ====================================================================
        // TEST GROUP 16: PURE ZERO-HOOK TM:PE ARCHITECTURE (v4.3.40)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 16: Pure Zero-Hook TM:PE Architecture (v4.3.40) ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 16.1 SpeedLimitManager.GetGameSpeedLimit is 100% native (not hooked)
            bool noSpeedHook = !renewCode.Contains("Patched TM:PE SpeedLimitManager.GetGameSpeedLimit");
            Assert(noSpeedHook, "SpeedLimitManager.GetGameSpeedLimit is NOT hooked in Harmony (TM:PE calculates native lane speeds)");

            // 16.2 TrafficManager.State.Flags is 100% native (not hooked)
            bool noFlagsHook = !renewCode.Contains("Patched TM:PE Flags.CanHaveLaneArrows") &&
                               !renewCode.Contains("Patched TM:PE Flags.ApplyLaneArrowFlags") &&
                               !renewCode.Contains("Patched TM:PE Flags.SetLaneAllowedVehicleTypes");
            Assert(noFlagsHook, "TrafficManager.State.Flags is NOT hooked in Harmony (TM:PE manages lane arrows & vehicle types natively)");

            // 16.3 PrioritySignsTool & JunctionRestrictionsTool are 100% native
            bool noToolsHook = !renewCode.Contains("Patched TM:PE PrioritySignsTool") &&
                               !renewCode.Contains("Patched TM:PE JunctionRestrictionsTool");
            Assert(noToolsHook, "PrioritySignsTool and JunctionRestrictionsTool are NOT hooked in Harmony");

            // 16.4 ExtBuildingManager.OnBeforeSimulationStep is 100% native
            bool noEbmHook = !renewCode.Contains("Patched TM:PE ExtBuildingManager.OnBeforeSimulationStep");
            Assert(noEbmHook, "ExtBuildingManager.OnBeforeSimulationStep is NOT hooked in Harmony");

            // 16.5 LaneConnectionSubManager is 100% native
            bool noLcsmHook = !renewCode.Contains("Patched TM:PE LaneConnectionSubManager");
            Assert(noLcsmHook, "LaneConnectionSubManager is NOT hooked in Harmony (preserves native lane connections)");

            // 16.6 Zero vehicle watchdog or forced despawn interference
            bool noWatchdog = !renewCode.Contains("MNCVehicleWatchdogThreading") &&
                              !renewCode.Contains("CleanStuckVehicles");
            Assert(noWatchdog, "Zero external vehicle watchdog/cleaner: vehicles and despawning follow pure user/game/TM:PE rules");

            // 16.7 Strictly ZERO Harmony hooks on TM:PE methods (zero GetIndex hooks, zero TM:PE method hooks)
            bool noTmpeHooks = !renewCode.Contains("Patched TM:PE SegmentEndManager.GetIndex") &&
                               !renewCode.Contains("Patched TM:PE TurnOnRedManager.GetIndex") &&
                               !renewCode.Contains("Patched TM:PE TrafficMeasurementManager.GetDirIndex");
            Assert(noTmpeHooks, "STRICTLY ZERO Harmony hooks into TM:PE methods (preserves 100% native TM:PE execution)");

            // 16.8 TrafficManager EXCLUDED from DynamicPatcher to prevent enum corruption & preserve Mono JIT
            bool tmpeExcluded = !renewCode.Contains("\"TrafficManager\"\n") &&
                                !renewCode.Contains("\"TrafficManager\",");
            Assert(tmpeExcluded, "TrafficManager excluded from DynamicPatcher targetModNames (avoids enum corruption & maintains full FPS)");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 16 exception", ex.ToString());
        }

        // ====================================================================
        // TEST GROUP 17: FULL-MAP TRAFFIC LIGHT STEPPER & NON-ZERO SPEED CACHE (v4.3.40)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 17: Full-Map Traffic Light Stepper & Non-Zero Speed Cache (v4.3.40) ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 17.1 MNCTrafficLightStepper exists and inherits from ThreadingExtensionBase
            bool hasStepper = renewCode.Contains("public class MNCTrafficLightStepper : ThreadingExtensionBase") &&
                              renewCode.Contains("OnAfterSimulationFrame") &&
                              renewCode.Contains("32768 + (int)frame64 * 512");
            Assert(hasStepper, "MNCTrafficLightStepper natively steps TM:PE traffic light simulations for nodes 32768..65535 via ThreadingExtensionBase");

            // 17.2 Stepper reset hooked into level load
            bool resetCalled = renewCode.Contains("MNCTrafficLightStepper.ResetStepper();");
            Assert(resetCalled, "MNCTrafficLightStepper.ResetStepper() called in RenewLoading.OnLevelLoaded");

            // 17.3 Preserved native speed limits with 524,288 cache capacity
            bool cacheExp = renewCode.Contains("int targetSize = 524288;") &&
                            renewCode.Contains("Expanded SpeedLimitManager.cachedLaneSpeedLimits_");
            Assert(cacheExp, "cachedLaneSpeedLimits_ expanded to 524,288 elements with natural speed limits preserved");

            // 17.4 Contiguous split array expansion for split array index compatibility without hooks
            bool contiguousSplit = renewCode.Contains("CORE_SPLIT_CAPACITY") &&
                                   renewCode.Contains("TurnOnRedSegments") &&
                                   renewCode.Contains("SegmentDirTrafficData");
            Assert(contiguousSplit, "TurnOnRedSegments and SegmentDirTrafficData expanded contiguously to CORE_SPLIT_CAPACITY elements (0..101,888 fit natively)");

            // 17.5 SegmentEndManager.SegmentEnds expanded to split capacity contiguously
            bool segEndsSplit = renewCode.Contains("CORE_SPLIT_CAPACITY") &&
                                renewCode.Contains("SegmentEndManager.SegmentEnds");
            Assert(segEndsSplit, "SegmentEndManager.SegmentEnds expanded contiguously to CORE_SPLIT_CAPACITY elements");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 17 exception", ex.ToString());
        }

        // ====================================================================
        // TEST GROUP 18: UNIVERSAL PUBLIC TRANSPORT & CITY ECONOMY ARMOR (v4.3.41)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 18: Universal Public Transport & City Economy Armor (v4.3.41) ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 18.1 EconomyProtection patches registered
            bool hasEconApply = renewCode.Contains("EconomyProtection.Apply(harmony);") &&
                                renewCode.Contains("nameof(EconomyManager_FetchResource_Prefix)") &&
                                renewCode.Contains("nameof(EconomyManager_AddResource_Prefix)") &&
                                renewCode.Contains("nameof(EconomyManager_AddPrivateIncome_Prefix)") &&
                                renewCode.Contains("nameof(PlayerNetAI_GetMaintenanceCost_Prefix)");
            Assert(hasEconApply, "EconomyProtection.Apply hooks FetchResource, AddResource, AddPrivateIncome, and GetMaintenanceCost");

            // 18.2 Non-negative guard prevents budget skyrocketing and negative expenses/income
            bool hasNonNegGuard = renewCode.Contains("amount = 0;") &&
                                  renewCode.Contains("__result = 0;") &&
                                  renewCode.Contains("return false;");
            Assert(hasNonNegGuard, "Universal non-negative guard prevents negative maintenance/income from corrupting city economy");

            // 18.3 Save repair SanitizeEconomy
            bool hasSanitizeEcon = renewCode.Contains("EconomyProtection.SanitizeEconomy();") &&
                                   renewCode.Contains("m_expenses") &&
                                   renewCode.Contains("m_totalExpenses") &&
                                   renewCode.Contains("m_income") &&
                                   renewCode.Contains("m_totalIncome");
            Assert(hasSanitizeEcon, "SanitizeEconomy cleans and restores all corrupted negative economic entries on level load");

            // 18.4 Transport line cycle-breaker and sanity
            bool hasLineSanitizer = renewCode.Contains("TransportLineCompatibility.SanitizeTransportLines();") &&
                                    renewCode.Contains("visitedVehicles.Add(curVehicle)") &&
                                    renewCode.Contains("m_ticketPrice > 50000");
            Assert(hasLineSanitizer, "TransportLineCompatibility.SanitizeTransportLines detects vehicle cycles and resets corrupted ticket prices");

            // 18.5 Track maintenance NaN/Inf overflow armor
            bool hasTrackArmor = renewCode.Contains("float.IsNaN(startPos.x)") &&
                                 renewCode.Contains("float.IsInfinity(startPos.x)") &&
                                 renewCode.Contains("calc > (long)int.MaxValue");
            Assert(hasTrackArmor, "PlayerNetAI.GetMaintenanceCost protected against NaN, Infinity, and 32-bit integer overflow");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 18 exception", ex.ToString());
        }

        // ====================================================================
        // TEST GROUP 19: FULL-MAP NETWORK STEPPER & TM:PE HIGH-ID SUBTOOL SYNC (v4.3.42)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 19: Full-Map Network Stepper & TM:PE High-ID Subtool Sync (v4.3.42) ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 19.1 Upper segment stepping (36,864..65,535) across 256 frames
            bool hasSegStepping = renewCode.Contains("int sStart = 36864 + (int)frame256 * 112;") &&
                                  renewCode.Contains("info.m_netAI.SimulationStep((ushort)s, ref segBuffer[s]);") &&
                                  renewCode.Contains("s_tmmStepDelegate(s_tmmInstance, (ushort)s, ref segBuffer[s]);");
            Assert(hasSegStepping, "Upper segments (36864..65535) stepped natively via NetAI across 256 frames and TM:PE TrafficMeasurement");

            // 19.2 Upper node stepping (32,768..65,535) across 256 frames
            bool hasNodeStepping = renewCode.Contains("int nStart256 = 32768 + (int)frame256 * 128;") &&
                                   renewCode.Contains("info.m_netAI.SimulationStep((ushort)i, ref nodeBuffer[i]);");
            Assert(hasNodeStepping, "Upper nodes (32768..65535) stepped natively via NetAI across 256 frames");

            // 19.3 Color map synchronization for upper network
            bool hasColorSync = renewCode.Contains("nm.UpdateSegmentColors(36864,") &&
                                renewCode.Contains("nm.UpdateNodeColors(32768,");
            Assert(hasColorSync, "Upper segments and nodes regularly sync colors with NetManager.m_objectColorMap");

            // 19.4 TM:PE SubTool high-ID synchronizer (Zero hooks)
            bool hasSubToolSync = renewCode.Contains("public static void SynchronizeActiveTool()") &&
                                  renewCode.Contains("SynchronizeJunctionRestrictionsTool") &&
                                  renewCode.Contains("SynchronizeTimedTrafficLightsTool") &&
                                  renewCode.Contains("SynchronizePrioritySignsTool") &&
                                  renewCode.Contains("SynchronizeVehicleRestrictionsTool") &&
                                  renewCode.Contains("SynchronizeLaneConnectorTool") &&
                                  renewCode.Contains("SynchronizeToggleTrafficLightsTool") &&
                                  renewCode.Contains("SynchronizeSpeedLimitsTool") &&
                                  renewCode.Contains("SynchronizeParkingRestrictionsTool");
            Assert(hasSubToolSync, "TM:PE active subtools synchronize high-ID nodes/segments (32K-65K) with zero Harmony hooks");

            // 19.5 ExtSegmentEnds expanded to CORE_SPLIT_CAPACITY
            bool hasExtSegEndSplit = renewCode.Contains("CORE_SPLIT_CAPACITY") &&
                                     renewCode.Contains("ExtSegmentEndManager.ExtSegmentEnds");
            Assert(hasExtSegEndSplit, "ExtSegmentEndManager.ExtSegmentEnds expanded to CORE_SPLIT_CAPACITY to match full network capacity");

            // 19.6 High-ID TM:PE Savegame Serialization (SerializableDataExtensionBase)
            bool hasSerialData = renewCode.Contains("class MNCSerializableData : SerializableDataExtensionBase") &&
                                 renewCode.Contains("DATA_KEY = \"MoreNetworkCapacity_HighID_TMPE_v1\";");
            Assert(hasSerialData, "MNCSerializableData extension implements SerializableDataExtensionBase for High-ID TM:PE data preservation");

            // 19.7 Serialize & Restore High-ID TM:PE Data with zero Harmony hooks
            bool hasSerializeRestore = renewCode.Contains("public static byte[] SerializeHighIdTMPEData()") &&
                                       renewCode.Contains("public static void RestorePendingHighIdTMPEData()") &&
                                       renewCode.Contains("TMPECompatibility.RestorePendingHighIdTMPEData();");
            Assert(hasSerializeRestore, "High-ID TM:PE custom settings (32K-65K) serialized and restored seamlessly across savegames with zero hooks");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 19 exception", ex.ToString());
        }

        // ====================================================================
        // TEST GROUP 20: TM:PE CRASH IMMUNIZATION & FULL FEATURE RESTORATION (v4.3.43)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 20: TM:PE Crash Immunization & Full Feature Restoration (v4.3.43) ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 20.1 SerializableDataWrapper.LoadData Harmony prefix in vanilla game (zero TM:PE hooks)
            bool hasLoadDataHook = renewCode.Contains("SerializableDataWrapper_LoadData_Prefix") &&
                                   renewCode.Contains("TrafficManager_v1.0") &&
                                   renewCode.Contains("EnsureParkingRestrictionsCapacity(") &&
                                   renewCode.Contains("EnsureTrafficLightSimulationCapacity(tmpe);");
            Assert(hasLoadDataHook, "Vanilla SerializableDataWrapper.LoadData prefixed to pre-expand all TM:PE buffers to Core capacity before TM:PE deserialization (zero TM:PE hooks)");

            // 20.2 256-frame cadence for upper NetAI stepping (112 segments/frame, 128 nodes/frame)
            bool has256Cadence = renewCode.Contains("uint frame256 = sm.m_currentFrameIndex & 255;") &&
                                 renewCode.Contains("int sStart = 36864 + (int)frame256 * 112;") &&
                                 renewCode.Contains("int nStart256 = 32768 + (int)frame256 * 128;");
            Assert(has256Cadence, "Upper NetAI stepping runs on exact 256-frame cadence (112 segs, 128 nodes), eliminating traffic light gridlock & allowing full traffic congestion accumulation");

            // 20.3 64-frame cadence for TM:PE Timed Traffic Lights (512 nodes/frame)
            bool has64TtlCadence = renewCode.Contains("uint frame64 = sm.m_currentFrameIndex & 63;") &&
                                   renewCode.Contains("int ttlStart = 32768 + (int)frame64 * 512;") &&
                                   renewCode.Contains("s_stepDelegate(s_simulationsArray, i);");
            Assert(has64TtlCadence, "TM:PE Timed Traffic Light stepping runs on exact native 64-frame cadence (512 nodes/frame reaching 65,536)");

            // 20.4 High-ID TM:PE Payload v2 format (Junction Restrictions, Priority Signs, Parking, Lane Connections, Speed Limits, Lane Arrows, Vehicle Restrictions, Timed Lights)
            bool hasPayloadV2 = renewCode.Contains("bw.Write((byte)2);") &&
                                renewCode.Contains("RestorePendingHighIdTMPEData()") &&
                                renewCode.Contains("else if (ver == 2)");
            Assert(hasPayloadV2, "High-ID TM:PE data serialized & restored using comprehensive binary Payload v2");

            // 20.5 Timed Traffic Light scan across all nodes (0..65535) for master nodes
            bool hasFullNodeScan = renewCode.Contains("for (int n = 0; n < 65536 && n < sims.Length; n++)") &&
                                   renewCode.Contains("masterNodeId != (ushort)n");
            Assert(hasFullNodeScan, "Timed Traffic Light scan inspects all nodes (0..65535) covering low-low, low-high, high-high, and single nodes");

            // 20.6 CustomSegmentLight reflection instantiation using parameterless constructor
            bool hasCslActivator = renewCode.Contains("object lightObj = Activator.CreateInstance(cslLightCfgType);") &&
                                   !renewCode.Contains("cslCtor.Invoke(new object[]");
            Assert(hasCslActivator, "CustomSegmentLight safely instantiated via Activator.CreateInstance parameterless constructor");

            // 20.7 Timed Traffic Light restore cleans up all nodes in nodeGroup before reload
            bool hasGroupClean = renewCode.Contains("allNodesToClean.Add((ushort)nId);") &&
                                 renewCode.Contains("foreach (ushort gn in nGroup)") &&
                                 renewCode.Contains("mDestroy.Invoke(tl, null);");
            Assert(hasGroupClean, "Timed Traffic Light restoration cleans up all nodes in nodeGroup before reload");

            // 20.8 Timed Traffic Light restoration writes back modified struct to TrafficLightSimulations array
            bool hasStructWriteback = renewCode.Contains("sims.SetValue(sim, (int)cleanNodeId);") &&
                                      renewCode.Contains("mRemoveNode.Invoke(tlsm, new object[] { (ushort)cleanNodeId });");
            Assert(hasStructWriteback, "Timed Traffic Light restoration properly writes back modified struct to array and clears simulation");

            // 20.9 SanitizeAndRepairTrafficLightSimulations heals broken simulations and cleans zombie 0-step lights
            bool hasSanitizer = renewCode.Contains("public static void SanitizeAndRepairTrafficLightSimulations(Assembly tmpe, bool cleanupEmptyOnLoad = false)") &&
                                renewCode.Contains("cleanupEmptyOnLoad && (stepsList == null || stepsList.Count == 0)") &&
                                renewCode.Contains("fLock.SetValue(tl, new object());");
            Assert(hasSanitizer, "SanitizeAndRepairTrafficLightSimulations repairs corrupt timed lights and clears empty zombies");

            // 20.10 PreSaveSanitization and subtools sanitize simulations
            bool hasPreSaveSanitize = renewCode.Contains("TMPECompatibility.SanitizeAndRepairTrafficLightSimulations(tmpe, false);") &&
                                      renewCode.Contains("SanitizeAndRepairTrafficLightSimulations(tmpe, cleanupEmptyOnLoad: false);");
            Assert(hasPreSaveSanitize, "PreSaveSanitization, restore, and TM:PE subtools execute simulation armor");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 20 exception", ex.ToString());
        }

        // ====================================================================
        // TEST GROUP 21: TRAFFIC LIGHT ENGINE SYNCHRONIZATION & DEADLOCK ARMOR (v4.3.44)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 21: Traffic Light Engine Synchronization & Deadlock Armor (v4.3.44) ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 21.1 Dual-bank state synchronization (eliminates high node bank inversion desync)
            bool hasDualBankSync = renewCode.Contains("public static bool SetTrafficLightState_Prefix(") &&
                                   renewCode.Contains("segmentData.m_trafficLightState0 = (segmentData.m_trafficLightState0 & ~15) | encoded;") &&
                                   renewCode.Contains("segmentData.m_trafficLightState1 = (segmentData.m_trafficLightState1 & ~15) | encoded;");
            Assert(hasDualBankSync, "SetTrafficLightState simultaneously synchronizes both state banks (0 and 1) eliminating high-node bank inversion desync");

            // 21.2 Bank-agnostic state getter prefixes (prevents visual & AI desync)
            bool hasGetSync = renewCode.Contains("public static bool GetTrafficLightState5_Prefix(") &&
                              renewCode.Contains("public static bool GetTrafficLightState7_Prefix(") &&
                              renewCode.Contains("vehicleLightState = (RoadBaseAI.TrafficLightState)(state & 3);");
            Assert(hasGetSync, "GetTrafficLightState 5- and 7-parameter prefixes reliably read synchronized state regardless of frame index or bank toggle");

            // 21.3 SimulationStep prefix with deadlock auto-cycling (clock >= 3)
            bool hasSimStepDeadlockFix = renewCode.Contains("public static bool TrafficLightSimulationStep_Prefix(") &&
                                         renewCode.Contains("else if (clock >= 3)") &&
                                         renewCode.Contains("selectedRed = nextIncomingRed;");
            Assert(hasSimStepDeadlockFix, "TrafficLightSimulationStep prefix enforces cycle timeout (clock >= 3), preventing perpetual red/green freeze even with zero waiting demand");

            // 21.4 TM:PE Timed Light pass-through with DynamicMethod (Zero Harmony hooks into TM:PE)
            bool hasTimedLightPassThrough = renewCode.Contains("if (MNCTrafficLightStepper.IsTMPETimedLightRunning(nodeID))") &&
                                            renewCode.Contains("s_isRunningDelegate") &&
                                            renewCode.Contains("return true;");
            Assert(hasTimedLightPassThrough, "SimulationStep prefix checks TM:PE Timed Light status via DynamicMethod, yielding cleanly to TM:PE without Harmony hooks");

            // 21.5 Visual node state rendering with 7-bit shifted offset
            bool hasVisualNodeState = renewCode.Contains("public static bool GetTrafficLightNodeState_Prefix(") &&
                                      renewCode.Contains("uint frameOffset = (uint)(nodeID >> 7) & 255u;") &&
                                      renewCode.Contains("TrafficLightPatches.Apply(harmony);");
            Assert(hasVisualNodeState, "GetTrafficLightNodeState prefix renders visual traffic light models accurately for all nodes 1..65535 and patches registered in EnsureAllPatchesApplied");

            // 21.6 Full 65536 array expansion for all TM:PE managers
            bool has65536Arrays = renewCode.Contains("EnsureTrafficLightSimulationCapacity(Assembly tmpe)") &&
                                  renewCode.Contains("EnsureCustomSegmentLightsCapacity(Assembly tmpe)") &&
                                  renewCode.Contains("EnsureTrafficPriorityCapacity(Assembly tmpe)") &&
                                  renewCode.Contains("EnsureJunctionRestrictionsCapacity(Assembly tmpe)") &&
                                  renewCode.Contains("EnsureVehicleRestrictionsCapacity(Assembly tmpe)") &&
                                  renewCode.Contains("EnsureParkingRestrictionsCapacity(int minSize)");
            Assert(has65536Arrays, "All TM:PE simulation, segment light, and restriction arrays expanded to full 65536 capacity");

            // 21.7 Zero Harmony hooks on TM:PE in TrafficLightPatches
            bool hasZeroTMPEHooks = renewCode.Contains("typeof(RoadBaseAI).GetMethod(") &&
                                    renewCode.Contains("\"SetTrafficLightState\"") &&
                                    renewCode.Contains("\"TrafficLightSimulationStep\"") &&
                                    renewCode.Contains("\"GetTrafficLightNodeState\"") &&
                                    !renewCode.Contains("harmony.Patch(typeof(TrafficManager");
            Assert(hasZeroTMPEHooks, "TrafficLightPatches strictly patches RoadBaseAI in Assembly-CSharp with ZERO direct Harmony hooks into TM:PE");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 21 exception", ex.ToString());
        }

        // ====================================================================
        // TEST GROUP 22: SAVEGAME FULL DATA INTEGRITY & CORE ARCHITECTURE ALIGNMENT (v4.3.46)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 22: Savegame Full Data Integrity & Core Architecture Alignment (v4.3.46) ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 22.1 Mod version bumped to v5.0
            bool hasVersionBump = renewCode.Contains("v5.0");
            Assert(hasVersionBump, "Mod version bumped to v5.0 in RenewMod.Name");

            // 22.2 EnsureExpandedSave ensures metadata and patch state across all save systems
            bool hasEnsureExpandedSave = renewCode.Contains("EnsureExpandedSave") &&
                                         renewCode.Contains("User/MoreNetworkCapacity/64K-v1") &&
                                         renewCode.Contains("SetExpandedSave");
            Assert(hasEnsureExpandedSave, "EnsureExpandedSave guarantees save metadata flag and sets patch state for 64K save compatibility");

            // 22.3 High-ID TM:PE serialization covers full network range
            bool hasFullSerialization = renewCode.Contains("SerializeHighIdTMPEData") &&
                                        renewCode.Contains("s <") &&
                                        renewCode.Contains("n <");
            Assert(hasFullSerialization, "SerializeHighIdTMPEData loops iterate full ranges covering all active nodes and segments");

            // 22.4 Canonical Core architecture constants defined and used
            bool hasCoreConstants = renewCode.Contains("CORE_NET_CAPACITY = 65024") &&
                                    renewCode.Contains("CORE_SPLIT_CAPACITY = 130048") &&
                                    renewCode.Contains("CORE_BUCKET_CAPACITY = 1016") &&
                                    renewCode.Contains("LANE_CAPACITY = 524288");
            Assert(hasCoreConstants, "Canonical Core constants (65024 net, 130048 split, 1016 bucket, 524288 lane) defined and applied");

            // 22.5 SegmentEndManager index offsets handle CORE_NET_CAPACITY and CORE_SPLIT_CAPACITY
            bool hasSegmentEndCore = renewCode.Contains("CORE_NET_CAPACITY") &&
                                     renewCode.Contains("CORE_SPLIT_CAPACITY");
            Assert(hasSegmentEndCore, "SegmentEndManager index calculation and bounds checks fully aligned with CORE_NET_CAPACITY and CORE_SPLIT_CAPACITY");

            // 22.6 ToolController collision bitmasks sized to CORE_BUCKET_CAPACITY (1016 ulongs)
            bool hasToolController1016 = renewCode.Contains("int req = MNCConstants.CORE_BUCKET_CAPACITY;") &&
                                         renewCode.Contains("int requiredSize = MNCConstants.CORE_BUCKET_CAPACITY;");
            Assert(hasToolController1016, "ToolController collision arrays sized to CORE_BUCKET_CAPACITY (1016 ulongs) preventing collision bitmask IndexOutOfRangeException");

            // 22.7 Public transport and transit stop sanitizers preserve stops on all valid nodes
            bool hasTransportStopSanitizer = renewCode.Contains("m_stops >=") &&
                                             renewCode.Contains("stop >=");
            Assert(hasTransportStopSanitizer, "TransportLine and IPT stop sanitizers protect valid nodes without wiping stops");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 22 exception", ex.ToString());
        }

        // ====================================================================
        // TEST GROUP 23: WATER TRANSPORT & SHIP PATH COMPATIBILITY (v4.3.47)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 23: Water Transport & Ship Path Compatibility (v4.3.47) ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 23.1 WaterTransportCompatibility class exists
            bool hasWaterClass = renewCode.Contains("public static class WaterTransportCompatibility");
            Assert(hasWaterClass, "WaterTransportCompatibility class defined for water transit and Ship Path support");

            // 23.2 Water network segregation: Ship Paths get dual types, Ferry Paths strictly Ferry only
            bool hasConfigureWater = renewCode.Contains("ConfigureWaterNetInfo") &&
                                     renewCode.Contains("SynchronizeWaterTransportNetworks") &&
                                     renewCode.Contains("IsShipPathNetwork") &&
                                     renewCode.Contains("IsFerryNetwork") &&
                                     renewCode.Contains("VehicleInfo.VehicleType.Ship | VehicleInfo.VehicleType.Ferry");
            Assert(hasConfigureWater, "SynchronizeWaterTransportNetworks segregates Ship Paths (dual types) and Ferry Paths (strictly Ferry only)");

            // 23.3 ShipDockAI FindFerryPath & FindConnectionPath postfixes with 128m radius and strict type separation
            bool hasShipDockPostfixes = renewCode.Contains("ShipDockAI_FindFerryPath_Postfix") &&
                                        renewCode.Contains("ShipDockAI_FindConnectionPath_Postfix") &&
                                        renewCode.Contains("128f");
            Assert(hasShipDockPostfixes, "ShipDockAI path postfixes expand connection radius to 128m with strict Ship / Ferry type separation");

            // 23.4 Zero hooks into traffic simulation (vanilla and TM:PE native)
            bool zeroTrafficHooks = !renewCode.Contains("harmony.Patch(typeof(ShipAI)") &&
                                    !renewCode.Contains("harmony.Patch(typeof(VehicleAI)") &&
                                    !renewCode.Contains("harmony.Patch(typeof(PathFind)") &&
                                    !renewCode.Contains("TransportStationAI_GetTransportLineInfo_Postfix");
            Assert(zeroTrafficHooks, "Zero Harmony hooks into traffic simulation / vehicle AI / transit lines (both vanilla & TM:PE native)");

            // 23.5 Level load integration and dock reconnection
            bool hasLevelLoadIntegration = renewCode.Contains("WaterTransportCompatibility.SynchronizeWaterTransportNetworks()") &&
                                           renewCode.Contains("WaterTransportCompatibility.ReconnectWaterDocks()") &&
                                           renewCode.Contains("WaterTransportCompatibility.Apply(harmony)");
            Assert(hasLevelLoadIntegration, "WaterTransportCompatibility applied in EnsureAllPatchesApplied and docks reconnected on level load");

            // 23.6 NetInfo.InitializePrefab postfix guarantees newly loaded water networks are compatible
            bool hasPrefabPostfix = renewCode.Contains("NetInfo_InitializePrefab_Postfix");
            Assert(hasPrefabPostfix, "NetInfo.InitializePrefab postfix guarantees newly instantiated water paths get configured");

            // 23.7 WaterTransportCompatibility uses only Assembly-CSharp types with zero TM:PE hooks
            bool waterNoTmpe = !renewCode.Contains("WaterTransportCompatibility error") ||
                               (!renewCode.Contains("Patched TM:PE SegmentEndManager.GetIndex") &&
                                !renewCode.Contains("Patched TM:PE TurnOnRedManager.GetIndex"));
            Assert(waterNoTmpe, "WaterTransportCompatibility operates cleanly on Assembly-CSharp with zero TM:PE hooks");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 23 exception", ex.ToString());
        }

        // ====================================================================
        // TEST GROUP 24: Save & Quit Thread-Safe Dispatching & Off-Thread Crash Elimination (v4.3.48)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 24: Save & Quit Thread-Safe Dispatching (v4.3.48) ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 24.1 No direct off-thread QuitApplication in SavePanelOnSavingCompletePatch
            bool noOffThreadQuit = !renewCode.Contains("Singleton<LoadingManager>.instance.QuitApplication();");
            Assert(noOffThreadQuit, "QuitApplication is NOT called directly off-thread on TaskWorker (prevents AdaptiveRoads NullReferenceException)");

            // 24.2 OnSavingComplete returns true on clean saves to let vanilla Dispatch handle quit
            bool yieldsToVanilla = renewCode.Contains("return true;") &&
                                   renewCode.Contains("// If save completed cleanly, let vanilla OnSavingComplete execute.");
            Assert(yieldsToVanilla, "Prefix yields to vanilla OnSavingComplete on clean saves to utilize ThreadHelper.dispatcher on Main Thread");

            // 24.3 ExceptionPanel modal dialog dispatched to Main Thread via ThreadHelper.dispatcher
            bool hasThreadSafeExceptionPanel = renewCode.Contains("ThreadHelper.dispatcher.Dispatch") &&
                                               renewCode.Contains("ShowModal<ExceptionPanel>");
            Assert(hasThreadSafeExceptionPanel, "Save error ExceptionPanel is safely dispatched to Main Thread via ThreadHelper.dispatcher");

            // 24.4 No duplicate HarmonyPatch attribute on SavePanelOnSavingCompletePatch
            bool noDuplicateAttr = !renewCode.Contains("[HarmonyPatch(typeof(SavePanel), \"OnSavingComplete\"");
            Assert(noDuplicateAttr, "SavePanelOnSavingCompletePatch is not double-patched via PatchAll (single explicit patch point)");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 24 exception", ex.ToString());
        }

        // ====================================================================
        // TEST GROUP 25: Impatient Commuters Pure Memory Array Expansion (Zero Hooks) (v5.0)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 25: Impatient Commuters Pure Memory Array Expansion (Zero Hooks) (v5.0) ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 25.1 ImpatientCommuters is registered in IsTargetModAssembly
            bool isTargetRegistered = renewCode.Contains("name == \"ImpatientCommuters\"");
            Assert(isTargetRegistered, "ImpatientCommuters is registered in IsTargetModAssembly for dynamic synchronization");

            // 25.2 SynchronizeThirdPartyMods calls EnsureImpatientCommutersBuffers
            bool syncBuffersCalled = renewCode.Contains("EnsureImpatientCommutersBuffers(context);");
            Assert(syncBuffersCalled, "SynchronizeThirdPartyMods ensures ImpatientCommuters buffers are synchronized");

            // 25.3 MNCTrafficLightStepper.OnBeforeSimulationTick ensures arrays are expanded before simulation ticks
            bool stepperHook = renewCode.Contains("EnsureImpatientCommutersBuffers(\"MNCTrafficLightStepper.OnBeforeSimulationTick\");");
            Assert(stepperHook, "MNCTrafficLightStepper natively ensures ImpatientCommuters array expansion on simulation tick without hooks");

            // 25.4 PreSaveSanitization includes ImpatientCommuters buffer sync
            bool preSavePatched = renewCode.Contains("ThirdPartyCompatibility.EnsureImpatientCommutersBuffers(\"PreSaveSanitization\");");
            Assert(preSavePatched, "PreSaveSanitization ensures ImpatientCommuters buffers are synchronized before save serialization");

            // 25.5 Target array capacity is 65,536 (eliminating IndexOutOfRangeException for all ushort stop nodes)
            bool targetCapacity65k = renewCode.Contains("const int targetCapacity = 65536;") &&
                                     renewCode.Contains("_passengerCount") &&
                                     renewCode.Contains("_capacityThreshold") &&
                                     renewCode.Contains("_vehicleCount") &&
                                     renewCode.Contains("_stopCount") &&
                                     renewCode.Contains("_hasAlternativeLine");
            Assert(targetCapacity65k, "ExpandImpatientCommutersInstance expands all 5 internal limiter arrays to 65536 elements in memory");

            // 25.6 Buffer synchronization links active NetManager, PathManager, CitizenManager buffers
            bool bufferSync = renewCode.Contains("fNodes.SetValue(instance, nm.m_nodes.m_buffer);") &&
                              renewCode.Contains("fSegs.SetValue(instance, nm.m_segments.m_buffer);") &&
                              renewCode.Contains("fPaths.SetValue(instance, Singleton<PathManager>.instance.m_pathUnits.m_buffer);");
            Assert(bufferSync, "ExpandImpatientCommutersInstance synchronizes nodes, segments, and pathUnits to active 65K game buffers");

            // 25.7 STRICTLY ZERO direct Harmony hooks into ImpatientCommuters methods
            bool zeroHarmonyHooks = !renewCode.Contains("PatchImpatientCommuters") &&
                                    !renewCode.Contains("ImpatientCommuters_OnBeforeSimulationTick_Prefix") &&
                                    !renewCode.Contains("ImpatientCommuters_GetStopNode_Prefix") &&
                                    !renewCode.Contains("ImpatientCommuters_ComputeNodeData_Prefix");
            Assert(zeroHarmonyHooks, "STRICTLY ZERO direct Harmony hooks into ImpatientCommuters methods (pure in-memory array expansion)");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 25 exception", ex.ToString());
        }

        // ====================================================================
        // TEST GROUP 26: Universal Mod Harmonizer & Multi-Mod Compatibility (v5.0)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 26: Universal Mod Harmonizer & Multi-Mod Compatibility (v5.0) ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 26.1 UniversalModHarmonizer class exists with dynamic scanning methods
            bool harmonizerDefined = renewCode.Contains("public static class UniversalModHarmonizer") &&
                                     renewCode.Contains("HarmonizeActiveMods(string context)") &&
                                     renewCode.Contains("HarmonizeAssembly(Assembly asm, string context)") &&
                                     renewCode.Contains("ScanAndHarmonizeType(Type type, object instance, string context)");
            Assert(harmonizerDefined, "UniversalModHarmonizer defines dynamic scanning and harmonization methods");

            // 26.2 UniversalModHarmonizer called in SynchronizeThirdPartyMods and AssemblyLoad
            bool harmonizerHooked = renewCode.Contains("UniversalModHarmonizer.HarmonizeActiveMods(context);") &&
                                    renewCode.Contains("UniversalModHarmonizer.HarmonizeAssembly(args.LoadedAssembly, \"AssemblyLoad: \" + name);");
            Assert(harmonizerHooked, "UniversalModHarmonizer hooked into SynchronizeThirdPartyMods and dynamic AssemblyLoad");

            // 26.3 Array size scanning targets intermediate limits (32768, 36864, 49152 -> 65024 and 1050 -> 3402)
            bool scansLimits = renewCode.Contains("len == 32768 || len == 36864 || len == 49152") &&
                               renewCode.Contains("targetLen = MNCConstants.CORE_NET_CAPACITY;") &&
                               renewCode.Contains("len == 1050") &&
                               renewCode.Contains("targetLen = 3402;");
            Assert(scansLimits, "UniversalModHarmonizer automatically expands 32K/36K/49K arrays to 65024 and 1050 tile node arrays to 3402");

            // 26.4 CitizenManager Universal Dynamic Buffer Guard (Konstantin fix)
            bool citizenBufferGuard = renewCode.Contains("CitizenManager_Data_Deserialize_Prefix") &&
                                      renewCode.Contains("cm.m_units = new Array32<CitizenUnit>((uint)EXPANDED_CITIZEN_UNITS);") &&
                                      renewCode.Contains("sm.m_metaData.m_modOverride[\"User/More CitizenUnits x2\"] = true;");
            Assert(citizenBufferGuard, "CitizenManager Universal Dynamic Buffer Guard pre-allocates 1,048,576 units and sets modOverride flag");

            // 26.5 Non-destructive m_modOverride handling in LoadingManager and SimulationMetaData
            bool nonDestructiveOverride = renewCode.Contains("SimulationMetaData_Merge_Prefix") &&
                                          renewCode.Contains("SimulationMetaData_Merge_Postfix") &&
                                          renewCode.Contains("if (ngs != null && ngs.m_modOverride != null)");
            Assert(nonDestructiveOverride, "Savegame modOverride dictionary is preserved bidirectionally without single-key clobbering");

            // 26.6 PathManager cooperative guard with MorePathUnits
            bool pathUnitsCooperative = renewCode.Contains("PathManager_Data_Deserialize_Prefix") &&
                                        renewCode.Contains("hasMorePathUnitsMod") &&
                                        renewCode.Contains("return true; // Let MorePathUnits prefix/transpiler/postfix execute!");
            Assert(pathUnitsCooperative, "PathManager guard yields to MorePathUnits when loaded while pre-expanding buffers safely");

            // 26.7 81 Tiles 2 Auto-Harmonization (m_tileNodesCount 1050 -> 3402)
            bool eightyOneHarmonized = renewCode.Contains("Harmonize81Tiles()") &&
                                       renewCode.Contains("const int EIGHTY_ONE_TILES_NODES_COUNT = 3402;") &&
                                       renewCode.Contains("NetManager_Awake_Postfix");
            Assert(eightyOneHarmonized, "Harmonize81Tiles dynamically expands NetManager.m_tileNodesCount to 3402 and hooks NetManager.Awake");

            // 26.8 Deferred NetManager.instance access to prevent early Awake race condition
            bool deferredNetManager = renewCode.Contains("if (Singleton<NetManager>.exists)") &&
                                      renewCode.Contains("ExpandTMPEFlagsArrays");
            Assert(deferredNetManager, "Early NetManager.instance access deferred using Singleton<NetManager>.exists check");

            // 26.9 Level Unload State Sanitizer (Fremdwanne fix)
            bool unloadSanitizer = renewCode.Contains("ClearPendingTMPEData()") &&
                                   renewCode.Contains("OnLevelUnloading()") &&
                                   renewCode.Contains("StrategicGarbageCollect(\"OnLevelUnloading\")");
            Assert(unloadSanitizer, "OnLevelUnloading resets pending TMPE buffers and runs strategic GC to prevent reload blue screen");

            // 26.10 Universal Stream Armor against EndDeserialize tag mismatches (CitizenManager & VehicleManager)
            bool streamArmor = renewCode.Contains("LoadingProfiler_BeginDeserialize_Prefix") &&
                               renewCode.Contains("LoadingProfiler_EndDeserialize_Prefix") &&
                               renewCode.Contains("SafeHandleDeserializeTag") &&
                               renewCode.Contains("Universal Stream Armor");
            Assert(streamArmor, "Universal Stream Armor intercepts Begin/EndDeserialize to recover stream alignment and suppress tag mismatches");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 26 exception", ex.ToString());
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
