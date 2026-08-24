using System.IO.Compression;
using System.Text.Json;

namespace Tests;

public class PackTests
{
    static readonly string[] SupportedRids =
    [
        "win-x64",
        "linux-x64",
        "linux-arm64",
        "osx-x64",
        "osx-arm64",
    ];

    [Fact]
    public void Pointer_and_rid_csproj_use_pack_split()
    {
        var repo = FindRepoRoot();
        var slnx = File.ReadAllText(Path.Combine(repo, "azx.slnx"));
        Assert.Contains("src/Azure.Cli/Azure.Cli.csproj", slnx);
        Assert.Contains("src/azx/azx.csproj", slnx);

        var azureCli = File.ReadAllText(Path.Combine(repo, "src", "Azure.Cli", "Azure.Cli.csproj"));
        Assert.Contains("<PackageId>Azure.Cli</PackageId>", azureCli);
        Assert.Contains("<RuntimeIdentifiers>win-x64;linux-x64;linux-arm64;osx-x64;osx-arm64</RuntimeIdentifiers>", azureCli);
        Assert.DoesNotContain("NuGetizer", azureCli, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<PackAsTool", azureCli);
        Assert.Contains("Azure.Cli.pack.targets", azureCli);
        Assert.Contains("buildTransitive\\Azure.Cli.targets", azureCli.Replace('/', '\\'));
        Assert.Contains("Readme", azureCli);
        Assert.DoesNotContain("win-arm64", azureCli);

        var packTargets = File.ReadAllText(Path.Combine(repo, "src", "Azure.Cli", "Azure.Cli.pack.targets"));
        Assert.Contains("WriteAzureCliRuntimeJson", packTargets);
        Assert.Contains("PackAzureCliPayload", packTargets);
        Assert.Contains("$(AzureCliPackageId).$(RuntimeIdentifier)", packTargets);
        Assert.DoesNotContain("runtimes/$(RuntimeIdentifier)/native/", packTargets);

        var consumer = File.ReadAllText(Path.Combine(repo, "src", "Azure.Cli", "buildTransitive", "Azure.Cli.targets"));
        Assert.Contains("IncludeAzureCliPayload", consumer);
        Assert.Contains(@"TargetPath>az\", consumer.Replace('/', '\\'));
        Assert.DoesNotContain("runtimes/$(RuntimeIdentifier)/native/", consumer);

        var azx = File.ReadAllText(Path.Combine(repo, "src", "azx", "azx.csproj"));
        Assert.Contains("<PackageId>azx</PackageId>", azx);
        Assert.Contains("<PackAsTool>true</PackAsTool>", azx);
        Assert.Contains("<PublishAot>true</PublishAot>", azx);
        Assert.Contains("<ToolCommandName>azx</ToolCommandName>", azx);
        Assert.Contains("<ToolPackageRuntimeIdentifiers>win-x64;linux-x64;linux-arm64;osx-x64;osx-arm64</ToolPackageRuntimeIdentifiers>", azx);
        Assert.Contains("""<PackageReference Include="Azure.Cli" Version="$(Version)" />""", azx);
        Assert.DoesNotContain("NuGetizer", azx, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Readme", azx);
        var nuget = File.ReadAllText(Path.Combine(repo, "src", "azx", "nuget.config"));
        Assert.Contains("key=\"local\"", nuget);
        Assert.Contains("../../bin", nuget);
        Assert.Contains("Azure.Cli", nuget);
        Assert.DoesNotContain("win-arm64", azx);
        Assert.DoesNotContain("azx.$(RuntimeIdentifier)", azx);

        Assert.False(File.Exists(Path.Combine(repo, "src", "Azure.Cli", "runtime.json")));
        foreach (var rid in SupportedRids)
        {
            Assert.Contains(rid, azureCli);
            Assert.Contains(rid, azx);
        }
    }

    [Fact]
    public void WriteAzureCliRuntimeJson_maps_five_rids()
    {
        var repo = FindRepoRoot();
        var project = Path.Combine(repo, "src", "Azure.Cli", "Azure.Cli.csproj");
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("msbuild");
        start.ArgumentList.Add(project);
        start.ArgumentList.Add("-restore");
        start.ArgumentList.Add("-t:WriteAzureCliRuntimeJson");
        start.ArgumentList.Add("-p:Configuration=" + configuration);
        start.ArgumentList.Add("-p:DesignTimeBuild=true");
        start.ArgumentList.Add("-p:GeneratePackageOnBuild=false");
        start.ArgumentList.Add("-nologo");

        using var process = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start dotnet msbuild.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000));
        Assert.True(process.ExitCode == 0, stdout + Environment.NewLine + stderr);

        var runtimeJson = Path.Combine(repo, "src", "Azure.Cli", "obj", configuration, "net10.0", "runtime.json");
        Assert.True(File.Exists(runtimeJson), runtimeJson);
        using var doc = JsonDocument.Parse(File.ReadAllText(runtimeJson));
        var runtimes = doc.RootElement.GetProperty("runtimes");
        Assert.Equal(SupportedRids.Length, runtimes.EnumerateObject().Count());
        foreach (var rid in SupportedRids)
        {
            var range = runtimes
                .GetProperty(rid)
                .GetProperty("Azure.Cli")
                .GetProperty("Azure.Cli." + rid)
                .GetString();
            Assert.False(string.IsNullOrWhiteSpace(range));
            Assert.StartsWith("[", range);
            Assert.EndsWith(", )", range);
        }

        Assert.False(runtimes.TryGetProperty("win-arm64", out _));
    }

    [Fact]
    public void Packed_nupkgs_have_pointer_and_rid_layout()
    {
        var bin = Path.Combine(FindRepoRoot(), "bin");
        if (!Directory.Exists(bin))
            return;

        var nupkgs = Directory.GetFiles(bin, "Azure.Cli*.nupkg")
            .Where(f => !f.Contains(".symbols.", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (nupkgs.Length == 0)
            return;

        var pointer = nupkgs.FirstOrDefault(f =>
            !SupportedRids.Any(r => Path.GetFileName(f).Contains("." + r + ".", StringComparison.Ordinal)));
        if (pointer is not null)
        {
            var names = ZipNames(pointer);
            Assert.Contains(names, n => n == "runtime.json" || n == "runtime.json/");
            Assert.Contains(names, n => n.Replace('\\', '/').StartsWith("lib/", StringComparison.Ordinal));
            Assert.Contains(names, n => n.Replace('\\', '/').Contains("buildTransitive/Azure.Cli.targets", StringComparison.Ordinal));
            Assert.DoesNotContain(names, n => n.Replace('\\', '/').StartsWith("az/", StringComparison.Ordinal));
        }

        var ridPkg = nupkgs.FirstOrDefault(f =>
            SupportedRids.Any(r => Path.GetFileName(f).Contains("." + r + ".", StringComparison.Ordinal)));
        if (ridPkg is not null)
        {
            var names = ZipNames(ridPkg);
            Assert.Contains(names, n => n.Replace('\\', '/').StartsWith("az/bin/", StringComparison.Ordinal));
            Assert.DoesNotContain(names, n => n.Replace('\\', '/').StartsWith("lib/", StringComparison.Ordinal));
            Assert.DoesNotContain(names, n => n.Replace('\\', '/').Contains("buildTransitive/", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Azure_cli_version_pin_is_semver()
    {
        var pin = File.ReadAllText(Path.Combine(FindRepoRoot(), "azure-cli.version")).Trim();
        Assert.Matches(@"^\d+\.\d+\.\d+$", pin);
    }

    static HashSet<string> ZipNames(string nupkg)
    {
        using var zip = ZipFile.OpenRead(nupkg);
        return zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
