using DokuzuNet.Client;
using DokuzuNet.Networking;

var client = new ClientManager();
await client.StartAsync();

//await client.SendPacketAsync(new Packet("Join", new { name = "Dias" }));
//await client.SendPacketAsync(new Packet("Chat", new { text = "Hello!" }));
while (true)
{
    Console.Write("> ");
    string msg = Console.ReadLine() ?? "";
    if (msg == "exit")
    {
        await client.StopAsync();
        break;
    }

    await client.SendAsync(msg);
}