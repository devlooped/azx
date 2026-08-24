using System.Diagnostics;

namespace Tests;

public class AzxTests
{
    [Fact]
    public void Lone_version_prints_azx_and_az()
    {
        var azx = FindAzx();
        if (azx is null)
            return;

        var (exit, stdout, stderr) = Run(azx, "--version");
        Assert.True(exit == 0, stderr);
        Assert.Contains("azx ", stdout, StringComparison.Ordinal);
        Assert.Contains("azure-cli", stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Other_args_are_passthrough()
    {
        var azx = FindAzx();
        if (azx is null)
            return;

        var (exit, stdout, stderr) = Run(azx, "version", "-o", "tsv", "--query", "\"azure-cli\"");
        Assert.True(exit == 0, stderr);
        Assert.DoesNotContain("azx ", stdout, StringComparison.Ordinal);
        var pin = File.ReadAllText(Path.Combine(FindRepoRoot(), "azure-cli.version")).Trim();
        Assert.Equal(pin, stdout.Trim());
    }

    static (int Exit, string Stdout, string Stderr) Run(string azx, params string[] args)
    {
        var start = new ProcessStartInfo(azx)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(azx),
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start azx.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000));
        return (process.ExitCode, stdout, stderr);
    }

    static string? FindAzx()
    {
        var dir = AppContext.BaseDirectory;
        var name = OperatingSystem.IsWindows() ? "azx.exe" : "azx";
        var path = Path.Combine(dir, name);
        return File.Exists(path) ? path : null;
    }

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "azx.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find azx.slnx from " + AppContext.BaseDirectory);
    }
}
