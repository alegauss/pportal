# app — the .NET host (PP1)

```
cd app
dotnet build                     debug, framework-dependent, ~1s incremental
dotnet publish -c Release        one self-contained ChiakiNg.exe, ~62 MB
dotnet run                       open the window
```

Output lives at `bin\<config>\net10.0-windows\win-x64\`, and the publish at
`…\win-x64\publish\ChiakiNg.exe`.

## What this is, and what it is not

It is the project, the manifest, the icon, the version, and a window that opens empty. It is
**not** a screen. Every screen in Block D is filed against a host that already builds, because
the alternative is a first screen carrying the build system on its back, which cannot be reviewed
as a screen at all — a reviewer cannot tell which half is wrong.

The Qt client stays until Block D empties. Two executables in one tree is the ordinary shape of a
port, and the one that is not shipped yet is the one being written. `compile.cmd` builds the Qt
one and does not know about this directory; wiring the two builds together is PP24's decision,
not this one's.

## The settings that are not arbitrary

`ChiakiNg.csproj` carries the reasoning inline. The three worth knowing before editing it:

- **`PublishSingleFile` and friends are Release-only.** Unconditional, they apply to
  `dotnet build` too and lay the whole runtime down every time — claude-tray measured 252 files
  and 155.6 MB, turning a no-op build from 1.2s into 81s and then 395s on unchanged source.
- **`RuntimeIdentifier` is unconditional**, so the output path is the same in Debug and Release
  and anything that spells `…\win-x64\ChiakiNg.exe` stays true.
- **The version is in one place.** `<Version>` in the csproj, tracking the Qt client's
  `CHIAKI_VERSION` from `CMakeLists.txt`. `app.manifest` needs its own four-part copy, and
  nothing in the SDK relates the two, so the `VerifyManifestVersion` target **fails the build**
  when they drift. That is PP1's assertion: set the manifest to `1.9.0.0` and the build stops
  with both numbers named.

## Why WPF and not WinForms

`UseWindowsForms` is deliberately off. claude-tray — the reference this project's settings come
from — needs it for a tray `NotifyIcon`; this has no tray. Leaving it off also keeps the SDK's
implicit global usings intact, which enabling both quietly narrows.

`ThemeMode="System"` in `App.xaml` is the SDK's built-in Fluent switch. It is why `UseWPF` alone
is enough for a Windows 11 look: no extra package, so the single self-contained `.exe` stays
single.
