using System;
using System.Collections.Generic;

namespace TUIWallet.Models
{
    public class WalletRenderer
    {
        private const int TxWindowSize = 4;
        private string _flashMsg = "";
        private bool _flashError = false;

        // ── Main draw ─────────────────────────────────────────────────────────────

        public void Draw(WalletState state, UiState ui, PriceTicker ticker,
                        VanityRunner.ComputeMode? computeMode = null)
        {
            Console.Clear();
            int h = Console.WindowHeight;
            int w = Console.WindowWidth;

            DrawPriceChart(ticker, h, w);
            DrawAddress(state.Address, state.Label, ui.Focus, w);
            DrawPrice(ticker, w);
            if (computeMode.HasValue) DrawComputeBadge(computeMode.Value, h);
            DrawBalance(state.Balance, state.Unconfirmed, h, w);
            DrawLeftMenu(ui.Focus, ui.LeftRow, h);
            DrawTransactions(state.Transactions, ui.Focus, ui.RightRow, ui.TxScroll, h, w);
            DrawHints(h, w);

            if (_flashMsg != "")
            {
                DrawFlash(_flashMsg, _flashError, h, w);
                _flashMsg = "";
            }

            Console.SetCursorPosition(0, 0);
        }

        public void Flash(string msg, bool error = false)
        {
            _flashMsg = msg;
            _flashError = error;
        }

        // ── Send / Receive / Keys dialogs ─────────────────────────────────────────

        public (string address, decimal amount) PromptSend(decimal balance)
        {
            Console.Clear();
            int w = Console.WindowWidth, h = Console.WindowHeight;
            DrawBox(w / 2 - 28, 56, h / 2 - 5, 10, "Send BTC");
            int cx = w / 2 - 24, cy = h / 2 - 3;

            Console.SetCursorPosition(cx, cy);
            Console.Write($"Available: {balance:F5} BTC");
            Console.SetCursorPosition(cx, cy + 2);
            Console.Write("To address : ");
            Console.CursorVisible = true;
            var addr = Console.ReadLine() ?? "";
            Console.SetCursorPosition(cx, cy + 4);
            Console.Write("Amount BTC : ");
            var amtStr = Console.ReadLine() ?? "0";
            Console.CursorVisible = false;
            decimal.TryParse(amtStr,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out decimal amount);
            return (addr, amount);
        }

        public void ShowReceiveAddress(string address)
        {
            Console.Clear();
            int w = Console.WindowWidth, h = Console.WindowHeight;
            DrawBox(w / 2 - 28, 56, h / 2 - 4, 8, "Receive BTC");
            WriteCenter("Send BTC to this address:", h / 2 - 2);
            WriteCenter(address, h / 2, highlight: true);
            WriteCenter("Enter = copy  \u2022  any other key = back", h / 2 + 2);
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.Enter) TryCopyToClipboard(address);
        }

        public void ShowKeys(string wif, string address)
        {
            Console.Clear();
            int w = Console.WindowWidth, h = Console.WindowHeight;
            DrawBox(w / 2 - 30, 60, h / 2 - 7, 14, "Key Backup");
            WriteCenter("\u26a0  Keep this secret. Anyone with this key owns your BTC.  \u26a0", h / 2 - 5, highlight: true);
            WriteCenter("Address:", h / 2 - 2);
            WriteCenter(address, h / 2 - 1);
            WriteCenter("Private Key (WIF):", h / 2 + 1);
            WriteCenter(wif, h / 2 + 2);
            WriteCenter("Press any key to return.", h / 2 + 5);
            Console.ReadKey(intercept: true);
        }


        // ── Live loading screen ───────────────────────────────────────────────────

        private static readonly string[] _spinFrames = { "◜", "◝", "◞", "◟" };
        private int _spinIdx = 0;

        /// <summary>
        /// Draws a live loading screen with spinner + label + optional sub-status.
        /// Call repeatedly while awaiting async work. Does NOT block or require a keypress.
        /// </summary>
        public void DrawLoading(string label, string subStatus = "")
        {
            Console.Clear();
            int h = Console.WindowHeight, w = Console.WindowWidth;

            // Outer box
            int boxW = Math.Min(58, w - 4);
            int boxH = string.IsNullOrEmpty(subStatus) ? 7 : 9;
            int boxX = w / 2 - boxW / 2;
            int boxY = h / 2 - boxH / 2;
            DrawBox(boxX, boxW, boxY, boxH, "");

            // Bitcoin logo shimmer row
            string btc = "[ ₿ ]";
            Console.SetCursorPosition(w / 2 - btc.Length / 2, boxY + 1);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(btc);
            Console.ResetColor();

            // Spinner + label
            string spinner = _spinFrames[_spinIdx % _spinFrames.Length];
            _spinIdx++;
            string line = $"{spinner}  {label}";
            Console.SetCursorPosition(w / 2 - line.Length / 2, boxY + 3);
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(line);
            Console.ResetColor();

            // Sub-status (e.g. "Connecting to Blockstream API...")
            if (!string.IsNullOrEmpty(subStatus))
            {
                string sub = subStatus.Length > boxW - 4
                    ? subStatus[..(boxW - 7)] + "..."
                    : subStatus;
                Console.SetCursorPosition(w / 2 - sub.Length / 2, boxY + 5);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(sub);
                Console.ResetColor();
            }

            // Animated dots progress bar at bottom of box
            int dotsTotal = 20;
            int dotsFilled = _spinIdx % (dotsTotal + 1);
            int dotX = w / 2 - dotsTotal / 2;
            Console.SetCursorPosition(dotX, boxY + boxH - 2);
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write(new string('•', dotsFilled));
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(new string('·', dotsTotal - dotsFilled));
            Console.ResetColor();
        }

        public void ShowMessage(string message, bool isError = false)
        {
            Console.Clear();
            int h = Console.WindowHeight, w = Console.WindowWidth;
            var lines = message.Split('\n');
            DrawBox(w / 2 - 28, 56, h / 2 - lines.Length - 2, lines.Length + 4, "");
            Console.ForegroundColor = isError ? ConsoleColor.Red : ConsoleColor.Green;
            for (int i = 0; i < lines.Length; i++)
                WriteCenter(lines[i], h / 2 - lines.Length / 2 + i);
            Console.ResetColor();
            WriteCenter("Press any key to continue.", h / 2 + lines.Length);
            Console.ReadKey(intercept: true);
        }

