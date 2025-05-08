using System.Net.Sockets;
using Windows_Forms_Chat;

public enum ClientState
{
    Lobby,    // Client is in the lobby
    Login,    // Client is logging in
    Register, // Client is registering
    Chatting, // Client is chatting
    Playing   // Client is playing the game
}

public class ClientSocket
{
    public TCPChatServer server { get; set; }  // Reference to the server instance
    public Socket socket;
    public const int BUFFER_SIZE = 2048;
    public byte[] buffer = new byte[BUFFER_SIZE];

    // Changed from null to string.Empty for safer string operations

    public bool isNewConnection = true;
    public string username = string.Empty;
    public string lobbyUsername = string.Empty;
    public string tempUsername = string.Empty;

    public ClientSocket privateChatTarget = null;
    public TicTacToe currentGame = null;
    public bool myTurn = false; // Start as false, server will enable
    public bool isModerator = false;
    public ClientState state = ClientState.Lobby;
    public int loginAttempts = 0;

    // NEW: Override ToString() for better debugging
    public override string ToString()
    {
        return $"Socket: {socket?.Handle.ToString() ?? "null"}, " +
               $"User: {username ?? "(null)"}, " +
               $"State: {state}, " +
               $"Temp: {tempUsername ?? "(null)"}, " +
               $"Lobby: {lobbyUsername ?? "(null)"}";
    }

    // NEW: Method to safely reset user session
    public void ResetSession()
    {
        username = string.Empty;
        tempUsername = string.Empty;
        state = ClientState.Lobby;
        loginAttempts = 0;
        isModerator = false;
        privateChatTarget = null;

        // Only generate new lobby username if this is a brand new connection
        if (isNewConnection)
        {
            lobbyUsername = DbUserManager.GenerateLobbyUsername();
            isNewConnection = false; // Mark as established connection
        }
    }
}