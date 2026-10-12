namespace LanGames;

public class ChessForm : Form
{
    private readonly NetworkPeer peer;
    private readonly bool iAmWhite;
    private readonly Button[,] squares = new Button[8, 8];   // [viewRow, viewCol]
    private readonly Label lblInfo = new();
    private readonly Button btnRestart = new();

    private char[,] board = new char[8, 8];   // uppercase = White, lowercase = Black, '.' = empty
    private bool[] castle = new bool[4];      // 0: White king-side, 1: White queen-side, 2: Black king-side, 3: Black queen-side
    private bool whiteToMove;
    private bool gameOver;
    private int selR = -1, selC = -1;
    private List<(int r, int c)> targets = new();
    private (int fr, int fc, int tr, int tc)? lastMove;

    private readonly Action<string> onMessage;
    private readonly Action onDisconnected;

    private static readonly (int, int)[] KnightD = { (-2, -1), (-2, 1), (-1, -2), (-1, 2), (1, -2), (1, 2), (2, -1), (2, 1) };
    private static readonly (int, int)[] KingD = { (-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1) };
    private static readonly (int, int)[] DiagD = { (-1, -1), (-1, 1), (1, -1), (1, 1) };
    private static readonly (int, int)[] StraightD = { (-1, 0), (1, 0), (0, -1), (0, 1) };
    private static readonly bool[] NoCastle = new bool[4];

