using System;

using Avalonia.Controls;
using Avalonia.Input;

using SourceGit.ViewModels;

namespace SourceGit.Views
{
    public partial class AIChatPanel : UserControl
    {
        public AIChatPanel()
        {
            InitializeComponent();
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is ViewModels.AIChatPanel vm)
            {
                vm.SendCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void OnResizerPressed(object sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _startY = e.GetPosition(null).Y;
                _startHeight = Bounds.Height;
                PART_Resizer.Cursor = _cursorNS;
                e.Pointer.Capture(PART_Resizer);
                e.Handled = true;
            }
        }

        private void OnResizerMoved(object sender, PointerEventArgs e)
        {
            if (_startHeight < 0)
                return;

            var delta = e.GetPosition(null).Y - _startY;
            var height = Math.Clamp(_startHeight - delta, 120, 720);
            ViewModels.Preferences.Instance.Layout.AIChatPanelHeight = height;
        }

        private void OnResizerReleased(object sender, PointerReleasedEventArgs e)
        {
            if (_startHeight < 0)
                return;

            _startHeight = -1;
            PART_Resizer.Cursor?.Dispose();
            PART_Resizer.Cursor = _cursorNS;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        private double _startY = 0;
        private double _startHeight = -1;
        private readonly Cursor _cursorNS = new(StandardCursorType.SizeNorthSouth);
    }
}
