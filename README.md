# BigKev

BigKev is a Windows controller profile and macro editor for a wired PS5
DualSense controller. It presents common controller-remapping workflows in a
cleaner, easier-to-understand interface.

## Current MVP

- Create, rename, duplicate, and delete profiles
- Map PlayStation controls and create button macros
- Build macros from press, release, and delay steps
- Validate macros before saving
- Persist profiles as readable JSON in `%LocalAppData%\BigKev\profiles`
- Wired PS5 DualSense discovery and live USB input reports
- Live display for buttons, sticks, and analog triggers

BigKev reads a physical DualSense over USB and uses HIDMaestro to expose a
virtual Xbox 360/XInput output for broad Windows game compatibility. The controller-test screen displays live button,
trigger, and stick activity.

## Build

This is a .NET 10 WPF application for Windows. Download
`HIDMaestro.Core.dll` from the
[official HIDMaestro releases](https://github.com/hifihedgehog/HIDMaestro/releases/latest)
and place it at
`src/PadPilot/ThirdParty/HIDMaestro/HIDMaestro.Core.dll` before building.

```powershell
dotnet build .\src\PadPilot\PadPilot.csproj
dotnet run --project .\src\PadPilot\PadPilot.csproj
```

## Portable release

The release configuration produces one self-contained `BigKev.exe`. Users do
not need to install .NET or extract a ZIP. Download the executable and open it.

Because local development builds are not code-signed, Windows SmartScreen may
show an unknown-publisher warning. Public releases should be signed with a
trusted Windows code-signing certificate.

## Safety

Macros are rate-limited and have a maximum step count. BigKev should be used
in accordance with the rules of the game or service being controlled.

