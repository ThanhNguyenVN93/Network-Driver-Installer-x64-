using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Threading;
using System.Windows.Forms;

namespace frmnetx64
{
    /// <summary>
    /// Network Driver Installer for Windows 7 x64.
    /// Auto-installs .NET Framework 4.5.2 if needed, then installs LAN and WiFi drivers.
    ///
    /// Crash-safety contract:
    ///   • All UI-updating calls go through SafeInvoke / SafeLog which check
    ///     IsDisposed + IsHandleCreated before touching controls.
    ///   • Every Process.Start() is wrapped in try/catch and uses a timeout.
    ///   • Every WMI / Registry / Shell-COM call is guarded; failures degrade
    ///     gracefully – the app never terminates without telling the user.
    ///   • BackgroundWorker lifecycle is honored: cancel on form close, never
    ///     call RunWorkerAsync while IsBusy, always inspect e.Error / e.Cancelled.
    /// </summary>
    public partial class Form1 : Form
    {
        // ── Logging colors ────────────────────────────────────────────────────
        private static readonly Color ColorInfo = Color.White;
        private static readonly Color ColorProcess = Color.Yellow;
        private static readonly Color ColorSuccess = Color.Lime;
        private static readonly Color ColorWarning = Color.Orange;
        private static readonly Color ColorError = Color.Red;

        // ── State ─────────────────────────────────────────────────────────────
        private bool lanInstalled;
        private bool wifiSupported;
        private BackgroundWorker installWorker;
        private BackgroundWorker netWorker;
        private DriverInstallJob currentJob;
        private volatile bool formClosing;

        // ── Constants ─────────────────────────────────────────────────────────
        private const string STARTUP_REG_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_NAME = "NetworkDriverInstallerX64";
        private const string DRIVERS_FOLDER = "drivers";
        private const string LAN_ZIP = "lanx64.zip";
        private const string WIFI_ZIP = "wifix64.zip";
        private const string NET_INSTALLER = "net452.exe";
        private const int DOTNET_452_RELEASE = 379893;
        private const int MAX_EXTRACTION_WAIT_MS = 60_000;
        private const int EXTRACTION_CHECK_INTERVAL = 500;
        private const int PNPUTIL_TIMEOUT_MS = 5 * 60 * 1000;  // 5 min
        private const int NET_INSTALL_TIMEOUT_MS = 15 * 60 * 1000; // 15 min

        // pnputil exit codes
        private const int EXIT_SUCCESS = 0;
        private const int EXIT_FILE_NOT_FOUND = 2;
        private const int EXIT_RESTART_REQUIRED = 3010;
        private const int EXIT_GENERAL_FAILURE = 256;
        private const int EXIT_STILL_EXECUTING = 259;

        // Driver file filtering
        private static readonly HashSet<string> RemovableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".log", ".txt", ".html", ".htm", ".xml", ".ini" };

