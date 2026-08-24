using System.Diagnostics;
using System.Reflection;
using Azure.Cli;

static class Program
{
    static int Main(string[] args)
    {
        var az = Az.ResolvePath();
        if (args is ["--version"])
        {
            var version = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "0.0.0";
            Console.WriteLine("azx " + version);
        }

        var start = new ProcessStartInfo
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory,
        };

        if (OperatingSystem.IsWindows())
        {
            start.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            start.ArgumentList.Add("/d");
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add(az);
        }
        else
        {
            start.FileName = az;
        }

        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Failed to start '{az}'.");
        process.WaitForExit();
        return process.ExitCode;
    }
}
