
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace horrorgameserverrelay;

public class JsonPacketConverter : Newtonsoft.Json.Converters.CustomCreationConverter<BasicPacket>
{
    public override BasicPacket Create(Type objectType)
    {
        throw new NotImplementedException();
    }

    public BasicPacket Create(Type objectType, JObject jObject)
    {
        var messageProperty = (string)jObject.Property("message")!;

        if (int.TryParse(messageProperty, out var message))
        {
            switch ((Message)message)
            {
                case Message.UserInfo:
                    return new UserInfoPacket();
                case Message.LobbyList:
                    return new BasicPacket();
                case Message.CreateLobby:
                    return new BasicPacket();
                case Message.LeaveLobby:
                    return new LeaveLobbyPacket();
                case Message.LobbyMessage:
                    return new LobbyMessagePacket();
                case Message.LobbyDescription:
                    return new LobbyDescriptionPacket();
                case Message.Offer:
                    return new RtcOfferPacket();
                case Message.Answer:
                    return new RtcAnswerPacket();
                case Message.InteractiveConnectivityEstablishment:
                    return new RtcIcePacket();
                case Message.StartSession:
                    return new BasicPacket();
            }
        }

        throw new ApplicationException("Packet not implemented!");
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var jObject = JObject.Load(reader);
        var target = Create(objectType, jObject);
        
        serializer.Populate(jObject.CreateReader(), target);

        return target;
    }
}
