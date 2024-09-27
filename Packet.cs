using Godot;

namespace horrorgameserverrelay;

public partial class Packet : RefCounted
{
    public Message type;
    public int id;
    public string data;
}