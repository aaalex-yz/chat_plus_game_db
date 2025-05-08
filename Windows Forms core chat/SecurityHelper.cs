using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Windows_Forms_Chat;

namespace Windows_Forms_Chat
{
    public class SecurityHelper
    {
        private readonly TCPChatServer server;

        public SecurityHelper(TCPChatServer serverInstance)
        {
            server = serverInstance;
        }

        public bool IsUsernameValid(string requestedUsername, ClientSocket sender, List<ClientSocket> clientSockets)
        {
            // Basic format validation
            if (string.IsNullOrWhiteSpace(requestedUsername))
            {
                sender.socket.Send(Encoding.ASCII.GetBytes("[Username can't be empty]\n"));
                return false;
            }

            if (!Regex.IsMatch(requestedUsername, @"^[a-zA-Z0-9]+$"))
            {
                sender.socket.Send(Encoding.ASCII.GetBytes("[Invalid username, no special characters allowed]\n"));
                return false;
            }

            if (requestedUsername.StartsWith("user", StringComparison.OrdinalIgnoreCase))
            {
                sender.socket.Send(Encoding.ASCII.GetBytes("[Username cannot start with 'user', prefix reserved for the system.]\n"));
                return false;
            }

            if (sender.username?.Equals(requestedUsername, StringComparison.OrdinalIgnoreCase) ?? false)
            {
                sender.socket.Send(Encoding.ASCII.GetBytes("[You are already using this username]\n"));
                return false;
            }

            // Check if another *connected* user is using it
            bool isTakenByConnectedUser = clientSockets
                .Where(c => c != sender)
                .Any(c => c.username?.Equals(requestedUsername, StringComparison.OrdinalIgnoreCase) ?? false);

            if (isTakenByConnectedUser)
            {
                sender.socket.Send(Encoding.ASCII.GetBytes("[Username already in use by another connected user]\n"));
                return false;
            }

            // Final DB check only relevant during !user rename
            if (sender.state == ClientState.Chatting)
            {
                bool existsInDb = DbUserManager.DbUsernameCheck(requestedUsername);
                if (existsInDb)
                {
                    sender.socket.Send(Encoding.ASCII.GetBytes("[Username is already registered by another user]\n"));
                    return false;
                }
            }

            return true;
        }

        public void HandlePasswordInput(ClientSocket sender, string input, DbUserManager dbUserManager)
        {
            // Add debug log
            Console.WriteLine($"[PASSWORD] State: {sender.state}, TempUser: {sender.tempUsername}");

            // Check if the input is "Cancel" and handle it
            if (input.Equals("Cancel", StringComparison.OrdinalIgnoreCase))
            {
                // Call the ResetSession directly from ClientSocket.cs
                Console.WriteLine($"[CANCEL] Resetting session for {sender.tempUsername}");
                sender.ResetSession(); // Uses the more comprehensive built-in method
                sender.socket.Send(Encoding.ASCII.GetBytes("Login/Register cancelled. You are back at the welcome page.\n"));

                string prompt = "To join the chat, please Login/Register.\r\n" +
                                "\r\n\tType: !username YourNameHere" +
                                "\r\n\tExample: !username Bob\r\n" +
                                "\r\nNote: You won't be able to chat until completing this step.\r\n" +
                                "\r\nTo get a list of available commands, use !commands\r\n";

                // CRITICAL: Restart receive
                sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, server.ReceiveCallback, sender);

                sender.socket.Send(Encoding.ASCII.GetBytes(prompt));
                return;
            }

