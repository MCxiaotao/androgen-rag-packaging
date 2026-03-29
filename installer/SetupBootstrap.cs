using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AndrogenRagSetup
{
    internal sealed class SetupPayload
    {
        public string app_id = "AndrogenRAG";
        public string display_name = "Androgen RAG";
        public string publisher = "MCxiaotao";
        public string bootstrap_version = "1.0.0";
        public string shortcut_name = "Androgen RAG";
        public string default_install_dir = "";
        public string payload_archive = "payload.zip";
    }

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

    internal enum WizardPage
    {
        Welcome = 0,
        Components = 1,
        Location = 2,
        Progress = 3,
        Finish = 4,
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
        private readonly Label _pageTitleLabel;
        private readonly Label _pageSubtitleLabel;
        private readonly Panel _welcomePage;
        private readonly Panel _componentsPage;
        private readonly Panel _locationPage;
        private readonly Panel _progressPage;
        private readonly Panel _finishPage;
        private readonly CheckBox _installCoreBox;
        private readonly CheckBox _desktopShortcutBox;
        private readonly CheckBox _startMenuShortcutBox;
        private readonly TextBox _installPathBox;
        private readonly Label _progressLabel;
        private readonly ProgressBar _progressBar;
        private readonly Label _finishSummaryLabel;
        private readonly CheckBox _launchAfterInstallBox;
        private readonly Button _backButton;
        private readonly Button _nextButton;
        private readonly Button _cancelButton;

        private WizardPage _currentPage;
        private bool _installInProgress;
        private string _installedPath = string.Empty;

        public SetupForm(SetupPayload payload)
        {
            _payload = payload;

            Text = payload.display_name + " Setup";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(720, 470);
            Font = SystemFonts.MessageBoxFont;

            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 90,
                BackColor = Color.White,
            };
            Controls.Add(headerPanel);

            _pageTitleLabel = new Label
            {
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 16, FontStyle.Bold),
                Location = new Point(22, 18),
                Text = payload.display_name,
            };
            headerPanel.Controls.Add(_pageTitleLabel);

            _pageSubtitleLabel = new Label
            {
                AutoSize = false,
                Size = new Size(650, 40),
                Location = new Point(24, 48),
                Text = string.Empty,
            };
            headerPanel.Controls.Add(_pageSubtitleLabel);

            var contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 18, 20, 16),
            };
            Controls.Add(contentPanel);

            var footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                Padding = new Padding(18, 10, 18, 10),
            };
            Controls.Add(footerPanel);

            var footerLine = new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = Color.Gainsboro,
            };
            footerPanel.Controls.Add(footerLine);

            _backButton = new Button
            {
                Text = "< 上一步",
                Size = new Size(92, 28),
                Location = new Point(412, 18),
            };
            _backButton.Click += BackButton_Click;
            footerPanel.Controls.Add(_backButton);

            _nextButton = new Button
            {
                Text = "下一步 >",
                Size = new Size(92, 28),
                Location = new Point(512, 18),
            };
            _nextButton.Click += NextButton_Click;
            footerPanel.Controls.Add(_nextButton);

            _cancelButton = new Button
            {
                Text = "取消",
                Size = new Size(92, 28),
                Location = new Point(612, 18),
            };
            _cancelButton.Click += CancelButton_Click;
            footerPanel.Controls.Add(_cancelButton);

            _welcomePage = CreateWelcomePage();
            _componentsPage = CreateComponentsPage();
            _locationPage = CreateLocationPage();
            _progressPage = CreateProgressPage();
            _finishPage = CreateFinishPage();

            contentPanel.Controls.Add(_welcomePage);
            contentPanel.Controls.Add(_componentsPage);
            contentPanel.Controls.Add(_locationPage);
            contentPanel.Controls.Add(_progressPage);
            contentPanel.Controls.Add(_finishPage);

            _installCoreBox = new CheckBox
            {
                Text = "主程序文件（必选）",
                Checked = true,
                Enabled = false,
                AutoSize = true,
                Location = new Point(10, 18),
            };
            _componentsPage.Controls.Add(_installCoreBox);

            _desktopShortcutBox = new CheckBox
            {
                Text = "创建桌面快捷方式",
                Checked = true,
                AutoSize = true,
                Location = new Point(10, 58),
            };
            _componentsPage.Controls.Add(_desktopShortcutBox);

            _startMenuShortcutBox = new CheckBox
            {
                Text = "创建开始菜单项（含卸载入口）",
                Checked = true,
                AutoSize = true,
                Location = new Point(10, 90),
            };
            _componentsPage.Controls.Add(_startMenuShortcutBox);

            var componentsHint = new Label
            {
                Text = "安装完成后，可从桌面快捷方式、开始菜单或安装目录中的 launcher.exe 启动程序。",
                AutoSize = false,
                Size = new Size(620, 48),
                Location = new Point(10, 132),
            };
            _componentsPage.Controls.Add(componentsHint);

            _installPathBox = new TextBox
            {
                Location = new Point(10, 48),
                Size = new Size(520, 24),
                Text = string.IsNullOrWhiteSpace(payload.default_install_dir)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", payload.app_id)
                    : payload.default_install_dir,
            };
            _locationPage.Controls.Add(_installPathBox);

            var browseButton = new Button
            {
                Text = "浏览...",
                Location = new Point(544, 45),
                Size = new Size(92, 28),
            };
            browseButton.Click += BrowseButton_Click;
            _locationPage.Controls.Add(browseButton);

            var locationHint = new Label
            {
                Text = "程序文件会安装到这里。用户输入输出、日志和缓存默认存放在 %LOCALAPPDATA%\\" + payload.app_id + "，不会跟安装目录混在一起。",
                AutoSize = false,
                Size = new Size(620, 72),
                Location = new Point(10, 94),
            };
            _locationPage.Controls.Add(locationHint);

            _progressLabel = new Label
            {
                Text = "准备开始安装。",
                AutoSize = false,
                Size = new Size(620, 38),
                Location = new Point(10, 36),
            };
            _progressPage.Controls.Add(_progressLabel);

            _progressBar = new ProgressBar
            {
                Style = ProgressBarStyle.Continuous,
                Value = 0,
                Location = new Point(10, 86),
                Size = new Size(626, 22),
            };
            _progressPage.Controls.Add(_progressBar);

            _finishSummaryLabel = new Label
            {
                AutoSize = false,
                Size = new Size(620, 150),
                Location = new Point(10, 20),
                Text = string.Empty,
            };
            _finishPage.Controls.Add(_finishSummaryLabel);

            _launchAfterInstallBox = new CheckBox
            {
                Text = "完成后立即启动 " + payload.display_name,
                Checked = true,
                AutoSize = true,
                Location = new Point(10, 186),
            };
            _finishPage.Controls.Add(_launchAfterInstallBox);

            ShowPage(WizardPage.Welcome);
        }

        private Panel CreateWelcomePage()
        {
            var panel = CreatePagePanel();
            var intro = new Label
            {
                AutoSize = false,
                Size = new Size(630, 160),
                Location = new Point(10, 18),
                Text = "欢迎使用 " + _payload.display_name + " 安装向导。\r\n\r\n"
                    + "这个安装程序会将启动器、版本化运行包和基础配置写入你选择的安装目录。\r\n"
                    + "首次启动后，程序会通过 launcher.exe 检查运行环境和更新，再拉起本地 Streamlit 服务。\r\n\r\n"
                    + "建议关闭正在运行的旧版本程序后再继续安装。",
            };
            panel.Controls.Add(intro);
            return panel;
        }

        private Panel CreateComponentsPage()
        {
            return CreatePagePanel();
        }

        private Panel CreateLocationPage()
        {
            var panel = CreatePagePanel();
            var title = new Label
            {
                Text = "选择安装目录",
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
                Location = new Point(10, 18),
            };
            panel.Controls.Add(title);
            return panel;
        }

        private Panel CreateProgressPage()
        {
            return CreatePagePanel();
        }

        private Panel CreateFinishPage()
        {
            return CreatePagePanel();
        }

        private Panel CreatePagePanel()
        {
            return new Panel
            {
                Dock = DockStyle.Fill,
                Visible = false,
            };
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

        private void BackButton_Click(object sender, EventArgs e)
        {
            if (_installInProgress)
                return;

            if (_currentPage == WizardPage.Components)
                ShowPage(WizardPage.Welcome);
            else if (_currentPage == WizardPage.Location)
                ShowPage(WizardPage.Components);
        }

        private void NextButton_Click(object sender, EventArgs e)
        {
            if (_currentPage == WizardPage.Welcome)
            {
                ShowPage(WizardPage.Components);
                return;
            }

            if (_currentPage == WizardPage.Components)
            {
                ShowPage(WizardPage.Location);
                return;
            }

            if (_currentPage == WizardPage.Location)
            {
                BeginInstall();
                return;
            }

            if (_currentPage == WizardPage.Finish)
            {
                if (_launchAfterInstallBox.Checked)
                    LaunchInstalledApp(_installedPath);
                Close();
            }
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            if (_installInProgress)
                return;
            Close();
        }

        private void BeginInstall()
        {
            var installDir = (_installPathBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(installDir))
            {
                MessageBox.Show(this, "请选择安装目录。", "安装", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            installDir = Path.GetFullPath(installDir);
            if (Directory.Exists(installDir))
            {
                try
                {
                    if (Directory.EnumerateFileSystemEntries(installDir).Any())
                    {
                        var overwrite = MessageBox.Show(
                            this,
                            "安装目录已经存在内容。继续安装会覆盖同名文件，是否继续？",
                            "安装",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question);
                        if (overwrite != DialogResult.Yes)
                            return;
                    }
                }
                catch
                {
                }
            }

            _installedPath = installDir;
            _installInProgress = true;
            ShowPage(WizardPage.Progress);
            SetProgressState("正在解压安装包并写入文件...", ProgressBarStyle.Marquee, 0);
            _backButton.Enabled = false;
            _nextButton.Enabled = false;
            _cancelButton.Enabled = false;

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    InstallTo(installDir);
                    SyncShortcuts(installDir);
                    var manifest = WriteInstallManifest(installDir);
                    RegisterUninstallEntry(manifest);
                    BeginInvoke((Action)delegate
                    {
                        _installInProgress = false;
                        SetProgressState("安装完成。", ProgressBarStyle.Continuous, 100);
                        PrepareFinishPage(manifest);
                        ShowPage(WizardPage.Finish);
                    });
                }
                catch (Exception ex)
                {
                    BeginInvoke((Action)delegate
                    {
                        _installInProgress = false;
                        SetProgressState("安装失败。", ProgressBarStyle.Continuous, 0);
                        MessageBox.Show(this, ex.ToString(), "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        ShowPage(WizardPage.Location);
                    });
                }
            });
        }

        private void SetProgressState(string status, ProgressBarStyle style, int value)
        {
            _progressLabel.Text = status;
            _progressBar.Style = style;
            if (style == ProgressBarStyle.Continuous)
                _progressBar.Value = Math.Max(_progressBar.Minimum, Math.Min(_progressBar.Maximum, value));
        }

        private void PrepareFinishPage(InstallManifest manifest)
        {
            _finishSummaryLabel.Text =
                "安装已完成。\r\n\r\n"
                + "安装目录: " + manifest.install_dir + "\r\n"
                + "用户数据目录: " + manifest.state_dir + "\r\n"
                + "启动程序: " + Path.Combine(manifest.install_dir, manifest.launcher_exe) + "\r\n"
                + "卸载程序: " + Path.Combine(manifest.install_dir, manifest.uninstall_exe) + "\r\n\r\n"
                + "如果勾选了开始菜单项，你可以在开始菜单里直接找到启动和卸载入口。";
        }

        private void ShowPage(WizardPage page)
        {
            _currentPage = page;
            _welcomePage.Visible = page == WizardPage.Welcome;
            _componentsPage.Visible = page == WizardPage.Components;
            _locationPage.Visible = page == WizardPage.Location;
            _progressPage.Visible = page == WizardPage.Progress;
            _finishPage.Visible = page == WizardPage.Finish;

            switch (page)
            {
                case WizardPage.Welcome:
                    SetHeader("欢迎安装 " + _payload.display_name, "准备开始安装。点击“下一步”继续。");
                    _backButton.Enabled = false;
                    _nextButton.Enabled = true;
                    _nextButton.Text = "下一步 >";
                    _cancelButton.Enabled = true;
                    _cancelButton.Text = "取消";
                    break;
                case WizardPage.Components:
                    SetHeader("选择组件", "选择需要创建的快捷方式和开始菜单入口。");
                    _backButton.Enabled = true;
                    _nextButton.Enabled = true;
                    _nextButton.Text = "下一步 >";
                    _cancelButton.Enabled = true;
                    _cancelButton.Text = "取消";
                    break;
                case WizardPage.Location:
                    SetHeader("选择安装位置", "你可以保留默认安装目录，也可以改成自定义路径。");
                    _backButton.Enabled = true;
                    _nextButton.Enabled = true;
                    _nextButton.Text = "安装";
                    _cancelButton.Enabled = true;
                    _cancelButton.Text = "取消";
                    break;
                case WizardPage.Progress:
                    SetHeader("正在安装", "安装过程中请不要关闭窗口。");
                    _backButton.Enabled = false;
                    _nextButton.Enabled = false;
                    _nextButton.Text = "安装";
                    _cancelButton.Enabled = false;
                    _cancelButton.Text = "取消";
                    break;
                case WizardPage.Finish:
                    SetHeader("安装完成", "程序已经安装完成，你现在可以直接启动。");
                    _backButton.Enabled = false;
                    _nextButton.Enabled = true;
                    _nextButton.Text = "完成";
                    _cancelButton.Enabled = false;
                    _cancelButton.Text = "关闭";
                    break;
            }
        }

        private void SetHeader(string title, string subtitle)
        {
            _pageTitleLabel.Text = title;
            _pageSubtitleLabel.Text = subtitle;
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
                    if (!fullPath.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(fullPath, Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Archive contains invalid path: " + entry.FullName);
                    }

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(fullPath);
                        continue;
                    }

                    var parent = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(parent))
                        Directory.CreateDirectory(parent);
                    entry.ExtractToFile(fullPath, true);
                }
            }
        }

        private void SyncShortcuts(string installDir)
        {
            RemoveDesktopShortcut();
            RemoveStartMenuShortcuts();

            if (_desktopShortcutBox.Checked)
                CreateDesktopShortcut(installDir);
            if (_startMenuShortcutBox.Checked)
                CreateStartMenuShortcuts(installDir);
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
            CreateShortcut(shortcutPath, launcher, installDir, launcher + ",0");
        }

        private void RemoveDesktopShortcut()
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop))
                return;
            var shortcutPath = Path.Combine(desktop, (_payload.shortcut_name ?? _payload.display_name ?? "Androgen RAG") + ".lnk");
            if (File.Exists(shortcutPath))
                File.Delete(shortcutPath);
        }

        private void CreateStartMenuShortcuts(string installDir)
        {
            var launcher = Path.Combine(installDir, "launcher.exe");
            var uninstall = Path.Combine(installDir, "uninstall.exe");
            var programsDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (string.IsNullOrWhiteSpace(programsDir) || !File.Exists(launcher))
                return;

            var groupDir = Path.Combine(programsDir, _payload.shortcut_name ?? _payload.display_name ?? "Androgen RAG");
            Directory.CreateDirectory(groupDir);
            CreateShortcut(Path.Combine(groupDir, (_payload.shortcut_name ?? _payload.display_name ?? "Androgen RAG") + ".lnk"), launcher, installDir, launcher + ",0");
            if (File.Exists(uninstall))
            {
                CreateShortcut(Path.Combine(groupDir, "卸载 " + (_payload.shortcut_name ?? _payload.display_name ?? "Androgen RAG") + ".lnk"), uninstall, installDir, uninstall + ",0");
            }
        }

        private void RemoveStartMenuShortcuts()
        {
            var programsDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (string.IsNullOrWhiteSpace(programsDir))
                return;
            var groupDir = Path.Combine(programsDir, _payload.shortcut_name ?? _payload.display_name ?? "Androgen RAG");
            if (Directory.Exists(groupDir))
                Directory.Delete(groupDir, true);
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string iconLocation)
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                return;
            var shell = Activator.CreateInstance(shellType);
            var shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            var shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
            shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { iconLocation });
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }

        private InstallManifest WriteInstallManifest(string installDir)
        {
            var manifest = new InstallManifest
            {
                app_id = _payload.app_id ?? "AndrogenRAG",
                display_name = _payload.display_name ?? "Androgen RAG",
                publisher = _payload.publisher ?? "MCxiaotao",
                version = _payload.bootstrap_version ?? "1.0.0",
                install_dir = installDir,
                state_dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), _payload.app_id ?? "AndrogenRAG"),
                launcher_exe = "launcher.exe",
                uninstall_exe = "uninstall.exe",
                shortcut_name = _payload.shortcut_name ?? _payload.display_name ?? "Androgen RAG",
            };

            var serializer = new JavaScriptSerializer();
            var manifestPath = Path.Combine(installDir, "install_manifest.json");
            File.WriteAllText(manifestPath, serializer.Serialize(manifest));
            return manifest;
        }

        private static void RegisterUninstallEntry(InstallManifest manifest)
        {
            var uninstallExe = Path.Combine(manifest.install_dir, manifest.uninstall_exe);
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + manifest.app_id))
            {
                if (key == null)
                    return;

                key.SetValue("DisplayName", manifest.display_name);
                key.SetValue("DisplayVersion", manifest.version);
                key.SetValue("Publisher", manifest.publisher);
                key.SetValue("InstallLocation", manifest.install_dir);
                key.SetValue("DisplayIcon", Path.Combine(manifest.install_dir, manifest.launcher_exe));
                key.SetValue("UninstallString", "\"" + uninstallExe + "\"");
                key.SetValue("QuietUninstallString", "\"" + uninstallExe + "\"");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            }
        }

        private static void LaunchInstalledApp(string installDir)
        {
            if (string.IsNullOrWhiteSpace(installDir))
                return;
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
