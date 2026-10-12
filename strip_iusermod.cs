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

            asm.Write(outputDll);
            Console.WriteLine("Saved stripped core assembly to: " + outputDll);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error stripping IUserMod: " + ex);
            return 1;
        }
    }
}
