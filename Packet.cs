using System.Collections.Generic;
using Godot;
using Newtonsoft.Json;

namespace horrorgameserverrelay;

public partial class BasicPacket : RefCounted
{
    [JsonProperty("message")] public Message Message { get; set; }
    [JsonProperty("id")] public int Id { get; set; }
}

public partial class BasicResponsePacket : BasicPacket
{
    [JsonProperty("success")] public bool Success { get; set; } = true;
    [JsonProperty("success_message")] public string SuccessMessage { get; set; } = "";
    [JsonProperty("error")] public string Error { get; set; } = "";
}

public partial class UserInfoPacket : BasicPacket
{
    [JsonProperty("username")] public string Username { get; set; }

    public UserInfoPacket()
    {
        Message = Message.UserInfo;
    }
}

public partial class LobbyDescriptionPacket : BasicPacket
{
    [JsonProperty("lobby_description")] public string LobbyDescription { get; set; }

    public LobbyDescriptionPacket()
    {
        Message = Message.LobbyDescription;
    }
}

public partial class CreateLobbyResponsePacket : BasicResponsePacket
{
    [JsonProperty("lobby_code")] public string LobbyCode { get; set; }

    public CreateLobbyResponsePacket()
    {
        Message = Message.CreateLobby;
    }
}

public partial class LobbyListResponsePacket : BasicResponsePacket
{
    [JsonProperty("lobby_list")] public List<Lobby> LobbyList { get; set; }

    public LobbyListResponsePacket()
    {
        Message = Message.LobbyList;
    }
}

public partial class JoinLobbyPacket : BasicPacket
{
    [JsonProperty("lobby_code")] public string LobbyCode { get; set; }

    public JoinLobbyPacket()
    {
        Message = Message.JoinLobby;
    }
}

public partial class JoinLobbyResponsePacket : BasicResponsePacket
{
    [JsonProperty("type")] public string Type { get; set; }

    public JoinLobbyResponsePacket()
    {
        Message = Message.JoinLobby;
    }
}

// response packet for the player joining the lobby
public partial class JoinLobbyJoiningUserResponsePacket : JoinLobbyResponsePacket
{
    [JsonProperty("lobby_peers")] public List<Peer> LobbyPeers { get; set; }
    public JoinLobbyJoiningUserResponsePacket()
    {
        Message = Message.JoinLobby;
        Type = "JoiningUser";
    }
}

// response packet for the players already in the lobby
public partial class JoinLobbyExistingUsersResponsePacket : JoinLobbyResponsePacket 
{
    [JsonProperty("joining_peer")] public Peer JoiningPeer { get; set; }
    public JoinLobbyExistingUsersResponsePacket()
    {
        Message = Message.JoinLobby;
        Type = "ExistingUser";
    }
}

public partial class LeaveLobbyPacket : BasicPacket
{
    [JsonProperty("lobby_code")] public string LobbyCode { get; set; }

    public LeaveLobbyPacket()
    {
        Message = Message.LeaveLobby;
    }
}

public partial class LeaveLobbyResponsePacket : BasicResponsePacket
{
    [JsonProperty("leavingPeer")] public Peer LeavingPeer { get; set; }

    public LeaveLobbyResponsePacket()
    {
        Message = Message.LeaveLobby;
    }
}

public partial class LobbyMessagePacket : BasicPacket
{
    [JsonProperty("lobby_message")] public string LobbyMessage { get; set; }
    [JsonProperty("peer_sender")] public Peer PeerSender { get; set; }

    public LobbyMessagePacket()
    {
        Message = Message.LobbyMessage;
    }
}

public partial class RtcOfferPacket : BasicPacket
{
    [JsonProperty("offer_type")] public string OfferType { get; set; }
    [JsonProperty("offer_sdp")] public string OfferSdp { get; set; }
    [JsonProperty("offer_id")] public int OfferId { get; set; }

    public RtcOfferPacket()
    {
        Message = Message.Offer;
    }
}

public partial class RtcAnswerPacket : BasicPacket
{
    [JsonProperty("answer_type")] public string AnswerType { get; set; }
    [JsonProperty("answer_sdp")] public string AnswerSdp { get; set; }
    [JsonProperty("answer_id")] public int AnswerId { get; set; }

    public RtcAnswerPacket()
    {
        Message = Message.Answer;
    }
}

public partial class RtcIcePacket : BasicPacket
{
    [JsonProperty("media")] public string Media { get; set; }
    [JsonProperty("index")] public int Index { get; set; }
    [JsonProperty("name")] public int Name { get; set; }
    [JsonProperty("ice_id")] public int IceId { get; set; }

    public RtcIcePacket()
    {
        Message = Message.InteractiveConnectivityEstablishment;
    }
}

public partial class HostResponsePacket : BasicResponsePacket
{
    [JsonProperty("host_peer")] public Peer HostPeer { get; set; }

    public HostResponsePacket()
    {
        Message = Message.Host;
    }
}

public partial class StartSessionResponsePacket : BasicResponsePacket
{
    [JsonProperty("start_peers")] public List<Peer> StartPeers { get; set; }

    public StartSessionResponsePacket()
    {
        Message = Message.StartSession;
    }
}