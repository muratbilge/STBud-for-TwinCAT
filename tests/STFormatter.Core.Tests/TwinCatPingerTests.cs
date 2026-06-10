using System.Net;
using System.Net.Sockets;
using STFormatter.Core.Toolbox;

namespace STFormatter.Core.Tests;

public class TwinCatPingerTests
{
    [Fact]
    public void TryResolve_ParsesIpLiteralsWithoutDns()
    {
        Assert.True(TwinCatPinger.TryResolve("127.0.0.1", out var address));
        Assert.Equal(IPAddress.Loopback, address);
    }

    [Fact]
    public void TryResolve_RejectsEmptyAndUnresolvableTargets()
    {
        Assert.False(TwinCatPinger.TryResolve("", out _));
        Assert.False(TwinCatPinger.TryResolve("   ", out _));
        Assert.False(TwinCatPinger.TryResolve("no-such-host.invalid", out _));
    }

    [Fact]
    public void PingHost_LoopbackSucceeds()
    {
        var result = TwinCatPinger.PingHost(IPAddress.Loopback);
        Assert.True(result.Success);
        Assert.Equal("Success", result.Status);
    }

    [Fact]
    public void CheckTcpPort_DetectsOpenAndClosedPorts()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int openPort = ((IPEndPoint)listener.LocalEndpoint).Port;

            var open = TwinCatPinger.CheckTcpPort(IPAddress.Loopback, openPort, "test");
            Assert.True(open.Open);

            listener.Stop();
            var closed = TwinCatPinger.CheckTcpPort(IPAddress.Loopback, openPort, "test", timeoutMs: 500);
            Assert.False(closed.Open);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void RunDiagnostics_UnresolvableTargetReportsError()
    {
        var report = TwinCatPinger.RunDiagnostics("no-such-host.invalid");
        Assert.NotNull(report.Error);
        Assert.False(report.Reachable);
        Assert.Contains("Could not resolve", report.BuildSummary());
    }

    [Fact]
    public void RunDiagnostics_LoopbackChecksTwinCatPorts()
    {
        var report = TwinCatPinger.RunDiagnostics("127.0.0.1", timeoutMs: 1000);
        Assert.Null(report.Error);
        Assert.Equal("127.0.0.1", report.ResolvedAddress);
        Assert.True(report.Reachable); // loopback ping always answers
        Assert.Equal(TwinCatPinger.TwinCatPorts.Count, report.Ports.Count);
        Assert.Contains(report.Ports, p => p.Port == 48898);
    }

    [Fact]
    public void BuildSummary_ListsPingAndPortLines()
    {
        var report = new TwinCatPinger.Report
        {
            Target = "plc1",
            ResolvedAddress = "192.168.0.10",
            Ping = new TwinCatPinger.PingCheck { Success = true, RoundtripMs = 3, Status = "Success" },
        };
        report.Ports.Add(new TwinCatPinger.PortCheck { Port = 48898, Name = "ADS/AMS router", Open = true, ElapsedMs = 5 });
        report.Ports.Add(new TwinCatPinger.PortCheck { Port = 8016, Name = "Secure ADS", Open = false });

        var summary = report.BuildSummary();
        Assert.Contains("Target: plc1 (192.168.0.10)", summary);
        Assert.Contains("Ping: OK (3 ms)", summary);
        Assert.Contains("Port 48898 (ADS/AMS router): OPEN", summary);
        Assert.Contains("Port 8016 (Secure ADS): closed/unreachable", summary);
        Assert.Contains("TwinCAT/ADS port responding", summary);
    }
}
