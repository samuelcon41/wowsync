// WoW Sync Setup v1.1
//
// Two-layer design:
//   * AddOns  -> directory junction into the cloud folder (live sync, safe:
//                a broken link is obvious and harmless)
//   * Settings (WTF\Account) -> stays a REAL local folder. A tiny background
//                agent pushes it to the cloud when WoW closes and pulls it
//                when the cloud copy is newer. WoW never depends on the cloud
//                drive being online.
//
// v1.0 linked WTF\Account directly; v1.1 detects that and converts it back.
//
// Build: build.cmd (embeds wowsync-agent.ps1 as a resource - keep that file
// next to this one when compiling).

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Reflection;
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
        private string launchedByUser;   // set on an elevated relaunch

        public MainForm(string[] args)
        {
            Text = "WoW Sync Setup v1.1";
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
                else if (a.StartsWith("/user=", StringComparison.OrdinalIgnoreCase))
                    launchedByUser = a.Substring(6).Trim('"');
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
                            // InstallPath usually points AT the flavor folder
                            // (...\World of Warcraft\_retail_), but not always.
                            // Only accept a candidate that actually contains a
                            // flavor folder - otherwise we'd happily return
                            // "C:\Program Files (x86)".
                            string trimmed = p.TrimEnd('\\', '/');
                            if (LooksLikeWowRoot(trimmed)) return trimmed;
                            string parent = Path.GetDirectoryName(trimmed);
                            if (LooksLikeWowRoot(parent)) return parent;
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
                if (LooksLikeWowRoot(d)) return d;
            foreach (string d in defaults)
                if (Directory.Exists(d)) return d;
            return "";
        }

        // A WoW root is a folder that directly contains at least one client
        // (flavor) folder.
        private static bool LooksLikeWowRoot(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return false;
            string[] flavors = new string[] { "_retail_", "_classic_", "_classic_era_", "_anniversary_", "_ptr_", "_beta_" };
            foreach (string f in flavors)
                if (Directory.Exists(Path.Combine(path, f))) return true;
            return false;
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
            string cloud = txtCloud.Text.Trim().TrimEnd('\\');
            string wow = txtWow.Text.Trim().TrimEnd('\\');
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
                    // Pass the current user's name through: if UAC is satisfied by a
                    // DIFFERENT admin account, the elevated run would otherwise install
                    // the background agent into that account's profile, where it would
                    // never run for this user.
                    psi.Arguments = string.Format("\"/cloud={0}\" \"/wow={1}\" /flavor={2} \"/user={3}\" /run",
                        cloud, wow, flavor, Environment.UserName);
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

        // ------------------------------------------------------------------
        //  Setup logic
        // ------------------------------------------------------------------

        private void DoSetup(string cloudFolder, string wowPath, string flavor)
        {
            Log("");
            Log("=== Starting setup (" + flavor + ") ===");

            if (WowRunning())
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
            try { Directory.CreateDirectory(cloudRoot); }
            catch (Exception ex)
            {
                Log("ERROR: Can't create '" + cloudRoot + "' - is your cloud drive running? (" + ex.Message + ")");
                return;
            }

            // Stop any agent for this game version FIRST: a running agent could
            // push or pull underneath the reconciliation below and race it.
            string localAcct = Path.Combine(flavorPath, @"WTF\Account");
            KillAgents(localAcct);

            bool addonsOk = SetupAddons(flavorPath, cloudRoot);
            bool settingsOk = SetupSettings(flavorPath, cloudRoot, flavor);
            bool agentOk = false;
            if (settingsOk)
                agentOk = InstallAgent(Path.Combine(cloudRoot, "WTF-Account"), localAcct, flavor);

            Log("");
            if (addonsOk && settingsOk && agentOk)
                Log("=== Done! Addons sync live; settings sync automatically whenever WoW closes. ===");
            else
                Log("=== Finished with problems - see messages above. Nothing was deleted. ===");
            Log("");
            Log("IMPORTANT - do this now, before you start WoW:");
            Log("  Right-click '" + cloudFolder + "'");
            Log("  -> Offline access -> Available offline   (Google Drive)");
            Log("  -> Always keep on this device            (OneDrive)");
            Log("  Then wait until every item shows the 'on this device' icon rather than a");
            Log("  cloud icon. Your addons are read through a link into that folder, so if the");
            Log("  download hasn't finished, WoW will start with addons missing or Lua errors.");
            Log("  (Nothing is broken if that happens - close WoW, let it finish, start again.)");
            Log("");
            Log("Also:");
            Log("  - On your other PC(s): install the same cloud app, mark the folder available");
            Log("    offline, let it finish downloading, THEN run this program there.");
            Log("  - Don't play WoW on two PCs at the same time.");
        }

        private bool SetupAddons(string flavorPath, string cloudRoot)
        {
            string game = Path.Combine(flavorPath, @"Interface\AddOns");
            string cloud = Path.Combine(cloudRoot, "AddOns");
            Log("");
            Log(">>> AddOns");

            if (Directory.Exists(game) && IsReparse(game))
            {
                // Already a link - but is it OUR link? A link to a different
                // cloud folder would leave this PC silently out of the sync.
                string target = ResolveLinkTarget(game);
                if (target != null && !target.Equals(cloud, StringComparison.OrdinalIgnoreCase))
                {
                    Log("WARNING: AddOns is already linked, but to a different folder:");
                    Log("           " + target);
                    Log("         Expected: " + cloud);
                    Log("         Leaving it alone. Delete that link and re-run if you want to switch.");
                    return false;
                }
                Log("    Already linked - skipping.");
                return true;
            }

            bool cloudSeeded = Directory.Exists(cloud) && Directory.GetFileSystemEntries(cloud).Length > 0;

            if (!cloudSeeded)
            {
                if (!Directory.Exists(game) || Directory.GetFileSystemEntries(game).Length == 0)
                {
                    Log("ERROR: No addons found locally and none in the cloud. Install some addons first, or run this on your main PC first.");
                    return false;
                }
                Log("    Cloud is empty - copying this PC's addons up (can take a few minutes)...");
                if (!Robo(game, cloud, false, "addons copy to cloud")) return false;
            }

            // Keep the original folder as a backup (rename can transiently fail
            // if something holds a handle on it - retry a few times).
            if (Directory.Exists(game))
            {
                string backup = UniqueBackupPath(game);
                bool renamed = false;
                for (int i = 0; i < 5; i++)
                {
                    try { Directory.Move(game, backup); renamed = true; break; }
                    catch (IOException) { Thread.Sleep(3000); }
                    catch (UnauthorizedAccessException) { Thread.Sleep(3000); }
                }
                if (!renamed)
                {
                    Log("ERROR: Windows is holding the AddOns folder open and won't let me rename it.");
                    Log("       Reboot and run this program again - that clears it.");
                    return false;
                }
                Log("    Your old AddOns folder was kept as: " + backup);
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
                Log("ERROR: Couldn't create the AddOns link. Try running this program as administrator.");
                return false;
            }
            Log("    Linked: " + game);
            Log("        -> " + cloud);

            int count = FileCount(game);
            if (count == 0 && cloudSeeded)
                Log("WARNING: Link works but no files visible yet - your cloud app may still be downloading.");
            else
                Log("    Verified: " + count + " addon files visible to the game.");

            WarnIfNotOffline(cloud);
            return true;
        }

        // Cloud clients keep files "online-only" by default: the name exists but
        // the contents aren't on disk until something opens them. WoW reading
        // addons through the link would then see missing/partial files. Windows
        // flags such files with recall attributes - check for them and say so
        // plainly, because the symptom (missing addons, Lua errors) looks like
        // a broken install rather than an unfinished download.
        private void WarnIfNotOffline(string cloudPath)
        {
            const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
            const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

            int placeholders = 0, checkedFiles = 0;
            try
            {
                foreach (string f in Directory.GetFiles(cloudPath, "*", SearchOption.AllDirectories))
                {
                    FileAttributes a = File.GetAttributes(f);
                    if ((a & RecallOnOpen) != 0 || (a & RecallOnDataAccess) != 0 || (a & FileAttributes.Offline) != 0)
                        placeholders++;
                    checkedFiles++;
                    if (checkedFiles >= 5000) break;   // a sample is enough to tell
                }
            }
            catch { return; }   // can't tell - stay quiet rather than cry wolf

            if (placeholders == 0)
            {
                Log("    Addon files are stored on this PC (not online-only) - good.");
                return;
            }

            Log("");
            Log("*** ACTION NEEDED BEFORE YOU START WOW ***");
            Log("    " + placeholders + " of the " + checkedFiles + " addon files checked are still ONLINE-ONLY -");
            Log("    your cloud app hasn't downloaded them to this PC yet.");
            Log("    If you launch WoW now, addons will be missing or throw Lua errors.");
            Log("");
            Log("    Fix: right-click your WoW Sync folder ->");
            Log("         'Offline access' -> 'Available offline'  (Google Drive)");
            Log("         'Always keep on this device'             (OneDrive)");
            Log("    Wait until the folder stops showing cloud icons, then start the game.");
        }

        private bool SetupSettings(string flavorPath, string cloudRoot, string flavor)
        {
            string game = Path.Combine(flavorPath, @"WTF\Account");
            string cloud = Path.Combine(cloudRoot, "WTF-Account");
            Log("");
            Log(">>> Settings (WTF\\Account)");

            // v1.0 migration: settings used to be a junction. Convert back to a
            // real local folder so WoW never depends on the cloud drive.
            // Check the restore source FIRST - removing the link before knowing
            // we can restore would leave the user with no settings folder.
            if (Directory.Exists(game) && IsReparse(game))
            {
                Log("    Found a settings link from WoW Sync v1.0 - converting back to a normal local folder...");
                if (FileCount(cloud) == 0)
                {
                    Log("ERROR: That link points at a cloud folder I can't read right now, so there is");
                    Log("       nothing to restore from. Make sure your cloud app is running and the");
                    Log("       folder has finished downloading, then run this again.");
                    Log("       (Your settings are NOT lost - they live in the cloud folder.)");
                    return false;
                }
                // Copy the data out BEFORE touching the link.
                string staging = game + ".restoring";
                if (Directory.Exists(staging)) { try { Directory.Delete(staging, true); } catch { } }
                if (!Robo(cloud, staging, false, "settings restore staging")) return false;
                if (FileCount(staging) == 0)
                {
                    Log("ERROR: Restore copy came out empty - stopping before changing anything.");
                    return false;
                }
                int rc = RunHidden("cmd.exe", string.Format("/c rmdir \"{0}\"", game)); // removes ONLY the link
                if (rc != 0 || Directory.Exists(game))
                {
                    Log("ERROR: Couldn't remove the old settings link. Reboot and run this program again.");
                    Log("       (Nothing was changed - your settings are safe in the cloud folder.)");
                    return false;
                }
                Directory.Move(staging, game);
                Log("    Restored " + FileCount(game) + " settings files to a normal local folder.");
                MarkJoined(flavor);   // this PC is now part of an established sync
            }

            bool cloudHas = FileCount(cloud) > 0;
            bool localHas = FileCount(game) > 0;

            if (!cloudHas && !localHas)
            {
                Log("WARNING: No settings found locally or in the cloud. Log into the game once, then run this again.");
                Log("         (Addons are still set up - only settings sync needs another pass.)");
                return false;
            }

            if (!cloudHas)
            {
                Log("    Cloud is empty - this PC becomes the master copy.");
                if (!Robo(game, cloud, false, "settings copy to cloud")) return false;
                Log("    Seeded cloud with " + FileCount(cloud) + " settings files.");
            }
            else if (!localHas)
            {
                Log("    No local settings - downloading the cloud copy...");
                if (!Robo(cloud, game, false, "settings download")) return false;
                Log("    Downloaded " + FileCount(game) + " settings files.");
            }
            else
            {
                // Both sides have settings. Which one wins depends on whether
                // this PC has ever been part of this sync before.
                //
                // NEVER use "newest wins" for a PC joining an existing sync:
                // a fresh WoW install that has been logged into once has
                // BRAND NEW default settings, which would look "newer" than
                // the master copy and mirror right over it - wiping the real
                // configuration on every machine.
                bool joinedBefore = HasJoined(flavor);

                if (!joinedBefore)
                {
                    Log("    This PC hasn't synced before and the cloud already has settings,");
                    Log("    so the cloud copy wins (it's the established master).");
                    string backup = UniqueBackupPath(game);
                    if (!Robo(game, backup, false, "settings backup")) return false;
                    Log("    This PC's old settings were kept as: " + backup);
                    if (!Robo(cloud, game, true, "settings download")) return false;
                }
                else
                {
                    DateTime localNewest = NewestWriteUtc(game);
                    DateTime cloudNewest = NewestWriteUtc(cloud);
                    if (cloudNewest > localNewest.AddMinutes(2))
                    {
                        Log("    Cloud settings are newer than this PC's - using the cloud copy.");
                        string backup = UniqueBackupPath(game);
                        if (!Robo(game, backup, false, "settings backup")) return false;
                        Log("    This PC's old settings were kept as: " + backup);
                        if (!Robo(cloud, game, true, "settings download")) return false;
                    }
                    else
                    {
                        // Local wins - but keep a copy of what the cloud had,
                        // in case the other PC had settings only it knew about.
                        Log("    This PC's settings are current - pushing them to the cloud.");
                        string cloudBackup = cloud + ".replaced-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                        if (!Robo(cloud, cloudBackup, false, "cloud settings backup")) return false;
                        Log("    Previous cloud copy kept as: " + Path.GetFileName(cloudBackup));
                        if (!Robo(game, cloud, true, "settings push")) return false;
                    }
                }
            }

            MarkJoined(flavor);
            Log("    Settings stay in a normal local folder - the background agent keeps them synced.");
            return true;
        }

        // "Has this PC already joined the sync for this game version?" Used to
        // stop a newly-joining PC from mirroring its default settings over an
        // established master copy.
        private static string JoinMarkerPath(string flavor)
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WoWSync");
            return Path.Combine(dir, "joined-" + flavor.Trim('_') + ".marker");
        }

        private static bool HasJoined(string flavor)
        {
            try { return File.Exists(JoinMarkerPath(flavor)); }
            catch { return false; }
        }

        private static void MarkJoined(string flavor)
        {
            try
            {
                string p = JoinMarkerPath(flavor);
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, DateTime.Now.ToString("u"));
            }
            catch { }
        }

        private bool InstallAgent(string cloudAcct, string localAcct, string flavor)
        {
            Log("");
            Log(">>> Background sync agent");

            // If UAC was satisfied by a different account, the agent would land
            // in that account's profile and never run for the actual player.
            if (!string.IsNullOrEmpty(launchedByUser) &&
                !launchedByUser.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase))
            {
                Log("WARNING: This program was elevated as '" + Environment.UserName + "', but you are");
                Log("         signed in as '" + launchedByUser + "'. The background settings agent can only");
                Log("         be installed for the account that plays the game.");
                Log("         Addons are set up and syncing. To finish settings sync, sign in as");
                Log("         '" + launchedByUser + "' with administrator rights and run this again.");
                return false;
            }

            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WoWSync");
            Directory.CreateDirectory(dir);
            string script = Path.Combine(dir, "wowsync-agent.ps1");

            // The agent script is embedded in this exe at build time.
            Stream res = Assembly.GetExecutingAssembly().GetManifestResourceStream("wowsync-agent.ps1");
            if (res == null)
            {
                Log("ERROR: agent script resource missing from this build.");
                return false;
            }
            using (StreamReader r = new StreamReader(res))
            {
                File.WriteAllText(script, r.ReadToEnd());
            }

            string agentArgs = string.Format(
                "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{0}\" -CloudSettings \"{1}\" -LocalSettings \"{2}\"",
                script, cloudAcct, localAcct);

            // Start at every login.
            string runName = "WoWSyncAgent-" + flavor.Trim('_');
            using (RegistryKey run = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"))
            {
                run.SetValue(runName, "powershell.exe " + agentArgs);
            }

            // Replace any agent already running for this game version.
            KillAgents(localAcct);

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "powershell.exe";
            psi.Arguments = agentArgs;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            Process.Start(psi);

            Log("    Agent installed and running (auto-starts at login).");
            Log("    Log files: " + Path.Combine(dir, "agent-*.log"));
            return true;
        }

        private void KillAgents(string localAcct)
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'powershell.exe'"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        string cmd = mo["CommandLine"] as string;
                        if (cmd == null) continue;
                        // Require the script to be the -File target. A bare
                        // "contains wowsync-agent" test would also match any
                        // console that merely mentions it - killing a user's shell.
                        if (cmd.IndexOf("wowsync-agent.ps1", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (cmd.IndexOf("-File", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        // Kill agents for THIS game version only. Early agents had no
                        // -LocalSettings argument at all; those are ours too. Agents for
                        // another flavor must keep running.
                        if (cmd.IndexOf("-LocalSettings", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            cmd.IndexOf(localAcct, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (Convert.ToInt32(mo["ProcessId"]) == Process.GetCurrentProcess().Id) continue;
                        try { Process.GetProcessById(Convert.ToInt32(mo["ProcessId"])).Kill(); }
                        catch { }
                    }
                }
            }
            catch { }
        }

        // ------------------------------------------------------------------
        //  Helpers
        // ------------------------------------------------------------------

        private static bool WowRunning()
        {
            string[] names = new string[] { "Wow", "WowClassic", "WowT", "WowB" };
            foreach (string n in names)
                if (Process.GetProcessesByName(n).Length > 0) return true;
            return false;
        }

        private static bool IsReparse(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint; }
            catch { return false; }
        }

        // Where does an existing junction/symlink actually point? Parsed from
        // `dir` output, which is available on every supported Windows without
        // P/Invoke: "AddOns [C:\target\path]".
        private static string ResolveLinkTarget(string linkPath)
        {
            try
            {
                string parent = Path.GetDirectoryName(linkPath);
                string name = Path.GetFileName(linkPath);
                using (Process p = new Process())
                {
                    p.StartInfo.FileName = "cmd.exe";
                    p.StartInfo.Arguments = string.Format("/c dir /al \"{0}\"", parent);
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.CreateNoWindow = true;
                    p.Start();
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();

                    foreach (string line in output.Split('\n'))
                    {
                        if (line.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        int open = line.LastIndexOf('[');
                        int close = line.LastIndexOf(']');
                        if (open >= 0 && close > open)
                            return line.Substring(open + 1, close - open - 1).Trim().TrimEnd('\\');
                    }
                }
            }
            catch { }
            return null;   // unknown - caller treats this as "don't block"
        }

        private static int FileCount(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return 0;
                return Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length;
            }
            catch { return 0; }
        }

        private static DateTime NewestWriteUtc(string path)
        {
            DateTime newest = DateTime.MinValue;
            try
            {
                foreach (string f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    DateTime t = File.GetLastWriteTimeUtc(f);
                    if (t > newest) newest = t;
                }
            }
            catch { }
            return newest;
        }

        private static string UniqueBackupPath(string folder)
        {
            string backup = folder + ".pre-sync-backup";
            if (Directory.Exists(backup))
                backup = backup + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            return backup;
        }

        private bool Robo(string src, string dst, bool mirror, string label)
        {
            try { Directory.CreateDirectory(dst); }
            catch (Exception ex) { Log("ERROR: " + label + " - can't create '" + dst + "' (" + ex.Message + ")"); return false; }
            string flags = mirror ? "/MIR" : "/E";
            int code = RunHidden("robocopy",
                string.Format("\"{0}\" \"{1}\" {2} /MT:8 /R:2 /W:2 /NFL /NDL /NJH /NP", src, dst, flags));
            if (code >= 8)
            {
                Log("ERROR: " + label + " failed (robocopy code " + code + ").");
                return false;
            }
            return true;
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
