namespace Tests;

public class ReleaseTests
{
    [Fact]
    public void Non_stable_release_is_named_version_preview_not_an_edit()
    {
        var release = File.ReadAllText(Path.Combine(FindRepoRoot(), ".github", "workflows", "release.yml"));
        Assert.Contains("vars.RELEASE", release);
        Assert.Contains("CHANNEL:-PRERELEASE", release);
        Assert.Contains("TAG=\"$VERSION-preview\"", release);
        Assert.Contains("TAG=\"$VERSION\"", release);
        Assert.Contains("gh release create \"$TAG\" --draft --prerelease --title \"$TAG\"", release);
        Assert.Contains("gh release create \"$TAG\" --draft --title \"$TAG\"", release);
        Assert.DoesNotContain("gh release edit", release);
        Assert.DoesNotContain("--prerelease=false", release);
    }

    [Fact]
    public void Publish_version_does_not_double_append_preview()
    {
        var publish = File.ReadAllText(Path.Combine(FindRepoRoot(), ".github", "workflows", "publish.yml"));
        Assert.Contains("endsWith(github.event.release.tag_name, '-preview')", publish);
        Assert.Contains("format('{0}-preview', github.event.release.tag_name)", publish);
        Assert.DoesNotContain(
            "Version: ${{ github.event.release.prerelease && format('{0}-preview', github.event.release.tag_name) || github.event.release.tag_name }}",
            publish);
        Assert.DoesNotContain("rids: linux-x64 win-x64 osx-x64 osx-arm64", publish);
        Assert.Contains("os: windows-latest", publish);
        Assert.Contains("os: macos-latest", publish);
        Assert.Contains("os: macos-15-intel", publish);
        Assert.Contains("rid: win-x64", publish);
        Assert.Contains("rid: osx-x64", publish);
        Assert.Contains("rid: osx-arm64", publish);
        Assert.Contains("Expand-Archive", publish);
        Assert.DoesNotContain("tar -xf $nupkg", publish);
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
