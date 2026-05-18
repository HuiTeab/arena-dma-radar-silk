# Arena DMA Radar — Silk.NET Edition

A standalone DMA (Direct Memory Access) radar overlay for **Escape from Tarkov: Arena**, built on [Silk.NET](https://github.com/dotnet/Silk.NET) (Windowing / Input / OpenGL), [ImGui.NET](https://github.com/ImGuiNET/ImGui.NET) panels, and [SkiaSharp](https://github.com/mono/SkiaSharp) 2D rendering.

> **Targeting the main EFT build instead of Arena?** See the sibling repos [**eft-dma-radar-silk**](https://github.com/HuiTeab/eft-dma-radar-silk) (Unity 2022.3.43f1) and [**eft-dma-radar-silk6**](https://github.com/HuiTeab/eft-dma-radar-silk6) (Unity 6000.3.6f1). The arena radar shares the Silk.NET + ImGui + SkiaSharp stack with the EFT builds but has its own match world model, IL2CPP dumper, and Arena-only map set.

The `src-arena/` codebase is an **original work written by [HuiTeab](https://github.com/HuiTeab)** and licensed under the **PolyForm Noncommercial License 1.0.0**. The only third-party code in this repository is `lib/VmmSharpEx/` — a separately-licensed (AGPL-3.0) wrapper around [MemProcFS](https://github.com/ufrisk/MemProcFS), included unmodified-in-attribution as part of the radar's DMA stack. See [LICENSE](LICENSE) for the full license breakdown.

---

## Repo Layout

```
arena-dma-radar-silk/
├── eft-dma-radar-arena.sln     # Visual Studio solution (VmmSharpEx + src-arena)
├── Directory.Build.props        # Common MSBuild props (net10.0-windows, x64, unsafe)
├── version.json                 # Nerdbank.GitVersioning version source
├── LICENSE                      # PolyForm Noncommercial License 1.0.0 (+ AGPL-3.0 for lib/VmmSharpEx)
├── Resources/                   # Embedded font (NeoSansStdRegular.otf)
├── lib/
│   └── VmmSharpEx/              # Managed MemProcFS / LeechCore wrapper + native DLLs
└── src-arena/                   # The radar itself (entry: Program.cs → ArenaProgram.Main)
    ├── Maps/                    # Arena-only map SVGs + JSON metadata
    └── Docs/
        └── DEBUG_OUTPUT_REFERENCE.md
```

---

## Requirements

- **DMA hardware** supported by [MemProcFS](https://github.com/ufrisk/MemProcFS) (FPGA card, `usb3380`, etc.)
- **Windows 10 / 11 (x64)** — project targets `net10.0-windows`, `PlatformTarget=x64`
- **[.NET 10 SDK / Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)**
- **Visual Studio 2022 17.12+** (or 2026 Insiders) with the **.NET desktop development** workload
- The native MemProcFS binaries (`vmm.dll`, `leechcore.dll`, `FTD3XX.dll`, …) ship under `lib/VmmSharpEx/native/` and are copied to the build output automatically.

---

## Build & Run

```powershell
git clone https://github.com/HuiTeab/arena-dma-radar-silk.git
cd arena-dma-radar-silk

# Build (Release, x64)
dotnet build eft-dma-radar-arena.sln -c Release

# Run
dotnet run --project src-arena\arena-dma-radar.csproj -c Release
```

In Visual Studio: open `eft-dma-radar-arena.sln`, set `arena-dma-radar` as the startup project, then press **F5**.

Pass `-debug` on the command line (or set `debugLogging=true` in the config) to enable verbose logging at startup.

---

## Project

### `src-arena` — Arena DMA Radar

- **AssemblyName:** `arena-dma-radar` · **RootNamespace:** `eft_dma_radar.Arena`
- **Entry point:** [`ArenaProgram.Main`](src-arena/Program.cs)
- **Packages:** `ImGui.NET 1.91.6.1`, `Silk.NET.Windowing/Input/OpenGL/OpenGL.Extensions.ImGui 2.23.0`, `SkiaSharp 3.119.2`, `Svg.Skia 3.0.3`.
- **Config:** [`ArenaConfig`](src-arena/Config/ArenaConfig.cs) at `%AppData%\eft-dma-radar-arena\config.json` (JSON persistence). IL2CPP offsets and camera offsets are resolved at startup and cached to `il2cpp_offsets.json` / `camera_offsets.json` in the same directory; the hard-coded values in [`src-arena/SDK/Offsets.cs`](src-arena/SDK/Offsets.cs) are used as a fallback.
- **Maps:** Arena-only set under [`src-arena/Maps`](src-arena/Maps) — `Arena_Airpit`, `Arena_Bay5`, `Arena_Block`, `Arena_Bowl`, `Arena_ChopShop`, `Arena_Equator`, `Arena_Fort`, `Arena_Iceberg`, `Arena_Sawmill`, `Arena_Skybridge`, plus `default`.
- **In-tree docs:** [`src-arena/Docs/DEBUG_OUTPUT_REFERENCE.md`](src-arena/Docs/DEBUG_OUTPUT_REFERENCE.md).

> **Note:** Unlike the EFT silk / silk6 radars, arena does **not** ship an embedded web radar server. There is no ASP.NET Core dependency, no `Web/` folder, and no browser client.

### `lib/VmmSharpEx` — Managed MemProcFS wrapper

A managed C# wrapper around MemProcFS (`vmm.dll`) and LeechCore (`leechcore.dll`). Provides the high-level [`Vmm`](lib/VmmSharpEx/Vmm.cs) handle (read/write, VFS, process enumeration), a [`LeechCore`](lib/VmmSharpEx/LeechCore.cs) device wrapper, a scatter API for batched gathers/writes, a memory search engine, a refresh manager, strongly-typed flag/option enums, a Win32 virtual-key DMA input manager, and a `VmmPointer` abstraction with rich `VmmException` hierarchy.

- **TFM:** `net10.0-windows`, `Nullable=enable`, doc-file generated.
- **Packages:** `Collections.Pooled.V2 2.2.2`.
- **Native bin:** `lib/VmmSharpEx/native/` (`vmm.dll`, `leechcore.dll`, `leechcore_driver.dll`, `FTD3XX.dll`, `dbghelp.dll`, `symsrv.dll`, `tinylz4.dll`, `vcruntime140.dll`) — copied to consumer output via `<None Include="native\**\*.dll" CopyToOutputDirectory="PreserveNewest" />`.
- **License:** AGPL-3.0 — original MemProcFS API © Ulf Frisk; `VmmSharpEx` modifications © Lone (Lone DMA), 2025.

---

## Resources

- **`Resources/NeoSansStdRegular.otf`** — embedded into the assembly as `eft_dma_radar.Arena.NeoSansStdRegular.otf` (see the `<LogicalName>` in [arena-dma-radar.csproj](src-arena/arena-dma-radar.csproj)) and consumed by [`CustomFonts`](src-arena/UI/CustomFonts.cs).

---

## Developer Notes

- Common build settings live in [`Directory.Build.props`](Directory.Build.props): `TargetFramework=net10.0-windows7.0`, `LangVersion=latest`, `ImplicitUsings=enable`, `AllowUnsafeBlocks=true`, `Nullable=enable`, `PlatformTarget=x64`.
- Versioning is driven by [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning) via [`version.json`](version.json) (base: `2.0`, public-release branches: `main`, `beta`).
- Arena opts into Server GC + Concurrent GC and raises process priority to `High`; an `ExceptionTracer` is installed and a high-resolution timer is enabled at startup.
- `lib/VmmSharpEx` disables trimming (`IsTrimmable=false`) due to heavy P/Invoke; AOT and single-file analyzers are kept on to flag interop issues.
