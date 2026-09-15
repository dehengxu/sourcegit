using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace SourceGit.Native
{
    [SupportedOSPlatform("linux")]
    internal class Linux : OS.IBackend
    {
        [DllImport("libc", SetLastError = true)]
        private static extern int kill(int pid, int sig);

        public void SetupApp(AppBuilder builder)
        {
            builder.With(new X11PlatformOptions() { EnableIme = true });
        }

        public void SetupWindow(Window window)
        {
            window.BorderThickness = new Thickness(0);

            if (OS.UseSystemWindowFrame)
            {
                window.ExtendClientAreaToDecorationsHint = false;
            }
            else
            {
                window.ExtendClientAreaToDecorationsHint = true;
                window.Classes.Add("custom_window_frame");
            }
        }

        public OS.Directories GetOrCreateDirectories()
        {
            var dirs = new OS.Directories();
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // AppImage supports portable mode
            var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
            if (!string.IsNullOrEmpty(appImage) && File.Exists(appImage))
            {
                var portableDir = Path.Combine(Path.GetDirectoryName(appImage)!, "data");
                if (Directory.Exists(portableDir))
                {
                    dirs.ConfigDir = portableDir;
                    dirs.CacheDir = portableDir;
                    return dirs;
                }
            }

            // XDG Base Directory Specification: https://specifications.freedesktop.org/basedir/latest/
            dirs.ConfigDir = GetXdgDirectory("XDG_CONFIG_HOME", Path.Combine(home, ".config"), "SourceGit");
            dirs.CacheDir = GetXdgDirectory("XDG_CACHE_HOME", Path.Combine(home, ".cache"), "SourceGit");

            // If the app basic dirs already exist, we can skip the migration step
            if (Directory.Exists(dirs.ConfigDir) && Directory.Exists(dirs.CacheDir))
                return dirs;

            // Create the config and cache directories if they don't exist
            if (!Directory.Exists(dirs.ConfigDir))
                Directory.CreateDirectory(dirs.ConfigDir);
            if (!Directory.Exists(dirs.CacheDir))
                Directory.CreateDirectory(dirs.CacheDir);

            // Migrate legacy data dir: ~/.sourcegit to XDG standard directories
            var legacyDir = Path.Combine(home, ".sourcegit");
            if (Directory.Exists(legacyDir))
            {
                try
                {
                    File.Copy(Path.Combine(legacyDir, "preference.json"), Path.Combine(dirs.ConfigDir, "preference.json"), true);
                    Directory.Move(Path.Combine(legacyDir, "avatars"), Path.Combine(dirs.CacheDir, "avatars"));
                    Directory.Delete(legacyDir, true);
                }
                catch
                {
                    // Ignore any errors during migration
                }
            }

            return dirs;
        }

        public string FindGitExecutable()
        {
            return FindExecutable("git");
        }

        public string FindTerminal(Models.ShellOrTerminal shell)
        {
            if (shell.Type.Equals("custom", StringComparison.Ordinal))
                return string.Empty;

            return FindExecutable(shell.Exec);
        }

        public List<Models.ExternalTool> FindExternalTools()
        {
            var localAppDataDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var finder = new Models.ExternalToolsFinder();
            finder.VSCode(() => FindExecutable("code"));
            finder.VSCodeInsiders(() => FindExecutable("code-insiders"));
            finder.VSCodium(() => FindExecutable("codium"));
            finder.Cursor(() => FindExecutable("cursor"));
            finder.FindJetBrainsFromToolbox(() => Path.Combine(localAppDataDir, "JetBrains/Toolbox"));
            finder.SublimeText(() => FindExecutable("subl"));
            finder.Zed(() =>
            {
                var exec = FindExecutable("zeditor");
                return string.IsNullOrEmpty(exec) ? FindExecutable("zed") : exec;
            });
            return finder.Tools;
        }

        public void OpenBrowser(string url)
        {
            var browser = Environment.GetEnvironmentVariable("BROWSER");
            if (string.IsNullOrEmpty(browser))
                browser = "xdg-open";
            Process.Start(browser, url.Quoted());
        }

        public void OpenInFileManager(string path)
        {
            if (Directory.Exists(path))
            {
                Process.Start("xdg-open", path.Quoted());
            }
            else
            {
                var dir = Path.GetDirectoryName(path);
                if (Directory.Exists(dir))
                    Process.Start("xdg-open", dir.Quoted());
            }
        }

        public void OpenTerminal(string workdir, string args)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var cwd = string.IsNullOrEmpty(workdir) ? home : workdir;

            var startInfo = new ProcessStartInfo();
            startInfo.WorkingDirectory = cwd;
            startInfo.FileName = OS.ShellOrTerminal;
            startInfo.Arguments = args;

            try
            {
                Process.Start(startInfo);
            }
            catch (Exception e)
            {
                Models.Notification.Send(workdir, $"Failed to start '{OS.ShellOrTerminal}'. Reason: {e.Message}", true);
            }
        }

        public void OpenWithDefaultEditor(string file)
        {
            var proc = Process.Start("xdg-open", file.Quoted());
            if (proc != null)
            {
                proc.WaitForExit();

                if (proc.ExitCode != 0)
                    Models.Notification.Send("", $"Failed to open: {file}", true);

                proc.Close();
            }
        }

        [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Assembly.Location is a dev-mode fallback only used when ProcessPath is empty.")]
        public void LaunchDetachedGui(string[] args)
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                exe = Assembly.GetExecutingAssembly().Location; // dev-mode fallback, empty under NativeAOT
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                return;

            var startInfo = new ProcessStartInfo(exe);
            foreach (var a in args)
                startInfo.ArgumentList.Add(a);
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            try
            {
                Process.Start(startInfo);
            }
            catch (Exception e)
            {
                Models.Notification.Send("", $"Failed to relaunch SourceGit. Reason: {e.Message}", true);
            }
        }

        private const string CliLinkPath = "/usr/local/bin/sourcegit";
        private const string CliLinkPathSystem = "/usr/bin/sourcegit";

        public CliLinkStatus GetCliLinkStatus()
        {
            // A package install (e.g. the shipped .deb) may have placed the link under /usr/bin.
            if (File.Exists(CliLinkPath))
                return new CliLinkStatus { Installed = true, Target = CliLinkPath };
            if (File.Exists(CliLinkPathSystem))
                return new CliLinkStatus { Installed = true, Target = CliLinkPathSystem };

            return new CliLinkStatus { Installed = false };
        }

        public bool TryCreateCliLink(out string error)
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                error = "Cannot determine the current executable path.";
                return false;
            }

            return RunElevated($"ln -sf {exe.Quoted()} {CliLinkPath.Quoted()}", out error);
        }

        public bool TryRemoveCliLink(out string error)
        {
            return RunElevated($"rm -f {CliLinkPath.Quoted()}", out error);
        }

        // Runs a command with root privileges using the first available polkit/sudo front-end so the
        // user gets an interactive authentication prompt. Only removes links we created. The command is
        // wrapped in `sh -c` so spaces in paths are handled correctly.
        private static bool RunElevated(string shellCommand, out string error)
        {
            var pkexec = FindOnPath("pkexec");
            if (!string.IsNullOrEmpty(pkexec))
                return RunElevatedProcess(pkexec, shellCommand, out error);

            var sudo = FindOnPath("sudo");
            if (!string.IsNullOrEmpty(sudo))
                return RunElevatedProcess(sudo, shellCommand, out error);

            error = "Neither pkexec nor sudo is available. Please install pkexec (polkit) and try again.";
            return false;
        }

        private static bool RunElevatedProcess(string tool, string shellCommand, out string error)
        {
            var psi = new ProcessStartInfo(tool)
            {
                UseShellExecute = true, // interactive auth prompt needs a controlling terminal/session
            };
            psi.ArgumentList.Add("sh");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(shellCommand);

            try
            {
                using var proc = Process.Start(psi);
                if (proc is null)
                {
                    error = $"Failed to start {tool}.";
                    return false;
                }

                proc.WaitForExit();
                if (proc.ExitCode != 0)
                {
                    error = $"{tool} exited with code {proc.ExitCode}.";
                    return false;
                }

                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string FindOnPath(string name)
        {
            var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var test = Path.Combine(dir, name);
                if (File.Exists(test))
                    return test;
            }

            return null;
        }

        public bool SupportSetSid()
        {
            return true;
        }

        public string GetSetSidExecutable()
        {
            return "setsid";
        }

        public void TerminateProcess(Process proc)
        {
            if (kill(-proc.Id, 15) != 0)
            {
                // If the process already exited, we can just ignore the error.
                if (Marshal.GetLastPInvokeError() == 3 /* ESRCH */)
                    return;

                // Actually, this will not be called since the process is
                // spawned by us (EPERM will not happen), and SIGTERM (15)
                // is a valid signal (EINVAL will not happen).
                // See https://www.man7.org/linux/man-pages/man2/kill.2.html
                try
                {
                    proc.Kill(true);
                }
                catch
                {
                    // Ignore any errors when trying to kill the process
                }
            }
        }

        private string FindExecutable(string filename)
        {
            var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var paths = pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            foreach (var path in paths)
            {
                var test = Path.Combine(path, filename);
                if (File.Exists(test))
                    return test;
            }

            var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", filename);
            return File.Exists(local) ? local : string.Empty;
        }

        private string GetXdgDirectory(string envVar, string fallback, string subDirName)
        {
            var dir = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                return Path.Combine(dir, subDirName);

            return Path.Combine(fallback, subDirName);
        }
    }
}
