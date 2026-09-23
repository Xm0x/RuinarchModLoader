using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

// Static Harmony-patch checker. Resolves every class-level [HarmonyPatch(typeof(T), "Method"
// [, Type[] args])] in the given assemblies against the REAL game DLLs, without running the
// game. Catches the failures that otherwise only surface at launch (and abort a whole
// PatchAll): a target method that does not exist, an ambiguous overload with no argument
// list, and a patch parameter whose name does not exist on the target.
//
// Usage: PatchCheck <search-dir>[;<search-dir>...] <assembly.dll> [<assembly.dll>...]
// Exit code 0 = every patch resolves; 1 = at least one failure.
internal static class Program
{
	private static readonly HashSet<string> PatchMethodNames = new HashSet<string> { "Prefix", "Postfix", "Finalizer" };

	private static readonly HashSet<string> Injected = new HashSet<string>
	{
		"__instance", "__result", "__state", "__originalMethod", "__args", "__runOriginal", "__exception"
	};

	private static int Main(string[] args)
	{
		if (args.Length < 2)
		{
			Console.Error.WriteLine("usage: PatchCheck <search-dir>[;<search-dir>...] <assembly.dll>...");
			return 2;
		}
		var resolver = new DefaultAssemblyResolver();
		foreach (string dir in args[0].Split(';', StringSplitOptions.RemoveEmptyEntries))
		{
			resolver.AddSearchDirectory(dir);
		}
		int failures = 0;
		int checkedCount = 0;
		foreach (string path in args.Skip(1))
		{
			resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
			var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver });
			foreach (TypeDefinition patch in AllTypes(asm.MainModule.Types))
			{
				var attrs = patch.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch").ToList();
				if (attrs.Count == 0)
				{
					continue;
				}
				checkedCount++;
				string error = Check(patch, attrs, out string target);
				if (error != null)
				{
					failures++;
					Console.WriteLine($"FAIL {Path.GetFileName(path)}: {patch.FullName} -> {target}: {error}");
				}
				else
				{
					Console.WriteLine($"ok   {Path.GetFileName(path)}: {patch.FullName} -> {target}");
				}
			}
		}
		Console.WriteLine($"{checkedCount} patch class(es) checked, {failures} failure(s).");
		return failures == 0 ? 0 : 1;
	}

	private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
	{
		foreach (TypeDefinition t in types)
		{
			yield return t;
			foreach (TypeDefinition n in AllTypes(t.NestedTypes))
			{
				yield return n;
			}
		}
	}

	private static string Check(TypeDefinition patch, List<CustomAttribute> attrs, out string target)
	{
		TypeReference targetTypeRef = null;
		string methodName = null;
		TypeReference[] argTypes = null;
		foreach (CustomAttribute a in attrs)
		{
			foreach (CustomAttributeArgument arg in a.ConstructorArguments)
			{
				if (arg.Value is TypeReference tr && targetTypeRef == null)
				{
					targetTypeRef = tr;
				}
				else if (arg.Value is string s && methodName == null)
				{
					methodName = s;
				}
				else if (arg.Value is CustomAttributeArgument[] arr && argTypes == null)
				{
					argTypes = arr.Select(x => (TypeReference)x.Value).ToArray();
				}
			}
		}
		target = (targetTypeRef?.FullName ?? "?") + "." + (methodName ?? "?");
		if (targetTypeRef == null || methodName == null)
		{
			return "attribute form not statically resolvable (needs typeof(T) + method name)";
		}
		TypeDefinition targetType;
		try
		{
			targetType = targetTypeRef.Resolve();
		}
		catch (Exception e)
		{
			return "target type unresolvable: " + e.Message;
		}
		if (targetType == null)
		{
			return "target type unresolvable";
		}

		// Harmony's AccessTools.Method searches the declaring type, then base types.
		List<MethodDefinition> candidates = new List<MethodDefinition>();
		for (TypeDefinition t = targetType; t != null && candidates.Count == 0; t = SafeResolve(t.BaseType))
		{
			candidates.AddRange(t.Methods.Where(m => m.Name == methodName));
		}
		if (candidates.Count == 0)
		{
			return "no method named '" + methodName + "' on the target or its base types";
		}

		MethodDefinition method;
		if (argTypes != null)
		{
			method = candidates.FirstOrDefault(m => m.Parameters.Count == argTypes.Length
				&& m.Parameters.Select(p => Strip(p.ParameterType)).SequenceEqual(argTypes.Select(Strip)));
			if (method == null)
			{
				return "no overload matching (" + string.Join(", ", argTypes.Select(Strip)) + "); have: "
					+ string.Join(" | ", candidates.Select(Signature));
			}
		}
		else if (candidates.Count > 1)
		{
			return "ambiguous: " + candidates.Count + " overloads and no argument types given: "
				+ string.Join(" | ", candidates.Select(Signature));
		}
		else
		{
			method = candidates[0];
		}
		target += "(" + string.Join(", ", method.Parameters.Select(p => Strip(p.ParameterType))) + ")";

		foreach (MethodDefinition pm in patch.Methods.Where(m => m.IsStatic && PatchMethodNames.Contains(m.Name)))
		{
			foreach (ParameterDefinition p in pm.Parameters)
			{
				string err = CheckParameter(p, method, targetType);
				if (err != null)
				{
					return pm.Name + " parameter '" + p.Name + "': " + err;
				}
			}
		}
		return null;
	}

	private static string CheckParameter(ParameterDefinition p, MethodDefinition method, TypeDefinition targetType)
	{
		string name = p.Name;
		if (Injected.Contains(name))
		{
			if (name == "__instance" && method.IsStatic)
			{
				return "__instance on a static target";
			}
			if (name == "__result" && method.ReturnType.FullName == "System.Void")
			{
				return "__result on a void target";
			}
			return null;
		}
		if (name.StartsWith("___", StringComparison.Ordinal))
		{
			string field = name.Substring(3);
			for (TypeDefinition t = targetType; t != null; t = SafeResolve(t.BaseType))
			{
				if (t.Fields.Any(f => f.Name == field))
				{
					return null;
				}
			}
			return "no field '" + field + "' on the target type";
		}
		if (name.StartsWith("__", StringComparison.Ordinal) && int.TryParse(name.Substring(2), out int index))
		{
			return index < method.Parameters.Count ? null : "index " + index + " out of range";
		}
		ParameterDefinition match = method.Parameters.FirstOrDefault(tp => tp.Name == name);
		if (match == null)
		{
			return "no parameter with this name on the target (" + string.Join(", ", method.Parameters.Select(x => x.Name)) + ")";
		}
		if (Strip(match.ParameterType) != Strip(p.ParameterType)
			&& p.ParameterType.FullName != "System.Object" && Strip(p.ParameterType) != "System.Object")
		{
			return "type " + Strip(p.ParameterType) + " does not match target " + Strip(match.ParameterType);
		}
		return null;
	}

	private static TypeDefinition SafeResolve(TypeReference t)
	{
		try
		{
			return t?.Resolve();
		}
		catch
		{
			return null;
		}
	}

	private static string Strip(TypeReference t)
	{
		return t is ByReferenceType br ? br.ElementType.FullName : t.FullName;
	}

	private static string Signature(MethodDefinition m)
	{
		return m.Name + "(" + string.Join(", ", m.Parameters.Select(p => Strip(p.ParameterType))) + ")";
	}
}
