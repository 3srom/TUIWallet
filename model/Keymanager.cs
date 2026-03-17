using NBitcoin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TUIWallet.Models
{
    /// <summary>
    /// Manages one or more wallet key files.
    /// Each wallet is stored as wallet_N.json (or wallet_testnet_N.json).
    /// The "active" wallet is tracked by active.txt in the wallet directory.
    /// </summary>
    public class KeyManager
    {
        private readonly string _walletDir;
        private readonly bool _testnet;
        private Key? _key;
        private Network _network;
        private string _label = "";
        private string _activeFile = "";

        public bool IsLoaded => _key != null;
        public Network Network => _network;
        public string Label => _label;
        public string ActiveFile => _activeFile;

        private string ActivePointerPath =>
            Path.Combine(_walletDir, _testnet ? "active_testnet.txt" : "active.txt");

        public KeyManager(bool testnet = false)
        {
            _testnet = testnet;
            _network = testnet ? Network.TestNet : Network.Main;
            _walletDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TUIWallet");
            Directory.CreateDirectory(_walletDir);

            // Default active file for backward compat
            _activeFile = testnet ? "wallet_testnet.json" : "wallet.json";
            if (File.Exists(ActivePointerPath))
                _activeFile = File.ReadAllText(ActivePointerPath).Trim();
        }

        // ── Enumeration ───────────────────────────────────────────────────────────

        /// <summary>Lists all wallet files with their labels.</summary>
        public List<WalletEntry> ListWallets()
        {
            var result = new List<WalletEntry>();
            var prefix = _testnet ? "wallet_testnet" : "wallet";

            foreach (var file in Directory.GetFiles(_walletDir, $"{prefix}*.json"))
            {
                var fname = Path.GetFileName(file);
                string label = "";
                try
                {
                    var doc = JsonDocument.Parse(File.ReadAllText(file));
                    if (doc.RootElement.TryGetProperty("label", out var lp))
                        label = lp.GetString() ?? "";
                }
                catch { }
                result.Add(new WalletEntry
                {
                    FileName = fname,
                    Label = string.IsNullOrWhiteSpace(label) ? fname : label,
                    IsActive = fname == _activeFile
                });
            }
            return result;
        }

        // ── Key lifecycle ─────────────────────────────────────────────────────────

        /// <summary>Creates a new wallet file and sets it active.</summary>
        public string GenerateNew(string password, string label = "")
        {
            _key = new Key();
            _label = label;
            _activeFile = NewFileName();
            SaveActivePointer();
            Save(password);
            return GetAddress();
        }

        /// <summary>Imports a WIF key into a new wallet file and sets it active.</summary>
        public string ImportWif(string wif, string password, string label = "")
        {
            _key = Key.Parse(wif, _network);
            _label = label;
            _activeFile = NewFileName();
            SaveActivePointer();
            Save(password);
            return GetAddress();
        }

        /// <summary>Loads the currently active wallet. Returns false on wrong password.</summary>
        public bool Load(string password) => LoadFile(_activeFile, password);

        /// <summary>Loads a specific wallet file by filename and sets it active.</summary>
        public bool LoadFile(string fileName, string password)
        {
            var path = Path.Combine(_walletDir, fileName);
            if (!File.Exists(path)) return false;

            var json = File.ReadAllText(path);
            var doc = JsonDocument.Parse(json);
            var encHex = doc.RootElement.GetProperty("encrypted_wif").GetString() ?? "";
            _label = doc.RootElement.TryGetProperty("label", out var lp)
                ? lp.GetString() ?? "" : "";

            var wif = XorDecrypt(encHex, password);
            try
            {
                _key = Key.Parse(wif, _network);
                _activeFile = fileName;
                SaveActivePointer();
                return true;
            }
            catch { return false; }
        }

        public bool WalletFileExists() =>
            File.Exists(Path.Combine(_walletDir, _activeFile));

        public bool AnyWalletExists()
        {
            var prefix = _testnet ? "wallet_testnet" : "wallet";
            return Directory.GetFiles(_walletDir, $"{prefix}*.json").Length > 0;
        }

        public void SetLabel(string label, string password)
        {
            _label = label;
            Save(password);
        }

        // ── Address / signing ─────────────────────────────────────────────────────

        public string GetAddress()
        {
            if (_key is null) throw new InvalidOperationException("No key loaded.");
            return _key.PubKey.GetAddress(ScriptPubKeyType.Segwit, _network).ToString();
        }

        public string GetWif()
        {
            if (_key is null) throw new InvalidOperationException("No key loaded.");
            return _key.GetWif(_network).ToString();
        }

        public string SignTransaction(
            string toAddress, decimal amountBtc,
            System.Text.Json.Nodes.JsonArray utxos, long feeRateSatPerVbyte)
        {
            if (_key is null) throw new InvalidOperationException("No key loaded.");

            var coins = new List<Coin>();
            long totalIn = 0;

            foreach (var utxo in utxos)
            {
                if (utxo is null) continue;
                var txid = uint256.Parse(utxo["txid"]!.ToString());
                var vout = (uint)utxo["vout"]!.GetValue<int>();
                var value = Money.Satoshis(utxo["value"]!.GetValue<long>());
                var script = _key.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit);
                coins.Add(new Coin(txid, vout, value, script));
                totalIn += utxo["value"]!.GetValue<long>();
            }

            long amountSat = (long)(amountBtc * 100_000_000m);
            long feeSat = feeRateSatPerVbyte * 140;
            long changeSat = totalIn - amountSat - feeSat;

            if (changeSat < 0)
                throw new InvalidOperationException("Insufficient funds after fee.");

            var destination = BitcoinAddress.Create(toAddress, _network);
            var txBuilder = _network.CreateTransactionBuilder();
            txBuilder.AddCoins(coins);
            txBuilder.AddKeys(_key);
            txBuilder.Send(destination, Money.Satoshis(amountSat));
            if (changeSat > 546)
                txBuilder.SetChange(_key.PubKey.GetAddress(ScriptPubKeyType.Segwit, _network));
            txBuilder.SendFees(Money.Satoshis(feeSat));

            return txBuilder.BuildTransaction(sign: true).ToHex();
        }

        // ── Persistence ───────────────────────────────────────────────────────────

        private void Save(string password)
        {
            if (_key is null) return;
            var wif = _key.GetWif(_network).ToString();
            var enc = XorEncrypt(wif, password);
            var json = JsonSerializer.Serialize(new { encrypted_wif = enc, label = _label });
            File.WriteAllText(Path.Combine(_walletDir, _activeFile), json);
        }

        private void SaveActivePointer() =>
            File.WriteAllText(ActivePointerPath, _activeFile);

        /// <summary>Finds the next unused wallet filename.</summary>
        private string NewFileName()
        {
            var prefix = _testnet ? "wallet_testnet" : "wallet";
            // Try the legacy name first for slot 1
            var legacy = $"{prefix}.json";
            if (!File.Exists(Path.Combine(_walletDir, legacy))) return legacy;

            for (int i = 2; i < 1000; i++)
            {
                var name = $"{prefix}_{i}.json";
                if (!File.Exists(Path.Combine(_walletDir, name))) return name;
            }
            throw new Exception("Too many wallets.");
        }

        private static string XorEncrypt(string input, string password)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(input);
            var key = System.Text.Encoding.UTF8.GetBytes(password);
            for (int i = 0; i < bytes.Length; i++) bytes[i] ^= key[i % key.Length];
            return Convert.ToHexString(bytes);
        }

        private static string XorDecrypt(string hex, string password)
        {
            var bytes = Convert.FromHexString(hex);
            var key = System.Text.Encoding.UTF8.GetBytes(password);
            for (int i = 0; i < bytes.Length; i++) bytes[i] ^= key[i % key.Length];
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }
}