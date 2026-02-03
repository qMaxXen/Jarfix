// Jarfix/UI/MainForm.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Jarfix.Core;
using Jarfix.Utils;

namespace Jarfix.UI
{
    public class MainForm : Form
    {
        private enum LogLevel
        {
            INFO,
            WARNING,
            ERROR
        }
        private TabControl mainTabs = null!;
        private TabPage tabJarfix = null!;
        private TabPage tabDetected = null!;
        private TabPage tabLog = null!;

        private Button btnRefresh = null!;
        private RichTextBox jarfixInfoBox = null!; 
        private ListView lvDetected = null!;
        private TextBox txtLog = null!;
        private Button btnUploadLog = null!;
        private List<JavaRuntime> lastDetected = new List<JavaRuntime>();
        private CancellationTokenSource? downloadCts;
        private StringBuilder runLog = new StringBuilder();
        private const string MicrosoftJdk21Msi = "https://aka.ms/download-jdk/microsoft-jdk-21-windows-x64.msi";
        private const string CurrentVersion = "v1.0.4";
        private const string LatestReleaseUrl = "https://github.com/qMaxXen/Jarfix/releases/latest";
        public MainForm()
        {
            InitializeComponent();
            Load += MainForm_Load;
            FormClosing += (s, e) => downloadCts?.Cancel();
        }

        private void InitializeComponent()
        {
            Text = "Jarfix";
            Width = 680;
            Height = 420;

            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("Jarfix.Assets.Jarfix.ico");
                if (stream != null)
                {
                    this.Icon = new System.Drawing.Icon(stream);
                }
            }
            catch
            {
            }

            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            mainTabs = new TabControl { Dock = DockStyle.Fill };

            tabJarfix = new TabPage("Jarfix");

            jarfixInfoBox = new RichTextBox
            {
                Left = 12,
                Top = 12,
                Width = 640,
                Height = 300,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                WordWrap = true,
                BackColor = System.Drawing.Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(8),
                Font = new System.Drawing.Font("Segoe UI", 9)
            };

            jarfixInfoBox.GotFocus += (s, e) => this.ActiveControl = null;
            jarfixInfoBox.SelectionChanged += (s, e) => jarfixInfoBox.DeselectAll();
            jarfixInfoBox.MouseEnter += (s, e) => jarfixInfoBox.Cursor = Cursors.Default;
            jarfixInfoBox.MouseLeave += (s, e) => jarfixInfoBox.Cursor = Cursors.Default;

            tabJarfix.Controls.Add(jarfixInfoBox);

            btnRefresh = new Button
            {
                Left = 12,
                Top = jarfixInfoBox.Bottom + 8, 
                Width = 120,
                Text = "Refresh"
            };
            btnRefresh.Click += async (s, e) => await RunJarfixFlow();
            tabJarfix.Controls.Add(btnRefresh);

            tabDetected = new TabPage("Installed Java Runtimes");
            lvDetected = new ListView { Left = 8, Top = 8, Width = 652, Height = 340, View = View.Details, FullRowSelect = true };
            lvDetected.Columns.Add("Vendor", -2);
            lvDetected.Columns.Add("Version", -2);
            lvDetected.Columns.Add("Architecture", -2);
            lvDetected.Columns.Add("Path", -2);
            tabDetected.Controls.Add(lvDetected);

            tabLog = new TabPage("Log");
            txtLog = new TextBox { Left = 8, Top = 8, Width = 652, Height = 300, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
            btnUploadLog = new Button { Left = 12, Top = txtLog.Bottom + 13, Width = 120, Text = "Upload log" };
            btnUploadLog.Click += async (s, e) => await UploadLogClicked();
            tabLog.Controls.Add(txtLog);
            tabLog.Controls.Add(btnUploadLog);

            mainTabs.TabPages.Add(tabJarfix);
            mainTabs.TabPages.Add(tabDetected);
            mainTabs.TabPages.Add(tabLog);

            Controls.Add(mainTabs);
        }

