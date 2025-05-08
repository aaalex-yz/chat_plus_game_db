using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Windows_Forms_Chat
{
    public partial class Form1 : Form
    {
        TicTacToe ticTacToe = new TicTacToe();
        TCPChatServer server = null;
        TCPChatClient client = null;

        public Form1()
        {
            InitializeComponent();

        }

        public bool CanHostOrJoin()
        {
            if (server == null && client == null)
                return true;
            else
                return false;
        }

        private void HostButton_Click(object sender, EventArgs e)
        {
            if (CanHostOrJoin())
            {
                try
                {
                    int port = int.Parse(MyPortTextBox.Text);
                    server = TCPChatServer.createInstance(port, ChatTextBox);
                    if (server == null)
                        throw new Exception("Incorrect port value!");

                    server.Start();

                }
                catch (Exception ex)
                {
                    ChatTextBox.Text += "Error: " + ex;
                    ChatTextBox.AppendText(Environment.NewLine);
                }
            }

        }

        private void JoinButton_Click(object sender, EventArgs e)
        {
            if (CanHostOrJoin())
            {
                try
                {
                    int port = int.Parse(MyPortTextBox.Text);
                    int serverPort = int.Parse(serverPortTextBox.Text);
                    client = TCPChatClient.CreateInstance(port, serverPort, ServerIPTextBox.Text, ChatTextBox, this);

                    if (client == null)
                        throw new Exception("Incorrect port value!");

                    client.ConnectToServer();

                }
                catch (Exception ex)
                {
                    client = null;
                    ChatTextBox.Text += "Error: " + ex;
                    ChatTextBox.AppendText(Environment.NewLine);
                }

            }
        }
        private void SendButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TypeTextBox.Text))
                return;

            string message = TypeTextBox.Text.Trim();

            if (client != null)
            {
                client.SendString(message);
                TypeTextBox.Clear();
            }
            else if (server != null)
            {
                if (message.StartsWith("!"))
                {
                    server.ProcessServerCommand(message);
                }
                else
                {
                    server.SendToAll(message, null);
                }

                TypeTextBox.Clear();
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // Add all buttons to the game
            ticTacToe.buttons.AddRange(new List<Button>
            {
                button1, button2, button3,
                button4, button5, button6,
                button7, button8, button9
            });

            // Hook each button to AttemptMove(i)
            for (int i = 0; i < ticTacToe.buttons.Count; i++)
            {
                int index = i; // Capture loop variable
                ticTacToe.buttons[i].Click += (s, e) => AttemptMove(index);
            }
        }

        private void AttemptMove(int i)
        {
            if (!ticTacToe.myTurn) return;

            bool validMove = ticTacToe.SetTile(i, ticTacToe.playerTileType);
            if (!validMove) return;

            // Tell the server the move
            client.SendString($"MOVE:{i}");

            ticTacToe.myTurn = false; // Wait for server to respond

            GameState gs = ticTacToe.GetGameState();
            if (gs == GameState.crossWins)
            {
                ChatTextBox.AppendText("X wins!\n");
                ticTacToe.ResetBoard();
            }
            else if (gs == GameState.naughtWins)
            {
                ChatTextBox.AppendText("O wins!\n");
                ticTacToe.ResetBoard();
            }
            else if (gs == GameState.draw)
            {
                ChatTextBox.AppendText("Draw!\n");
                ticTacToe.ResetBoard();
            }
        }

        public bool ProcessServerMessage(string message)
        {
            if (message.StartsWith("UPDATE_TILE:"))
            {
                var parts = message.Split(':');
                if (parts.Length == 3 && int.TryParse(parts[1], out int index))
                {
                    TileType t = parts[2] switch
                    {
                        "cross" => TileType.cross,
                        "naught" => TileType.naught,
                        _ => TileType.blank
                    };

                    ticTacToe.SetTile(index, t);
                }
                return true;
            }
            else if (message.StartsWith("YOUR_TURN"))
            {
                ticTacToe.myTurn = true;
                AddToChat("It's your turn!");
                return true;
            }
            else if (message.StartsWith("WAIT_TURN"))
            {
                ticTacToe.myTurn = false;
                string info = message.Contains('|') ? message.Split('|')[1] : "Waiting...";
                AddToChat(info);
                return true;
            }
            else if (message.StartsWith("BOARD_STATE:"))
            {
                string board = message.Substring("BOARD_STATE:".Length);
                ticTacToe.StringToGrid(board);
                return true;
            }
            else if (message == "X wins!" || message == "O wins!" || message == "It's a draw!")
            {
                MessageBox.Show(message, "Game Over");

                AddToChat($"[Game] {message}"); // NEW
                ticTacToe.myTurn = false; // Ensure no further moves are made // NEW

                ticTacToe.ResetBoard();
                return true;
            }

            return false; // let it fall through to normal chat display
        }

        private void AddToChat(string message)
        {
            ChatTextBox.AppendText(message + Environment.NewLine);
        }

        
        private void button1_Click(object sender, EventArgs e)
        {
            AttemptMove(0);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            AttemptMove(1);
        }

        private void button3_Click(object sender, EventArgs e)
        {
            AttemptMove(2);
        }

        private void button4_Click(object sender, EventArgs e)
        {
            AttemptMove(3);
        }

        private void button5_Click(object sender, EventArgs e)
        {
            AttemptMove(4);
        }

        private void button6_Click(object sender, EventArgs e)
        {
            AttemptMove(5);
        }

        private void button7_Click(object sender, EventArgs e)
        {
            AttemptMove(6);
        }

        private void button8_Click(object sender, EventArgs e)
        {
            AttemptMove(7);
        }

        private void button9_Click(object sender, EventArgs e)
        {
            AttemptMove(8);
        }
    }
}
