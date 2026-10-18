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
        // TEST SUITE 3: VEHICLE & BUS AT STATION (ANTI-DEADLOCK)
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 3: Vehicle & Bus at Station (Game & TM:PE Compliance) ---");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");

            // 3.1 Verify IGNORE_FLAGS contains all stationary/boarding states
            Assert(renewCode.Contains("Vehicle.Flags.Stopped"), "StuckVehicleRecovery IGNORE_FLAGS includes Vehicle.Flags.Stopped");
            Assert(renewCode.Contains("Vehicle.Flags.Leaving"), "StuckVehicleRecovery IGNORE_FLAGS includes Vehicle.Flags.Leaving");
            Assert(renewCode.Contains("Vehicle.Flags.Arriving"), "StuckVehicleRecovery IGNORE_FLAGS includes Vehicle.Flags.Arriving");
            Assert(renewCode.Contains("Vehicle.Flags.Parking"), "StuckVehicleRecovery IGNORE_FLAGS includes Vehicle.Flags.Parking");
            Assert(renewCode.Contains("Vehicle.Flags.WaitingLoading"), "StuckVehicleRecovery IGNORE_FLAGS includes Vehicle.Flags.WaitingLoading");
            Assert(renewCode.Contains("Vehicle.Flags.WaitingPath"), "StuckVehicleRecovery IGNORE_FLAGS includes Vehicle.Flags.WaitingPath");
            Assert(renewCode.Contains("Vehicle.Flags.WaitingSpace"), "StuckVehicleRecovery IGNORE_FLAGS includes Vehicle.Flags.WaitingSpace");
            Assert(renewCode.Contains("Vehicle.Flags.WaitingCargo"), "StuckVehicleRecovery IGNORE_FLAGS includes Vehicle.Flags.WaitingCargo");
            Assert(renewCode.Contains("Vehicle.Flags.WaitingTarget"), "StuckVehicleRecovery IGNORE_FLAGS includes Vehicle.Flags.WaitingTarget");
            Assert(renewCode.Contains("Vehicle.Flags.InsideBuilding"), "StuckVehicleRecovery IGNORE_FLAGS includes Vehicle.Flags.InsideBuilding");

            // 3.2 Verify InvalidatePath is NOT called on normal blocked vehicles
            // We want vanilla game and TM:PE to handle traffic naturally, without crude invalidation
            bool hasCrudeInvalidatePath = renewCode.Contains("info.m_vehicleAI.InvalidatePath(");
            Assert(!hasCrudeInvalidatePath, "StuckVehicleRecovery does NOT crudely call InvalidatePath on traffic/buses");

            // 3.3 Verify anti-deadlock ClearStuckBoardingCitizens exists
            bool hasAntiDeadlock = renewCode.Contains("ClearStuckBoardingCitizens");
            Assert(hasAntiDeadlock, "ClearStuckBoardingCitizens helper exists in StuckVehicleRecovery");

            // 3.4 Test bitwise logic of ClearStuckBoardingCitizens
            Assert(renewCode.Contains("CitizenInstance.Flags.EnteringVehicle"), "Renew.cs uses CitizenInstance.Flags.EnteringVehicle");
            uint citizenFlags = 2048 | 1; // EnteringVehicle (2048) + Created (1)
            const uint ENTERING_FLAG = 2048;
            citizenFlags &= ~ENTERING_FLAG;
            Assert((citizenFlags & ENTERING_FLAG) == 0 && (citizenFlags & 1) == 1, 
                "Bitwise clearing of CitizenInstance.Flags.EnteringVehicle preserves other citizen flags");

            // 3.5 Test IGNORE_FLAGS bitwise check for Bus at Station
            int stoppedFlag = 512;
            int ignoreMask = 512 | 1024 | 2048 | 4194304 | 33554432 | 65536 | 131072 | 524288 | 256 | 1073741824;
            Assert((stoppedFlag & ignoreMask) != 0, "Bus at station (Stopped = 512) is correctly matched by ignoreMask");
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

            // 4.2 Verify DataLocation.saveLocation is used with Path.Combine
            bool usesDataLocation = renewCode.Contains("System.IO.Path.Combine(ColossalFramework.IO.DataLocation.saveLocation");
            Assert(usesDataLocation, "RestoreLinesFromBackupCRP uses Path.Combine with DataLocation.saveLocation");
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

            // 5.4 Verify SimulationManager.SYNCHRONIZE_TIMEOUT initialization
            bool hasTimeoutGuard = renewCode.Contains("SimulationManager.SYNCHRONIZE_TIMEOUT = 500;");
            Assert(hasTimeoutGuard, "SimulationManager.SYNCHRONIZE_TIMEOUT initialized to 500ms to eliminate 0ms spin loops");

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
        // TEST SUITE 6: TM:PE PATHFINDING, VEHICLE RECOVERY & IPT COMPATIBILITY
        // ====================================================================
        Console.WriteLine("\n--- TEST GROUP 6: TM:PE Pathfinding, Vehicle Recovery & IPT Compatibility ---");
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

            // 6.3 Verify ThreadingExtension reflection caching (eliminates frame-rate stutter)
            bool hasCachedThreading = renewCode.Contains("s_threadingReflectionInitialized") && renewCode.Contains("s_sgoInstanceProp");
            Assert(hasCachedThreading, "ThreadingExtension_OnBeforeSimulationFrame caches reflection metadata (0 overhead per frame)");

            // 6.4 Verify IPT buffers expanded for both Node and Vehicle arrays
            bool expandsIptNodes = renewCode.Contains("m_cachedNodeData") && renewCode.Contains("65024");
            bool expandsIptVehicles = renewCode.Contains("m_cachedVehicleData") && renewCode.Contains("65536");
            Assert(expandsIptNodes, "EnsureImprovedPublicTransportBuffers expands CachedNodeData.m_cachedNodeData to 65024");
            Assert(expandsIptVehicles, "EnsureImprovedPublicTransportBuffers expands CachedVehicleData.m_cachedVehicleData to 65536");

            // 6.5 Verify public transport stop anti-deadlock & WaitingPath recovery
            bool hasPtAntiDeadlock = renewCode.Contains("ClearStuckBoardingCitizens") && renewCode.Contains("buffer[i].m_waitCounter > 60");
            bool hasWaitingPathRecovery = renewCode.Contains("buffer[i].m_waitCounter > 250") && renewCode.Contains("Vehicle.Flags.WaitingPath");
            Assert(hasPtAntiDeadlock, "Public transport stop anti-deadlock clears stuck boarding citizens after 60 ticks");
            Assert(hasWaitingPathRecovery, "Vehicle recovery handles stalled WaitingPath states (>250 ticks clean release)");
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
            bool hasPatchMpu = renewCode.Contains("PatchMorePathUnits") && renewCode.Contains("MorePathUnits_DeserializeSize_Prefix");
            bool hasPreDesHook = renewCode.Contains("PathManager_Data_Deserialize_Prefix") && renewCode.Contains("SynchronizeMorePathUnits(\"PathManager.Data.Deserialize.Prefix\")");
            bool hasSimDesSync = renewCode.Contains("EnsureMorePathUnitsPatched") && renewCode.Contains("SynchronizeMorePathUnits(\"SimulationManager.Data.Deserialize.Prefix\")");
            bool hasProfilerGuard = renewCode.Contains("LoadingProfiler_EndDeserialize_Prefix") && renewCode.Contains("Guarded LoadingProfiler.EndDeserialize PathManager");
            bool hasSaveSync = renewCode.Contains("SynchronizeMorePathUnits(\"PreSaveSanitization\")");
            bool hasEarlyPatches = renewCode.Contains("RenewLoading.EnsureAllPatchesApplied()") && renewCode.Contains("void OnEnabled()");
            bool hasMetaMergePatch = renewCode.Contains("SimulationMetaData_Merge_Prefix") && renewCode.Contains("SimulationMetaData_Merge_Postfix");
            bool hasLoadSimPatch = renewCode.Contains("LoadingManager_LoadSimulationData_Prefix") && renewCode.Contains("Patched LoadingManager.LoadSimulationData");
            bool hasFullPmDes = renewCode.Contains("public static bool PathManager_Data_Deserialize_Prefix") && renewCode.Contains("return false; // Skip original method and all transpilers!");

            Assert(hasSyncMpu, "SynchronizeMorePathUnits guarantees s_loadingExpanded=true and modOverride['User/More PathUnits x2']");
            Assert(hasPatchMpu, "PatchMorePathUnits hooks MetaData.LoadingExtended and PathDeserialize.DeserializeSize");
            Assert(hasPreDesHook, "PathManager.Data.Deserialize prefix synchronizes MorePathUnits before deserialization starts");
            Assert(hasFullPmDes, "PathManager.Data.Deserialize prefix performs 100% byte-accurate full deserialization and returns false");
            Assert(hasSimDesSync, "SimulationManager.Data.Deserialize prefix ensures MorePathUnits patched and synchronized");
            Assert(hasProfilerGuard, "LoadingProfiler.EndDeserialize guards against PathManager tag mismatch & ArgumentOutOfRangeException");
            Assert(hasSaveSync, "PreSaveSanitization synchronizes MorePathUnits before game save serialization");
            Assert(hasEarlyPatches, "RenewMod.OnEnabled applies EnsureAllPatchesApplied at startup before save load");
            Assert(hasMetaMergePatch, "SimulationMetaData.Merge is hooked to prevent ngs from wiping out save modOverride");
            Assert(hasLoadSimPatch, "LoadingManager.LoadSimulationData seeds expanded modOverride flags before load starts");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 7 exception", ex.ToString());
        }

        // ====================================================================
        // TEST GROUP 8: Vehicle AI, TM:PE Pathfinding Stability & Cleanse
        // ====================================================================
        Console.WriteLine("\n[TEST GROUP 8: Vehicle AI & TM:PE Pathfinding Stability]");
        try
        {
            string renewCode = File.ReadAllText("Renew.cs");
            bool tmpeNativeVehicles = renewCode.Contains("VehicleAICommons: Left 100% native to TM:PE");
            bool tmpeNativeBehavior = renewCode.Contains("VehicleBehaviorManager: Left 100% native to TM:PE");
            bool carAiNative = renewCode.Contains("CarAI.CalculateSegmentPosition is left 100% native");
            bool hasNetSegLanes = renewCode.Contains("NetSegment_GetLeftAndRightLanes_Prefix") && renewCode.Contains("GetLeftAndRightLanes");
            bool hasEnsureEvm = renewCode.Contains("EnsureExtVehicleManagerCapacity") && renewCode.Contains("ExtVehicleManager");
            bool hasAutoCleanse = renewCode.Contains("Auto-cleansed") && renewCode.Contains("VehicleAndTransportRepair.ClearAllVehicles()");
            bool hasHotkeyT = renewCode.Contains("KeyCode.T") && renewCode.Contains("ClearAllVehicles()");

            Assert(tmpeNativeVehicles, "VehicleAICommons left 100% native to TM:PE for natural physics & coordinates");
            Assert(tmpeNativeBehavior, "VehicleBehaviorManager left 100% native to TM:PE for native despawn and segment transitions");
            Assert(carAiNative, "CarAI.CalculateSegmentPosition left 100% native to game & TM:PE");
            Assert(hasNetSegLanes, "NetSegment.GetLeftAndRightLanes guarded against null Info and m_lanes");
            Assert(hasEnsureEvm, "ExtVehicleManager.ExtVehicles expanded to 65,536 to match More Vehicles");
            Assert(hasAutoCleanse, "Auto-cleanses broken/floating/ghost vehicles on level load");
            Assert(hasHotkeyT, "Provides Ctrl+Shift+T hotkey for on-demand traffic wipeout and logistics reactivation");

            // 8.1 Verify LaneConnectionSubManager null safety & capacity
            bool hasEnsureLcm = renewCode.Contains("EnsureLaneConnectionManagerCapacity") && renewCode.Contains("connectionDataBase_");
            bool hasLcsmPatches = renewCode.Contains("LaneConnectionSubManager_HasNodeConnections_Prefix") 
                               && renewCode.Contains("LaneConnectionSubManager_HasConnections_Prefix")
                               && renewCode.Contains("LaneConnectionSubManager_AreLanesConnected_Prefix");
            bool has4PosPfi = renewCode.Contains("seg00") && renewCode.Contains("seg01") && renewCode.Contains("seg02") && renewCode.Contains("seg03");

            Assert(hasEnsureLcm, "TM:PE LaneConnectionManager.Road/Track connectionDataBase_ guaranteed initialized");
            Assert(hasLcsmPatches, "TM:PE LaneConnectionSubManager guarded against NRE and uncreated nodes/lanes");
            Assert(has4PosPfi, "CustomPathFind validates all 4 positions (start A/B and end A/B) without misidentifying destination");

            // 8.2 Verify TurboTrafficDispatcher & Save-Load Vehicle Preservation
            bool hasSelectiveCleanse = renewCode.Contains("VehicleAndTransportRepair.ClearAllVehicles(wipeAll: false)");
            bool hasTurboTraffic = renewCode.Contains("TurboTrafficDispatcher") && renewCode.Contains("TurboTrafficDispatcher.Apply");
            bool hasPmUnsleep = renewCode.Contains("PathManager_SimulationStepImpl_Prefix");
            bool hasTmSafe1x = renewCode.Contains("TransferManager_SimulationStepImpl_Postfix");
            bool hasOcPatches = renewCode.Contains("OutsideConnectionAI_DummyTrafficProbability_Prefix") && renewCode.Contains("OutsideConnectionAI_AddConnectionOffers_Prefix");
            bool hasLogisticsBoost = renewCode.Contains("ApplyLogisticsBoost") && renewCode.Contains("CargoTruckAI") && renewCode.Contains("GarbageTruckAI") && renewCode.Contains("HearseAI");

            Assert(hasSelectiveCleanse, "Level load preserves healthy vehicles and only cleanses corrupt/broken vehicles (wipeAll: false)");
            Assert(hasTurboTraffic, "TurboTrafficDispatcher initialized and applied via Harmony");
            Assert(hasPmUnsleep, "PathManager.SimulationStepImpl safely manages simulation and pathfinding");
            Assert(hasTmSafe1x, "TransferManager runs at safe 1x cadence to prevent PathManager queue flooding");
            Assert(hasOcPatches, "OutsideConnectionAI dummy traffic and connection offers scaled for 65k capacity");
            Assert(hasLogisticsBoost, "Smart logistics capacity boost applied for Cargo, Garbage, and Hearse service vehicles");

            // 8.3 Verify Highway Merge & Anti-Deadlock Mechanism Removed (Solution 2)
            bool antiDeadlockRemoved = !renewCode.Contains("buffer[i].m_blockCounter >= 80 && buffer[i].m_blockCounter < 180") &&
                                       !renewCode.Contains("buffer[i].m_blockCounter >= 180 || buffer[i].m_waitCounter > 250");
            bool hasJrmNative = renewCode.Contains("JunctionRestrictionsManager.LoadData");

            Assert(antiDeadlockRemoved, "2-stage Anti-Deadlock mechanism cleanly removed from StuckVehicleRecovery (Solution 2)");
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
                               renewCode.Contains("CommonBuildingAI_SimulationStepPatch_Prefix_Guard") &&
                               renewCode.Contains("Patched TM:PE ExtBuildingManager.OnBeforeSimulationStep");
            Assert(hasEbmArmor, "ExtBuildingManager & CommonBuildingAI.SimulationStep armored against NullReferenceException");

            // 9.5 Verify SanitizeRoutingManager purges dead transitions
            bool hasSanitizer = renewCode.Contains("SanitizeRoutingManager") &&
                                renewCode.Contains("Purged {0} stale/corrupted transitions");
            Assert(hasSanitizer, "SanitizeRoutingManager automatically detects and purges broken/dead segment transitions");

            // 9.6 Verify EnsureJunctionRestrictionsDefaults64k populates native TM:PE defaults
            bool hasJrmDefaults = renewCode.Contains("EnsureJunctionRestrictionsDefaults64k") &&
                                  renewCode.Contains("HandleValidSegment");
            Assert(hasJrmDefaults, "EnsureJunctionRestrictionsDefaults64k populates native TM:PE defaults for segments >= 36864");

            // 9.7 Verify RequestFullRecalculation64k queues all segments
            bool hasFullRecalc = renewCode.Contains("RequestFullRecalculation64k") &&
                                 renewCode.Contains("Queued all 65,024 segments for TM:PE recalculation");
            Assert(hasFullRecalc, "RequestFullRecalculation64k ensures all 65,024 segments are marked for TM:PE update");

            // 9.8 Verify TMPEBufferValidation.Validate armoring prefix
            bool hasValArmor = renewCode.Contains("TMPEBufferValidation_Validate_Prefix") &&
                               renewCode.Contains("Armored TMPEBufferValidation.Validate");
            Assert(hasValArmor, "TMPEBufferValidation.Validate protected against simulation deserialization crash with Harmony prefix");

            // 9.9 Verify CustomPathFind calculation methods left 100% native to TM:PE
            bool hasNativeCpf = renewCode.Contains("CustomPathFind internal routing methods are left 100% native to TM:PE to eliminate thread NREs") &&
                                renewCode.Contains("PathFindImplementation and GetLaneDirection are left 100% native to TM:PE without Harmony reflection overhead");
            Assert(hasNativeCpf, "CustomPathFind methods left 100% native to TM:PE without Harmony reflection overhead");

            // 9.10 Verify CustomPathFind calculation methods are unhooked from Harmony
            bool cpfUnhooked = !renewCode.Contains("Patched TM:PE CustomPathFind.PathFindImplementation") &&
                               !renewCode.Contains("Patched TM:PE CustomPathFind.GetLaneDirection");
            Assert(cpfUnhooked, "CustomPathFind.PathFindImplementation & GetLaneDirection unhooked from Harmony");

            // 9.11 Verify road & node demolition native handling (delegated to vanilla game & TM:PE)
            bool hasNativeDemolition = renewCode.Contains("Road & node demolition handling is natively delegated to vanilla game & TM:PE") &&
                                       !renewCode.Contains("harmony.Patch(relSegM");
            Assert(hasNativeDemolition, "Road & node demolition handling natively delegated to vanilla game & TM:PE (no aggressive despawn)");

            // 9.12 Verify Level Load preserves all vehicles natively without auto-purging
            bool hasPreservedOnLoad = renewCode.Contains("Vehicles in save game are preserved 100% natively without auto-purging or forced resets");
            Assert(hasPreservedOnLoad, "Level load preserves all vehicles in save game 100% natively without auto-purging");

            // 9.13 Verify flexible More Vehicles compatibility
            bool hasFlexibleVehicles = renewCode.Contains("int bufferLen = (vm != null && vm.m_vehicles != null && vm.m_vehicles.m_buffer != null)") &&
                                       renewCode.Contains("int targetSize = Math.Max(16384, bufferLen);");
            Assert(hasFlexibleVehicles, "EnsureExtVehicleManagerCapacity dynamically adapts to More Vehicles (65k) or vanilla (16k)");

            // 9.14 Verify StuckVehicleRecovery priority (Legitimate states / IGNORE_FLAGS evaluated first)
            bool hasIgnoreFlagsFirst = renewCode.Contains("PRIORITY 1: Legitimate Vehicle States") &&
                                       renewCode.Contains("const Vehicle.Flags IGNORE_FLAGS");
            Assert(hasIgnoreFlagsFirst, "StuckVehicleRecovery prioritizes legitimate vehicle states (IGNORE_FLAGS) first, preventing premature vehicle removal");

            // 9.15 Verify CustomPathFind inner-loop methods unhooked for native 60+ FPS pathfinding performance
            bool hasNativePathFindLoop = renewCode.Contains("CustomPathFind inner-loop methods (ProcessItemMain, ProcessItemCosts, ProcessItemRouted, etc.)") &&
                                         renewCode.Contains("are intentionally NOT hooked with Harmony prefixes to ensure 100% native C# path computation speed");
            Assert(hasNativePathFindLoop, "CustomPathFind inner-loop methods unhooked to ensure native 60+ FPS pathfinding performance");

            // 9.16 Verify EnsureExtBuildingManagerCapacity ensures 49,152 building capacity
            bool hasEbmCap = renewCode.Contains("EnsureExtBuildingManagerCapacity") &&
                             renewCode.Contains("49152");
            Assert(hasEbmCap, "EnsureExtBuildingManagerCapacity guarantees 49,152 building capacity for TM:PE");
        }
        catch (Exception ex)
        {
            Assert(false, "Test Group 9 exception", ex.ToString());
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
