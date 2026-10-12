using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace LanGames;

public partial class Form1 : Form
{
    private readonly ListView lvNetworks = new();
    private readonly Button btnScan = new();
    private readonly Label lblStatus = new();

    private readonly Panel pnlLobby = new();
    private readonly TextBox txtName = new();
    private readonly Button btnHost = new();
    private readonly Button btnFind = new();
    private readonly ListBox lstGames = new();
    private readonly Button btnJoin = new();
    private readonly TextBox txtIp = new();
    private readonly Button btnJoinIp = new();
        private readonly ComboBox cmbGame = new();

    private NetworkPeer? peer;
    private CancellationTokenSource? discoverCts;

    private record GameEntry(string Name, string Ip)
    {
        public override string ToString() => $"{Name}  ({Ip})";
    }

    public Form1()
    {
        InitializeComponent();

        Text = "LAN Games";
        Width = 720;
        Height = 600;
        StartPosition = FormStartPosition.CenterScreen;

        // WLAN scan
        btnScan.Text = "Scan WLANs";
        btnScan.Dock = DockStyle.Top;
        btnScan.Height = 40;
        btnScan.Click += async (s, e) => await ScanAsync();

        lvNetworks.Dock = DockStyle.Fill;
        lvNetworks.View = View.Details;
        lvNetworks.FullRowSelect = true;
        lvNetworks.GridLines = true;
        lvNetworks.Columns.Add("Network (SSID)", 280);
        lvNetworks.Columns.Add("Signal", 100);
        lvNetworks.Columns.Add("Security", 220);

        lblStatus.Dock = DockStyle.Bottom;
        lblStatus.Height = 28;
        lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        lblStatus.Text = "  Ready";

        // Multiplayer lobby
        var lblName = new Label { Text = "Your name:", Left = 10, Top = 14, Width = 75 };
        txtName.SetBounds(90, 10, 140, 25);
        txtName.Text = Environment.UserName;

        btnHost.Text = "Host Game";
        btnHost.SetBounds(250, 6, 120, 30);
        btnHost.Click += BtnHost_Click;

        btnFind.Text = "Find Games";
        btnFind.SetBounds(380, 6, 120, 30);
        btnFind.Click += BtnFind_Click;

        lstGames.SetBounds(10, 44, 360, 100);

        btnJoin.Text = "Join Selected";
        btnJoin.SetBounds(380, 44, 120, 30);
        btnJoin.Click += BtnJoin_Click;

        var lblIp = new Label { Text = "Or type host IP:", Left = 380, Top = 86, Width = 120 };
        txtIp.SetBounds(380, 106, 120, 25);

        btnJoinIp.Text = "Join by IP";
        btnJoinIp.SetBounds(510, 104, 90, 28);
        btnJoinIp.Click += BtnJoinIp_Click;

                var lblGame = new Label { Text = "Game:", Left = 510, Top = 14, Width = 45 };
        cmbGame.SetBounds(555, 10, 110, 25);
        cmbGame.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbGame.Items.AddRange(new object[] { "Tic Tac Toe", "Chess" });
        cmbGame.SelectedIndex = 0;

        pnlLobby.Dock = DockStyle.Bottom;
        pnlLobby.Height = 155;
               pnlLobby.Controls.AddRange(new Control[]
        {
            lblName, txtName, btnHost, btnFind, lstGames, btnJoin, lblIp, txtIp, btnJoinIp, lblGame, cmbGame
        });

        Controls.Add(lvNetworks);
        Controls.Add(pnlLobby);
        Controls.Add(btnScan);
        Controls.Add(lblStatus);

        Load += async (s, e) => await ScanAsync();
    }

    // ================= NETWORK / LOBBY =================

