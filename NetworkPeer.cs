using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LanGames;

public class NetworkPeer
{
    public const int GamePort = 47778;
    public const int DiscoveryPort = 47777;

    private TcpListener? listener;
    private TcpClient? client;
    private StreamWriter? writer;
    private CancellationTokenSource? cts;

    public bool IsHost { get; private set; }

    public event Action? Connected;
    public event Action<string>? MessageReceived;
    public event Action? Disconnected;

    // ---------- HOST ----------
    public async Task StartHostAsync(string hostName)
    {
        IsHost = true;
        cts = new CancellationTokenSource();
        var token = cts.Token;

        listener = new TcpListener(IPAddress.Any, GamePort);
        listener.Start();

        _ = Task.Run(() => BroadcastLoop(hostName.Replace("|", " "), token));

        try
        {
            client = await listener.AcceptTcpClientAsync(token);
        }
        catch when (token.IsCancellationRequested)
        {
            return;
        }

        cts.Cancel();      // broadcast band
        listener.Stop();
        StartReceiving();
        Connected?.Invoke();
    }

    private static async Task BroadcastLoop(string name, CancellationToken token)
    {
        using var udp = new UdpClient();
        udp.EnableBroadcast = true;
        var ep = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
        byte[] data = Encoding.UTF8.GetBytes("LANGAME|" + name + "|" + GamePort);

        while (!token.IsCancellationRequested)
        {
            try { await udp.SendAsync(data, data.Length, ep); } catch { }
            try { await Task.Delay(1000, token); } catch { break; }
        }
    }

    // ---------- JOIN ----------
    public static async Task DiscoverAsync(Action<string, string> onFound, CancellationToken token)
    {
        using var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(token);
                string msg = Encoding.UTF8.GetString(result.Buffer);
                var parts = msg.Split('|');
                if (parts.Length == 3 && parts[0] == "LANGAME")
                    onFound(parts[1], result.RemoteEndPoint.Address.ToString());
            }
            catch when (token.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await Task.Delay(200);
            }
        }
    }

    public async Task ConnectAsync(string ip)
    {
        IsHost = false;
        client = new TcpClient();
        await client.ConnectAsync(ip, GamePort);
        StartReceiving();
        Connected?.Invoke();
    }

    // ---------- COMMON ----------
    private void StartReceiving()
    {
        var stream = client!.GetStream();
        writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        var reader = new StreamReader(stream, Encoding.UTF8);

        _ = Task.Run(async () =>
        {
            try
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                    MessageReceived?.Invoke(line);
            }
            catch { }
            Disconnected?.Invoke();
        });
    }

    public void Send(string message)
    {
        try { writer?.WriteLine(message); } catch { }
    }

    public void Close()
    {
        try { cts?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }
        try { client?.Close(); } catch { }
    }
}