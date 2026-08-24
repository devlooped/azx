# Preview is the GitHub prerelease bit

The GitHub tag is always the naked CLI version (`2.89.1`). `vars.RELEASE=PRERELEASE` (default) creates a draft `--prerelease`; the org webhook publishes it; nupkgs are `2.89.1-preview`. Flipping to `STABLE` edits that release (`--prerelease=false`) and publishes `2.89.1`. Same tag, two nupkg versions; previews stay on nuget.org.
