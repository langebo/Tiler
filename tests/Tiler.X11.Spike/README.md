# Tiler.X11.Spike

Throwaway spike for issue #5: does the whole X11 backend capability surface work through raw
P/Invoke inside a net11 Native AOT binary?

Deliberately **not** in `Tiler.sln`: TL-203 turns this into the shipping `X11Backend`. It binds
`libX11.so.6` and `libXrandr.so.2` at runtime with `NativeLibrary` (dlopen) and unmanaged
function pointers, so there is no DllImport stub for AOT to marshal or JIT.

Needs a display and the two X libraries on the machine that runs it:

```sh
dotnet publish tests/Tiler.X11.Spike -c Release -r linux-x64 -o out-x11-spike   # needs clang
xvfb-run -a ./out-x11-spike/Tiler.X11.Spike                                     # RANDR path
```

Prints one `OPERATION ok ...` line per operation (`AOT-GUARD`, `FOCUS`, `GEOMETRY-CSD`,
`GEOMETRY-FRAMED`, `APPLY`, `MONITORS`, `NOT-TILEABLE`) and exits non-zero on the first
failure. The AOT guard rejects a JIT run, so it must be the published native binary.

Fallback path (RANDR disabled):

```sh
Xvfb :99 -screen 0 1920x1080x24 -extension RANDR -nolisten tcp &
DISPLAY=:99 ./out-x11-spike/Tiler.X11.Spike    # MONITORS ok ... source=fallback
```

The `x11-spike (linux-x64)` CI job publishes and runs it under Xvfb (see
`.github/workflows/ci.yml`).
