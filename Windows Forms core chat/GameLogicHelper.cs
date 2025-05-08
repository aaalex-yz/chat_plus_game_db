using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;

namespace Windows_Forms_Chat
{
    public class GameLogicHelper
    {
        public static void InitializeGame(TicTacToe game, ClientSocket player1, ClientSocket player2)
        {
            game.ResetBoard(); // ✅ Restore this to clear the board

            // Attach game to each player
            player1.currentGame = game;
            player2.currentGame = game;

            // ✅ Set Player 1’s turn to true
            player1.myTurn = true;
            player2.myTurn = false;

            player1.state = ClientState.Playing;
            player2.state = ClientState.Playing;

            // Notify players
            player1.socket.Send(Encoding.ASCII.GetBytes("It's your turn!"));
            player2.socket.Send(Encoding.ASCII.GetBytes("WAIT_TURN|Waiting for Player 1..."));

            Console.WriteLine("[InitGame] Game initialized.");
            Console.WriteLine($"[InitGame] P1: {player1.username}, Turn: {player1.myTurn}");
            Console.WriteLine($"[InitGame] P2: {player2.username}, Turn: {player2.myTurn}");
        }


        // Replace the existing HandleMove with this single version:
        public static void HandleMove(ClientSocket player, int index, List<ClientSocket> clients, DbUserManager dbUserManager)
        {
            // Safeguard check with debug output
            if (player == null)
            {
                Console.WriteLine("[HandleMove] Player is null.");
                return;
            }

            if (player.state != ClientState.Playing)
            {
                Console.WriteLine($"[HandleMove] {player.username} is not in a Playing state.");
                return;
            }

            if (player.currentGame == null)
            {
                Console.WriteLine($"[HandleMove] {player.username} has no active game.");
                return;
            }

            if (!player.myTurn)
            {
                Console.WriteLine($"[HandleMove] It's not {player.username}'s turn.");
                return;
            }

            if (index < 0 || index > 8)
            {
                Console.WriteLine($"[HandleMove] Invalid move index: {index}");
                return;
            }

            var success = player.currentGame.SetTile(index, player == clients[0] ? TileType.cross : TileType.naught);

            if (!success)
            {
                Console.WriteLine($"[HandleMove] Tile {index} already occupied.");
                return;
            }

            // Broadcast move to all clients
            foreach (var c in clients)
            {
                c.socket.Send(Encoding.ASCII.GetBytes($"UPDATE_TILE:{index}:{(player == clients[0] ? "cross" : "naught")}"));
            }

            var gameState = player.currentGame.GetGameState();

            if (gameState == GameState.crossWins || gameState == GameState.naughtWins || gameState == GameState.draw)
            {
                HandleGameEnd(gameState, clients, dbUserManager);
            }
            else
            {
                // Switch turns
                var nextPlayer = clients.FirstOrDefault(p => p != player && p.state == ClientState.Playing);
                if (nextPlayer != null)
                {
                    player.myTurn = false;
                    nextPlayer.myTurn = true;

                    player.socket.Send(Encoding.ASCII.GetBytes("WAIT_TURN|Waiting for opponent..."));
                    nextPlayer.socket.Send(Encoding.ASCII.GetBytes("YOUR_TURN"));

                    Console.WriteLine($"[HandleMove] {player.username} played. It's now {nextPlayer.username}'s turn.");
                }
            }

        }

        private static void HandleGameEnd(GameState state, List<ClientSocket> clients, DbUserManager dbUserManager)
        {
            string resultMessage = state switch
            {
                GameState.crossWins => "X wins!",
                GameState.naughtWins => "O wins!",
                GameState.draw => "It's a draw!",
                _ => "Unexpected result"
            };

            foreach (var c in clients.Where(c => c.state == ClientState.Playing))
            {
                c.socket.Send(Encoding.ASCII.GetBytes(resultMessage));
                c.state = ClientState.Chatting; // Reset state
            }

            // Update scores if usernames are available
            if (clients.Count == 2 && !string.IsNullOrEmpty(clients[0].username) && !string.IsNullOrEmpty(clients[1].username))
            {
                string player1 = clients[0].username;
                string player2 = clients[1].username;

                if (state == GameState.crossWins)
                    dbUserManager.UpdateGameStats(player1, win: 1, loss: 0, draw: 0, opponent: player2);
                else if (state == GameState.naughtWins)
                    dbUserManager.UpdateGameStats(player2, win: 1, loss: 0, draw: 0, opponent: player1);
                else if (state == GameState.draw)
                {
                    dbUserManager.UpdateGameStats(player1, win: 0, loss: 0, draw: 1, opponent: player2);
                    dbUserManager.UpdateGameStats(player2, win: 0, loss: 0, draw: 1, opponent: player1);
                }
            }
        }

    }
}