        private async void MainForm_Load(object? sender, EventArgs e)
        {
            mainTabs.SelectedTab = tabJarfix;
                        
            var (isOutdated, latestVersion) = await CheckForUpdatesAsync();

            if (!string.IsNullOrEmpty(latestVersion))
            {
                if (isOutdated)
                {
                    LogForLog($"Jarfix version: {CurrentVersion} (Update available: {latestVersion})", LogLevel.WARNING);
                }
                else
                {
                    LogForLog($"Jarfix version: {CurrentVersion} (latest version)");
                }
            }
            else
            {
                LogForLog($"Jarfix version: {CurrentVersion}");
                LogForLog($"Could not check for updates", LogLevel.WARNING);
            }
            
            if (isOutdated)
            {
                var result = MessageBox.Show(
                    "You are running an outdated version of Jarfix. Would you like to open the latest release on GitHub?",
                    "Update Available",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button1
                );
                
                if (result == DialogResult.Yes)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = LatestReleaseUrl,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        LogForLog($"Failed to open update URL: {ex.Message}", LogLevel.ERROR);
                    }
                    Application.Exit();
                    return;
                }
            }
            
            await RunJarfixFlow();
        }

        private async Task<(bool isOutdated, string latestVersion)> CheckForUpdatesAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "Jarfix");
                client.Timeout = TimeSpan.FromSeconds(5);
                
                var response = await client.GetAsync("https://api.github.com/repos/qMaxXen/Jarfix/releases/latest");
                if (!response.IsSuccessStatusCode) return (false, "");
                
                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                
                if (doc.RootElement.TryGetProperty("tag_name", out var tagProp))
                {
                    var latestVersion = tagProp.GetString() ?? "";
                    var isOutdated = CompareVersions(CurrentVersion, latestVersion) < 0;
                    return (isOutdated, latestVersion);
                }
            }
            catch
            {
            }
            return (false, "");
        }

        private int CompareVersions(string current, string latest)
        {
            var currentClean = current.TrimStart('v', 'V');
            var latestClean = latest.TrimStart('v', 'V');
            
            var currentParts = currentClean.Split('.');
            var latestParts = latestClean.Split('.');
            
            int maxLength = Math.Max(currentParts.Length, latestParts.Length);
            
            for (int i = 0; i < maxLength; i++)
            {
                int currentNum = 0;
                int latestNum = 0;
                
                if (i < currentParts.Length)
                    int.TryParse(currentParts[i], out currentNum);
                
                if (i < latestParts.Length)
                    int.TryParse(latestParts[i], out latestNum);
                
                if (currentNum < latestNum)
                    return -1;
                else if (currentNum > latestNum)
                    return 1; 
            }
            
            return 0;
        }
        private void InfoTitle(string title)
        {
            if (jarfixInfoBox.InvokeRequired)
            {
                jarfixInfoBox.BeginInvoke((Action)(() => InfoTitle(title)));
                return;
            }
            jarfixInfoBox.SelectionFont = new System.Drawing.Font(jarfixInfoBox.Font.FontFamily, 14, System.Drawing.FontStyle.Bold);
            jarfixInfoBox.AppendText(title + Environment.NewLine);
            jarfixInfoBox.SelectionFont = new System.Drawing.Font(jarfixInfoBox.Font.FontFamily, 12, System.Drawing.FontStyle.Regular);
        }

        private void Info(string message)
        {
            if (jarfixInfoBox.InvokeRequired)
            {
                jarfixInfoBox.BeginInvoke((Action)(() => Info(message)));
                return;
            }
            jarfixInfoBox.AppendText(message + Environment.NewLine);
        }
        private void ResizeJarfixInfoBoxToContent()
        {
            int charIndex = jarfixInfoBox.TextLength;
            if (charIndex == 0) return;

            var lastCharPos = jarfixInfoBox.GetPositionFromCharIndex(charIndex - 1);
            int neededHeight = lastCharPos.Y + jarfixInfoBox.Font.Height + 16;

            jarfixInfoBox.Height = Math.Min(neededHeight, 280);
        }

        private void InfoBlankLine()
        {
            if (jarfixInfoBox.InvokeRequired)
            {
                jarfixInfoBox.BeginInvoke((Action)InfoBlankLine);
                return;
            }
            jarfixInfoBox.AppendText(Environment.NewLine);
        }

        private void LogForLog(string message, LogLevel level = LogLevel.INFO)
        {
            string levelName = level switch
            {
                LogLevel.WARNING => "WARNING",
                LogLevel.ERROR => "ERROR",
                _ => "INFO"
            };
            
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            
            var uploadLine = $"[{timestamp}] [main/{levelName}]: {message}";
            runLog.AppendLine(uploadLine);
            
            var displayLine = $"[{timestamp}/{levelName}] {message}";
            
            if (txtLog.InvokeRequired)
                txtLog.BeginInvoke((Action)(() => txtLog.AppendText(displayLine + Environment.NewLine)));
            else
                txtLog.AppendText(displayLine + Environment.NewLine);
        }

        private async Task RunJarfixFlow()
        {
            try
            {
                SetBusy(true);
                jarfixInfoBox.Clear();

                Info("Looking for installed Java runtimes on your computer...");
                LogForLog("Starting detection of installed Java runtimes...");
                
                await DoDetect();

                LogForLog($"Found {lastDetected.Count} runtime(s).");

                if (lastDetected.Count > 0)
                {
                    foreach (var r in lastDetected)
                    {
                        LogForLog($"Detected: {r.JavawPath}");
                    }
                }

                jarfixInfoBox.Clear();

                if (lastDetected.Count == 0)
                {
                    InfoWarning("No Java runtime found.");
                    InfoBlankLine();
                }

                bool onlyOldJava = false;
                if (lastDetected.Count > 0)
                {
                    var maxMajor = 0;
                    foreach (var r in lastDetected) maxMajor = Math.Max(maxMajor, r.MajorVersion);
                    if (maxMajor <= 8) onlyOldJava = true;
                }

                if (onlyOldJava)
                {
                    InfoWarning("You're using Java 8 or older, which may cause problems.");
                    InfoWarning("You should use Java 17 or higher.");
                    InfoBlankLine();

                    LogForLog("Only Java 8 or older detected.");
                }

                bool has32BitModernJava = false;
                bool has64BitModernJava = false;
                if (lastDetected.Count > 0)
                {
                    has32BitModernJava = lastDetected.Any(r => r.MajorVersion >= 17 && !r.Is64Bit);
                    has64BitModernJava = lastDetected.Any(r => r.MajorVersion >= 17 && r.Is64Bit);
                }

                if (has32BitModernJava && !has64BitModernJava)
                {
                    InfoWarning("You have Java 17+ installed, but it's 32-bit.");
                    InfoWarning("32-bit Java is not recommended. You should use 64-bit Java 17 or higher.");
                    InfoBlankLine();

                    LogForLog("Java 17+ detected but only in 32-bit version.");
                }

                bool only32BitJava = false;
                bool has64BitJava = false;
                if (lastDetected.Count > 0)
                {
                    has64BitJava = lastDetected.Any(r => r.Is64Bit);
                    only32BitJava = !has64BitJava;
                }

                if (only32BitJava && !has32BitModernJava)
                {
                    InfoWarning("You're using 32-bit Java on a 64-bit system.");
                    InfoWarning("32-bit Java is not recommended. You should use 64-bit Java 17 or higher.");
                    InfoBlankLine();

                    LogForLog("Only 32-bit Java detected.");
                }

                JavaRuntime? recommended = SelectPreferredRuntime(lastDetected);

                if (recommended != null && recommended.MajorVersion >= 17)
                {                    
                    LogForLog($"Auto-applying association to runtime: {recommended.JavawPath}");
                    var ok = JarAssociationFixer.SetJarAssociationUserScope(recommended);
                    
                    if (ok)
                    {
                        Info("Successfully updated the .jar suffix. Your .jar files should now open with Java " + recommended.MajorVersion + "."  + "\n" );
                        Info("Java runtime environment: "  + "\n" + recommended.JavawPath);
                        LogForLog("Association updated (user-scope).");
                    }
                    else
                    {
                        Info("Failed to update the file association. Try running Jarfix as administrator.");
                        LogForLog("Failed to update association (user-scope).", LogLevel.ERROR);
                    }

                    await DoDetect();
                    UpdateDetectedTab();
                    return;
                }
                            
                var dialogResult = MessageBox.Show(
                    "No suitable Java runtime (17 or higher) was found. Would you like to download and install Java 21?",
                    "Java installation needed",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (dialogResult == DialogResult.Yes)
                {   
                    using var progressForm = new DownloadProgressForm();
                    progressForm.StartPosition = FormStartPosition.CenterParent;
                    progressForm.Show(this);
                    await DownloadAndInstallJdk21(MicrosoftJdk21Msi, progressForm);
                    if (!progressForm.IsDisposed)
                    {
                        try { progressForm.Close(); } catch { }
                    }
                }
                else
                {
                    LogForLog("User declined download.", LogLevel.WARNING);
                }

                LogForLog("Re-detecting runtimes after install attempt.");
                await DoDetect();
                UpdateDetectedTab();

                JavaRuntime? afterRec = SelectPreferredRuntime(lastDetected);
                if (afterRec != null && afterRec.MajorVersion >= 17)
                {
                    jarfixInfoBox.Clear();
                    Info("Installation Complete!");
                    Info($"Java {afterRec.MajorVersion} has been installed successfully.");
                    InfoBlankLine();                    
                    LogForLog($"Auto-applying association to runtime: {afterRec.JavawPath}");
                    var ok2 = JarAssociationFixer.SetJarAssociationUserScope(afterRec);
                    
                    if (ok2)
                    {
                        Info("Successfully updated the .jar suffix. Your .jar files are now configured to run with Java " + afterRec.MajorVersion + "." + "\n");
                        Info("Java runtime environment: " + "\n" + afterRec.JavawPath);
                        LogForLog("Association updated after install.");
                    }
                    else
                    {
                        Info("Installation succeeded, but failed to set file association. Try running Jarfix as administrator.");
                        LogForLog("Failed to set association after install.", LogLevel.ERROR);
                    }
                }
                else if (dialogResult == DialogResult.Yes)
                {
                    Info("Installation may not have completed successfully. Please check the Log tab for details.");
                }
            }
            catch (OperationCanceledException)
            {
                Info("Operation cancelled by user.");
                LogForLog("Operation cancelled by user.", LogLevel.WARNING);
            }
            catch (Exception ex)
            {
                Info("An unexpected error occurred. Check the Log tab for details.");
                LogForLog($"Unexpected error: {ex.Message}", LogLevel.ERROR);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private JavaRuntime? SelectPreferredRuntime(List<JavaRuntime> runtimes)
        {
            if (runtimes == null || runtimes.Count == 0) return null;
            
            var cand = runtimes.FindAll(x => x.MajorVersion >= 17 && x.Is64Bit);
            if (cand.Count > 0)
            {
                cand.Sort((a, b) => b.MajorVersion.CompareTo(a.MajorVersion));
                return cand[0];
            }
            
            return null;
        }

        private void SetBusy(bool busy)
        {
            btnRefresh.Enabled = !busy;
        }

        private void InfoWarning(string message)
        {
            if (jarfixInfoBox.InvokeRequired)
            {
                jarfixInfoBox.BeginInvoke((Action)(() => InfoWarning(message)));
                return;
            }

            jarfixInfoBox.SelectionStart = jarfixInfoBox.TextLength;
            jarfixInfoBox.SelectionLength = 0;

            jarfixInfoBox.SelectionBackColor = System.Drawing.Color.LightYellow;
            jarfixInfoBox.SelectionColor = System.Drawing.Color.Black;

            jarfixInfoBox.AppendText(message + Environment.NewLine);

            jarfixInfoBox.SelectionBackColor = jarfixInfoBox.BackColor;
            jarfixInfoBox.SelectionColor = jarfixInfoBox.ForeColor;
        }
        private async Task DoDetect()
        {
            lastDetected = await JavaDetector.DetectInstalledJavaAsync();
            UpdateDetectedTab();
        }

        private void UpdateDetectedTab()
        {
            if (lvDetected.InvokeRequired)
            {
                lvDetected.BeginInvoke((Action)UpdateDetectedTab);
                return;
            }

            lvDetected.Items.Clear();
            foreach (var r in lastDetected)
            {
                var arch = r.Is64Bit ? "x64" : "x86";
                var item = new ListViewItem(new[] { r.Vendor, r.MajorVersion.ToString(), arch, r.JavawPath });
                lvDetected.Items.Add(item);
            }
            
            lvDetected.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
            lvDetected.AutoResizeColumns(ColumnHeaderAutoResizeStyle.HeaderSize);
        }

        private async Task DownloadAndInstallJdk21(string msiUrl, DownloadProgressForm progressForm)
        {
            var tempFile = Path.Combine(Path.GetTempPath(), "jarfix-jdk21.msi");
            downloadCts = new CancellationTokenSource();
            var token = downloadCts.Token;
            try
            {
                progressForm.SetCancelable(() => downloadCts?.Cancel());
                progressForm.UpdateProgress(-1, "Starting download...");
                LogForLog("Beginning download of Java 21 installer...");

                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                using var response = await http.GetAsync(msiUrl, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();

                var contentLength = response.Content.Headers.ContentLength ?? -1L;
                using var inStream = await response.Content.ReadAsStreamAsync(token);

                var sw = Stopwatch.StartNew();
                long totalRead = 0;
                var buffer = new byte[81920];

                using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    while (true)
                    {
                        var read = await inStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                        if (read == 0) break;
                        await fs.WriteAsync(buffer.AsMemory(0, read), token);
                        totalRead += read;

                        int percent = -1;
                        if (contentLength > 0)
                            percent = (int)(totalRead * 100 / contentLength);

                        var mbDownloaded = totalRead / 1024.0 / 1024.0;
                        var mbTotal = contentLength > 0 ? contentLength / 1024.0 / 1024.0 : -1.0;
                        var seconds = Math.Max(0.001, sw.Elapsed.TotalSeconds);
                        var mbPerSec = mbDownloaded / seconds;

                        var statusText = mbTotal > 0
                            ? $"Downloading: {mbDownloaded:F1} MB / {mbTotal:F1} MB ({mbPerSec:F2} MB/s)"
                            : $"Downloading: {mbDownloaded:F1} MB ({mbPerSec:F2} MB/s)";

                        progressForm.UpdateProgress(percent, statusText);
                    }
                }

                sw.Stop();
                progressForm.UpdateProgress(100, "Download complete. Launching installer...");
                LogForLog("Download complete; launching MSI installer (elevated).");

                var psi = new ProcessStartInfo
                {
                    FileName = "msiexec.exe",
                    Arguments = $"/i \"{tempFile}\" /passive",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                progressForm.UpdateProgress(100, "Requesting elevation for installer...");
                var proc = Process.Start(psi);
                if (proc == null)
                {
                    progressForm.UpdateProgress(-1, "Failed to start installer.");
                    LogForLog("Failed to start MSI installer process.", LogLevel.ERROR);
                    return;
                }

                progressForm.UpdateProgress(100, "Installing Java...");
                await Task.Run(() =>
                {
                    try { proc.WaitForExit(); } catch { }
                });

                progressForm.UpdateProgress(100, "Installer finished.");
                LogForLog("Installer process finished.");
            }
            catch (OperationCanceledException)
            {
                progressForm.UpdateProgress(-1, "Download cancelled.");
                LogForLog("User cancelled download.", LogLevel.WARNING);
                throw;
            }
            catch (Exception ex)
            {          
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
                progressForm.UpdateProgress(-1, "Download/install failed.");
                LogForLog($"Download or install failed: {ex.Message}", LogLevel.ERROR);
            }
            finally
            {
                downloadCts = null;
            }
        }

        private async Task UploadLogClicked()
        {
            try
            {
                btnUploadLog.Enabled = false;
                var content = runLog.ToString();
                if (string.IsNullOrWhiteSpace(content))
                {
                    LogForLog("Upload aborted: no log content.", LogLevel.WARNING);
                    MessageBox.Show("No log to upload.", "Jarfix", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var tmp = Path.Combine(Path.GetTempPath(), "jarfix-upload-log.txt");
                await File.WriteAllTextAsync(tmp, content, Encoding.UTF8);

                LogForLog("Uploading log to mclo.gs...");

                var jsonOpt = await UploadUtil.UploadLogAsync(tmp);

                if (jsonOpt.HasValue)
                {
                    var root = jsonOpt.Value;
                    if (root.TryGetProperty("success", out var successProp) && successProp.ValueKind == JsonValueKind.True)
                    {
                        if (root.TryGetProperty("url", out var urlProp))
                        {
                            var url = urlProp.GetString() ?? "";
                            LogForLog($"Log uploaded: {url}");
                            var res = MessageBox.Show($"Log uploaded: {url}\n\nClick Yes to copy URL to clipboard.", "Upload complete", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                            if (res == DialogResult.Yes && !string.IsNullOrEmpty(url))
                            {
                                var staThread = new Thread(() =>
                                {
                                    try
                                    {
                                        Clipboard.SetText(url);
                                    }
                                    catch (Exception ex)
                                    {
                                        LogForLog($"Failed to copy URL to clipboard: {ex.Message}");
                                    }
                                });
                                staThread.SetApartmentState(ApartmentState.STA);
                                staThread.Start();
                                staThread.Join();
                                LogForLog("URL copied to clipboard.");
                            }
                        }
                        else
                        {
                            LogForLog("Upload returned success but no URL.");
                            MessageBox.Show("Upload returned success but no URL.", "Jarfix", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                    else
                    {
                        var err = root.TryGetProperty("error", out var er) ? er.GetString() : "Unknown error";
                        LogForLog($"Upload failed: {err}");
                        MessageBox.Show($"Upload failed: {err}", "Jarfix", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    LogForLog("Upload failed (no response).");
                    MessageBox.Show("Upload failed (no response).", "Jarfix", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                LogForLog($"Upload error: {ex.Message}", LogLevel.ERROR);
                MessageBox.Show($"Upload failed: {ex.Message}", "Jarfix", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnUploadLog.Enabled = true;
            }
        }

        private class DownloadProgressForm : Form
        {
            private ProgressBar pb = null!;
            private Label lbl = null!;
            private Button btnCancel = null!;
            private Action? cancelAction;

            public DownloadProgressForm()
            {
                Width = 520;
                Height = 140;
                StartPosition = FormStartPosition.CenterParent;
                Text = "Downloading Java 21";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;

                lbl = new Label { Left = 12, Top = 12, Width = 480, Height = 28, Text = "Starting..." };
                pb = new ProgressBar { Left = 12, Top = 44, Width = 480, Height = 20, Minimum = 0, Maximum = 100, Value = 0 };
                btnCancel = new Button { Left = 12, Top = 70, Width = 100, Text = "Cancel" };
                btnCancel.Click += (s, e) =>
                {
                    btnCancel.Enabled = false;
                    cancelAction?.Invoke();
                    lbl.Text = "Cancelling...";
                };

                Controls.Add(lbl);
                Controls.Add(pb);
                Controls.Add(btnCancel);
            }

            public void SetCancelable(Action onCancel)
            {
                cancelAction = onCancel;
            }

            public void UpdateProgress(int percent, string status)
            {
                if (IsDisposed) return;
                if (InvokeRequired)
                {
                    BeginInvoke((Action)(() => UpdateProgress(percent, status)));
                    return;
                }
                lbl.Text = status;
                if (percent >= 0)
                {
                    pb.Style = ProgressBarStyle.Continuous;
                    pb.Value = Math.Max(0, Math.Min(100, percent));
                }
                else
                {
                    pb.Style = ProgressBarStyle.Marquee;
                }
                Application.DoEvents();
            }
        }
    }
}