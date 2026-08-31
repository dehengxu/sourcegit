using System;

namespace SourceGit.AI
{
    /// <summary>
    /// Abstraction over an agent conversation so the UI never depends on where replies
    /// come from. Phase 0 only has <see cref="MockAgentSession"/> (scripted, no LLM);
    /// Phase 1 adds an OpenAI-compatible implementation behind the same interface and
    /// the UI stays untouched (docs/plan/ai-phase0.md §5).
    /// </summary>
    public interface IAgentSession
    {
        /// <summary>
        /// Raised (possibly on a background thread) for every message the session produces.
        /// Receivers must marshal to the UI thread themselves.
        /// </summary>
        event Action<AIAgentMessage> MessageRaised;

        bool IsRunning
        {
            get;
        }

        void Start(string prompt);
        void Cancel();
    }
}
