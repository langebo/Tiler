# Tiler.AotGui.Spike

Throwaway spike for issue #4: can a GTK4 window live inside a net11 Native AOT binary?

Deliberately **not** in `Tiler.sln`: nothing in the shipping build or the tests may depend on
GTK. It binds GTK4 through the community `GirCore.Gtk-4.0` package.

Needs an X display and the GTK4 runtime on the machine that runs it:

```sh
dotnet publish tests/Tiler.AotGui.Spike -c Release -r linux-x64 -o out-spike   # needs clang + zlib1g-dev
xvfb-run -a ./out-spike/Tiler.AotGui.Spike             # one window, prints first-frame-ms, exit 0
xvfb-run -a ./out-spike/Tiler.AotGui.Spike --cycles 50 # 50 open/close cycles
./out-spike/Tiler.AotGui.Spike --hold                 # keep window open; Close button or WM close ends it
```

The `gui-spike (*)` CI job publishes and measures it per RID (see `.github/workflows/ci.yml`).
