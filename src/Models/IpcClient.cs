using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace SourceGit.Models
{
    public static class IpcClient
    {
        // 探测是否有另一个实例在跑:跟 IpcChannel 用同一个 process.lock 文件
        // (FileShare.None 持有,如果能拿到锁 = 没别人在跑)
        public static bool IsAnotherInstanceRunning()
        {
            var lockPath = Path.Combine(Native.OS.BasicDirectories.CacheDir, "process.lock");
            try
            {
                using var probe = File.Open(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return false;
            }
            catch
            {
                return true;
            }
        }

        public static bool TrySendPath(string path, int timeoutMs = 1500)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // 复用 IpcChannel.GetPipeName() 拿到跟 server 一致的 pipe name
            // (macOS 固定名 / 其他平台 hash,见 IpcChannel.GetPipeName 注释)
            try
            {
                using var client = new NamedPipeClientStream(
                    ".",
                    IpcChannel.GetPipeName(),
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
    }
}
