using MultiRoom;

namespace MultiRoom2;

public class Room(string id, int host)
{
    public readonly int Host = host;
    public readonly string Id = id;
    public Dictionary<int, Client> clients = new();
    public Dictionary<int, Client> streamingCLients = new();

    public Action LeaveRoom;

    public void AddClient(Client client)
    {
        client.SendMessage += (i, msg) => streamingCLients[i].Send(msg);
        client.readressSdp += (i, msg) => streamingCLients[i].HandleSdp(msg, client.id);
        client.OnStreamingStart += id =>
        {
            client.setOutgoing(streamingCLients.Values.ToList());
            foreach (var (clientId, otherClient) in streamingCLients)
            {
                if (clientId == client.id)
                {
                    continue;
                }

                if (otherClient.isStreaming)
                {
                    Console.WriteLine("Adding peer " + client.id + " to " + otherClient.id);
                    otherClient.AddPeer(client.id);
                }
            }

            streamingCLients[client.id] = client;
        };
        client.OnSocketClosed += () =>
        {
            streamingCLients.Remove(client.id);
            clients.Remove(client.id);
            foreach (var (_, otherClient) in streamingCLients)
            {
                if (otherClient.isStreaming)
                {
                    otherClient.DisconnectPeer(client.id);
                }
            }
            LeaveRoom();
        };

        client.IsHost = client.id == Host;
        clients[client.id] = client;
    }
}