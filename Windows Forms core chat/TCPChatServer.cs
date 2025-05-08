using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;
using System.IO;  // Add this for File operations
using System.Data.SQLite;  // For SQLite database operations

namespace Windows_Forms_Chat
{
    // AMENDED CLASS
    public class TCPChatServer : TCPChatBase
    {
        ClientSocket[] currentPlayers = new ClientSocket[2]; // player 1 at index 0, player 2 at 1
        TicTacToe currentGame = new TicTacToe();        

        public Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        public List<ClientSocket> clientSockets = new List<ClientSocket>();
        public List<ClientSocket> moderators = new List<ClientSocket>();      
        public bool isServer = true;
        private bool isFirstClient = true; // bool to track the first client only to clear the chat window 
        private DbUserManager dbUserManager; // reference to DbUserManager.cs
        private SecurityHelper securityHelper; // reference to SecurityHelper.cs
        private GameLogicHelper gameHelper; // reference to GameLogicHelper.cs  


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


        // NEW METHOD
        // Add this method to the TCPChatServer class
        public void UpdateClientState(ClientSocket clientSocket, ClientState newState)
        {
            clientSocket.state = newState; // Update the client's state

        }
        // END NEW METHOD

        // NEW METHOD
        private void BroadcastGameState()
        {
            if (currentPlayers[0] == null || currentPlayers[1] == null) return;

            string boardState = currentGame.GridToString();
            foreach (var player in currentPlayers)
            {
                if (player?.socket.Connected == true)
                {
                    player.socket.Send(Encoding.ASCII.GetBytes($"BOARD_STATE:{boardState}"));
                }
            }
        }
        // END NEW METHOD
          
        //NEW METHOD

        public bool Start()
        {
            chatTextBox.Clear();

            // ASCII startup banner
            string banner = @"
==============================
        TCP Chat Server v1.0     
==============================            
";
            chatTextBox.AppendText(banner);
            chatTextBox.AppendText("Initializing server system..." + Environment.NewLine);

            // 1. Initialize Database via DbUserManager and SecurityHelper constructor
            try
            {
                string connectionString = "Data Source=ChatAppDB.sqlite;Version=3;";
                dbUserManager = new DbUserManager(connectionString); // This will initialize the database automatically
                securityHelper = new SecurityHelper(this); // Pass reference to TCPChatServer

                chatTextBox.AppendText("Database initialized successfully!" + Environment.NewLine);
            }
            catch (Exception ex)
            {
                chatTextBox.AppendText($"[FATAL] Database initialization failed. Error: {ex.Message}" + Environment.NewLine);
                return false;
            }

            // 2. Setup Network Components
            try
            {
                SetupServer(); // This contains the critical socket operations
                chatTextBox.AppendText("Server started successfully!" + Environment.NewLine);
                chatTextBox.AppendText("\r\nTo get a list of available server commands, use !commands" + Environment.NewLine); // server commands prompt
                return true;
            }
            catch (Exception ex)
            {
                chatTextBox.AppendText($"[NETWORK ERROR] Failed to start: {ex.Message}" + Environment.NewLine);
                return false;
            }
        }


        // END NEW METHOD


        // AMENDED METHOD
        public void SetupServer()
        {
            try
            {            
                serverSocket.Bind(new IPEndPoint(IPAddress.Any, port));
                serverSocket.Listen(0);
                serverSocket.BeginAccept(AcceptCallback, this);

                chatTextBox.AppendText("Network layer ready (listening on port " + port + ")" + Environment.NewLine); // New line added
            }
            catch (SocketException sex)
            {
                chatTextBox.AppendText($"[SOCKET ERROR] {sex.Message}" + Environment.NewLine);
                throw; // Re-throw for Start() to handle
            }
        }

        // END AMENDED METHOD     

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

        // AMENDED METHOD
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
            newClientSocket.server = this;  // Add this line to set the server reference

            // Initialize connection state
            newClientSocket.isNewConnection = true; // Mark as new connection
            newClientSocket.ResetSession(); // This will preserve the new connection state

