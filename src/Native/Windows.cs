using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace SourceGit.Native
{
    [SupportedOSPlatform("windows")]
    internal class Windows : OS.IBackend
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern bool PathFindOnPath([In, Out] StringBuilder pszFile, [In] string[] ppszOtherDirs);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern IntPtr ILCreateFromPathW(string pszPath);

        [DllImport("shell32.dll", SetLastError = false)]
        private static extern void ILFree(IntPtr pidl);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, int cild, IntPtr apidl, int dwFlags);

        public void SetupApp(AppBuilder builder)
        {
            // Do nothing for now.
        }

        public void SetupWindow(Window window)
        {
            window.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
            window.ExtendClientAreaToDecorationsHint = true;
            window.BorderThickness = new Thickness(1);
            window.Padding = new Thickness(0);
        }

        public string GetDataDir()
        {
            var execFile = Environment.ProcessPath;
            var portableDir = Path.Combine(Path.GetDirectoryName(execFile)!, "data");
            if (Directory.Exists(portableDir))
                return portableDir;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SourceGit");
        }

        public string FindGitExecutable()
        {
            var reg = Microsoft.Win32.RegistryKey.OpenBaseKey(
                Microsoft.Win32.RegistryHive.LocalMachine,
                Microsoft.Win32.RegistryView.Registry64);

            var git = reg.OpenSubKey(@"SOFTWARE\GitForWindows");
            if (git?.GetValue("InstallPath") is string installPath)
                return Path.Combine(installPath, "bin", "git.exe");

            var builder = new StringBuilder("git.exe", 259);
            if (!PathFindOnPath(builder, null))
                return null;

            var exePath = builder.ToString();
            if (!string.IsNullOrEmpty(exePath))
                return exePath;

            return null;
        }

        public string FindTerminal(Models.ShellOrTerminal shell)
        {
            switch (shell.Type)
            {
                case "git-bash":
                    if (string.IsNullOrEmpty(OS.GitExecutable))
                        break;

                    var binDir = Path.GetDirectoryName(OS.GitExecutable)!;
                    var bash = Path.GetFullPath(Path.Combine(binDir, "..", "git-bash.exe"));
                    if (!File.Exists(bash))
                        break;

                    return bash;
                case "pwsh":
                    var localMachine = Microsoft.Win32.RegistryKey.OpenBaseKey(
                            Microsoft.Win32.RegistryHive.LocalMachine,
                            Microsoft.Win32.RegistryView.Registry64);

                    var pwsh = localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\pwsh.exe");
                    if (pwsh != null)
                    {
                        var path = pwsh.GetValue(null) as string;
                        if (File.Exists(path))
                            return path;
                    }

                    var pwshFinder = new StringBuilder("powershell.exe", 512);
                    if (PathFindOnPath(pwshFinder, null))
                        return pwshFinder.ToString();

                    break;
                case "cmd":
                    return @"C:\Windows\System32\cmd.exe";
                case "wt":
                    var wtFinder = new StringBuilder("wt.exe", 512);
                    if (PathFindOnPath(wtFinder, null))
                        return wtFinder.ToString();

                    break;
            }

            return string.Empty;
        }

        public List<Models.ExternalTool> FindExternalTools()
        {
            var localAppDataDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var finder = new Models.ExternalToolsFinder();
            finder.VSCode(FindVSCode);
            finder.VSCodeInsiders(FindVSCodeInsiders);
            finder.VSCodium(FindVSCodium);
            finder.Cursor(() => Path.Combine(localAppDataDir, @"Programs\Cursor\Cursor.exe"));
            finder.FindJetBrainsFromToolbox(() => Path.Combine(localAppDataDir, @"JetBrains\Toolbox"));
            finder.SublimeText(FindSublimeText);
            finder.Zed(FindZed);
            FindVisualStudio(finder);
            return finder.Tools;
        }

        public void OpenBrowser(string url)
        {
            var info = new ProcessStartInfo(url);
            info.UseShellExecute = true;
            info.CreateNoWindow = true;
            Process.Start(info);
        }

        public void OpenTerminal(string workdir, string args)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var cwd = string.IsNullOrEmpty(workdir) ? home : workdir;
            var terminal = OS.ShellOrTerminal;

            if (!File.Exists(terminal))
            {
                Models.Notification.Send(workdir, "Terminal is not specified! Please confirm that the correct shell/terminal has been configured.", true);
                return;
            }

            var startInfo = new ProcessStartInfo();
            startInfo.WorkingDirectory = cwd;
            startInfo.FileName = terminal;
            startInfo.Arguments = args;
            Process.Start(startInfo);
        }

        public void OpenInFileManager(string path)
        {
            if (File.Exists(path))
            {
                var pidl = ILCreateFromPathW(new FileInfo(path).FullName);

                try
                {
                    SHOpenFolderAndSelectItems(pidl, 0, 0, 0);
                }
                finally
                {
                    ILFree(pidl);
                }

                return;
            }

            var dir = new DirectoryInfo(path).FullName + Path.DirectorySeparatorChar;
            Process.Start(new ProcessStartInfo(dir)
            {
                UseShellExecute = true,
                CreateNoWindow = true,
            });
        }

        public void LaunchDetachedGui(string[] args)
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                return;

            var startInfo = new ProcessStartInfo(exe);
            foreach (var a in args)
                startInfo.ArgumentList.Add(a);
            startInfo.UseShellExecute = true;
            startInfo.CreateNoWindow = false;

            try
            {
                Process.Start(startInfo);
            }
            catch (Exception e)
            {
                Models.Notification.Send("", $"Failed to relaunch SourceGit. Reason: {e.Message}", true);
            }
        }

        private static readonly string CliBinDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SourceGit", "bin");
        private static readonly string CliWrapperPath = Path.Combine(CliBinDir, "sourcegit.cmd");

        public CliLinkStatus GetCliLinkStatus()
        {
            return File.Exists(CliWrapperPath)
                ? new CliLinkStatus { Installed = true, Target = CliWrapperPath }
                : new CliLinkStatus { Installed = false };
        }

        public bool TryCreateCliLink(out string error)
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                error = "Cannot determine the current executable path.";
                return false;
            }

            // Forward all args to the SourceGit GUI executable. The exe is a WinExe subsystem app so
            // invoking it does not pop a console; the single-arg case takes the existing CLI shim path
            // (App.TryLaunchAsCliShim) and returns promptly. Base64-encode the content so neither the
            // exe path nor special characters can break the PowerShell quoting.
            var wrapper = $"@echo off\r\n\"{exe}\" %*\r\n";
            var b64 = Convert.ToBase64String(Encoding.ASCII.GetBytes(wrapper));

            return RunElevatedPowerShell(
                out error,
                $"New-Item -ItemType Directory -Force -Path '{CliBinDir}' | Out-Null;",
                $"Set-Content -LiteralPath '{CliWrapperPath}' " +
                $"-Value ([System.Text.Encoding]::ASCII.GetString([Convert]::FromBase64String('{b64}'))) -Encoding ASCII;",
                // Append to the system (Machine) PATH if not already present.
                $"if (([Environment]::GetEnvironmentVariable('Path','Machine') -split ';') -notcontains '{CliBinDir}') " +
                $"{{ [Environment]::SetEnvironmentVariable('Path', ([Environment]::GetEnvironmentVariable('Path','Machine').TrimEnd(';') + ';' + '{CliBinDir}'), 'Machine') }}");
        }

        public bool TryRemoveCliLink(out string error)
        {
            return RunElevatedPowerShell(
                out error,
                $"if (Test-Path -LiteralPath '{CliWrapperPath}') {{ Remove-Item -LiteralPath '{CliWrapperPath}' -Force }};",
                $"if (([Environment]::GetEnvironmentVariable('Path','Machine') -split ';') -contains '{CliBinDir}') " +
                $"{{ [Environment]::SetEnvironmentVariable('Path', (([Environment]::GetEnvironmentVariable('Path','Machine') -split ';' | Where-Object {{ $_ -ne '{CliBinDir}' }}) -join ';'), 'Machine') }}");
        }

        // Launches PowerShell with admin privileges (UAC prompt) to run the given script statements.
        // Returns false and sets <paramref name="error"/> when the user declines the UAC prompt or the
        // script fails.
        private static bool RunElevatedPowerShell(out string error, params string[] statements)
        {
            var script = string.Join("\n", statements) + $"\nexit $LASTEXITCODE";

            // -Wait so we can read the exit code; -WindowStyle Hidden to avoid a flashing console.
            var psi = new ProcessStartInfo("powershell")
            {
                UseShellExecute = true, // required for Verb = "runas"
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);

            try
            {
                using var proc = Process.Start(psi);
                if (proc is null)
                {
                    error = "Failed to start elevated PowerShell.";
                    return false;
                }

                proc.WaitForExit();
                if (proc.ExitCode != 0)
                {
                    error = $"PowerShell exited with code {proc.ExitCode}.";
                    return false;
                }

                error = string.Empty;
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // ERROR_CANCELLED (1223): user clicked "No" on the UAC prompt.
                error = ex.NativeErrorCode == 1223 ? "Operation cancelled by user." : ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public void OpenWithDefaultEditor(string file)
        {
            var info = new FileInfo(file);
            var start = new ProcessStartInfo("cmd", $"""/c start "" {info.FullName.Quoted()}""");
            start.CreateNoWindow = true;
            Process.Start(start);
        }

        #region HELPER_METHODS
        private List<Models.ExternalTool.LaunchOption> GenerateVSProjectLaunchOptions(string path)
        {
            var root = new DirectoryInfo(path);
            if (!root.Exists)
                return null;

            var options = new List<Models.ExternalTool.LaunchOption>();
            var prefixLen = root.FullName.Length;
            root.WalkFiles(f =>
            {
                if (f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
                {
                    var display = f.Substring(prefixLen).TrimStart(Path.DirectorySeparatorChar);
                    options.Add(new(display, f.Quoted()));
                }
            });
            return options;
        }
        #endregion

        #region EXTERNAL_EDITOR_FINDER
        private string FindVSCode()
        {
            var localMachine = Microsoft.Win32.RegistryKey.OpenBaseKey(
                    Microsoft.Win32.RegistryHive.LocalMachine,
                    Microsoft.Win32.RegistryView.Registry64);

            // VSCode (system)
            var systemVScode = localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{EA457B21-F73E-494C-ACAB-524FDE069978}_is1");
            if (systemVScode != null)
                return systemVScode.GetValue("DisplayIcon") as string;

            var currentUser = Microsoft.Win32.RegistryKey.OpenBaseKey(
                    Microsoft.Win32.RegistryHive.CurrentUser,
                    Microsoft.Win32.RegistryView.Registry64);

            // VSCode (user)
            var vscode = currentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{771FD6B0-FA20-440A-A002-3B3BAC16DC50}_is1");
            if (vscode != null)
                return vscode.GetValue("DisplayIcon") as string;

            return string.Empty;
        }

        private string FindVSCodeInsiders()
        {
            var localMachine = Microsoft.Win32.RegistryKey.OpenBaseKey(
                    Microsoft.Win32.RegistryHive.LocalMachine,
                    Microsoft.Win32.RegistryView.Registry64);

            // VSCode - Insiders (system)
            var systemVScodeInsiders = localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{1287CAD5-7C8D-410D-88B9-0D1EE4A83FF2}_is1");
            if (systemVScodeInsiders != null)
                return systemVScodeInsiders.GetValue("DisplayIcon") as string;

            var currentUser = Microsoft.Win32.RegistryKey.OpenBaseKey(
                    Microsoft.Win32.RegistryHive.CurrentUser,
                    Microsoft.Win32.RegistryView.Registry64);

            // VSCode - Insiders (user)
            var vscodeInsiders = currentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{217B4C08-948D-4276-BFBB-BEE930AE5A2C}_is1");
            if (vscodeInsiders != null)
                return vscodeInsiders.GetValue("DisplayIcon") as string;

            return string.Empty;
        }

        private string FindVSCodium()
        {
            var localMachine = Microsoft.Win32.RegistryKey.OpenBaseKey(
                    Microsoft.Win32.RegistryHive.LocalMachine,
                    Microsoft.Win32.RegistryView.Registry64);

            // VSCodium (system)
            var systemVSCodium = localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{88DA3577-054F-4CA1-8122-7D820494CFFB}_is1");
            if (systemVSCodium != null)
                return systemVSCodium.GetValue("DisplayIcon") as string;

            var currentUser = Microsoft.Win32.RegistryKey.OpenBaseKey(
                    Microsoft.Win32.RegistryHive.CurrentUser,
                    Microsoft.Win32.RegistryView.Registry64);

            // VSCodium (user)
            var vscodium = currentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{2E1F05D1-C245-4562-81EE-28188DB6FD17}_is1");
            if (vscodium != null)
                return vscodium.GetValue("DisplayIcon") as string;

            return string.Empty;
        }

        private string FindSublimeText()
        {
            var localMachine = Microsoft.Win32.RegistryKey.OpenBaseKey(
                    Microsoft.Win32.RegistryHive.LocalMachine,
                    Microsoft.Win32.RegistryView.Registry64);

            // Sublime Text 4
            var sublime = localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Sublime Text_is1");
            if (sublime != null)
            {
                var icon = sublime.GetValue("DisplayIcon") as string;
                return Path.Combine(Path.GetDirectoryName(icon)!, "subl.exe");
            }

            // Sublime Text 3
            var sublime3 = localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Sublime Text 3_is1");
            if (sublime3 != null)
            {
                var icon = sublime3.GetValue("DisplayIcon") as string;
                return Path.Combine(Path.GetDirectoryName(icon)!, "subl.exe");
            }

            return string.Empty;
        }

        private void FindVisualStudio(Models.ExternalToolsFinder finder)
        {
            var vswhere = Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe");
            if (!File.Exists(vswhere))
                return;

            var startInfo = new ProcessStartInfo();
            startInfo.FileName = vswhere;
            startInfo.Arguments = "-format json -prerelease -utf8";
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.RedirectStandardOutput = true;
            startInfo.StandardOutputEncoding = Encoding.UTF8;

            try
            {
                using var proc = Process.Start(startInfo)!;
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                if (proc.ExitCode == 0)
                {
                    var instances = JsonSerializer.Deserialize(output, JsonCodeGen.Default.ListVisualStudioInstance);
                    foreach (var instance in instances)
                    {
                        var exec = instance.ProductPath;
                        var icon = instance.IsPrerelease ? "vs-preview" : "vs";
                        finder.TryAdd(instance.DisplayName, icon, () => exec, GenerateVSProjectLaunchOptions, false);
                    }
                }
            }
            catch
            {
                // Just ignore.
            }
        }

        private string FindZed()
        {
            var currentUser = Microsoft.Win32.RegistryKey.OpenBaseKey(
                    Microsoft.Win32.RegistryHive.CurrentUser,
                    Microsoft.Win32.RegistryView.Registry64);

            // NOTE: this is the official Zed Preview reg data.
            var preview = currentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{F70E4811-D0E2-4D88-AC99-D63752799F95}_is1");
            if (preview != null)
                return preview.GetValue("DisplayIcon") as string;

            var findInPath = new StringBuilder("zed.exe", 512);
            if (PathFindOnPath(findInPath, null))
                return findInPath.ToString();

            return string.Empty;
        }
        #endregion
    }

    [SupportedOSPlatform("windows")]
    public static class Win64Utilities
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct MARGINS
        {
            public int cxLeftWidth;
            public int cxRightWidth;
            public int cyTopHeight;
            public int cyBottomHeight;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        public static void FixWindowFrame(Window w)
        {
            if (w.WindowState == WindowState.Maximized)
            {
                w.BorderThickness = new Thickness(0);
                w.Padding = new Thickness(8, 6, 8, 8);
            }
            else
            {
                w.BorderThickness = new Thickness(1);
                w.Padding = new Thickness(0);
            }

            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                return;

            Dispatcher.UIThread.Post(() =>
            {
                var platformHandle = w.TryGetPlatformHandle();
                if (platformHandle == null)
                    return;

                var margins = new MARGINS { cxLeftWidth = 1, cxRightWidth = 1, cyTopHeight = 1, cyBottomHeight = 1 };
                DwmExtendFrameIntoClientArea(platformHandle.Handle, ref margins);
            }, DispatcherPriority.Render);
        }
    }
}
