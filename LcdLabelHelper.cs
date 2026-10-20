// LcdLabelHelper - restores SpacePilot Pro LCD button labels under 3DxWare 10.8 and later.
//
// 3DxService 17.8 (3DxWare 10.8.5) stopped writing <Text> inside each <Button> of
// %LOCALAPPDATA%\3Dconnexion\3DxWare\Cfg\LCDCfg.xml, which the LCD applet
// (3DxLCD10SPPRO.exe) needs to draw the Function Keys screen. This program watches that
// file and, whenever the service rewrites it, resolves each button's assigned action from
// the active profile (as reported in 3DxServiceState.xml) and inserts the label.
//
// Usage (the exe is named LcdLabelHelper-<version>.exe):
//   LcdLabelHelper.exe          run in the background (single instance)
//   LcdLabelHelper.exe --once   patch LCDCfg.xml once and print the resolved labels
//   LcdLabelHelper.exe --exit   stop a running background instance

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;

[assembly: AssemblyTitle("LcdLabelHelper")]
[assembly: AssemblyDescription("Restores SpacePilot Pro LCD button labels under 3DxWare 10.8 and later")]
[assembly: AssemblyProduct("LcdLabelHelper")]
[assembly: AssemblyCompany("Bassquake")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Bassquake. MIT License.")]
[assembly: AssemblyVersion(LcdLabelHelper.Version + ".0.0")]
[assembly: AssemblyFileVersion(LcdLabelHelper.Version + ".0.0")]
[assembly: AssemblyInformationalVersion(LcdLabelHelper.Version)]

static class LcdLabelHelper
{
    // Release version (major.minor). build.cmd reads this line to name the exe.
    public const string Version = "1.0";

    const string MutexName = @"Local\LcdLabelHelper_3Dx";
    const string ExitEventName = @"Local\LcdLabelHelper_3Dx_Exit";

    static readonly string LocalDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"3Dconnexion\3DxWare");
    static readonly string LcdCfgPath = Path.Combine(LocalDir, @"Cfg\LCDCfg.xml");
    static readonly string StatePath = Path.Combine(LocalDir, "3DxServiceState.xml");
    static readonly string LogPath = Path.Combine(LocalDir, "LcdLabelHelper.log");

    static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);
    static bool verbose;

    [STAThread]
    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";

        if (mode == "--exit")
        {
            EventWaitHandle ev;
            if (EventWaitHandle.TryOpenExisting(ExitEventName, out ev)) { ev.Set(); return 0; }
            return 1;
        }

        if (mode == "--once")
        {
            verbose = true;
            AttachConsole(-1);
            Out("LcdLabelHelper v" + Version);
            return PatchOnce(true) ? 0 : 1;
        }

        bool created;
        using (var mutex = new Mutex(true, MutexName, out created))
        {
            if (!created) return 0; // already running
            using (var exitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ExitEventName))
            {
                Log("Started v" + Version);
                DateTime lastSeen = DateTime.MinValue;
                while (!exitEvent.WaitOne(500))
                {
                    try
                    {
                        if (!File.Exists(LcdCfgPath)) continue;
                        DateTime t = File.GetLastWriteTimeUtc(LcdCfgPath);
                        if (t == lastSeen) continue;
                        // Let the service finish writing before we read it.
                        Thread.Sleep(150);
                        PatchOnce(false);
                        lastSeen = File.GetLastWriteTimeUtc(LcdCfgPath);
                    }
                    catch (Exception e)
                    {
                        Log("Error: " + e.Message);
                    }
                }
                Log("Exiting");
            }
        }
        return 0;
    }

    // Only the SpacePilot Pro's hardware LCD is drawn from these labels. Other devices in
    // LCDCfg.xml (e.g. the SpaceMouse Enterprise, read by 3DxVirtualLCD) are left untouched.
    static readonly string[] LcdProductIds = { "c629" };

    static readonly Regex DeviceRe = new Regex(@"<Device>[\s\S]*?</Device>");
    static readonly Regex ButtonRe = new Regex(@"<Button>(?<body>[\s\S]*?)</Button>");

    // Returns true when every SpacePilot Pro button is (now) labelled.
    static bool PatchOnce(bool print)
    {
        string text = ReadShared(LcdCfgPath);
        if (text == null) { Out("LCDCfg.xml not readable"); return false; }

        bool ok = true, sawDevice = false;
        int added = 0;
        string patched = DeviceRe.Replace(text, delegate (Match d)
        {
            Match pid = Regex.Match(d.Value, @"<ProductID>([0-9a-fA-F]+)</ProductID>");
            if (!pid.Success || Array.IndexOf(LcdProductIds, pid.Groups[1].Value.ToLowerInvariant()) < 0) return d.Value;
            sawDevice = true;
            int n;
            string result = PatchDevice(d.Value, "ID_ProductID_" + pid.Groups[1].Value.ToUpperInvariant(), print, out n);
            if (result == null) { ok = false; return d.Value; }
            added += n;
            return result;
        });

        if (!sawDevice) { if (print) Out("No SpacePilot Pro section in LCDCfg.xml"); return true; }
        if (added == 0) return ok;
        if (!WriteShared(LcdCfgPath, patched)) { Out("Could not write LCDCfg.xml"); return false; }
        if (print) Out("Added " + added + " labels to " + LcdCfgPath);
        return ok;
    }

    // Labels one <Device> block. Returns the new block, or null if the device's profile could not be resolved.
    static string PatchDevice(string block, string deviceId, bool print, out int added)
    {
        added = 0;
        bool needsWork = false;
        foreach (Match m in ButtonRe.Matches(block))
            if (m.Groups["body"].Value.IndexOf("<Text>", StringComparison.Ordinal) < 0) { needsWork = true; break; }
        if (!needsWork && !print) return block;

        var resolver = new LabelResolver(deviceId);
        if (!resolver.Load(StatePath)) { Out("Could not resolve the active profile for " + deviceId); return null; }
        if (print)
        {
            Out(deviceId + "   cfg dir: " + resolver.InstallCfgDir);
            Out("Profile chain: " + string.Join(" -> ", resolver.ChainDescription()) + "   bank: " + resolver.Bank);
        }

        int count = 0;
        string patched = ButtonRe.Replace(block, delegate (Match m)
        {
            string body = m.Groups["body"].Value;
            Match v = Regex.Match(body, @"(\r?\n)(\s*)<V3DK>([^<]+)</V3DK>");
            if (!v.Success) return m.Value;
            string v3dk = v.Groups[3].Value.Trim();
            string actionId;
            string label = resolver.Resolve(v3dk, out actionId);
            if (print) Out(string.Format("  {0,-14} -> {1,-32} \"{2}\"", v3dk, actionId ?? "(unassigned)", label));
            if (body.IndexOf("<Text>", StringComparison.Ordinal) >= 0 || string.IsNullOrEmpty(label)) return m.Value;
            count++;
            string insert = v.Groups[1].Value + v.Groups[2].Value + "<Text>" + EscapeXml(label) + "</Text>";
            int at = v.Index + v.Length;
            return "<Button>" + body.Substring(0, at) + insert + body.Substring(at) + "</Button>";
        });

        added = count;
        if (count > 0)
            Log(string.Format("Added {0} labels ({1}, profile {2}, bank {3})", count, deviceId, resolver.ProfileId, resolver.Bank));
        return patched;
    }

    static string ReadShared(string path)
    {
        for (int i = 0; i < 20; i++)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                    return sr.ReadToEnd();
            }
            catch (IOException) { Thread.Sleep(50); }
        }
        return null;
    }

    static bool WriteShared(string path, string content)
    {
        for (int i = 0; i < 20; i++)
        {
            try
            {
                File.WriteAllText(path, content, Utf8Bom);
                return true;
            }
            catch (IOException) { Thread.Sleep(50); }
            catch (UnauthorizedAccessException) { Thread.Sleep(50); }
        }
        return false;
    }

    static string EscapeXml(string s)
    {
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    static void Out(string s)
    {
        if (verbose) Console.WriteLine(s);
        else Log(s);
    }

    static void Log(string s)
    {
        try
        {
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 256 * 1024) File.Delete(LogPath);
            File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + s + Environment.NewLine);
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool AttachConsole(int pid);
}

