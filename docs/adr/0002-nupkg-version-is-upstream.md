# Nupkg version is Upstream

`azx.cli` and `azx` use the Azure CLI version (`2.89.1`) so `ndx azx@2.89.1` is that CLI. The pin is `azure-cli.version`. CI dogfood stays `42.42.*`. Packaging-only republishes of the same CLI use a SemVer label, not a different major.
