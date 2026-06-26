using System;
using System.Collections.Generic;
using System.Text;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using PlayEveryWare.EpicOnlineServices;
using UnityEngine;

/// <summary>
/// 固定 Socket で EOS P2P の最小送受信を行うサービス。
/// ping/pong/hello と、将来のバトルコマンド送受信の共通土台にする。
/// </summary>
public class EosP2PTestService : MonoBehaviour
{
    public const string SocketName = "gcg-p2p-test";
    public static EosP2PTestService Instance { get; private set; }

    public event Action<string> StatusChanged;
    public event Action<string, string> MessageReceived;

    private readonly Queue<(string PeerId, string Payload)> _pendingMessages = new Queue<(string PeerId, string Payload)>();
    private P2PInterface _p2pInterface;
    private ulong _connectionRequestNotificationId;
    private bool _isShuttingDown;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (!_isShuttingDown)
        {
            ShutdownForQuit();
        }
    }

    /// <summary>アプリ終了時に P2P 通知・接続を解放する。</summary>
    public void ShutdownForQuit(string remotePeerId = null)
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        _pendingMessages.Clear();
        UnsubscribeFromConnectionRequests();
        TryCloseAllP2PConnections(remotePeerId);
        _p2pInterface = null;
    }

    private void OnEnable()
    {
        TryRefreshInterface();
        TrySubscribeToConnectionRequests();
    }

    private void OnDisable()
    {
        UnsubscribeFromConnectionRequests();
    }

    private void Update()
    {
        if (_isShuttingDown || EosOnlineShutdownCoordinator.IsShuttingDown)
        {
            return;
        }

        TryRefreshInterface();
        PumpIncomingPackets();
        DispatchQueuedMessages();
    }

    public void OnLoggedIn()
    {
        TryRefreshInterface();
        UnsubscribeFromConnectionRequests();
        TrySubscribeToConnectionRequests();
    }

    public bool IsReady()
    {
        ProductUserId localUserId = GetLocalUserId();
        return localUserId != null && localUserId.IsValid() && TryRefreshInterface();
    }

    public bool SendText(string remoteProductUserId, string message)
    {
        if (_isShuttingDown || EosOnlineShutdownCoordinator.IsShuttingDown)
        {
            return false;
        }

        ProductUserId localUserId = GetLocalUserId();
        ProductUserId remoteUserId = ProductUserId.FromString(remoteProductUserId);

        if (localUserId == null || !localUserId.IsValid())
        {
            SetStatus("P2P send failed: local ProductUserId is invalid.");
            return false;
        }

        if (remoteUserId == null || !remoteUserId.IsValid())
        {
            SetStatus("P2P send failed: remote ProductUserId is invalid.");
            return false;
        }

        if (!TryRefreshInterface())
        {
            SetStatus("P2P send failed: could not get P2PInterface.");
            return false;
        }

        TrySubscribeToConnectionRequests();

        var options = new SendPacketOptions
        {
            LocalUserId = localUserId,
            RemoteUserId = remoteUserId,
            SocketId = BuildSocketId(),
            AllowDelayedDelivery = true,
            Channel = 0,
            Reliability = PacketReliability.ReliableOrdered,
            Data = new ArraySegment<byte>(Encoding.UTF8.GetBytes(message ?? string.Empty))
        };

        Result result = _p2pInterface.SendPacket(ref options);
        if (result != Result.Success)
        {
            SetStatus($"P2P send failed: {result}");
            return false;
        }

        SetStatus($"P2P sent -> {remoteProductUserId}: {message}");
        return true;
    }

    private bool TryRefreshInterface()
    {
        if (_p2pInterface != null)
        {
            return true;
        }

        _p2pInterface = EOSManager.Instance?.GetEOSPlatformInterface()?.GetP2PInterface();
        return _p2pInterface != null;
    }

    private void PumpIncomingPackets()
    {
        if (_p2pInterface == null)
        {
            return;
        }

        ProductUserId localUserId = GetLocalUserId();
        if (localUserId == null || !localUserId.IsValid())
        {
            return;
        }

        while (true)
        {
            var sizeOptions = new GetNextReceivedPacketSizeOptions
            {
                LocalUserId = localUserId,
                RequestedChannel = null
            };

            Result sizeResult = _p2pInterface.GetNextReceivedPacketSize(ref sizeOptions, out uint nextPacketSizeBytes);
            if (sizeResult == Result.NotFound || nextPacketSizeBytes == 0)
            {
                break;
            }

            if (sizeResult != Result.Success)
            {
                SetStatus($"Failed to get receive packet size: {sizeResult}");
                break;
            }

            byte[] buffer = new byte[nextPacketSizeBytes];
            ArraySegment<byte> segment = new ArraySegment<byte>(buffer);
            ProductUserId peerId = null;
            SocketId socketId = default;

            var receiveOptions = new ReceivePacketOptions
            {
                LocalUserId = localUserId,
                MaxDataSizeBytes = nextPacketSizeBytes,
                RequestedChannel = null
            };

            Result receiveResult = _p2pInterface.ReceivePacket(
                ref receiveOptions,
                ref peerId,
                ref socketId,
                out byte _,
                segment,
                out uint bytesWritten);

            if (receiveResult == Result.NotFound)
            {
                break;
            }

            if (receiveResult != Result.Success)
            {
                SetStatus($"P2P receive failed: {receiveResult}");
                break;
            }

            string text = Encoding.UTF8.GetString(buffer, 0, (int)bytesWritten);
            _pendingMessages.Enqueue((peerId?.ToString() ?? string.Empty, text));
        }
    }

    private void DispatchQueuedMessages()
    {
        if (_isShuttingDown || EosOnlineShutdownCoordinator.IsShuttingDown)
        {
            _pendingMessages.Clear();
            return;
        }

        while (_pendingMessages.Count > 0)
        {
            (string peerId, string payload) = _pendingMessages.Dequeue();
            SetStatus($"P2P received <- {peerId}: {payload}");
            MessageReceived?.Invoke(peerId, payload);
        }
    }

    private void TrySubscribeToConnectionRequests()
    {
        if (_connectionRequestNotificationId != 0 || _p2pInterface == null)
        {
            return;
        }

        ProductUserId localUserId = GetLocalUserId();
        if (localUserId == null || !localUserId.IsValid())
        {
            return;
        }

        var options = new AddNotifyPeerConnectionRequestOptions
        {
            LocalUserId = localUserId,
            SocketId = BuildSocketId()
        };

        _connectionRequestNotificationId = _p2pInterface.AddNotifyPeerConnectionRequest(
            ref options,
            null,
            OnIncomingConnectionRequest);
    }

    private void UnsubscribeFromConnectionRequests()
    {
        if (_connectionRequestNotificationId == 0 || _p2pInterface == null)
        {
            return;
        }

        _p2pInterface.RemoveNotifyPeerConnectionRequest(_connectionRequestNotificationId);
        _connectionRequestNotificationId = 0;
    }

    private void OnIncomingConnectionRequest(ref OnIncomingConnectionRequestInfo data)
    {
        if (_isShuttingDown || EosOnlineShutdownCoordinator.IsShuttingDown)
        {
            return;
        }

        if (!TryRefreshInterface())
        {
            return;
        }

        ProductUserId localUserId = GetLocalUserId();
        if (localUserId == null || !localUserId.IsValid())
        {
            return;
        }

        var options = new AcceptConnectionOptions
        {
            LocalUserId = localUserId,
            RemoteUserId = data.RemoteUserId,
            SocketId = BuildSocketId()
        };

        Result result = _p2pInterface.AcceptConnection(ref options);
        SetStatus(result == Result.Success
            ? $"P2P connection request accepted: {data.RemoteUserId}"
            : $"P2P connection accept failed: {result}");
    }

    private static ProductUserId GetLocalUserId()
    {
        return EOSManager.Instance != null ? EOSManager.Instance.GetProductUserId() : null;
    }

    private static SocketId BuildSocketId()
    {
        return new SocketId { SocketName = SocketName };
    }

    private void SetStatus(string message)
    {
        if (_isShuttingDown || EosOnlineShutdownCoordinator.IsShuttingDown)
        {
            return;
        }

        Debug.Log("[EOS P2P] " + message);
        StatusChanged?.Invoke(message);
    }

    private void TryCloseAllP2PConnections(string remotePeerId = null)
    {
        if (_p2pInterface == null && !TryRefreshInterface())
        {
            return;
        }

        ProductUserId localUserId = GetLocalUserId();
        if (localUserId == null || !localUserId.IsValid())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(remotePeerId))
        {
            remotePeerId = EosOnlineMatchState.RemoteProductUserId;
        }

        if (!string.IsNullOrWhiteSpace(remotePeerId))
        {
            ProductUserId remoteUserId = ProductUserId.FromString(remotePeerId);
            if (remoteUserId != null && remoteUserId.IsValid())
            {
                var closePeerOptions = new CloseConnectionOptions
                {
                    LocalUserId = localUserId,
                    RemoteUserId = remoteUserId,
                    SocketId = BuildSocketId()
                };
                _p2pInterface.CloseConnection(ref closePeerOptions);
            }
        }

        var closeAllOptions = new CloseConnectionsOptions
        {
            LocalUserId = localUserId,
            SocketId = BuildSocketId()
        };
        _p2pInterface.CloseConnections(ref closeAllOptions);
    }
}
