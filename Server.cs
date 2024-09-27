using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Newtonsoft.Json;

namespace HorrorGameServerRelay;

public partial class Server : Node
{
    private TcpServer _server;
    private ushort _port;
    private readonly Dictionary<int, Peer> _peers;
    private readonly List<Lobby> _lobbies;

    private readonly Stack<Lobby> _removeLobbyStack;
    private readonly Stack<Peer> _removePeerStack;
    
    public Server()
    {
        _server = new TcpServer();
        _port = 4556;
        _peers = new Dictionary<int, Peer>();
        _lobbies = new List<Lobby>();

        _removeLobbyStack = new Stack<Lobby>();
        _removePeerStack = new Stack<Peer>();
        
        var error = _server.Listen(_port);
        
        GD.Print(error is Error.Ok ? "[Log] Server Created!" : $"[Error] Cannot create server! ErrCode: {error}\n");
    }

    public override void _Process(double delta)
    {
        Poll();
        Clean();
    }

    private void Clean()
    {
        while (_removePeerStack.TryPop(out var peer))
        {
            _peers.Remove(peer.Id);
        }

        while (_removeLobbyStack.TryPop(out var lobby))
        {
            _lobbies.Remove(lobby);
        }

        var disconnectedPeers = (
                from Lobby lobby in _lobbies
                from Peer lobbyPeer in lobby.Peers
                where !_peers.ContainsValue(lobbyPeer)
                select lobbyPeer
            ).ToList();

        foreach (var disconnectedPeer in disconnectedPeers)
        {
            var foundLobby = FindLobbyByPeer(disconnectedPeer)
                .Select(searchedLobby =>
                {
                    if (searchedLobby.Peers.Count <= 2)
                    {
                        disconnectedPeer.IsHost = false;
                        searchedLobby.Peers.First().IsHost = true;
                    }

                    foreach (var peer in searchedLobby.Peers.Where(peer => disconnectedPeer != peer))
                    {
                        peer.SendMessage(Message.LeaveLobby, disconnectedPeer.Id, disconnectedPeer.UserName);
                    }

                    searchedLobby.Peers.Remove(disconnectedPeer);
                    if (searchedLobby.Peers.Count == 0)
                    {
                        _removeLobbyStack.Push(searchedLobby);
                    }

                    return true;
                })
                .DefaultIfEmpty(false)
                .Single();
            
            if (!foundLobby)
            {
                GD.Print($"[Error] Could not find lobby to disconnect peer {disconnectedPeer.Id} from!");
            }
        }
    }

    private void Poll()
    {
        if (_server.IsConnectionAvailable())
        {
            // calculate ID, but use mod to keep it within 32 bit range
            var id = Random.Shared.NextInt64() % int.MaxValue;
            _peers.Add((int)id, new Peer((int)id, _server.TakeConnection()));
        }

        foreach (var peer in _peers.Values)
        {
            peer.WebSocketPeer.Poll();

            while (peer.IsWebsocketOpen() && peer.WebSocketPeer.GetAvailablePacketCount() > 0)
            {
                if (!ParseMessage(peer))
                {
                    GD.Print("[Error] Message received but could not parse!");
                }
            }

            if (peer.WebSocketPeer.GetReadyState() is not WebSocketPeer.State.Closed) continue;
            
            GD.Print($"[Log] Peer {peer.Id} has disconnected!\n");
            _removePeerStack.Push(peer);
        }
    }

