// DSH 隔离版启动器（C# / .NET Framework 4.x）
// 双击本 exe = 读取 launcher.ini -> 准备隔离 home -> 直接拉起桌面版主程序。
// 不再经过 cmd / wscript / 快捷方式：只要本 exe 的完整性标签是 Medium，
// 子进程就继承 Medium，Electron 可以正常启动。

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

internal static class Program
{
    private const string IniFile = "launcher.ini";

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string iniPath = Path.Combine(baseDir, IniFile);
        string logPath = Path.Combine(baseDir, "launcher.log");

        string appDir = @"..\DeepSeekHarness桌面版";
        string home = "home";
        string userData = "electron-data";
        string workspace = @"..\dsh-workspace";
        string extraArgs = "";

        try
        {
            if (File.Exists(iniPath))
            {
                foreach (string raw in File.ReadAllLines(iniPath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string value = line.Substring(eq + 1).Trim();
                    if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                        value = value.Substring(1, value.Length - 2);
                    if (key == "appdir") appDir = value;
                    else if (key == "home") home = value;
                    else if (key == "userdata") userData = value;
                    else if (key == "workspace") workspace = value;
                    else if (key == "extraargs") extraArgs = value;
                }
            }
        }
        catch (Exception ex)
        {
            return Fail("读取 launcher.ini 失败：\n" + ex.Message);
        }

        appDir = FullPath(baseDir, appDir);
        home = FullPath(baseDir, home);
        workspace = FullPath(baseDir, workspace);
        bool sharedUserData = userData.Trim().Length == 0 || userData.Trim() == "-";
        if (!sharedUserData) userData = FullPath(baseDir, userData);

        string exePath = Path.Combine(appDir, "DeepSeek Harness.exe");
        if (!File.Exists(exePath))
        {
            return Fail("找不到桌面版主程序：\n" + exePath + "\n\n请检查 launcher.ini 里的 AppDir 配置。");
        }

        try
        {
            Directory.CreateDirectory(home);
            Directory.CreateDirectory(workspace);
            if (!sharedUserData) Directory.CreateDirectory(userData);
        }
        catch (Exception ex)
        {
            return Fail("创建隔离目录失败：\n" + ex.Message);
        }

        RepairHome(home);
        if (!sharedUserData) CleanStaleLock(userData);

        if (sharedUserData)
        {
            string mainUserData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "@deepseek-ai", "dsh-desktop");
            if (IsLocked(mainUserData))
            {
                Log(logPath, "blocked: main instance is running");
                return Fail("主版 DSH 正在运行。\n\n请先从托盘完全退出主版，再启动隔离版。\n\n隔离版与主版共用 Electron 数据目录，需要串行使用。");
            }
        }

        try
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = exePath;
            info.WorkingDirectory = appDir;
            info.UseShellExecute = false;

            StringBuilder arguments = new StringBuilder();
            if (!sharedUserData) arguments.Append("--user-data-dir=\"").Append(userData).Append("\"");
            if (extraArgs.Length > 0)
            {
                if (arguments.Length > 0) arguments.Append(' ');
                arguments.Append(extraArgs);
            }
            info.Arguments = arguments.ToString();

            info.EnvironmentVariables["DSH_HOME"] = home;
            // 只删除，不要写空值：Electron 判断的是变量是否存在，空值仍会被当成 node 模式。
            info.EnvironmentVariables.Remove("ELECTRON_RUN_AS_NODE");
            info.EnvironmentVariables.Remove("NODE_OPTIONS");
            info.EnvironmentVariables.Remove("NODE_PATH");

            Process child = Process.Start(info);
            if (child == null) return Fail("无法启动 DeepSeek Harness。");

            Log(logPath, "started pid=" + child.Id + " home=" + home + " userData=" + (sharedUserData ? "<shared>" : userData));

            // 启动初期就退出的情况基本只有两种：单实例被占，或 exe 带 Low 完整性标签被降权。
            if (child.WaitForExit(3000))
            {
                int code = child.ExitCode;
                Log(logPath, "EX exited code=" + code);
                if (code != 0)
                {
                    return Fail("DeepSeek Harness 启动后立即退出，退出码 " + code + "（0x" + code.ToString("X8") + "）。\n\n"
                        + "如果退出码是 -2147483645 (0x80000003)，通常是本 exe 所在的目录带有 Low 完整性标签，\n"
                        + "导致 Electron 被降权启动。请在普通 PowerShell 中执行：\n\n"
                        + "icacls \"" + baseDir.TrimEnd(Path.DirectorySeparatorChar) + "\" /setintegritylevel (OI)(CI)Medium /T /C");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Log(logPath, "EX " + ex.Message);
            return Fail("启动 DeepSeek Harness 失败：\n" + ex.Message);
        }
    }

    private static string FullPath(string baseDir, string value)
    {
        string expanded = Environment.ExpandEnvironmentVariables(value);
        string combined = Path.IsPathRooted(expanded) ? expanded : Path.Combine(baseDir, expanded);
        return Path.GetFullPath(combined);
    }

    private static void Log(string path, string message)
    {
        try
        {
            File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + message + "\r\n", Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static void RepairHome(string home)
    {
        try
        {
            string profile = Path.Combine(home, "profiles", "desktop");
            if (!Directory.Exists(profile)) return;
            // 只在完全为空时清理；已经有 cordis.yml/package.json 的半成品不能删。
            if (Directory.GetFileSystemEntries(profile).Length != 0) return;
            Directory.Delete(profile, true);
        }
        catch
        {
        }
    }

    private static void CleanStaleLock(string userDataDir)
    {
        try
        {
            string lockFile = Path.Combine(userDataDir, "lockfile");
            if (!File.Exists(lockFile)) return;
            try
            {
                using (FileStream stream = new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    stream.Close();
                }
            }
            catch (IOException)
            {
                return;
            }
            File.Delete(lockFile);
        }
        catch
        {
        }
    }

    private static bool IsLocked(string userDataDir)
    {
        string lockFile = Path.Combine(userDataDir, "lockfile");
        if (!File.Exists(lockFile)) return false;
        try
        {
            using (FileStream stream = new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                stream.Close();
            }
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static int Fail(string message)
    {
        MessageBox.Show(message, "DSH 隔离版", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return 1;
    }
}
