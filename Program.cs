using System;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace frmnetx64
{
    /// <summary>
    /// Entry point for Network Driver Installer x64.
    /// Installs global exception handlers, enforces single-instance,
    /// and verifies Administrator privileges before starting the main form.
    /// </summary>
    internal static class Program
    {
        private const string MutexName = "Global\\NetworkDriverInstallerX64_SingleInstance_{CED2DF1F-FF22-4A20-A138-8B4EF71E7E9E}";
        private const string CrashLogFile = "NetworkDriverInstallerX64_crash.log";

        [STAThread]
        static void Main()
        {
            // Global error traps (installed before ANY UI call).
            // Catch exceptions on the UI thread so the app never "disappears".
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainException;

            Mutex mutex = null;
            bool ownsMutex = false;
            try
            {
                // Single-instance guard
                bool createdNew;
                mutex = new Mutex(true, MutexName, out createdNew);
                ownsMutex = createdNew;

                if (!createdNew)
                {
                    MessageBox.Show(
                        "Ứng dụng đang chạy rồi.\nApplication is already running.",
                        "Already Running",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // Administrator check (belt-and-braces alongside manifest)
                if (!IsAdministrator())
                {
                    MessageBox.Show(
                        "Ứng dụng cần quyền Administrator để cài driver.\n" +
                        "Vui lòng chạy lại với quyền Run as Administrator.\n\n" +
                        "This application requires Administrator privileges.\n" +
                        "Please relaunch with 'Run as Administrator'.",
                        "Administrator Required",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Application.Run(new Form1());
            }
            catch (Exception ex)
            {
                ShowFatal(ex);
            }
            finally
            {
                if (mutex != null)
                {
                    if (ownsMutex)
                        try { mutex.ReleaseMutex(); } catch { /* already released */ }
                    try { mutex.Dispose(); } catch { /* non-critical */ }
                }
            }
        }

        // Privilege check
        private static bool IsAdministrator()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    if (identity == null) return false;
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                // If the check itself fails, assume NOT admin so the user is warned.
                return false;
            }
        }

        // Exception handlers
        private static void OnThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            LogCrash(e.Exception);
            try
            {
                MessageBox.Show(
                    "Đã xảy ra lỗi không mong đợi nhưng ứng dụng vẫn đang chạy.\n" +
                    "An unexpected error occurred but the application is still running.\n\n" +
                    "Chi tiết / Details: " + (e.Exception?.Message ?? "(no message)"),
                    "Unexpected Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch { /* last-resort – nothing we can do */ }
        }

        private static void OnAppDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            LogCrash(e.ExceptionObject as Exception);
            // Cannot keep the app alive past a non-UI-thread unhandled exception,
            // but at least we've written to the crash log.
        }

        // Crash log (written to %TEMP% so we never race with driver extraction)
        private static void LogCrash(Exception ex)
        {
            if (ex == null) return;
            try
            {
                string path = Path.Combine(Path.GetTempPath(), CrashLogFile);
                var sb = new StringBuilder();
                sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ");
                sb.Append(ex.GetType().FullName).Append(": ").AppendLine(ex.Message);
                sb.AppendLine(ex.ToString());
                sb.AppendLine(new string('-', 60));

                // Append is safer than WriteAllText – multiple crashes stack up.
                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Swallow — if we cannot log, we cannot log. Do not cascade.
            }
        }

        private static void ShowFatal(Exception ex)
        {
            LogCrash(ex);
            try
            {
                string msg = "Lỗi nghiêm trọng. Ứng dụng sẽ đóng.\n" +
                             "Fatal error. Application will close.\n\n" +
                             "Chi tiết / Details: " + (ex?.Message ?? "(unknown)") + "\n\n" +
                             "Crash log: " + Path.Combine(Path.GetTempPath(), CrashLogFile);

                MessageBox.Show(msg, "Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { /* give up quietly */ }
        }
    }
}