    private bool ParseMessage(Peer fromPeer)
    {
        var packetBuffer = fromPeer.WebSocketPeer.GetPacket().GetStringFromUtf8();

        if (packetBuffer == "Test msg!")
        {
            return true;
        }
        
        GD.Print(packetBuffer);

        var packet = JsonConvert.DeserializeObject<Packet>(packetBuffer);
        
        if (packet is null)
        {
            return false;
        }

        if (packet.type == Message.UserInfo)
        {
            fromPeer.SendMessage(Message.UserInfo, fromPeer.Id, packet.data);
            fromPeer.UserName = packet.data;
            GD.Print($"[Log] Received Username: {fromPeer.UserName}");
        }
        else if (packet.type == Message.LobbyList)
        {
            var spaceSeperatedLobbyNames = string.Join(" ", _lobbies.Select(e => e.Name).ToArray());
            fromPeer.SendMessage(Message.LobbyList, 0, spaceSeperatedLobbyNames);
            GD.Print($"[Log] Lobby List sent!");
        }
        else if (packet.type == Message.CreateLobby)
        {
            foreach (var lobby in from Lobby lobby in _lobbies where lobby.Name != packet.data select lobby)
            {
                GD.Print($"[Error] Trying to create new lobby with name {lobby.Name} but it already exists!");
                fromPeer.SendMessage(Message.CreateLobby, 0, "INVALID");
                return true;
            }

            // create new lobby
            var newLobby = new Lobby(packet.data);
            _lobbies.Add(newLobby);

            // add the peer who created the lobby to the lobby
            fromPeer.IsHost = true;
            newLobby.Peers.Add(fromPeer);

            fromPeer.WebSocketPeer.SendText(Json.Stringify($"FEEDBACK: New lobby name: {packet.data}"));
            fromPeer.SendMessage(Message.CreateLobby, 0, packet.data);
            GD.Print($"[Log] Created lobby successfully! Name: {packet.data}");
        }
        else if (packet.type == Message.JoinLobby)
        {
            fromPeer.WebSocketPeer.SendText(Json.Stringify($"FEEDBACK: Join lobby name: {packet.data}"));
            bool foundLobby = FindLobbyByName(packet.data)
                .Select(lobby =>
                {
                    fromPeer.SendMessage(Message.Host, lobby.Peers.First().Id, lobby.Peers.First().UserName);
                    fromPeer.SendMessage(Message.JoinLobby, 0, $"LOBBY_NAME{lobby.Name}");

                    foreach (var lobbyPeer in lobby.Peers)
                    {
                        lobbyPeer.SendMessage(Message.JoinLobby, fromPeer.Id, $"NEW_JOINED_USER_NAME{fromPeer.UserName}");
                        fromPeer.SendMessage(
                            Message.JoinLobby,
                            lobbyPeer.Id,
                            $"EXISTING_USER_NAME{lobbyPeer.UserName}");
                    }

                    lobby.Peers.Add(fromPeer);
                    GD.Print($"Lobby {packet.data} Requested to be joined!");

                    return true;
                })
                .DefaultIfEmpty(false)
                .Single();

            if (foundLobby) return true;
            
            GD.Print($"Lobby ${packet.data} Requested to be joined!\n[Error] No such lobby!");
            fromPeer.SendMessage(Message.JoinLobby, 0, "INVALID");
        }
        else if (packet.type == Message.LeaveLobby)
        {
            var foundLobby = FindLobbyByName(packet.data)
                .Select(lobby =>
                {
                    switch (lobby.Peers.Count)
                    {
                        case 1:
                            _removeLobbyStack.Push(lobby);
                            fromPeer.IsHost = false;
                            break;
                        case > 1:
                        {
                            // copy lobby.Peers to a list so that it's not being mutated whilst iterating through
                            foreach (var lobbyPeer in lobby.Peers.ToList())
                            {
                                lobbyPeer.IsHost = false;
                                if (lobbyPeer.UserName != fromPeer.UserName)
                                {
                                    lobbyPeer.SendMessage(Message.LeaveLobby, fromPeer.Id, fromPeer.UserName);
                                }
                                else
                                {
                                    lobby.Peers.Remove(lobbyPeer);
                                }
                            }

                            lobby.Peers.First().IsHost = true;

                            foreach (var lobbyPeer in lobby.Peers)
                            {
                                lobbyPeer.SendMessage(
                                    Message.Host,
                                    lobby.Peers.First().Id,
                                    lobby.Peers.First().UserName);
                            }

                            break;
                        }
                    }

                    return true;
                })
                .DefaultIfEmpty(false)
                .Single();

            if (!foundLobby)
            {
                GD.Print("[Error] Could not find lobby to disconnect peer from!");
            }
        }
        else if (packet.type == Message.LobbyMessage)
        {
            var relayMessagePeers = _lobbies
                .Select(lobby => lobby.Peers)
                .Single(p => p.Contains(fromPeer));

            foreach (var p in relayMessagePeers)
            {
                p.SendMessage(Message.LobbyMessage, 0, packet.data);
            }
        }
        else if (packet.type == Message.StartSession)
        {
            var foundLobby = FindLobbyByPeer(fromPeer)
                .Select(lobby =>
                {
                    var peerIDs = string.Join("***", lobby.Peers.Select(p => p.Id));

                    foreach (var lobbyPeer in lobby.Peers)
                    {
                        lobbyPeer.SendMessage(Message.StartSession, 0, peerIDs);
                    }
                    
                    GD.Print($"[Log] Starting session from lobby: {lobby.Name}");
                    return true;
                })
                .Single();

            if (!foundLobby)
            {
                GD.Print("[Error] Could not find lobby to start session from!");
            }
        }
        else if (packet.type == Message.Offer)
        {
            var splitData = packet.data.Split("***", 3);
            var sendId = splitData[2].ToInt();
            var foundPeer = FindPeerById(sendId)
                .Select(foundPeer =>
                {
                    foundPeer.SendMessage(packet.type, fromPeer.Id, packet.data);
                    GD.Print($"[Log] Relaying Offer to peer {fromPeer.Id}");
                    return true;
                })
                .DefaultIfEmpty(false)
                .Single();

            if (foundPeer) return true;
            
            GD.Print("[Error] Offer received but could not find matching peer!");
            return false;
        }
        else if (packet.type == Message.Answer)
        {
            var splitData = packet.data.Split("***", 3);
            var sendId = splitData[2].ToInt();
            var foundPeer = FindPeerById(sendId)
                .Select(foundPeer =>
                {
                    foundPeer.SendMessage(packet.type, fromPeer.Id, packet.data);
                    GD.Print($"[Log] Relaying Answer to peer {fromPeer.Id}");
                    return true;
                })
                .DefaultIfEmpty(false)
                .Single();

            if (foundPeer) return true;
            
            GD.Print("[Error] Answer received but could not find matching peer!");
            return false;
        }
        else if (packet.type == Message.InteractiveConnectivityEstablishment)
        {
            var splitData = packet.data.Split("***", 4);
            var sendId = splitData[3].ToInt();
            var foundPeer = FindPeerById(sendId)
                .Select(foundPeer =>
                {
                    foundPeer.SendMessage(packet.type, fromPeer.Id, packet.data);
                    GD.Print($"[Log] Relaying ICE to peer {fromPeer.Id}");
                    return true;
                })
                .DefaultIfEmpty(false)
                .Single();

            if (foundPeer) return true;
            
            GD.Print("[Error] ICE received but could not find matching peer!");
            return false;
        }
        else
        {
            return false;
        }

        return true;
    }

    private Option<Peer> FindPeerById(int id)
    {
        foreach (var peerId in _peers.Keys)
        {
            if (id == peerId && _peers.TryGetValue(id, out var peer))
            {
                return Option<Peer>.Create(peer);
            }
        }

        return Option<Peer>.CreateEmpty();
    }

    private Option<Lobby> FindLobbyByPeer(Peer peer)
    {
        foreach (var lobby in from Lobby lobby in _lobbies from Peer lobbyPeer in lobby.Peers where lobbyPeer.Equals(peer) select lobby)
        {
            return Option<Lobby>.Create(lobby);
        }

        return Option<Lobby>.CreateEmpty();
    }

    private Option<Lobby> FindLobbyByName(string lobbyName)
    {
        foreach (var lobby in from Lobby lobby in _lobbies where lobby.Name == lobbyName select lobby)
        {
            return Option<Lobby>.Create(lobby);
        }

        return Option<Lobby>.CreateEmpty();
    }
}