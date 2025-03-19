using UnityEngine;
using System;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Threading.Tasks;
using XProtocolLib;
using UnityEngine.Events;
using Assets.Scripts.XPacketTypes;


public class NetworkClient : MonoBehaviour
{
    public UnityAction<string> OnPlayerJoins;

    private TcpClient tcpClient;
    private NetworkStream stream;
    private Vector3 playerPosition;
    [SerializeField]
    private GameObject playerPrefab;
    private Dictionary<int, GameObject> otherPlayers = new Dictionary<int, GameObject>();
    private Vector3 lastPosition;
    public Hero hero;

    private bool isSuccessConnect;
    public bool IsSuccessConnect { get => isSuccessConnect; }

    private float sendInterval = 0.1f;
    private float lastSendTime = 0f;
    public UnityAction OnReady;

    private bool isStartedGame;
    private bool isThisClientReady;
    private int handshakeMagic;
    private string playerName;
    private string characterChoice;

    public void Initiate(string name, string character)
    {
        playerName = name;
        characterChoice = character;
        ConnectToServer();
    }

    private async void ConnectToServer()
    {
        tcpClient = new TcpClient("127.0.0.1", 5000);
        stream = tcpClient.GetStream();
        await SendHandshake();
        await SendPlayerData();
        ReceiveMessagesAsync();
    }

    private async Task SendHandshake()
    {
        var rand = new System.Random();
        handshakeMagic = rand.Next();
        var handshakePacket = XPacket.Serialize((byte)XPacketType.Handshake, 0, new XPacketHandshake { MagicHandshakeNumber = handshakeMagic });
        await SendPacket(handshakePacket);
    }


    
    private async Task SendPlayerData()
    {
        var playerDataPacket = XPacket.Serialize((byte)XPacketType.PlayerData, 0, new PlayerData { Name = playerName, Character = characterChoice });
        await SendPacket(playerDataPacket);
    }

    
    private async Task SendPacket(XPacket packet)
    {
        byte[] data = packet.ToPacket();
        await stream.WriteAsync(data, 0, data.Length);
        await stream.FlushAsync();
    }

    
    public async void SendAttackPacket(float attackX, float attackY, float damage, bool IsRight, float distance)
    {
        var attackPacket = XPacket.Serialize((byte)XPacketType.Attack, 0, new PlayerAttack
        {
            AttackX = attackX,
            AttackY = attackY,
            Distance = distance,
            IsRight = IsRight,
            Damage = damage
        });

        await SendPacket(attackPacket);
    }

    public async void SendReadyPacket()
    {
        var readyPacket = XPacket.Serialize((byte)XPacketType.Ready, 0, new XPacketReady());
        await SendPacket(readyPacket);
    }

    public class XPacketReady
    {
    }



    private void Update()
    {
        if (!isStartedGame) return;
        if (Input.GetKeyDown(KeyCode.G))
        {
            SendReadyPacket();
        }

        if (hero != null)
        {
            playerPosition = hero.transform.position;
            if (playerPosition != lastPosition && Time.time - lastSendTime >= sendInterval)
            {
                var positionPacket = XPacket.Serialize((byte)XPacketType.PositionUpdate, 0, new PlayerPosition
                {
                    X = playerPosition.x,
                    Y = playerPosition.y,
                    IsRight = hero.IsRight,
                    PlayerName = hero.gameObject.name 
                });

                SendPacket(positionPacket);
                lastPosition = playerPosition;
                lastSendTime = Time.time;
            }
        }
    }


    private async void ReceiveMessagesAsync()
    {
        byte[] buffer = new byte[1024];
        while (tcpClient.Connected)
        {
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead > 0)
            {
                var packet = XPacket.Parse(buffer);
                if (packet != null)
                {
                    ProcessPacket(packet);
                }
            }
        }
    }

    private void ProcessPacket(XPacket packet)
    {
        var type = XPacketTypeManager.GetTypeFromPacket(packet);
        switch (type)
        {
            case XPacketType.Handshake: // Обмен рукопожатиями
                var handshake = XPacket.Deserialize<XPacketHandshake>(packet);
                if (handshakeMagic - handshake.MagicHandshakeNumber == 15)
                {
                    Debug.Log("Handshake successful!");
                }
                break;
            case XPacketType.Ready: // Готовность
                {
                    Debug.Log("All players are ready! The game begins!");
                    isStartedGame = true;
                    OnReady?.Invoke(); 
                    break;
                }

            case XPacketType.PositionUpdate: // Позиция
                var pos = XPacket.Deserialize<PlayerPosition>(packet);
                UpdateOtherPlayerPosition(pos.PlayerId, new Vector3(pos.X, pos.Y, 0), pos.IsRight, pos.PlayerName);
                break;
            case XPacketType.Attack: // Атака
                var attack = XPacket.Deserialize<PlayerAttack>(packet);
                SimulateOtherPlayerAttack(new Vector2(attack.AttackX, attack.AttackY), attack.Damage, attack.IsRight, attack.Distance, attack.PlayerId);
                break;
            case XPacketType.PlayerData: // Данные игрока
                {
                    var playerData = XPacket.Deserialize<PlayerData>(packet);

                    if (playerData.Name == playerName)
                    {
                        Debug.Log($"Мы зарегистрировались как {playerData.Name} ({playerData.Character})");
                        isThisClientReady = true;
                    }
                    else
                    {
                        Debug.Log($"Новый игрок: {playerData.Name} ({playerData.Character})");
                        CreateNewPlayer(playerData.Name, playerData.Character);
                    }
                    break;
                }

            default:
                Console.WriteLine("Unknown packet received.");
                break;
        }
    }

    private void CreateNewPlayer(string name, string character)
    {
        if (otherPlayers.ContainsKey(name.GetHashCode())) return; 

        GameObject playerObject = Instantiate(playerPrefab);
        playerObject.name = name;
        playerObject.transform.position = Vector3.zero;
        otherPlayers[name.GetHashCode()] = playerObject;

        OnPlayerJoins?.Invoke(name);
    }



    private void UpdateOtherPlayerPosition(int playerId, Vector3 position, bool isRight, string name)
    {
        if (otherPlayers.ContainsKey(playerId))
        {
            otherPlayers[playerId].transform.position = position;
        }
        else
        {
            GameObject playerObject = Instantiate(playerPrefab);
            playerObject.name = name;
            playerObject.transform.position = position;
            playerObject.transform.localScale = Vector3.one;
            otherPlayers[playerId] = playerObject;
            OnPlayerJoins?.Invoke(name);
        }
        otherPlayers[playerId].GetComponentInChildren<SpriteRenderer>().flipX = !isRight;
    }

    private void SimulateOtherPlayerAttack(Vector2 point, float damage, bool isRight, float distance, int attackerId)
    {
        int direction = isRight ? 1 : -1;
        var hit = Physics2D.Raycast(point, Vector2.right * direction, distance);
        if (hit.collider != null)
        {
            hit.collider.GetComponentInParent<Health>()?.TakeDamage(damage);
        }
        otherPlayers[attackerId].GetComponent<Animator>().SetTrigger("IsAttack");
    }
}


