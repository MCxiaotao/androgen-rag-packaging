using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AndrogenRagUninstall
{
    internal sealed class InstallManifest
    {
        public string app_id = "AndrogenRAG";
        public string display_name = "Androgen RAG";
        public string publisher = "MCxiaotao";
        public string version = "1.0.0";
        public string install_dir = "";
        public string state_dir = "";
        public string launcher_exe = "launcher.exe";
        public string uninstall_exe = "uninstall.exe";
        public string shortcut_name = "Androgen RAG";
    }

    internal sealed class RunningProcessInfo
    {
        public int Id;
        public string Name = string.Empty;
        public string ExecutablePath = string.Empty;
        public string CommandLine = string.Empty;
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UninstallForm(LoadManifest()));
        }

        private static InstallManifest LoadManifest()
        {
            var exePath = Assembly.GetExecutingAssembly().Location;
            var installDir = Path.GetDirectoryName(exePath) ?? string.Empty;
            var manifestPath = Path.Combine(installDir, "install_manifest.json");
            if (!File.Exists(manifestPath))
            {
                return new InstallManifest
                {
                    install_dir = installDir,
                    state_dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AndrogenRAG"),
                };
            }

            try
            {
                var serializer = new JavaScriptSerializer();
                var text = File.ReadAllText(manifestPath);
                var manifest = serializer.Deserialize<InstallManifest>(text) ?? new InstallManifest();
                if (string.IsNullOrWhiteSpace(manifest.install_dir))
                    manifest.install_dir = installDir;
                if (string.IsNullOrWhiteSpace(manifest.state_dir))
                    manifest.state_dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), manifest.app_id ?? "AndrogenRAG");
                return manifest;
            }
            catch
            {
                return new InstallManifest
                {
                    install_dir = installDir,
                    state_dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AndrogenRAG"),
                };
            }
        }
    }

    internal sealed class UninstallForm : Form
    {
        private readonly InstallManifest _manifest;
        private readonly CheckBox _removeUserDataBox;
        private readonly Label _statusLabel;
        private readonly Button _uninstallButton;
        private readonly Button _cancelButton;

        public UninstallForm(InstallManifest manifest)
        {
            _manifest = manifest;

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = (manifest.display_name ?? "Androgen RAG") + " \u5378\u8f7d";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(720, 340);
            Font = SystemFonts.MessageBoxFont;

            var title = new Label
            {
                Text = "\u5378\u8f7d " + (manifest.display_name ?? "Androgen RAG"),
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 15, FontStyle.Bold),
                Location = new Point(20, 18),
            };
            Controls.Add(title);

            var intro = new Label
            {
                AutoSize = false,
                Size = new Size(668, 108),
                Location = new Point(22, 56),
                Text = "\u5378\u8f7d\u5c06\u79fb\u9664\u5b89\u88c5\u76ee\u5f55\u4e2d\u7684\u542f\u52a8\u5668\u3001\u7248\u672c\u5305\u548c\u5feb\u6377\u65b9\u5f0f\u3002\r\n\r\n"
                    + "\u5b89\u88c5\u76ee\u5f55: " + (_manifest.install_dir ?? string.Empty) + "\r\n"
                    + "\u7528\u6237\u6570\u636e\u76ee\u5f55: " + (_manifest.state_dir ?? string.Empty),
            };
            Controls.Add(intro);

            _removeUserDataBox = new CheckBox
            {
                Text = "\u540c\u65f6\u5220\u9664\u7528\u6237\u6570\u636e\u76ee\u5f55\uff08input / output / logs / cache\uff09",
                Checked = false,
                AutoSize = true,
                Location = new Point(24, 184),
            };
            Controls.Add(_removeUserDataBox);

            _statusLabel = new Label
            {
                AutoSize = false,
                Size = new Size(470, 46),
                Location = new Point(22, 238),
                Text = string.Empty,
            };
            Controls.Add(_statusLabel);

            _uninstallButton = new Button
            {
                Text = "\u5378\u8f7d",
                Size = new Size(96, 30),
                Location = new Point(516, 246),
            };
            _uninstallButton.Click += UninstallButton_Click;
            Controls.Add(_uninstallButton);

            _cancelButton = new Button
            {
                Text = "\u53d6\u6d88",
                Size = new Size(96, 30),
                Location = new Point(622, 246),
            };
            _cancelButton.Click += delegate { Close(); };
            Controls.Add(_cancelButton);
        }

        private void UninstallButton_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                this,
                "\u786e\u8ba4\u5378\u8f7d " + (_manifest.display_name ?? "Androgen RAG") + "\uff1f",
                "\u5378\u8f7d",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
                return;

            try
            {
                _statusLabel.Text = "\u6b63\u5728\u6e05\u7406\u5feb\u6377\u65b9\u5f0f\u5e76\u51c6\u5907\u5220\u9664\u6587\u4ef6...";
                _uninstallButton.Enabled = false;
                _cancelButton.Enabled = false;

                if (!EnsureNoConflictingProcesses())
                {
                    _statusLabel.Text = "\u5378\u8f7d\u5df2\u53d6\u6d88\u3002";
                    _uninstallButton.Enabled = true;
                    _cancelButton.Enabled = true;
                    return;
                }

                RemoveDesktopShortcut();
                RemoveStartMenuShortcuts();
                UnregisterUninstallEntry();
                ScheduleCleanup(_removeUserDataBox.Checked);

                MessageBox.Show(this, "\u5378\u8f7d\u4efb\u52a1\u5df2\u542f\u52a8\u3002\u5173\u95ed\u672c\u7a97\u53e3\u540e\uff0c\u5b89\u88c5\u76ee\u5f55\u4f1a\u81ea\u52a8\u5220\u9664\u3002", "\u5378\u8f7d", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "\u5378\u8f7d\u5931\u8d25\u3002";
                _uninstallButton.Enabled = true;
                _cancelButton.Enabled = true;
                MessageBox.Show(this, ex.Message, "\u5378\u8f7d\u5931\u8d25", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RemoveDesktopShortcut()
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop))
                return;
            var shortcut = Path.Combine(desktop, (_manifest.shortcut_name ?? _manifest.display_name ?? "Androgen RAG") + ".lnk");
            if (File.Exists(shortcut))
                File.Delete(shortcut);
        }

        private void RemoveStartMenuShortcuts()
        {
            var programsDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (string.IsNullOrWhiteSpace(programsDir))
                return;
            var groupDir = Path.Combine(programsDir, _manifest.shortcut_name ?? _manifest.display_name ?? "Androgen RAG");
            if (Directory.Exists(groupDir))
                Directory.Delete(groupDir, true);
        }

        private void UnregisterUninstallEntry()
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + (_manifest.app_id ?? "AndrogenRAG"), false);
        }

        private bool EnsureNoConflictingProcesses()
        {
            var related = FindRelatedProcesses(_manifest.install_dir, _manifest.state_dir);
            if (related.Count == 0)
                return true;

            var details = string.Join("\r\n", related.Select(p => "- " + p.Name + " (PID " + p.Id + ")"));
            var answer = MessageBox.Show(
                this,
                "\u68c0\u6d4b\u5230\u7a0b\u5e8f\u4ecd\u5728\u8fd0\u884c\uff0c\u5378\u8f7d\u524d\u9700\u8981\u5148\u5173\u95ed\u3002\r\n\r\n"
                + details
                + "\r\n\r\n\u70b9\u51fb\u201c\u662f\u201d\u81ea\u52a8\u7ed3\u675f\u8fd9\u4e9b\u8fdb\u7a0b\u5e76\u7ee7\u7eed\u5378\u8f7d\uff0c\u70b9\u51fb\u201c\u5426\u201d\u53d6\u6d88\u5378\u8f7d\u3002",
                "\u5378\u8f7d",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
                return false;

            if (!TryStopProcesses(related, 10))
            {
                MessageBox.Show(this, "\u4ecd\u6709\u76f8\u5173\u8fdb\u7a0b\u672a\u80fd\u5173\u95ed\uff0c\u8bf7\u624b\u52a8\u9000\u51fa\u7a0b\u5e8f\u540e\u518d\u5378\u8f7d\u3002", "\u5378\u8f7d", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

        private static List<RunningProcessInfo> FindRelatedProcesses(string installDir, string stateDir)
        {
            var result = new List<RunningProcessInfo>();
            var currentPid = Process.GetCurrentProcess().Id;
            var installNeedle = NormalizeNeedle(installDir);
            var stateNeedle = NormalizeNeedle(stateDir);

            using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, Name, ExecutablePath, CommandLine FROM Win32_Process"))
            using (var collection = searcher.Get())
            {
                foreach (ManagementObject item in collection)
                {
                    try
                    {
                        var pid = Convert.ToInt32(item["ProcessId"] ?? 0);
                        if (pid == 0 || pid == currentPid)
                            continue;

                        var executable = (item["ExecutablePath"] as string) ?? string.Empty;
                        var commandLine = (item["CommandLine"] as string) ?? string.Empty;
                        var haystack = NormalizeNeedle(executable + "\n" + commandLine);
                        if (string.IsNullOrWhiteSpace(haystack))
                            continue;
                        if (!haystack.Contains(installNeedle) && !haystack.Contains(stateNeedle))
                            continue;

                        result.Add(new RunningProcessInfo
                        {
                            Id = pid,
                            Name = ((item["Name"] as string) ?? "process").Trim(),
                            ExecutablePath = executable,
                            CommandLine = commandLine,
                        });
                    }
                    catch
                    {
                    }
                }
            }

            return result.OrderBy(p => p.Name).ThenBy(p => p.Id).ToList();
        }

        private static bool TryStopProcesses(IEnumerable<RunningProcessInfo> processes, int timeoutSeconds)
        {
            foreach (var item in processes)
            {
                try
                {
                    var process = Process.GetProcessById(item.Id);
                    if (!process.HasExited)
                        process.Kill();
                }
                catch
                {
                }
            }

            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                var anyRunning = false;
                foreach (var item in processes)
                {
                    try
                    {
                        var process = Process.GetProcessById(item.Id);
                        if (!process.HasExited)
                        {
                            anyRunning = true;
                            break;
                        }
                    }
                    catch
                    {
                    }
                }

                if (!anyRunning)
                    return true;

                Thread.Sleep(500);
            }

            return false;
        }

        private static string NormalizeNeedle(string value)
        {
            return (value ?? string.Empty).Replace('/', '\\').ToLowerInvariant();
        }

        private void ScheduleCleanup(bool removeUserData)
        {
            var installDir = _manifest.install_dir ?? string.Empty;
            if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
                throw new InvalidOperationException("\u627e\u4e0d\u5230\u5b89\u88c5\u76ee\u5f55\u3002");

            var tempCmd = Path.Combine(Path.GetTempPath(), "AndrogenRagUninstall_" + Guid.NewGuid().ToString("N") + ".cmd");
            var builder = new StringBuilder();
            builder.AppendLine("@echo off");
            builder.AppendLine("ping 127.0.0.1 -n 3 > nul");
            if (removeUserData && !string.IsNullOrWhiteSpace(_manifest.state_dir))
                builder.AppendLine("rmdir /s /q \"" + _manifest.state_dir + "\"");
            builder.AppendLine("rmdir /s /q \"" + installDir + "\"");
            builder.AppendLine("del /f /q \"%~f0\"");
            File.WriteAllText(tempCmd, builder.ToString(), Encoding.ASCII);

            Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + tempCmd + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(tempCmd) ?? Path.GetTempPath(),
            });
        }
    }
}
