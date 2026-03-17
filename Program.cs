using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TUIWallet.Models;

namespace TUIWallet
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            bool testnet = args.Length > 0 && args[0] == "--testnet";

            var api = new BlockstreamClient(testnet);
            var keys = new KeyManager(testnet);
            var service = new WalletService(api, keys);
            var renderer = new WalletRenderer();
            var input = new InputHandler();
            var ui = new UiState();
            var ticker = new PriceTicker();

            Console.CursorVisible = false;
            Console.Title = testnet ? "TUI Wallet [TESTNET]" : "TUI Wallet";

            // ── Detect VanitySearch binary + GPU once at startup ──────────────────
            string? vanityBin = VanityRunner.FindBinary();
            var vanityMode = vanityBin is not null
                ? VanityRunner.DetectMode(vanityBin)
                : VanityRunner.ComputeMode.CpuOnly;
            // vanityBin may be updated later if user triggers auto-download

            // ── Live price feed ───────────────────────────────────────────────────
            var priceCts = new CancellationTokenSource();
            _ = Task.Run(() => RunPriceStreamAsync(ticker, priceCts.Token));

            // ── Seed chart with last 168 hours (1 week) of history ────────────────
            _ = Task.Run(async () =>
            {
                try
                {
                    var closes = await FetchKlineHistoryAsync(testnet);
                    if (closes.Length > 0) ticker.Seed(closes);
                }
                catch { /* non-critical, chart just starts empty */ }
            });

            // ── First-run or unlock ───────────────────────────────────────────────
            if (!service.HasWallet)
            {
                var (choice, wifInput, password, label) = renderer.PromptSetup();
                _ = choice == SetupChoice.Create
                    ? service.CreateWallet(password, label)
                    : service.ImportWif(wifInput, password, label);
            }
            else
            {
                bool unlocked = false;
                while (!unlocked)
                {
                    var wallets = service.ListWallets();
                    string password;
                    if (wallets.Count == 1)
                    {
                        // Only one wallet — simple password prompt with its label
                        password = renderer.PromptPassword(wallets[0].Label);
                        unlocked = service.LoadWallet(password);
                    }
                    else
                    {
                        // Multiple wallets — show picker so user knows which they're unlocking
                        password = renderer.PromptPasswordWithPicker(wallets, out string chosen);
                        unlocked = service.SwitchWallet(chosen, password);
                    }
                    if (!unlocked)
                        renderer.ShowMessage("Wrong password. Try again.", isError: true);
                }
            }

            // ── Initial data load ─────────────────────────────────────────────────
            WalletState state;
            try
            {
                // Live loading screen while fetch runs
                var fetchTask = service.RefreshAsync();
                int frame = 0;
                while (!fetchTask.IsCompleted)
                {
                    renderer.DrawLoading(
                        "Fetching wallet data",
                        frame % 3 == 0 ? "Connecting to Blockstream API..."
                      : frame % 3 == 1 ? "Loading transactions..."
                                       : "Calculating balance...");
                    frame++;
                    await Task.WhenAny(fetchTask, Task.Delay(120));
                }
                state = await fetchTask;
            }
            catch (Exception ex)
            {
                renderer.ShowMessage($"Could not reach Blockstream API:\n{ex.Message}", isError: true);
                priceCts.Cancel();
                return;
            }

            UpdateTitle(testnet, state.Label);

            // ── Main loop ─────────────────────────────────────────────────────────
            while (true)
            {
                renderer.Draw(state, ui, ticker, vanityBin is not null ? vanityMode : null);

                var key = Console.ReadKey(intercept: true);
                var ev = input.Handle(key, ui, state.Transactions.Length);

                switch (ev)
                {
                    case AppEvent.Quit:
                        priceCts.Cancel();
                        Console.Clear();
                        Console.CursorVisible = true;
                        return;

                    case AppEvent.Refresh:
                        state = await TryRefreshAsync(service, renderer, state);
                        break;

                    case AppEvent.Send:
                        state = await HandleSendAsync(service, renderer, state);
                        break;

                    case AppEvent.Receive:
                        renderer.ShowReceiveAddress(state.Address);
                        break;

                    case AppEvent.Keys:
                        renderer.ShowKeys(service.GetWif(), state.Address);
                        break;

                    case AppEvent.Wallets:
                        state = await HandleWalletManagerAsync(service, renderer, state, testnet);
                        UpdateTitle(testnet, state.Label);
                        break;

                    case AppEvent.Vanity:
                        state = await HandleVanityAsync(
                            service, renderer, state, vanityBin, vanityMode, testnet);
                        UpdateTitle(testnet, state.Label);
                        break;

                    case AppEvent.CopyAddress:
                        WalletRenderer.TryCopyToClipboard(state.Address);
                        renderer.Flash("\u2713 Address copied to clipboard");
                        break;

                    case AppEvent.None:
                    default:
                        break;
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static void UpdateTitle(bool testnet, string label)
        {
            string net = testnet ? "[TESTNET] " : "";
            string lbl = string.IsNullOrWhiteSpace(label) ? "" : $" \u2014 {label}";
            Console.Title = $"TUI Wallet {net}{lbl}".Trim();
        }

        private static async Task<WalletState> TryRefreshAsync(
            WalletService service, WalletRenderer renderer, WalletState current)
        {
            try
            {
                var task = service.RefreshAsync();
                int frame = 0;
                while (!task.IsCompleted)
                {
                    renderer.DrawLoading("Refreshing wallet",
                        frame % 2 == 0 ? "Fetching balance..." : "Loading transactions...");
                    frame++;
                    await Task.WhenAny(task, Task.Delay(120));
                }
                return await task;
            }
            catch (Exception ex)
            {
                renderer.ShowMessage($"Refresh failed:\n{ex.Message}", isError: true);
                return current;
            }
        }

        private static async Task<WalletState> HandleSendAsync(
            WalletService service, WalletRenderer renderer, WalletState state)
        {
            var (toAddress, amount) = renderer.PromptSend(state.Balance);
            if (string.IsNullOrWhiteSpace(toAddress) || amount <= 0)
            { renderer.ShowMessage("Send cancelled.", isError: false); return state; }
            try
            {
                var txid = await service.SendAsync(toAddress, amount);
                renderer.ShowMessage($"Sent!\nTxID: {txid}", isError: false);
                return await service.GetStateAsync();
            }
            catch (Exception ex)
            { renderer.ShowMessage($"Send failed:\n{ex.Message}", isError: true); return state; }
        }

        private static async Task<WalletState> HandleWalletManagerAsync(
            WalletService service, WalletRenderer renderer,
            WalletState current, bool testnet)
        {
            var wallets = service.ListWallets();
            var (action, payload, password) = renderer.ShowWalletManager(wallets);

            switch (action)
            {
                case WalletRenderer.WalletManagerAction.Switched:
                    {
                        if (payload is null || password is null) return current;
                        bool ok = service.SwitchWallet(payload, password);
                        if (!ok) { renderer.ShowMessage("Wrong password.", isError: true); return current; }
                        return await TryRefreshAsync(service, renderer, current);
                    }
                case WalletRenderer.WalletManagerAction.Created:
                    {
                        if (password is null) return current;
                        service.CreateWallet(password, payload ?? "");
                        return await TryRefreshAsync(service, renderer, current);
                    }
                case WalletRenderer.WalletManagerAction.Imported:
                    {
                        if (payload is null || password is null) return current;
                        var parts = payload.Split('|', 2);
                        service.ImportWif(parts[0], password, parts.Length > 1 ? parts[1] : "");
                        return await TryRefreshAsync(service, renderer, current);
                    }
                case WalletRenderer.WalletManagerAction.Renamed:
                    {
                        if (payload is null || password is null) return current;
                        var pwd = renderer.PromptPassword(current.Label);
                        service.SetLabel(password, pwd);
                        return await TryRefreshAsync(service, renderer, current);
                    }
                default: return current;
            }
        }

        // ── Vanity search handler ─────────────────────────────────────────────────

        private static async Task<WalletState> HandleVanityAsync(
            WalletService service, WalletRenderer renderer,
            WalletState current, string? vanityBin,
            VanityRunner.ComputeMode mode, bool testnet)
        {
            // ── Auto-download if binary missing ───────────────────────────────────
            if (vanityBin is null)
            {
                vanityBin = await TryDownloadVanitySearchAsync(renderer);
                if (vanityBin is null)
                    return current;
                // Re-detect GPU now that we have the binary
                mode = VanityRunner.DetectMode(vanityBin);
            }

            var (prefix, confirmed) = renderer.PromptVanityPrefix(mode, vanityBin);
            if (!confirmed || vanityBin is null)
                return current;

            // Run the search with a live progress loop
            var cts = new CancellationTokenSource();
            var progress = new Progress<VanityRunner.VanityProgress>();
            VanityRunner.VanityProgress? latest = null;
            ((Progress<VanityRunner.VanityProgress>)progress).ProgressChanged +=
                (_, p) => latest = p;

            var searchTask = VanityRunner.SearchAsync(vanityBin, prefix, mode, progress, cts.Token);
            var started = DateTime.UtcNow;

            // Drive the progress UI on the main thread while the search runs in background
            while (!searchTask.IsCompleted)
            {
                bool cancelled = renderer.DrawVanityProgress(
                    prefix, latest, mode,
                    DateTime.UtcNow - started,
                    done: false);

                if (cancelled)
                {
                    cts.Cancel();
                    renderer.ShowMessage("Search cancelled.", isError: false);
                    return current;
                }

                // Redraw every ~200 ms
                await Task.WhenAny(searchTask, Task.Delay(200));
            }

            var result = await searchTask;
            if (result is null)
                return current;

            // Show result — stays on screen until user makes a choice
            char choice = renderer.ShowVanityResult(result, mode);

            if (choice == 'I')
            {
                // Import as a new wallet
                Console.Clear();
                int h = Console.WindowHeight, w = Console.WindowWidth;
                int bw = 56, bh = 10;
                int bx = w / 2 - bw / 2, by = h / 2 - bh / 2;
                // reuse renderer box drawing via ShowMessage workaround — just prompt directly
                Console.Clear();
                Console.SetCursorPosition(w / 2 - 20, h / 2 - 3);
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("Importing vanity wallet...");
                Console.ResetColor();

                Console.SetCursorPosition(w / 2 - 20, h / 2 - 1);
                Console.Write("Wallet label (Enter to use address): ");
                Console.CursorVisible = true;
                string label = Console.ReadLine() ?? "";
                if (string.IsNullOrWhiteSpace(label))
                    label = result.Address[..Math.Min(14, result.Address.Length)];
                Console.CursorVisible = false;

                var pwd = renderer.PromptPassword(label);
                service.ImportWif(result.Wif, pwd, label);
                renderer.ShowMessage($"Vanity wallet imported!\n{result.Address}", isError: false);
                return await TryRefreshAsync(service, renderer, current);
            }

            return current;
        }

        // ── VanitySearch auto-download ────────────────────────────────────────────

        private static async Task<string?> TryDownloadVanitySearchAsync(WalletRenderer renderer)
        {
            var cts = new CancellationTokenSource();
            var progress = new Progress<VanityRunner.DownloadProgress>();
            VanityRunner.DownloadProgress? latest = null;
            ((Progress<VanityRunner.DownloadProgress>)progress).ProgressChanged +=
                (_, p) => latest = p;

            var dlTask = VanityRunner.DownloadAsync(progress, cts.Token);

            while (!dlTask.IsCompleted)
            {
                bool cancelled = renderer.DrawDownloadProgress(
                    latest ?? new VanityRunner.DownloadProgress
                    {
                        StatusMessage = "Connecting to GitHub..."
                    });

                if (cancelled)
                {
                    cts.Cancel();
                    renderer.ShowMessage("Download cancelled.", isError: false);
                    return null;
                }

                await Task.WhenAny(dlTask, Task.Delay(150));
            }

            var bin = await dlTask;
            if (bin is null)
            {
                renderer.ShowMessage(
                    "Could not download VanitySearch.\n" +
                    "Download manually from:\n" +
                    "github.com/JeanLucPons/VanitySearch/releases\n" +
                    "and place VanitySearch.exe beside TUIWallet.exe",
                    isError: true);
                return null;
            }

            renderer.ShowMessage($"VanitySearch installed!\n{bin}", isError: false);
            return bin;
        }

        // ── Binance kline history (seeds the chart on startup) ───────────────────

        private static async Task<decimal[]> FetchKlineHistoryAsync(bool testnet)
        {
            // Use testnet prices from Binance testnet, or mainnet for real
            // Binance klines: GET /api/v3/klines?symbol=BTCUSDT&interval=1h&limit=168
            string baseUrl = "https://api.binance.com";
            string url = $"{baseUrl}/api/v3/klines?symbol=BTCUSDT&interval=1h&limit=168";

            using var hc = new System.Net.Http.HttpClient();
            hc.DefaultRequestHeaders.Add("User-Agent", "TUIWallet/1.0");
            hc.Timeout = TimeSpan.FromSeconds(10);

            var json = await hc.GetStringAsync(url);
            // Response: [[openTime, open, high, low, close, ...], ...]
            var arr = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonArray;
            if (arr is null) return Array.Empty<decimal>();

            var closes = new System.Collections.Generic.List<decimal>();
            foreach (var candle in arr)
            {
                // Index 4 = close price (as string)
                var closeStr = candle?[4]?.ToString();
                if (closeStr != null && decimal.TryParse(closeStr,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out decimal close))
                    closes.Add(close);
            }
            return closes.ToArray();
        }

        // ── Binance WebSocket price stream ────────────────────────────────────────

        private static async Task RunPriceStreamAsync(PriceTicker ticker, CancellationToken ct)
        {
            const string uri = "wss://stream.binance.com:9443/ws/btcusdt@aggTrade";
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var ws = new System.Net.WebSockets.ClientWebSocket();
                    ws.Options.SetRequestHeader("User-Agent", "TUIWallet/1.0");
                    await ws.ConnectAsync(new Uri(uri), ct);
                    var buf = new byte[512];
                    while (ws.State == System.Net.WebSockets.WebSocketState.Open && !ct.IsCancellationRequested)
                    {
                        var result = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                        if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
                        var msg = System.Text.Encoding.UTF8.GetString(buf, 0, result.Count);
                        var node = JsonNode.Parse(msg);
                        var ps = node?["p"]?.ToString();
                        if (ps != null && decimal.TryParse(ps,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out decimal p))
                            ticker.Update(p);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { try { await Task.Delay(3000, ct); } catch { break; } }
            }
        }
    }
}