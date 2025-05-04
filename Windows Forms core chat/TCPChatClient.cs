using System;
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
                tcp.clientSocket.socket = tcp.socket;       
            }
            return tcp;
        }

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
            }

            AddToChat("Connected!");      

            ShowUsernamePrompt();

            clientSocket.socket.BeginReceive(
                clientSocket.buffer, 0, ClientSocket.BUFFER_SIZE,
                SocketFlags.None, ReceiveCallback, clientSocket
            );
        }

        public void ShowUsernamePrompt()
        {
            AddToChat("To join the chat, please choose a username.");
            AddToChat("\r\n\tType: !username + YourNameHere\r\n\tExample: !username Bob");
            AddToChat("\r\nNote: You won't be able to chat until a valid username is set.");
            AddToChat("\r\nTo get a list of available commands, use !commands\r\n");
        }

        public void SendString(string text)
        {
            byte[] buffer = Encoding.ASCII.GetBytes(text);
            socket.Send(buffer, 0, buffer.Length, SocketFlags.None);
        }

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

            AddToChat(incomingMessage);       

            currentClientSocket.socket.BeginReceive(
                currentClientSocket.buffer, 0, ClientSocket.BUFFER_SIZE,
                SocketFlags.None, ReceiveCallback, currentClientSocket
            );
        }

        public void Close()
        {
            socket.Close();
        }
    }
}
