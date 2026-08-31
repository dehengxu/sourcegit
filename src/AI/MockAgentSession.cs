using System;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.AI
{
    /// <summary>
    /// Wizard-of-Oz session: replays a hardcoded script (mirroring the H5 prototype's
    /// SCRIPT array in docs/prototype/ai-s0/app.js) with small delays. No LLM, no network.
    /// </summary>
    public class MockAgentSession : IAgentSession
    {
        public event Action<AIAgentMessage> MessageRaised;

        public bool IsRunning
        {
            get;
            private set;
        }

        public void Start(string prompt)
        {
            if (IsRunning)
                return;

            _cancel?.Cancel();
            _cancel = new CancellationTokenSource();
            IsRunning = true;

            var token = _cancel.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    foreach (var (delay, message) in BuildScript(prompt))
                    {
                        await Task.Delay(delay, token);
                        MessageRaised?.Invoke(message);
                    }
                }
                catch (OperationCanceledException)
                {
                    // cancelled by user, nothing to do
                }
                finally
                {
                    IsRunning = false;
                }
            }, CancellationToken.None);
        }

        public void Cancel()
        {
            _cancel?.Cancel();
            IsRunning = false;
        }

        private static (int Delay, AIAgentMessage Message)[] BuildScript(string prompt)
        {
            return
            [
                (200, new AIAgentReasoningMessage()
                {
                    Text = $"用户询问:{prompt}。当前面板是单次调用原型,没有会话状态。我先查看现有实现,再定位生成入口。",
                }),
                (700, new AIAgentReadFileMessage() { File = "src/AI/Agent.cs", Detail = "123 lines" }),
                (900, new AIAgentSearchMessage() { Pattern = "CompleteChatAsync", Detail = "4 matches" }),
                (800, new AIAgentReasoningMessage()
                {
                    Text = "改动点集中在两处:构造函数需要持有会话对象;生成方法需要换成流式接口并尊重取消令牌。生成一个最小改动补丁。",
                }),
                (900, new AIAgentProposeChangeMessage() { File = "src/AI/Agent.cs", LinesAdded = 12, LinesDeleted = 2 }),
                (900, new AIAgentCommandMessage() { CommandLine = "dotnet build src/SourceGit.csproj -c Debug" }),
                (1200, new AIAgentCommandResultMessage() { Text = "Build succeeded. 0 Warning(s) · 4.2s" }),
                (600, new AIAgentAnswerMessage()
                {
                    Text = "已完成对 src/AI/Agent.cs 的改造提议:引入 ConversationSession 承载多轮状态,生成路径切换为流式并接入取消令牌。(Mock 消息,来自硬编码脚本,无 LLM 调用)",
                }),
                (400, new AIAgentNoteMessage() { Text = "— Mock 会话结束(演示)" }),
            ];
        }

        private CancellationTokenSource _cancel = null;
    }
}
