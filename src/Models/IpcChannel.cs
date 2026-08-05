using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    public class IpcChannel : IDisposable
    {
        public bool IsFirstInstance { get; }

        public event Action<string> MessageReceived;

        public IpcChannel()
        {
            try
            {
                _singletonLock = File.Open(Path.Combine(Native.OS.DataDir, "process.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                // 写自己的 PID 到独立文件,供 client 端 IpcClient 拼 pipe name
                // (不能写到 process.lock,因为这里已经用 FileShare.None 持有了它)
                File.WriteAllText(Path.Combine(Native.OS.DataDir, "process.pid"), Environment.ProcessId.ToString());
                IsFirstInstance = true;
                _server = new NamedPipeServerStream(
                    GetPipeName(),
                    PipeDirection.In,
                    -1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                _cancellationTokenSource = new CancellationTokenSource();
                Task.Run(StartServer);
            }
            catch
            {
                IsFirstInstance = false;
            }
        }

        public void SendToFirstInstance(string cmd)
        {
            IpcClient.TrySendPath(cmd);
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _singletonLock?.Dispose();
            // 清理 PID 文件,避免 client 端拿到一个已死进程的 PID(僵尸)
            try { File.Delete(Path.Combine(Native.OS.DataDir, "process.pid")); } catch { /* 忽略 */ }
        }

        private static string GetPipeName()
        {
            // NOTE: .NET 10 on macOS maps NamedPipe to a unix domain socket under
            // /var/folders/.../T/CoreFxPipe_<name>, where sun_path is limited to
            // 104 chars. The previous name (SourceGitIPCChannel<user>_<16hex>)
            // pushes the total path to ~105 chars on common systems, which
            // throws ArgumentOutOfRangeException out of NamedPipeServerStream's
            // ctor and makes IpcChannel.IsFirstInstance always false.
            //
            // Use ProcessId (guaranteed unique per running process) so the
            // resulting CoreFxPipe_SourceGit_<pid> stays well under 104 chars.
            return $"SourceGit_{Environment.ProcessId}";
        }

        private async void StartServer()
        {
            using var reader = new StreamReader(_server);

            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    await _server.WaitForConnectionAsync(_cancellationTokenSource.Token);

                    if (!_cancellationTokenSource.IsCancellationRequested)
                    {
                        var line = await reader.ReadToEndAsync(_cancellationTokenSource.Token);
                        MessageReceived?.Invoke(line.Trim());
                    }

                    _server.Disconnect();
                }
                catch
                {
                    if (!_cancellationTokenSource.IsCancellationRequested && _server.IsConnected)
                        _server.Disconnect();
                }
            }
        }

        private FileStream _singletonLock = null;
        private NamedPipeServerStream _server = null;
        private CancellationTokenSource _cancellationTokenSource = null;
    }
}
