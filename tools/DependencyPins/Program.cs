using DependencyPins;

if (args.Length is < 1 or > 2 || args[0] is not ("check" or "update"))
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/DependencyPins -- <check|update> [repository-root]");
    return 2;
}

try
{
    var root = Path.GetFullPath(args.Length == 2 ? args[1] : Directory.GetCurrentDirectory());
    var plan = PinUpdater.Plan(root);
    foreach (var change in plan)
        Console.WriteLine($"{(args[0] == "check" ? "Out of date" : "Updating")}: {Path.GetRelativePath(root, change.Path)}");
    if (args[0] == "check")
    {
        Console.WriteLine(plan.Count == 0 ? "Dependency pins are current." : "Run update and review the generated diff.");
        return plan.Count == 0 ? 0 : 1;
    }
    PinUpdater.Apply(plan);
    Console.WriteLine($"Updated {plan.Count} file(s). Review and commit dependencies.json with the generated changes.");
    return 0;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
                            or System.Text.Json.JsonException or ArgumentException or System.Text.RegularExpressions.RegexMatchTimeoutException)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}