// Resolves a V3DK button to its action label the way 3DxService does: walk the profile
// chain (user app cfg -> app default cfg -> ID_Default_Cfg [-> ID_Shared_Cfg in 10.9] -> ID_Base_Cfg)
// and, in each, the device chain (ID_ProductID_xxxx -> ID_Standard_3D_Mouse).
class LabelResolver
{
    static readonly string IsvCfgDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"3Dconnexion\3DxWare\Cfg");
    static readonly string UserCfgDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"3Dconnexion\3DxWare\Cfg");

    readonly string deviceId;
    readonly List<XmlDocument> chain = new List<XmlDocument>();
    readonly List<string> chainIds = new List<string>();
    readonly List<string> deviceChain = new List<string>();
    // Every cfg with a given ID, highest priority first (user, ISV, install). A user cfg that
    // inherits from its own ID is an overlay on the next layer down.
    readonly Dictionary<string, List<XmlDocument>> cfgLayers = new Dictionary<string, List<XmlDocument>>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> strings = new Dictionary<string, string>(StringComparer.Ordinal);
    XmlDocument globalCfg;
    string installCfgDir;

    public string ProfileId { get; private set; }
    public string Bank { get; private set; }

    public LabelResolver(string deviceId) { this.deviceId = deviceId; }

    public IEnumerable<string> ChainDescription() { return chainIds; }

    public bool Load(string statePath)
    {
        XmlDocument state = LoadXml(statePath);
        if (state == null) return false;
        XmlNode dev = state.SelectSingleNode("/DriverState/DeviceTypeList/Device[ID='" + deviceId + "']");
        if (dev == null) return false;

        ProfileId = Text(dev, "CfgProperties/ID");
        string userFile = Text(dev, "AppInfo/CreateCfgInfo/Filename");
        Bank = Text(dev, "AppInfo/CurrentButtonBank");
        if (string.IsNullOrEmpty(Bank)) Bank = "Default";
        string culture = Text(state, "/DriverState/Global/Settings/Culture");
        installCfgDir = FindInstallCfgDir(state);
        if (installCfgDir == null) return false;

        // Index every cfg by ID, scanning the highest-priority directory first.
        foreach (string dir in new[] { UserCfgDir, IsvCfgDir, installCfgDir })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (string f in Directory.GetFiles(dir, "*.xml"))
            {
                XmlDocument d = LoadXml(f);
                if (d == null || d.DocumentElement == null) continue;
                string root = d.DocumentElement.Name;
                if (root == "Global") { if (globalCfg == null) globalCfg = d; continue; }
                // Profile roots vary by version (AppCfg, BaseCfg; 10.9 adds AppDefCfg and Cfg), so skip only non-profiles.
                if (root == "LocalizationTables" || root == "AppCfgTemplate") continue;
                string id = Text(d, "/*/CfgProperties/ID");
                if (string.IsNullOrEmpty(id)) continue;
                List<XmlDocument> layers;
                if (!cfgLayers.TryGetValue(id, out layers)) cfgLayers[id] = layers = new List<XmlDocument>();
                layers.Add(d);
            }
        }

        // Start from the per-app user cfg when it exists, otherwise from the active profile,
        // always at its top layer so a user overlay is applied even if the state names the installed file.
        string cur = null;
        XmlDocument userCfg = string.IsNullOrEmpty(userFile) ? null : LoadXml(userFile);
        if (userCfg != null) cur = Text(userCfg, "/*/CfgProperties/ID");
        if (cur == null || !cfgLayers.ContainsKey(cur)) cur = ProfileId;
        if (cur == null || !cfgLayers.ContainsKey(cur)) return false;
        int layer = 0;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (cur != null && cfgLayers.ContainsKey(cur) && layer < cfgLayers[cur].Count)
        {
            if (!seen.Add(cur + "#" + layer)) break;
            XmlDocument doc = cfgLayers[cur][layer];
            chain.Add(doc);
            chainIds.Add(layer == cfgLayers[cur].Count - 1 ? cur : cur + " (user)");
            string parent = Text(doc, "/*/CfgProperties/InheritsFromID");
            if (string.IsNullOrEmpty(parent)) break;
            if (string.Equals(parent, cur, StringComparison.OrdinalIgnoreCase)) layer++;
            else { cur = parent; layer = 0; }
        }

        // Device inheritance chain, from whichever cfg defines the device (normally Base.xml).
        string d2 = deviceId;
        var seenDev = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!string.IsNullOrEmpty(d2) && seenDev.Add(d2))
        {
            deviceChain.Add(d2);
            string next = null;
            foreach (XmlDocument c in chain)
            {
                next = Text(c, "/*/Devices/Device[ID='" + d2 + "']/InheritsFromID");
                if (!string.IsNullOrEmpty(next)) break;
            }
            d2 = next;
        }

        LoadStrings(culture);
        return true;
    }

    public string InstallCfgDir { get { return installCfgDir; } }

    // 3DxWinCore's own Cfg folder: as reported by the service, else the registry, else the default location.
    static string FindInstallCfgDir(XmlDocument state)
    {
        var candidates = new List<string>();
        candidates.Add(Text(state, "/DriverState/_3DxWinCoreInfo/Dir"));
        candidates.Add(Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\3Dconnexion\3DxWare", "Home Directory", null) as string);
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"3Dconnexion\3DxWare\3DxWinCore"));
        foreach (string home in candidates)
        {
            if (string.IsNullOrEmpty(home)) continue;
            try
            {
                string cfg = Path.Combine(home.Trim(), "Cfg");
                if (File.Exists(Path.Combine(cfg, "Base.xml"))) return cfg;
            }
            catch (ArgumentException) { } // not a path (e.g. an error message in the state file)
        }
        return null;
    }

    // Customised buttons are stored by physical ID (HIDButton_n); the device's ButtonInfo maps those to V3DK IDs.
    List<string> InputIds(string v3dk)
    {
        var ids = new List<string> { v3dk };
        foreach (XmlDocument cfg in chain)
            foreach (string dev in deviceChain)
                foreach (XmlNode b in cfg.SelectNodes("/*/Devices/Device[ID='" + dev + "']/ButtonInfo/Button[V3DKID='" + v3dk + "']/ButtonID"))
                {
                    string id = b.InnerText.Trim();
                    if (id.Length > 0 && !ids.Contains(id)) ids.Add(id);
                }
        return ids;
    }

    public string Resolve(string v3dk, out string actionId)
    {
        actionId = null;
        List<string> inputs = InputIds(v3dk);
        var match = new StringBuilder();
        foreach (string id in inputs) match.Append(match.Length == 0 ? "" : " or ").Append("Input/ActionID='").Append(id).Append("'");
        foreach (XmlDocument cfg in chain)
        {
            foreach (string dev in deviceChain)
            {
                XmlNode device = cfg.SelectSingleNode("/*/Devices/Device[ID='" + dev + "']");
                if (device == null) continue;
                foreach (XmlNode bank in BankCandidates(device))
                {
                    XmlNode btn = bank.SelectSingleNode("Button[" + match + "]");
                    if (btn == null) continue;
                    string output = Text(btn, "Output/ActionID");
                    // 10.9 shared banks list unshared buttons as Driver_DevNull: defer to the app's own assignment.
                    if (output == "Driver_DevNull" && IsShared(bank)) continue;
                    actionId = output;
                    string own = Text(btn, "Output/Name") ?? Text(btn, "Name");
                    if (!string.IsNullOrEmpty(own)) return Localize(own);
                    return ActionLabel(actionId);
                }
            }
        }
        return null;
    }

    // The current bank first (by ID, else the non-shared default bank), then any shared bank (10.9+).
    IEnumerable<XmlNode> BankCandidates(XmlNode device)
    {
        XmlNode bank = device.SelectSingleNode("ButtonBank[ID='" + Bank + "' and not(@Shared='true')]");
        if (bank == null && Bank == "Default") bank = device.SelectSingleNode("ButtonBank[@Default='true' and not(@Shared='true')]");
        if (bank != null) yield return bank;
        foreach (XmlNode shared in device.SelectNodes("ButtonBank[@Shared='true']"))
            yield return shared;
    }

    static bool IsShared(XmlNode bank)
    {
        XmlAttribute a = bank.Attributes == null ? null : bank.Attributes["Shared"];
        return a != null && a.Value == "true";
    }

    string ActionLabel(string actionId)
    {
        if (string.IsNullOrEmpty(actionId)) return null;
        // Bare <ButtonAction><ID/></ButtonAction> entries are GUI category references, not definitions.
        string q = "//ButtonAction[ID='" + actionId + "'][Name]";
        foreach (XmlDocument cfg in chain)
        {
            XmlNode a = cfg.SelectSingleNode(q);
            if (a != null) return Localize(Text(a, "Name"));
        }
        if (globalCfg != null)
        {
            XmlNode a = globalCfg.SelectSingleNode(q);
            if (a != null) return Localize(Text(a, "Name"));
        }
        return null;
    }

    string Localize(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        string v;
        if (strings.TryGetValue(key, out v)) return v;
        return key.StartsWith("STR_", StringComparison.Ordinal) ? null : key;
    }

    void LoadStrings(string culture)
    {
        // Same fallback as the driver: exact culture, then its base language, then en-US.
        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(culture))
        {
            candidates.Add(culture);
            int dash = culture.IndexOf('-');
            if (dash > 0)
                foreach (string f in Directory.GetFiles(installCfgDir, culture.Substring(0, dash) + "-*.xml"))
                    candidates.Add(Path.GetFileNameWithoutExtension(f));
        }
        candidates.Add("en-US");
        foreach (string c in candidates)
        {
            XmlDocument d = LoadXml(Path.Combine(installCfgDir, c + ".xml"));
            if (d == null) continue;
            foreach (XmlNode e in d.SelectNodes("//StringEntry"))
            {
                string k = Text(e, "Key"), val = Text(e, "Value");
                if (k != null && val != null && !strings.ContainsKey(k)) strings[k] = val;
            }
            return;
        }
    }

    static XmlDocument LoadXml(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            var d = new XmlDocument();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                d.Load(fs);
            StripNamespaces(d);
            return d;
        }
        catch { return null; }
    }

    // Some cfgs declare a default namespace; drop it so plain XPath works everywhere.
    static void StripNamespaces(XmlDocument d)
    {
        if (d.DocumentElement == null || string.IsNullOrEmpty(d.DocumentElement.NamespaceURI)) return;
        string xml = Regex.Replace(d.OuterXml, @"\sxmlns=""[^""]*""", "");
        d.LoadXml(xml);
    }

    static string Text(XmlNode n, string xpath)
    {
        XmlNode x = n.SelectSingleNode(xpath);
        if (x == null) return null;
        string t = x.InnerText.Trim();
        return t.Length == 0 ? null : t;
    }
}