        // ── Setup / password prompts ──────────────────────────────────────────────

        public (SetupChoice choice, string wifInput, string password, string label) PromptSetup()
        {
            Console.Clear();
            int h = Console.WindowHeight, w = Console.WindowWidth;
            DrawBox(w / 2 - 24, 48, h / 2 - 9, 18, "TUI Bitcoin Wallet \u2014 New Wallet");

            int cx = w / 2 - 20, cy = h / 2 - 7;
            Console.SetCursorPosition(cx, cy); Console.Write("No wallet found. What would you like to do?");
            Console.SetCursorPosition(cx, cy + 2); Console.Write("[1]  Create a new wallet");
            Console.SetCursorPosition(cx, cy + 3); Console.Write("[2]  Import existing private key (WIF)");
            Console.SetCursorPosition(cx, cy + 5); Console.Write("Press 1 or 2:");

            Console.CursorVisible = true;
            char choice = ' ';
            while (choice != '1' && choice != '2')
                choice = Console.ReadKey(intercept: true).KeyChar;

            string input = "";
            int row = cy + 7;
            if (choice == '2')
            {
                Console.SetCursorPosition(cx, row); Console.Write("Paste WIF key: ");
                input = Console.ReadLine() ?? "";
                row += 2;
            }

            Console.SetCursorPosition(cx, row);
            Console.Write("Wallet label (optional, Enter to skip): ");
            string label = Console.ReadLine() ?? "";
            row += 2;

            Console.SetCursorPosition(cx, row);
            Console.Write("Set wallet password: ");
            var password = ReadPassword();
            Console.CursorVisible = false;

            return (choice == '1' ? SetupChoice.Create : SetupChoice.Import, input, password, label);
        }

        public string PromptPassword(string hint = "") =>
            PromptPasswordInternal(hint);

