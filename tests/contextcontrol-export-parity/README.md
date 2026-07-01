# ContextControl Export Parity Tests

This folder contains the baseline/parity harness for the native DIR/CC rewrite.

The current PowerShell scripts remain the reference implementation. Run the
parity script before and after native changes to keep a timestamped record under
`.tmp/contextcontrol-export-parity/`.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\contextcontrol-export-parity\Run-ExportParity.ps1
```

When a native executable exists, compare it against the PowerShell baseline:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\contextcontrol-export-parity\Run-ExportParity.ps1 -NativeExe .\build\native-contextcontrol\Release\ccnative.exe -RequireNative
```

During native porting, use functional comparison to require every case to pass
the same behavior assertions before byte-identical formatting is complete:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\contextcontrol-export-parity\Run-ExportParity.ps1 -NativeExe .\build\native-contextcontrol\Release\ccnative.exe -RequireNative -NativeCompare functional
```

Expected native command shape:

```text
ccnative.exe dir <same ccDir.ps1 arguments>
ccnative.exe cc <same cc.ps1 arguments>
```

The runner checks the exporter surface that must stay 1:1:

- DIR profile, L0, and L1 scoped manifests.
- CC full file and folder exports.
- C/C++ matching-header auto dependencies.
- Shader direct `#include` auto dependencies.
- Scoped `FUNCTION path :: symbol` requests.
- Wildcard scoped function requests.
- Global `FUNC:` and `FUNCTION:` requests.
- Single and multi-pattern `FIND:` discovery.
- `-HashHints`.
- large-file skip and `-ForceLargeFiles`.
- custom project file rules.
- missing path reporting.
- disabled `SYMBOL:` failure.