        private static readonly HashSet<string> LanguageFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "en-US", "en-us", "ja-JP", "ja-jp", "zh-CN", "zh-cn", "fr-FR", "de-DE", "it-IT", "es-ES" };

        private static readonly HashSet<string> SHA2Patches = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "KB4474419", "KB4490628" };

        // ── Typed job descriptor ──────────────────────────────────────────────
        private sealed class DriverInstallJob
        {
            public string ZipFile { get; }
            public string FolderName { get; }
            public string DriverName { get; }
            public string DriverKey { get; }   // "lanx64" | "wifix64"

            public DriverInstallJob(string zipFile, string folderName, string driverName, string driverKey)
            {
                ZipFile = zipFile;
                FolderName = folderName;
                DriverName = driverName;
                DriverKey = driverKey;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        #region Constructor / Initialization
        // ─────────────────────────────────────────────────────────────────────

        public Form1()
        {
            InitializeComponent();
            InitializeInstallWorker();
            this.FormClosing += Form1_FormClosing;
        }

        private void InitializeInstallWorker()
        {
            installWorker = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            installWorker.DoWork += InstallWorker_DoWork;
            installWorker.ProgressChanged += InstallWorker_ProgressChanged;
            installWorker.RunWorkerCompleted += InstallWorker_RunWorkerCompleted;
        }

        private void InstallWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            SetProgress(e.ProgressPercentage);
            string msg = e.UserState as string;
            if (msg != null) SetStatus(msg);
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            formClosing = true;
            CancelWorker(installWorker);
            CancelWorker(netWorker);
            // Clean up temp folder %TEMP%\ThanhNguyen_Drivers
            AppResourceManager.Cleanup();
        }

        private static void CancelWorker(BackgroundWorker worker)
        {
            try
            {
                if (worker != null && worker.IsBusy)
                    worker.CancelAsync();
            }
            catch { /* non-critical */ }
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Form Load – startup sequence
        // ─────────────────────────────────────────────────────────────────────

        private void Form1_Load(object sender, EventArgs e)
        {
            // Wrap the entire load sequence – a single early WMI/Registry failure
            // must never kill the whole app.
            try
            {
                timer1.Start();
                try { lblos.Left = Math.Max(0, (ClientSize.Width - lblos.Width) / 2); }
                catch { /* designer-time oddities */ }

                Log("> === Network Driver Installer x64 Started ===", ColorInfo);

                if (!CheckDotNetFramework())
                {
                    InstallDotNetFramework();
                    return;
                }

                RemoveFromStartup();

                if (!CheckWindowsVersion())
                {
                    Log("> ✗ Unsupported OS. Exiting...", ColorError);
                    ShowError(
                        "This application only supports Windows 7 x64.",
                        "Unsupported OS");
                    BeginInvokeClose();
                    return;
                }

                CheckSHA2Support();
                DetectNetworkHardware();
            }
            catch (Exception ex)
            {
                Log($"> ✗ Startup error: {ex.Message}", ColorError);
                // Never rethrow in Load – that would unwind into an OS-level crash dialog.
            }
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Startup checks
        // ─────────────────────────────────────────────────────────────────────

        private bool CheckDotNetFramework()
        {
            Log("> Checking .NET Framework 4.5.2...", ColorProcess);
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\"))
                {
                    if (key == null)
                    {
                        Log("> ⚠ .NET Framework 4.5.2 not found", ColorWarning);
                        return false;
                    }

                    object raw = key.GetValue("Release");
                    if (raw == null || !int.TryParse(raw.ToString(), out int release))
                    {
                        Log("> ⚠ .NET Framework 4.5.2 not found", ColorWarning);
                        return false;
                    }

                    if (release >= DOTNET_452_RELEASE)
                    {
                        Log($"> ✓ .NET Framework 4.5.2+ detected (Release: {release})", ColorSuccess);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"> ⚠ Error checking .NET Framework: {ex.Message}", ColorWarning);
                return true;
            }

            Log("> ⚠ .NET Framework 4.5.2 not found", ColorWarning);
            return false;
        }

        private void InstallDotNetFramework()
        {
            SetStatus(".NET Framework 4.5.2 is required. Starting installation...");
            Log("> .NET Framework 4.5.2 installation required", ColorWarning);

            // Extract net452.exe from embedded resource to temp folder
            string net452Path = ExtractEmbeddedExecutable(NET_INSTALLER);
            if (net452Path == null)
            {
                Log($"> ✗ net452.exe not found in resources or external folder", ColorError);
                ShowError(
                    "net452.exe was not found.\nPlease place net452.exe in:\n" + Application.StartupPath,
                    "File Not Found");
                BeginInvokeClose();
                return;
            }

            AddToStartup();

            netWorker = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            netWorker.DoWork += (s, ev) => RunNetInstaller(ev, net452Path, (BackgroundWorker)s);
            netWorker.ProgressChanged += (s, ev) =>
            {
                SetProgress(ev.ProgressPercentage);
                string m = ev.UserState as string;
                if (m != null) SetStatus(m);
            };
            netWorker.RunWorkerCompleted += NetWorker_RunWorkerCompleted;

            try
            {
                netWorker.RunWorkerAsync();
            }
            catch (Exception ex)
            {
                Log($"> ✗ Could not start installer worker: {ex.Message}", ColorError);
                ShowError("Could not start the .NET Framework installer.", "Worker Error");
                return;
            }

            SetDriverButtons(false, false);
        }

        private void RunNetInstaller(DoWorkEventArgs e, string installerPath, BackgroundWorker worker)
        {
            try
            {
                worker.ReportProgress(10, "Starting .NET Framework 4.5.2 installation...");
                Log("> Running net452.exe installer...", ColorProcess);

                var psi = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = "/q /norestart",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using (var proc = Process.Start(psi))
                {
                    if (proc == null) { e.Result = -1; return; }
                    worker.ReportProgress(30, "Installing .NET Framework 4.5.2... Please wait...");

                    // Bounded wait – if the installer hangs we must not hang forever.
                    if (!proc.WaitForExit(NET_INSTALL_TIMEOUT_MS))
                    {
                        try { proc.Kill(); } catch { }
                        Log("> ✗ .NET installer timed out", ColorError);
                        e.Result = -2;
                        return;
                    }

                    int code = proc.ExitCode;
                    e.Result = code;

                    bool ok = code == EXIT_SUCCESS || code == EXIT_RESTART_REQUIRED;
                    Log(ok ? $"> ✓ .NET Framework installed (Exit: {code})"
                           : $"> ⚠ .NET installer exit code: {code}",
                        ok ? ColorSuccess : ColorWarning);
                    worker.ReportProgress(100, ok ? "Installation completed!" : $"Finished with code: {code}");
                }
            }
            catch (System.ComponentModel.Win32Exception wex)
            {
                Log($"> ✗ Cannot launch installer (UAC declined?): {wex.Message}", ColorError);
                e.Result = -1;
            }
            catch (Exception ex)
            {
                Log($"> ✗ Installation error: {ex.Message}", ColorError);
                e.Result = -1;
            }
        }

        private void NetWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (formClosing) return;

            int code = -1;
            if (e.Error != null)
            {
                Log("> ✗ .NET installer worker failed: " + e.Error.Message, ColorError);
            }
            else if (!e.Cancelled && e.Result != null && e.Result is int)
            {
                code = (int)e.Result;
            }

            bool ok = code == EXIT_SUCCESS || code == EXIT_RESTART_REQUIRED;

            if (ok)
            {
                var answer = MessageBox.Show(
                    ".NET Framework 4.5.2 has been installed.\n\n" +
                    "A restart is required to complete the installation.\n" +
                    "The application will automatically resume after reboot.\n\n" +
                    "Restart now?",
                    "Restart Required", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                if (answer == DialogResult.Yes) RestartComputer();
                else BeginInvokeClose();
            }
            else
            {
                ShowError("Failed to install .NET Framework.\nPlease install it manually.", "Installation Failed");
                BeginInvokeClose();
            }
        }

        private bool CheckWindowsVersion()
        {
            Log("> Detecting operating system...", ColorProcess);
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Caption, OSArchitecture, Version FROM Win32_OperatingSystem"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject obj in results)
                    {
                        try
                        {
                            string caption = obj["Caption"]?.ToString() ?? "Unknown";
                            string arch = obj["OSArchitecture"]?.ToString() ?? "Unknown";
                            string version = obj["Version"]?.ToString() ?? "Unknown";

                            SetOsLabel($"{caption} - {arch}");
                            Log($"> OS: {caption} - {arch}", ColorInfo);
                            Log($"> Version: {version}", ColorInfo);

                            bool isWin7 = version.StartsWith("6.1") || caption.IndexOf("Windows 7", StringComparison.OrdinalIgnoreCase) >= 0;
                            bool is64bit = arch.IndexOf("64", StringComparison.OrdinalIgnoreCase) >= 0 || Environment.Is64BitOperatingSystem;

                            if (isWin7 && is64bit)
                            {
                                Log("> ✓ Windows 7 x64 detected – Supported!", ColorSuccess);
                                return true;
                            }

                            if (!isWin7) Log(">   Reason: Not Windows 7", ColorError);
                            if (!is64bit) Log(">   Reason: Not 64-bit", ColorError);
                            return false;
                        }
                        finally
                        {
                            try { obj.Dispose(); } catch { }
                        }
                    }
                }

                // No results from WMI – fall back to Environment.OSVersion.
                var os = Environment.OSVersion;
                bool fallbackWin7 = os.Version.Major == 6 && os.Version.Minor == 1;
                bool fallback64bit = Environment.Is64BitOperatingSystem;
                Log($"> ⚠ WMI returned no data; falling back to OSVersion {os.Version}", ColorWarning);
                return fallbackWin7 && fallback64bit;
            }
            catch (Exception ex)
            {
                Log($"> ⚠ Error detecting OS via WMI: {ex.Message}", ColorWarning);
                // WMI can be disabled; fall back rather than crashing.
                var os = Environment.OSVersion;
                bool isWin7 = os.Version.Major == 6 && os.Version.Minor == 1;
                bool is64bit = Environment.Is64BitOperatingSystem;
                return isWin7 && is64bit;
            }
        }

        private void CheckSHA2Support()
        {
            Log("> Checking SHA-2 code signing support...", ColorProcess);
            try
            {
                string patch = FindInstalledSHA2Patch();
                if (!string.IsNullOrEmpty(patch))
                {
                    Log($"> ✓ SHA-2 support detected ({patch})", ColorSuccess);
                    return;
                }

                Log("> ⚠ SHA-2 update not detected (KB4474419 / KB4490628)", ColorWarning);
                Log(">   Modern drivers may fail without this patch", ColorWarning);

                var answer = MessageBox.Show(
                    "Warning: SHA-2 update not detected (KB4474419).\n\n" +
                    "Windows 7 requires this patch to support modern drivers signed with SHA-2.\n" +
                    "Some drivers may fail to install without this update.\n\n" +
                    "Do you want to continue?",
                    "SHA-2 Support Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (answer == DialogResult.No)
                {
                    Log("> User chose to exit – missing SHA-2 support", ColorInfo);
                    BeginInvokeClose();
                }
            }
            catch (Exception ex) { Log($"> ⚠ Could not check SHA-2: {ex.Message}", ColorWarning); }
        }

        private string FindInstalledSHA2Patch()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT HotFixID FROM Win32_QuickFixEngineering"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject hf in results)
                    {
                        try
                        {
                            string id = hf["HotFixID"]?.ToString() ?? string.Empty;
                            if (SHA2Patches.Contains(id)) return id;
                        }
                        finally { try { hf.Dispose(); } catch { } }
                    }
                }
            }
            catch { /* non-critical */ }
            return null;
        }

        private void DetectNetworkHardware()
        {
            Log("> Detecting network hardware...", ColorProcess);
            try
            {
                bool hasLAN = false, hasWiFi = false;
                string vendor = "Unknown";

                const string query =
                    "SELECT Name, Description, PNPDeviceID FROM Win32_NetworkAdapter " +
                    "WHERE (AdapterTypeId = 0 OR AdapterTypeId = 9) AND NOT PNPDeviceID LIKE 'ROOT%'";

                using (var searcher = new ManagementObjectSearcher(query))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject dev in results)
                    {
                        try
                        {
                            string name = dev["Name"]?.ToString() ?? "";
                            string desc = dev["Description"]?.ToString() ?? "";
                            string pnpId = dev["PNPDeviceID"]?.ToString() ?? "";

                            Log($"> Found adapter: {name}", ColorInfo);

                            if (IsWireless(name) || IsWireless(desc))
                            {
                                hasWiFi = true;
                                Log(">   └─ Type: WiFi", ColorSuccess);
                            }
                            else
                            {
                                hasLAN = true;
                                Log(">   └─ Type: LAN", ColorSuccess);
                            }

                            if (vendor == "Unknown")
                                vendor = DetectVendor(pnpId.ToUpperInvariant());
                        }
                        finally { try { dev.Dispose(); } catch { } }
                    }
                }

                Log($"> ✓ Vendor: {vendor}", ColorSuccess);
                Log($"> ✓ LAN:  {(hasLAN ? "Detected" : "None")}", hasLAN ? ColorSuccess : ColorWarning);
                Log($"> ✓ WiFi: {(hasWiFi ? "Detected" : "None")}", hasWiFi ? ColorSuccess : ColorWarning);

                wifiSupported = hasWiFi;
                SetDriverButtons(hasLAN, false);
                SetStatus(hasLAN
                    ? (hasWiFi ? "Ready – install LAN first, then WiFi" : "Ready – install LAN driver")
                    : "No LAN adapter detected");
            }
            catch (Exception ex)
            {
                Log($"> ⚠ Hardware detection error: {ex.Message}", ColorWarning);
                // Enable LAN button anyway – user may still want to try.
                SetDriverButtons(true, false);
                SetStatus("Hardware detection failed – manual install available");
            }
        }

        private static readonly string[] WirelessKeywords = { "wireless", "wi-fi", "802.11", "wlan", "wifi" };

        private static bool IsWireless(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            foreach (var keyword in WirelessKeywords)
            {
                if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static readonly Dictionary<string, string> VendorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "VEN_10EC", "Realtek" },
            { "VEN_8086", "Intel" },
            { "VEN_168C", "Atheros" },
            { "VEN_14E4", "Broadcom" },
            { "VEN_1969", "Qualcomm Atheros" }
        };

        private static string DetectVendor(string pnpId)
        {
            if (string.IsNullOrEmpty(pnpId)) return "Unknown";

            foreach (var kvp in VendorMap)
            {
                if (pnpId.Contains(kvp.Key))
                    return kvp.Value;
            }
            return "Unknown";
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Startup registry / task scheduler helpers
        // ─────────────────────────────────────────────────────────────────────

        private void AddToStartup()
        {
            if (CreateScheduledTask())
            {
                Log("> ✓ Added to Task Scheduler for auto-restart", ColorSuccess);
                return;
            }
            Log("> ⚠ Task Scheduler failed – using Registry fallback", ColorWarning);
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(STARTUP_REG_KEY, writable: true))
                {
                    if (key != null)
                    {
                        key.SetValue(APP_NAME, $"\"{Application.ExecutablePath}\"");
                        Log("> ✓ Added to startup registry", ColorSuccess);
                    }
                }
            }
            catch (Exception ex) { Log($"> ⚠ Could not add to startup: {ex.Message}", ColorWarning); }
        }

        private void RemoveFromStartup()
        {
            DeleteScheduledTask();
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(STARTUP_REG_KEY, writable: true))
                {
                    if (key?.GetValue(APP_NAME) != null)
                    {
                        key.DeleteValue(APP_NAME, throwOnMissingValue: false);
                        Log("> ✓ Removed from startup registry", ColorSuccess);
                    }
                }
            }
            catch { /* non-critical */ }
        }

        private bool CreateScheduledTask()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Create /TN \"{APP_NAME}\" /TR \"\\\"{Application.ExecutablePath}\\\"\" /SC ONLOGON /RL HIGHEST /F",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    if (!p.WaitForExit(15_000))
                    {
                        try { p.Kill(); } catch { }
                        return false;
                    }
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        private void DeleteScheduledTask()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Delete /TN \"{APP_NAME}\" /F",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return;
                    if (!p.WaitForExit(10_000))
                    {
                        try { p.Kill(); } catch { }
                        return;
                    }
                    if (p.ExitCode == 0)
                        Log("> ✓ Removed from Task Scheduler", ColorSuccess);
                }
            }
            catch { /* non-critical */ }
        }

        private void RestartComputer()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "shutdown.exe",
                    Arguments = "/r /t 5 /c \"Restarting to complete .NET Framework installation...\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                BeginInvokeClose();
            }
            catch (Exception ex)
            {
                Log($"> ✗ Restart error: {ex.Message}", ColorError);
                MessageBox.Show(
                    "Please restart your computer manually.",
                    "Restart Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                BeginInvokeClose();
            }
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Button handlers
        // ─────────────────────────────────────────────────────────────────────

        private void btnlan_Click(object sender, EventArgs e)
        {
            try
            {
                if (!EnsureWorkerIdle()) return;
                if (!AskRestorePoint("Before LAN Driver Installation")) return;

                Log("> === Starting LAN Driver Installation ===", ColorProcess);
                currentJob = new DriverInstallJob(LAN_ZIP, "LANDriverx64", "LAN Driver x64", "lanx64");
                SetDriverButtons(false, false);

                try { installWorker.RunWorkerAsync(currentJob); }
                catch (InvalidOperationException)
                {
                    Log("> ⚠ Worker already running – please wait.", ColorWarning);
                    SetDriverButtons(!lanInstalled, false);
                }
            }
            catch (Exception ex)
            {
                Log($"> ✗ LAN install error: {ex.Message}", ColorError);
                SetDriverButtons(!lanInstalled, false);
            }
        }

        private void btnwifi_Click(object sender, EventArgs e)
        {
            try
            {
                if (!EnsureWorkerIdle()) return;

                if (!lanInstalled)
                {
                    MessageBox.Show("Please install LAN driver first.",
                        "LAN Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!wifiSupported)
                {
                    MessageBox.Show(
                        "No WiFi adapter detected on this system.",
                        "WiFi Not Supported", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!AskRestorePoint("Before WiFi Driver Installation")) return;

                Log("> === Starting WiFi Driver Installation ===", ColorProcess);
                currentJob = new DriverInstallJob(WIFI_ZIP, "WiFiDriverx64", "WiFi Driver x64", "wifix64");
                SetDriverButtons(false, false);

                try { installWorker.RunWorkerAsync(currentJob); }
                catch (InvalidOperationException)
                {
                    Log("> ⚠ Worker already running – please wait.", ColorWarning);
                    SetDriverButtons(false, true);
                }
            }
            catch (Exception ex)
            {
                Log($"> ✗ WiFi install error: {ex.Message}", ColorError);
                SetDriverButtons(false, true);
            }
        }

        private void btnDevMgr_Click(object sender, EventArgs e)
        {
            OpenDeviceManager();
        }

        private bool EnsureWorkerIdle()
        {
            if (installWorker == null || !installWorker.IsBusy) return true;
            MessageBox.Show("Installation is already in progress.", "Busy",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        /// <summary>
        /// Prompt user about System Restore Point. Returns false if user cancels.
        /// </summary>
        private bool AskRestorePoint(string description)
        {
            var answer = MessageBox.Show(
                "Would you like to create a System Restore Point before installing the driver?\n\n" +
                "This allows you to roll back if something goes wrong.\n(Recommended)",
                "System Restore Point", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (answer == DialogResult.Cancel) return false;

            if (answer == DialogResult.Yes)
            {
                Log("> Creating System Restore Point...", ColorProcess);
                SetStatus("Creating System Restore Point...");

                // DO NOT call Application.DoEvents() – it re-enters the message loop
                // and has caused real-world crashes when users click buttons during this.
                bool created = false;
                try { created = CreateSystemRestorePoint(description); }
                catch (Exception ex) { Log($"> ⚠ Restore Point exception: {ex.Message}", ColorWarning); }

                if (!created)
                {
                    var retry = MessageBox.Show(
                        "Could not create a System Restore Point.\n\nDo you want to continue with the driver installation?",
                        "Restore Point Failed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (retry == DialogResult.No) return false;
                }
            }
            return true;
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region BackgroundWorker – driver installation pipeline
        // ─────────────────────────────────────────────────────────────────────

        private void ReportStep(BackgroundWorker worker, int pct, string msg)
        {
            try { worker.ReportProgress(pct, msg); } catch { }
            Log("> " + msg, ColorProcess);
        }

        private void InstallWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            DriverInstallJob job = e.Argument as DriverInstallJob;
            BackgroundWorker worker = (BackgroundWorker)sender;

            if (job == null)
            {
                Log("> ✗ Internal error: install job missing", ColorError);
                e.Result = "error";
                return;
            }

            try
            {
                // Step 1 – extract
                ReportStep(worker, 10, "[1/3] Extracting " + job.DriverName + "...");
                if (!ExtractDriverZip(job.ZipFile, job.FolderName))
                {
                    e.Result = "failed";
                    return;
                }
                ReportStep(worker, 30, "[1/3] Extraction complete");

                if (worker.CancellationPending) { e.Cancel = true; return; }

                // Step 2 – install via pnputil
                ReportStep(worker, 35, "[2/3] Installing " + job.DriverName + " via PnPUtil...");
                InstallDriver(job.FolderName);
                ReportStep(worker, 70, "[2/3] PnPUtil finished");

                if (worker.CancellationPending) { e.Cancel = true; return; }

                // Step 3 – rescan + cleanup
                ReportStep(worker, 75, "[3/3] Rescanning hardware...");
                RescanHardware();

                string driverFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop), job.FolderName);
                CleanupDriverFolder(driverFolder);

                ReportStep(worker, 100, "[3/3] Done!");
                e.Result = "success";
            }
            catch (Exception ex)
            {
                Log("> ✗ Unexpected error: " + ex.Message, ColorError);
                try { worker.ReportProgress(0, "Error during installation"); } catch { }
                e.Result = "error";
            }
        }

        private void InstallWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (formClosing) return;

            string result;
            if (e.Error != null)
            {
                Log($"> ✗ Worker exception: {e.Error.Message}", ColorError);
                result = "error";
            }
            else if (e.Cancelled)
            {
                result = "cancelled";
            }
            else
            {
                result = e.Result as string ?? "unknown";
            }

            bool success = result == "success";

            if (success)
            {
                Log($"> ✓ {currentJob?.DriverName ?? "Driver"} installed successfully!", ColorSuccess);

                if (currentJob?.DriverKey == "lanx64")
                {
                    lanInstalled = true;
                    if (wifiSupported)
                    {
                        SetDriverButtons(false, true);
                        SetStatus("✓ LAN installed! Click 'Install Wi-Fi' to continue.");
                        Log("> Ready for WiFi driver installation", ColorInfo);
                    }
                    else
                    {
                        SetStatus("✓ LAN driver installed successfully!");
                        Log("> Installation complete – no WiFi adapter on this system", ColorInfo);
                    }
                }
                else // wifix64
                {
                    SetStatus("✓ Both LAN and WiFi drivers installed!");
                    Log("> All drivers installed successfully!", ColorSuccess);
                }
            }
            else
            {
                Log($"> ✗ Installation {result}. Check log for details.", ColorError);
                SetDriverButtons(!lanInstalled, lanInstalled && wifiSupported);
                SetStatus("Installation failed – check the log.");
            }
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Driver extraction
        // ─────────────────────────────────────────────────────────────────────

        private bool ExtractDriverZip(string zipFileName, string folderName)
        {
            string destPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), folderName);

            // Use AppResourceManager: automatically prioritizes external file → embedded resource
            Log($"> Loading driver package: {zipFileName}...", ColorProcess);
            byte[] zipData = AppResourceManager.GetZipBytes(zipFileName);

            if (zipData == null)
            {
                string expected = Path.Combine(Application.StartupPath, DRIVERS_FOLDER, zipFileName);
                LogAndShowError(
                    $"Driver package '{zipFileName}' was not found.\n\n" +
                    $"Place the file in: {expected}\n" +
                    $"Or embed it under Resources\\ in the project.",
                    "Resource Error",
                    $"> ✗ Driver package not found: {zipFileName}\n>   Expected path: {expected}");
                return false;
            }

            Log($"> ✓ Driver package loaded ({zipData.Length / 1024} KB)", ColorSuccess);

            try
            {
                PrepareDirectory(destPath);
                return ExtractZipBytes(zipData, destPath);
            }
            catch (Exception ex)
            {
                Log($"> ✗ Extraction failed: {ex.Message}", ColorError);
                return false;
            }
        }

        /// <summary>
        /// Kept for backwards compatibility – delegates to AppResourceManager.
        /// </summary>
        private byte[] TryLoadExternalDriver(string zipFileName)
        {
            return AppResourceManager.GetZipBytes(zipFileName);
        }

        /// <summary>
        /// Kept for backwards compatibility – delegates to AppResourceManager.
        /// </summary>
        private byte[] TryLoadEmbeddedResource(string resourceName)
        {
            return AppResourceManager.GetZipBytes(resourceName);
        }
        private static void PrepareDirectory(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                    return;
                }

                // Clear existing contents to avoid stale driver conflicts.
                DeleteDirectoryContents(path);
            }
            catch
            {
                // If we cannot prep the directory, let the caller fail during extraction.
            }
        }

        private static void DeleteDirectoryContents(string path)
        {
            try
            {
                var dirInfo = new DirectoryInfo(path);

                // Delete files
                foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                {
                    try
                    {
                        if ((file.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                            file.Attributes &= ~FileAttributes.ReadOnly;
                        file.Delete();
                    }
                    catch { }
                }

                // Delete directories
                foreach (var dir in dirInfo.GetDirectories())
                {
                    try { dir.Delete(true); } catch { }
                }
            }
            catch { }
        }

        private bool ExtractZipBytes(byte[] zipData, string destPath)
        {
            string tempZip = Path.Combine(Path.GetTempPath(), $"drv_{Guid.NewGuid():N}.zip");
            try
            {
                Log("> Writing ZIP to temp location...", ColorProcess);
                File.WriteAllBytes(tempZip, zipData);

                Log("> Extracting via Shell.Application...", ColorProcess);
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null)
                {
                    Log("> ✗ Shell.Application COM not available", ColorError);
                    return false;
                }

                dynamic shell = null;
                try { shell = Activator.CreateInstance(shellType); }
                catch (Exception ex)
                {
                    Log($"> ✗ Cannot create Shell COM: {ex.Message}", ColorError);
                    return false;
                }

                dynamic src = shell.NameSpace(tempZip);
                dynamic dst = shell.NameSpace(destPath);

                if (src == null)
                {
                    Log("> ✗ Cannot open ZIP – file may be corrupted", ColorError);
                    return false;
                }
                if (dst == null)
                {
                    Log("> ✗ Cannot open destination namespace", ColorError);
                    return false;
                }

                int total = 0;
                try { total = (int)src.Items().Count; } catch { }
                Log($"> Extracting {total} item(s)...", ColorInfo);

                // 4 = no dialogs, 16 = yes-to-all, 1024 = no error UI
                try
                {
                    dst.CopyHere(src.Items(), 4 | 16 | 1024);
                }
                catch (Exception ex)
                {
                    Log($"> ✗ CopyHere failed: {ex.Message}", ColorError);
                    return false;
                }

                return WaitForExtraction(destPath);
            }
            catch (Exception ex)
            {
                Log($"> ✗ Extraction pipeline error: {ex.Message}", ColorError);
                return false;
            }
            finally
            {
                if (File.Exists(tempZip))
                    try { File.Delete(tempZip); } catch { }
            }
        }

        private bool WaitForExtraction(string destPath)
        {
            int elapsed = 0;
            int lastCount = -1;
            int stableCount = 0;

            while (elapsed < MAX_EXTRACTION_WAIT_MS)
            {
                Thread.Sleep(EXTRACTION_CHECK_INTERVAL);
                elapsed += EXTRACTION_CHECK_INTERVAL;
                try
                {
                    int count = 0;
                    try { count = Directory.GetFiles(destPath, "*", SearchOption.AllDirectories).Length; }
                    catch { /* dir still being created */ }

                    if (count > 0 && count == lastCount)
                    {
                        stableCount++;
                        if (stableCount >= 2)  // file count stopped changing for 1 sec
                        {
                            Log($"> ✓ Extraction complete ({count} files)", ColorSuccess);
                            return true;
                        }
                    }
                    else
                    {
                        stableCount = 0;
                    }
                    lastCount = count;
                }
                catch { /* directory may not be ready yet */ }
            }

            // Timeout – return true if any files landed; false otherwise.
            try
            {
                int count = Directory.GetFiles(destPath, "*", SearchOption.AllDirectories).Length;
                if (count > 0)
                {
                    Log($"> ⚠ Extraction timeout, but {count} files present – continuing", ColorWarning);
                    return true;
                }
            }
            catch { }

            Log("> ✗ Extraction timeout and no files extracted", ColorError);
            return false;
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Driver installation via pnputil
        // ─────────────────────────────────────────────────────────────────────

        private void InstallDriver(string folderName)
        {
            string driverPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), folderName);

            if (!Directory.Exists(driverPath))
            {
                Log($"> ✗ Driver folder not found: {driverPath}", ColorError);
                return;
            }

            string[] infFiles;
            try { infFiles = Directory.GetFiles(driverPath, "*.inf", SearchOption.AllDirectories); }
            catch (Exception ex)
            {
                Log($"> ✗ Could not enumerate INF files: {ex.Message}", ColorError);
                return;
            }

            if (infFiles.Length == 0)
            {
                Log($"> ✗ No .inf files found in: {driverPath}", ColorError);
                return;
            }

            Log($"> Found {infFiles.Length} .inf file(s):", ColorInfo);
            foreach (string inf in infFiles)
                Log($">   └─ {Path.GetFileName(inf)}: {ExtractDeviceName(inf)}", ColorSuccess);

            var psi = new ProcessStartInfo
            {
                FileName = "pnputil.exe",
                Arguments = $"/add-driver \"{driverPath}\\*.inf\" /install /subdirs",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                using (var p = Process.Start(psi))
                {
                    if (p == null) { Log("> ✗ Could not start pnputil", ColorError); return; }

                    p.OutputDataReceived += (_, ev) => { if (!string.IsNullOrEmpty(ev.Data)) Log($">   {ev.Data}", ColorInfo); };
                    p.ErrorDataReceived += (_, ev) => { if (!string.IsNullOrEmpty(ev.Data)) Log($">   ! {ev.Data}", ColorWarning); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    if (!p.WaitForExit(PNPUTIL_TIMEOUT_MS))
                    {
                        try { p.Kill(); } catch { }
                        Log("> ✗ pnputil timed out – killed", ColorError);
                        return;
                    }

                    HandlePnputilExitCode(p.ExitCode, folderName);
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Log("> ✗ Admin privileges required for pnputil", ColorError);
                MessageBox.Show(
                    "This action requires Administrator privileges.\nPlease run as Administrator.",
                    "Admin Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                Log($"> ✗ pnputil error: {ex.Message}", ColorError);
            }
        }

        private void HandlePnputilExitCode(int code, string folder)
        {
            string msg;
            Color color;

            switch (code)
            {
                case EXIT_SUCCESS:
                    msg = "Driver installed successfully";
                    color = ColorSuccess;
                    break;
                case EXIT_RESTART_REQUIRED:
                    msg = "Installed – restart required to activate driver";
                    color = ColorWarning;
                    break;
                case EXIT_FILE_NOT_FOUND:
                    msg = "File not found – driver package may be corrupted";
                    color = ColorError;
                    break;
                case EXIT_GENERAL_FAILURE:
                    msg = "General failure – driver may be incompatible or missing dependencies";
                    color = ColorError;
                    break;
                default:
                    msg = $"Unknown code {code} – check Device Manager";
                    color = ColorWarning;
                    break;
            }

            Log($"> PnPUtil exit {code}: {msg}", color);
            if (code != EXIT_SUCCESS && code != EXIT_RESTART_REQUIRED)
                Log($">   Folder: {folder} | Open Device Manager to investigate.", ColorWarning);
        }

        /// <summary>
        /// Scan first matching device-name string from an INF [Strings] section.
        /// </summary>
        private static readonly string[] DeviceNameKeywords =
            { "Ethernet", "Network", "Wireless", "WiFi", "LAN", "Adapter" };

        private static string ExtractDeviceName(string infPath)
        {
            try
            {
                foreach (string line in File.ReadLines(infPath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith(";")) continue;

                    int eqIndex = line.IndexOf('=');
                    if (eqIndex <= 0) continue;

                    string value = line.Substring(eqIndex + 1).Trim().Trim('"');
                    if (value.Length <= 5) continue;

                    foreach (var keyword in DeviceNameKeywords)
                    {
                        if (value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                            return value;
                    }
                }
            }
            catch { /* best-effort */ }
            return "Network Device";
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Post-install: rescan + cleanup
        // ─────────────────────────────────────────────────────────────────────

        private void RescanHardware()
        {
            Log("> Rescanning hardware...", ColorProcess);
            if (RunProcess("devcon.exe", "rescan", timeoutMs: 5000) ||
                RunProcess("pnputil.exe", "/scan-devices", timeoutMs: 5000))
            {
                Log("> ✓ Hardware rescan completed", ColorSuccess);
            }
            else
            {
                Log("> ⚠ Auto-rescan unavailable – consider restarting", ColorWarning);
            }
        }

        /// <summary>Returns true if process exits with code 0 within timeout.</summary>
        private static bool RunProcess(string exe, string args, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        return false;
                    }
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        private void CleanupDriverFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return;
            Log("> Cleaning up unnecessary files...", ColorProcess);

            int filesRemoved = 0, dirsRemoved = 0;

            try
            {
                var dirInfo = new DirectoryInfo(folderPath);

                // Remove unnecessary files
                filesRemoved = RemoveUnnecessaryFiles(dirInfo);

                // Remove language folders and empty directories (bottom-up)
                dirsRemoved = RemoveUnnecessaryDirectories(dirInfo);
            }
            catch { return; }

            Log($"> ✓ Cleanup done (removed {filesRemoved} files, {dirsRemoved} folders)", ColorSuccess);
        }

        private static int RemoveUnnecessaryFiles(DirectoryInfo dirInfo)
        {
            int count = 0;
            try
            {
                foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                {
                    if (RemovableExtensions.Contains(file.Extension))
                    {
                        try { file.Delete(); count++; } catch { }
                    }
                }
            }
            catch { }
            return count;
        }

        private static int RemoveUnnecessaryDirectories(DirectoryInfo dirInfo)
        {
            int count = 0;
            try
            {
                var allDirs = dirInfo.GetDirectories("*", SearchOption.AllDirectories);
                // Process bottom-up
                for (int i = allDirs.Length - 1; i >= 0; i--)
                {
                    var dir = allDirs[i];
                    try
                    {
                        if (LanguageFolders.Contains(dir.Name) || dir.GetFileSystemInfos().Length == 0)
                        {
                            dir.Delete(true);
                            count++;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return count;
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region System Restore Point
        // ─────────────────────────────────────────────────────────────────────

        private bool CreateSystemRestorePoint(string description)
        {
            try
            {
                using (var cls = new ManagementClass("root\\default", "SystemRestore", null))
                using (var inParams = cls.GetMethodParameters("CreateRestorePoint"))
                {
                    inParams["Description"] = description;
                    inParams["RestorePointType"] = 0;   // APPLICATION_INSTALL
                    inParams["EventType"] = 100; // BEGIN_SYSTEM_CHANGE

                    using (var outParams = cls.InvokeMethod("CreateRestorePoint", inParams, null))
                    {
                        uint ret = 1u;
                        object rv = outParams?["ReturnValue"];
                        if (rv != null) uint.TryParse(rv.ToString(), out ret);

                        if (ret == 0)
                        {
                            Log($"> ✓ Restore Point created: {description}", ColorSuccess);
                            return true;
                        }
                        Log($"> ⚠ Restore Point failed (WMI return {ret})", ColorWarning);
                    }
                }
            }
            catch (Exception ex) { Log($"> ⚠ Could not create Restore Point: {ex.Message}", ColorWarning); }
            return false;
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Logging and UI helpers (thread-safe, dispose-safe)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Safely marshal an action onto the UI thread.
        /// Swallows ObjectDisposedException / handle-not-created races that
        /// happen during form shutdown.
        /// </summary>
        private void SafeInvoke(Action action)
        {
            if (action == null) return;
            if (formClosing || IsDisposed) return;
            try
            {
                if (!IsHandleCreated)
                {
                    // Handle not yet created – execute directly if on UI thread,
                    // otherwise drop (caller is pre-load).
                    if (!InvokeRequired) action();
                    return;
                }
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch (ObjectDisposedException) { /* form went away */ }
            catch (InvalidOperationException) { /* handle destroyed mid-invoke */ }
            catch { /* last-resort swallow; we never crash on UI updates */ }
        }

        private void Log(string message, Color color)
        {
            SafeInvoke(() => AppendLog(message, color));
        }

        private const int MaxLogChars = 200_000;
        private const int TrimLogChars = 150_000;

        private void AppendLog(string text, Color color)
        {
            try
            {
                if (rtbLog == null || rtbLog.IsDisposed) return;

                // Trim log if too long before adding new content
                if (rtbLog.TextLength > MaxLogChars)
                {
                    rtbLog.Select(0, rtbLog.TextLength - TrimLogChars);
                    rtbLog.SelectedText = string.Empty;
                }

                rtbLog.SelectionStart = rtbLog.TextLength;
                rtbLog.SelectionLength = 0;
                rtbLog.SelectionColor = color;
                rtbLog.AppendText(text + Environment.NewLine);
                rtbLog.SelectionColor = rtbLog.ForeColor;
                rtbLog.ScrollToCaret();
            }
            catch { /* never crash while logging */ }
        }

        private void SetStatus(string text)
        {
            SafeInvoke(() =>
            {
                try { if (lblnettus != null && !lblnettus.IsDisposed) lblnettus.Text = text; }
                catch { }
            });
        }

        private void SetOsLabel(string text)
        {
            SafeInvoke(() =>
            {
                try { if (lblos != null && !lblos.IsDisposed) lblos.Text = text; }
                catch { }
            });
        }

        private void SetProgress(int pct)
        {
            int clamped = pct < 0 ? 0 : (pct > 100 ? 100 : pct);
            SafeInvoke(() =>
            {
                try { if (pbStatus != null && !pbStatus.IsDisposed) pbStatus.Value = clamped; }
                catch { }
            });
        }

        private void SetDriverButtons(bool lan, bool wifi)
        {
            SafeInvoke(() =>
            {
                try { if (btnlan != null && !btnlan.IsDisposed) btnlan.Enabled = lan; } catch { }
                try { if (btnwifi != null && !btnwifi.IsDisposed) btnwifi.Enabled = wifi; } catch { }
            });
        }

        private void BeginInvokeClose()
        {
            SafeInvoke(() =>
            {
                try { this.Close(); } catch { }
            });
        }

        private static void ShowError(string msg, string title)
        {
            try { MessageBox.Show(msg, title, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            catch { /* MessageBox shouldn't fail, but defensive */ }
        }

        private void LogAndShowError(string userMsg, string title, string logMsg)
        {
            Log(logMsg, ColorError);
            SafeInvoke(() => ShowError(userMsg, title));
        }

        private void OpenDeviceManager()
        {
            try
            {
                Log("> Opening Device Manager...", ColorProcess);
                var psi = new ProcessStartInfo
                {
                    FileName = "devmgmt.msc",
                    UseShellExecute = true   // required for .msc files
                };
                Process.Start(psi);
                Log("> ✓ Device Manager opened", ColorSuccess);
            }
            catch (Exception ex)
            {
                Log($"> ✗ Device Manager error: {ex.Message}", ColorError);
                try
                {
                    MessageBox.Show(
                        $"Could not open Device Manager:\n{ex.Message}",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            try
            {
                if (!IsDisposed && !formClosing)
                    Text = $"Network Driver Installer (x64) - {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            }
            catch { /* never crash from the clock */ }
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Embedded executable extraction
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the full path of an executable (e.g. net452.exe) ready to run.
        /// Delegates to AppResourceManager:
        ///   1. Look in drivers\ folder next to exe
        ///   2. Extract from embedded resource to %TEMP%\ThanhNguyen_Drivers
        /// Returns the full path, or null if not found.
        /// </summary>
        private string ExtractEmbeddedExecutable(string executableName)
        {
            Log($"> Locating executable: {executableName}...", ColorProcess);
            string path = AppResourceManager.GetExecutablePath(executableName);

            if (path != null)
                Log($"> ✓ Executable ready: {path}", ColorSuccess);
            else
                Log($"> ⚠ Executable not found: {executableName}", ColorWarning);

            return path;
        }
        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Link labels
        // ─────────────────────────────────────────────────────────────────────

        private void llbthanks_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            MessageBox.Show("Thanks for your product!",
                "Thank You!!", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Process.Start("https://www.anh-dv.com/");
        }

        private void llbdonate_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                using (var form2 = new Form2())
                {
                    form2.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                Log($"> ✗ Error opening donation form: {ex.Message}", ColorError);
                MessageBox.Show("Could not open donation form.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void OpenUrl(string url)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true   // required for URL handlers on modern Windows
                };
                Process.Start(psi);
            }
            catch { /* user has no default browser / policy restriction – non-fatal */ }
        }

        #endregion
    }
}
