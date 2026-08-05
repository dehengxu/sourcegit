using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace SourceGit.Models
{
    public static class IpcClient
    {
        // Server 端由 IpcChannel 写自己的 PID 到 <DataDir>/process.pid,
        // client 端从这里读 PID 拼出 NamedPipe 名字 SourceGit_{pid}。
        // 走独立文件而不是 process.lock 是因为后者用 FileShare.None 持有,
        // 其他进程连打开都打不开,更别说读内容。
        private static string PidFilePath => Path.Combine(Native.OS.DataDir, "process.pid");

        public static bool IsAnotherInstanceRunning()
        {
            // process.pid 存在 ≈ 有 server 在跑(server 启动时写、Dispose 时删)
            // 进程崩溃没 Dispose 的小概率场景,会在下次 TrySendPath 时被发现(server 连不上)
            return File.Exists(PidFilePath);
        }

        public static bool TrySendPath(string path, int timeoutMs = 1500)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            if (!TryReadServerPid(out var pid))
                return false;

            try
            {
                using var client = new NamedPipeClientStream(
                    ".",
                    $"SourceGit_{pid}",
                    PipeDirection.Out,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                client.Connect(timeoutMs);
                if (!client.IsConnected)
                    return false;

                using var writer = new StreamWriter(client);
                writer.WriteLine(path);
                writer.Flush();

                if (OperatingSystem.IsWindows())
                    client.WaitForPipeDrain();
                else
                    Thread.Sleep(200);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadServerPid(out int pid)
        {
            pid = 0;
            try
            {
                var raw = File.ReadAllText(PidFilePath).Trim();
                if (!int.TryParse(raw, out pid) || pid <= 0)
                    return false;

                // 顺手 sanity check: PID 对应的进程得活着,不然拼出来的 pipe name 一定连不上
                // (异常退出没 Dispose 删 .pid 的小概率场景)
                try
                {
                    Process.GetProcessById(pid);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
