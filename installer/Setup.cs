using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

// Zombie Empire installer: the game (payload.zip) is embedded in the exe.
//   ZombieEmpire-Setup.exe                         install with the window
//   ZombieEmpire-Setup.exe /silent [/dir=PATH] [/nolaunch] [/noshortcuts]   no window (tests)
//   Uninstall.exe /uninstall [/silent]             uninstall
static class Setup
{
    const string AppName = "Zombie Empire";
    const string Version = "1.1";
    const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ZombieEmpire";

    static string DefaultDir
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName); }
    }

    [STAThread]
    static int Main(string[] args)
    {
        bool silent = false, noLaunch = false, uninstall = false, shortcuts = true;
        string dir = DefaultDir;
        foreach (string a in args)
        {
            string l = a.ToLowerInvariant();
            if (l == "/silent") silent = true;
            else if (l == "/nolaunch") noLaunch = true;
            else if (l == "/uninstall") uninstall = true;
            else if (l == "/noshortcuts") shortcuts = false;
            else if (l.StartsWith("/dir=")) dir = a.Substring(5).Trim('"');
        }

        Application.EnableVisualStyles();
        if (uninstall) return Uninstall(silent);
        if (silent)
        {
            try { string root = Install(dir, shortcuts, shortcuts, null); if (!noLaunch) Launch(root); return 0; }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
        Application.Run(new SetupForm());
        return 0;
    }

    // ---------- install ----------

    const string Marker = ".zombieempire-install";

    // Returns the actual install folder.
    public static string Install(string dir, bool desktopShortcut, bool startMenuShortcut, Action<int, int, string> progress)
    {
        string root = Path.GetFullPath(dir);
        // Never install into a folder that contains other things (e.g. the Desktop):
        // uninstalling deletes the whole folder.
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).GetEnumerator().MoveNext()
            && !File.Exists(Path.Combine(root, Marker)))
            root = Path.Combine(root, AppName);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, Marker), Version);
        using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
        using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
        {
            int total = zip.Entries.Count, n = 0;
            foreach (ZipArchiveEntry e in zip.Entries)
            {
                n++;
                string target = Path.GetFullPath(Path.Combine(root, e.FullName.Replace('/', '\\')));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // paths outside the folder
                if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                // Update: don't overwrite worlds that were already played
                if (KeepExisting(root, e.FullName) && File.Exists(target)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (Stream src = e.Open())
                using (Stream dst = File.Create(target)) src.CopyTo(dst);
                if (progress != null && (n % 5 == 0 || n == total)) progress(n, total, e.Name);
            }
        }

        // The uninstaller is a copy of this exe
        string uninstaller = Path.Combine(root, "Uninstall.exe");
        File.Copy(Assembly.GetExecutingAssembly().Location, uninstaller, true);

        string exe = Path.Combine(root, "ZombieEmpire.exe");
        if (desktopShortcut)
            Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"), exe, root);
        if (startMenuShortcut)
            Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"), exe, root);

        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RegKey))
        {
            k.SetValue("DisplayName", AppName);
            k.SetValue("DisplayVersion", Version);
            k.SetValue("Publisher", "Zombie Empire");
            k.SetValue("DisplayIcon", exe + ",0");
            k.SetValue("InstallLocation", root);
            k.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
            k.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /uninstall /silent");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)(DirSize(root) / 1024), RegistryValueKind.DWord);
        }
        return root;
    }

    static bool KeepExisting(string root, string entry)
    {
        return entry.StartsWith("instances/ZombieEmpire/minecraft/saves/", StringComparison.OrdinalIgnoreCase)
            || entry.Equals("ZombieEmpire.ini", StringComparison.OrdinalIgnoreCase);
    }

    public static void Launch(string dir)
    {
        var psi = new ProcessStartInfo(Path.Combine(dir, "ZombieEmpire.exe")) { WorkingDirectory = dir, UseShellExecute = false };
        Process.Start(psi);
    }

    static void Shortcut(string lnk, string target, string workDir)
    {
        Type t = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(t);
        object s = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
        Type st = s.GetType();
        st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, s, new object[] { target });
        st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, s, new object[] { workDir });
        st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, s, new object[] { target + ",0" });
        st.InvokeMember("Description", BindingFlags.SetProperty, null, s, new object[] { AppName });
        st.InvokeMember("Save", BindingFlags.InvokeMethod, null, s, null);
    }

    static long DirSize(string dir)
    {
        long size = 0;
        foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) size += new FileInfo(f).Length;
        return size;
    }

    // ---------- uninstall ----------

    static int Uninstall(bool silent)
    {
        string root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!File.Exists(Path.Combine(root, "ZombieEmpire.exe")) || !File.Exists(Path.Combine(root, Marker)))
        {
            if (!silent) MessageBox.Show("This is not a Zombie Empire installation folder.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        if (!silent && MessageBox.Show("Uninstall Zombie Empire?\n\nThe game, your worlds and your login in this launcher will be removed from:\n" + root,
                AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return 0;
        if (Process.GetProcessesByName("javaw").Length > 0 || Process.GetProcessesByName("prismlauncher").Length > 0)
        {
            if (!silent) MessageBox.Show("Close Zombie Empire / Minecraft before uninstalling.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }

        foreach (string lnk in new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk") })
            if (File.Exists(lnk)) File.Delete(lnk);
        Registry.CurrentUser.DeleteSubKeyTree(RegKey, false);
        string state = Environment.GetEnvironmentVariable("ZOMBIEEMPIRE_STATE");
        if (string.IsNullOrEmpty(state))
            state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZombieEmpire");
        if (Directory.Exists(state)) Directory.Delete(state, true);

        // The folder contains this exe: cmd removes it after we exit.
        var psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + root + "\"")
        { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() };
        Process.Start(psi);
        if (!silent) MessageBox.Show("Zombie Empire has been uninstalled.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }
}

class SetupForm : Form
{
    readonly TextBox dirBox = new TextBox();
    readonly CheckBox desktop = new CheckBox(), startMenu = new CheckBox();
    readonly Button install = new Button(), browse = new Button();
    readonly ProgressBar bar = new ProgressBar();
    readonly Label status = new Label();

    static readonly Color Bg = Color.FromArgb(18, 20, 18), Fg = Color.FromArgb(225, 230, 220), Red = Color.FromArgb(200, 30, 30), Green = Color.FromArgb(110, 170, 90);

    public SetupForm()
    {
        Text = "Zombie Empire Setup";
        ClientSize = new Size(560, 330);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg; ForeColor = Fg;
        Font = new Font("Segoe UI", 9.5f);
        try { Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { }

        var logo = new PictureBox { Left = 24, Top = 22, Width = 72, Height = 72, SizeMode = PictureBoxSizeMode.Zoom };
        try { logo.Image = Image.FromStream(Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png")); } catch { }
        var title = new Label { Left = 110, Top = 22, Width = 420, Height = 44, Text = "ZOMBIE EMPIRE", ForeColor = Red, Font = new Font("Segoe UI", 24f, FontStyle.Bold) };
        var sub = new Label { Left = 112, Top = 68, Width = 420, Height = 22, Text = "Minecraft 1.21.1 zombies modpack", ForeColor = Green };

        var dirLabel = new Label { Left = 24, Top = 112, Width = 300, Text = "Install folder" };
        dirBox.SetBounds(24, 134, 420, 26); dirBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Zombie Empire");
        dirBox.BackColor = Color.FromArgb(34, 38, 34); dirBox.ForeColor = Fg; dirBox.BorderStyle = BorderStyle.FixedSingle;
        browse.SetBounds(452, 133, 84, 28); browse.Text = "Browse..."; Flat(browse, Color.FromArgb(50, 56, 50));
        browse.Click += (s, e) =>
        {
            using (var d = new FolderBrowserDialog { Description = "Choose where to install Zombie Empire" })
                if (d.ShowDialog(this) == DialogResult.OK) dirBox.Text = Path.Combine(d.SelectedPath, "Zombie Empire");
        };

        desktop.SetBounds(24, 172, 300, 24); desktop.Text = "Create a desktop shortcut"; desktop.Checked = true;
        startMenu.SetBounds(24, 196, 300, 24); startMenu.Text = "Add to the Start menu"; startMenu.Checked = true;

        bar.SetBounds(24, 236, 512, 16); bar.Visible = false;
        status.SetBounds(24, 256, 512, 20); status.ForeColor = Color.FromArgb(150, 160, 145);
        status.Text = "Requires Minecraft: Java Edition. Mods download from Modrinth on first launch.";

        install.SetBounds(386, 282, 150, 36); install.Text = "INSTALL && PLAY"; Flat(install, Red);
        install.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        install.Click += (s, e) => DoInstall();

        Controls.AddRange(new Control[] { logo, title, sub, dirLabel, dirBox, browse, desktop, startMenu, bar, status, install });
        AcceptButton = install;
    }

    static void Flat(Button b, Color c)
    {
        b.FlatStyle = FlatStyle.Flat; b.BackColor = c; b.ForeColor = Color.White;
        b.FlatAppearance.BorderSize = 0; b.Cursor = Cursors.Hand;
    }

    void DoInstall()
    {
        string dir = dirBox.Text.Trim();
        if (dir.Length == 0) return;
        foreach (Control c in Controls) if (c is Button || c is CheckBox || c is TextBox) c.Enabled = false;
        bar.Visible = true;
        status.Text = "Installing...";
        try
        {
            dir = Setup.Install(dir, desktop.Checked, startMenu.Checked, (n, total, name) =>
            {
                bar.Maximum = total; bar.Value = n;
                status.Text = "Installing... " + (n * 100 / total) + "%";
                Application.DoEvents();
            });
            status.Text = "Done! Starting Zombie Empire...";
            Application.DoEvents();
            Setup.Launch(dir);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Installation failed:\n" + ex.Message, "Zombie Empire", MessageBoxButtons.OK, MessageBoxIcon.Error);
            foreach (Control c in Controls) c.Enabled = true;
            bar.Visible = false;
            status.Text = "Installation failed.";
        }
    }
}
