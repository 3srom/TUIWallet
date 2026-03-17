using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TUIWallet.Models
{
    public class WalletService
    {
        private readonly BlockstreamClient _api;
        private readonly KeyManager _keys;
        private WalletState? _cached;

        public WalletService(BlockstreamClient api, KeyManager keys)
        {
            _api = api;
            _keys = keys;
        }

        public bool HasWallet => _keys.AnyWalletExists();
        public string Label => _keys.Label;

        // ── Setup ─────────────────────────────────────────────────────────────────

        public bool LoadWallet(string password) => _keys.Load(password);
        public bool SwitchWallet(string fileName, string password) =>
            _keys.LoadFile(fileName, password);

        public string CreateWallet(string password, string label = "") =>
            _keys.GenerateNew(password, label);

        public string ImportWif(string wif, string password, string label = "") =>
            _keys.ImportWif(wif, password, label);

        public string GetWif() => _keys.GetWif();

        public void SetLabel(string label, string password) =>
            _keys.SetLabel(label, password);

        public List<WalletEntry> ListWallets() => _keys.ListWallets();

        // ── State ─────────────────────────────────────────────────────────────────

        public async Task<WalletState> RefreshAsync()
        {
            var address = _keys.GetAddress();
            var (confirmed, unconfirmed) = await _api.GetBalanceAsync(address);
            var txs = await _api.GetTransactionsAsync(address);

            _cached = new WalletState
            {
                Balance = confirmed,
                Unconfirmed = unconfirmed,
                Address = address,
                Label = _keys.Label,
                Transactions = txs,
                FetchedAt = DateTime.Now
            };
            return _cached;
        }

        public async Task<WalletState> GetStateAsync() =>
            _cached ?? await RefreshAsync();

        // ── Send ──────────────────────────────────────────────────────────────────

        public async Task<string> SendAsync(string toAddress, decimal amountBtc)
        {
            if (string.IsNullOrWhiteSpace(toAddress))
                throw new ArgumentException("Address cannot be empty.");
            if (amountBtc <= 0)
                throw new ArgumentException("Amount must be positive.");

            var state = await GetStateAsync();
            if (amountBtc >= state.Balance)
                throw new InvalidOperationException(
                    $"Insufficient balance ({state.Balance:F5} BTC). Fee is deducted on top.");

            var myAddress = _keys.GetAddress();
            var utxos = await _api.GetUtxosAsync(myAddress)
                            ?? throw new Exception("Could not fetch UTXOs.");
            var feeRate = await _api.GetFeeRateAsync();
            var rawHex = _keys.SignTransaction(toAddress, amountBtc, utxos, feeRate);
            var txid = await _api.BroadcastAsync(rawHex);
            await RefreshAsync();
            return txid;
        }
    }
}