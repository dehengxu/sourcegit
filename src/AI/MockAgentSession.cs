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
                    Text = "改动点集中在两处:构造函数需要持有会话对象;生成方法需要换成流式接口并尊重取消令牌。先在 README 顶部加一行说明作为演示改动,等待用户在卡片上审查。",
                }),
                (900, new AIAgentProposeChangeMessage()
                {
                    File = "README.md",
                    LinesAdded = 1,
                    LinesDeleted = 0,
                    NewLine = "# 🤖 AI 助手演示修改(在聊天面板中批准;可随时在 Working Copy 中丢弃)",
                }),
                (900, new AIAgentCommandMessage() { CommandLine = "dotnet build src/SourceGit.csproj -c Debug" }),
                (1200, new AIAgentCommandResultMessage() { Text = "Build succeeded. 0 Warning(s) · 4.2s" }),
                (600, new AIAgentAnswerMessage()
                {
                    Text = "已提交一个修改提议(README.md 顶部插入一行说明),请在上方卡片中审查:查看 Diff 后 Approve 或 Reject。批准后改动会真实进入未暂存列表,可正常暂存/提交/丢弃。(Mock 消息,无 LLM 调用)",
                }),
                (400, new AIAgentNoteMessage() { Text = "— Mock 会话结束(演示)" }),
            ];
        }

        private CancellationTokenSource _cancel = null;
    }
}
