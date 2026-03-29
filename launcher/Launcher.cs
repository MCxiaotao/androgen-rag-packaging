using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace AndrogenRagLauncher
{
    internal sealed class LauncherSettings
    {
        public string app_id = "AndrogenRAG";
        public string display_name = "Androgen RAG";
        public string channel = "stable";
        public string bootstrap_version = "1.0.0";
        public string manifest_url = "";
        public bool update_enabled = true;
        public bool open_browser = true;
        public int default_port = 8501;
        public int request_timeout_seconds = 10;
    }

    internal sealed class CurrentState
    {
        public string channel = "stable";
        public string current_version = null;
        public string previous_version = null;
        public string last_known_good_version = null;
        public string pending_version = null;
        public string updated_at = null;
    }

    internal sealed class ManifestWindows
    {
        public string arch = "x64";
        public string url = "";
        public string sha256 = "";
        public long size = 0;
        public ManifestDelta delta = null;
    }

    internal sealed class ManifestDelta
    {
        public string base_version = "";
        public string algorithm = "placeholder";
        public string url = "";
        public string sha256 = "";
        public long size = 0;
    }

    internal sealed class ManifestRoot
    {
        public string app_id = "AndrogenRAG";
        public string channel = "stable";
        public string version = "";
        public string pub_date = "";
        public string notes = "";
        public string min_launcher_version = "1.0.0";
        public ManifestWindows windows = new ManifestWindows();
    }

    internal static class Program
    {
        private const string LauncherVersion = "1.0.0";
        private static readonly string[] StateDirs =
        {
            "archive",
            "cache",
            "cache\\downloads",
            "cache\\manifests",
            "cache\\temp",
            "config",
            "input",
            "kb",
            "logs",
            "logs\\app",
            "logs\\launcher",
            "output",
            "packages",
            "packages\\versions",
            "runs",
        };

        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static string LogPath;

        private static int Main()
        {
            try
            {
                var installDir = InstallRoot();
                var settings = LoadSettings(installDir);
                var stateDir = StateRoot(settings.app_id);
                EnsureStateTree(stateDir);
                LogPath = Path.Combine(stateDir, "logs", "launcher", "launcher.log");
                Log("Launcher starting from " + installDir);

                var state = LoadCurrentState(stateDir);
                state = EnsureBootstrapAvailable(installDir, stateDir, state, settings.bootstrap_version);
                SaveCurrentState(stateDir, state);

                try
                {
                    state = MaybeUpdateBundle(settings, stateDir, state);
                }
                catch (Exception ex)
                {
                    Log("Update failed: " + ex);
                }

                var launchVersion = !string.IsNullOrWhiteSpace(state.pending_version) ? state.pending_version : state.current_version;
                if (string.IsNullOrWhiteSpace(launchVersion))
                    throw new InvalidOperationException("No runnable version available.");

                var bundleDir = VersionDir(stateDir, launchVersion);
                PrepareBundleRuntimeLayout(bundleDir, stateDir);
                EnsureBundleIsValid(bundleDir);

                var port = FindFreePort(settings.default_port);
                var process = LaunchApp(bundleDir, installDir, stateDir, launchVersion, port);
                if (WaitForReady(port, 45))
                {
                    state = CommitSuccessfulLaunch(stateDir, state, launchVersion);
                    if (settings.open_browser)
                        OpenBrowser(port);
                    Log("Launched version " + launchVersion + " on port " + port);
                    return 0;
                }

                TryKill(process);
                Log("Launch failed for version " + launchVersion);

                if (!string.IsNullOrWhiteSpace(state.pending_version) && !string.Equals(state.pending_version, state.current_version, StringComparison.OrdinalIgnoreCase))
                {
                    var fallbackVersion = state.current_version;
                    state = RollbackPending(stateDir, state);
                    if (!string.IsNullOrWhiteSpace(fallbackVersion))
                    {
                        var fallbackDir = VersionDir(stateDir, fallbackVersion);
                        PrepareBundleRuntimeLayout(fallbackDir, stateDir);
                        EnsureBundleIsValid(fallbackDir);
                        var fallbackPort = FindFreePort(settings.default_port);
                        var fallbackProcess = LaunchApp(fallbackDir, installDir, stateDir, fallbackVersion, fallbackPort);
                        if (WaitForReady(fallbackPort, 45))
                        {
                            CommitSuccessfulLaunch(stateDir, state, fallbackVersion);
                            if (settings.open_browser)
                                OpenBrowser(fallbackPort);
                            Log("Rollback launch succeeded on " + fallbackVersion);
                            return 0;
                        }
                        TryKill(fallbackProcess);
                    }
                }

                return 1;
            }
            catch (Exception ex)
            {
                Log("Launcher fatal error: " + ex);
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static string InstallRoot()
        {
            return Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
        }

        private static string StateRoot(string appId)
        {
            var overrideRoot = Environment.GetEnvironmentVariable("APP_STATE_ROOT");
            if (!string.IsNullOrWhiteSpace(overrideRoot))
                return Path.Combine(overrideRoot, appId);

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                throw new InvalidOperationException("LocalApplicationData not available.");
            return Path.Combine(localAppData, appId);
        }

        private static void EnsureStateTree(string root)
        {
            foreach (var relative in StateDirs)
                Directory.CreateDirectory(Path.Combine(root, relative));
        }

        private static T LoadJson<T>(string path, T fallback) where T : class
        {
            if (!File.Exists(path))
                return fallback;
            var text = File.ReadAllText(path, Encoding.UTF8);
            return Json.Deserialize<T>(text) ?? fallback;
        }

        private static void SaveJsonAtomic(string path, object payload)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + ".tmp";
            var json = Json.Serialize(payload);
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temp, path);
        }

        private static LauncherSettings LoadSettings(string installDir)
        {
            return LoadJson(Path.Combine(installDir, "launcher.json"), new LauncherSettings());
        }

        private static CurrentState LoadCurrentState(string stateDir)
        {
            var path = Path.Combine(stateDir, "current.json");
            var state = LoadJson(path, new CurrentState());
            if (!File.Exists(path))
                SaveCurrentState(stateDir, state);
            return state;
        }

        private static void SaveCurrentState(string stateDir, CurrentState state)
        {
            state.updated_at = DateTime.UtcNow.ToString("o");
            SaveJsonAtomic(Path.Combine(stateDir, "current.json"), state);
        }

        private static string VersionDir(string stateDir, string version)
        {
            return Path.Combine(stateDir, "packages", "versions", version);
        }

        private static void EnsureBundleIsValid(string bundleDir)
        {
            var python = Path.Combine(bundleDir, "runtime", "python.exe");
            var app = Path.Combine(bundleDir, "app", "scripts", "streamlit_app.py");
            if (!File.Exists(python) || !File.Exists(app))
                throw new InvalidOperationException("Bundle is incomplete: " + bundleDir);
        }

        private static CurrentState EnsureBootstrapAvailable(string installDir, string stateDir, CurrentState state, string bootstrapVersion)
        {
            if (!string.IsNullOrWhiteSpace(state.current_version))
            {
                var existing = VersionDir(stateDir, state.current_version);
                if (Directory.Exists(existing))
                {
                    try
                    {
                        EnsureBundleIsValid(existing);
                        return state;
                    }
                    catch
                    {
                    }
                }
            }

            var source = Path.Combine(installDir, "bootstrap", bootstrapVersion);
            if (!Directory.Exists(source))
            {
                var bootstrapRoot = Path.Combine(installDir, "bootstrap");
                if (!Directory.Exists(bootstrapRoot))
                    throw new InvalidOperationException("No bootstrap bundle available. Reinstall required.");
                var dirs = new DirectoryInfo(bootstrapRoot).GetDirectories().OrderByDescending(d => d.Name, StringComparer.OrdinalIgnoreCase).ToArray();
                if (dirs.Length == 0)
                    throw new InvalidOperationException("No bootstrap bundle available. Reinstall required.");
                source = dirs[0].FullName;
            }

            var sourceName = new DirectoryInfo(source).Name;
            var target = VersionDir(stateDir, sourceName);
            if (!Directory.Exists(target))
            {
                Log("Bootstrapping version " + sourceName + " from " + source);
                CopyDirectory(source, target);
            }

            state.current_version = sourceName;
            state.last_known_good_version = sourceName;
            state.pending_version = null;
            return state;
        }

        private static ManifestRoot FetchManifest(string url, int timeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;
            using (var client = new WebClient())
            {
                client.Headers.Add("User-Agent", "androgen-rag-launcher/1.0");
                client.Encoding = Encoding.UTF8;
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
                {
                    try
                    {
                        var text = client.DownloadString(url);
                        return Json.Deserialize<ManifestRoot>(text);
                    }
                    catch (Exception ex)
                    {
                        Log("Manifest fetch failed: " + ex.Message);
                        return null;
                    }
                }
            }
        }

        private static CurrentState MaybeUpdateBundle(LauncherSettings settings, string stateDir, CurrentState state)
        {
            if (!settings.update_enabled)
                return state;

            var manifest = FetchManifest(settings.manifest_url, settings.request_timeout_seconds);
            if (manifest == null || manifest.windows == null)
                return state;
            if (CompareVersions(manifest.version, state.current_version) <= 0)
                return state;
            if (CompareVersions(manifest.min_launcher_version, LauncherVersion) > 0)
            {
                Log("Remote bundle requires newer launcher: " + manifest.min_launcher_version);
                return state;
            }
            if (manifest.windows.delta != null && !string.IsNullOrWhiteSpace(manifest.windows.delta.url))
            {
                Log("Delta update advertised for " + manifest.windows.delta.base_version + " -> " + manifest.version + " (" + manifest.windows.delta.algorithm + "), falling back to full bundle in v1.");
            }
            if (string.IsNullOrWhiteSpace(manifest.windows.url))
                return state;

            var downloadDir = Path.Combine(stateDir, "cache", "downloads");
            Directory.CreateDirectory(downloadDir);
            var partial = Path.Combine(downloadDir, manifest.version + ".zip.partial");
            var finalZip = Path.Combine(downloadDir, manifest.version + ".zip");

            using (var client = new WebClient())
            {
                client.Headers.Add("User-Agent", "androgen-rag-launcher/1.0");
                client.DownloadFile(manifest.windows.url, partial);
            }

            var fileInfo = new FileInfo(partial);
            if (manifest.windows.size > 0 && fileInfo.Length != manifest.windows.size)
                throw new InvalidOperationException("Downloaded size mismatch for " + manifest.version);
            if (!string.IsNullOrWhiteSpace(manifest.windows.sha256))
            {
                var actual = Sha256Of(partial);
                if (!string.Equals(actual, manifest.windows.sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("SHA256 mismatch for " + manifest.version);
            }

            if (File.Exists(finalZip))
                File.Delete(finalZip);
            File.Move(partial, finalZip);

            var target = VersionDir(stateDir, manifest.version);
            InstallZipBundle(finalZip, target);
            state.pending_version = manifest.version;
            SaveCurrentState(stateDir, state);
            Log("Installed pending version " + manifest.version);
            return state;
        }

        private static void InstallZipBundle(string zipPath, string targetDir)
        {
            var parent = Directory.GetParent(targetDir).FullName;
            Directory.CreateDirectory(parent);
            var tempRoot = Path.Combine(Path.GetTempPath(), "ARStage");
            Directory.CreateDirectory(tempRoot);
            var extractDir = Path.Combine(tempRoot, "e_" + Guid.NewGuid().ToString("N"));
            var stageDir = Path.Combine(tempRoot, "s_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(extractDir);
            try
            {
                ZipFile.ExtractToDirectory(zipPath, extractDir);
                var extracted = NormalizeExtractedRoot(extractDir);
                EnsureBundleIsValid(extracted);
                if (Directory.Exists(stageDir))
                    Directory.Delete(stageDir, true);
                Directory.Move(extracted, stageDir);
                if (Directory.Exists(targetDir))
                    Directory.Delete(targetDir, true);
                MoveDirectoryOrCopy(stageDir, targetDir);
            }
            finally
            {
                SafeDeleteDirectory(extractDir);
                SafeDeleteDirectory(stageDir);
            }
        }

        private static string NormalizeExtractedRoot(string extractDir)
        {
            var dirs = new DirectoryInfo(extractDir).GetDirectories();
            var files = new DirectoryInfo(extractDir).GetFiles();
            if (dirs.Length == 1 && files.Length == 0)
                return dirs[0].FullName;
            return extractDir;
        }

        private static void PrepareBundleRuntimeLayout(string bundleDir, string stateDir)
        {
            var appDir = Path.Combine(bundleDir, "app");
            var kbSeed = Path.Combine(appDir, "kb");
            var kbTarget = Path.Combine(stateDir, "kb");
            SyncSeedDirectory(kbSeed, kbTarget);

            EnsureDirJunction(Path.Combine(appDir, "input"), Path.Combine(stateDir, "input"), false);
            EnsureDirJunction(Path.Combine(appDir, "output"), Path.Combine(stateDir, "output"), false);
            EnsureDirJunction(Path.Combine(appDir, "runs"), Path.Combine(stateDir, "runs"), false);
            EnsureDirJunction(Path.Combine(appDir, "archive"), Path.Combine(stateDir, "archive"), false);
            EnsureDirJunction(Path.Combine(appDir, "kb"), kbTarget, true);
        }

        private static void SyncSeedDirectory(string sourceDir, string targetDir)
        {
            if (!Directory.Exists(sourceDir))
                return;
            var attrs = File.GetAttributes(sourceDir);
            if ((attrs & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                return;
            var sourceFull = Path.GetFullPath(sourceDir).TrimEnd('\\');
            var targetFull = Path.GetFullPath(targetDir).TrimEnd('\\');
            if (string.Equals(sourceFull, targetFull, StringComparison.OrdinalIgnoreCase))
                return;
            CopyDirectory(sourceDir, targetDir, true);
        }

        private static void EnsureDirJunction(string linkPath, string targetPath, bool allowReplaceDir)
        {
            Directory.CreateDirectory(targetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(linkPath));

            if (Directory.Exists(linkPath))
            {
                if (IsSameResolvedPath(linkPath, targetPath))
                    return;

                var attrs = File.GetAttributes(linkPath);
                var isReparse = (attrs & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
                if (isReparse)
                {
                    Directory.Delete(linkPath);
                }
                else if (allowReplaceDir)
                {
                    Directory.Delete(linkPath, true);
                }
                else if (!Directory.EnumerateFileSystemEntries(linkPath).Any())
                {
                    Directory.Delete(linkPath);
                }
                else
                {
                    throw new InvalidOperationException("Refusing to replace non-empty directory: " + linkPath);
                }
            }
            else if (File.Exists(linkPath))
            {
                File.Delete(linkPath);
            }

            var psi = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + linkPath + "\" \"" + targetPath + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var proc = Process.Start(psi);
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                throw new InvalidOperationException("mklink failed: " + stdout + " " + stderr);
            }
        }

        private static bool IsSameResolvedPath(string a, string b)
        {
            try
            {
                var left = new DirectoryInfo(a).FullName.TrimEnd('\\');
                var right = new DirectoryInfo(b).FullName.TrimEnd('\\');
                if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
            }
            return false;
        }

        private static Process LaunchApp(string bundleDir, string installDir, string stateDir, string version, int port)
        {
            var runtimeDir = Path.Combine(bundleDir, "runtime");
            var appDir = Path.Combine(bundleDir, "app");
            var pythonExe = Path.Combine(runtimeDir, "python.exe");
            var appScript = Path.Combine(appDir, "scripts", "streamlit_app.py");
            var stdoutPath = Path.Combine(stateDir, "logs", "app", "app-" + version + "-stdout.log");
            var stderrPath = Path.Combine(stateDir, "logs", "app", "app-" + version + "-stderr.log");

            var args = string.Format("/c \"\"{0}\" -m streamlit run \"{1}\" --server.address 127.0.0.1 --server.port {2} --server.headless true --browser.gatherUsageStats false 1>>\"{3}\" 2>>\"{4}\"\"", pythonExe, appScript, port, stdoutPath, stderrPath);
            var psi = new ProcessStartInfo("cmd.exe", args)
            {
                WorkingDirectory = appDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            var tempDir = Path.Combine(stateDir, "cache", "temp");
            Directory.CreateDirectory(tempDir);

            var pathEntries = new[]
            {
                runtimeDir,
                Path.Combine(runtimeDir, "Scripts"),
                Path.Combine(runtimeDir, "Library", "bin"),
                Path.Combine(runtimeDir, "Library", "mingw-w64", "bin"),
                Path.Combine(runtimeDir, "Library", "usr", "bin"),
                Path.Combine(runtimeDir, "DLLs"),
            }.Where(Directory.Exists).ToArray();

            var chemprop = Path.Combine(bundleDir, "vendor", "envs", "pk_env", "Scripts", "chemprop.exe");
            var admetPredict = Path.Combine(runtimeDir, "Scripts", "admet_predict.exe");
            var mainChemprop = Path.Combine(runtimeDir, "Scripts", "chemprop.exe");
            var fpgnnRepo = Path.Combine(bundleDir, "vendor", "fpgnn");
            var fpgnnPython = Path.Combine(bundleDir, "vendor", "envs", "fpgnn_cyp2", "python.exe");
            var smartcypJar = Path.Combine(bundleDir, "vendor", "smartcyp", "target", "smartcyp.jar");
            var smartcypJava = Path.Combine(bundleDir, "vendor", "jre", "bin", "java.exe");
            var sygmaPython = Path.Combine(bundleDir, "vendor", "envs", "sygma_env", "python.exe");
            var existingPath = Environment.GetEnvironmentVariable("Path") ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

            var envOverrides = new Dictionary<string, string>
            {
                { "PYTHONUTF8", "1" },
                { "PYTHONIOENCODING", "utf-8" },
                { "APP_INSTALL_DIR", installDir },
                { "APP_STATE_DIR", stateDir },
                { "APP_DATA_DIR", stateDir },
                { "APP_BUNDLE_DIR", bundleDir },
                { "APP_BUNDLE_APP_DIR", appDir },
                { "APP_VERSION", version },
                { "APP_PORT", port.ToString() },
                { "APP_TEMP_DIR", tempDir },
                { "TEMP", tempDir },
                { "TMP", tempDir },
                { "Path", string.Join(";", pathEntries.Concat(string.IsNullOrWhiteSpace(existingPath) ? new string[0] : new[] { existingPath })) },
                { "FPGNN_PYTHON", File.Exists(fpgnnPython) ? fpgnnPython : pythonExe },
                { "SYGMA_PYTHON", File.Exists(sygmaPython) ? sygmaPython : pythonExe },
            };

            if (File.Exists(chemprop)) envOverrides["CHEMPROP_EXE"] = chemprop;
            else if (File.Exists(mainChemprop)) envOverrides["CHEMPROP_EXE"] = mainChemprop;
            if (File.Exists(admetPredict)) envOverrides["ADMET_PREDICT_EXE"] = admetPredict;
            if (Directory.Exists(fpgnnRepo)) envOverrides["FPGNN_REPO"] = fpgnnRepo;
            if (File.Exists(smartcypJar)) envOverrides["SMARTCYP_JAR"] = smartcypJar;
            if (File.Exists(smartcypJava)) envOverrides["SMARTCYP_JAVA"] = smartcypJava;

            var originalEnv = new Dictionary<string, string>();
            foreach (var item in envOverrides)
            {
                originalEnv[item.Key] = Environment.GetEnvironmentVariable(item.Key);
                Environment.SetEnvironmentVariable(item.Key, item.Value);
            }

            try
            {
                return Process.Start(psi);
            }
            finally
            {
                foreach (var item in originalEnv)
                    Environment.SetEnvironmentVariable(item.Key, item.Value);
            }
        }

        private static bool WaitForReady(int port, int timeoutSeconds)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            var urls = new[]
            {
                "http://127.0.0.1:" + port + "/_stcore/health",
                "http://127.0.0.1:" + port + "/",
            };

            while (DateTime.UtcNow < deadline)
            {
                foreach (var url in urls)
                {
                    try
                    {
                        var request = WebRequest.Create(url);
                        request.Timeout = 3000;
                        using (var response = (HttpWebResponse)request.GetResponse())
                        {
                            if ((int)response.StatusCode < 500)
                                return true;
                        }
                    }
                    catch
                    {
                    }
                }
                Thread.Sleep(1000);
            }
            return false;
        }

        private static void OpenBrowser(int port)
        {
            Process.Start("http://127.0.0.1:" + port + "/");
        }

        private static CurrentState CommitSuccessfulLaunch(string stateDir, CurrentState state, string launchedVersion)
        {
            if (!string.Equals(state.current_version, launchedVersion, StringComparison.OrdinalIgnoreCase))
                state.previous_version = state.current_version;
            state.current_version = launchedVersion;
            state.last_known_good_version = launchedVersion;
            state.pending_version = null;
            SaveCurrentState(stateDir, state);
            return state;
        }

        private static CurrentState RollbackPending(string stateDir, CurrentState state)
        {
            state.pending_version = null;
            SaveCurrentState(stateDir, state);
            return state;
        }

        private static int FindFreePort(int preferredPort)
        {
            foreach (var port in new[] { preferredPort, 8502, 8503, 8504, 8505 })
            {
                try
                {
                    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                    listener.Start();
                    listener.Stop();
                    return port;
                }
                catch
                {
                }
            }

            var fallback = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            fallback.Start();
            var freePort = ((System.Net.IPEndPoint)fallback.LocalEndpoint).Port;
            fallback.Stop();
            return freePort;
        }

        private static int CompareVersions(string left, string right)
        {
            var l = (left ?? "").Split(new[] { '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var r = (right ?? "").Split(new[] { '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var len = Math.Max(l.Length, r.Length);
            for (var i = 0; i < len; i++)
            {
                var a = i < l.Length ? l[i] : "0";
                var b = i < r.Length ? r[i] : "0";
                int ai, bi;
                var aNum = int.TryParse(a, out ai);
                var bNum = int.TryParse(b, out bi);
                int cmp;
                if (aNum && bNum)
                    cmp = ai.CompareTo(bi);
                else
                    cmp = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0)
                    return cmp;
            }
            return 0;
        }

        private static string Sha256Of(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var hash = sha.ComputeHash(stream);
                var sb = new StringBuilder();
                foreach (var b in hash)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir, bool overwrite = false)
        {
            var source = new DirectoryInfo(sourceDir);
            if (!source.Exists)
                return;
            Directory.CreateDirectory(destDir);
            foreach (var file in source.GetFiles())
            {
                var target = Path.Combine(destDir, file.Name);
                file.CopyTo(target, overwrite);
            }
            foreach (var dir in source.GetDirectories())
            {
                CopyDirectory(dir.FullName, Path.Combine(destDir, dir.Name), overwrite);
            }
        }

        private static void MoveDirectoryOrCopy(string sourceDir, string destDir)
        {
            try
            {
                Directory.Move(sourceDir, destDir);
            }
            catch (IOException)
            {
                CopyDirectory(sourceDir, destDir, true);
                SafeDeleteDirectory(sourceDir);
            }
            catch (UnauthorizedAccessException)
            {
                CopyDirectory(sourceDir, destDir, true);
                SafeDeleteDirectory(sourceDir);
            }
        }

        private static void SafeDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (process != null && !process.HasExited)
                    process.Kill();
            }
            catch
            {
            }
        }

        private static void Log(string message)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(LogPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                    File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch
            {
            }
            Console.WriteLine(message);
        }
    }
}





