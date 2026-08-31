using System;
using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using SourceGit.AI;

namespace SourceGit.ViewModels
{
    /// <summary>
    /// View-model of the AI chat dock (docs/plan/ai-phase0.md T-s0-1/T-s0-2).
    /// Talks to an <see cref="IAgentSession"/>; Phase 0 uses the scripted mock.
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

        public AIChatPanel()
        {
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

        private readonly IAgentSession _session = null;
        private bool _isOpen = false;
        private bool _isGenerating = false;
        private string _inputText = string.Empty;
    }
}
