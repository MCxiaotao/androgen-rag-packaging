using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AndrogenRagSetup
{
    internal sealed class SetupPayload
    {
        public string app_id = "AndrogenRAG";
        public string display_name = "Androgen RAG";
        public string bootstrap_version = "1.0.0";
        public string shortcut_name = "Androgen RAG";
        public string default_install_dir = "";
        public string payload_archive = "payload.zip";
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm(LoadPayload()));
        }
        private static SetupPayload LoadPayload()
        {
            var serializer = new JavaScriptSerializer();
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("setup_payload.json"))
            {
                if (stream == null)
                    return new SetupPayload();
                using (var reader = new StreamReader(stream))
                {
                    var text = reader.ReadToEnd();
                    return serializer.Deserialize<SetupPayload>(text) ?? new SetupPayload();
                }
            }
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly SetupPayload _payload;
        private readonly TextBox _installPathBox;
        private readonly CheckBox _desktopShortcutBox;
        private readonly CheckBox _launchAfterInstallBox;
        private readonly Button _installButton;
        private readonly Label _statusLabel;

        public SetupForm(SetupPayload payload)
        {
            _payload = payload;
            Text = payload.display_name + " Setup";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(620, 240);
            var title = new Label
            {
                Text = payload.display_name + " 安装程序",
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 14, FontStyle.Bold),
                Location = new Point(20, 20),
            };
            Controls.Add(title);

            var version = new Label
            {
                Text = "版本: " + payload.bootstrap_version,
                AutoSize = true,
                Location = new Point(22, 58),
            };
            Controls.Add(version);

            var pathLabel = new Label
            {
                Text = "安装目录",
                AutoSize = true,
                Location = new Point(22, 95),
            };
            Controls.Add(pathLabel);

            _installPathBox = new TextBox
            {
                Location = new Point(22, 118),
                Size = new Size(470, 24),
                Text = string.IsNullOrWhiteSpace(payload.default_install_dir)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", payload.app_id)
                    : payload.default_install_dir,
            };
            Controls.Add(_installPathBox);
            var browseButton = new Button
            {
                Text = "浏览...",
                Location = new Point(505, 116),
                Size = new Size(90, 28),
            };
            browseButton.Click += BrowseButton_Click;
            Controls.Add(browseButton);

            _desktopShortcutBox = new CheckBox
            {
                Text = "创建桌面快捷方式",
                Checked = true,
                AutoSize = true,
                Location = new Point(24, 156),
            };
            Controls.Add(_desktopShortcutBox);

            _launchAfterInstallBox = new CheckBox
            {
                Text = "安装完成后立即启动",
                Checked = true,
                AutoSize = true,
                Location = new Point(190, 156),
            };
            Controls.Add(_launchAfterInstallBox);

            _installButton = new Button
            {
                Text = "安装",
                Location = new Point(505, 192),
                Size = new Size(90, 30),
            };
            _installButton.Click += InstallButton_Click;
            Controls.Add(_installButton);
            _statusLabel = new Label
            {
                Text = "",
                AutoSize = false,
                Size = new Size(470, 36),
                Location = new Point(22, 188),
            };
            Controls.Add(_statusLabel);
        }

        private void BrowseButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = _installPathBox.Text;
                dialog.Description = "选择安装目录";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _installPathBox.Text = dialog.SelectedPath;
            }
        }

        private void InstallButton_Click(object sender, EventArgs e)
        {
            var installDir = (_installPathBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(installDir))
            {
                MessageBox.Show(this, "请选择安装目录。", "安装", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                ToggleBusy(true, "正在安装，请稍候...");
                InstallTo(installDir);
                if (_desktopShortcutBox.Checked)
                    CreateDesktopShortcut(installDir);
                if (_launchAfterInstallBox.Checked)
                    LaunchInstalledApp(installDir);
                ToggleBusy(false, "安装完成。");
                MessageBox.Show(this, "安装完成。", "安装", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                ToggleBusy(false, "安装失败。请查看错误信息。");
                MessageBox.Show(this, ex.ToString(), "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ToggleBusy(bool busy, string status)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _installButton.Enabled = !busy;
            _statusLabel.Text = status;
            _statusLabel.Refresh();
        }

        private void InstallTo(string installDir)
        {
            Directory.CreateDirectory(installDir);
            var tempRoot = Path.Combine(Path.GetTempPath(), "AndrogenRagSetup", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var zipPath = Path.Combine(tempRoot, _payload.payload_archive ?? "payload.zip");
                ExtractEmbeddedResource("payload.zip", zipPath);
                ExtractZipTo(zipPath, installDir);
            }
            finally
            {
                try
                {
                    Directory.Delete(tempRoot, true);
                }
                catch
                {
                }
            }
        }

        private static void ExtractEmbeddedResource(string resourceName, string targetPath)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException("Missing embedded resource: " + resourceName);
                using (var file = File.Create(targetPath))
                    stream.CopyTo(file);
            }
        }

        private static void ExtractZipTo(string zipPath, string destination)
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    var fullPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                    var destRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
                    if (!fullPath.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase) && !string.Equals(fullPath, Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Archive contains invalid path: " + entry.FullName);

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(fullPath);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                    entry.ExtractToFile(fullPath, true);
                }
            }
        }

        private void CreateDesktopShortcut(string installDir)
        {
            var launcher = Path.Combine(installDir, "launcher.exe");
            if (!File.Exists(launcher))
                return;

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop))
                return;
            var shortcutPath = Path.Combine(desktop, (_payload.shortcut_name ?? _payload.display_name ?? "Androgen RAG") + ".lnk");
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                return;
            var shell = Activator.CreateInstance(shellType);
            var shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            var shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { launcher });
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { installDir });
            shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { launcher + ",0" });
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }

        private static void LaunchInstalledApp(string installDir)
        {
            var launcher = Path.Combine(installDir, "launcher.exe");
            if (!File.Exists(launcher))
                return;
            Process.Start(new ProcessStartInfo(launcher)
            {
                WorkingDirectory = installDir,
                UseShellExecute = true,
            });
        }
    }
}
