using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using Newtonsoft.Json;

namespace horrorgameserverrelay;

public partial class Lobby : RefCounted
{
    [JsonIgnore] public static readonly HashSet<string> TakenLobbyNames = new();
    [JsonIgnore] public List<Peer> Peers { get; private set; }
    [JsonProperty("lobby_code")] public string LobbyCode { get; private set; }
    [JsonProperty("lobby_description")] private string LobbyDescription { get; set; } = "";

    public Lobby(string lobbyCode)
    {
        Peers = new List<Peer>();
        LobbyCode = lobbyCode;
    }

    public static string GenerateRandomLobbyCode()
    {
        StringBuilder sb = new();
        
        var letters = new[]
        {
            'A', 'B', 'C', 'D', 'E', 'F', 'G',
            'H', 'I', 'J', 'K', 'L', 'M', 'N',
            'O', 'P', 'Q', 'R', 'S', 'T', 'U',
            'V', 'W', 'X', 'Y', 'Z'
        };

        for (int i = 0; i < 5; i++)
        {
            var randLetter = letters[Random.Shared.Next() % 26];
            sb.Append(randLetter);
        }

        return sb.ToString();
    }
}