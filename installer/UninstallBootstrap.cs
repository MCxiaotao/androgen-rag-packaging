using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
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

            Text = (manifest.display_name ?? "Androgen RAG") + " 卸载";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(650, 300);
            Font = SystemFonts.MessageBoxFont;

            var title = new Label
            {
                Text = "卸载 " + (manifest.display_name ?? "Androgen RAG"),
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 15, FontStyle.Bold),
                Location = new Point(20, 18),
            };
            Controls.Add(title);

            var intro = new Label
            {
                AutoSize = false,
                Size = new Size(600, 96),
                Location = new Point(22, 56),
                Text = "卸载将移除安装目录中的启动器、版本包和快捷方式。\r\n\r\n"
                    + "安装目录: " + (_manifest.install_dir ?? string.Empty) + "\r\n"
                    + "用户数据目录: " + (_manifest.state_dir ?? string.Empty),
            };
            Controls.Add(intro);

            _removeUserDataBox = new CheckBox
            {
                Text = "同时删除用户数据目录（input / output / logs / cache）",
                Checked = false,
                AutoSize = true,
                Location = new Point(24, 166),
            };
            Controls.Add(_removeUserDataBox);

            _statusLabel = new Label
            {
                AutoSize = false,
                Size = new Size(420, 44),
                Location = new Point(22, 214),
                Text = string.Empty,
            };
            Controls.Add(_statusLabel);

            _uninstallButton = new Button
            {
                Text = "卸载",
                Size = new Size(92, 28),
                Location = new Point(446, 226),
            };
            _uninstallButton.Click += UninstallButton_Click;
            Controls.Add(_uninstallButton);

            _cancelButton = new Button
            {
                Text = "取消",
                Size = new Size(92, 28),
                Location = new Point(548, 226),
            };
            _cancelButton.Click += delegate { Close(); };
            Controls.Add(_cancelButton);
        }

        private void UninstallButton_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                this,
                "确认卸载 " + (_manifest.display_name ?? "Androgen RAG") + "？",
                "卸载",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
                return;

            try
            {
                _statusLabel.Text = "正在清理快捷方式并准备删除文件...";
                _uninstallButton.Enabled = false;
                _cancelButton.Enabled = false;

                RemoveDesktopShortcut();
                RemoveStartMenuShortcuts();
                UnregisterUninstallEntry();
                ScheduleCleanup(_removeUserDataBox.Checked);

                MessageBox.Show(this, "卸载任务已启动。关闭本窗口后，安装目录会自动删除。", "卸载", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "卸载失败。";
                _uninstallButton.Enabled = true;
                _cancelButton.Enabled = true;
                MessageBox.Show(this, ex.ToString(), "卸载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        private void ScheduleCleanup(bool removeUserData)
        {
            var installDir = _manifest.install_dir ?? string.Empty;
            if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
                throw new InvalidOperationException("找不到安装目录。");

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
