using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;

// Starts Prism Launcher (portable, same folder):
//  1. if the official server answers, joins it directly;
//  2. otherwise opens the singleplayer world (moving the player back to the hub with the
//     chosen game mode) and the zombieempire mod opens it to LAN on the configured port.
static class ZombieEmpire
{
    [STAThread]
    static int Main()
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        var cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "instance", "ZombieEmpire" },
            { "server", "zombieempire.serveminecraft.net" },
            { "world", "bossworld" },
            { "hub", "spawn" },
            { "gamemode", "adventure" },
            { "lanport", "25565" },
        };

        string ini = Path.Combine(root, "ZombieEmpire.ini");
        if (File.Exists(ini))
        {
            foreach (string raw in File.ReadAllLines(ini))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                cfg[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
        }
        string instance = cfg["instance"], server = cfg["server"], world = cfg["world"];

        string prism = Path.Combine(root, "prismlauncher.exe");
        if (!File.Exists(prism))
            return Fail("prismlauncher.exe not found in:\n" + root);

        string gameDir = Path.Combine(root, "instances", instance, "minecraft");
        if (!Directory.Exists(gameDir))
            return Fail("Instance \"" + instance + "\" not found in:\n" + Path.Combine(root, "instances"));

        // Third-party mods are not in the zip: they are downloaded from Modrinth (official source).
        string missing = EnsureMods(root, gameDir);
        if (missing != null)
            return Fail("Some mods could not be downloaded from Modrinth. Check your connection and try again.\n\n" + missing);

        // First launch on this PC: the markers live outside the game folder, so whoever
        // receives the zip always starts fresh.
        string state = Environment.GetEnvironmentVariable("ZOMBIEEMPIRE_STATE");
        if (string.IsNullOrEmpty(state))
            state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZombieEmpire");
        string welcomeMark = Path.Combine(state, "welcome.done");
        string inventoryMark = Path.Combine(state, "inventory-" + world + ".done");
        bool welcome = !File.Exists(welcomeMark);

        string args = "--launch \"" + instance + "\"";
        bool singleplayer = false;
        try
        {
            if (server.Length > 0 && ServerOnline(server))
            {
                args += " --server \"" + server + "\"";
            }
            else if (world.Length > 0)
            {
                string worldDir = Path.Combine(gameDir, "saves", world);
                if (!File.Exists(Path.Combine(worldDir, "level.dat")))
                    return Fail("World \"" + world + "\" does not exist in saves.\nCheck the world= line in ZombieEmpire.ini.");
                bool clearInventory = !File.Exists(inventoryMark);
                PrepareWorld(worldDir, cfg["hub"], GameModeId(cfg["gamemode"]), clearInventory);
                singleplayer = true;
                args += " --world \"" + world + "\"";
                if (clearInventory) Mark(inventoryMark);
            }
            WriteModConfig(gameDir, cfg, singleplayer, welcome);
            if (welcome) Mark(welcomeMark);
        }
        catch (Exception e) { return Fail("Could not prepare the game:\n" + e.Message); }

        var psi = new ProcessStartInfo(prism, args);
        psi.WorkingDirectory = root;
        psi.UseShellExecute = false;
        Process.Start(psi);
        return 0;
    }

    // Server List Ping: true only if a real Minecraft server answers at that address.
    static bool ServerOnline(string address)
    {
        string host = address; int port = 25565;
        int colon = address.LastIndexOf(':');
        if (colon > 0) { host = address.Substring(0, colon); port = int.Parse(address.Substring(colon + 1)); }
        try
        {
            using (var tcp = new TcpClient())
            {
                var ar = tcp.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(4000) || !tcp.Connected) return false;
                tcp.EndConnect(ar);
                var s = tcp.GetStream();
                s.ReadTimeout = 4000; s.WriteTimeout = 4000;

                var hs = new MemoryStream();
                VarInt(hs, 0x00); VarInt(hs, 767);
                var h = Encoding.UTF8.GetBytes(host); VarInt(hs, h.Length); hs.Write(h, 0, h.Length);
                hs.WriteByte((byte)(port >> 8)); hs.WriteByte((byte)port);
                VarInt(hs, 1);
                var pkt = new MemoryStream(); VarInt(pkt, (int)hs.Length); hs.WriteTo(pkt);
                pkt.WriteByte(1); pkt.WriteByte(0x00); // status request
                pkt.WriteTo(s);

                int len = ReadVarInt(s), id = ReadVarInt(s), jsonLen = ReadVarInt(s);
                return len > 0 && id == 0x00 && jsonLen > 0;
            }
        }
        catch { return false; }
    }

    static void VarInt(Stream s, int v)
    {
        uint u = (uint)v;
        do { byte b = (byte)(u & 0x7F); u >>= 7; if (u != 0) b |= 0x80; s.WriteByte(b); } while (u != 0);
    }

    static int ReadVarInt(Stream s)
    {
        int v = 0, shift = 0;
        while (true)
        {
            int b = s.ReadByte();
            if (b < 0) throw new EndOfStreamException();
            v |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return v;
            shift += 7;
            if (shift > 35) throw new InvalidDataException();
        }
    }

    static int GameModeId(string mode)
    {
        switch (mode.Trim().ToLowerInvariant())
        {
            case "": case "off": return -1;
            case "survival": case "0": return 0;
            case "creative": case "1": return 1;
            case "adventure": case "2": return 2;
            case "spectator": case "3": return 3;
        }
        throw new FormatException("gamemode= must be survival, creative, adventure, spectator or off");
    }

    // mods.txt: "sha1<TAB>file<TAB>url" per line. Downloads only what is missing or doesn't match.
    // Returns null when everything is fine, otherwise the list of errors.
    static string EnsureMods(string root, string gameDir)
    {
        string manifest = Path.Combine(root, "mods.txt");
        if (!File.Exists(manifest)) return null;
        string modsDir = Path.Combine(gameDir, "mods");
        Directory.CreateDirectory(modsDir);

        var todo = new List<string[]>();
        foreach (string raw in File.ReadAllLines(manifest))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            string[] p = line.Split('\t');
            if (p.Length < 3) continue;
            string target = Path.Combine(modsDir, p[1]);
            if (!File.Exists(target) || Sha1(target) != p[0].ToLowerInvariant()) todo.Add(p);
        }
        if (todo.Count == 0) return null;

        System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072; // TLS 1.2
        var form = new Form
        {
            Text = "Zombie Empire", Width = 460, Height = 130, FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false, ControlBox = false
        };
        try { form.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        var label = new Label { Left = 15, Top = 12, Width = 415, Text = "Downloading mods from Modrinth..." };
        var bar = new ProgressBar { Left = 15, Top = 40, Width = 415, Height = 22, Maximum = todo.Count };
        form.Controls.Add(label); form.Controls.Add(bar);
        form.Show(); Application.DoEvents();

        var errors = new StringBuilder();
        {
            for (int i = 0; i < todo.Count; i++)
            {
                string[] p = todo[i];
                label.Text = "Downloading mods from Modrinth (" + (i + 1) + "/" + todo.Count + "): " + p[1];
                Application.DoEvents();
                string target = Path.Combine(modsDir, p[1]), part = target + ".part";
                try
                {
                    var done = false; Exception err = null;
                    using (var web = new System.Net.WebClient())
                    {
                        web.Headers[System.Net.HttpRequestHeader.UserAgent] = "ZombieEmpire-Launcher/1.2";
                        web.DownloadFileCompleted += (s, e) => { err = e.Error; done = true; };
                        web.DownloadFileAsync(new Uri(p[2]), part);
                        while (!done) { Application.DoEvents(); System.Threading.Thread.Sleep(15); }
                    }
                    if (err != null) throw err;
                    if (Sha1(part) != p[0].ToLowerInvariant()) throw new InvalidDataException("corrupted file (SHA-1 mismatch)");
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(part, target);
                }
                catch (Exception e)
                {
                    errors.Append(p[1]).Append(": ").Append(e.Message).Append('\n');
                    try { File.Delete(part); } catch { }
                }
                bar.Value = i + 1;
            }
        }
        form.Close();
        return errors.Length == 0 ? null : errors.ToString();
    }

    static string Sha1(string path)
    {
        using (var sha = System.Security.Cryptography.SHA1.Create())
        using (var fs = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
    }

    static void Mark(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, DateTime.Now.ToString("s"));
    }

    // Settings read by the zombieempire mod (music, welcome title, LAN).
    static void WriteModConfig(string gameDir, Dictionary<string, string> cfg, bool singleplayer, bool welcome)
    {
        string dir = Path.Combine(gameDir, "config");
        Directory.CreateDirectory(dir);
        string port = Get(cfg, "lanport", "25565");
        bool lan = singleplayer && port.Length > 0 && port.ToLowerInvariant() != "off";
        string mode = GameModeId(Get(cfg, "gamemode", "adventure")) < 0 ? "adventure" : cfg["gamemode"].Trim().ToLowerInvariant();
        var sb = new StringBuilder();
        sb.Append("# Written by ZombieEmpire.exe on every launch: edit ZombieEmpire.ini, not this file\r\n");
        sb.Append("music=").Append(Get(cfg, "music", "on").ToLowerInvariant() != "off" ? "true" : "false").Append("\r\n");
        sb.Append("welcome=").Append(welcome ? "true" : "false").Append("\r\n");
        sb.Append("welcome_title=").Append(PropEscape(Get(cfg, "welcome_title", "§4§lWelcome to Zombie Empire"))).Append("\r\n");
        sb.Append("welcome_subtitle=").Append(PropEscape(Get(cfg, "welcome_subtitle", ""))).Append("\r\n");
        sb.Append("lan=").Append(lan ? "true" : "false").Append("\r\n");
        sb.Append("lan_port=").Append(lan ? port : "25565").Append("\r\n");
        sb.Append("lan_gamemode=").Append(mode).Append("\r\n");
        sb.Append("lan_cheats=false\r\n");
        File.WriteAllText(Path.Combine(dir, "zombieempire.properties"), sb.ToString(), new UTF8Encoding(false));
        File.Delete(Path.Combine(dir, "zombieempire_lan.properties")); // config of the old Auto LAN mod
    }

    static string Get(Dictionary<string, string> cfg, string key, string def)
    {
        string v;
        return cfg.TryGetValue(key, out v) ? v : def;
    }

    static string PropEscape(string s) { return s.Replace("\\", "\\\\"); }

    // hub = "spawn" (world spawn point), "x,y,z[,yaw,pitch]" or "off".
    // gameMode = game mode id, -1 to leave it unchanged.
    // clearInventory = empties the player's inventory (level.dat and playerdata).
    static void PrepareWorld(string worldDir, string hub, int gameMode, bool clearInventory)
    {
        string levelDat = Path.Combine(worldDir, "level.dat");
        string name;
        var rootTag = Nbt.Load(levelDat, out name);
        var data = (Dictionary<string, Nbt.Tag>)((Dictionary<string, Nbt.Tag>)rootTag.Value)["Data"].Value;
        if (gameMode >= 0) data["GameType"] = new Nbt.Tag(3, gameMode);
        Nbt.Tag playerTag;
        if (data.TryGetValue("Player", out playerTag))
        {
            var player = (Dictionary<string, Nbt.Tag>)playerTag.Value;
            if (gameMode >= 0) player["playerGameType"] = new Nbt.Tag(3, gameMode);
            if (hub.Length > 0 && hub.ToLowerInvariant() != "off") MoveToHub(data, player, hub);
            if (clearInventory) ClearInventory(player);
        }
        Nbt.Save(levelDat, name, rootTag);

        string playerData = Path.Combine(worldDir, "playerdata");
        if (clearInventory && Directory.Exists(playerData))
        {
            foreach (string f in Directory.GetFiles(playerData, "*.dat"))
            {
                if (!f.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)) continue; // GetFiles("*.dat") also matches .dat_old
                string n;
                var p = Nbt.Load(f, out n);
                ClearInventory((Dictionary<string, Nbt.Tag>)p.Value);
                Nbt.Save(f, n, p);
            }
        }
    }

    static void ClearInventory(Dictionary<string, Nbt.Tag> player)
    {
        player["Inventory"] = Nbt.List(10, new object[0]); // includes armor and offhand
        player["SelectedItemSlot"] = new Nbt.Tag(3, 0);
        player.Remove("SelectedItem");
    }

    static void MoveToHub(Dictionary<string, Nbt.Tag> data, Dictionary<string, Nbt.Tag> player, string hub)
    {
        double x, y, z; float yaw, pitch = 0;
        if (hub.ToLowerInvariant() == "spawn")
        {
            x = (int)data["SpawnX"].Value + 0.5;
            y = (int)data["SpawnY"].Value;
            z = (int)data["SpawnZ"].Value + 0.5;
            yaw = data.ContainsKey("SpawnAngle") ? (float)data["SpawnAngle"].Value : 0f;
        }
        else
        {
            string[] p = hub.Split(',');
            if (p.Length < 3) throw new FormatException("hub= must be \"spawn\", \"off\" or x,y,z[,yaw,pitch]");
            x = Num(p[0]); y = Num(p[1]); z = Num(p[2]);
            yaw = p.Length > 3 ? (float)Num(p[3]) : 0f;
            pitch = p.Length > 4 ? (float)Num(p[4]) : 0f;
        }

        player["Pos"] = Nbt.List(6, new object[] { x, y, z });
        player["Rotation"] = Nbt.List(5, new object[] { yaw, pitch });
        player["Motion"] = Nbt.List(6, new object[] { 0.0, 0.0, 0.0 });
        player["FallDistance"] = new Nbt.Tag(5, 0f);
        player["Dimension"] = new Nbt.Tag(8, "minecraft:overworld");
        player.Remove("RootVehicle");
    }

    static double Num(string s) { return double.Parse(s.Trim(), CultureInfo.InvariantCulture); }

    static int Fail(string msg)
    {
        MessageBox.Show(msg, "Zombie Empire", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return 1;
    }
}

