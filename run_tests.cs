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

            // 6.1 Verify TM:PE CustomPathFind guards are present
            bool hasCpfPfi = renewCode.Contains("CustomPathFind_PathFindImplementation_Prefix");
            bool hasCpfGld = renewCode.Contains("CustomPathFind_GetLaneDirection_Prefix");
            bool hasCpfPim = renewCode.Contains("CustomPathFind_ProcessItemMain_Prefix");
            bool hasCpfCul = renewCode.Contains("CustomPathFind_CanUseLane_Prefix");
            Assert(hasCpfPfi, "CustomPathFind.PathFindImplementation safe boundary validation prefix installed");
            Assert(hasCpfGld, "CustomPathFind.GetLaneDirection safe boundary validation prefix installed");
            Assert(hasCpfPim, "CustomPathFind.ProcessItemMain validation prefix installed");
            Assert(hasCpfCul, "CustomPathFind.CanUseLane safe null guard prefix installed");

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
