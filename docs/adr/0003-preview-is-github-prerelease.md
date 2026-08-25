# Preview is its own GitHub release

The pin is the naked CLI version (`2.89.1`). When `vars.RELEASE` is not `STABLE` (default `PRERELEASE`), the draft is tagged and titled `2.89.1-preview` (`--prerelease`). The org webhook publishes it; nupkgs use that tag. Flipping to `STABLE` creates a new draft `2.89.1` and does not edit the preview. `publish.yml` appends `-preview` only for a naked prerelease tag, so a `{version}-preview` tag is not doubled. Previews stay on nuget.org.
