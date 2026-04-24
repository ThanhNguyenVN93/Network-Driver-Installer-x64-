using System;
using System.IO;
using System.Reflection;

namespace frmnetx64
{
    /// <summary>
    /// Manages embedded resources (EmbeddedResource) for the All-in-One build.
    ///
    /// Resolution order:
    ///   1. External file: "drivers\" folder next to the exe  → highest priority
    ///      (allows replacing drivers without rebuilding)
    ///   2. Embedded resource in the assembly               → AIO fallback
    ///
    /// Temp folder: %TEMP%\ThanhNguyen_Drivers
    ///   • Created automatically when needed
    ///   • Deleted when the application exits (call Cleanup())
    /// </summary>
    internal static class AppResourceManager
    {
        // ── Temp folder ─────────────────────────────────────────────────────────
        private static readonly string TempRoot =
            Path.Combine(Path.GetTempPath(), "ThanhNguyen_Drivers");

        // ── External drivers folder name ───────────────────────────────────────
        private const string ExternalDriversFolder = "drivers";

        // ─────────────────────────────────────────────────────────────────────
        #region Public API
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the path to an executable (*.exe) ready to run.
        /// Resolution order: external → embedded → null if not found.
        /// </summary>
        /// <param name="fileName">File name, e.g. "net452.exe"</param>
        public static string GetExecutablePath(string fileName)
        {
            // 1. Try external file
            string externalPath = GetExternalPath(fileName);
            if (externalPath != null) return externalPath;

            // 2. Extract from embedded resources to %TEMP%\ThanhNguyen_Drivers
            return ExtractToTemp(fileName);
        }

        /// <summary>
        /// Returns the contents of a ZIP file as a byte[].
        /// Resolution order: external → embedded → null if not found.
        /// </summary>
        /// <param name="zipFileName">File name, e.g. "lanx64.zip"</param>
        public static byte[] GetZipBytes(string zipFileName)
        {
            // 1. Try external file
            string externalPath = GetExternalPath(zipFileName);
            if (externalPath != null)
            {
                try
                {
                    byte[] data = File.ReadAllBytes(externalPath);
                    Log($"[ARM] ✓ Loaded external: {externalPath} ({data.Length / 1024} KB)");
                    return data;
                }
                catch (Exception ex)
                {
                    Log($"[ARM] ⚠ Cannot read external file: {ex.Message}");
                }
            }

            // 2. Read from embedded resource
            return LoadEmbeddedBytes(zipFileName);
        }

        /// <summary>
        /// Deletes the entire temp folder %TEMP%\ThanhNguyen_Drivers.
        /// Call this when the application exits.
        /// </summary>
        public static void Cleanup()
        {
            try
            {
                if (Directory.Exists(TempRoot))
                {
                    Directory.Delete(TempRoot, recursive: true);
                    Log($"[ARM] ✓ Cleaned up temp folder: {TempRoot}");
                }
            }
            catch (Exception ex)
            {
                Log($"[ARM] ⚠ Cleanup failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Lists the names of all embedded resources in the assembly (for debugging).
        /// </summary>
        public static string[] ListEmbeddedResources()
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceNames();
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Private helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Checks whether a file exists in the "drivers\" folder next to the exe.
        /// Returns the full path, or null.
        /// </summary>
        private static string GetExternalPath(string fileName)
        {
            try
            {
                string path = Path.Combine(
                    System.Windows.Forms.Application.StartupPath,
                    ExternalDriversFolder,
                    fileName);

                if (File.Exists(path))
                {
                    Log($"[ARM] ✓ Found external file: {path}");
                    return path;
                }
            }
            catch { /* non-critical */ }
            return null;
        }

        /// <summary>
        /// Extracts a file from embedded resources to %TEMP%\ThanhNguyen_Drivers.
        /// Returns the path of the extracted file, or null on failure.
        /// </summary>
        private static string ExtractToTemp(string fileName)
        {
            byte[] data = LoadEmbeddedBytes(fileName);
            if (data == null) return null;

            try
            {
                EnsureTempDirectory();
                string tempPath = Path.Combine(TempRoot, fileName);
                File.WriteAllBytes(tempPath, data);
                Log($"[ARM] ✓ Extracted to temp: {tempPath} ({data.Length / 1024} KB)");
                return tempPath;
            }
            catch (Exception ex)
            {
                Log($"[ARM] ✗ Failed to extract '{fileName}' to temp: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads an embedded resource by file name (suffix match, case-insensitive).
        /// Returns byte[] or null.
        /// </summary>
        private static byte[] LoadEmbeddedBytes(string fileName)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                string matched = null;

                foreach (string name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        matched = name;
                        break;
                    }
                }

                if (matched == null)
                {
                    Log($"[ARM] ⚠ Embedded resource not found: {fileName}");
                    return null;
                }

                using (var stream = asm.GetManifestResourceStream(matched))
                {
                    if (stream == null) return null;

                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        byte[] buf = ms.ToArray();
                        Log($"[ARM] ✓ Loaded embedded resource: {matched} ({buf.Length / 1024} KB)");
                        return buf;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[ARM] ✗ Error loading embedded resource '{fileName}': {ex.Message}");
                return null;
            }
        }

        private static void EnsureTempDirectory()
        {
            if (!Directory.Exists(TempRoot))
                Directory.CreateDirectory(TempRoot);
        }

        /// <summary>
        /// Internal log — writes to Debug Output (never throws if no debugger is attached).
        /// </summary>
        private static void Log(string message)
        {
            try { System.Diagnostics.Debug.WriteLine(message); }
            catch { }
        }

        #endregion
    }
}