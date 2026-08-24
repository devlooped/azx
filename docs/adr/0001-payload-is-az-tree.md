# Payload is `az/`, not `runtimes/{rid}/native/`

The .NET SDK flattens anything under `runtimes/{rid}/native/` when copying to output. Azure CLI is a directory tree (`bin/az` plus a Python install), so a RID package ships that tree as `az/` and `buildTransitive` copies it with paths intact.
