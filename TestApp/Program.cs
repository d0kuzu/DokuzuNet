using DokuzuNet.Server;

var server = new ServerManager();
await server.StartAsync(7777);