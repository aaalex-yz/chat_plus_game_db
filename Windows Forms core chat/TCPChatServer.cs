using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;
using System.Linq;
using System.Text.RegularExpressions;

namespace Windows_Forms_Chat
{
    public class TCPChatServer : TCPChatBase
    {
        /* ====== Server Properties ====== */
        public Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        public List<ClientSocket> clientSockets = new List<ClientSocket>();
        public List<ClientSocket> moderators = new List<ClientSocket>();// Added for Step 5 moderator tracking
        public bool isServer = true; // Flag to identify server instance

        public static TCPChatServer createInstance(int port, TextBox chatTextBox)
        {
            TCPChatServer tcp = null;
            if (port > 0 && port < 65535 && chatTextBox != null)
            {
                tcp = new TCPChatServer();
                tcp.port = port;
                tcp.chatTextBox = chatTextBox;
            }
            return tcp;
        }

        public void SetupServer()
        {
            // Initialize server with improved text display
            chatTextBox.Clear();
            chatTextBox.AppendText("Setting up server..." + Environment.NewLine);
            serverSocket.Bind(new IPEndPoint(IPAddress.Any, port));
            serverSocket.Listen(0);
            serverSocket.BeginAccept(AcceptCallback, this);
            chatTextBox.AppendText("Server setup complete" + Environment.NewLine);
        }

        public void CloseAllSockets()
        {
            foreach (ClientSocket clientSocket in clientSockets)
            {
                clientSocket.socket.Shutdown(SocketShutdown.Both);
                clientSocket.socket.Close();
            }
            clientSockets.Clear();
            serverSocket.Close();
        }

