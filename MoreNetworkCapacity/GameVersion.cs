using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;

namespace MoreNetworkCapacity {

internal static class GameVersion
{
	private const string TargetAssemblyHash = "DF557994A03C93D8BB4A14F80B1FBCC0FB29D547658442445D078D069BCDD9FE";

	private static readonly Guid TargetModuleVersionId = new Guid("e8d46364-9995-4176-87e0-ae3192b9a20b");

	private static bool _checked;

	private static bool _supported;

	public static bool IsSupported
	{
		get
		{
			return true;
		}
	}

	private static void EnsureChecked()
	{
		if (_checked)
		{
			return;
		}
		_checked = true;
		Assembly assembly = typeof(NetManager).Assembly;
		Guid moduleVersionId = assembly.ManifestModule.ModuleVersionId;
		if (moduleVersionId != TargetModuleVersionId)
		{
			Logging.Error("Assembly-CSharp MVID does not match the supported build.");
			return;
		}
		string text = ResolveAssemblyPath(assembly);
		if (text == null)
		{
			Logging.Error("Could not locate the loaded Assembly-CSharp file.");
			return;
		}
		string a = ComputeHash(text);
		if (!string.Equals(a, "DF557994A03C93D8BB4A14F80B1FBCC0FB29D547658442445D078D069BCDD9FE", StringComparison.OrdinalIgnoreCase))
		{
			Logging.Error("Assembly-CSharp SHA-256 does not match the supported build: " + text);
			return;
		}
		_supported = true;
		Logging.Info("Supported game assembly detected: " + text);
	}

	private static string ResolveAssemblyPath(Assembly assembly)
	{
		string location = assembly.Location;
		if (IsExistingFile(location))
		{
			return location;
		}
		string fullyQualifiedName = assembly.ManifestModule.FullyQualifiedName;
		if (IsExistingFile(fullyQualifiedName))
		{
			return fullyQualifiedName;
		}
		string dataPath = Application.dataPath;
		if (!string.IsNullOrEmpty(dataPath))
		{
			string text = Path.Combine(Path.Combine(dataPath, "Managed"), "Assembly-CSharp.dll");
			if (IsExistingFile(text))
			{
				return text;
			}
		}
		return null;
	}

	private static bool IsExistingFile(string path)
	{
		if (!string.IsNullOrEmpty(path))
		{
			return File.Exists(path);
		}
		return false;
	}

	private static string ComputeHash(string path)
	{
		using FileStream inputStream = File.OpenRead(path);
		using SHA256 sHA = SHA256.Create();
		byte[] array = sHA.ComputeHash(inputStream);
		return BitConverter.ToString(array).Replace("-", string.Empty);
	}
}

}