    private void UI(Action a)
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(a);
    }

         private NetworkPeer CreatePeer()
    {
        var p = new NetworkPeer();
        p.Connected += () => UI(() =>
        {
            discoverCts?.Cancel();
            if (p.IsHost)
            {
                string game = cmbGame.SelectedItem?.ToString() ?? "Tic Tac Toe";
                p.Send("GAME|" + game);
                OpenGame(p, game);
            }
            else
            {
                lblStatus.Text = "  Connected! Waiting for the host to start the game...";
            }
        });
        p.MessageReceived += msg =>
        {
            if (!p.IsHost && msg.StartsWith("GAME|"))
                UI(() => OpenGame(p, msg.Substring(5)));
        };
        p.Disconnected += () => UI(() => lblStatus.Text = "  Disconnected");
        return p;
    }

    private void OpenGame(NetworkPeer p, string game)
    {
        lblStatus.Text = "  Playing " + game;
        Form f = game == "Chess" ? new ChessForm(p, txtName.Text) : new TicTacToeForm(p, txtName.Text);
        f.Show(this);
    }

    private void CleanupNetwork()
    {
        discoverCts?.Cancel();
        peer?.Close();
        peer = null;
    }

    private async void BtnHost_Click(object? sender, EventArgs e)
    {
        CleanupNetwork();
        peer = CreatePeer();
        lblStatus.Text = $"  Hosting... waiting for the other player (my IP: {GetLocalIp()})";
        try
        {
            await peer.StartHostAsync(txtName.Text);
        }
        catch (Exception ex)
        {
            lblStatus.Text = "  Could not host: " + ex.Message;
        }
    }

    private void BtnFind_Click(object? sender, EventArgs e)
    {
        discoverCts?.Cancel();
        discoverCts = new CancellationTokenSource();
        lstGames.Items.Clear();
        lblStatus.Text = "  Searching for games...";
        _ = NetworkPeer.DiscoverAsync((name, ip) => UI(() => AddGame(name, ip)), discoverCts.Token);
    }

    private void AddGame(string name, string ip)
    {
        foreach (GameEntry g in lstGames.Items)
            if (g.Ip == ip) return;
        lstGames.Items.Add(new GameEntry(name, ip));
    }

    private async void BtnJoin_Click(object? sender, EventArgs e)
    {
        if (lstGames.SelectedItem is not GameEntry g)
        {
            lblStatus.Text = "  	Select a game from the list firs";
            return;
        }
        await JoinAsync(g.Ip);
    }

    private async void BtnJoinIp_Click(object? sender, EventArgs e)
    {
        await JoinAsync(txtIp.Text.Trim());
    }

    private async Task JoinAsync(string ip)
    {
        CleanupNetwork();
        peer = CreatePeer();
        lblStatus.Text = "  Connecting to " + ip + "...";
        try
        {
            await peer.ConnectAsync(ip);
        }
        catch (Exception ex)
        {
            lblStatus.Text = "  Could not connect: " + ex.Message;
        }
    }

    private static string GetLocalIp()
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            s.Connect("8.8.8.8", 65530);
            return ((IPEndPoint)s.LocalEndPoint!).Address.ToString();
        }
        catch { }
        try
        {
            foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                if (ip.AddressFamily == AddressFamily.InterNetwork) return ip.ToString();
        }
        catch { }
        return "?";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        CleanupNetwork();
        base.OnFormClosing(e);
    }

    // ================= WLAN SCAN =================

    private async Task ScanAsync()
    {
        btnScan.Enabled = false;
        lblStatus.Text = "  Scanning...";
        lvNetworks.Items.Clear();

        try
        {
            string output = await Task.Run(RunNetsh);
            var networks = ParseNetworks(output);

            foreach (var n in networks.OrderByDescending(x => x.Signal))
            {
                var item = new ListViewItem(string.IsNullOrWhiteSpace(n.Ssid) ? "(Hidden network)" : n.Ssid);
                item.SubItems.Add(n.Signal + "%");
                item.SubItems.Add(n.Security);
                lvNetworks.Items.Add(item);
            }

            lblStatus.Text = networks.Count == 0
                ? "  No networks found (is WiFi on? is Location on?)"
                : $"  {networks.Count} network(s) found";
        }
        catch (Exception ex)
        {
            lblStatus.Text = "  Error: " + ex.Message;
        }
        finally
        {
            btnScan.Enabled = true;
        }
    }

    private static string RunNetsh()
    {
        var psi = new ProcessStartInfo("netsh", "wlan show networks mode=bssid")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        string text = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return text;
    }

    private record WlanInfo(string Ssid, int Signal, string Security);

    private static List<WlanInfo> ParseNetworks(string text)
    {
        var list = new List<WlanInfo>();
        string? ssid = null;
        string security = "";
        int signal = 0;

        void Flush()
        {
            if (ssid != null) list.Add(new WlanInfo(ssid, signal, security));
        }

        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();

            var mSsid = Regex.Match(line, @"^SSID\s+\d+\s*:\s*(.*)$");
            if (mSsid.Success)
            {
                Flush();
                ssid = mSsid.Groups[1].Value.Trim();
                security = "";
                signal = 0;
                continue;
            }

            var mAuth = Regex.Match(line, @"^Authentication\s*:\s*(.*)$");
            if (mAuth.Success) { security = mAuth.Groups[1].Value.Trim(); continue; }

            var mSig = Regex.Match(line, @"^Signal\s*:\s*(\d+)%");
            if (mSig.Success)
            {
                int s = int.Parse(mSig.Groups[1].Value);
                if (s > signal) signal = s;
            }
        }
        Flush();
        return list;
    }
}