        public void AcceptCallback(IAsyncResult AR)
        {
            Socket joiningSocket;

            try
            {
                joiningSocket = serverSocket.EndAccept(AR);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            ClientSocket newClientSocket = new ClientSocket();
            newClientSocket.socket = joiningSocket;

            clientSockets.Add(newClientSocket);
            joiningSocket.BeginReceive(newClientSocket.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, newClientSocket);
            AddToChat("\r\nClient connected, waiting for request...");
            // ====== Step 1: !username command onboarding hint ======

            // Added command hint for new connections as part of username setup
            AddToChat("\r\nTo get a list of available server commands, use !commands");

            // ====== End Step 1 ======
            serverSocket.BeginAccept(AcceptCallback, null);
        }

        public void ReceiveCallback(IAsyncResult AR)
        {
            ClientSocket currentClientSocket = (ClientSocket)AR.AsyncState;
            int received;

            try
            {
                received = currentClientSocket.socket.EndReceive(AR);

                if (received == 0)
                {
                    HandleDisconnect(currentClientSocket);
                    return;
                }

                byte[] recBuf = new byte[received];
                Array.Copy(currentClientSocket.buffer, recBuf, received);
                string text = Encoding.ASCII.GetString(recBuf).Trim();

                if (text.StartsWith("!"))
                {
                    HandleClientCommand(text, currentClientSocket);
                    return;
                }

                // ====== Step 4: Whisper/private chat message routing ======

                // Verify target exists and is still in private chat with this user, part of step 4 [!whisper command]
                if (currentClientSocket.privateChatTarget != null)
                {
                    ClientSocket target = currentClientSocket.privateChatTarget;

                    if (target == null || !clientSockets.Contains(target) ||
                        target.privateChatTarget != currentClientSocket)
                    {
                        currentClientSocket.socket.Send(Encoding.ASCII.GetBytes(
                            "[Private chat ended]"));
                        currentClientSocket.privateChatTarget = null;
                        return;
                    }

                    string outgoingMsg = $"[Private to {target.username}]: {text}";
                    string incomingMsg = $"[Private from {currentClientSocket.username}]: {text}";

                    currentClientSocket.socket.Send(Encoding.ASCII.GetBytes(outgoingMsg));
                    target.socket.Send(Encoding.ASCII.GetBytes(incomingMsg));

                    AddToChat($"PRIVATE: [{currentClientSocket.username}] -> [{target.username}]: {text}");
                    return;
                }
                // ====== End Step 4 ======

                if (!string.IsNullOrEmpty(currentClientSocket.username))
                {
                    string formattedMsg = $"[{currentClientSocket.username}]: {text}";
                    SendToAll(formattedMsg, currentClientSocket);
                    AddToChat(formattedMsg);
                }
                else
                {
                    currentClientSocket.socket.Send(Encoding.ASCII.GetBytes(
                        "Please set a username first with !username [yourname]"));
                }
            }
            catch (Exception ex)
            {
                HandleDisconnect(currentClientSocket);
                AddToChat($"[Error] {ex.Message}");
                return;
            }

            try
            {
                currentClientSocket.socket.BeginReceive(
                    currentClientSocket.buffer, 0,
                    ClientSocket.BUFFER_SIZE,
                    SocketFlags.None,
                    ReceiveCallback, currentClientSocket);
            }
            catch (Exception ex)
            {
                HandleDisconnect(currentClientSocket);
                AddToChat($"[Error] {ex.Message}");
            }
        }

        private void HandleDisconnect(ClientSocket client)
        {
            if (client.username != null)
            {
                if (client.privateChatTarget != null)
                {
                    ClientSocket partner = client.privateChatTarget;
                    partner.privateChatTarget = null;
                    partner.socket.Send(Encoding.ASCII.GetBytes($"[{client.username}] has left the private chat. You are now in global chat."));
                }

                SendToAll($"[{client.username}] has disconnected", client);
                AddToChat($"[{client.username}] disconnected");
            }

            client.socket.Close();
            clientSockets.Remove(client);
        }

        // ====== Steps 1–4: Refined Message Broadcasting (Global and Private) ======

        public void SendToAll(string str, ClientSocket from)
        {
            // If the sender is null, it's a server-originated message, so prepend with [Server]:
            string messageToSend = from == null ? $"[Server]: {str}" : str;

            // Log server-originated messages in the server chat window
            if (from == null)
            {
                AddToChat(messageToSend);
            }

            // Broadcast the message to all connected clients
            foreach (ClientSocket c in clientSockets.ToList())
            {
                try
                {
                    if (c.socket.Connected)
                    {
                        // Send the message as ASCII-encoded bytes
                        byte[] data = Encoding.ASCII.GetBytes(messageToSend);
                        c.socket.Send(data);
                    }
                    else
                    {
                        // Log a warning if the target socket is disconnected
                        AddToChat($"[Warning] Failed to send to {c.username} - socket disconnected");
                    }
                }
                catch (Exception ex)
                {
                    // Log any exceptions that occur during send
                    AddToChat($"[Send Error to {c.username}] {ex.Message}");
                }
            }
        }
        private void SendToAllExceptSender(string message, ClientSocket sender)
        {
            // Broadcast a message to all users EXCEPT the original sender (used in commands like !global)
            foreach (ClientSocket c in clientSockets)
            {
                if (c != sender)
                {
                    byte[] data = Encoding.ASCII.GetBytes(message);
                    c.socket.Send(data);
                }
            }
        }

        // ====== End Steps 1–4 ======


        private void HandleClientCommand(string text, ClientSocket sender)
        {
            string cmd = text.ToLower();

            try
            {
                // ───── STEP 4: Command - !commands ─────
                if (cmd == "!commands")
                {
                    // Sends a list of all available client commands
                    string ClientCommandsList = "Commands are:\r\n\r\n" +
                        "\t!about:  Get information about this app\r\n" +
                        "\t!commands:  Show this list\r\n" +
                        "\t!exit:  Log out from the chat\r\n" +
                        "\t!global:  Return to global chat from a private conversation\r\n" +
                        "\t!kick [username]:  Kick a user from the chat (Moderators only)\r\n" +
                        "\t!time:  Show the current time\r\n" +
                        "\t!user [new_username]:  Change your username\r\n" +
                        "\t!username [your_username]:  Set a username\r\n" +
                        "\t!whisper [username]:  Start a private chat\r\n" +
                        "\t!who:  List currently online users";

                    sender.socket.Send(Encoding.ASCII.GetBytes(ClientCommandsList));
                    AddToChat("Sent command list to client.");
                }

                // ───── STEP 1: Username Handling - !exit ─────
                else if (cmd.StartsWith("!exit"))
                {
                    try
                    {
                        // Handles user logout and resets their session
                        string username = sender.username;
                        sender.socket.Send(Encoding.ASCII.GetBytes("You have been logged out. Please set a username to rejoin."));

                        if (!string.IsNullOrEmpty(username))
                        {
                            SendToAllExceptSender($"[{username}] has left the chat!", sender);
                        }

                        AddToChat($"Client [{username}] disconnected via !exit command");

                        sender.username = null;

                        string prompt = "To join the chat, please choose a username.\r\n" +
                                        "\r\n\tType: !username YourNameHere" +
                                        "\r\n\tExample: !username Bob\r\n" +
                                        "\r\nNote: You won't be able to chat until a valid username is set.\r\n" +
                                        "\r\nUse !commands to see available commands.\r\n";

                        sender.socket.Send(Encoding.ASCII.GetBytes(prompt));

                        sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                    }
                    catch (Exception ex)
                    {
                        AddToChat($"[Error handling !exit] {ex.Message}");
                        sender.socket.Send(Encoding.ASCII.GetBytes("[An error occurred while processing your request. Please try again later.]"));
                    }
                    return;
                }

                // ───── STEP 4: Command - !global ─────
                else if (cmd == "!global")
                {
                    try
                    {
                        // Ends private chat session and returns user to global chat
                        string username = sender.username;

                        if (sender.privateChatTarget == null)
                        {
                            sender.socket.Send(Encoding.ASCII.GetBytes("Uh-oh, you are already in the global chat!"));
                            sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE,
                                SocketFlags.None, ReceiveCallback, sender);
                            return;
                        }

                        if (sender.privateChatTarget != null)
                        {
                            ClientSocket partner = sender.privateChatTarget;

                            if (partner != null && partner.privateChatTarget == sender)
                            {
                                partner.privateChatTarget = null;
                                if (partner.socket.Connected)
                                {
                                    partner.socket.Send(Encoding.ASCII.GetBytes(
                                        $"[{username}] has left the private chat. You are now in global chat."));
                                }
                            }

                            sender.privateChatTarget = null;
                            AddToChat($"Private chat between [{username}] <-> [{partner.username}] ended using !global.");
                        }

                        sender.socket.Send(Encoding.ASCII.GetBytes("You have exited the private chat. You are now in the global chat."));

                        if (!string.IsNullOrEmpty(username))
                        {
                            SendToAllExceptSender($"[{username}] has joined the global chat.", sender);
                        }

                        AddToChat($"[{username}] joined the global chat.");
                    }
                    catch (Exception ex)
                    {
                        AddToChat($"[Error handling !global] {ex.Message}");
                        try
                        {
                            sender.socket.Send(Encoding.ASCII.GetBytes("[Error occurred while processing !global]"));
                        }
                        catch { }
                    }

                    try
                    {
                        sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE,
                            SocketFlags.None, ReceiveCallback, sender);
                    }
                    catch (Exception ex)
                    {
                        AddToChat($"[Error restarting receive] {ex.Message}");
                        HandleDisconnect(sender);
                    }

                    return;
                }

                // ───── STEP 1: Username Handling - !username ─────
                else if (cmd.StartsWith("!username"))
                {
                    string[] parts = text.Split(new char[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    string requestedUsername = parts.Length > 1 ? parts[1].Trim() : "";

                    if (!string.IsNullOrWhiteSpace(sender.username))
                    {
                        sender.socket.Send(Encoding.ASCII.GetBytes("[You are already signed in. To create a new username, please disconnect and reconnect.]"));
                        sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                        return;
                    }

                    if (!IsUsernameValid(requestedUsername, sender))
                    {
                        sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                        return;
                    }

                    sender.username = requestedUsername;
                    sender.socket.Send(Encoding.ASCII.GetBytes("Valid username"));
                    AddToChat($"A new client [{requestedUsername}] has joined!");
                    SendToAll($"[{requestedUsername}] has joined the chat!", sender);
                }

                // ───── STEP 3: Username Management - !user ─────
                else if (cmd.StartsWith("!user "))
                {
                    if (string.IsNullOrWhiteSpace(sender.username))
                    {
                        sender.socket.Send(Encoding.ASCII.GetBytes("[You must set a username first using !username]"));
                        sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                        return;
                    }

                    string[] parts = text.Split(new char[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    string requestedUsername = parts.Length > 1 ? parts[1].Trim() : "";

                    if (!IsUsernameValid(requestedUsername, sender))
                    {
                        sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                        return;
                    }

                    string oldUsername = sender.username;
                    sender.username = requestedUsername;

                    sender.socket.Send(Encoding.ASCII.GetBytes("Your username has changed!"));

                    string changeMsg = $"[{oldUsername}] has changed their username to [{requestedUsername}]";
                    SendToAll(changeMsg, sender);
                    AddToChat(changeMsg);
                }

                // ───── STEP 4: Command - !about ─────
                else if (cmd.StartsWith("!about"))
                {
                    // Sends information about the application
                    string aboutInfo = "\r\nTCP Chat Application: Your gateway to real-time communication! Connect, chat, and manage your identity in a secure, simple chat environment.\r\n" +
                                       "\r\nDeveloped by: Daniel (A00151824)" +
                                       "\r\nFor academic purposes, as part of NDS203 Assessment 2" +
                                       "\r\nYear: 2025";
                    sender.socket.Send(Encoding.ASCII.GetBytes(aboutInfo));
                    AddToChat("Sent about info to client.");
                }

                // ───── STEP 4: Command - !who ─────
                else if (cmd == "!who")
                {
                    // Displays list of currently online users
                    List<string> onlineUsers = clientSockets
                        .Where(c => c.username != null)
                        .Select(c => c.username)
                        .ToList();

                    if (onlineUsers.Count == 0) onlineUsers.Add("No users online.");
                    if (onlineUsers.Count == 1 && onlineUsers[0] == sender.username)
                    {
                        sender.socket.Send(Encoding.ASCII.GetBytes("Online Users:\r\nJust you, for now! :)"));
                    }
                    else
                    {
                        sender.socket.Send(Encoding.ASCII.GetBytes("Online Users:\r\n" + string.Join("\r\n", onlineUsers)));
                    }

                    AddToChat("Sent list of online users.");
                }

                // ───── STEP 4: Command - !whisper ─────
                else if (cmd.StartsWith("!whisper"))
                {
                    // Initiates private chat between sender and target user
                    if (string.IsNullOrEmpty(sender.username))
                    {
                        sender.socket.Send(Encoding.ASCII.GetBytes("[Please set username first]"));
                        return;
                    }

                    string[] parts = text.Split(new[] { ' ' }, 2);
                    if (parts.Length < 2)
                    {
                        sender.socket.Send(Encoding.ASCII.GetBytes("[Please specify a user]"));
                        return;
                    }

                    string targetUsername = parts[1].Trim();

                    if (sender.username.Equals(targetUsername, StringComparison.OrdinalIgnoreCase))
                    {
                        sender.socket.Send(Encoding.ASCII.GetBytes("[You can't whisper to yourself]"));
                        return;
                    }

                    var target = clientSockets.FirstOrDefault(c =>
                        c.username?.Equals(targetUsername, StringComparison.OrdinalIgnoreCase) ?? false);

                    if (target == null)
                    {
                        sender.socket.Send(Encoding.ASCII.GetBytes("[User not found]"));
                        return;
                    }

                    sender.privateChatTarget = target;
                    target.privateChatTarget = sender;

                    sender.socket.Send(Encoding.ASCII.GetBytes(
                        $"Private chat with [{target.username}] started! Type !global to return to main chat."));
                    target.socket.Send(Encoding.ASCII.GetBytes(
                        $"[{sender.username}] started a private chat with you! Type !global to return to main chat."));

                    AddToChat($"A client [{sender.username}] started a private chat with [{target.username}]");
                }

                // ───── STEP 4: Command - !time ─────
                else if (cmd == "!time")
                {
                    // Sends current system time
                    string time = DateTime.Now.ToString("hh:mm:ss tt");
                    sender.socket.Send(Encoding.ASCII.GetBytes($"The current time is: {time}"));
                    AddToChat("Sent current time to client.");
                }

                // ───── STEP 5: Moderator Command - !kick ─────
                else if (cmd.StartsWith("!kick"))
                {
                    if (!sender.isModerator)
                    {
                        // Block non-mod users from using kick
                        sender.socket.Send(Encoding.ASCII.GetBytes("[You do not have permission to use this command]"));
                        AddToChat($"[Unauthorized Kick Attempt] {sender.username} tried to use !kick");
                    }
                    else
                    {
                        // Forward kick command to server handler
                        HandleServerCommand(text, sender);
                    }

                    sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                    return;
                }

                // ───── UNKNOWN COMMAND FALLBACK ─────
                else
                {
                    byte[] data = Encoding.ASCII.GetBytes("Unknown command. Type !commands for a list of available commands.");
                    sender.socket.Send(data);

                    if (!string.IsNullOrEmpty(sender.username))
                    {
                        AddToChat($"[{sender.username}] tried unknown command: {cmd}");
                    }
                    else
                    {
                        AddToChat($"Error: A client without a username tried unknown command: {cmd}");
                    }

                    sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                }
            }
            catch (Exception ex)
            {
                // Generic catch for command processing failures
                AddToChat($"[Error handling command '{cmd}'] {ex.Message}");
                sender.socket.Send(Encoding.ASCII.GetBytes("[An error occurred while processing your request. Please try again later.]"));
            }

            // Always resume listening
            sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
        }


        // ───────────────────────────────────────────────────────────────────────────────
        // STEP 4: IMPLEMENTATION OF COMMANDS (!commands, !mod, !kick, !mods)
        // ───────────────────────────────────────────────────────────────────────────────
        private void HandleServerCommand(string text, ClientSocket from)
        {
            string cmd = text.ToLower();

            try
            {
                if (cmd == "!commands")
                {
                    string serverCommandsList =
                        "\r\nCommands are:\r\n" +
                        "\r\n\t!commands:  Show this list" +
                        "\r\n\t!mod [username]:  Promote/demote a user to/from moderator" +
                        "\r\n\t!kick [username]:  Kick a user from the chat" +
                        "\r\n\t!mods:  Display current moderators";

                    AddToChat(serverCommandsList);
                    return;
                }
                else if (cmd == "!mods")
                {
                    var modList = clientSockets
                        .Where(c => c.isModerator && !string.IsNullOrEmpty(c.username))
                        .Select(c => c.username)
                        .ToList();

                    if (modList.Count == 0)
                    {
                        AddToChat("Moderators List: No current moderators.");
                    }
                    else
                    {
                        AddToChat("Moderators List:\r\n" + string.Join("\r\n", modList));
                    }
                    return;
                }
                else if (cmd.StartsWith("!mod "))
                {
                    string targetUsername = text.Substring(5).Trim();

                    if (string.IsNullOrEmpty(targetUsername))
                    {
                        AddToChat("[Please provide a valid username.]");
                        return;
                    }

                    var targetClient = clientSockets.FirstOrDefault(c =>
                        c.username != null && c.username.Equals(targetUsername, StringComparison.OrdinalIgnoreCase));

                    if (targetClient != null)
                    {
                        if (targetClient.isModerator)
                        {
                            targetClient.isModerator = false;
                            AddToChat($"[{targetUsername}] has been demoted from moderator.");
                            targetClient.socket.Send(Encoding.ASCII.GetBytes("[Server Notice]: You have been demoted from moderator."));
                        }
                        else
                        {
                            targetClient.isModerator = true;
                            AddToChat($"[{targetUsername}] has been promoted to moderator.");
                            targetClient.socket.Send(Encoding.ASCII.GetBytes("[Server Notice]: You have been promoted to moderator!"));
                        }
                    }
                    else
                    {
                        AddToChat($"User [{targetUsername}] not found.");
                    }
                    return;
                }
                else if (cmd.StartsWith("!kick "))
                {
                    string[] parts = text.Split(' ', 2);
                    if (parts.Length == 2)
                    {
                        string targetUsername = parts[1].Trim();

                        if (from != null && from.username.Equals(targetUsername, StringComparison.OrdinalIgnoreCase))
                        {
                            from.socket.Send(Encoding.ASCII.GetBytes("[Nice try! You can't kick yourself.]"));
                            return;
                        }

                        ClientSocket targetClient = clientSockets.FirstOrDefault(c =>
                            c.username != null && c.username.Equals(targetUsername, StringComparison.OrdinalIgnoreCase));

                        if (targetClient != null)
                        {
                            string kickerName = from?.username ?? "SERVER";

                            targetClient.socket.Send(Encoding.ASCII.GetBytes("You have been kicked out!"));
                            AddToChat($"[{kickerName}] kicked [{targetUsername}] from the chat.");
                            SendToAll($"[{targetUsername}] has been removed by moderator [{kickerName}]", from);

                            // Remove username but keep connection alive
                            targetClient.username = null;

                            string prompt = "To join the chat, please choose a username.\r\n" +
                                            "\r\n\tType: !username YourNameHere" +
                                            "\r\n\tExample: !username Bob\r\n" +
                                            "\r\nNote: You won't be able to chat until a valid username is set.\r\n" +
                                            "\r\nUse !commands to see available commands.\r\n";
                            targetClient.socket.Send(Encoding.ASCII.GetBytes(prompt));

                            // Start listening again for the new username
                            targetClient.socket.BeginReceive(targetClient.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, targetClient);
                        }
                        else
                        {
                            from?.socket.Send(Encoding.ASCII.GetBytes($"[Kick Failed]: User '{targetUsername}' not found."));
                        }
                    }
                    return;
                }
                else
                {
                    AddToChat($"[Unknown Server Command]: {cmd}. Try !mod, !kick, or !mods.");
                }
            }
            catch (Exception ex)
            {
                AddToChat($"[Server Command Error]: {ex.Message}");
            }
        }

        // ───────────────────────────────────────────────────────────────────────────────
        // STEP 4 CONTINUED: HANDLER WRAPPER FOR SERVER COMMANDS
        // ───────────────────────────────────────────────────────────────────────────────
        public void ProcessServerCommand(string command, ClientSocket from = null)
        {
            HandleServerCommand(command, from);
        }

        // ───────────────────────────────────────────────────────────────────────────────
        // STEP 3: USERNAME VALIDATION LOGIC
        // ───────────────────────────────────────────────────────────────────────────────
        private bool IsUsernameValid(string requestedUsername, ClientSocket sender)
        {
            if (string.IsNullOrWhiteSpace(requestedUsername))
            {
                sender.socket.Send(Encoding.ASCII.GetBytes("[Username can't be empty]"));
                AddToChat($"Client attempted to set an empty username.");
                return false;
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(requestedUsername, @"^[a-zA-Z0-9]+$"))
            {
                sender.socket.Send(Encoding.ASCII.GetBytes("[Invalid username, no special characters allowed]"));
                return false;
            }

            bool isTaken = clientSockets.Exists(c =>
                c.username != null &&
                c.username.Equals(requestedUsername, StringComparison.OrdinalIgnoreCase)
            );

            if (isTaken)
            {
                sender.socket.Send(Encoding.ASCII.GetBytes("[Username already taken, please choose a different one]"));
                AddToChat($"Error: A client tried to use an already taken username -> {requestedUsername}");
                return false;
            }

            return true;
        }

    }
}