        /// <summary>
        /// Full unlock screen: lists all wallets, lets user arrow-key to pick one,
        /// shows clearly which wallet is being unlocked, then takes the password.
        /// Sets chosenFile to the selected wallet filename.
        /// </summary>
        public string PromptPasswordWithPicker(
            List<WalletEntry> wallets, out string chosenFile)
        {
            int selected = Math.Max(0, wallets.FindIndex(w => w.IsActive));

            // ── Phase 1: Navigate wallet list, Enter to pick ──────────────────────
            while (true)
            {
                Console.Clear();
                int h = Console.WindowHeight, ww = Console.WindowWidth;
                int listH = Math.Min(wallets.Count, 6);
                int boxW = 54, boxH = listH + 6;
                int boxX = ww / 2 - boxW / 2, boxY = h / 2 - boxH / 2;
                DrawBox(boxX, boxW, boxY, boxH, "Select Wallet");

                int cx = boxX + 3, cy = boxY + 2;
                Console.SetCursorPosition(cx, cy);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("↑↓ = move   Enter = select");
                Console.ResetColor();

                for (int i = 0; i < wallets.Count && i < listH; i++)
                {
                    var entry = wallets[i];
                    Console.SetCursorPosition(cx, cy + 2 + i);
                    string icon = entry.IsActive ? "▶ " : "  ";
                    if (i == selected)
                    {
                        Console.BackgroundColor = ConsoleColor.DarkCyan;
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write($" {icon}{entry.Label}".PadRight(boxW - 4));
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = entry.IsActive ? ConsoleColor.Cyan : ConsoleColor.Gray;
                        Console.Write($" {icon}{entry.Label}");
                        Console.ResetColor();
                    }
                }

                var k = Console.ReadKey(intercept: true);
                if (k.Key == ConsoleKey.UpArrow && selected > 0) selected--;
                else if (k.Key == ConsoleKey.DownArrow && selected < wallets.Count - 1) selected++;
                else if (k.Key == ConsoleKey.Enter) break; // wallet chosen, go to password
            }

            // ── Phase 2: Password screen for the chosen wallet ────────────────────
            chosenFile = wallets[selected].FileName;

            Console.Clear();
            {
                int h = Console.WindowHeight, ww = Console.WindowWidth;
                int boxW = 54, boxH = 8;
                DrawBox(ww / 2 - boxW / 2, boxW, h / 2 - boxH / 2, boxH, "Enter Password");

                int cx = ww / 2 - boxW / 2 + 3;
                int cy = h / 2 - boxH / 2 + 2;
                Console.SetCursorPosition(cx, cy);
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"« {wallets[selected].Label} »");
                Console.ResetColor();
                Console.SetCursorPosition(cx, cy + 2);
                Console.Write("Password: ");
                Console.CursorVisible = true;
                var pwd = ReadPassword();
                Console.CursorVisible = false;
                return pwd;
            }
        }

        private string PromptPasswordInternal(string hint)
        {
            Console.Clear();
            int h = Console.WindowHeight, w = Console.WindowWidth;
            int boxH = string.IsNullOrWhiteSpace(hint) ? 8 : 10;
            int boxY = h / 2 - boxH / 2;
            DrawBox(w / 2 - 20, 40, boxY, boxH, "Unlock Wallet");

            int cy = boxY + 2;
            if (!string.IsNullOrWhiteSpace(hint))
            {
                WriteCenter($"« {hint} »", cy);
                cy += 2;
            }
            WriteCenter("Enter wallet password:", cy);
            Console.SetCursorPosition(w / 2 - 10, cy + 2);
            Console.CursorVisible = true;
            var pwd = ReadPassword();
            Console.CursorVisible = false;
            return pwd;
        }

        // ── Wallet manager ────────────────────────────────────────────────────────

        public enum WalletManagerAction { None, Switched, Created, Imported, Renamed }

        public (WalletManagerAction action, string? fileName, string? password) ShowWalletManager(
            List<WalletEntry> wallets)
        {
            int selected = wallets.FindIndex(w => w.IsActive);
            if (selected < 0) selected = 0;

            while (true)
            {
                RenderWalletManager(wallets, selected);
                var k = Console.ReadKey(intercept: true);

                if (k.Key == ConsoleKey.Escape || k.Key == ConsoleKey.Q)
                    return (WalletManagerAction.None, null, null);

                if (k.Key == ConsoleKey.UpArrow && selected > 0) { selected--; continue; }
                if (k.Key == ConsoleKey.DownArrow && selected < wallets.Count - 1) { selected++; continue; }

                if (k.Key == ConsoleKey.Enter && wallets.Count > 0)
                {
                    var entry = wallets[selected];
                    if (entry.IsActive) return (WalletManagerAction.None, null, null);
                    Console.Clear();
                    int h = Console.WindowHeight, ww = Console.WindowWidth;
                    DrawBox(ww / 2 - 20, 40, h / 2 - 3, 6, "Switch Wallet");
                    WriteCenter($"Unlocking \u00ab {entry.Label} \u00bb", h / 2 - 1);
                    Console.SetCursorPosition(ww / 2 - 10, h / 2 + 1);
                    Console.Write("Password: ");
                    Console.CursorVisible = true;
                    var pwd = ReadPassword();
                    Console.CursorVisible = false;
                    return (WalletManagerAction.Switched, entry.FileName, pwd);
                }

                if (k.Key == ConsoleKey.N)
                {
                    var (choice, wif, pwd, label) = PromptSetup();
                    return choice == SetupChoice.Create
                        ? (WalletManagerAction.Created, label, pwd)
                        : (WalletManagerAction.Imported, wif + "|" + label, pwd);
                }

                if (k.Key == ConsoleKey.R && wallets.Count > 0)
                {
                    Console.Clear();
                    int h = Console.WindowHeight, ww = Console.WindowWidth;
                    DrawBox(ww / 2 - 22, 44, h / 2 - 3, 6, "Rename Wallet");
                    Console.SetCursorPosition(ww / 2 - 18, h / 2 - 1);
                    Console.Write("New label: ");
                    Console.CursorVisible = true;
                    var newLabel = Console.ReadLine() ?? "";
                    Console.CursorVisible = false;
                    if (!string.IsNullOrWhiteSpace(newLabel))
                        return (WalletManagerAction.Renamed, wallets[selected].FileName, newLabel);
                }
            }
        }

        private void RenderWalletManager(List<WalletEntry> wallets, int selected)
        {
            Console.Clear();
            int h = Console.WindowHeight, w = Console.WindowWidth;
            int boxW = Math.Min(60, w - 6);
            int boxX = w / 2 - boxW / 2;
            int boxH = Math.Min(wallets.Count + 10, h - 4);
            int boxY = h / 2 - boxH / 2;
            DrawBox(boxX, boxW, boxY, boxH, "Wallet Manager");

            int listStart = boxY + 3;
            for (int i = 0; i < wallets.Count; i++)
            {
                var entry = wallets[i];
                int y = listStart + i;
                if (y >= boxY + boxH - 3) break;
                Console.SetCursorPosition(boxX + 2, y);
                string prefix = entry.IsActive ? "\u25b6 " : "  ";
                string tag = entry.IsActive ? " [active]" : "";
                if (i == selected)
                {
                    Console.BackgroundColor = ConsoleColor.White;
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.Write($"{prefix}{entry.Label}{tag}".PadRight(boxW - 4));
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = entry.IsActive ? ConsoleColor.Cyan : ConsoleColor.Gray;
                    Console.Write($"{prefix}{entry.Label}{tag}");
                    Console.ResetColor();
                }
            }
            if (wallets.Count == 0) WriteCenter("No wallets found.", listStart);

            int footerY = boxY + boxH - 2;
            string hints = "Enter=switch  N=new  R=rename  Esc=back";
            Console.SetCursorPosition(boxX + boxW / 2 - hints.Length / 2, footerY);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(hints);
            Console.ResetColor();
        }

        // ── Vanity address screen ─────────────────────────────────────────────────


        // ── VanitySearch auto-download screen ────────────────────────────────────

        /// <summary>
        /// Renders a live download progress screen.
        /// Returns true if the user pressed Esc to cancel.
        /// </summary>
        public bool DrawDownloadProgress(VanityRunner.DownloadProgress prog)
        {
            Console.Clear();
            int h = Console.WindowHeight, w = Console.WindowWidth;
            DrawBox(w / 2 - 28, 56, h / 2 - 6, 12, "Downloading VanitySearch");
            int cx = w / 2 - 24, cy = h / 2 - 4;

            Console.SetCursorPosition(cx, cy);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("VanitySearch binary not found.");
            Console.ResetColor();
            Console.SetCursorPosition(cx, cy + 1);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("Fetching latest release from GitHub...");
            Console.ResetColor();

            Console.SetCursorPosition(cx, cy + 3);
            Console.ForegroundColor = ConsoleColor.White;
            // Truncate message to fit box
            var msg = prog.StatusMessage;
            if (msg.Length > 50) msg = msg[..50];
            Console.Write(msg.PadRight(50));
            Console.ResetColor();

            // Progress bar
            if (prog.Percent >= 0)
            {
                int barW = 50;
                int filled = prog.Percent * barW / 100;
                Console.SetCursorPosition(cx, cy + 5);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("[");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(new string('█', filled));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(new string('░', barW - filled));
                Console.Write($"] {prog.Percent,3}%");
                Console.ResetColor();
            }
            else
            {
                // Indeterminate spinner
                int spin = (int)(DateTime.UtcNow.Millisecond / 200) % 4;
                string[] frames = { "|", "/", "-", "\\" };
                Console.SetCursorPosition(cx, cy + 5);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"{frames[spin]} Working...");
                Console.ResetColor();
            }

            Console.SetCursorPosition(cx, cy + 7);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("Esc = cancel");
            Console.ResetColor();

            if (Console.KeyAvailable)
            {
                var k = Console.ReadKey(intercept: true);
                return k.Key == ConsoleKey.Escape;
            }
            return false;
        }


        // Valid prefix types the user can choose from
        private static readonly (string Display, string Required, string Desc)[] _prefixTypes =
        {
            ("bc1q...  native SegWit (P2WPKH)", "bc1q", "Most common, lowest fees"),
            ("bc1p...  Taproot (P2TR)",          "bc1p", "Newest, maximum privacy"),
            ("1...     Legacy (P2PKH)",           "1",    "Widest compatibility"),
            ("3...     P2SH wrapped SegWit",      "3",    "Script hash addresses"),
        };

        public (string prefix, bool confirmed) PromptVanityPrefix(
            VanityRunner.ComputeMode mode, string? binaryPath)
        {
            int typeSelected = 0;

        // ── Phase 1: Pick address type ────────────────────────────────────────
        typeSelect:
            while (true)
            {
                Console.Clear();
                int h = Console.WindowHeight, w = Console.WindowWidth;
                int boxW = 64, boxH = _prefixTypes.Length + 10;
                DrawBox(w / 2 - boxW / 2, boxW, h / 2 - boxH / 2, boxH, "Vanity Address Generator");
                int cx = w / 2 - boxW / 2 + 3, cy = h / 2 - boxH / 2 + 2;

                Console.SetCursorPosition(cx, cy);
                if (binaryPath is null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("[ ! ] VanitySearch not found — will auto-download");
                    Console.ResetColor();
                }
                else if (mode == VanityRunner.ComputeMode.Gpu)
                {
                    Console.BackgroundColor = ConsoleColor.DarkGreen;
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write("██ GPU ACCELERATED ██  CUDA active — maximum speed");
                    Console.ResetColor();
                }
                else
                {
                    Console.BackgroundColor = ConsoleColor.DarkYellow;
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.Write("░░ CPU ONLY ░░  No CUDA GPU — install CUDA drivers for GPU speed");
                    Console.ResetColor();
                }

                Console.SetCursorPosition(cx, cy + 2);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("Choose address type:  ↑↓ = move   Enter = select   Esc = cancel");
                Console.ResetColor();

                for (int i = 0; i < _prefixTypes.Length; i++)
                {
                    var (display, _, desc) = _prefixTypes[i];
                    Console.SetCursorPosition(cx, cy + 4 + i);
                    if (i == typeSelected)
                    {
                        Console.BackgroundColor = ConsoleColor.DarkCyan;
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write($" ▶ {display,-30}  {desc}".PadRight(boxW - 4));
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Gray;
                        Console.Write($"   {display,-30}  {desc}");
                        Console.ResetColor();
                    }
                }

                bool bc1 = _prefixTypes[typeSelected].Required.StartsWith("bc1");
                Console.SetCursorPosition(cx, cy + 4 + _prefixTypes.Length + 1);
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.Write(bc1 ? "Each extra char ~32x harder (bech32 charset)"
                                  : "Each extra char ~58x harder (base58 charset)");
                Console.ResetColor();

                var k = Console.ReadKey(intercept: true);
                if (k.Key == ConsoleKey.Escape) return ("", false);
                if (k.Key == ConsoleKey.UpArrow && typeSelected > 0) typeSelected--;
                else if (k.Key == ConsoleKey.DownArrow && typeSelected < _prefixTypes.Length - 1) typeSelected++;
                else if (k.Key == ConsoleKey.Enter) break;
            }

            string required = _prefixTypes[typeSelected].Required;
            bool isBech32 = required.StartsWith("bc1");
            const string bech32Chars = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
            const string base58Chars = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

            // ── Phase 2: Type the suffix char by char ─────────────────────────────
            string suffix = "";
            string error = "";
            while (true)
            {
                Console.Clear();
                int h = Console.WindowHeight, w = Console.WindowWidth;
                int boxW = 64, boxH = 16;
                DrawBox(w / 2 - boxW / 2, boxW, h / 2 - boxH / 2, boxH, "Enter Vanity Suffix");
                int cx = w / 2 - boxW / 2 + 3, cy = h / 2 - boxH / 2 + 2;

                Console.SetCursorPosition(cx, cy);
                Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write("Fixed prefix:  "); Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.Cyan; Console.Write(required); Console.ResetColor();

                Console.SetCursorPosition(cx, cy + 2);
                Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write("Your suffix:   "); Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(suffix.Length == 0 ? "(type below)" : suffix);
                Console.ResetColor();

                Console.SetCursorPosition(cx, cy + 4);
                Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write("Full pattern:  "); Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.Yellow; Console.Write(required + suffix); Console.ResetColor();

                if (suffix.Length > 0)
                {
                    int charSpace = isBech32 ? 32 : 58;
                    double diff = Math.Pow(charSpace, suffix.Length);
                    string diffStr = diff < 1e6 ? $"{diff:N0} keys avg"
                                   : diff < 1e9 ? $"{diff / 1e6:F1}M keys avg"
                                                : $"{diff / 1e9:F1}B keys avg";
                    Console.SetCursorPosition(cx, cy + 6);
                    Console.ForegroundColor = suffix.Length <= 3 ? ConsoleColor.Green
                                            : suffix.Length <= 5 ? ConsoleColor.Yellow
                                                                  : ConsoleColor.Red;
                    Console.Write($"Difficulty:    {diffStr}  ({suffix.Length} suffix chars)");
                    Console.ResetColor();

                    // Valid charset hint
                    Console.SetCursorPosition(cx, cy + 7);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write(isBech32 ? "Valid chars:   qpzry9x8gf2tvdw0s3jn54khce6mua7l"
                                           : "Valid chars:   1-9 A-H J-N P-Z a-k m-z  (no 0,O,I,l)");
                    Console.ResetColor();
                }

                if (error != "")
                {
                    Console.SetCursorPosition(cx, cy + 9);
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write(error);
                    Console.ResetColor();
                }

                Console.SetCursorPosition(cx, cy + 11);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("Enter = confirm   Backspace = delete   Esc = back to type");
                Console.ResetColor();

                var k = Console.ReadKey(intercept: true);
                if (k.Key == ConsoleKey.Escape) goto typeSelect;
                if (k.Key == ConsoleKey.Backspace && suffix.Length > 0) { suffix = suffix[..^1]; error = ""; continue; }
                if (k.Key == ConsoleKey.Backspace) continue;

                if (k.Key == ConsoleKey.Enter)
                {
                    if (suffix.Length == 0) { error = "Please type at least 1 character."; continue; }
                    return (required + suffix, binaryPath is not null);
                }

                if (!char.IsControl(k.KeyChar))
                {
                    char c = k.KeyChar;
                    string valid = isBech32 ? bech32Chars : base58Chars;
                    if (!valid.Contains(char.ToLower(c)))
                    {
                        error = isBech32
                            ? $"Invalid: '{c}' not in bech32 charset"
                            : $"Invalid: '{c}' not in base58 charset (no 0,O,I,l)";
                    }
                    else { suffix += char.ToLower(c); error = ""; }
                }
            }
        }


        // Rolling speed history for mini spark-graph
        private readonly System.Collections.Generic.Queue<double> _speedHistory = new();

        public bool DrawVanityProgress(
            string prefix,
            VanityRunner.VanityProgress? latest,
            VanityRunner.ComputeMode mode,
            TimeSpan elapsed,
            bool done)
        {
            Console.Clear();
            int h = Console.WindowHeight, w = Console.WindowWidth;
            int boxW = Math.Min(66, w - 4);
            int boxH = 20;
            int boxX = w / 2 - boxW / 2, boxY = h / 2 - boxH / 2;
            string title = done ? "*** ADDRESS FOUND! ***" : "Vanity Search";
            DrawBox(boxX, boxW, boxY, boxH, title);
            int cx = boxX + 3, cy = boxY + 2;

            // ── Row 0: prominent compute mode banner ──────────────────────────────
            bool gpuActive = mode == VanityRunner.ComputeMode.Gpu;
            string modeLine = gpuActive
                ? "██ GPU ACCELERATED ██  CUDA active — maximum speed"
                : "░░ CPU ONLY ░░  No CUDA GPU — slower search";
            Console.SetCursorPosition(cx, cy);
            Console.BackgroundColor = gpuActive ? ConsoleColor.DarkGreen : ConsoleColor.DarkYellow;
            Console.ForegroundColor = gpuActive ? ConsoleColor.White : ConsoleColor.Black;
            Console.Write(modeLine.PadRight(boxW - 6));
            Console.ResetColor();
            Console.SetCursorPosition(cx, cy + 1);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"Elapsed: {elapsed:hh\\:mm\\:ss}");
            Console.ResetColor();

            // ── Row 1: target prefix ──────────────────────────────────────────────
            Console.SetCursorPosition(cx, cy + 2);
            Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write("Target:  "); Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Cyan; Console.Write(prefix); Console.ResetColor();

            if (latest != null)
            {
                // ── Row 2-3: speed stats ──────────────────────────────────────────
                Console.SetCursorPosition(cx, cy + 4);
                Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write("Speed:   "); Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(latest.SpeedLabel.PadRight(24));
                Console.ResetColor();

                if (!string.IsNullOrEmpty(latest.GpuSpeed))
                {
                    Console.SetCursorPosition(cx, cy + 5);
                    Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write("GPU:     "); Console.ResetColor();
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write(latest.GpuSpeed.PadRight(24));
                    Console.ResetColor();
                }

                // ── Row 4-5: probability + ETA ────────────────────────────────────
                Console.SetCursorPosition(cx, cy + 6);
                Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write("Prob:    "); Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(latest.Probability.PadRight(12));
                Console.ResetColor();

                if (!string.IsNullOrEmpty(latest.Eta))
                {
                    Console.SetCursorPosition(cx + 24, cy + 6);
                    Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write("ETA 50%: "); Console.ResetColor();
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.Write(latest.Eta);
                    Console.ResetColor();
                }

                if (!string.IsNullOrEmpty(latest.Difficulty))
                {
                    Console.SetCursorPosition(cx, cy + 7);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($"Diff:    {latest.Difficulty}");
                    Console.ResetColor();
                }

                // ── Speed spark-graph (last 50 readings) ──────────────────────────
                if (double.TryParse(
                    System.Text.RegularExpressions.Regex.Match(latest.SpeedLabel, @"[\d.]+").Value,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double spd))
                {
                    _speedHistory.Enqueue(spd);
                    if (_speedHistory.Count > 50) _speedHistory.Dequeue();
                }

                if (_speedHistory.Count > 1)
                {
                    int graphW = boxW - 6;
                    int graphY = cy + 9;
                    Console.SetCursorPosition(cx, graphY - 1);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("Speed history:");
                    Console.ResetColor();

                    double[] speeds = _speedHistory.ToArray();
                    double maxSpd = 0;
                    foreach (var s in speeds) if (s > maxSpd) maxSpd = s;
                    if (maxSpd == 0) maxSpd = 1;

                    // 3-row spark graph using block chars
                    string[] blocks = { " ", "▁", "▂", "▃", "▄", "▅", "▆", "▇", "█" };
                    int take = Math.Min(speeds.Length, graphW);
                    var recent = speeds[(speeds.Length - take)..];

                    Console.SetCursorPosition(cx, graphY);
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    foreach (var s in recent)
                    {
                        int idx = (int)(s / maxSpd * 8);
                        idx = Math.Clamp(idx, 0, 8);
                        Console.Write(blocks[idx]);
                    }
                    Console.ResetColor();
                }

                // ── Probability fill bar ──────────────────────────────────────────
                if (!string.IsNullOrEmpty(latest.Probability) &&
                    double.TryParse(
                        latest.Probability.TrimEnd('%'),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double pct))
                {
                    int barW = boxW - 6;
                    int filled = (int)(pct / 100.0 * barW);
                    int barY = cy + 13;
                    Console.SetCursorPosition(cx, barY - 1);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($"Probability to have found it by now:");
                    Console.ResetColor();
                    Console.SetCursorPosition(cx, barY);
                    Console.ForegroundColor = pct < 30 ? ConsoleColor.Green
                                            : pct < 70 ? ConsoleColor.Yellow
                                                       : ConsoleColor.Red;
                    Console.Write(new string('█', filled));
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write(new string('░', barW - filled));
                    Console.Write($" {pct:F1}%");
                    Console.ResetColor();
                }
            }
            else
            {
                // Startup spinner
                int spin = (int)(elapsed.TotalMilliseconds / 150) % 4;
                string[] frames = { "|", "/", "-", "\\" };
                Console.SetCursorPosition(cx, cy + 4);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"{frames[spin]}  Starting VanitySearch...");
                Console.ResetColor();
            }

            // ── Footer ────────────────────────────────────────────────────────────
            Console.SetCursorPosition(cx, boxY + boxH - 2);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(done ? "Press any key to continue."
                               : "Esc = cancel search");
            Console.ResetColor();

            if (Console.KeyAvailable)
            {
                var k = Console.ReadKey(intercept: true);
                return k.Key == ConsoleKey.Escape;
            }
            return false;
        }

        /// <summary>
        /// Full-screen vanity result. Stays visible until the user makes a choice.
        /// Returns 'I' = import, 'C' = copy WIF, 'A' = copy address, anything else = back.
        /// </summary>
        public char ShowVanityResult(VanityRunner.VanityResult result, VanityRunner.ComputeMode mode)
        {
            while (true)
            {
                Console.Clear();
                int h = Console.WindowHeight, w = Console.WindowWidth;
                int boxW = Math.Min(72, w - 4);
                int boxH = 20;
                int boxX = w / 2 - boxW / 2, boxY = h / 2 - boxH / 2;
                DrawBox(boxX, boxW, boxY, boxH, "✨  Vanity Address Found!  ✨");
                int cx = boxX + 3, cy = boxY + 2;

                // ── Compute mode banner ───────────────────────────────────────────
                bool gpu = mode == VanityRunner.ComputeMode.Gpu;
                Console.SetCursorPosition(cx, cy);
                Console.BackgroundColor = gpu ? ConsoleColor.DarkGreen : ConsoleColor.DarkYellow;
                Console.ForegroundColor = gpu ? ConsoleColor.White : ConsoleColor.Black;
                Console.Write(gpu ? "██ FOUND BY GPU ██" : "░░ FOUND BY CPU ░░");
                Console.ResetColor();

                // ── Address ───────────────────────────────────────────────────────
                Console.SetCursorPosition(cx, cy + 2);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("Bitcoin Address:");
                Console.ResetColor();

                Console.SetCursorPosition(cx, cy + 3);
                Console.BackgroundColor = ConsoleColor.DarkBlue;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write((" " + result.Address + " ").PadRight(boxW - 4));
                Console.ResetColor();

                // ── Private key ───────────────────────────────────────────────────
                Console.SetCursorPosition(cx, cy + 5);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("Private Key (WIF):");
                Console.ResetColor();

                Console.SetCursorPosition(cx, cy + 6);
                Console.BackgroundColor = ConsoleColor.DarkRed;
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write((" " + result.Wif + " ").PadRight(boxW - 4));
                Console.ResetColor();

                Console.SetCursorPosition(cx, cy + 8);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("Private Key (HEX):");
                Console.ResetColor();
                Console.SetCursorPosition(cx, cy + 9);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                // Truncate hex to fit box
                var hex = result.HexKey.Length > boxW - 6
                    ? result.HexKey[..(boxW - 9)] + "..."
                    : result.HexKey;
                Console.Write(hex);
                Console.ResetColor();

                // ── Warning ───────────────────────────────────────────────────────
                Console.SetCursorPosition(cx, cy + 11);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("⚠  SAVE YOUR PRIVATE KEY NOW — this screen will not reappear.");
                Console.ResetColor();
                Console.SetCursorPosition(cx, cy + 12);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("    Key was generated by VanitySearch. Keep it secret and safe.");
                Console.ResetColor();

                // ── Action menu ───────────────────────────────────────────────────
                Console.SetCursorPosition(cx, cy + 14);
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("[ I ] Import as wallet   ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("[ C ] Copy WIF key   ");
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write("[ A ] Copy address");
                Console.ResetColor();
                Console.SetCursorPosition(cx, cy + 15);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("[ any other key ] Back to wallet (keys shown above will be lost!)");
                Console.ResetColor();

                Console.CursorVisible = false;
                var k = Console.ReadKey(intercept: true);
                char choice = char.ToUpper(k.KeyChar);

                if (choice == 'C')
                {
                    TryCopyToClipboard(result.Wif);
                    // Flash confirmation on same screen
                    Console.SetCursorPosition(cx, cy + 17);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("✓ WIF private key copied to clipboard. Press any key...");
                    Console.ResetColor();
                    Console.ReadKey(intercept: true);
                    continue; // re-render so they can still choose I or A
                }
                if (choice == 'A')
                {
                    TryCopyToClipboard(result.Address);
                    Console.SetCursorPosition(cx, cy + 17);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("✓ Address copied to clipboard. Press any key...");
                    Console.ResetColor();
                    Console.ReadKey(intercept: true);
                    continue; // re-render
                }
                return choice; // 'I' or anything else
            }
        }

        // ── Background price chart ────────────────────────────────────────────────


        private static void DrawComputeBadge(VanityRunner.ComputeMode mode, int h)
        {
            // Bottom-left corner, just above the menu box
            bool gpu = mode == VanityRunner.ComputeMode.Gpu;
            string badge = gpu ? "[█GPU█]" : "[░CPU░]";
            Console.SetCursorPosition(1, 0);
            Console.BackgroundColor = gpu ? ConsoleColor.DarkGreen : ConsoleColor.DarkYellow;
            Console.ForegroundColor = gpu ? ConsoleColor.White : ConsoleColor.Black;
            Console.Write(badge);
            Console.ResetColor();
        }

        private void DrawPriceChart(PriceTicker ticker, int h, int w)
        {
            var history = ticker.GetHourlySnapshot();
            decimal live = ticker.Price;
            if (history.Length == 0 && live == 0m) return;

            decimal[] points;
            if (live > 0m)
            {
                points = new decimal[history.Length + 1];
                history.CopyTo(points, 0);
                points[history.Length] = live;
            }
            else { points = history; }

            if (points.Length < 2) return;

            int chartTop = 2, chartBottom = h - 2;
            int chartH = chartBottom - chartTop;
            if (chartH < 3) return;

            int maxPoints = w - 2;
            int startIdx = Math.Max(0, points.Length - maxPoints);
            var visible = new ArraySegment<decimal>(points, startIdx, points.Length - startIdx);

            decimal min = decimal.MaxValue, max = decimal.MinValue;
            foreach (var p in visible) { if (p < min) min = p; if (p > max) max = p; }
            if (min == max) max = min + 1;

            int PriceToRow(decimal price)
            {
                double ratio = (double)((price - min) / (max - min));
                return Math.Clamp(chartBottom - (int)(ratio * chartH), chartTop, chartBottom);
            }

            var uiRows = BuildUiRowSet(h);
            int[] rows = new int[visible.Count];
            for (int i = 0; i < visible.Count; i++) rows[i] = PriceToRow(visible[i]);

            int originX = w - 2 - (visible.Count - 1);
            for (int i = 0; i < visible.Count - 1; i++)
                DrawLineSegment(originX + i, rows[i], originX + i + 1, rows[i + 1],
                                uiRows, w, chartTop, chartBottom);

            int liveX = w - 2, liveRow = rows[visible.Count - 1];
            if (liveX >= 0 && liveX < w && liveRow >= chartTop && liveRow <= chartBottom)
            {
                Console.SetCursorPosition(liveX, liveRow);
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("\u25cf");
                Console.ResetColor();
            }
        }

        private static void DrawLineSegment(int x0, int y0, int x1, int y1,
            HashSet<int> uiRows, int w, int top, int bottom)
        {
            int steps = Math.Max(1, Math.Abs(x1 - x0));
            for (int s = 0; s <= steps; s++)
            {
                int x = x0 + s;
                int y = y0 + (int)Math.Round((double)(y1 - y0) * s / steps);
                DrawChartColumn(x, y, y, uiRows, w, top, bottom);
            }
            int yMin = Math.Min(y0, y1), yMax = Math.Max(y0, y1);
            DrawChartColumn(x0, yMin, yMax, uiRows, w, top, bottom);
            DrawChartColumn(x1, yMin, yMax, uiRows, w, top, bottom);
        }

        private static void DrawChartColumn(int x, int yMin, int yMax,
            HashSet<int> uiRows, int w, int top, int bottom)
        {
            if (x < 0 || x >= w) return;
            yMin = Math.Clamp(yMin, top, bottom);
            yMax = Math.Clamp(yMax, top, bottom);
            for (int y = yMin; y <= yMax; y++)
            {
                Console.SetCursorPosition(x, y);
                Console.ForegroundColor = uiRows.Contains(y)
                    ? ConsoleColor.DarkYellow : ConsoleColor.Yellow;
                Console.Write(y == yMin && y == yMax ? "\u2502"
                            : y == yMin ? "\u2575"
                            : y == yMax ? "\u2577"
                                        : "\u2502");
                Console.ResetColor();
            }
        }

        private static HashSet<int> BuildUiRowSet(int h)
        {
            var set = new HashSet<int> { 0, 1, 2 };

            // Balance row: matches DrawBalance calculation
            int menuCount = Enum.GetValues<MenuAction>().Length;
            int topEdge = 3;
            int bottomEdge = h - menuCount - 3;
            int balRow = topEdge + (bottomEdge - topEdge) / 2;
            set.Add(balRow); set.Add(balRow + 1);

            // Left menu box: bottom-left, sits above hints row
            int menuH = menuCount + 2;
            int menuY = h - menuH - 1;
            for (int i = 0; i < menuH; i++) set.Add(menuY + i);

            // Transactions box: bottom-right
            int txPanelH = TxWindowSize + 4;
            int txPanelY = h - txPanelH - 1;
            for (int i = 0; i < txPanelH; i++) set.Add(txPanelY + i);

            // Hints bar
            set.Add(h - 1);
            return set;
        }

        // ── UI layers ─────────────────────────────────────────────────────────────

        private void DrawAddress(string address, string label, FocusZone focus, int w)
        {
            if (!string.IsNullOrWhiteSpace(label))
            {
                var ls = $"\u00ab {label} \u00bb";
                Console.SetCursorPosition(0, 0);
                Console.Write(new string(' ', w));
                Console.SetCursorPosition(Math.Max(0, w / 2 - ls.Length / 2), 0);
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(ls);
                Console.ResetColor();
            }

            var str = $"[ {address} ]";
            Console.SetCursorPosition(0, 1);
            Console.Write(new string(' ', w));
            int x = Math.Max(0, w / 2 - str.Length / 2);
            Console.SetCursorPosition(x, 1);

            if (focus == FocusZone.Top)
            {
                var hint = $"[ {address} ] \u2190 Enter to copy";
                x = Math.Max(0, w / 2 - hint.Length / 2);
                Console.SetCursorPosition(x, 1);
                Console.BackgroundColor = ConsoleColor.DarkGray;
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(hint);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(str);
            }
            Console.ResetColor();
        }

        private void DrawPrice(PriceTicker ticker, int w)
        {
            var text = ticker.Formatted();
            int x = Math.Max(0, w - text.Length - 2);
            Console.SetCursorPosition(x, 0);
            Console.ForegroundColor = ticker.Direction switch
            {
                1 => ConsoleColor.Green,
                -1 => ConsoleColor.Red,
                _ => ConsoleColor.DarkGray
            };
            Console.Write(text);
            Console.ResetColor();
        }

        private void DrawBalance(decimal balance, decimal unconfirmed, int h, int w)
        {
            // Pin balance in the true vertical centre of the usable middle zone
            // Top zone ends at row 2; bottom zone starts at (h - menuCount - 3)
            int menuCount = Enum.GetValues<MenuAction>().Length;
            int topEdge = 3;
            int bottomEdge = h - menuCount - 3;
            int by = topEdge + (bottomEdge - topEdge) / 2;

            var str = $"{balance:F5} BTC";
            Console.SetCursorPosition(0, by); Console.Write(new string(' ', w));
            Console.SetCursorPosition(Math.Max(0, w / 2 - str.Length / 2), by);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(str);
            Console.ResetColor();

            if (unconfirmed != 0)
            {
                var pend = $"({unconfirmed:+F5;-F5} pending)";
                int py = by + 1;
                Console.SetCursorPosition(0, py); Console.Write(new string(' ', w));
                Console.SetCursorPosition(Math.Max(0, w / 2 - pend.Length / 2), py);
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.Write(pend);
                Console.ResetColor();
            }
        }

        private void DrawLeftMenu(FocusZone focus, int selectedRow, int h)
        {
            var items = Enum.GetNames<MenuAction>();
            // Anchor: bottom-left corner, 2 cols from edge, 1 row above hints bar
            int menuW = 14;
            int menuH = items.Length + 2;
            int menuX = 1;
            int menuY = h - menuH - 1;   // sits just above the hints row

            DrawBox(menuX, menuW, menuY, menuH, "");

            for (int i = 0; i < items.Length; i++)
            {
                int y = menuY + 1 + i;
                Console.SetCursorPosition(menuX + 2, y);
                if (focus == FocusZone.Left && i == selectedRow)
                {
                    Console.BackgroundColor = ConsoleColor.White;
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.Write($"> {items[i]}".PadRight(menuW - 3));
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write($"  {items[i]}");
                    Console.ResetColor();
                }
            }
        }

        private void DrawTransactions(Models.Transaction[] txs, FocusZone focus,
            int selectedRow, int scroll, int h, int w)
        {
            // Anchor: bottom-right corner, fixed width panel
            const int PanelW = 44;
            const int PanelH = TxWindowSize + 4;
            int panelX = w - PanelW - 1;          // 1 col from right edge
            int panelY = h - PanelH - 1;           // just above hints row
            panelX = Math.Max(0, panelX);

            DrawBox(panelX, PanelW, panelY, PanelH, "Transactions");

            int cx = panelX + 2;

            if (txs.Length == 0)
            {
                Console.SetCursorPosition(cx, panelY + 2);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("No transactions yet.");
                Console.ResetColor();
                return;
            }

            var visible = new ArraySegment<Models.Transaction>(
                txs,
                Math.Min(scroll, Math.Max(0, txs.Length - 1)),
                Math.Min(TxWindowSize, Math.Max(0, txs.Length - scroll)));

            int idx = 0;
            foreach (var tx in visible)
            {
                int y = panelY + 2 + idx;
                var line = $"{(tx.Confirmed ? " " : "~")}{tx.FormattedAmount,9}  {tx.ShortId}";
                int maxLen = PanelW - 4;
                if (line.Length > maxLen) line = line[..maxLen];
                Console.SetCursorPosition(cx, y);
                if (focus == FocusZone.Right && idx == selectedRow)
                {
                    Console.BackgroundColor = ConsoleColor.White;
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.Write(line.PadRight(maxLen));
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = tx.Amount >= 0 ? ConsoleColor.Green : ConsoleColor.Red;
                    Console.Write(line);
                    Console.ResetColor();
                }
                idx++;
            }
        }

        private void DrawHints(int h, int w)
        {
            var hints = "\u2191\u2193 navigate  \u2190\u2192 panels  Enter select  R refresh  Q quit";
            Console.SetCursorPosition(Math.Max(0, w / 2 - hints.Length / 2), h - 1);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(hints);
            Console.ResetColor();
        }

        private void DrawFlash(string msg, bool error, int h, int w)
        {
            Console.SetCursorPosition(0, h - 2);
            Console.Write(new string(' ', w));
            Console.ForegroundColor = error ? ConsoleColor.Red : ConsoleColor.Green;
            Console.SetCursorPosition(Math.Max(0, w / 2 - msg.Length / 2), h - 2);
            Console.Write(msg);
            Console.ResetColor();
        }

        // ── Box drawing ───────────────────────────────────────────────────────────

        private static void DrawBox(int x, int width, int y, int height, string title)
        {
            x = Math.Max(0, x);
            width = Math.Min(width, Console.WindowWidth - x);
            y = Math.Max(0, y);
            height = Math.Min(height, Console.WindowHeight - y);
            if (width < 4 || height < 2) return;

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.SetCursorPosition(x, y);
            Console.Write("\u250c" + new string('\u2500', width - 2) + "\u2510");

            if (!string.IsNullOrWhiteSpace(title))
            {
                var t = $" {title} ";
                int tx = x + (width / 2 - t.Length / 2);
                Console.SetCursorPosition(Math.Max(x + 1, tx), y);
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(t);
                Console.ForegroundColor = ConsoleColor.DarkGray;
            }

            for (int row = y + 1; row < y + height - 1; row++)
            {
                Console.SetCursorPosition(x, row); Console.Write("\u2502");
                Console.SetCursorPosition(x + width - 1, row); Console.Write("\u2502");
            }

            Console.SetCursorPosition(x, y + height - 1);
            Console.Write("\u2514" + new string('\u2500', width - 2) + "\u2518");
            Console.ResetColor();
        }

        // ── Utilities ─────────────────────────────────────────────────────────────

        public void WriteCenter(string text, int y, bool highlight = false)
        {
            int w = Console.WindowWidth;
            int x = Math.Max(0, w / 2 - text.Length / 2);
            Console.SetCursorPosition(x, y);
            if (highlight)
            {
                Console.BackgroundColor = ConsoleColor.DarkGray;
                Console.ForegroundColor = ConsoleColor.White;
            }
            Console.Write(text);
            Console.ResetColor();
        }

        public static void TryCopyToClipboard(string text)
        {
            try
            {
                var proc = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = OperatingSystem.IsWindows() ? "clip" : "xclip",
                        Arguments = OperatingSystem.IsWindows() ? "" : "-selection clipboard",
                        RedirectStandardInput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                proc.Start();
                proc.StandardInput.Write(text);
                proc.StandardInput.Close();
            }
            catch { }
        }

        private static string ReadPassword()
        {
            var pwd = "";
            while (true)
            {
                var k = Console.ReadKey(intercept: true);
                // Ignore Enter until at least one character typed — prevents
                // accidental empty-password submit when selecting a wallet
                if (k.Key == ConsoleKey.Enter && pwd.Length == 0) continue;
                if (k.Key == ConsoleKey.Enter) break;
                if (k.Key == ConsoleKey.Backspace && pwd.Length > 0)
                {
                    pwd = pwd[..^1];
                    Console.Write("\b \b");
                }
                else if (k.Key != ConsoleKey.Backspace && !char.IsControl(k.KeyChar))
                {
                    pwd += k.KeyChar;
                    Console.Write("*");
                }
            }
            return pwd;
        }
    }

    public enum SetupChoice { Create, Import }
}