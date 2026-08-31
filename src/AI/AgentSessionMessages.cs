namespace SourceGit.AI
{
    /// <summary>
    /// Base type of messages emitted by an agent session. Distinct concrete types so
    /// views can pick a DataTemplate per message kind (M1 renders them as simple lines;
    /// M2 upgrades to interactive cards per docs/plan/ai-phase0.md).
    /// </summary>
    public abstract class AIAgentMessage
    {
    }

    public sealed class AIAgentUserMessage : AIAgentMessage
    {
        public string Text
        {
            get;
            init;
        } = string.Empty;
    }

    public sealed class AIAgentReasoningMessage : AIAgentMessage
    {
        public string Text
        {
            get;
            init;
        } = string.Empty;
    }

    public sealed class AIAgentReadFileMessage : AIAgentMessage
    {
        public string File
        {
            get;
            init;
        } = string.Empty;

        public string Detail
        {
            get;
            init;
        } = string.Empty;
    }

    public sealed class AIAgentSearchMessage : AIAgentMessage
    {
        public string Pattern
        {
            get;
            init;
        } = string.Empty;

        public string Detail
        {
            get;
            init;
        } = string.Empty;
    }

    public sealed class AIAgentProposeChangeMessage : AIAgentMessage
    {
        public string File
        {
            get;
            init;
        } = string.Empty;

        public int LinesAdded
        {
            get;
            init;
        }

        public int LinesDeleted
        {
            get;
            init;
        }
    }

    public sealed class AIAgentCommandMessage : AIAgentMessage
    {
        public string CommandLine
        {
            get;
            init;
        } = string.Empty;
    }

    public sealed class AIAgentCommandResultMessage : AIAgentMessage
    {
        public string Text
        {
            get;
            init;
        } = string.Empty;
    }

    public sealed class AIAgentAnswerMessage : AIAgentMessage
    {
        public string Text
        {
            get;
            init;
        } = string.Empty;
    }

    public sealed class AIAgentNoteMessage : AIAgentMessage
    {
        public string Text
        {
            get;
            init;
        } = string.Empty;
    }
}
