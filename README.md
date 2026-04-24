# Network Driver Installer x64

![Windows 7](https://img.shields.io/badge/Windows-7_x64-0078D6?style=flat&logo=windows&logoColor=white)
![.NET Framework](https://img.shields.io/badge/.NET_Framework-4.5.2-512BD4?style=flat&logo=.net&logoColor=white)
![Build](https://img.shields.io/badge/Build-x64_Release-brightgreen?style=flat)
![License](https://img.shields.io/badge/License-MIT-green.svg)

An **All-in-One** application that automatically installs LAN and WiFi network drivers on **Windows 7 x64**, with `.NET Framework 4.5.2` bundled inside — all driver packages are embedded directly into the `.exe`, no extra files required.

---

## Features

### .NET Framework 4.5.2 check and install
- Automatically checks the Registry; installs `net452.exe` if not present
- Adds itself to Startup to resume automatically after a reboot
- Shows progress via ProgressBar and color-coded log output

### Hardware detection
- Scans WMI to identify LAN / WiFi adapters
- Detects vendor: Realtek, Intel, Atheros, Broadcom, Qualcomm Atheros
- Enables / disables install buttons based on detected hardware

### Sequential and safe driver installation
- **LAN Driver** → must be installed first (required)
- **WiFi Driver** → unlocked only after LAN is done
- Optional **System Restore Point** before each install
- Uses `pnputil.exe` — a built-in Windows tool, no third-party software needed

### SHA-2 code signing check
- Warns if KB4474419 / KB4490628 is missing
- Modern drivers require SHA-2 signatures; this patch is mandatory on Win 7

### Color-coded log output

| Color | Meaning |
|-------|---------|
| White | Information |
| Yellow | Processing |
| Green | Success |
| Orange | Warning |
| Red | Error |

---

## System requirements

| Item | Requirement |
|------|-------------|
| OS | Windows 7 x64 (64-bit) |
| Privileges | Administrator (UAC) |
| .NET Framework | 4.5.2+ (auto-installed if missing) |
| SHA-2 patch | KB4474419 or KB4490628 (recommended) |

---

## How to use (AIO build)

1. **Run the exe** → right-click → **Run as Administrator**
2. The app checks for .NET Framework automatically
   - If missing → installs it → restarts → resumes automatically
3. Checks for SHA-2 patch; prompts you if it is missing
4. Click **Install LAN Driver** → wait for completion
5. Click **Install WiFi Driver** (if a WiFi adapter is present)
6. Open **Device Manager** to confirm

---

## Project structure

```
frmnetx64/
├── Form1.cs                  # Main application logic
├── Form1.Designer.cs         # UI designer
├── AppResourceManager.cs     # Embedded resource manager (AIO)
├── Program.cs                # Entry point
├── app.manifest              # UAC – requires Administrator privileges
├── frmnetx64.csproj          # Project file (declares EmbeddedResource)
├── Resources/                # Folder holding embedded files
│   ├── net452.exe            # .NET 4.5.2 offline installer
│   ├── lanx64.zip            # LAN driver package x64
│   └── wifix64.zip           # WiFi driver package x64
└── Properties/
    ├── AssemblyInfo.cs
    ├── Resources.resx
    └── Settings.settings
```

---

## Build from source

### Requirements
- Visual Studio 2015 or later
- .NET Framework 4.5.2 SDK

### Steps

```bash
git clone https://github.com/yourusername/frmnetx64.git
cd frmnetx64
```

1. Create the `Resources\` folder in the project (if it does not exist)
2. Copy the three real files into `Resources\`:
   - `net452.exe` — [download from Microsoft](https://www.microsoft.com/en-us/download/details.aspx?id=42642)
   - `lanx64.zip` — your LAN driver package (x64)
   - `wifix64.zip` — your WiFi driver package (x64)
3. In Visual Studio, select each file → Properties → **Build Action = Embedded Resource**
4. Build:

```bash
msbuild frmnetx64.csproj /p:Configuration=Release /p:Platform=AnyCPU
```

Output: `bin\Release\frmnetx64.exe` — fully self-contained, no companion files needed.

---

## How AppResourceManager works

`AppResourceManager` resolves resources in priority order:

```
File found in drivers\ next to exe?  ──Yes──▶ Use external file
        │ No
        ▼
Found in Embedded Resources?          ──Yes──▶ Extract to %TEMP%\ThanhNguyen_Drivers
        │ No
        ▼
      null  ──▶  Show error message to user
```

When the application closes, `%TEMP%\ThanhNguyen_Drivers` is cleaned up automatically.

---

## Troubleshooting

**`net452.exe not found`**
→ Copy the file to `Resources\` and set Build Action = Embedded Resource, or place it next to the exe inside a `drivers\` folder.

**"This application only supports Windows 7 x64"**
→ The application only runs on Windows 7 64-bit. Check your OS.

**"SHA-2 update not detected"**
→ Install KB4474419 first, then run the application again.

**Driver installed but not recognized**
→ Restart your computer, then check Device Manager.

**Build error CS1061**
→ Check `Form1.Designer.cs` — make sure no event handler was deleted (`timer1_Tick`, `llbdonate_LinkClicked`, `llbthanks_LinkClicked`, etc.).

---

## License

MIT License — see [LICENSE](LICENSE)

---

## Author

**Thanh Nguyen**  
Website: [anh-dv.com](https://www.anh-dv.com/)

---

*Made with ❤️ for Windows 7 users*
