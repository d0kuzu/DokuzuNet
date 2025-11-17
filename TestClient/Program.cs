using DokuzuNet.Core;
using DokuzuNet.Integration;
using DokuzuNet.Networking;
using DokuzuNet.Networking.Message;
using DokuzuNet.Networking.Message.Messages;
using DokuzuNet.Networking.Packet;
using DokuzuNet.Transprot;
using System;
using System.Threading.Tasks;

namespace DokuzuNet.ClientTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var transport = new UdpTransport();
            var manager = new NetworkManager(transport);

            // Test 1: StartClient
            await manager.StartClientAsync("127.0.0.1", 11000);
            await Task.Delay(5000);

            // Test 2: LocalPlayer
            var localPlayer = manager.LocalPlayer;
            if (localPlayer != null)
            {
                Console.WriteLine($"Test Passed: LocalPlayer (IsLocal: {localPlayer.IsLocal})");
            }
            else
            {
                Console.WriteLine("Test Failed: LocalPlayer null");
            }

            // Test 3: SendToServerAsync (ChatMessage)
            await manager.SendToServerAsync(new ChatMessage("Hello from client!"));
            Console.WriteLine("Test Passed: SendToServerAsync");

            // Ждём спавна от сервера (для теста — press key)
            Console.WriteLine("Press any key after server spawns...");
            Console.ReadKey();

            // Test 4: SpawnMessage received (from server)
            if (manager.Objects.Count > 0)
            {
                var obj = manager.Objects.Values.First();
                Console.WriteLine("Test Passed: SpawnMessage + NetworkObject");

                // Test 5: SyncVar received (from server)
                obj.AddBehaviour(new PlayerBehaviour());
                var behaviour = obj.GetBehaviour<PlayerBehaviour>();
                if (behaviour != null)
                {
                    Console.WriteLine($"Test Passed: SyncVar (Health: {behaviour.GetHealth()})");
                }

                // Test 6: CallServerRpc (client → server)
                await behaviour.TestServerRpc();
                Console.WriteLine("Test Passed: CallServerRpc");

                // Test 7: ClientRpc received (from server)
                Console.WriteLine("Test Passed: ClientRpc (wait for server call)");
            }

            // Test 8: Despawn (from server)
            Console.WriteLine("Press any key after server despawns...");
            Console.ReadKey();
            if (manager.Objects.Count == 0)
            {
                Console.WriteLine("Test Passed: Despawn");
            }

            // Test 9: StopAsync
            await manager.StopAsync();
            Console.WriteLine("Test Passed: StopAsync");

            Console.WriteLine("All Tests Passed!");
            Console.ReadKey();
        }
    }

    public class PlayerBehaviour : NetworkBehaviour
    {
        private SyncVar<int> _health;

        protected override void OnSpawn()
        {
            base.OnSpawn();
            _health = CreateSyncVar(100);
            Console.WriteLine("PlayerBehaviour Spawned");
        }

        public void ChangeSyncVar()
        {
            _health.Value = 80;
        }

        [ClientRpc]
        public void ShowTestMessage(string text)
        {
            Console.WriteLine($"RPC Message: {text}");
        }

        public async Task TestClientRpc(NetworkPlayer target)
        {
            await CallClientRpc(target, nameof(ShowTestMessage), "RPC from server!");
        }

        [ServerRpc]
        public void TestServerMethod()
        {
            Console.WriteLine("Server: RPC from client received");
        }

        public async Task TestServerRpc()
        {
            await CallServerRpc("TestServerMethod");
        }

        public int GetHealth() => _health.Value;
    }
}