            // Proceed to login if in Login state
            if (sender.state == ClientState.Login)
            {
                if (dbUserManager.TryAuthenticateUser(sender.tempUsername, input))
                {
                    sender.socket.Send(Encoding.ASCII.GetBytes("__clearChat__"));
                    sender.username = sender.tempUsername;                               
                    sender.state = ClientState.Chatting;                    
                    string joinMessage = $"\r\n[{sender.username}] has joined the chat!";
                    sender.socket.Send(Encoding.ASCII.GetBytes("Login successful! Welcome back.\r\n"));
                    sender.socket.Send(Encoding.ASCII.GetBytes(joinMessage));
                    sender.socket.BeginReceive(
                sender.buffer, 0, ClientSocket.BUFFER_SIZE,
                SocketFlags.None, sender.server.ReceiveCallback, sender);
                    sender.server.AddToChat($"\r\n\tUser Logged in: [{sender.tempUsername}] | State: [{sender.state}]");
                }
                else
                {
                    sender.loginAttempts++;
                    if (sender.loginAttempts >= 3)
                    {                      
                        sender.socket.Send(Encoding.ASCII.GetBytes("[Too many failed attempts. Returning to welcome page.]\n"));

                        string prompt = "To join the chat, please Login/Register.\r\n" +
                                        "\r\n\tType: !username YourNameHere" +
                                        "\r\n\tExample: !username Bob\r\n" +
                                        "\r\nNote: You won't be able to chat until completing this step.\r\n" +
                                        "\r\nTo get a list of available commands, use !commands\r\n";

                        sender.socket.Send(Encoding.ASCII.GetBytes(prompt)); // send prompt

                        sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, 
                        SocketFlags.None, server.ReceiveCallback, sender); // restart receive

                        sender.ResetSession(); // Reset state
                        return;
                    }
                    else
                    {
                        sender.socket.Send(Encoding.ASCII.GetBytes($"[Incorrect password. Attempt {sender.loginAttempts}/3] Try again or type 'Cancel':\n"));
                        sender.server.AddToChat($"\r\n\tClient failed login: [{sender.tempUsername}] | Remaining attempts: {3 - sender.loginAttempts}");

                        sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, server.ReceiveCallback, sender);
                        return; // Stop further processing
                    }
                  
                }
            }
            // Proceed to registration if in Register state
            else if (sender.state == ClientState.Register)
            {
                if (string.IsNullOrEmpty(sender.tempUsername)) // Add this check
                {
                    Console.WriteLine("[ERROR] Register state with no tempUsername!");
                    sender.socket.Send(Encoding.ASCII.GetBytes("[SYSTEM ERROR] Missing username. Please reconnect.\n"));
                    
                    sender.ResetSession();
                    sender.socket.BeginReceive(
                sender.buffer, 0, ClientSocket.BUFFER_SIZE,
                SocketFlags.None, sender.server.ReceiveCallback, sender);
                    return;
                }
                if (!SecurityHelper.IsPasswordValid(input, sender))
                {
                    sender.socket.Send(Encoding.ASCII.GetBytes("Please enter a valid password or type 'Cancel' to go back:\n"));

                    // Restart receive for new password attempt
                    sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, server.ReceiveCallback, sender);
                    return;
                }

                if (dbUserManager.TryRegisterUser(sender.tempUsername, input))
                {
                    sender.socket.Send(Encoding.ASCII.GetBytes("__clearChat__"));
                    sender.username = sender.tempUsername;
                    sender.lobbyUsername = sender.tempUsername; // Keep these in sync
                    sender.tempUsername = null;
                    sender.state = ClientState.Chatting;

                    string joinMessage = $"\r\n[{sender.username}] has joined the chat!";
                    sender.socket.Send(Encoding.ASCII.GetBytes("Registration successful! Welcome to the Chat\r\n"));
                    sender.socket.Send(Encoding.ASCII.GetBytes(joinMessage));
                    sender.server.AddToChat($"\r\n\tUsername Registered: [{sender.lobbyUsername}] | State: [{sender.state}]");
                }
                else
                {
                    sender.socket.Send(Encoding.ASCII.GetBytes("[Something went wrong. Username might already exist. Type 'Cancel' or try again.]\n"));
                }
            }
        }

        public static bool IsPasswordValid(string password, ClientSocket sender)
        {
            if (password.Equals("Cancel", StringComparison.OrdinalIgnoreCase))
                return true;  // Allow cancel, it’s a special command

            if (password.Length < 6 || !Regex.IsMatch(password, @"[0-9]") || !Regex.IsMatch(password, @"[\W_]"))
            {
                sender.socket.Send(Encoding.ASCII.GetBytes("[Password must be at least 6 characters, include a number and a special character.]"));
                return false;
            }

            return true;
        }

        public static string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashBytes);
            }
        }
    }
}
