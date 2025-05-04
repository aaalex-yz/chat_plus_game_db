using System.Net.Sockets;

public class ClientSocket
{

    public Socket socket;

    public const int BUFFER_SIZE = 2048;

    public byte[] buffer = new byte[BUFFER_SIZE];

    public string username = null;

    public ClientSocket privateChatTarget = null;

    public bool isModerator = false;

}
