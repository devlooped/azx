using Azure;

namespace Tests;

public class CliTests
{
    [Fact]
    public void ResolvePath_finds_payload_under_az_bin()
    {
        var root = Path.Combine(Path.GetTempPath(), "azx-resolve-" + Guid.NewGuid().ToString("n"));
        var bin = Path.Combine(root, "az", "bin");
        Directory.CreateDirectory(bin);
        var name = OperatingSystem.IsWindows() ? "az.cmd" : "az";
        var expected = Path.Combine(bin, name);
        File.WriteAllText(expected, "fake");

        try
        {
            Assert.Equal(Path.GetFullPath(expected), Cli.ResolvePath(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolvePath_throws_when_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "azx-missing-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            var ex = Assert.Throws<FileNotFoundException>(() => Cli.ResolvePath(root));
            Assert.Contains("az", ex.FileName, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolvePath_finds_project_payload_and_az_version_matches_pin()
    {
        string path;
        try
        {
            path = Cli.ResolvePath();
        }
        catch (FileNotFoundException)
        {
            return;
        }

        Assert.True(File.Exists(path), path);
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(
                File.GetUnixFileMode(path).HasFlag(UnixFileMode.UserExecute),
                path);
        }
        var pin = File.ReadAllText(Path.Combine(FindRepoRoot(), "azure-cli.version")).Trim();
        var start = new System.Diagnostics.ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(path),
        };
        if (OperatingSystem.IsWindows())
        {
            start.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            start.ArgumentList.Add("/d");
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add(path);
        }
        else
        {
            start.FileName = path;
        }
        start.ArgumentList.Add("version");
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add("tsv");
        start.ArgumentList.Add("--query");
        start.ArgumentList.Add("\"azure-cli\"");

        using var process = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start az.");
        var output = process.StandardOutput.ReadToEnd().Trim();
        Assert.True(process.WaitForExit(60_000));
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        Assert.Equal(pin, output);
    }

    [Fact]
    public void ResolvePath_sets_unix_execute_bits()
    {
        if (OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(Path.GetTempPath(), "azx-chmod-" + Guid.NewGuid().ToString("n"));
        var az = Path.Combine(root, "az", "bin", "az");
        var py = Path.Combine(root, "az", "python", "bin", "python3");
        Directory.CreateDirectory(Path.GetDirectoryName(az)!);
        Directory.CreateDirectory(Path.GetDirectoryName(py)!);
        File.WriteAllText(az, "#!/bin/sh\n");
        File.WriteAllText(py, "x");
        var readWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite |
                        UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        File.SetUnixFileMode(az, readWrite);
        File.SetUnixFileMode(py, readWrite);

        try
        {
            Assert.Equal(Path.GetFullPath(az), Cli.ResolvePath(root));
            Assert.True(File.GetUnixFileMode(az).HasFlag(UnixFileMode.UserExecute), az);
            Assert.True(File.GetUnixFileMode(py).HasFlag(UnixFileMode.UserExecute), py);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
