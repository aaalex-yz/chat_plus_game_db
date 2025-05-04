using System;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;

namespace Windows_Forms_Chat
{
    // TCPChatClient handles all client-side networking operations, including:
    // establishing a connection to the server, sending/receiving messages,
    // and managing user interaction through the chat interface.
    public class TCPChatClient : TCPChatBase
    {
        public Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        public ClientSocket clientSocket = new ClientSocket();
        public int serverPort;
        public string serverIP;

        // -----------------------------
        // STEP 1: Instantiate Client
        // -----------------------------

        // Factory method to create a fully configured TCPChatClient instance.
        public static TCPChatClient CreateInstance(int port, int serverPort, string serverIP, TextBox chatTextBox)
        {
            TCPChatClient tcp = null;
            if (port > 0 && port < 65535 &&
                serverPort > 0 && serverPort < 65535 &&
                serverIP.Length > 0 &&
                chatTextBox != null)
            {
                tcp = new TCPChatClient();
                tcp.port = port;
                tcp.serverPort = serverPort;
                tcp.serverIP = serverIP;
                tcp.chatTextBox = chatTextBox;
                tcp.clientSocket.socket = tcp.socket; // Link internal ClientSocket to main socket
            }
            return tcp;
        }

        // -----------------------------
        // STEP 2: Connect to Server
        // -----------------------------

        // Attempts to connect to the remote server using provided IP and port.
        public void ConnectToServer()
        {
            int attempts = 0;

            while (!socket.Connected)
            {
                try
                {
                    attempts++;
                    SetChat("Connection attempt: " + attempts);
                    socket.Connect(serverIP, serverPort); // Tries to establish connection
                }
                catch (SocketException)
                {
                    chatTextBox.Text = ""; // Clears UI if a connection error occurs
                }
            }

            AddToChat("Connected!"); // Notify user of successful connection

            // Prompt client to enter a username upon joining the server
            ShowUsernamePrompt();

            // Begin listening for server messages asynchronously
            clientSocket.socket.BeginReceive(
                clientSocket.buffer, 0, ClientSocket.BUFFER_SIZE,
                SocketFlags.None, ReceiveCallback, clientSocket
            );
        }

        // -----------------------------
        // STEP 3: Prompt for Username
        // -----------------------------

        // Informs the user to set a username using the !username command.
        public void ShowUsernamePrompt()
        {
            AddToChat("To join the chat, please choose a username.");
            AddToChat("\r\n\tType: !username + YourNameHere\r\n\tExample: !username Bob");
            AddToChat("\r\nNote: You won't be able to chat until a valid username is set.");
            AddToChat("\r\nTo get a list of available commands, use !commands\r\n");
        }

        // -----------------------------
        // STEP 4: Send Data to Server
        // -----------------------------

        // Converts a message into bytes and transmits it to the server via the socket.
        public void SendString(string text)
        {
            byte[] buffer = Encoding.ASCII.GetBytes(text);
            socket.Send(buffer, 0, buffer.Length, SocketFlags.None);
        }

        // -----------------------------
        // STEP 5: Handle Incoming Data
        // -----------------------------

        // Asynchronous callback that processes data received from the server.
        public void ReceiveCallback(IAsyncResult AR)
        {
            ClientSocket currentClientSocket = (ClientSocket)AR.AsyncState;
            int received;

            try
            {
                received = currentClientSocket.socket.EndReceive(AR); // Finalizes receive
            }
            catch (SocketException)
            {
                AddToChat("Client forcefully disconnected");
                currentClientSocket.socket.Close(); // Closes client socket on error
                return;
            }

            byte[] recBuf = new byte[received];
            Array.Copy(currentClientSocket.buffer, recBuf, received);

            string incomingMessage = Encoding.ASCII.GetString(recBuf); // Decodes received bytes

            AddToChat(incomingMessage); // Displays incoming message in chat window

            // Continues listening for further messages from server
            currentClientSocket.socket.BeginReceive(
                currentClientSocket.buffer, 0, ClientSocket.BUFFER_SIZE,
                SocketFlags.None, ReceiveCallback, currentClientSocket
            );
        }

        // -----------------------------
        // STEP 6: Close Client Socket
        // -----------------------------

        // Closes the client socket and terminates connection to the server.
        public void Close()
        {
            socket.Close();
        }
    }
}
