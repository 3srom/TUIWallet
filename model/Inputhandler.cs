using System;

namespace TUIWallet.Models
{
    public class InputHandler
    {
        private readonly int _menuCount = Enum.GetValues<MenuAction>().Length;
        private readonly int _txWindowSize = 4;

        public AppEvent Handle(ConsoleKeyInfo key, UiState ui, int totalTxs)
        {
            if (key.Key == ConsoleKey.Q) return AppEvent.Quit;
            if (key.Key == ConsoleKey.R) return AppEvent.Refresh;

            if (key.Key == ConsoleKey.Enter)
            {
                return ui.Focus switch
                {
                    FocusZone.Left => (AppEvent)ui.LeftRow,
                    FocusZone.Top => AppEvent.CopyAddress,
                    _ => AppEvent.None
                };
            }

            // Panel switching
            if (key.Key == ConsoleKey.RightArrow && ui.Focus == FocusZone.Left)
            { ui.Focus = FocusZone.Right; return AppEvent.None; }
            if (key.Key == ConsoleKey.LeftArrow && ui.Focus == FocusZone.Right)
            { ui.Focus = FocusZone.Left; return AppEvent.None; }

            // Left menu navigation
            if (ui.Focus == FocusZone.Left)
            {
                if (key.Key == ConsoleKey.UpArrow)
                {
                    if (ui.LeftRow > 0) ui.LeftRow--;
                    else ui.Focus = FocusZone.Top;
                }
                else if (key.Key == ConsoleKey.DownArrow && ui.LeftRow < _menuCount - 1)
                    ui.LeftRow++;
                return AppEvent.None;
            }

            // Right transactions
            if (ui.Focus == FocusZone.Right)
            {
                if (key.Key == ConsoleKey.UpArrow)
                {
                    if (ui.RightRow > 0) ui.RightRow--;
                    else if (ui.TxScroll > 0) ui.TxScroll--;
                    else ui.Focus = FocusZone.Top;
                }
                else if (key.Key == ConsoleKey.DownArrow)
                {
                    int visible = Math.Min(totalTxs - ui.TxScroll, _txWindowSize);
                    if (ui.RightRow < visible - 1) ui.RightRow++;
                    else if (ui.TxScroll < totalTxs - _txWindowSize) ui.TxScroll++;
                }
                return AppEvent.None;
            }

            // Top zone
            if (ui.Focus == FocusZone.Top && key.Key == ConsoleKey.DownArrow)
                ui.Focus = FocusZone.Left;

            return AppEvent.None;
        }
    }

    public enum AppEvent
    {
        None = -1,
        Send = 0,
        Receive = 1,
        Keys = 2,
        Wallets = 3,
        Vanity = 4,       // matches MenuAction.Vanity index
        CopyAddress = 10,
        Refresh = 11,
        Quit = 99
    }

    public class UiState
    {
        public FocusZone Focus { get; set; } = FocusZone.Left;
        public int LeftRow { get; set; } = 0;
        public int RightRow { get; set; } = 0;
        public int TxScroll { get; set; } = 0;
    }
}