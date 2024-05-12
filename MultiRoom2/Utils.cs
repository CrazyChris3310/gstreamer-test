namespace MultiRoom;

public class SdpMsg
{
    public SdpContent sdp;
    public SdpIceContent ice;
}

public class SdpContent
{
    public string type;
    public string sdp;
}

public class SdpIceContent
{
    public string candidate;
    public uint sdpMLineIndex;
}

public class WebMsg
{
    public int dest;
    public SdpMsg sdp;
    public ChatMsg chat;
    public ControlMsg control;
    public string username;
}

public enum ControlMsgType
{
    REMOVE_PEER,
    REMOVE_STREAM
}

public class ControlMsg
{
    public ControlMsgType type;
    public string streamId;
}

public class ChatMsg
{
    public string text;
}

public enum MediaType
{
    VIDEO, AUDIO
}

public class RegistrationRequest
{
    public string Username { get; set; }
    public string Password { get; set; }
}

public class CreateRoomResponse {
    public string RoomId { get; set; }
}