            // Generate username ONLY for new connections
            if (newClientSocket.isNewConnection)
            {
                string guestName = DbUserManager.GenerateLobbyUsername();
                newClientSocket.lobbyUsername = guestName;
                newClientSocket.tempUsername = guestName;
                Console.WriteLine($"[CONNECTION] New client assigned: {guestName}");
            }

            clientSockets.Add(newClientSocket);
            joiningSocket.BeginReceive(newClientSocket.buffer, 0, ClientSocket.BUFFER_SIZE,
                SocketFlags.None, ReceiveCallback, newClientSocket);

            // Send temporary username to client
            byte[] welcomeMessage = Encoding.ASCII.GetBytes($"__lobbyUsername__:{newClientSocket.lobbyUsername}");
            joiningSocket.Send(welcomeMessage);

            // First connection UI handling (unchanged)
            if (isFirstClient)
            {
                chatTextBox.Clear();
                string banner = @"
==============================
        TCP Chat Server v1.0     
==============================

To get a list of available server commands, use !commands
";
                chatTextBox.AppendText(banner);
                isFirstClient = false;
            }

            AddToChat($"\r\n\tClient connected: [{newClientSocket.lobbyUsername}] | State: [{newClientSocket.state}]");

            serverSocket.BeginAccept(AcceptCallback, null);
        }


        // END AMENDED METHOD

        // AMENDED METHOD
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

                // Handle password input FIRST
                if ((currentClientSocket.state == ClientState.Register ||
                     currentClientSocket.state == ClientState.Login) &&
                    !text.StartsWith("!")) // Not a command
                {
                    securityHelper.HandlePasswordInput(currentClientSocket, text, dbUserManager);

                    return;
                }

                // Then handle commands
                if (text.StartsWith("!"))
                {
                    HandleClientCommand(text, currentClientSocket);
                    return;
                }

                // Handle MOVE input for Tic-Tac-Toe                
                if (text.StartsWith("MOVE:"))
                {
                    Console.WriteLine($"Processing MOVE from {currentClientSocket.username}");
                    if (int.TryParse(text.Substring(5), out int index) &&
                        currentPlayers.Contains(currentClientSocket))
                    {
                        GameLogicHelper.HandleMove(
                            currentClientSocket,
                            index,
                            clientSockets,
                            dbUserManager);

                        Console.WriteLine($"Processed move {index}"); // Debug log
                    }
                    return;
                }

                // Handle private messages (unchanged)
                if (currentClientSocket.privateChatTarget != null)
                {
                    ClientSocket target = currentClientSocket.privateChatTarget;

                    if (target == null || !clientSockets.Contains(target) || target.privateChatTarget != currentClientSocket)
                    {
                        currentClientSocket.socket.Send(Encoding.ASCII.GetBytes("[Private chat ended]"));
                        currentClientSocket.privateChatTarget = null;
                        return;
                    }

                    string outgoingMsg = $"[Private to {target.username}]: {text}";
                    string incomingMsg = $"[Private from {currentClientSocket.username}]: {text}";

                    currentClientSocket.socket.Send(Encoding.ASCII.GetBytes(outgoingMsg));
                    target.socket.Send(Encoding.ASCII.GetBytes(incomingMsg));

                    AddToChat($"PRIVATE: [{currentClientSocket.username}] → [{target.username}]: {text}");
                    return;
                }

                // General message handling
                if ((currentClientSocket.state == ClientState.Chatting || currentClientSocket.state == ClientState.Playing) ||
                (currentClientSocket.state == ClientState.Register && !string.IsNullOrEmpty(currentClientSocket.tempUsername)))
                {
                    // Fully authenticated or registered users can chat
                    string formattedMsg = $"[{currentClientSocket.username}]: {text}";
                    SendToAll(formattedMsg, currentClientSocket);
                    AddToChat(formattedMsg);
                }
                
                else if (currentClientSocket.state == ClientState.Lobby)
                {
                    currentClientSocket.socket.Send(Encoding.ASCII.GetBytes(
                        "You can't chat yet. Please complete the Login/Registration process first."));
                }
                
