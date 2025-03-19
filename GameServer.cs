using System.Net.Sockets;
using System.Net;
using XProtocolLib;
using GameServer.XPacketTypes;

namespace GameServer
{
    public class GameServer
    {
        private TcpListener server;
        private Thread listenerThread;
        private Dictionary<int, ClientContext> connectedClients = new Dictionary<int, ClientContext>();
        private Dictionary<int, bool> playersReady = new Dictionary<int, bool>();
        private Queue<XPacket> messageQueue = new Queue<XPacket>();
        private SemaphoreSlim semaphore = new SemaphoreSlim(1);

        public void StartServer()
        {
            server = new TcpListener(IPAddress.Any, 5000);
            server.Start();
            listenerThread = new Thread(ListenForClients);
            listenerThread.Start();
            Console.WriteLine("The server is running...");
        }

        private void ListenForClients()
        {
            while (true)
            {
                TcpClient client = server.AcceptTcpClient();
                Task.Run(() => HandleClient(client));
            }
        }

        private async Task HandleClient(TcpClient tcpClient)
        {
            NetworkStream stream = tcpClient.GetStream();
            var clientId = tcpClient.GetHashCode();
            var clientContext = new ClientContext(clientId, tcpClient);

            connectedClients.Add(clientId, clientContext);
            playersReady[clientId] = false;
            Console.WriteLine($"Player {clientId} connected");

            while (true)
            {
                try
                {
                    byte[] buffer = new byte[1024];
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                        break;

                    var packet = XPacket.Parse(buffer);
                    if (packet != null)
                        await EnqueueMessageAsync(packet, clientContext);
                }
                catch
                {
                    break;
                }
            }

            connectedClients.Remove(clientId);
            playersReady.Remove(clientId);
            tcpClient.Close();
            Console.WriteLine($"Player {clientId} disconnected");
        }

        private async Task EnqueueMessageAsync(XPacket packet, ClientContext clientContext)
        {
            await semaphore.WaitAsync();
            try
            {
                messageQueue.Enqueue(packet);
            }
            finally
            {
                semaphore.Release();
            }

            await ReceiveMessagesAsync(clientContext);
        }

        private async Task ReceiveMessagesAsync(ClientContext clientContext)
        {
            while (messageQueue.Count > 0)
            {
                await semaphore.WaitAsync();
                try
                {
                    if (messageQueue.Count > 0)
                    {
                        XPacket packet = messageQueue.Dequeue();
                        ProcessPacket(packet, clientContext);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
                await Task.Delay(50);
            }
        }

        private async void ProcessPacket(XPacket packet, ClientContext clientContext)
        {
            var type = XPacketTypeManager.GetTypeFromPacket(packet);
            var clientId = clientContext.ClientId;

            switch (type)
            {
                case XPacketType.Handshake: // Обмен рукопожатиями
                    var handshake = XPacket.Deserialize<XPacketHandshake>(packet);
                    handshake.MagicHandshakeNumber -= 15;
                    await SendToClient(clientId, XPacket.Serialize((byte)XPacketType.Handshake, 0, handshake));
                    break;
                case XPacketType.Ready: // Готовность
                    {
                        playersReady[clientId] = true;
                        Console.WriteLine($"Player {clientId} is ready!");
                        if (playersReady.Values.All(ready => ready))
                        {
                            Console.WriteLine("All players are ready! The game starts!");
                            await SendToAllClients(XPacket.Serialize((byte)XPacketType.Ready, 0, new XPacketReady()));
                        }
                        break;
                    }

                case XPacketType.PositionUpdate: //Обновление позиции
                    var pos = XPacket.Deserialize<PlayerPosition>(packet);
                    pos.PlayerId = clientId;
                    await SendToAllClients(XPacket.Serialize((byte)XPacketType.PositionUpdate, 0, pos), clientId);
                    break;
                case XPacketType.Attack: //  Атака
                    var attack = XPacket.Deserialize<PlayerAttack>(packet);
                    attack.PlayerId = clientId;
                    await SendToAllClients(XPacket.Serialize((byte)XPacketType.Attack, 0, attack), clientId);
                    break;
                case XPacketType.PlayerData: // Данные игрока
                    await HandlePlayerData(packet, clientContext);
                    break;
                case XPacketType.Unknown:
                default:
                    Console.WriteLine("Unknown packet received.");
                    break;
            }
        }

        public class XPacketReady
        {
        }

        private async Task HandlePlayerData(XPacket packet, ClientContext clientContext)
        {
            var playerData = XPacket.Deserialize<PlayerData>(packet);
            clientContext.Name = playerData.Name;
            clientContext.Character = playerData.Character;
            int clientId = clientContext.ClientId;

            Console.WriteLine($"Player {clientId} registered as {playerData.Name} ({playerData.Character})");

            foreach (var client in connectedClients.Values)
            {
                if (client.ClientId != clientId && !string.IsNullOrEmpty(client.Name))
                {
                    var existingPlayerData = XPacket.Serialize((byte)XPacketType.PlayerData, 0, new PlayerData
                    {
                        Name = client.Name,
                        Character = client.Character
                    });
                    await SendToClient(clientId, existingPlayerData);
                }
            }

            await SendToAllClients(XPacket.Serialize((byte)XPacketType.PlayerData, 0, playerData), clientId);
        }

        private async Task SendToClient(int clientId, XPacket packet)
        {
            if (connectedClients.ContainsKey(clientId))
            {
                byte[] data = packet.ToPacket();
                await connectedClients[clientId].Stream.WriteAsync(data, 0, data.Length);
            }
        }

        private async Task SendToAllClients(XPacket packet, int ignoreId = default)
        {
            byte[] data = packet.ToPacket();
            foreach (var clientId in connectedClients.Keys)
            {
                if (clientId == ignoreId) continue;
                await connectedClients[clientId].Stream.WriteAsync(data, 0, data.Length);
            }
        }
    }

}
