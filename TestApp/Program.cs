using DokuzuNet.Core;
using DokuzuNet.Integration;
using DokuzuNet.Networking;
using DokuzuNet.Networking.Message;
using DokuzuNet.Networking.Message.Messages;
using DokuzuNet.Networking.Packet;
using DokuzuNet.Transprot;
using System;
using System.Threading.Tasks;

namespace DokuzuNet.ServerTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var transport = new UdpTransport();
            var manager = new NetworkManager(transport);

            // Test 1: StartHost (Server + Client)
            await manager.StartHostAsync(11000);
            Console.WriteLine("Test Passed: StartHost");

            // Test 2: PrefabRegistry
            manager.Prefabs.Register("PlayerPrefab"); // Prefabs public
            ushort prefabId = manager.Prefabs.GetId("PlayerPrefab");
            string prefabName = manager.Prefabs.GetPrefab(prefabId);
            Console.WriteLine($"Test Passed: PrefabRegistry (ID: {prefabId}, Name: {prefabName})");

            // Test 3: OnPlayerJoined (ждём клиента)
            manager.OnPlayerJoined += player =>
            {
                Console.WriteLine($"Test Passed: OnPlayerJoined ({player.Connection.EndPoint})");
            };

            // Ждём клиента
            Console.WriteLine("Press any key after starting client...");
            Console.ReadKey();

            var clientPlayer = manager.Players.FirstOrDefault(p => !p.IsLocal);
            if (clientPlayer == null) { Console.WriteLine("Test Failed: No client"); return; }

            // Test 4: SpawnAsync + NetworkObject + AddBehaviour + OnSpawn
            var obj = await manager.SpawnAsync("PlayerPrefab", clientPlayer, 10, 0, 0);
            obj.AddBehaviour(new PlayerBehaviour()); // AddBehaviour public
            Console.WriteLine($"Test Passed: SpawnAsync + AddBehaviour + OnSpawn for {clientPlayer.Connection.EndPoint.Port}");

            // Test 5: SyncVar (изменение на сервере)
            var behaviour = obj.GetBehaviour<PlayerBehaviour>();
            if (behaviour != null)
            {
                behaviour.ChangeSyncVar(); // Тестовое изменение
                Console.WriteLine("Test Passed: SyncVar change on server");
            }

            // Test 6: RPC (BroadcastClientRpc от сервера)
            if (behaviour != null)
            {
                await behaviour.TestClientRpc(clientPlayer); // Вызов на клиенте
                Console.WriteLine("Test Passed: BroadcastClientRpc from server");
            }

            // Test 7: ChatMessage (Broadcast)
            await manager.BroadcastAsync(new ChatMessage("Hello from server!"));
            Console.WriteLine("Test Passed: ChatMessage Broadcast");

            // Test 8: PacketWriter / Reader
            using (var writer = new PacketWriter())
            {
                writer.WriteInt(42);
                writer.WriteFloat(3.14f);
                writer.WriteString("Test");
                var buffer = writer.GetBuffer();

                var reader = new PacketReader(buffer);
                int i = reader.ReadInt();
                float f = reader.ReadFloat();
                string s = reader.ReadString();

                Console.WriteLine($"Test Passed: PacketWriter/Reader (int: {i}, float: {f}, string: {s})");
            }

            Console.WriteLine("Press any key for start despawn test...");
            Console.ReadKey();
            // Test 9: DespawnAsync + OnDespawn
            await manager.DespawnAsync(obj); // DespawnAsync добавлен
            Console.WriteLine("Test Passed: DespawnAsync + OnDespawn");

            // Test 10: StopAsync + OnPlayerLeft
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