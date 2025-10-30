using DokuzuNet.Client;
using DokuzuNet.Networking;

var client = new ClientManager();
await client.ConnectAsync("127.0.0.1", 7777);

await client.SendPacketAsync(new Packet("Join", new { name = "Dias" }));
await client.SendPacketAsync(new Packet("Chat", new { text = "Hello!" }));
while (true)
{
    Console.Write("> ");
    string msg = Console.ReadLine() ?? "";
    if (msg == "exit")
    {
        client.Disconnect();
        break;
    }

    await client.SendAsync(msg);
}