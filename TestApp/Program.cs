using DokuzuNet.Server;

var server = new ServerManager();
await server.StartAsync();
Console.ReadLine();