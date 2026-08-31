using Avalonia.Controls;

namespace SourceGit.Views
{
    /// <summary>
    /// Standalone preview of an agent write proposal, reusing the app's TextDiffView.
    /// Opened from the propose card in AIChatPanel (docs/plan/ai-phase0.md T-s0-4).
    /// </summary>
    public partial class AIProposeDiff : ChromelessWindow
    {
        public string FileName
        {
            get;
            set;
        } = string.Empty;

        public AIProposeDiff()
        {
            InitializeComponent();
        }
    }
}
