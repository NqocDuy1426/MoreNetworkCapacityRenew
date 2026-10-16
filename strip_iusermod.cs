using System;
using System.IO;
using Mono.Cecil;

class StripIUserMod
{
    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: strip_iusermod.exe <input.dll> <output.dll>");
            return 1;
        }

        string inputDll = args[0];
        string outputDll = args[1];

        if (!File.Exists(inputDll))
        {
            Console.WriteLine("Error: Input file does not exist: " + inputDll);
            return 1;
        }

        try
        {
            var asm = AssemblyDefinition.ReadAssembly(inputDll);
            var modType = asm.MainModule.GetType("MoreNetworkCapacity.Mod");
            if (modType != null)
            {
                modType.Interfaces.Clear();
                Console.WriteLine("Successfully stripped IUserMod from " + modType.FullName);
            }
            else
            {
                Console.WriteLine("Warning: MoreNetworkCapacity.Mod type not found.");
            }

            // Strip watchdog thread in SimulationStepDiagnostics
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
                        m.Body.Instructions.Add(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ret));
                        Console.WriteLine("Successfully stripped watchdog thread method: " + m.Name);
                    }
                }
            }

            // Verify no Thread::.ctor calls remain in the core assembly
            int threadCount = 0;
            foreach (var t in asm.MainModule.Types)
            {
                foreach (var m in t.Methods)
                {
                    if (m.HasBody)
                    {
                        foreach (var inst in m.Body.Instructions)
                        {
                            if (inst.Operand != null && inst.Operand.ToString().Contains("Thread::.ctor"))
                            {
                                threadCount++;
                            }
                        }
                    }
                }
            }

            if (threadCount > 0)
            {
                Console.WriteLine("ERROR: Found " + threadCount + " Thread::.ctor call(s) still remaining in core assembly!");
                return 1;
            }
            Console.WriteLine("Verified: 0 background Thread creation calls in core assembly.");

            asm.Write(outputDll);
            Console.WriteLine("Saved stripped core assembly to: " + outputDll);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error stripping core assembly: " + ex);
            return 1;
        }
    }
}
