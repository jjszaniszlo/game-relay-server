using Godot;
using Newtonsoft.Json;

namespace horrorgameserverrelay;

public partial class Peer : RefCounted
{
    public int Id { get; private set; }
    public WebSocketPeer WebSocketPeer { get; private set; }
    public bool IsHost { get; set; }
    public string UserName { get; set; }

    public Peer(int id, StreamPeer tcp)
    {
        WebSocketPeer = new WebSocketPeer();
        
        this.Id = id;
        var error = WebSocketPeer.AcceptStream(tcp);
        GD.Print(error is Error.Ok ? "Peer connection accepted!" : "[ERROR] Cannot accept connection!");
    }
    
    public Error SendMessage(Message message, int id, string data)
    {
        var packet = new Packet
        {
            type = message,
            id = id,
            data = data
        };
        return WebSocketPeer.SendText(JsonConvert.SerializeObject(packet));
    }

    public bool IsWebsocketOpen()
    {
        return WebSocketPeer.GetReadyState() == WebSocketPeer.State.Open;
    }
}