using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TUIWallet.Models
{
    public class VanityRunner
    {
        // ── Install path (AppData — no UAC needed) ────────────────────────────────

        private static readonly string InstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TUIWallet", "tools");

        private static string InstalledBinary =>
            Path.Combine(InstallDir,
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "VanitySearch.exe" : "VanitySearch");

        // GitHub releases — try API first, fall back to direct known-good URL
        private const string GhApi =
            "https://api.github.com/repos/JeanLucPons/VanitySearch/releases/latest";

        // Direct download URLs for v1.19 (last known release)
        private const string DirectWin =
            "https://github.com/JeanLucPons/VanitySearch/releases/download/1.19/VanitySearch1.19.zip";
        private const string DirectLin =
            "https://github.com/JeanLucPons/VanitySearch/releases/download/1.19/VanitySearch1.19.tar.gz";

        private static readonly string[] _candidatePaths =
        {
            "VanitySearch",
            "VanitySearch.exe",
            "./VanitySearch",
            "./VanitySearch.exe",
            Path.Combine(AppContext.BaseDirectory, "VanitySearch"),
            Path.Combine(AppContext.BaseDirectory, "VanitySearch.exe"),
            InstalledBinary,
        };

        // ── Download progress ─────────────────────────────────────────────────────

        public class DownloadProgress
        {
            public long BytesReceived { get; init; }
            public long TotalBytes { get; init; }
            public int Percent => TotalBytes > 0 ? (int)(BytesReceived * 100 / TotalBytes) : -1;
            public string StatusMessage { get; init; } = "";
        }

        // ── Auto-download ─────────────────────────────────────────────────────────

        public static async Task<string?> DownloadAsync(
            IProgress<DownloadProgress> onProgress, CancellationToken ct)
        {
            bool isWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            Directory.CreateDirectory(InstallDir);

            // Release assets are bare .exe / Linux binary — NOT zipped
            // URL pattern: releases/download/{tag}/VanitySearch.exe
            // We try the GitHub API to get the latest tag, then fall back to 1.19
            try
            {
                // ── Step 1: resolve latest tag ────────────────────────────────────
                string tag = "1.19";
                onProgress.Report(new DownloadProgress { StatusMessage = "Checking GitHub for latest release..." });
                try
                {
                    using var hc = MakeHttpClient();
                    hc.Timeout = TimeSpan.FromSeconds(8);
                    var json = await hc.GetStringAsync(GhApi, ct);
                    var m = Regex.Match(json, @"""tag_name""\s*:\s*""([^""]+)""");
                    if (m.Success) tag = m.Groups[1].Value;
                }
                catch { /* use fallback tag */ }

                // ── Step 2: build direct binary URL ───────────────────────────────
                // VanitySearch releases ship VanitySearch.exe directly as an asset
                // Linux builds must be compiled from source — only Windows binary available
                if (!isWin)
                {
                    onProgress.Report(new DownloadProgress
                    { StatusMessage = "No prebuilt Linux binary available." });
                    return null;
                }

                string url = $"https://github.com/JeanLucPons/VanitySearch/releases/download/{tag}/VanitySearch.exe";
                onProgress.Report(new DownloadProgress { StatusMessage = $"Downloading VanitySearch {tag}..." });

                // ── Step 3: download the .exe directly ────────────────────────────
                using (var hc = MakeHttpClient())
                {
                    hc.Timeout = TimeSpan.FromMinutes(3);
                    using var resp = await hc.GetAsync(url,
                        HttpCompletionOption.ResponseHeadersRead, ct);

                    // If direct URL fails (e.g. asset named differently), try alternate name
                    if (!resp.IsSuccessStatusCode)
                    {
                        string altUrl = $"https://github.com/JeanLucPons/VanitySearch/releases/download/{tag}/VanitySearch{tag}.exe";
                        using var resp2 = await hc.GetAsync(altUrl,
                            HttpCompletionOption.ResponseHeadersRead, ct);
                        resp2.EnsureSuccessStatusCode();
                        await StreamToFileAsync(resp2, InstalledBinary, onProgress, ct);
                    }
                    else
                    {
                        await StreamToFileAsync(resp, InstalledBinary, onProgress, ct);
                    }
                }

                // ── Step 4: verify ────────────────────────────────────────────────
                if (!File.Exists(InstalledBinary) || !Probe(InstalledBinary))
                {
                    onProgress.Report(new DownloadProgress
                    { StatusMessage = "Download failed — binary did not respond." });
                    return null;
                }

                onProgress.Report(new DownloadProgress
                { StatusMessage = "Done!", BytesReceived = 1, TotalBytes = 1 });
                return InstalledBinary;
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                onProgress.Report(new DownloadProgress
                { StatusMessage = $"Error: {ex.Message[..Math.Min(55, ex.Message.Length)]}" });
                return null;
            }
        }

        private static async Task StreamToFileAsync(
            HttpResponseMessage resp,
            string destPath,
            IProgress<DownloadProgress> onProgress,
            CancellationToken ct)
        {
            long total = resp.Content.Headers.ContentLength ?? -1;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dest = File.Create(destPath);
            var buf = new byte[81920];
            long received = 0; int read;
            while ((read = await src.ReadAsync(buf, ct)) > 0)
            {
                await dest.WriteAsync(buf.AsMemory(0, read), ct);
                received += read;
                onProgress.Report(new DownloadProgress
                {
                    BytesReceived = received,
                    TotalBytes = total,
                    StatusMessage = $"Downloading... {received / 1024} KB"
                                  + (total > 0 ? $" / {total / 1024} KB" : "")
                });
            }
        }

        private static HttpClient MakeHttpClient()
        {
            // AllowAutoRedirect=true is the default; just set User-Agent
            var hc = new HttpClient();
            hc.DefaultRequestHeaders.Add("User-Agent", "TUIWallet/1.0");
            return hc;
        }

        private static string? FindExtractedBinary(string dir)
        {
            bool isWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            string name = isWin ? "VanitySearch.exe" : "VanitySearch";
            foreach (var f in Directory.EnumerateFiles(dir, name,
                         SearchOption.AllDirectories))
                return f;
            return null;
        }

        // ── Hardware detection ────────────────────────────────────────────────────

        public enum ComputeMode { CpuOnly, Gpu }

        public static ComputeMode DetectMode(string binaryPath)
        {
            try
            {
                var psi = new ProcessStartInfo(binaryPath, "-l")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi)!;
                var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(3000);
                return output.Contains("GPU #") ? ComputeMode.Gpu : ComputeMode.CpuOnly;
            }
            catch { return ComputeMode.CpuOnly; }
        }

        public static string? FindBinary()
        {
            foreach (var path in _candidatePaths)
                if (Probe(path)) return path;
            return null;
        }

        private static bool Probe(string path)
        {
            try
            {
                if (!File.Exists(path) && !Path.IsPathRooted(path))
                {
                    // for bare names like "VanitySearch" — let the OS find it
                }
                var psi = new ProcessStartInfo(path, "-v")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p is null) return false;
                p.WaitForExit(2000);
                return true;
            }
            catch { return false; }
        }

        // ── Search result / progress types ────────────────────────────────────────

        public class VanityResult
        {
            public string Address { get; init; } = "";
            public string Wif { get; init; } = "";
            public string HexKey { get; init; } = "";
        }

        public class VanityProgress
        {
            public string SpeedLabel { get; init; } = "";
            public string GpuSpeed { get; init; } = "";
            public string Probability { get; init; } = "";
            public string Eta { get; init; } = "";
            public string Difficulty { get; init; } = "";
            public ComputeMode Mode { get; init; }
            public int Found { get; init; }
            public string RawLine { get; init; } = "";
        }

        // ── Main search ───────────────────────────────────────────────────────────

        public static async Task<VanityResult?> SearchAsync(
            string binaryPath, string prefix, ComputeMode mode,
            IProgress<VanityProgress> onProgress, CancellationToken ct)
        {
            var args = new List<string>();
            if (mode == ComputeMode.Gpu) args.Add("-gpu");
            args.Add("-stop");
            args.Add(prefix);

            var psi = new ProcessStartInfo(binaryPath, string.Join(" ", args))
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            VanityResult? result = null;
            string? pendingAddr = null, pendingWif = null, difficulty = null;
            var tcs = new TaskCompletionSource<VanityResult?>();

            void ProcessLine(string line)
            {
                line = line.Trim();
                var dm = Regex.Match(line, @"Difficulty:\s*([\d,]+)");
                if (dm.Success) difficulty = dm.Groups[1].Value;

                var prog = ParseProgressLine(line, mode, difficulty ?? "");
                if (prog != null) onProgress.Report(prog);

                if (line.StartsWith("PubAddress:")) pendingAddr = line["PubAddress:".Length..].Trim();
                if (line.StartsWith("Priv (WIF):")) pendingWif = ExtractWif(line);
                if (line.StartsWith("Priv (HEX):") && pendingAddr != null && pendingWif != null)
                    result = new VanityResult
                    {
                        Address = pendingAddr,
                        Wif = pendingWif,
                        HexKey = line["Priv (HEX):".Length..].Trim()
                    };
            }

            proc.OutputDataReceived += (_, e) => { if (e.Data != null) ProcessLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) ProcessLine(e.Data); };
            proc.Exited += (_, _) => tcs.TrySetResult(result);

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            using var reg = ct.Register(() =>
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
                tcs.TrySetResult(null);
            });

            return await tcs.Task;
        }

        private static VanityProgress? ParseProgressLine(string line, ComputeMode mode, string diff)
        {
            if (!line.Contains("Mkey") && !line.Contains("MK/s")) return null;
            return new VanityProgress
            {
                SpeedLabel = ExtractPat(line, @"([\d.]+\s*M(?:key|K)/s)"),
                GpuSpeed = ExtractPat(line, @"GPU\s+([\d.]+\s*M(?:key|K)/s)"),
                Probability = ExtractPat(line, @"(?:Prob|P)\s+([\d.]+%)"),
                Eta = ExtractPat(line, @"50(?:\.00)?% in\s+(\d+:\d+:\d+)"),
                Difficulty = diff,
                Mode = mode,
                RawLine = line
            };
        }

        private static string ExtractPat(string input, string pattern)
        {
            var m = Regex.Match(input, pattern);
            if (!m.Success) return "";
            for (int i = 1; i < m.Groups.Count; i++)
                if (m.Groups[i].Success && m.Groups[i].Value != "") return m.Groups[i].Value;
            return m.Value;
        }

        private static string ExtractWif(string line)
        {
            var raw = line["Priv (WIF):".Length..].Trim();
            var colon = raw.IndexOf(':');
            return colon >= 0 && colon < 12 ? raw[(colon + 1)..] : raw;
        }
    }
}