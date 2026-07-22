using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace SourceGit.Models
{
    public static class IpcClient
    {
        private const string PipeName = "SourceGitIPCChannel";

        public static bool TrySendPath(string path, int timeoutMs = 1500)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            try
            {
                using var client = new NamedPipeClientStream(
                    ".",
                    PipeName + Environment.UserName,
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