    public ChessForm(NetworkPeer peer, string playerName)
    {
        this.peer = peer;
        iAmWhite = peer.IsHost;   // host = White, joiner = Black

        Text = $"Chess - {playerName} ({(iAmWhite ? "White" : "Black")})";
        ClientSize = new Size(540, 630);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        lblInfo.SetBounds(10, 8, 520, 32);
        lblInfo.Font = new Font("Segoe UI", 13, FontStyle.Bold);
        lblInfo.TextAlign = ContentAlignment.MiddleCenter;
        Controls.Add(lblInfo);

        for (int vr = 0; vr < 8; vr++)
        {
            for (int vc = 0; vc < 8; vc++)
            {
                var b = new Button();
                b.SetBounds(14 + vc * 64, 50 + vr * 64, 64, 64);
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.UseVisualStyleBackColor = false;
                b.Font = new Font("Segoe UI Symbol", 30);
                b.ForeColor = Color.Black;
                int r1 = vr, c1 = vc;
                b.Click += (s, e) => SquareClicked(r1, c1);
                squares[vr, vc] = b;
                Controls.Add(b);
            }
        }

        btnRestart.Text = "Restart Game";
        btnRestart.SetBounds(200, 575, 140, 40);
        btnRestart.Click += (s, e) =>
        {
            peer.Send("RESTART");
            ResetGame();
        };
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

    // ================= GAME STATE =================

    private void ResetGame()
    {
        string[] start =
        {
            "rnbqkbnr",
            "pppppppp",
            "........",
            "........",
            "........",
            "........",
            "PPPPPPPP",
            "RNBQKBNR"
        };
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
                board[r, c] = start[r][c];

        castle = new[] { true, true, true, true };
        whiteToMove = true;
        gameOver = false;
        selR = selC = -1;
        targets.Clear();
        lastMove = null;
        UpdateInfo(false);
        Redraw();
    }

    private void UpdateInfo(bool check)
    {
        string who = whiteToMove == iAmWhite ? "Your turn" : "Opponent's turn";
        string color = whiteToMove ? "White" : "Black";
        lblInfo.Text = $"{who} ({color})" + (check ? " - CHECK!" : "");
    }

    // ================= CLICK HANDLING =================

    private void SquareClicked(int vr, int vc)
    {
        if (gameOver || whiteToMove != iAmWhite) return;

        int r = iAmWhite ? vr : 7 - vr;
        int c = iAmWhite ? vc : 7 - vc;

        if (selR >= 0 && targets.Contains((r, c)))
        {
            int fr = selR, fc = selC;
            DoMove(fr, fc, r, c);
            peer.Send($"MOVE|{fr}|{fc}|{r}|{c}");
            return;
        }

        char p = board[r, c];
        if (p != '.' && char.IsUpper(p) == iAmWhite)
        {
            selR = r;
            selC = c;
            targets = Legal(r, c);
        }
        else
        {
            selR = selC = -1;
            targets.Clear();
        }
        Redraw();
    }

    private void HandleMessage(string msg)
    {
        var parts = msg.Split('|');
        if (parts[0] == "RESTART")
        {
            ResetGame();
            return;
        }

        if (parts[0] == "MOVE" && parts.Length == 5 && !gameOver && whiteToMove != iAmWhite)
        {
            if (int.TryParse(parts[1], out int fr) && int.TryParse(parts[2], out int fc) &&
                int.TryParse(parts[3], out int tr) && int.TryParse(parts[4], out int tc) &&
                fr is >= 0 and < 8 && fc is >= 0 and < 8 && tr is >= 0 and < 8 && tc is >= 0 and < 8)
            {
                char p = board[fr, fc];
                if (p != '.' && char.IsUpper(p) == whiteToMove && Legal(fr, fc).Contains((tr, tc)))
                    DoMove(fr, fc, tr, tc);
            }
        }
    }

    private void DoMove(int fr, int fc, int tr, int tc)
    {
        Apply(board, castle, fr, fc, tr, tc);
        lastMove = (fr, fc, tr, tc);
        selR = selC = -1;
        targets.Clear();

        whiteToMove = !whiteToMove;
        bool check = InCheck(board, whiteToMove);

        if (!HasMoves(whiteToMove))
        {
            gameOver = true;
            if (check)
                lblInfo.Text = whiteToMove == iAmWhite ? "Checkmate - You lose!" : "Checkmate - You win! 🎉";
            else
                lblInfo.Text = "Stalemate - it's a draw!";
        }
        else
        {
            UpdateInfo(check);
        }
        Redraw();
    }

    // ================= DRAWING =================

    private static string Glyph(char p) => p switch
    {
        'K' => "♔", 'Q' => "♕", 'R' => "♖", 'B' => "♗", 'N' => "♘", 'P' => "♙",
        'k' => "♚", 'q' => "♛", 'r' => "♜", 'b' => "♝", 'n' => "♞", 'p' => "♟",
        _ => ""
    };

    private void Redraw()
    {
        var light = Color.FromArgb(240, 217, 181);
        var dark = Color.FromArgb(181, 136, 99);

        for (int vr = 0; vr < 8; vr++)
        {
            for (int vc = 0; vc < 8; vc++)
            {
                int r = iAmWhite ? vr : 7 - vr;
                int c = iAmWhite ? vc : 7 - vc;
                var b = squares[vr, vc];

                b.Text = Glyph(board[r, c]);
                Color bg = (r + c) % 2 == 0 ? light : dark;

                if (lastMove is { } lm && ((lm.fr == r && lm.fc == c) || (lm.tr == r && lm.tc == c)))
                    bg = Color.FromArgb(205, 210, 106);
                if (r == selR && c == selC)
                    bg = Color.FromArgb(246, 246, 105);
                if (targets.Contains((r, c)))
                    bg = Color.FromArgb(130, 190, 120);

                b.BackColor = bg;
            }
        }
    }

    // ================= CHESS RULES =================

    private static bool In(int r, int c) => r >= 0 && r < 8 && c >= 0 && c < 8;

    private static List<(int r, int c)> Pseudo(char[,] b, int r, int c, bool[] cr, bool withCastle)
    {
        var res = new List<(int r, int c)>();
        char p = b[r, c];
        if (p == '.') return res;
        bool white = char.IsUpper(p);

        bool Enemy(int x, int y) => b[x, y] != '.' && char.IsUpper(b[x, y]) != white;

        void Slide((int, int)[] dirs)
        {
            foreach (var (dr, dc) in dirs)
            {
                int x = r + dr, y = c + dc;
                while (In(x, y))
                {
                    if (b[x, y] == '.') res.Add((x, y));
                    else
                    {
                        if (Enemy(x, y)) res.Add((x, y));
                        break;
                    }
                    x += dr;
                    y += dc;
                }
            }
        }

        void Step((int, int)[] dirs)
        {
            foreach (var (dr, dc) in dirs)
            {
                int x = r + dr, y = c + dc;
                if (In(x, y) && (b[x, y] == '.' || Enemy(x, y))) res.Add((x, y));
            }
        }

        switch (char.ToLower(p))
        {
            case 'p':
            {
                int dir = white ? -1 : 1;
                int startRow = white ? 6 : 1;
                if (In(r + dir, c) && b[r + dir, c] == '.')
                {
                    res.Add((r + dir, c));
                    if (r == startRow && b[r + 2 * dir, c] == '.') res.Add((r + 2 * dir, c));
                }
                foreach (int dc in new[] { -1, 1 })
                    if (In(r + dir, c + dc) && Enemy(r + dir, c + dc)) res.Add((r + dir, c + dc));
                break;
            }
            case 'n': Step(KnightD); break;
            case 'b': Slide(DiagD); break;
            case 'r': Slide(StraightD); break;
            case 'q': Slide(DiagD); Slide(StraightD); break;
            case 'k':
                Step(KingD);
                if (withCastle)
                {
                    if (p == 'K' && r == 7 && c == 4 && !IsAttacked(b, 7, 4, false))
                    {
                        if (cr[0] && b[7, 5] == '.' && b[7, 6] == '.' && b[7, 7] == 'R'
                            && !IsAttacked(b, 7, 5, false) && !IsAttacked(b, 7, 6, false))
                            res.Add((7, 6));
                        if (cr[1] && b[7, 3] == '.' && b[7, 2] == '.' && b[7, 1] == '.' && b[7, 0] == 'R'
                            && !IsAttacked(b, 7, 3, false) && !IsAttacked(b, 7, 2, false))
                            res.Add((7, 2));
                    }
                    if (p == 'k' && r == 0 && c == 4 && !IsAttacked(b, 0, 4, true))
                    {
                        if (cr[2] && b[0, 5] == '.' && b[0, 6] == '.' && b[0, 7] == 'r'
                            && !IsAttacked(b, 0, 5, true) && !IsAttacked(b, 0, 6, true))
                            res.Add((0, 6));
                        if (cr[3] && b[0, 3] == '.' && b[0, 2] == '.' && b[0, 1] == '.' && b[0, 0] == 'r'
                            && !IsAttacked(b, 0, 3, true) && !IsAttacked(b, 0, 2, true))
                            res.Add((0, 2));
                    }
                }
                break;
        }
        return res;
    }

    // Is square (r,c) attacked by the given side?
    private static bool IsAttacked(char[,] b, int r, int c, bool byWhite)
    {
        for (int i = 0; i < 8; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                char p = b[i, j];
                if (p == '.' || char.IsUpper(p) != byWhite) continue;

                if (char.ToLower(p) == 'p')
                {
                    int dir = byWhite ? -1 : 1;
                    if (i + dir == r && Math.Abs(j - c) == 1) return true;
                }
                else
                {
                    foreach (var m in Pseudo(b, i, j, NoCastle, false))
                        if (m.r == r && m.c == c) return true;
                }
            }
        }
        return false;
    }

