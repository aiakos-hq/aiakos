using System.Reflection;

var version = typeof(Program).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

if (args is ["--version"] or ["-v"])
{
    Console.WriteLine(version);
    return 0;
}

Console.WriteLine($"Hi from Aiakos {version}.");
Console.WriteLine("This is an early preview placeholder; the real CLI is not available yet.");
Console.WriteLine("https://github.com/aiakos-hq/aiakos");
return 0;
