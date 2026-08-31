using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.AI
{
    /// <summary>
    /// Base type of messages emitted by an agent session. Distinct concrete types so
    /// views can pick a DataTemplate per message kind (M1 renders them as simple lines;
    /// M2 upgrades to interactive cards per docs/plan/ai-phase0.md). Observable so
    /// stateful messages (write proposals) can drive card UI.
    /// </summary>
    public abstract class AIAgentMessage : ObservableObject
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

    public enum AIAgentProposeStatus
    {
        Pending,
        Approved,
        Rejected,
    }

    /// <summary>
    /// Write proposal. Never applied silently: the user must Approve on the card
    /// (decision 3). In Phase 0 the mock approves a one-line insertion at the top of
    /// a real repo file so the change genuinely lands in the unstaged list.
    /// </summary>
    public partial class AIAgentProposeChangeMessage : AIAgentMessage
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsPending))]
        [NotifyPropertyChangedFor(nameof(IsDecided))]
        [NotifyPropertyChangedFor(nameof(IsApproved))]
        [NotifyPropertyChangedFor(nameof(IsRejected))]
        private AIAgentProposeStatus _status = AIAgentProposeStatus.Pending;

        public bool IsPending => Status == AIAgentProposeStatus.Pending;
        public bool IsDecided => Status != AIAgentProposeStatus.Pending;
        public bool IsApproved => Status == AIAgentProposeStatus.Approved;
        public bool IsRejected => Status == AIAgentProposeStatus.Rejected;

        public string File
        {
            get;
            init;
        } = string.Empty;

        public string NewLine
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
