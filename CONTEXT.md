# Azure.Cli

NuGet pointer and per-RID payloads that put a working Azure CLI next to a .NET app or tool. Humans run that payload through **azx**.

## Language

**Pointer**:
The `Azure.Cli` nupkg: `Az` plus `runtime.json` mapping each supported RID to a RID package.
_Avoid_: metapackage, tool package, native package

**RID package**:
`Azure.Cli.{rid}` — one nupkg, one RID, one Payload.
_Avoid_: native package, runtime pack, sidecar package

**Payload**:
The self-contained tree copied to the consuming app as `az/`, from which `az` is executed.
_Avoid_: native files, native/, binaries, sidecar, nopython tarball (that is an upstream artifact, not what we ship)

**Az**:
The managed type in `Azure.Cli` whose `ResolvePath` returns the Payload's `az` executable (`az.cmd` on Windows).
_Avoid_: Azx, AzureCli, WhatsBoxHost, ResolveBinaryPath

**azx**:
The passthrough .NET tool that execs the Payload `az` with the same arguments. Primary human vehicle via `dnx`/`ndnx azx`.
_Avoid_: Azure CLI, az, wrapper with its own Azure verbs

**Execute**:
azx replacing itself with the Payload `az`. Every argument is forwarded. The only exception is a lone `--version`, which prints azx and `az`.
_Avoid_: wrap, shell out and wait as the product metaphor (implementation may still spawn)

**Upstream**:
The Azure CLI GitHub release we track. Tag `azure-cli-{version}`; that `{version}` is our nupkg version.
_Avoid_: submodule, clone, PyPI as the version source

**Preview**:
A separate GitHub release tagged `{upstream}-preview` when `vars.RELEASE` is not `STABLE`. Nupkgs use that tag. A later STABLE release is `{upstream}`, not an edit of the preview.
_Avoid_: flipping the prerelease bit on a published release, `{version}-preview-preview`
