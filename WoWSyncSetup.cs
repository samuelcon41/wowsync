using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WoWSyncSetup
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
        }
    }

    public class MainForm : Form
    {
        private TextBox txtCloud;
        private TextBox txtWow;
        private ComboBox cmbFlavor;
        private Button btnBrowseCloud;
        private Button btnBrowseWow;
        private Button btnRun;
        private TextBox txtLog;
        private bool autoRun;

        public MainForm(string[] args)
        {
            Text = "WoW Sync Setup";
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(640, 470);
            MinimumSize = new Size(560, 420);
            StartPosition = FormStartPosition.CenterScreen;

            Label lblIntro = new Label();
            lblIntro.Text = "Syncs your WoW addons + addon settings across PCs through a cloud folder\r\n" +
                            "(Google Drive, OneDrive, Dropbox...). Run on your main PC first, then on the others.";
            lblIntro.SetBounds(12, 10, 616, 34);
            lblIntro.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(lblIntro);

            Label lblCloud = new Label();
            lblCloud.Text = "Cloud sync folder:";
            lblCloud.SetBounds(12, 54, 130, 20);
            Controls.Add(lblCloud);

            txtCloud = new TextBox();
            txtCloud.SetBounds(145, 51, 395, 23);
            txtCloud.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(txtCloud);

            btnBrowseCloud = new Button();
            btnBrowseCloud.Text = "Browse...";
            btnBrowseCloud.SetBounds(548, 50, 80, 25);
            btnBrowseCloud.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowseCloud.Click += new EventHandler(BrowseCloud_Click);
            Controls.Add(btnBrowseCloud);

            Label lblWow = new Label();
            lblWow.Text = "WoW install folder:";
            lblWow.SetBounds(12, 84, 130, 20);
            Controls.Add(lblWow);

            txtWow = new TextBox();
            txtWow.SetBounds(145, 81, 395, 23);
            txtWow.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(txtWow);

            btnBrowseWow = new Button();
            btnBrowseWow.Text = "Browse...";
            btnBrowseWow.SetBounds(548, 80, 80, 25);
            btnBrowseWow.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowseWow.Click += new EventHandler(BrowseWow_Click);
            Controls.Add(btnBrowseWow);

            Label lblFlavor = new Label();
            lblFlavor.Text = "Game version:";
            lblFlavor.SetBounds(12, 114, 130, 20);
            Controls.Add(lblFlavor);

            cmbFlavor = new ComboBox();
            cmbFlavor.SetBounds(145, 111, 160, 23);
            cmbFlavor.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbFlavor.Items.AddRange(new object[] { "_retail_", "_classic_", "_classic_era_", "_anniversary_" });
            cmbFlavor.SelectedIndex = 0;
            Controls.Add(cmbFlavor);

            btnRun = new Button();
            btnRun.Text = "Set Up Sync";
            btnRun.SetBounds(145, 144, 160, 32);
            btnRun.Click += new EventHandler(Run_Click);
            Controls.Add(btnRun);

            txtLog = new TextBox();
            txtLog.SetBounds(12, 188, 616, 270);
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BackColor = Color.White;
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(txtLog);

            txtCloud.Text = DetectCloudFolder();
            txtWow.Text = DetectWowFolder();

            ParseArgs(args);

            Log("Ready. Check the folders above, then click 'Set Up Sync'.");
            if (txtCloud.Text.Length == 0)
                Log("Couldn't auto-detect a cloud folder - browse to your Google Drive / OneDrive / Dropbox and pick (or create) a 'WoW Sync' folder inside it.");

            if (autoRun)
            {
                Shown += new EventHandler(delegate(object s, EventArgs e) { Run_Click(s, e); });
            }
        }

        private void ParseArgs(string[] args)
        {
            foreach (string a in args)
            {
                if (a.StartsWith("/cloud=", StringComparison.OrdinalIgnoreCase))
                    txtCloud.Text = a.Substring(7).Trim('"');
                else if (a.StartsWith("/wow=", StringComparison.OrdinalIgnoreCase))
                    txtWow.Text = a.Substring(5).Trim('"');
                else if (a.StartsWith("/flavor=", StringComparison.OrdinalIgnoreCase))
                {
                    string f = a.Substring(8).Trim('"');
                    int idx = cmbFlavor.Items.IndexOf(f);
                    if (idx >= 0) cmbFlavor.SelectedIndex = idx;
                }
                else if (a.Equals("/run", StringComparison.OrdinalIgnoreCase))
                    autoRun = true;
            }
        }

        private static string DetectCloudFolder()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] candidates = new string[]
            {
                Environment.GetEnvironmentVariable("OneDriveConsumer"),
                Environment.GetEnvironmentVariable("OneDrive"),
                @"G:\My Drive",
                Path.Combine(userProfile, "Google Drive"),
                Path.Combine(userProfile, "Dropbox")
            };
            foreach (string c in candidates)
            {
                if (!string.IsNullOrEmpty(c) && Directory.Exists(c))
                    return Path.Combine(c, "WoW Sync");
            }
            return "";
        }

        private static string DetectWowFolder()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft"))
                {
                    if (key != null)
                    {
                        string p = key.GetValue("InstallPath") as string;
                        if (!string.IsNullOrEmpty(p))
                        {
                            // InstallPath points at the _retail_ (flavor) folder - we want its parent
                            string trimmed = p.TrimEnd('\\', '/');
                            string parent = Path.GetDirectoryName(trimmed);
                            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                                return parent;
                            if (Directory.Exists(trimmed))
                                return trimmed;
                        }
                    }
                }
            }
            catch { }

            string[] defaults = new string[]
            {
                @"C:\Program Files (x86)\World of Warcraft",
                @"C:\Program Files\World of Warcraft",
                @"D:\World of Warcraft",
                @"C:\Games\World of Warcraft"
            };
            foreach (string d in defaults)
                if (Directory.Exists(d)) return d;
            return "";
        }

        private void BrowseCloud_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Pick (or create) a folder INSIDE your cloud-synced drive, e.g. 'WoW Sync'.";
                if (Directory.Exists(txtCloud.Text)) dlg.SelectedPath = txtCloud.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) txtCloud.Text = dlg.SelectedPath;
            }
        }

        private void BrowseWow_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Pick your World of Warcraft install folder (the one containing _retail_).";
                if (Directory.Exists(txtWow.Text)) dlg.SelectedPath = txtWow.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) txtWow.Text = dlg.SelectedPath;
            }
        }

        private static bool IsAdmin()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
            {
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private void Run_Click(object sender, EventArgs e)
        {
            string cloud = txtCloud.Text.Trim();
            string wow = txtWow.Text.Trim();
            string flavor = cmbFlavor.SelectedItem.ToString();

            if (cloud.Length == 0 || wow.Length == 0)
            {
                MessageBox.Show(this, "Please fill in both the cloud folder and the WoW install folder.",
                    "WoW Sync Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // The WoW folder usually needs admin rights to modify - relaunch elevated if we aren't.
            if (!IsAdmin())
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = Application.ExecutablePath;
                    psi.Arguments = string.Format("\"/cloud={0}\" \"/wow={1}\" /flavor={2} /run", cloud, wow, flavor);
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    Process.Start(psi);
                    Application.Exit();
                    return;
                }
                catch (Win32Exception)
                {
                    Log("Admin elevation was declined - trying without it (this can still work).");
                }
            }

            btnRun.Enabled = false;
            Thread t = new Thread(new ThreadStart(delegate()
            {
                try { DoSetup(cloud, wow, flavor); }
                catch (Exception ex) { Log("ERROR: " + ex.Message); }
                finally { Invoke(new MethodInvoker(delegate() { btnRun.Enabled = true; })); }
            }));
            t.IsBackground = true;
            t.Start();
        }

        private void Log(string msg)
        {
            if (InvokeRequired)
            {
                Invoke(new MethodInvoker(delegate() { Log(msg); }));
                return;
            }
            txtLog.AppendText(msg + "\r\n");
        }

        private void DoSetup(string cloudFolder, string wowPath, string flavor)
        {
            Log("");
            Log("=== Starting setup (" + flavor + ") ===");

            if (Process.GetProcessesByName("Wow").Length > 0 || Process.GetProcessesByName("WowClassic").Length > 0)
            {
                Log("ERROR: World of Warcraft is running. Close the game completely (Battle.net can stay open), then try again.");
                return;
            }

            string flavorPath = Path.Combine(wowPath, flavor);
            if (!Directory.Exists(flavorPath))
            {
                Log("ERROR: Can't find '" + flavorPath + "'. Check the WoW folder and game version above.");
                return;
            }

            string cloudRoot = Path.Combine(cloudFolder, flavor);
            Directory.CreateDirectory(cloudRoot);

            string[][] pairs = new string[][]
            {
                new string[] { Path.Combine(flavorPath, @"Interface\AddOns"), Path.Combine(cloudRoot, "AddOns"), "AddOns" },
                new string[] { Path.Combine(flavorPath, @"WTF\Account"), Path.Combine(cloudRoot, "WTF-Account"), "Settings (WTF\\Account)" }
            };

            string cloudAddons = pairs[0][1];
            bool cloudSeeded = Directory.Exists(cloudAddons) && Directory.GetFileSystemEntries(cloudAddons).Length > 0;
            if (cloudSeeded)
                Log("Cloud folder already has addons in it - linking this PC to the existing cloud copy.");
            else
                Log("Cloud folder is empty - this PC's addons and settings will become the master copy.");

            foreach (string[] pair in pairs)
            {
                string game = pair[0];
                string cloud = pair[1];
                string label = pair[2];
                Log("");
                Log(">>> " + label);

                if (Directory.Exists(game) &&
                    (File.GetAttributes(game) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                {
                    Log("    Already linked - skipping.");
                    continue;
                }

                if (!cloudSeeded)
                {
                    if (!Directory.Exists(game))
                    {
                        Log("ERROR: '" + game + "' doesn't exist, so there is nothing to copy. Run the game once first, or seed from your main PC.");
                        return;
                    }
                    Log("    Copying to cloud (this can take a few minutes)...");
                    int code = RunHidden("robocopy",
                        string.Format("\"{0}\" \"{1}\" /E /MT:16 /R:2 /W:2 /NFL /NDL /NJH /NP", game, cloud));
                    if (code >= 8) { Log("ERROR: Copy failed (robocopy code " + code + ")."); return; }
                    Log("    Copy done.");
                }
                else if (!Directory.Exists(cloud))
                {
                    Log("ERROR: Cloud copy '" + cloud + "' not found. Wait for your cloud app to finish downloading, then try again.");
                    return;
                }

                if (Directory.Exists(game))
                {
                    string backup = game + ".pre-sync-backup";
                    if (Directory.Exists(backup))
                        backup = backup + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    Directory.Move(game, backup);
                    Log("    Your old folder was kept as: " + backup);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(game));
                }

                int linkCode = RunHidden("cmd.exe", string.Format("/c mklink /J \"{0}\" \"{1}\"", game, cloud));
                if (linkCode != 0)
                    linkCode = RunHidden("cmd.exe", string.Format("/c mklink /D \"{0}\" \"{1}\"", game, cloud));
                if (linkCode != 0)
                {
                    Log("ERROR: Couldn't create the link. Try running this program as administrator (right-click -> Run as administrator).");
                    return;
                }
                Log("    Linked: " + game);
                Log("        -> " + cloud);

                try
                {
                    int count = Directory.GetFiles(game, "*", SearchOption.AllDirectories).Length;
                    if (count == 0 && cloudSeeded)
                    {
                        Log("WARNING: The link works but no files are visible yet - your cloud app may still be downloading.");
                    }
                    else
                    {
                        Log("    Verified: " + count + " files visible to the game.");
                    }
                }
                catch (Exception ex)
                {
                    Log("    (Couldn't count files: " + ex.Message + ")");
                }
            }

            Log("");
            Log("=== Done! ===");
            Log("Two things to remember:");
            Log(" 1. In your cloud app, mark the sync folder 'Available offline' (Google Drive) or");
            Log("    'Always keep on this device' (OneDrive) so the game loads at full speed.");
            Log(" 2. Don't play WoW on two PCs at the same time - settings save on logout, last one wins.");
            Log("");
            Log("On your other PC: install the same cloud app, wait for the folder to download,");
            Log("then run this same program there.");
        }

        private static int RunHidden(string file, string arguments)
        {
            using (Process p = new Process())
            {
                p.StartInfo.FileName = file;
                p.StartInfo.Arguments = arguments;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                p.WaitForExit();
                return p.ExitCode;
            }
        }
    }
}
