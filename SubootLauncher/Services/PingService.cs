using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SubootLauncher.Services;

/// <summary>
/// Пинг до игрового сервера: ICMP, а если сервер на него не отвечает (закрыт брандмауэром) —
/// время установки TCP-соединения с игровым портом. null — сервер недоступен.
/// </summary>
public static class PingService
{
    private const int TimeoutMs = 2000;

    private static bool IsVpnFakeAddress(System.Net.IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19);
    }

    public static async Task<long?> MeasureAsync(string host, int port)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, TimeoutMs);
            // 198.18.0.0/15 — «фиктивные» адреса VPN в режиме TUN (Clash, mihomo): на ICMP отвечает сам VPN за 1 мс
            if (reply.Status == IPStatus.Success && !IsVpnFakeAddress(reply.Address)) return reply.RoundtripTime;
        }
        catch (PingException)
        {
            // Имя не разрешилось или ICMP запрещён — пробуем TCP
        }

        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeoutMs);
            var watch = Stopwatch.StartNew();
            await client.ConnectAsync(host, port, timeout.Token);
            return watch.ElapsedMilliseconds;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
