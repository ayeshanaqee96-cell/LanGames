namespace LanGames;

public class TicTacToeForm : Form
{
    private readonly NetworkPeer peer;
    private readonly Button[] cells = new Button[9];
    private readonly Label lblInfo = new();
    private readonly Button btnRestart = new();
    private readonly char[] board = new char[9];
    private readonly char me;
    private readonly char opponent;
    private bool myTurn;
    private bool gameOver;

    private readonly Action<string> onMessage;
    private readonly Action onDisconnected;

    public TicTacToeForm(NetworkPeer peer, string playerName)
    {
        this.peer = peer;
        me = peer.IsHost ? 'X' : 'O';
        opponent = me == 'X' ? 'O' : 'X';

        Text = $"Tic Tac Toe - {playerName} ({me})";
        ClientSize = new Size(330, 430);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        lblInfo.SetBounds(10, 10, 310, 30);
        lblInfo.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        lblInfo.TextAlign = ContentAlignment.MiddleCenter;

        for (int i = 0; i < 9; i++)
        {
            var b = new Button();
            b.SetBounds(15 + (i % 3) * 100, 50 + (i / 3) * 100, 95, 95);
            b.Font = new Font("Segoe UI", 36, FontStyle.Bold);
            int index = i;
            b.Click += (s, e) => CellClicked(index);
            cells[i] = b;
            Controls.Add(b);
        }

        btnRestart.Text = "Restart Game";
        btnRestart.SetBounds(95, 365, 140, 40);
        btnRestart.Click += (s, e) =>
        {
            peer.Send("RESTART");
            ResetGame();
        };

        Controls.Add(lblInfo);
        Controls.Add(btnRestart);

        onMessage = msg => UI(() => HandleMessage(msg));
        onDisconnected = () => UI(() =>
        {
            gameOver = true;
            lblInfo.Text = "Opponent disconnected";
            btnRestart.Enabled = false;
        });
        peer.MessageReceived += onMessage;
        peer.Disconnected += onDisconnected;

        ResetGame();
    }

    private void UI(Action a)
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(a);
    }

    private void ResetGame()
    {
        Array.Fill(board, ' ');
        gameOver = false;
        myTurn = me == 'X';   // X always starts
        foreach (var c in cells)
        {
            c.Text = "";
            c.Enabled = true;
            c.ForeColor = Color.Black;
        }
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        lblInfo.Text = myTurn ? $"Your turn ({me})" : $"Opponent's turn ({opponent})";
    }

    private void CellClicked(int i)
    {
        if (gameOver || !myTurn || board[i] != ' ') return;
        Place(i, me);
        peer.Send("MOVE|" + i);
        myTurn = false;
        if (!CheckEnd()) UpdateInfo();
    }

    private void HandleMessage(string msg)
    {
        var parts = msg.Split('|');
        if (parts[0] == "MOVE" && parts.Length == 2 && int.TryParse(parts[1], out int i)
            && i >= 0 && i < 9 && board[i] == ' ' && !gameOver)
        {
            Place(i, opponent);
            myTurn = true;
            if (!CheckEnd()) UpdateInfo();
        }
        else if (parts[0] == "RESTART")
        {
            ResetGame();
        }
    }

    private void Place(int i, char mark)
    {
        board[i] = mark;
        cells[i].Text = mark.ToString();
        cells[i].ForeColor = mark == 'X' ? Color.RoyalBlue : Color.Crimson;
        cells[i].Enabled = false;
    }

    private static readonly int[][] Lines =
    {
        new[] {0,1,2}, new[] {3,4,5}, new[] {6,7,8},
        new[] {0,3,6}, new[] {1,4,7}, new[] {2,5,8},
        new[] {0,4,8}, new[] {2,4,6}
    };

    private bool CheckEnd()
    {
        foreach (var l in Lines)
        {
            char a = board[l[0]];
            if (a != ' ' && a == board[l[1]] && a == board[l[2]])
            {
                gameOver = true;
                lblInfo.Text = a == me ? "You win! 🎉" : "You lose!";
                foreach (var c in cells) c.Enabled = false;
                return true;
            }
        }
        if (!board.Contains(' '))
        {
            gameOver = true;
            lblInfo.Text = "It's a draw!";
            return true;
        }
        return false;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        peer.MessageReceived -= onMessage;
        peer.Disconnected -= onDisconnected;
        peer.Close();
        base.OnFormClosed(e);
    }
}