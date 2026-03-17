using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Transactions;

namespace TUIWallet.Models
{
    /// <summary>
    /// Lightweight HTTP client for the Blockstream Esplora API.
    /// No daemon, no download — all data lives on Blockstream's servers.
    /// Docs: https://github.com/Blockstream/esplora/blob/master/API.md
    /// </summary>
    public class BlockstreamClient
    {
        private readonly HttpClient _http;
        private readonly string _base;

        /// <param name="testnet">Use testnet (free test BTC) instead of mainnet.</param>
        public BlockstreamClient(bool testnet = false)
        {
            _base = testnet
                ? "https://blockstream.info/testnet/api"
                : "https://blockstream.info/api";
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("User-Agent", "TUIWallet/1.0");
        }

        // ── Balance ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns (confirmed, unconfirmed) balance in BTC for a given address.
        /// </summary>
        public async Task<(decimal confirmed, decimal unconfirmed)> GetBalanceAsync(string address)
        {
            var json = await GetJsonAsync($"/address/{address}");
            var stats = json?["chain_stats"];
            var mstats = json?["mempool_stats"];

            long fundedSat = stats?["funded_txo_sum"]?.GetValue<long>() ?? 0;
            long spentSat = stats?["spent_txo_sum"]?.GetValue<long>() ?? 0;
            long mFundedSat = mstats?["funded_txo_sum"]?.GetValue<long>() ?? 0;
            long mSpentSat = mstats?["spent_txo_sum"]?.GetValue<long>() ?? 0;

            decimal confirmed = SatToBtc(fundedSat - spentSat);
            decimal unconfirmed = SatToBtc(mFundedSat - mSpentSat);

            return (confirmed, unconfirmed);
        }

        // ── Transactions ──────────────────────────────────────────────────────────

        /// <summary>Returns up to 25 recent transactions for an address.</summary>
        public async Task<Transaction[]> GetTransactionsAsync(string address)
        {
            var json = await GetJsonAsync($"/address/{address}/txs");
            if (json is not JsonArray arr) return Array.Empty<Transaction>();

            var list = new List<Transaction>();
            foreach (var item in arr)
            {
                if (item is null) continue;

                // Calculate net value for this address
                decimal received = 0, sent = 0;

                if (item["vout"] is JsonArray vout)
                    foreach (var o in vout)
                        if (o?["scriptpubkey_address"]?.ToString() == address)
                            received += SatToBtc(o?["value"]?.GetValue<long>() ?? 0);

                if (item["vin"] is JsonArray vin)
                    foreach (var i in vin)
                        if (i?["prevout"]?["scriptpubkey_address"]?.ToString() == address)
                            sent += SatToBtc(i?["prevout"]?["value"]?.GetValue<long>() ?? 0);

                bool confirmed = item["status"]?["confirmed"]?.GetValue<bool>() ?? false;
                long ts = item["status"]?["block_time"]?.GetValue<long>() ?? 0;

                list.Add(new Transaction
                {
                    TxId = item["txid"]?.ToString() ?? "",
                    Amount = received - sent,
                    Timestamp = ts > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime
                        : DateTime.Now,
                    Confirmed = confirmed
                });
            }
            return list.ToArray();
        }

        // ── Broadcast ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Broadcasts a signed raw transaction hex to the network.
        /// Returns the txid on success.
        /// </summary>
        public async Task<string> BroadcastAsync(string rawTxHex)
        {
            var content = new System.Net.Http.StringContent(rawTxHex,
                               System.Text.Encoding.UTF8, "text/plain");
            var response = await _http.PostAsync($"{_base}/tx", content);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(); // returns txid
        }

        // ── UTXOs (needed for building transactions) ──────────────────────────────

        /// <summary>Returns unspent outputs for the address.</summary>
        public async Task<JsonArray?> GetUtxosAsync(string address)
        {
            var json = await GetJsonAsync($"/address/{address}/utxo");
            return json as JsonArray;
        }

        // ── Fee estimate ──────────────────────────────────────────────────────────

        /// <summary>Returns recommended sat/vbyte fee for ~1 block confirmation.</summary>
        public async Task<long> GetFeeRateAsync()
        {
            var json = await GetJsonAsync("/fee-estimates");
            // "1" = next block fee rate in sat/vbyte
            return json?["1"]?.GetValue<long>() ?? 5;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private async Task<JsonNode?> GetJsonAsync(string path)
        {
            var resp = await _http.GetAsync(_base + path);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync();
            return JsonNode.Parse(body);
        }

        private static decimal SatToBtc(long satoshis) => satoshis / 100_000_000m;
    }
}