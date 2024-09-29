using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Newtonsoft.Json;

namespace horrorgameserverrelay;

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
                        peer.SendPacket(new LeaveLobbyResponsePacket
                        {
                            Id = disconnectedPeer.Id,
                            LeavingPeer = disconnectedPeer,
                            SuccessMessage = $"Peer {disconnectedPeer.Username}:{disconnectedPeer.Id} has left!",
                        });
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

    // Parses packets coming in from Peer and sends out an appropriate response.
    private bool ParseMessage(Peer fromPeer)
    {
        var packetBuffer = fromPeer.WebSocketPeer.GetPacket().GetStringFromUtf8();

        var packet = JsonConvert.DeserializeObject<BasicPacket>(packetBuffer, new JsonPacketConverter());

        if (packet is null)
        {
            return false;
        }

        if (packet is UserInfoPacket userInfoPacket)
        {
            fromPeer.SendPacket(new BasicResponsePacket
            {
                Message = Message.UserInfo,
                Success = true,
                SuccessMessage = $"Server received user information for {userInfoPacket.Username}!",
            });
            fromPeer.Username = userInfoPacket.Username;
            GD.Print($"[Log] Received Username: {fromPeer.Username}");
        }
        else if (packet is JoinLobbyPacket joinLobbyPacket)
        {
            //     fromPeer.WebSocketPeer.SendText(Json.Stringify($"FEEDBACK: Join lobby name: {packet.Data}"));
            bool foundLobby = FindLobbyByCode(joinLobbyPacket.LobbyCode)
                .Select(lobby =>
                {
                    GD.Print(
                        $"[Log] Lobby '{joinLobbyPacket.LobbyCode}' requested to be joined by {fromPeer.Username}:{fromPeer.Id}");

                    fromPeer.SendPacket(new HostResponsePacket
                    {
                        Id = 0,
                        HostPeer = lobby.Peers.First()
                    });

                    // send response packet that lobby was found
                    fromPeer.SendPacket(new BasicResponsePacket
                    {
                        Message = Message.JoinLobby,
                        Id = 0,
                        SuccessMessage = $"Lobby '{lobby.LobbyCode}' found!",
                    });

                    // for each lobby peer send the information of the peer that is joining.
                    foreach (var lobbyPeer in lobby.Peers)
                    {
                        lobbyPeer.SendPacket(new JoinLobbyExistingUserResponsePacket
                        {
                            Id = fromPeer.Id,
                            JoiningPeer = fromPeer,
                            SuccessMessage = $"Player {fromPeer.Username}:{fromPeer.Id} joined!"
                        });
                    }

                    var peersString = string.Join(", ", lobby.Peers.Select(e => e.Username).ToList());
                    fromPeer.SendPacket(new JoinLobbyJoiningUserResponsePacket
                    {
                        Id = 0,
                        LobbyPeers = lobby.Peers,
                        SuccessMessage = $"Joined lobby with peers: [{peersString}]",
                    });

                    lobby.Peers.Add(fromPeer);

                    return true;
                })
                .DefaultIfEmpty(false)
                .Single();

            if (!foundLobby)
            {
                GD.Print(
                    $"[Error] Peer {fromPeer.Username}:{fromPeer.Id} attempted to join lobby '{joinLobbyPacket.LobbyCode}', but no such lobby exists!");
                fromPeer.SendPacket(new JoinLobbyJoiningUserResponsePacket
                {
                    Id = 0,
                    Success = false,
                    Error = $"Lobby '{joinLobbyPacket.LobbyCode}' does not exist!",
                });
            }
        }
        else if (packet is LeaveLobbyPacket leaveLobbyPacket)
        {
            var foundLobby = FindLobbyByCode(leaveLobbyPacket.LobbyCode)
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
                                if (lobbyPeer.Username != fromPeer.Username)
                                {
                                    // send a message to all lobby peer's that the peer who sent this message has disconnected.
                                    lobbyPeer.SendPacket(new LeaveLobbyResponsePacket
                                    {
                                        Id = fromPeer.Id,
                                        LeavingPeer = fromPeer,
                                        SuccessMessage = $"Peer {fromPeer.Username}:{fromPeer.Id} has disconnected from lobby '{lobby.LobbyCode}'",
                                    });
                                }
                                else
                                {
                                    lobby.Peers.Remove(lobbyPeer);
                                }
                            }

                            lobby.Peers.First().IsHost = true;

                            var host = lobby.Peers.First();
                            foreach (var lobbyPeer in lobby.Peers)
                            {
                                lobbyPeer.SendPacket(new HostResponsePacket
                                {
                                    Id = 0,
                                    HostPeer = host,
                                    SuccessMessage =
                                        $"Lobby {lobby.LobbyCode}'s host has changed to {host.Username}:{host.Id}!",
                                });
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
        else if (packet is LobbyMessagePacket lobbyMessagePacket)
        {
            var relayMessagePeers = _lobbies
                .Select(lobby => lobby.Peers)
                .Single(p => p.Contains(fromPeer));
        
            foreach (var p in relayMessagePeers)
            {
                p.SendPacket(new LobbyMessagePacket
                {
                    Id = 0,
                    LobbyMessage = lobbyMessagePacket.LobbyMessage,
                    PeerSender = fromPeer
                });
            }
        }
        else if (packet is RtcAnswerPacket rtcAnswerPacket)
        {
            var foundPeer = FindPeerById(rtcAnswerPacket.AnswerId)
                .Select(foundPeer =>
                {
                    foundPeer.SendPacket(new RtcAnswerPacket
                    {
                        Id = fromPeer.Id,
                        AnswerType = rtcAnswerPacket.AnswerType,
                        AnswerSdp = rtcAnswerPacket.AnswerSdp,
                        AnswerId = rtcAnswerPacket.AnswerId,
                    });
                    
                    GD.Print($"[Log] Relaying Answer to peer {fromPeer.Id}");
                    return true;
                })
                .DefaultIfEmpty(false)
                .Single();
        
            if (foundPeer) return true;
            
            GD.Print("[Error] Answer received but could not find matching peer!");
            return false;
        }
        else if (packet is RtcOfferPacket rtcOfferPacket)
        {
            var foundPeer = FindPeerById(rtcOfferPacket.OfferId)
                .Select(foundPeer =>
                {
                    foundPeer.SendPacket(new RtcOfferPacket
                    {
                        Id = fromPeer.Id,
                        OfferType = rtcOfferPacket.OfferType,
                        OfferSdp = rtcOfferPacket.OfferSdp,
                        OfferId = rtcOfferPacket.OfferId,
                    });
                    GD.Print($"[Log] Relaying Offer to peer {fromPeer.Id}");
                    return true;
                })
                .DefaultIfEmpty(false)
                .Single();
            
            if (foundPeer) return true;
            
            GD.Print("[Error] Offer received but could not find matching peer!");
            return false;
        }
        else if (packet is RtcIcePacket rtcIcePacket)
        {
            var foundPeer = FindPeerById(rtcIcePacket.IceId)
                .Select(foundPeer =>
                {
                    foundPeer.SendPacket(new RtcIcePacket
                    {
                        Id = fromPeer.Id,
                        Media = rtcIcePacket.Media,
                        Name = rtcIcePacket.Name,
                        Index = rtcIcePacket.Index,
                        IceId = rtcIcePacket.IceId
                    });
                    GD.Print($"[Log] Relaying ICE to peer {fromPeer.Id}");
                    return true;
                })
                .DefaultIfEmpty(false)
                .Single();
            
            if (foundPeer) return true;
            
            GD.Print("[Error] ICE received but could not find matching peer!");
            return false;
        }
        else // handle basic packets here
        {
            switch (packet.Message)
            {
                case Message.LobbyList:
                    GD.Print($"[Log] Peer {fromPeer.Username}:{fromPeer.Id} requested lobby list.  Sending...");
                    fromPeer.SendPacket(new LobbyListResponsePacket
                    {
                        Id = 0, // id 0 signifies that the relay server itself is the source
                        LobbyList = _lobbies
                    });
                    break;
                case Message.CreateLobby:
                {
                    var code = Lobby.GenerateRandomLobbyCode();
                    while (!Lobby.TakenLobbyNames.Contains(code)) code = Lobby.GenerateRandomLobbyCode();
                    Lobby.TakenLobbyNames.Add(code);

                    GD.Print($"[Log] Generated lobby code: {code}");

                    // create new lobby
                    var lobby = new Lobby(code);
                    _lobbies.Add(lobby);

                    // add peer who created lobby to the lobby
                    fromPeer.IsHost = true;
                    lobby.Peers.Add(fromPeer);

                    //fromPeer.WebSocketPeer.SendText(Json.Stringify($"FEEDBACK: New lobby name: {packet.Data}"));

                    fromPeer.SendPacket(new CreateLobbyResponsePacket
                    {
                        Id = 0,
                        LobbyCode = code,
                    });

                    GD.Print($"[Log] Sent lobby code: '{code}' to the peer who requested it!");

                    break;
                }
                case Message.StartSession:
                {
                    var foundLobby = FindLobbyByPeer(fromPeer)
                        .Select(lobby =>
                        {
                            var peerIDs = string.Join(", ", lobby.Peers.Select(p => p.Id));
                    
                            foreach (var lobbyPeer in lobby.Peers)
                            {
                                lobbyPeer.SendPacket(new StartSessionResponsePacket
                                {
                                    Id = 0,
                                    StartPeers = lobby.Peers,
                                    SuccessMessage = $"Starting lobby '{lobby.LobbyCode}' with peers: [{peerIDs}]",
                                });
                            }
                            
                            GD.Print($"[Log] Starting session from lobby '{lobby.LobbyCode}'");
                            return true;
                        })
                        .Single();
                    
                        if (!foundLobby)
                        {
                            GD.Print("[Error] Could not find lobby to start session from!");
                        }
                    break; 
                }
                default:
                    return false;
            }
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
        foreach (var lobby in from Lobby lobby in _lobbies
                 from Peer lobbyPeer in lobby.Peers
                 where lobbyPeer.Equals(peer)
                 select lobby)
        {
            return Option<Lobby>.Create(lobby);
        }

        return Option<Lobby>.CreateEmpty();
    }

    private Option<Lobby> FindLobbyByCode(string lobbyCode)
    {
        foreach (var lobby in from Lobby lobby in _lobbies where lobby.LobbyCode == lobbyCode select lobby)
        {
            return Option<Lobby>.Create(lobby);
        }

        return Option<Lobby>.CreateEmpty();
    }
}