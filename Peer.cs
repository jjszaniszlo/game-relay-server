using Godot;
using Newtonsoft.Json;

namespace horrorgameserverrelay;

public partial class Peer : RefCounted
{
    [JsonIgnore] public WebSocketPeer WebSocketPeer { get; private set; }
    [JsonIgnore] public bool IsHost { get; set; }
    [JsonProperty("id")] public int Id { get; private set; }
    [JsonProperty("username")] public string Username { get; set; }

    public Peer(int id, StreamPeer tcp)
    {
        WebSocketPeer = new WebSocketPeer();
        
        Id = id;
        var error = WebSocketPeer.AcceptStream(tcp);
        GD.Print(error is Error.Ok ? "[Log] Peer connection accepted!" : "[ERROR] Cannot accept connection!");
    }
    
    public Error SendPacket(BasicPacket packet)
    {
        return WebSocketPeer.SendText(JsonConvert.SerializeObject(packet));
    }

    public bool IsWebsocketOpen()
    {
        return WebSocketPeer.GetReadyState() == WebSocketPeer.State.Open;
    }
}