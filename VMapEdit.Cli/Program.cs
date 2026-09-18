using System.Globalization;

using VMapEdit.Cli;

if (args.Length == 0)
{
	PrintUsage();
	return 1;
}

try
{
	return args[0].ToLowerInvariant() switch
	{
		"inspect" => RunInspect(args[1..]),
		"scale" => RunScale(args[1..]),
		"set" => RunSet(args[1..]),
		_ => throw new ArgumentException($"Unknown command '{args[0]}'.")
	};
}
catch (Exception ex)
{
	Console.Error.WriteLine(ex.Message);
	return 1;
}

static int RunInspect(string[] args)
{
	if (args.Length != 1)
	{
		throw new ArgumentException("Usage: inspect <input.vmap>");
	}

	using var editor = VmapEditor.Load(args[0]);

	Console.WriteLine($"File: {args[0]}");
	foreach (var entity in editor.EnumerateEntities())
	{
		Console.WriteLine(entity.FormatLine());
	}

	return 0;
}

static int RunScale(string[] args)
{
	if (args.Length < 2)
	{
		throw new ArgumentException("Usage: scale <input.vmap> <output.vmap> --factor <number>");
	}

	var inputPath = args[0];
	var outputPath = args[1];
	var factor = ParseScaleFactor(args[2..]);

	using var editor = VmapEditor.Load(inputPath);
	var scaledCount = editor.ScaleEntityOrigins(factor);
	var meshScaledCount = editor.ScaleMapMeshes(factor);

	using var stream = File.Create(outputPath);
	editor.Save(stream);

	Console.WriteLine($"Scaled {scaledCount} entity origins by {factor.ToString(CultureInfo.InvariantCulture)}.");
    Console.WriteLine($"Scaled {meshScaledCount} meshes by {factor.ToString(CultureInfo.InvariantCulture)}.");
    Console.WriteLine($"Wrote {outputPath}");
	return 0;
}

static int RunSet(string[] args)
{
	if (args.Length < 2)
	{
		throw new ArgumentException("Usage: set <input.vmap> <output.vmap> --field <name> --value <value> [--class <classname>] [--targetname <name>] [--nodeid <id>]");
	}

	var inputPath = args[0];
	var outputPath = args[1];
	var selector = new EntitySelector();
	string? fieldName = null;
	string? fieldValue = null;

	for (var i = 2; i < args.Length; i++)
	{
		switch (args[i].ToLowerInvariant())
		{
			case "--class":
				selector = selector with { ClassName = RequireValue(args, ref i, "--class") };
				break;
			case "--targetname":
				selector = selector with { TargetName = RequireValue(args, ref i, "--targetname") };
				break;
			case "--nodeid":
				selector = selector with { NodeId = int.Parse(RequireValue(args, ref i, "--nodeid"), CultureInfo.InvariantCulture) };
				break;
			case "--field":
				fieldName = RequireValue(args, ref i, "--field");
				break;
			case "--value":
				fieldValue = RequireValue(args, ref i, "--value");
				break;
			default:
				throw new ArgumentException($"Unknown option '{args[i]}'.");
		}
	}

	if (string.IsNullOrWhiteSpace(fieldName))
	{
		throw new ArgumentException("Missing --field.");
	}

	if (fieldValue is null)
	{
		throw new ArgumentException("Missing --value.");
	}

	using var editor = VmapEditor.Load(inputPath);
	var matchCount = editor.UpdateEntityField(selector, fieldName, fieldValue);

	using var stream = File.Create(outputPath);
	editor.Save(stream);

	Console.WriteLine($"Updated {matchCount} entity.");
	Console.WriteLine($"Wrote {outputPath}");
	return 0;
}

static float ParseScaleFactor(string[] args)
{
	for (var i = 0; i < args.Length; i++)
	{
		if (args[i].Equals("--factor", StringComparison.OrdinalIgnoreCase))
		{
			return float.Parse(RequireValue(args, ref i, "--factor"), CultureInfo.InvariantCulture);
		}
	}

	throw new ArgumentException("Missing --factor.");
}

static string RequireValue(string[] args, ref int index, string optionName)
{
	if (index + 1 >= args.Length)
	{
		throw new ArgumentException($"Missing value for {optionName}.");
	}

	index++;
	return args[index];
}

static void PrintUsage()
{
	Console.WriteLine("VMapEdit");
	Console.WriteLine();
	Console.WriteLine("Commands:");
	Console.WriteLine("  inspect <input.vmap>");
	Console.WriteLine("  scale <input.vmap> <output.vmap> --factor <number>");
	Console.WriteLine("  set <input.vmap> <output.vmap> --field <name> --value <value> [--class <classname>] [--targetname <name>] [--nodeid <id>]");
}
