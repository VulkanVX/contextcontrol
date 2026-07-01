# ContextControl Native Exporter

`ccnative` is the C++ port target for the DIR and CC exporters.

The PowerShell scripts stay in place as the reference implementation and backup
until the native executable passes `tests/contextcontrol-export-parity`.

Build:

```powershell
cmake -S .\native\contextcontrol -B .\build\native-contextcontrol -DCMAKE_BUILD_TYPE=Release
cmake --build .\build\native-contextcontrol --config Release
```

Command shape:

```text
ccnative dir <ccDir-compatible arguments>
ccnative cc <cc-compatible arguments>
```

Workbench integration:

- `dotnet build ide\ContextControl.Workbench\ContextControl.Workbench.csproj`
  builds and copies `ccnative` into the Workbench output directory when CMake is
  available.
- Set `BuildContextControlNative=false` to skip the native build target.
- Set `ContextControlNativeRequired=true` to fail the Workbench build when the
  native exporter cannot be built or copied.
- DIR and CC exports prefer `ccnative` when the executable is discoverable.
- PowerShell remains the fallback if native is missing, disabled, fails, or
  does not create the expected export.
- Set `CC_WORKBENCH_DISABLE_NATIVE_EXPORTS=1` to force the PowerShell path.
- Set `CC_WORKBENCH_NATIVE_EXPORTER=<path-to-ccnative>` to force an executable
  location.

The current native gate is functional parity through
`tests/contextcontrol-export-parity`. Strict byte-for-byte export parity is still
tracked separately with `-NativeCompare hash`.
