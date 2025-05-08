using System.Collections.Generic;
using System.Windows.Forms;
using System.Linq; // For LINQ operations
using System.Data.SQLite;  // For SQLite database operations

namespace Windows_Forms_Chat
{
    public enum TileType { blank, cross, naught }
    public enum GameState { playing, draw, crossWins, naughtWins }

    public class TicTacToe
    {
        public bool myTurn = true;
        public TileType playerTileType = TileType.blank; // Server will assign
        public List<Button> buttons = new List<Button>();
        public TileType[] grid = new TileType[9];

        public string GridToString()
        {
            char[] result = new char[9];
            for (int i = 0; i < 9; i++)
            {
                result[i] = grid[i] switch
                {
                    TileType.cross => 'X',
                    TileType.naught => 'O',
                    _ => '_'
                };
            }
            return new string(result);
        }

        public void StringToGrid(string s)
        {
            for (int i = 0; i < s.Length && i < 9; i++)
            {
                grid[i] = s[i] switch
                {
                    'X' => TileType.cross,
                    'O' => TileType.naught,
                    _ => TileType.blank
                };

                if (i < buttons.Count)
                    buttons[i].Text = TileTypeToString(grid[i]);
            }
        }

        public bool SetTile(int index, TileType tileType)
        {
            if (index < 0 || index >= grid.Length || grid[index] != TileType.blank)
                return false;

            grid[index] = tileType;
            if (index < buttons.Count)
                buttons[index].Text = TileTypeToString(tileType);

            return true;
        }

        public GameState GetGameState()
        {
            GameState state = GameState.playing;
            if (CheckForWin(TileType.cross))
                state = GameState.crossWins;
            else if (CheckForWin(TileType.naught))
                state = GameState.naughtWins;
            else if (CheckForDraw())
                state = GameState.draw;


            return state;
        }
        public bool CheckForWin(TileType t)
        {
            if (grid[0] == t && grid[1] == t && grid[2] == t)
                return true;
            if (grid[3] == t && grid[4] == t && grid[5] == t)
                return true;
            if (grid[6] == t && grid[7] == t && grid[8] == t)
                return true;

            if (grid[0] == t && grid[3] == t && grid[6] == t)
                return true;
            if (grid[1] == t && grid[4] == t && grid[7] == t)
                return true;
            if (grid[2] == t && grid[5] == t && grid[8] == t)
                return true;

            if (grid[0] == t && grid[4] == t && grid[8] == t)
                return true;
            if (grid[2] == t && grid[4] == t && grid[6] == t)
                return true;


            return false;
        }

        public bool CheckForDraw()
        {
            for (int i = 0; i < 9; i++)
            {
                if (grid[i] == TileType.blank)
                    return false;
            }

            return true;
        }

        public void ResetBoard()
        {
            for (int i = 0; i < 9; i++)
            {
                grid[i] = TileType.blank;
                if (buttons.Count >= 9)
                    buttons[i].Text = TileTypeToString(TileType.blank);
            }
        }

        public static string TileTypeToString(TileType t)
        {
            if (t == TileType.blank)
                return "";
            else if (t == TileType.cross)
                return "X";
            else
                return "O";
        }
    }
}