                else
                {
                    // Unexpected edge case: no username at all
                    currentClientSocket.socket.Send(Encoding.ASCII.GetBytes(
                        "No username set. Please set one using !username YourName."));
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

        // END AMENDED METHOD

        // AMENDED METHOD
        private void HandleDisconnect(ClientSocket client)
        {
            if (client.state == ClientState.Chatting || client.state == ClientState.Playing)
            {
                client.ResetSession(); // Ensures clean state on disconnect
            }
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

        // END AMENDED METHOD

        // AMENDED METHOD
        public void SendToAll(string str, ClientSocket from)
        {
            string messageToSend = from == null ? $"[Server]: {str}" : str;

            if (from == null)
            {
                AddToChat(messageToSend);
            }

            foreach (ClientSocket c in clientSockets.ToList())
            {
                // Only send to users in Chatting state and not in private chat
                if (c.state != ClientState.Chatting || c.privateChatTarget != null)
                    continue;

                try
                {
                    if (c.socket.Connected)
                    {
                        byte[] data = Encoding.ASCII.GetBytes(messageToSend);
                        c.socket.Send(data);

                        // Resume listening after sending
                        c.socket.BeginReceive(c.buffer, 0, ClientSocket.BUFFER_SIZE,
                            SocketFlags.None, ReceiveCallback, c);
                    }
                }
                catch (Exception ex)
                {
                    AddToChat($"[Send Error to {(c.username ?? "Unknown")}]: {ex.Message}");
                }
            }
        }

        // END AMENDED METHOD

        private void SendToAllExceptSender(string message, ClientSocket sender)
        {
            foreach (ClientSocket c in clientSockets.ToList())
            {
                // Skip sender, users in Login state, and those in private chat
                if (c == sender || c.state != ClientState.Chatting || c.privateChatTarget != null)
                    continue;

                try
                {
                    if (c.socket.Connected)
                    {
                        byte[] data = Encoding.ASCII.GetBytes(message);
                        c.socket.Send(data);

                        // Resume listening
                        c.socket.BeginReceive(c.buffer, 0, ClientSocket.BUFFER_SIZE,
                            SocketFlags.None, ReceiveCallback, c);
                    }
                }
                catch (Exception ex)
                {
                    AddToChat($"[Send Error to {(c.username ?? "Unknown")}]: {ex.Message}");
                }
            }
        }

        private static readonly Dictionary<ClientState, HashSet<string>> StateCommandWhitelist =
            new Dictionary<ClientState, HashSet<string>>
        {
            [ClientState.Login] = new HashSet<string> { "cancel" },
            [ClientState.Register] = new HashSet<string> { "cancel" },
            [ClientState.Lobby] = new HashSet<string> { "!about", "!commands", "!time", "!username" },
            [ClientState.Chatting] = new HashSet<string> { },
            [ClientState.Playing] = new HashSet<string> { "!about", "!commands", "!time", "!who" }
        };


        private void HandleClientCommand(string text, ClientSocket sender)
        {
            string cmd = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToLower();

            if (StateCommandWhitelist.TryGetValue(sender.state, out var allowedCommands) 
            && allowedCommands.Count > 0 
            && !allowedCommands.Contains(cmd))
            {
                sender.socket.Send(Encoding.ASCII.GetBytes(
                    "[Command not allowed in your current state.]\n"));
                sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                return;
            }

            try
            {
                if (cmd == "!commands")
                {
                    string ClientCommandsList = "Commands are:\r\n\r\n" +
                        "\t!about:  Get information about this app\r\n" +
                        "\t!commands:  Show this list\r\n" +
                        "\t!exit:  Log out from the chat\r\n" +
                        "\t!global:  Return to global chat from a private conversation\r\n" +
                        "\t!join:  Play Tic-tac-toe with other player\r\n" +
                        "\t!kick [username]:  Kick a user from the chat (Moderators only)\r\n" +
                        "\t!time:  Show the current time\r\n" +
                        "\t!user [new_username]:  Change your username\r\n" +
                        "\t!username [your_username]:  Login/Register\r\n" +
                        "\t!whisper [username]:  Start a private chat\r\n" +
                        "\t!who:  List currently online users";

                    sender.socket.Send(Encoding.ASCII.GetBytes(ClientCommandsList));
                    AddToChat($"\r\n\tSent command list to [{sender.username ?? sender.lobbyUsername}].");
                }

                // AMENDED METHOD
                else if (cmd.StartsWith("!exit"))
                {
                    try
                    {
                        sender.socket.Send(Encoding.ASCII.GetBytes("You have been logged out."));

                        // Broadcast message to all clients except the sender
                        SendToAllExceptSender($"[{sender.username}] has left the chat!", sender);
                        AddToChat($"\r\n\tClient [{sender.username}] disconnected via !exit command");

                        // Reassign a new temporary username
                        sender.ResetSession();

                        string prompt = "To join the chat, please Login/Register.\r\n" +
                                        "\r\n\tType: !username YourNameHere" +
                                        "\r\n\tExample: !username Bob\r\n" +
                                        "\r\nNote: You won't be able to chat until completing this step.\r\n" +
                                        "\r\nTo get a list of available commands, use !commands\r\n";

                        sender.socket.Send(Encoding.ASCII.GetBytes(prompt));

                        // Restart async receive for the new state
                        sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                    }
                    catch (Exception ex)
                    {
                        AddToChat($"[Error handling !exit] {ex.Message}");
                        sender.socket.Send(Encoding.ASCII.GetBytes("[An error occurred while processing your request. Please try again later.]"));
                    }
                    return;
                }

                // END AMENDED METHOD

                // AMENDED METHOD

                else if (cmd == "!global")
                {
                    try
                    {
                        // Check if the user is not in Chatting state
                        if (sender.state != ClientState.Chatting)
                        {
                            sender.socket.Send(Encoding.ASCII.GetBytes("[Please login to use this command]"));
                            sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE,
                                SocketFlags.None, ReceiveCallback, sender);
                            return;
                        }

                        // Check if the sender is already in the global chat
                        if (sender.privateChatTarget == null)
                        {
                            sender.socket.Send(Encoding.ASCII.GetBytes("Uh-oh, you are already in the global chat!"));
                            sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE,
                                SocketFlags.None, ReceiveCallback, sender);
                            return;
                        }

                        ClientSocket partner = sender.privateChatTarget;

                        if (partner != null && partner.privateChatTarget == sender)
                        {
                            // Remove private chat state for both users
                            partner.privateChatTarget = null;
                            sender.privateChatTarget = null;

                            // Inform the partner that the sender has left the private chat
                            if (partner.socket.Connected)
                            {
                                partner.socket.Send(Encoding.ASCII.GetBytes(
                                    $"[{sender.username}] has left the private chat. You are now in global chat."));
                            }

                            // Send a message to the sender confirming the global chat rejoin
                            sender.socket.Send(Encoding.ASCII.GetBytes("You have exited the private chat. You are now in the global chat."));

                            // Prepare rejoin message
                            string rejoinMessage = $"\r\n\t[{sender.username}] & [{partner.username}] have rejoined the global chat.";

                            // Send the rejoin message to the sender (already done above)                            
                            SendToAllExceptSender(rejoinMessage, sender); // Only broadcast to all except the sender
                            

                            // Reset the sender's privateChatTarget to null in case it was not properly reset before
                            sender.privateChatTarget = null;
                            
                        }

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
                        // Resume listening for the sender
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

                else if (cmd == "!join")
                {
                    try
                    {
                        // Ensure user is logged in and in the proper state
                        if (sender.state != ClientState.Chatting)
                        {
                            sender.socket.Send(Encoding.ASCII.GetBytes("[Please login before joining a game]"));
                            sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                            return;
                        }

                        // Already playing?
                        if (sender.state == ClientState.Playing)
                        {
                            sender.socket.Send(Encoding.ASCII.GetBytes("[You're already in a game]"));
                            sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                            return;
                        }

                        // Add sender to available slot
                        if (currentPlayers[0] == null)
                        {
                            currentPlayers[0] = sender;
                            sender.socket.Send(Encoding.ASCII.GetBytes("You joined as Player 1 (X)"));
                        }
                        else if (currentPlayers[1] == null)
                        {
                            currentPlayers[1] = sender;
                            sender.socket.Send(Encoding.ASCII.GetBytes("You joined as Player 2 (O)"));
                        }
                        else
                        {
                            sender.socket.Send(Encoding.ASCII.GetBytes("[Two players already joined. Please wait for the next game]"));
                            sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                            return;
                        }

                        sender.state = ClientState.Playing;

                        // Debug
                        Console.WriteLine($"[JOIN] {sender.username} joined as {(currentPlayers[0] == sender ? "P1" : "P2")}");

                        // Start game if both players present
                        if (currentPlayers[0] != null && currentPlayers[1] != null)
                        {
                            currentGame = new TicTacToe();
                            GameLogicHelper.InitializeGame(currentGame, currentPlayers[0], currentPlayers[1]);
                            Console.WriteLine("[JOIN] Game initialized and both players notified.");
                        }

                        sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR - !join]: {ex.Message}");
                        sender.socket.Send(Encoding.ASCII.GetBytes("[Error joining game]"));
                        sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                    }

                    return;
                }


                else if (cmd == "!username")
{
                    try
                    {
                        Console.WriteLine($"[DEBUG][!username] Command received. Full text: '{text}'");
                        Console.WriteLine($"[DEBUG][!username] Current state: {sender.state}, User: {sender.username ?? "null"}, Temp: {sender.tempUsername ?? "null"}, Lobby: {sender.lobbyUsername ?? "null"}");

                        // Validate parameter exists
                        if (text.Trim().Length <= "!username".Length)
                        {
                            Console.WriteLine("[DEBUG][!username] Validation failed - no username provided");
                            sender.socket.Send(Encoding.ASCII.GetBytes("[Please specify a username after !username]"));
                            sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                            return;
                        }

                        // Extract username
                        string[] parts = text.Split(new char[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("[DEBUG][!username] Invalid username format");
                            sender.socket.Send(Encoding.ASCII.GetBytes("[Invalid username format]"));
                            sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                            return;
                        }

                        string requestedUsername = parts[1].Trim();
                        Console.WriteLine($"[DEBUG][!username] Processing username: '{requestedUsername}'");

                        // Block if already authenticated
                        if (sender.state == ClientState.Chatting || sender.state == ClientState.Playing)
                        {
                            Console.WriteLine($"[DEBUG][!username] Rejected - already authenticated (state: {sender.state})");
                            sender.socket.Send(Encoding.ASCII.GetBytes("[You are already signed in. To change username, please reconnect.]"));
                            sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                            return;
                        }

                        // Validate username format and availability
                        Console.WriteLine("[DEBUG][!username] Validating username");
                        if (!securityHelper.IsUsernameValid(requestedUsername, sender, this.clientSockets))
                        {
                            Console.WriteLine("[DEBUG][!username] Validation failed");
                            sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE,
                                SocketFlags.None, ReceiveCallback, sender);
                            return;
                        }

                        // Check database
                        Console.WriteLine("[DEBUG][!username] Checking database");
                        bool userExists;
                        try
                        {
                            userExists = DbUserManager.DbUsernameCheck(requestedUsername);
                            Console.WriteLine($"[DEBUG][!username] Database check result: {userExists}");
                        }
                        catch (Exception dbEx)
                        {
                            Console.WriteLine($"[DEBUG ERROR][!username] Database error: {dbEx.Message}");
                            sender.socket.Send(Encoding.ASCII.GetBytes("[System error during username check]"));
                            throw;
                        }

                        // Update client state
                        sender.ResetSession();
                        sender.tempUsername = requestedUsername;
                        sender.lobbyUsername = requestedUsername;
                        sender.state = DbUserManager.DbUsernameCheck(requestedUsername) ?
                            ClientState.Login : ClientState.Register;
                        AddToChat($"\r\n\tClient state change: [{sender.lobbyUsername}] | {ClientState.Lobby} → {sender.state}");

                        Console.WriteLine($"[DEBUG][!username] Updated state - TempUser: {sender.tempUsername}, Lobby: {sender.lobbyUsername}, State: {sender.state}");

                        // Send appropriate response
                        sender.socket.Send(Encoding.ASCII.GetBytes("__clearChat__"));
                        if (userExists)
                        {
                            sender.socket.Send(Encoding.ASCII.GetBytes(
                                $"Username [{requestedUsername}] found!\r\n" +
                                "\r\nPlease enter your Password to Login (or type 'Cancel' to return):"));
                        }
                        else
                        {
                            sender.socket.Send(Encoding.ASCII.GetBytes(
                                $"Username [{requestedUsername}] is available!\r\n" +
                                "\r\nPlease set a Password to Register it (or type 'Cancel' to return):"));
                        }

                        Console.WriteLine("[DEBUG][!username] Command processed successfully");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[CRITICAL ERROR][!username] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                        try
                        {
                            sender.socket.Send(Encoding.ASCII.GetBytes("[System error during username processing]"));
                            sender.ResetSession(); // Ensure clean state on error
                        }
                        catch { /* Ignore socket errors */ }
                    }
                    finally
                    {
                        sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                    }
                }
                // END AMENDED METHOD             

                // AMENDED METHOD

                else if (cmd.StartsWith("!user"))
                {
                    if (sender.state != ClientState.Chatting)
                    {
                        sender.socket.Send(Encoding.ASCII.GetBytes("[Please login to use this command]"));
                        sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                        return;
                    }

                    string[] parts = text.Split(new char[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    string requestedUsername = parts.Length > 1 ? parts[1].Trim() : "";

                    // Validate using your security helper (already customized to check format and uniqueness)
                    if (!securityHelper.IsUsernameValid(requestedUsername, sender, this.clientSockets))
                    {
                        sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                        return;
                    }

                    string oldUsername = sender.username;
                    sender.username = requestedUsername;
                    dbUserManager.UpdateUsername(oldUsername, requestedUsername);                   

                    sender.socket.Send(Encoding.ASCII.GetBytes("Your username has changed!"));

                    // Reuse methods and send appropriate message types
                    SendToAll($"[{oldUsername}] has changed their username to [{requestedUsername}]", sender);
                    AddToChat($"\r\n\tNew username change: [{oldUsername}] → [{requestedUsername}] | State: [{sender.state}]");
                    sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                }

                // END AMENDED METHOD

                else if (cmd.StartsWith("!about"))
                {
                    string aboutInfo = "\r\nTCP Chat Application + Game: Your gateway to real-time communication! Connect, chat, and manage your identity in a secure, simple chat environment.\r\n" +
                                       "\r\nDeveloped by: Daniel (A00151824)" +
                                       "\r\nFor academic purposes, as part of NDS203 Assessment 2 & 3" +
                                       "\r\nYear: 2025";
                    sender.socket.Send(Encoding.ASCII.GetBytes(aboutInfo));
                    AddToChat($"\r\n\tSent about info to [{sender.username ?? sender.lobbyUsername}].");
                }                

                // AMENDED METHOD
                else if (cmd == "!who")
                {
                    // Block temp users from using this command
                    if (sender.state != ClientState.Chatting)
                    {
                        sender.socket.Send(Encoding.ASCII.GetBytes("[Please login to use this command]"));
                        sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                        return;
                    }

                    List<string> onlineUsers = clientSockets
                        .Where(c => c.state == ClientState.Chatting && c != sender)
                        .Select(c => c.username)
                        .ToList();

                    if (onlineUsers.Count == 0)
                    {
                        sender.socket.Send(Encoding.ASCII.GetBytes("\r\n\tOnline Users:\r\n\tJust you, for now! :)"));
                    }
                    else
                    {
                        // Include the sender at the top of the list
                        onlineUsers.Insert(0, sender.username + " (you)"); // way to differentiate the sender
                        sender.socket.Send(Encoding.ASCII.GetBytes("\r\n\tOnline Users:\r\n" + string.Join("\r\n", onlineUsers)));
                    }

                    AddToChat($"\r\n\tSent list of online users to [{sender.username ?? sender.lobbyUsername}].");
                }
                // END AMENDED METHOD

                else if (cmd.StartsWith("!whisper"))
                {
                    if (sender.state != ClientState.Chatting)
                    {
                        sender.socket.Send(Encoding.ASCII.GetBytes("[Please login to use this command]"));
                        // VERY IMPORTANT: Resume listening for more input
                        sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE,
                            SocketFlags.None, ReceiveCallback, sender);

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

                    AddToChat($"\r\n\t [{sender.username}] started a private chat with [{target.username}]");
                }


                else if (cmd == "!time")
                {
                    string time = DateTime.Now.ToString("hh:mm:ss tt");
                    sender.socket.Send(Encoding.ASCII.GetBytes($"The current time is: {time}"));
                    AddToChat($"\r\n\tSent current time to [{sender.username ?? sender.lobbyUsername}].");
                }

                else if (cmd.StartsWith("!kick"))
                {
                    if (!sender.isModerator)
                    {
                        sender.socket.Send(Encoding.ASCII.GetBytes("[You do not have permission to use this command]"));
                        AddToChat($"\r\n\tUnauthorized Kick Attempt: [{sender.username}] tried to use !kick");
                    }
                    else
                    {
                        HandleServerCommand(text, sender);
                    }

                    sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                    return;
                }

                else
                {
                    byte[] data = Encoding.ASCII.GetBytes("Unknown command. Type !commands for a list of available commands.");
                    sender.socket.Send(data);

                    if (sender.state == ClientState.Chatting || sender.state == ClientState.Playing)
                    {
                        AddToChat($"\r\n\tBlocked unknown command → [{cmd}] from [{sender.username}] ");
                    }
                    else
                    {
                        AddToChat($"\r\n\tBlocked unknown command → [{cmd}] from lobby user [{sender.lobbyUsername}] ");
                    }

                    sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
                }
            }
            catch (Exception ex)
            {
                AddToChat($"[Error handling command '{cmd}'] {ex.Message}");
                sender.socket.Send(Encoding.ASCII.GetBytes("[An error occurred while processing your request. Please try again later.]"));
            }

            sender.socket.BeginReceive(sender.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, sender);
        }


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
                        AddToChat("\r\n\tModerators List:\r\nNo current moderators.");
                    }
                    else
                    {
                        AddToChat("\r\n\tModerators List:\r\n\t" + string.Join("\r\n", modList));
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

                        ClientSocket targetClient = null;
                        foreach (var client in clientSockets)
                        {
                            if (!string.IsNullOrWhiteSpace(client.username) &&
                                client.username.Equals(targetUsername, StringComparison.OrdinalIgnoreCase))
                            {
                                targetClient = client;
                                break;
                            }
                        }

                        if (targetClient != null)
                        {
                            string kickerName = from?.username ?? "SERVER";

                            // Send kick message and notify all
                            targetClient.socket.Send(Encoding.ASCII.GetBytes("You have been kicked out!"));
                            AddToChat($"\r\n\t[{kickerName}] kicked [{targetUsername}] from the chat.");
                            SendToAll($"[{targetUsername}] has been removed by moderator [{kickerName}]", from);

                            // Reset session (will regenerate lobby username if applicable)
                            targetClient.ResetSession();

                            // Send rejoin prompt
                            string prompt = "To join the chat, please choose a username.\r\n" +
                                            "\r\n\tType: !username YourNameHere" +
                                            "\r\n\tExample: !username Bob\r\n" +
                                            "\r\nNote: You won't be able to chat until a valid username is set.\r\n" +
                                            "\r\nUse !commands to see available commands.\r\n";
                            targetClient.socket.Send(Encoding.ASCII.GetBytes(prompt));

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
                    AddToChat($"\r\n\t[Unknown Server Command]: {cmd}. Try !mod, !kick, or !mods.");
                }
            }
            catch (Exception ex)
            {
                AddToChat($"\r\n\t[Server Command Error]: {ex.Message}");
            }
        }

        public void ProcessServerCommand(string command, ClientSocket from = null)
        {
            HandleServerCommand(command, from);
        }


    }
    // END AMENDED CLASS
}