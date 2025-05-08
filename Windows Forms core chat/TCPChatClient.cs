using System;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;

namespace Windows_Forms_Chat
{
    public class TCPChatClient : TCPChatBase
    {
        public Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        public ClientSocket clientSocket = new ClientSocket();
        public int serverPort;
        public string serverIP;
        private Form1 form;

        public static TCPChatClient CreateInstance(int port, int serverPort, string serverIP, TextBox chatTextBox, Form1 formRef)
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
                tcp.clientSocket.socket = tcp.socket;
                tcp.form = formRef; // 🔗 Set the Form1 reference
            }
            return tcp;
        }
        // AMENDED METHOD
        public void ConnectToServer()
        {
            int attempts = 0;

            while (!socket.Connected)
            {
                try
                {
                    attempts++;
                    SetChat("Connection attempt: " + attempts);
                    socket.Connect(serverIP, serverPort);     
                }
                catch (SocketException)
                {
                    chatTextBox.Text = "";        
                }
                // Moved the welcome message to ReceiveCallback() method
            }
            clientSocket.socket.BeginReceive(
                clientSocket.buffer, 0, ClientSocket.BUFFER_SIZE,
                SocketFlags.None, ReceiveCallback, clientSocket
            );
        }
        // END AMENDED METHOD

        public void ShowUsernamePrompt()
        {
            AddToChat("To join the chat, please Login/Register.");
            AddToChat("\r\n\tType: !username + YourNameHere\r\n\tExample: !username Bob");
            AddToChat("\r\nNote: You won't be able to chat until completing this step.");
            AddToChat("\r\nTo get a list of available commands, use !commands\n");
        }

        public void SendString(string text)
        {
            byte[] buffer = Encoding.ASCII.GetBytes(text);
            socket.Send(buffer, 0, buffer.Length, SocketFlags.None);
        }

        // AMENDED METHOD
        public void ReceiveCallback(IAsyncResult AR)
        {
            ClientSocket currentClientSocket = (ClientSocket)AR.AsyncState;
            int received;

            try
            {
                received = currentClientSocket.socket.EndReceive(AR);
            }
            catch (SocketException)
            {
                AddToChat("Client forcefully disconnected");
                currentClientSocket.socket.Close();
                return;
            }

            byte[] recBuf = new byte[received];
            Array.Copy(currentClientSocket.buffer, recBuf, received);

            string incomingMessage = Encoding.ASCII.GetString(recBuf);

            // Handle __clearChat__
            if (incomingMessage == "__clearChat__")
            {
                chatTextBox.Invoke((Action)(() => chatTextBox.Clear()));

                // ✅ Keep listening
                currentClientSocket.socket.BeginReceive(
                    currentClientSocket.buffer, 0, ClientSocket.BUFFER_SIZE,
                    SocketFlags.None, ReceiveCallback, currentClientSocket
                );
                return;
            }

            // Handle temporary username assignment
            if (incomingMessage.StartsWith("__lobbyUsername__:"))
            {
                currentClientSocket.lobbyUsername = incomingMessage.Replace("__lobbyUsername__:", "").Trim();
                AddToChat($"Connected! Your Lobby username is: [{currentClientSocket.lobbyUsername}].");

                // Show username prompt for login/register flow
                ShowUsernamePrompt();

                // Continue listening for input
                currentClientSocket.socket.BeginReceive(
                    currentClientSocket.buffer, 0, ClientSocket.BUFFER_SIZE,
                    SocketFlags.None, ReceiveCallback, currentClientSocket
                );
                return;
            }

            bool isGameMessage = form.ProcessServerMessage(incomingMessage);
            if (!isGameMessage)
            {
                AddToChat(incomingMessage); // Display only non-game messages
            }
            // ✅ THIS WAS MISSING
            currentClientSocket.socket.BeginReceive(
                currentClientSocket.buffer, 0, ClientSocket.BUFFER_SIZE,
                SocketFlags.None, ReceiveCallback, currentClientSocket
            );

        }

        // END AMENDED METHOD

        public void Close()
        {
            socket.Close();
        }
    }
}
