using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Text;
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

    internal sealed class LauncherConfig
    {
        public string app_id = "AndrogenRAG";
        public string display_name = "Androgen RAG";
        public string channel = "stable";
        public string bootstrap_version = "1.0.0";
        public string state_dir = "";
        public string manifest_url = "";
        public bool update_enabled = true;
        public bool open_browser = true;
        public int default_port = 8501;
        public int request_timeout_seconds = 10;
        public int update_retry_count = 3;
    }

    internal sealed class RunningProcessInfo
    {
        public int Id;
        public string Name = string.Empty;
        public string ExecutablePath = string.Empty;
        public string CommandLine = string.Empty;
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
        private readonly Button _browseButton;
        private readonly TextBox _dataPathBox;
        private readonly Button _dataBrowseButton;
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
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            ClientSize = new Size(820, 560);
            Font = SystemFonts.MessageBoxFont;

            var rootLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            Controls.Add(rootLayout);

            var headerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Margin = Padding.Empty,
            };
            rootLayout.Controls.Add(headerPanel, 0, 0);

            var bannerImage = TryLoadBannerImage();
            if (bannerImage != null)
            {
                var bannerBox = new PictureBox
                {
                    Image = bannerImage,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Size = new Size(210, 72),
                    Location = new Point(586, 14),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                };
                headerPanel.Controls.Add(bannerBox);
            }

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
                Size = new Size(520, 42),
                Location = new Point(24, 48),
                Text = string.Empty,
            };
            headerPanel.Controls.Add(_pageSubtitleLabel);

            var contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24, 24, 24, 18),
                Margin = Padding.Empty,
            };
            rootLayout.Controls.Add(contentPanel, 0, 1);

            var footerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18, 10, 18, 10),
                Margin = Padding.Empty,
            };
            rootLayout.Controls.Add(footerPanel, 0, 2);

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
                Location = new Point(514, 22),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            };
            _backButton.Click += BackButton_Click;
            footerPanel.Controls.Add(_backButton);

            _nextButton = new Button
            {
                Text = "下一步 >",
                Size = new Size(92, 28),
                Location = new Point(616, 22),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            };
            _nextButton.Click += NextButton_Click;
            footerPanel.Controls.Add(_nextButton);

            _cancelButton = new Button
            {
                Text = "取消",
                Size = new Size(92, 28),
                Location = new Point(718, 22),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
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
                Location = new Point(12, 34),
            };
            _componentsPage.Controls.Add(_installCoreBox);

            _desktopShortcutBox = new CheckBox
            {
                Text = "创建桌面快捷方式",
                Checked = true,
                AutoSize = true,
                Location = new Point(12, 76),
            };
            _componentsPage.Controls.Add(_desktopShortcutBox);

            _startMenuShortcutBox = new CheckBox
            {
                Text = "创建开始菜单项（含卸载入口）",
                Checked = true,
                AutoSize = true,
                Location = new Point(12, 116),
            };
            _componentsPage.Controls.Add(_startMenuShortcutBox);

            var componentsHint = new Label
            {
                Text = "安装完成后，可从桌面快捷方式、开始菜单或安装目录中的 launcher.exe 启动程序。",
                AutoSize = false,
                Size = new Size(680, 56),
                Location = new Point(12, 164),
            };
            _componentsPage.Controls.Add(componentsHint);

            _installPathBox = new TextBox
            {
                Location = new Point(10, 48),
                Size = new Size(560, 28),
                Text = ResolveDefaultInstallDir(),
            };
            _locationPage.Controls.Add(_installPathBox);

            _browseButton = new Button
            {
                Text = "浏览...",
                Location = new Point(584, 45),
                Size = new Size(100, 28),
            };
            _browseButton.Click += BrowseButton_Click;
            _locationPage.Controls.Add(_browseButton);

            var dataPathLabel = new Label
            {
                Text = "用户数据目录",
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
                Location = new Point(10, 100),
            };
            _locationPage.Controls.Add(dataPathLabel);

            _dataPathBox = new TextBox
            {
                Location = new Point(10, 130),
                Size = new Size(560, 28),
                Text = ResolveDefaultStateDir(),
            };
            _locationPage.Controls.Add(_dataPathBox);

            _dataBrowseButton = new Button
            {
                Text = "浏览...",
                Location = new Point(584, 127),
                Size = new Size(100, 28),
            };
            _dataBrowseButton.Click += DataBrowseButton_Click;
            _locationPage.Controls.Add(_dataBrowseButton);

            var locationHint = new Label
            {
                Text = "程序文件会安装到这里。用户输入输出、日志和缓存会存放在你选择的用户数据目录里，不会跟安装目录混在一起。",
                AutoSize = false,
                Size = new Size(680, 92),
                Location = new Point(10, 182),
            };
            _locationPage.Controls.Add(locationHint);

            _progressLabel = new Label
            {
                Text = "准备开始安装。",
                AutoSize = false,
                Size = new Size(680, 38),
                Location = new Point(10, 36),
            };
            _progressPage.Controls.Add(_progressLabel);

            _progressBar = new ProgressBar
            {
                Style = ProgressBarStyle.Continuous,
                Value = 0,
                Location = new Point(10, 86),
                Size = new Size(680, 22),
            };
            _progressPage.Controls.Add(_progressBar);

            _finishSummaryLabel = new Label
            {
                AutoSize = false,
                Size = new Size(680, 180),
                Location = new Point(10, 20),
                Text = string.Empty,
            };
            _finishPage.Controls.Add(_finishSummaryLabel);

            _launchAfterInstallBox = new CheckBox
            {
                Text = "完成后立即启动 " + payload.display_name,
                Checked = true,
                AutoSize = true,
                Location = new Point(10, 220),
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
                AutoScroll = true,
            };
        }

        private void BrowseButton_Click(object sender, EventArgs e)
        {
            ShowInstallFolderPicker();
        }

        private void DataBrowseButton_Click(object sender, EventArgs e)
        {
            ShowDataFolderPicker();
        }

        private void ShowInstallFolderPicker()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = _installPathBox.Text;
                dialog.Description = "选择安装目录";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _installPathBox.Text = dialog.SelectedPath;
            }
        }

        private void ShowDataFolderPicker()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = _dataPathBox.Text;
                dialog.Description = "选择用户数据目录";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _dataPathBox.Text = dialog.SelectedPath;
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
            string validationMessage;
            if (!TryValidateInstallDirectory(installDir, out validationMessage))
            {
                var choice = MessageBox.Show(
                    this,
                    validationMessage + "\r\n\r\n点击“是”立即选择其他安装目录，点击“否”返回当前页面后手动修改。",
                    "安装",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                _installPathBox.Focus();
                _installPathBox.SelectAll();
                if (choice == DialogResult.Yes)
                    ShowInstallFolderPicker();
                return;
            }

            var stateDir = (_dataPathBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(stateDir))
                stateDir = ResolveDefaultStateDir();
            stateDir = Path.GetFullPath(stateDir);
            if (!TryValidateStateDirectory(installDir, stateDir, out validationMessage))
            {
                var dataChoice = MessageBox.Show(
                    this,
                    validationMessage + "\r\n\r\n点击“是”立即选择其他用户数据目录，点击“否”返回当前页面后手动修改。",
                    "安装",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                _dataPathBox.Focus();
                _dataPathBox.SelectAll();
                if (dataChoice == DialogResult.Yes)
                    ShowDataFolderPicker();
                return;
            }

            if (!EnsureNoConflictingProcesses(installDir, stateDir))
                return;

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
                    UpdateLauncherConfig(installDir, stateDir);
                    SyncShortcuts(installDir);
                    var manifest = WriteInstallManifest(installDir, stateDir);
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
                        MessageBox.Show(this, BuildInstallErrorMessage(ex, installDir), "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        private string ResolveDefaultInstallDir()
        {
            if (!string.IsNullOrWhiteSpace(_payload.default_install_dir))
                return _payload.default_install_dir;

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                return Path.Combine(Path.GetTempPath(), _payload.app_id ?? "AndrogenRAG");

            return Path.Combine(localAppData, "Apps", _payload.app_id ?? "AndrogenRAG");
        }

        private string ResolveDefaultStateDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), _payload.app_id ?? "AndrogenRAG");
        }

        private static bool TryValidateInstallDirectory(string installDir, out string message)
        {
            try
            {
                if (File.Exists(installDir))
                {
                    message = "\u5f53\u524d\u5b89\u88c5\u8def\u5f84\u6307\u5411\u4e86\u4e00\u4e2a\u6587\u4ef6\uff0c\u8bf7\u6539\u6210\u6587\u4ef6\u5939\u8def\u5f84\u3002";
                    return false;
                }

                Directory.CreateDirectory(installDir);
                var probePath = Path.Combine(installDir, ".write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probePath, "ok", Encoding.ASCII);
                File.Delete(probePath);
                message = string.Empty;
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                message =
                    "\u65e0\u6cd5\u5199\u5165\u5f53\u524d\u5b89\u88c5\u76ee\u5f55\uff1a\r\n"
                    + installDir
                    + "\r\n\r\n\u8bf7\u70b9\u51fb\u201c\u6d4f\u89c8...\u201d\u6539\u6210\u4f60\u6709\u5199\u6743\u9650\u7684\u76ee\u5f55\uff0c\u4f8b\u5982 D:\\Apps\\AndrogenRAG \u3002\r\n\r\n"
                    + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                message =
                    "\u5f53\u524d\u5b89\u88c5\u76ee\u5f55\u4e0d\u53ef\u7528\uff1a\r\n"
                    + installDir
                    + "\r\n\r\n"
                    + ex.Message;
                return false;
            }
        }

        private static bool TryValidateStateDirectory(string installDir, string stateDir, out string message)
        {
            if (PathsOverlap(installDir, stateDir))
            {
                message = "用户数据目录不能与安装目录相同，也不能互相包含。\r\n\r\n请改成单独的目录，例如 D:\\AndrogenRAG-Data。";
                return false;
            }

            try
            {
                if (File.Exists(stateDir))
                {
                    message = "当前用户数据路径指向了一个文件，请改成文件夹路径。";
                    return false;
                }

                Directory.CreateDirectory(stateDir);
                var probePath = Path.Combine(stateDir, ".write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probePath, "ok", Encoding.ASCII);
                File.Delete(probePath);
                message = string.Empty;
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                message =
                    "无法写入当前用户数据目录：\r\n"
                    + stateDir
                    + "\r\n\r\n请选择你有写权限的目录，例如 D:\\AndrogenRAG-Data。\r\n\r\n"
                    + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                message =
                    "当前用户数据目录不可用：\r\n"
                    + stateDir
                    + "\r\n\r\n"
                    + ex.Message;
                return false;
            }
        }

        private static bool PathsOverlap(string left, string right)
        {
            var a = NormalizeFullPath(left);
            var b = NormalizeFullPath(right);
            return a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeFullPath(string path)
        {
            return Path.GetFullPath(path).TrimEnd('\\') + "\\";
        }

        private static string BuildInstallErrorMessage(Exception ex, string installDir)
        {
            if (ex is UnauthorizedAccessException)
            {
                return
                    "\u5b89\u88c5\u5931\u8d25\uff1a\u65e0\u6cd5\u5199\u5165\u5b89\u88c5\u76ee\u5f55\u3002\r\n\r\n"
                    + installDir
                    + "\r\n\r\n\u8bf7\u5c1d\u8bd5\u6362\u4e00\u4e2a\u81ea\u5b9a\u4e49\u8def\u5f84\uff0c\u6216\u4ee5\u7ba1\u7406\u5458\u8eab\u4efd\u8fd0\u884c\u5b89\u88c5\u7a0b\u5e8f\u3002\r\n\r\n"
                    + ex.Message;
            }

            return
                "\u5b89\u88c5\u5931\u8d25\uff1a\r\n\r\n"
                + ex.Message;
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

        private InstallManifest WriteInstallManifest(string installDir, string stateDir)
        {
            var manifest = new InstallManifest
            {
                app_id = _payload.app_id ?? "AndrogenRAG",
                display_name = _payload.display_name ?? "Androgen RAG",
                publisher = _payload.publisher ?? "MCxiaotao",
                version = _payload.bootstrap_version ?? "1.0.0",
                install_dir = installDir,
                state_dir = stateDir,
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

        private void UpdateLauncherConfig(string installDir, string stateDir)
        {
            var launcherJsonPath = Path.Combine(installDir, "launcher.json");
            var serializer = new JavaScriptSerializer();
            LauncherConfig config;
            if (File.Exists(launcherJsonPath))
                config = serializer.Deserialize<LauncherConfig>(File.ReadAllText(launcherJsonPath, Encoding.UTF8)) ?? new LauncherConfig();
            else
                config = new LauncherConfig();

            config.app_id = _payload.app_id ?? "AndrogenRAG";
            config.display_name = _payload.display_name ?? "Androgen RAG";
            config.bootstrap_version = _payload.bootstrap_version ?? "1.0.0";
            config.state_dir = stateDir;

            File.WriteAllText(launcherJsonPath, serializer.Serialize(config), new UTF8Encoding(false));
        }

        private bool EnsureNoConflictingProcesses(string installDir, string stateDir)
        {
            var related = FindRelatedProcesses(installDir, stateDir);
            if (related.Count == 0)
                return true;

            var details = string.Join("\r\n", related.Select(p => "- " + p.Name + " (PID " + p.Id + ")"));
            var answer = MessageBox.Show(
                this,
                "检测到程序仍在运行，安装覆盖前需要先关闭。\r\n\r\n"
                + details
                + "\r\n\r\n点击“是”自动结束这些进程并继续安装，点击“否”取消安装。",
                "安装",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
                return false;

            if (!TryStopProcesses(related, 10))
            {
                MessageBox.Show(this, "仍有相关进程未能关闭，请手动退出程序后再安装。", "安装", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        private static Image TryLoadBannerImage()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("branding_banner.bmp"))
            {
                if (stream == null)
                    return null;
                using (var image = Image.FromStream(stream))
                {
                    return new Bitmap(image);
                }
            }
        }
    }
}


