using System.Collections.Generic;
using Godot;

namespace horrorgameserverrelay;

public partial class Lobby : RefCounted
{
    public List<Peer> Peers { get; private set; }
    public string Name { get; private set; }

    public Lobby(string name)
    {
        Peers = new List<Peer>();
        Name = name;
    }
}