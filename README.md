# VMapEdit

Small Source 2 VMAP editor built on ValveResourceFormat and Datamodel.NET.

## What it can do

- Open editable `.vmap` files
- Inspect the world tree and entity metadata
- Scale entity origins relative to world origin
- Update entity keyvalues in `entity_properties`
- Write the modified VMAP back as binary DMX

## Commands

```bash
dotnet run --project VMapEdit.Cli -- inspect path/to/map.vmap
dotnet run --project VMapEdit.Cli -- scale path/to/input.vmap path/to/output.vmap --factor 2
dotnet run --project VMapEdit.Cli -- set path/to/input.vmap path/to/output.vmap --class prop_dynamic --field targetname --value crate_02
```

## Notes

- Editable Source 2 VMAPs are binary DMX files.
- The implementation uses the Datamodel.NET package pulled in by ValveResourceFormat.
- The editor preserves the existing VMAP structure instead of converting through Blender or mesh exporters.