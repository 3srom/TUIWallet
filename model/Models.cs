using System;
using System.Collections.Generic;
using System.Threading;

namespace TUIWallet.Models
{
    /// <summary>A single on-chain Bitcoin transaction.</summary>
    public class Transaction
    {
        public string TxId { get; init; } = "";
        public decimal Amount { get; init; }
        public DateTime Timestamp { get; init; }
        public bool Confirmed { get; init; }

        public string ShortId => TxId.Length > 16
            ? TxId[..8] + "..." + TxId[^8..]
            : TxId;

        public string FormattedAmount =>
            Amount >= 0 ? $"+{Amount:F5}" : $"{Amount:F5}";
    }

    /// <summary>Full snapshot of wallet state from the API.</summary>
    public class WalletState
    {
        public decimal Balance { get; init; }
        public decimal Unconfirmed { get; init; }
        public string Address { get; init; } = "";
        public string Label { get; init; } = "";
        public Transaction[] Transactions { get; init; } = Array.Empty<Transaction>();
        public DateTime FetchedAt { get; init; } = DateTime.UtcNow;
    }

    /// <summary>Which panel has keyboard focus.</summary>
    public enum FocusZone { Top, Left, Right }

    /// <summary>Actions in the left menu.</summary>
    public enum MenuAction { Send, Receive, Keys, Wallets, Vanity }

    /// <summary>
    /// Holds the latest BTC/USD price and a rolling hourly history for the chart.
    /// Thread-safe via a single lock object.
    /// </summary>
    public class PriceTicker
    {
        private readonly object _lock = new();
        private decimal _price = 0m;
        private int _direction = 0;
        private readonly List<decimal> _hourlyPrices = new();
        private decimal _hourOpen = 0m;
        private DateTime _hourStart = DateTime.MinValue;

        public decimal Price { get { lock (_lock) return _price; } }
        public int Direction { get { lock (_lock) return _direction; } }

        public decimal[] GetHourlySnapshot()
        {
            lock (_lock) return _hourlyPrices.ToArray();
        }

        public void Update(decimal newPrice)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                if (_hourStart == DateTime.MinValue)
                {
                    _hourStart = now;
                    _hourOpen = newPrice;
                }
                if ((now - _hourStart).TotalHours >= 1.0)
                {
                    _hourlyPrices.Add(newPrice);
                    if (_hourlyPrices.Count > 200) _hourlyPrices.RemoveAt(0);
                    _hourStart = now;
                    _hourOpen = newPrice;
                }
                _direction = newPrice > _price ? 1 : newPrice < _price ? -1 : 0;
                _price = newPrice;
            }
        }

        public string Formatted()
        {
            lock (_lock)
            {
                if (_price == 0m) return "BTC/USD ---";
                var arrow = _direction switch { 1 => "\u25b2", -1 => "\u25bc", _ => "-" };
                return $"BTC/USD {_price:N2} {arrow}";
            }
        }
    }

    /// <summary>A saved wallet entry (label + filename, no key material).</summary>
    public class WalletEntry
    {
        public string FileName { get; init; } = "";
        public string Label { get; init; } = "";
        public bool IsActive { get; init; }
    }
}