// Minimal NBT (gzip, big-endian) that keeps tag types and order.
static class Nbt
{
    public class Tag
    {
        public byte Type; public object Value;
        public Tag(byte t, object v) { Type = t; Value = v; }
    }
    public class ListValue { public byte ElemType; public List<object> Items = new List<object>(); }

    public static Tag List(byte elemType, object[] items)
    {
        var l = new ListValue { ElemType = elemType };
        l.Items.AddRange(items);
        return new Tag(9, l);
    }

    public static Tag Load(string path, out string name)
    {
        byte[] raw;
        using (var fs = File.OpenRead(path))
        using (var gz = new GZipStream(fs, CompressionMode.Decompress))
        using (var ms = new MemoryStream()) { gz.CopyTo(ms); raw = ms.ToArray(); }
        var r = new BinaryReader(new MemoryStream(raw));
        byte t = r.ReadByte();
        name = Str(r);
        return new Tag(t, Read(r, t));
    }

    public static void Save(string path, string name, Tag root)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(root.Type); Str(w, name); Write(w, root.Type, root.Value);
        string tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        using (var gz = new GZipStream(fs, CompressionMode.Compress)) { var b = ms.ToArray(); gz.Write(b, 0, b.Length); }
        File.Copy(path, path + "_old", true);
        File.Copy(tmp, path, true);
        File.Delete(tmp);
    }

    static byte[] Be(BinaryReader r, int n) { var b = r.ReadBytes(n); if (BitConverter.IsLittleEndian) Array.Reverse(b); return b; }
    static void Be(BinaryWriter w, byte[] b) { if (BitConverter.IsLittleEndian) Array.Reverse(b); w.Write(b); }
    static string Str(BinaryReader r) { int n = BitConverter.ToUInt16(Be(r, 2), 0); return Encoding.UTF8.GetString(r.ReadBytes(n)); }
    static void Str(BinaryWriter w, string s) { var b = Encoding.UTF8.GetBytes(s); Be(w, BitConverter.GetBytes((ushort)b.Length)); w.Write(b); }

    static object Read(BinaryReader r, byte t)
    {
        switch (t)
        {
            case 1: return r.ReadSByte();
            case 2: return BitConverter.ToInt16(Be(r, 2), 0);
            case 3: return BitConverter.ToInt32(Be(r, 4), 0);
            case 4: return BitConverter.ToInt64(Be(r, 8), 0);
            case 5: return BitConverter.ToSingle(Be(r, 4), 0);
            case 6: return BitConverter.ToDouble(Be(r, 8), 0);
            case 7: return r.ReadBytes(BitConverter.ToInt32(Be(r, 4), 0));
            case 8: return Str(r);
            case 9:
            {
                var l = new ListValue { ElemType = r.ReadByte() };
                int n = BitConverter.ToInt32(Be(r, 4), 0);
                for (int i = 0; i < n; i++) l.Items.Add(Read(r, l.ElemType));
                return l;
            }
            case 10:
            {
                var d = new Dictionary<string, Tag>();
                while (true)
                {
                    byte tt = r.ReadByte();
                    if (tt == 0) return d;
                    string k = Str(r);
                    d[k] = new Tag(tt, Read(r, tt));
                }
            }
            case 11:
            {
                int n = BitConverter.ToInt32(Be(r, 4), 0); var a = new int[n];
                for (int i = 0; i < n; i++) a[i] = BitConverter.ToInt32(Be(r, 4), 0);
                return a;
            }
            case 12:
            {
                int n = BitConverter.ToInt32(Be(r, 4), 0); var a = new long[n];
                for (int i = 0; i < n; i++) a[i] = BitConverter.ToInt64(Be(r, 8), 0);
                return a;
            }
        }
        throw new InvalidDataException("Tag NBT sconosciuto: " + t);
    }

    static void Write(BinaryWriter w, byte t, object v)
    {
        switch (t)
        {
            case 1: w.Write((sbyte)v); break;
            case 2: Be(w, BitConverter.GetBytes((short)v)); break;
            case 3: Be(w, BitConverter.GetBytes((int)v)); break;
            case 4: Be(w, BitConverter.GetBytes((long)v)); break;
            case 5: Be(w, BitConverter.GetBytes((float)v)); break;
            case 6: Be(w, BitConverter.GetBytes((double)v)); break;
            case 7: { var b = (byte[])v; Be(w, BitConverter.GetBytes(b.Length)); w.Write(b); break; }
            case 8: Str(w, (string)v); break;
            case 9:
            {
                var l = (ListValue)v; w.Write(l.ElemType); Be(w, BitConverter.GetBytes(l.Items.Count));
                foreach (var i in l.Items) Write(w, l.ElemType, i);
                break;
            }
            case 10:
            {
                foreach (var kv in (Dictionary<string, Tag>)v) { w.Write(kv.Value.Type); Str(w, kv.Key); Write(w, kv.Value.Type, kv.Value.Value); }
                w.Write((byte)0);
                break;
            }
            case 11: { var a = (int[])v; Be(w, BitConverter.GetBytes(a.Length)); foreach (var i in a) Be(w, BitConverter.GetBytes(i)); break; }
            case 12: { var a = (long[])v; Be(w, BitConverter.GetBytes(a.Length)); foreach (var i in a) Be(w, BitConverter.GetBytes(i)); break; }
            default: throw new InvalidDataException("Tag NBT sconosciuto: " + t);
        }
    }
}
