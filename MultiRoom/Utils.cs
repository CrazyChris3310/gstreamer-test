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
    public int src;
    public int dest;
    public SdpMsg sdp;
    public ChatMsg chat;
    public ControlMsg control;
}

public class ControlMsg
{
    public bool authenticated;
    public UserlistMsg userlist;
}

public class UserlistMsg
{
    public string[] names;
    public int awaiting;
}

public class ChatMsg
{
    public string text;
}