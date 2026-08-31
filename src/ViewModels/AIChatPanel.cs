using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using SourceGit.AI;

namespace SourceGit.ViewModels
{
    /// <summary>
    /// View-model of the AI chat dock (docs/plan/ai-phase0.md).
    /// Talks to an <see cref="IAgentSession"/>; Phase 0 uses the scripted mock.
    /// M2 adds the review loop: proposals are never applied silently — Approve on the
    /// card writes the change for real so it lands in the unstaged list (decision 3).
    /// </summary>
    public partial class AIChatPanel : ObservableObject
    {
        public bool IsOpen
        {
            get => _isOpen;
            set => SetProperty(ref _isOpen, value);
        }

        public bool IsGenerating
        {
            get => _isGenerating;
            private set => SetProperty(ref _isGenerating, value);
        }

        public AvaloniaList<object> Messages
        {
            get;
        } = [];

        public string InputText
        {
            get => _inputText;
            set => SetProperty(ref _inputText, value);
        }

        public string RepoRoot
        {
            get => _repoRoot;
        }

        public AIChatPanel(string repoRoot)
        {
            _repoRoot = repoRoot;
            _session = new MockAgentSession();
            _session.MessageRaised += OnMessageRaised;
        }

        [RelayCommand]
        public void ToggleOpen()
        {
            IsOpen = !IsOpen;
        }

        private void OnMessageRaised(AIAgentMessage message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Messages.Add(message);
                if (message is AIAgentNoteMessage)
                    IsGenerating = false;
            }, DispatcherPriority.Background);
        }

        [RelayCommand]
        private void Send()
        {
            var input = InputText;
            if (string.IsNullOrWhiteSpace(input) || _session.IsRunning)
                return;

            Messages.Add(new AIAgentUserMessage() { Text = input.Trim() });
            InputText = string.Empty;
            IsGenerating = true;
            _session.Start(input.Trim());
        }

        [RelayCommand]
        private void Cancel()
        {
            _session.Cancel();
            IsGenerating = false;
            Messages.Add(new AIAgentNoteMessage() { Text = "— 已取消" });
        }

        [RelayCommand]
        private void Clear()
        {
            Messages.Clear();
        }

        [RelayCommand]
        private void ApproveChange(AIAgentProposeChangeMessage proposal)
        {
            if (proposal.Status != AIAgentProposeStatus.Pending)
                return;

            var fullPath = Path.Combine(_repoRoot, proposal.File);
            try
            {
                if (File.Exists(fullPath))
                {
                    var content = File.ReadAllText(fullPath, Encoding.UTF8);
                    File.WriteAllText(fullPath, proposal.NewLine + Environment.NewLine + content, Encoding.UTF8);
                }
                else
                {
                    File.WriteAllText(fullPath, proposal.NewLine + Environment.NewLine, Encoding.UTF8);
                }

                proposal.Status = AIAgentProposeStatus.Approved;
            }
            catch (Exception e)
            {
                Messages.Add(new AIAgentNoteMessage() { Text = $"— 应用修改失败:{e.Message}" });
            }
        }

        [RelayCommand]
        private void RejectChange(AIAgentProposeChangeMessage proposal)
        {
            if (proposal.Status != AIAgentProposeStatus.Pending)
                return;

            proposal.Status = AIAgentProposeStatus.Rejected;
        }

        /// <summary>
        /// Builds the diff context shown in the preview window from the proposal itself
        /// (what you preview is exactly what Approve writes).
        /// </summary>
        public TextDiffContext BuildProposalDiff(AIAgentProposeChangeMessage proposal)
        {
            var fullPath = Path.Combine(_repoRoot, proposal.File);
            var lines = new List<Models.TextDiffLine>();
            var indicator = "@@ -1,4 +1,5 @@";
            lines.Add(new Models.TextDiffLine(Models.TextDiffLineType.Indicator, indicator, Encoding.UTF8.GetBytes(indicator), 0, 0));

            var newLine = proposal.NewLine.TrimEnd();
            lines.Add(new Models.TextDiffLine(Models.TextDiffLineType.Added, newLine, Encoding.UTF8.GetBytes(newLine), 0, 1));

            var oldIdx = 1;
            var newIdx = 2;
            if (File.Exists(fullPath))
            {
                foreach (var raw in File.ReadLines(fullPath))
                {
                    lines.Add(new Models.TextDiffLine(Models.TextDiffLineType.Normal, raw, Encoding.UTF8.GetBytes(raw), oldIdx, newIdx));
                    oldIdx++;
                    newIdx++;
                    if (lines.Count >= 6)
                        break;
                }
            }

            var diff = new Models.TextDiff()
            {
                Lines = lines,
                MaxLineNumber = newIdx,
                AddedLines = proposal.LinesAdded,
                DeletedLines = proposal.LinesDeleted,
            };

            var change = new Models.Change()
            {
                Path = proposal.File,
                Index = Models.ChangeState.Modified,
                WorkTree = Models.ChangeState.Modified,
            };

            var option = new Models.DiffOption(change, false);
            return new CombinedTextDiff(option, diff);
        }

        private readonly string _repoRoot = null;
        private readonly IAgentSession _session = null;
        private bool _isOpen = false;
        private bool _isGenerating = false;
        private string _inputText = string.Empty;
    }
}
