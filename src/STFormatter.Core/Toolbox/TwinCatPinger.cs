using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace STFormatter.Core.Toolbox;

/// <summary>
/// Lightweight network/runtime pinger for TwinCAT machines (see ROADMAP "Pinger").
/// Read-only checks: ICMP ping plus TCP reachability of the well-known TwinCAT
/// ports. Independent of TcXaeShell; usable from CLI and tray UI.
/// </summary>
public static class TwinCatPinger
{
    public sealed class PortDefinition
    {
        public PortDefinition(int port, string name)
        {
            Port = port;
            Name = name;
        }

        public int Port { get; }
        public string Name { get; }
    }

    /// <summary>Well-known TwinCAT TCP ports checked by RunDiagnostics.</summary>
    public static readonly IReadOnlyList<PortDefinition> TwinCatPorts = new[]
    {
        new PortDefinition(48898, "ADS/AMS router"),
        new PortDefinition(8016, "Secure ADS"),
    };

    public sealed class PingCheck
    {
        public bool Success { get; set; }
        public long RoundtripMs { get; set; }
        public string Status { get; set; } = "";
    }

    public sealed class PortCheck
    {
        public int Port { get; set; }
        public string Name { get; set; } = "";
        public bool Open { get; set; }
        public long ElapsedMs { get; set; }
    }

    public sealed class Report
    {
        public string Target { get; set; } = "";
        public string? ResolvedAddress { get; set; }
        public PingCheck? Ping { get; set; }
        public List<PortCheck> Ports { get; } = new List<PortCheck>();
        public string? Error { get; set; }

        /// <summary>True when the host answered ping or any checked port is open.</summary>
        public bool Reachable =>
            (Ping != null && Ping.Success) || Ports.Exists(p => p.Open);

        public string BuildSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Target: {Target}" +
                (ResolvedAddress != null && ResolvedAddress != Target ? $" ({ResolvedAddress})" : ""));

            if (Error != null)
            {
                sb.AppendLine($"Error: {Error}");
                return sb.ToString();
            }

            if (Ping != null)
            {
                sb.AppendLine(Ping.Success
                    ? $"Ping: OK ({Ping.RoundtripMs} ms)"
                    : $"Ping: FAILED ({Ping.Status})");
            }

            foreach (var p in Ports)
            {
                sb.AppendLine(p.Open
                    ? $"Port {p.Port} ({p.Name}): OPEN ({p.ElapsedMs} ms)"
                    : $"Port {p.Port} ({p.Name}): closed/unreachable");
            }

            sb.AppendLine(Reachable
                ? "Result: host reachable" + (Ports.Exists(p => p.Open) ? ", TwinCAT/ADS port responding" : ", no TwinCAT port responding")
                : "Result: host unreachable");

            return sb.ToString();
        }
    }

    public static bool TryResolve(string target, out IPAddress? address)
    {
        address = null;
        if (string.IsNullOrWhiteSpace(target))
            return false;

        if (IPAddress.TryParse(target, out var parsed))
        {
            address = parsed;
            return true;
        }

        try
        {
            var addresses = Dns.GetHostAddresses(target);
            foreach (var a in addresses)
            {
                if (a.AddressFamily == AddressFamily.InterNetwork)
                {
                    address = a;
                    return true;
                }
            }
            if (addresses.Length > 0)
            {
                address = addresses[0];
                return true;
            }
        }
        catch (SocketException)
        {
        }

        return false;
    }

    public static PingCheck PingHost(IPAddress address, int timeoutMs = 2000)
    {
        var result = new PingCheck();
        try
        {
            using var ping = new Ping();
            var reply = ping.Send(address, timeoutMs);
            result.Success = reply.Status == IPStatus.Success;
            result.RoundtripMs = reply.RoundtripTime;
            result.Status = reply.Status.ToString();
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Status = ex.GetBaseException().Message;
        }
        return result;
    }

    public static PortCheck CheckTcpPort(IPAddress address, int port, string name = "", int timeoutMs = 2000)
    {
        var result = new PortCheck { Port = port, Name = name };
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient(address.AddressFamily);
            var connectTask = client.ConnectAsync(address, port);
            result.Open = connectTask.Wait(timeoutMs) && client.Connected;
        }
        catch
        {
            result.Open = false;
        }
        result.ElapsedMs = sw.ElapsedMilliseconds;
        return result;
    }

    /// <summary>Ping the target and probe the well-known TwinCAT ports.</summary>
    public static Report RunDiagnostics(string target, int timeoutMs = 2000)
    {
        var report = new Report { Target = target };

        if (!TryResolve(target, out var address) || address == null)
        {
            report.Error = $"Could not resolve '{target}'.";
            return report;
        }

        report.ResolvedAddress = address.ToString();
        report.Ping = PingHost(address, timeoutMs);

        foreach (var def in TwinCatPorts)
        {
            report.Ports.Add(CheckTcpPort(address, def.Port, def.Name, timeoutMs));
        }

        return report;
    }
}
