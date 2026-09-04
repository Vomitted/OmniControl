# OmniHub

A replacement for Omen Gaming Hub: fan curve control, GPU power/mode, and CPU
power limits, talking directly to the same `hpqBIntM` BIOS WMI interface OGH,
OmenMon, and OmenCore all use, plus AMD SMU tuning through the PawnIO driver.

It runs on any Windows laptop. What it can *drive* depends on the machine, and
it says which on the dashboard rather than presenting dead controls — see
[Compatibility](#compatibility).

## Why this exists

Default/Balanced BIOS fan mode on some HP Omen/Victus laptops has a real bug:
the fan table has a 0% "idle" entry that can get hit while the laptop is still
genuinely hot, so the fan stops while the machine overheats. OmniHub's Auto
(Curve) mode fixes this with a safety floor: once you're past a configurable
temperature, the fan is never allowed to command 0% again. See
`OmniHub.Core/Fan/FanCurve.cs`.

The rule the whole project is built on: **it does not synthesize telemetry.**
No invented fan RPM, no "AI" workload classification, no plausible-looking
number standing in for a reading the hardware could not give. If a value is
unavailable, the UI says so. That extends to tuning — every apply reads the
hardware back, and the Tuning tab distinguishes a limit the firmware
*accepted* from one it actually *enforces*, because on some platforms the SMU
returns success for limits the vendor firmware then arbitrates away.

## Compatibility

OmniHub starts and runs on any Windows laptop. Missing vendor support disables
the controls that need it — it is never a crash, and the dashboard names what
is unavailable and why.

| Feature | Works on |
| --- | --- |
| CPU/GPU temperature, load, clocks, memory, battery health | Any laptop |
| Windows power plans, scheduling, MMCSS, timer resolution, process priority | Any laptop |
| Discrete GPU telemetry | Any NVIDIA GPU (via `nvidia-smi`) |
| CPU tuning: power limits, thermal limit, curve optimizer | AMD Ryzen, with the PawnIO driver |
| Fan curve control, GPU TGP unlock, BIOS power limits | HP laptops exposing `hpqBIntM` |

Other vendors (Lenovo, Dell, ASUS) each expose a completely different ACPI/WMI
interface and there is no cross-vendor standard. The vendor layer is isolated
behind one availability check, so a second backend can slot in beside the HP
one — but none is shipped unverified, because untested code that writes to
unknown ACPI methods on someone's laptop should not exist.

## Requirements

- Windows 10/11. Full functionality needs an HP laptop exposing the
  `hpqBIntM` BIOS WMI interface (Omen and Victus models); everything else
  degrades gracefully.
- .NET 8 SDK to build.
- Administrator privileges to run -- the BIOS calls require an elevated WMI
  session. `app.manifest` requests this automatically (UAC prompt on launch).

## Building and running

```
dotnet build OmniHub.slnx
dotnet run --project OmniHub.App
```

Or open `OmniHub.slnx` in Visual Studio 2022+ and run `OmniHub.App`.

### First run on a new laptop model

Before trusting curve control on hardware that hasn't been checked, run:

```
OmniHub.exe -Probe
```

from an elevated terminal. This dumps the raw fan count/type/level/table,
temperature, throttling state, and GPU mode/power exactly as the BIOS
reports them, with no interpretation layered on top -- the ground truth
needed to confirm the command layout matches before relying on the curve.

## Tabs

- **Dashboard** -- live temperature/fan/throttling readout, GPU mode, and
  three quick presets (Silent / Balanced / Performance) that combine a GPU
  power preset with a fan mode.
- **Fans** -- the core fix. Switch between Auto (Curve), BIOS Default, and
  Max Fan; edit the safety floor and the curve's lookup points directly, with
  a live chart showing the curve and the current reading. Also has a
  **Manual Calibration** tool that steps the fan through raw speed levels one
  at a time so you can listen for where it stops getting louder -- this
  hardware family's 20-55 usable raw range (see below) is a well-sourced but
  *borrowed* community bound, not something measured on any specific unit.
- **GPU** -- power preset (Eco/Balanced/Performance) and graphics mode
  (Hybrid/Discrete/Optimus -- mode changes need a reboot and carry real risk
  on machines without a wired dGPU display path, which the UI warns about).
- **Power** -- CPU sustained (PL1) and boost (PL4) wattage limits, and idle
  power-saving toggle.
- **App GPU Routing** -- forces specific apps to the discrete or integrated
  GPU via the same `HKCU\...\DirectX\UserGpuPreferences` registry mechanism
  Windows Settings > Display > Graphics uses. Apps can be picked by browsing
  to the `.exe`, or via **Detect Running App**, which lists currently running
  processes with a visible window -- it doesn't guess which apps are "games"
  or GPU-heavy, you still choose the preference explicitly.
- **Settings** -- toggle launching OmniHub automatically at sign-in (via a
  Task Scheduler entry set to run elevated, not a registry Run key -- a Run
  key doesn't reliably auto-elevate an admin-required app), and choose
  whether the window's X button minimizes to tray or fully exits.

## Tray icon

- Single left-click opens a quick-glance flyout (temperature, throttling
  state, fan mode, commanded level) without opening the full window.
- Double-click, or the flyout's "Open OmniHub" button, opens the full window.
- Right-click gives quick fan-mode switches and Exit.
- A Windows notification fires the moment thermal throttling starts (not on
  every poll while it continues), so you find out even away from the app.

## Behavior on close

By default, closing the window minimizes to the system tray and keeps the
fan service running (that's the point -- the fix only holds while the app is
alive). The Settings tab lets you change the X button to fully exit instead.
Either way, use the tray icon's **Exit** (or the X button, if set to exit) to
fully quit; this always hands fan control back to the BIOS's own automatic
mode first. Never quit an OmniHub headless process (`-RunHeadless`) with
`kill -9` / Task Manager "End task" for the same reason -- use Ctrl+C or the
tray Exit path so the fan isn't left pinned.

**Important:** the fan-curve fix only protects you while OmniHub is actually
running in Auto (Curve) mode. Any time it's closed (fully exited), crashed,
or not yet launched after a reboot, the laptop is back on the stock BIOS
curve -- including its 0%-while-hot bug. Enabling "Launch at sign-in" in
Settings is the way to make that protection the default instead of something
you have to remember.

## Known limitations

- `OmniHub.Core/Hardware/PawnIoAccess.cs` is an intentional stub. Real
  CPU-package temperature via MSR read (a cross-check against the BIOS's
  single coarse sensor) needs the PawnIO SDK wired in against your actual
  installed version -- see the comments in that file.
- Fan "level" sent to the BIOS is **not** a 0-255 PWM duty cycle -- it's a
  fan-speed target in units of ~100 RPM, confirmed against OmenMon and
  decompiled Omen Gaming Hub source (see `OmniHub.Core/Fan/FanService.cs`).
  The real usable range across this HP EC family is only about raw 20-55
  (~2000-5500 RPM); this app's UI percentages (0-100%) are mapped onto that
  range, not onto the raw byte's full 0-255 span. This hardware interface
  does not expose real tachometer RPM; nothing in this app invents an RPM
  number that wasn't actually read.
- Per-model curve tuning is manual (via the Fans tab, its Manual Calibration
  tool, or `-Probe` output) -- there's no bundled database of per-model
  presets.
- `SystemController.GetThrottling()` is not independently verified against
  known-good hardware behavior -- see the doc comment on that method.

## Settings

Stored as plain JSON at `%AppData%\OmniHub\settings.json` (fan mode, curve
points, safety floor, close behavior). No telemetry, nothing sent anywhere.
The startup toggle lives outside this file, as a Windows Task Scheduler
entry named `OmniHub_AutoStart` (see `OmniHub.App/StartupManager.cs`).