    private static bool InCheck(char[,] b, bool white)
    {
        char king = white ? 'K' : 'k';
        for (int i = 0; i < 8; i++)
            for (int j = 0; j < 8; j++)
                if (b[i, j] == king) return IsAttacked(b, i, j, !white);
        return false;
    }

    private static void Apply(char[,] b, bool[] cr, int fr, int fc, int tr, int tc)
    {
        char p = b[fr, fc];
        b[tr, tc] = p;
        b[fr, fc] = '.';

        // castling: move the rook too
        if (p == 'K' && fr == 7 && fc == 4 && Math.Abs(tc - fc) == 2)
        {
            if (tc == 6) { b[7, 5] = 'R'; b[7, 7] = '.'; }
            else { b[7, 3] = 'R'; b[7, 0] = '.'; }
        }
        if (p == 'k' && fr == 0 && fc == 4 && Math.Abs(tc - fc) == 2)
        {
            if (tc == 6) { b[0, 5] = 'r'; b[0, 7] = '.'; }
            else { b[0, 3] = 'r'; b[0, 0] = '.'; }
        }

        // promotion (always to Queen)
        if (p == 'P' && tr == 0) b[tr, tc] = 'Q';
        if (p == 'p' && tr == 7) b[tr, tc] = 'q';

        // castling rights
        if (p == 'K') { cr[0] = false; cr[1] = false; }
        if (p == 'k') { cr[2] = false; cr[3] = false; }
        void Touch(int r, int c)
        {
            if (r == 7 && c == 7) cr[0] = false;
            if (r == 7 && c == 0) cr[1] = false;
            if (r == 0 && c == 7) cr[2] = false;
            if (r == 0 && c == 0) cr[3] = false;
        }
        Touch(fr, fc);
        Touch(tr, tc);
    }

    // Legal moves for the piece on (r,c): moves that don't leave own king in check
    private List<(int r, int c)> Legal(int r, int c)
    {
        var list = new List<(int r, int c)>();
        bool white = char.IsUpper(board[r, c]);
        foreach (var m in Pseudo(board, r, c, castle, true))
        {
            var copy = (char[,])board.Clone();
            var crCopy = (bool[])castle.Clone();
            Apply(copy, crCopy, r, c, m.r, m.c);
            if (!InCheck(copy, white)) list.Add(m);
        }
        return list;
    }

    private bool HasMoves(bool white)
    {
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
                if (board[r, c] != '.' && char.IsUpper(board[r, c]) == white && Legal(r, c).Count > 0)
                    return true;
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