using DokuzuNet.Client;

var client = new ClientManager();
await client.ConnectAsync("127.0.0.1", 7777);

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