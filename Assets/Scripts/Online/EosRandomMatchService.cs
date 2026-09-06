using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using PlayEveryWare.EpicOnlineServices;
using PlayEveryWare.EpicOnlineServices.Samples;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ランダムマッチ（バックグラウンド検索 → マッチ成立 UI → OK でバトル開始）。
/// パネルを閉じてもマッチングは継続する。
/// </summary>
public sealed class EosRandomMatchService : MonoBehaviour
{
    public enum MatchPhase
    {
        Idle,
        LoggingIn,
        Searching,
        WaitingForOpponent,
        MatchFound,
        StartingBattle
    }

    private const string BucketAttributeKey = "GCG_BUCKET";
    private const string DefaultBucket = "gcg-room";
    private const float SearchRetrySeconds = 8f;
    private const float LobbyPollSeconds = 1f;

    public static EosRandomMatchService Instance { get; private set; }

    public MatchPhase Phase { get; private set; } = MatchPhase.Idle;
    public string StatusMessage { get; private set; } = string.Empty;
    public string RemotePeerId { get; private set; } = string.Empty;
    public bool LocalAccepted { get; private set; }
    public bool RemoteAccepted { get; private set; }
    public bool IsMatchmakingActive =>
        Phase == MatchPhase.LoggingIn
        || Phase == MatchPhase.Searching
        || Phase == MatchPhase.WaitingForOpponent
        || Phase == MatchPhase.MatchFound;

    public event Action StateChanged;

    private EosDeviceIdLoginService _loginService;
    private EosP2PTestService _p2pService;
    private EOSLobbyManager _lobbyManager;
    private string _bucket = DefaultBucket;
    private float _nextSearchAt;
    private float _nextLobbyPollAt;
    private bool _actionInFlight;
    private bool _isLobbyOwner;

    private GameObject _searchingHudRoot;
    private GameObject _matchFoundRoot;
    private TMP_Text _searchingLabel;
    private TMP_Text _matchFoundLabel;
    private static TMP_FontAsset _font;

    public static EosRandomMatchService EnsureExists()
    {
        if (Instance != null)
        {
            return Instance;
        }

        EosRandomMatchService existing = FindObjectOfType<EosRandomMatchService>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        GameObject go = new GameObject("EosRandomMatchService");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<EosRandomMatchService>();
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        _loginService = FindOrCreateService<EosDeviceIdLoginService>("EosDeviceIdLoginService");
        _p2pService = FindOrCreateService<EosP2PTestService>("EosP2PTestService");
        _lobbyManager = EOSManager.Instance.GetOrCreateManager<EOSLobbyManager>();
    }

    private void OnEnable()
    {
        if (_lobbyManager != null)
        {
            _lobbyManager.AddNotifyLobbyUpdate(OnLobbyUpdated);
            _lobbyManager.AddNotifyMemberUpdateReceived(OnLobbyMemberUpdated);
        }

        if (_p2pService != null)
        {
            _p2pService.MessageReceived += OnP2PMessageReceived;
        }
    }

    private void OnDisable()
    {
        if (_lobbyManager != null)
        {
            _lobbyManager.RemoveNotifyLobbyUpdate(OnLobbyUpdated);
            _lobbyManager.RemoveNotifyMemberUpdate(OnLobbyMemberUpdated);
        }

        if (_p2pService != null)
        {
            _p2pService.MessageReceived -= OnP2PMessageReceived;
        }
    }

    private void Update()
    {
        if (!IsMatchmakingActive)
        {
            return;
        }

        if (Phase == MatchPhase.WaitingForOpponent || Phase == MatchPhase.Searching)
        {
            if (Time.unscaledTime >= _nextLobbyPollAt)
            {
                _nextLobbyPollAt = Time.unscaledTime + LobbyPollSeconds;
                RefreshLobbyAndCheckMembers();
            }

            if (Phase == MatchPhase.WaitingForOpponent
                && _isLobbyOwner
                && Time.unscaledTime >= _nextSearchAt
                && !_actionInFlight)
            {
                _nextSearchAt = Time.unscaledTime + SearchRetrySeconds;
                TryJoinExistingLobbyInsteadOfWaiting();
            }
        }

        RefreshHud();
    }

    public void StartRandomMatch(string bucket = null)
    {
        if (DeckSettinObject.Instance == null || !DeckSettinObject.Instance.HasSelectedPlayerDeck())
        {
            SetStatus(MatchPhase.Idle, "Select a deck before Random Match.");
            return;
        }

        if (DeckSettinObject.Instance.SelectedDeckContainsNotUsedOnlineCards())
        {
            SetStatus(MatchPhase.Idle, "Deck contains cards not allowed online.");
            return;
        }

        if (IsMatchmakingActive)
        {
            SetStatus(Phase, "Random Match is already running.");
            EnsureSearchingHud();
            return;
        }

        // 押下時点のデッキを固定（マッチング中の編集で差し替わらないようにする）
        TestPlayDeckPick lockedDeck = DeckSettinObject.Instance.CaptureCurrentPlayerDeckPick();
        if (lockedDeck == null || lockedDeck.Cards == null || lockedDeck.Cards.Count == 0)
        {
            SetStatus(MatchPhase.Idle, "Select a deck before Random Match.");
            return;
        }

        EosOnlineMatchState.LockPlayerDeck(lockedDeck);
        Debug.Log(
            $"[RandomMatch] Locked player deck '{lockedDeck.Title}' cards={lockedDeck.TotalCount} key={lockedDeck.StorageKey}");

        _bucket = string.IsNullOrWhiteSpace(bucket) ? DefaultBucket : bucket.Trim();
        LocalAccepted = false;
        RemoteAccepted = false;
        RemotePeerId = string.Empty;
        _actionInFlight = false;
        _isLobbyOwner = false;
        EnsureSearchingHud();

        if (_loginService == null || !_loginService.IsLoggedIn)
        {
            SetStatus(MatchPhase.LoggingIn, "Logging in...");
            _loginService.LoginWithDeviceId(SystemInfo.deviceName);
            // Login is async via status; poll in Update via LoginStateChanged subscription
            _loginService.LoginStateChanged += OnLoginStateChangedForMatchmaking;
            return;
        }

        BeginSearchOrCreate();
    }

    public void CancelMatchmaking(string reason = "Matchmaking cancelled.")
    {
        _loginService.LoginStateChanged -= OnLoginStateChangedForMatchmaking;
        NotifyPeerCancel();
        LeaveCurrentLobby();
        HideMatchFoundDialog();
        DestroySearchingHud();
        LocalAccepted = false;
        RemoteAccepted = false;
        RemotePeerId = string.Empty;
        _actionInFlight = false;
        _isLobbyOwner = false;
        EosOnlineMatchState.ClearLockedPlayerDeck();
        SetStatus(MatchPhase.Idle, reason);
    }

    public void AcceptMatch()
    {
        if (Phase != MatchPhase.MatchFound)
        {
            return;
        }

        LocalAccepted = true;
        if (!string.IsNullOrWhiteSpace(RemotePeerId))
        {
            _p2pService.SendText(RemotePeerId, EosOnlineBattleMessage.CreateMatchAccept());
        }

        SetStatus(MatchPhase.MatchFound, BuildMatchFoundStatusText());
        TryStartBattleIfReady();
    }

    public void DeclineMatch()
    {
        if (Phase != MatchPhase.MatchFound && Phase != MatchPhase.WaitingForOpponent && Phase != MatchPhase.Searching)
        {
            CancelMatchmaking("Match declined.");
            return;
        }

        NotifyPeerCancel();
        LeaveCurrentLobby();
        HideMatchFoundDialog();
        DestroySearchingHud();
        LocalAccepted = false;
        RemoteAccepted = false;
        RemotePeerId = string.Empty;
        _actionInFlight = false;
        _isLobbyOwner = false;
        EosOnlineMatchState.ClearLockedPlayerDeck();
        SetStatus(MatchPhase.Idle, "Match declined.");
    }

    private void OnLoginStateChangedForMatchmaking()
    {
        if (Phase != MatchPhase.LoggingIn)
        {
            return;
        }

        if (_loginService != null && _loginService.IsLoggedIn)
        {
            _loginService.LoginStateChanged -= OnLoginStateChangedForMatchmaking;
            BeginSearchOrCreate();
            return;
        }

        if (_loginService != null && !_loginService.IsLoggingIn)
        {
            _loginService.LoginStateChanged -= OnLoginStateChangedForMatchmaking;
            DestroySearchingHud();
            EosOnlineMatchState.ClearLockedPlayerDeck();
            SetStatus(MatchPhase.Idle, "Login failed. Random Match cancelled.");
        }
    }

    private void BeginSearchOrCreate()
    {
        SetStatus(MatchPhase.Searching, "Searching for opponent...");
        _nextSearchAt = Time.unscaledTime + SearchRetrySeconds;
        SearchAndJoinOrCreate();
    }

    private void SearchAndJoinOrCreate()
    {
        if (_actionInFlight || _lobbyManager == null)
        {
            return;
        }

        _actionInFlight = true;
        _lobbyManager.SearchByAttribute(BucketAttributeKey, _bucket, result =>
        {
            _actionInFlight = false;
            if (Phase != MatchPhase.Searching && Phase != MatchPhase.WaitingForOpponent)
            {
                return;
            }

            if (result == Result.Success)
            {
                Dictionary<Lobby, Epic.OnlineServices.Lobby.LobbyDetails> results = _lobbyManager.GetSearchResults();
                foreach (KeyValuePair<Lobby, Epic.OnlineServices.Lobby.LobbyDetails> pair in results)
                {
                    if (!pair.Key.IsValid())
                    {
                        continue;
                    }

                    ProductUserId localUserId = EOSManager.Instance.GetProductUserId();
                    if (pair.Key.LobbyOwner == localUserId)
                    {
                        continue;
                    }

                    // 既に自ロビー待ちなら離脱して合流
                    Lobby current = _lobbyManager.GetCurrentLobby();
                    if (current != null && current.IsValid())
                    {
                        _lobbyManager.LeaveLobby(_ => JoinFoundLobby(pair.Key.Id, pair.Value));
                        return;
                    }

                    JoinFoundLobby(pair.Key.Id, pair.Value);
                    return;
                }
            }

            if (Phase == MatchPhase.Searching)
            {
                CreateWaitingLobby();
            }
        });
    }

    private void TryJoinExistingLobbyInsteadOfWaiting()
    {
        if (Phase != MatchPhase.WaitingForOpponent || !_isLobbyOwner)
        {
            return;
        }

        Lobby current = _lobbyManager.GetCurrentLobby();
        if (current != null && current.IsValid() && current.Members.Count >= 2)
        {
            return;
        }

        SetStatus(MatchPhase.WaitingForOpponent, "Still waiting... checking other rooms...");
        SearchAndJoinOrCreate();
    }

    private void JoinFoundLobby(string lobbyId, Epic.OnlineServices.Lobby.LobbyDetails details)
    {
        _actionInFlight = true;
        _lobbyManager.JoinLobby(lobbyId, details, false, joinResult =>
        {
            _actionInFlight = false;
            if (joinResult != Result.Success)
            {
                SetStatus(MatchPhase.Searching, $"Join failed ({joinResult}). Retrying...");
                _nextSearchAt = Time.unscaledTime + 2f;
                return;
            }

            _isLobbyOwner = false;
            RefreshCurrentLobbyFromServer();
            SetStatus(MatchPhase.WaitingForOpponent, "Joined room. Waiting for ready...");
            RefreshLobbyAndCheckMembers();
        });
    }

    private void CreateWaitingLobby()
    {
        if (_actionInFlight)
        {
            return;
        }

        Lobby current = _lobbyManager.GetCurrentLobby();
        if (current != null && current.IsValid())
        {
            _isLobbyOwner = current.LobbyOwner == EOSManager.Instance.GetProductUserId();
            SetStatus(MatchPhase.WaitingForOpponent, "Waiting for opponent...");
            RefreshLobbyAndCheckMembers();
            return;
        }

        _actionInFlight = true;
        Lobby lobby = new Lobby
        {
            BucketId = _bucket,
            MaxNumLobbyMembers = 2,
            PresenceEnabled = false,
            AllowInvites = false,
            RTCRoomEnabled = false
        };
        lobby.Attributes.Add(new LobbyAttribute
        {
            Key = BucketAttributeKey,
            ValueType = AttributeType.String,
            AsString = _bucket,
            Visibility = Epic.OnlineServices.Lobby.LobbyAttributeVisibility.Public
        });

        _lobbyManager.CreateLobby(lobby, result =>
        {
            _actionInFlight = false;
            if (result != Result.Success)
            {
                SetStatus(MatchPhase.Searching, $"Create lobby failed ({result}). Retrying...");
                _nextSearchAt = Time.unscaledTime + 2f;
                return;
            }

            _isLobbyOwner = true;
            RefreshCurrentLobbyFromServer();
            SetStatus(MatchPhase.WaitingForOpponent, "Room created. Waiting for opponent...");
            _nextSearchAt = Time.unscaledTime + SearchRetrySeconds;
            RefreshLobbyAndCheckMembers();
        });
    }

    private void RefreshLobbyAndCheckMembers()
    {
        RefreshCurrentLobbyFromServer();
        Lobby lobby = _lobbyManager != null ? _lobbyManager.GetCurrentLobby() : null;
        if (lobby == null || !lobby.IsValid())
        {
            return;
        }

        if (lobby.Members.Count < 2)
        {
            return;
        }

        if (!TryGetRemotePeerId(out string remotePeerId))
        {
            return;
        }

        RemotePeerId = remotePeerId;
        if (Phase == MatchPhase.MatchFound || Phase == MatchPhase.StartingBattle)
        {
            return;
        }

        LocalAccepted = false;
        RemoteAccepted = false;
        SetStatus(MatchPhase.MatchFound, "Opponent found!");
        ShowMatchFoundDialog();
    }

    private void TryStartBattleIfReady()
    {
        if (Phase != MatchPhase.MatchFound)
        {
            return;
        }

        if (!LocalAccepted || !RemoteAccepted)
        {
            return;
        }

        // Host がバトル開始を送る
        Lobby lobby = _lobbyManager.GetCurrentLobby();
        bool localIsHost = lobby != null
            && lobby.IsValid()
            && lobby.LobbyOwner == EOSManager.Instance.GetProductUserId();

        if (!localIsHost)
        {
            SetStatus(MatchPhase.MatchFound, "Ready. Waiting for host to start...");
            return;
        }

        StartBattleAsHost();
    }

    private void StartBattleAsHost()
    {
        if (Phase == MatchPhase.StartingBattle)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(RemotePeerId))
        {
            SetStatus(MatchPhase.MatchFound, "Remote peer missing.");
            return;
        }

        SetStatus(MatchPhase.StartingBattle, "Starting battle...");
        int seed = UnityEngine.Random.Range(1, int.MaxValue);
        bool hostGoesFirst = UnityEngine.Random.value < 0.5f;
        string lobbyId = _lobbyManager.GetCurrentLobby()?.Id ?? string.Empty;
        string message = EosOnlineBattleMessage.CreateMatchStart(seed, hostGoesFirst, lobbyId);
        _p2pService.SendText(RemotePeerId, message);

        BeginLocalBattle(isHost: true, localPlayerGoesFirst: hostGoesFirst, seed: seed, remotePeerId: RemotePeerId);
    }

    private void BeginLocalBattle(bool isHost, bool localPlayerGoesFirst, int seed, string remotePeerId)
    {
        if (EosOnlineMatchState.HasActiveMatch)
        {
            return;
        }

        HideMatchFoundDialog();
        DestroySearchingHud();

        EosOnlineMatchState.BeginMatch(
            isHost,
            localPlayerGoesFirst,
            seed,
            _loginService != null ? _loginService.ProductUserIdString : string.Empty,
            remotePeerId);

        SetStatus(MatchPhase.Idle, "Battle started.");
        LocalAccepted = false;
        RemoteAccepted = false;

        if (DeckSettinObject.Instance != null)
        {
            DeckSettinObject.Instance.EnterBattleFromMenu();
        }
    }

    private void OnP2PMessageReceived(string peerId, string payload)
    {
        if (!EosOnlineBattleMessage.TryParse(payload, out EosOnlineBattleMessage message))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(peerId) && string.IsNullOrWhiteSpace(RemotePeerId))
        {
            RemotePeerId = peerId;
        }

        switch (message.type)
        {
            case EosOnlineBattleMessage.MatchAccept:
                RemoteAccepted = true;
                if (Phase == MatchPhase.MatchFound)
                {
                    SetStatus(MatchPhase.MatchFound, BuildMatchFoundStatusText());
                    TryStartBattleIfReady();
                }
                break;
            case EosOnlineBattleMessage.MatchCancel:
                HideMatchFoundDialog();
                LeaveCurrentLobby();
                DestroySearchingHud();
                LocalAccepted = false;
                RemoteAccepted = false;
                EosOnlineMatchState.ClearLockedPlayerDeck();
                SetStatus(MatchPhase.Idle, "Opponent cancelled matchmaking.");
                break;
            case "MatchStart":
                if ((Phase == MatchPhase.MatchFound || Phase == MatchPhase.StartingBattle)
                    && LocalAccepted
                    && !EosOnlineMatchState.HasActiveMatch)
                {
                    BeginLocalBattle(
                        isHost: false,
                        localPlayerGoesFirst: !message.hostGoesFirst,
                        seed: message.seed,
                        remotePeerId: peerId);
                }
                break;
        }
    }

    private void OnLobbyUpdated()
    {
        if (IsMatchmakingActive)
        {
            RefreshLobbyAndCheckMembers();
        }
    }

    private void OnLobbyMemberUpdated(string lobbyId, ProductUserId targetUserId)
    {
        if (IsMatchmakingActive)
        {
            RefreshLobbyAndCheckMembers();
        }
    }

    private void NotifyPeerCancel()
    {
        if (_p2pService != null && !string.IsNullOrWhiteSpace(RemotePeerId))
        {
            _p2pService.SendText(RemotePeerId, EosOnlineBattleMessage.CreateMatchCancel());
        }
    }

    private void LeaveCurrentLobby()
    {
        if (_lobbyManager == null)
        {
            return;
        }

        Lobby lobby = _lobbyManager.GetCurrentLobby();
        if (lobby != null && lobby.IsValid())
        {
            _lobbyManager.LeaveLobby(_ => { });
        }
    }

    private void RefreshCurrentLobbyFromServer()
    {
        Lobby lobby = _lobbyManager != null ? _lobbyManager.GetCurrentLobby() : null;
        if (lobby == null || !lobby.IsValid())
        {
            return;
        }

        lobby.InitFromLobbyHandle(lobby.Id);
    }

    private bool TryGetRemotePeerId(out string remotePeerId)
    {
        remotePeerId = string.Empty;
        Lobby lobby = _lobbyManager != null ? _lobbyManager.GetCurrentLobby() : null;
        if (lobby == null || !lobby.IsValid())
        {
            return false;
        }

        string localUserId = _loginService != null ? _loginService.ProductUserIdString : string.Empty;
        for (int i = 0; i < lobby.Members.Count; i++)
        {
            string candidate = lobby.Members[i].ProductId?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(candidate)
                && !string.Equals(candidate, localUserId, StringComparison.Ordinal))
            {
                remotePeerId = candidate;
                return true;
            }
        }

        return false;
    }

    private string BuildMatchFoundStatusText()
    {
        string local = LocalAccepted ? "OK" : "Waiting";
        string remote = RemoteAccepted ? "OK" : "Waiting";
        return $"Opponent found! You: {local} / Opponent: {remote}";
    }

    private void SetStatus(MatchPhase phase, string message)
    {
        Phase = phase;
        StatusMessage = message ?? string.Empty;
        StateChanged?.Invoke();
        RefreshHud();
        Debug.Log($"[RandomMatch] {phase}: {StatusMessage}");
    }

    private void EnsureSearchingHud()
    {
        if (_searchingHudRoot != null)
        {
            return;
        }

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            return;
        }

        EnsureFont();
        _searchingHudRoot = CreateRect("RandomMatchSearchingHud", canvas.transform);
        RectTransform rect = _searchingHudRoot.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(420f, 88f);
        rect.anchoredPosition = new Vector2(0f, -16f);

        Image bg = _searchingHudRoot.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.12f, 0.18f, 0.92f);

        GameObject labelGo = CreateRect("Label", _searchingHudRoot.transform);
        Stretch(labelGo.GetComponent<RectTransform>(), new Vector2(12f, 40f), new Vector2(-12f, -8f));
        _searchingLabel = labelGo.AddComponent<TextMeshProUGUI>();
        _searchingLabel.font = _font;
        _searchingLabel.fontSize = 16;
        _searchingLabel.color = Color.white;
        _searchingLabel.alignment = TextAlignmentOptions.MidlineLeft;
        _searchingLabel.text = "Random Match: Searching...";

        GameObject cancelGo = CreateRect("CancelSearchButton", _searchingHudRoot.transform);
        RectTransform cancelRect = cancelGo.GetComponent<RectTransform>();
        cancelRect.anchorMin = new Vector2(1f, 0f);
        cancelRect.anchorMax = new Vector2(1f, 0f);
        cancelRect.pivot = new Vector2(1f, 0f);
        cancelRect.sizeDelta = new Vector2(120f, 28f);
        cancelRect.anchoredPosition = new Vector2(-10f, 8f);
        Image cancelBg = cancelGo.AddComponent<Image>();
        cancelBg.color = new Color(0.55f, 0.2f, 0.2f, 1f);
        Button cancelBtn = cancelGo.AddComponent<Button>();
        cancelBtn.onClick.AddListener(() => CancelMatchmaking("Search cancelled."));
        GameObject cancelTextGo = CreateRect("Text", cancelGo.transform);
        Stretch(cancelTextGo.GetComponent<RectTransform>());
        TextMeshProUGUI cancelText = cancelTextGo.AddComponent<TextMeshProUGUI>();
        cancelText.font = _font;
        cancelText.text = "Cancel";
        cancelText.fontSize = 14;
        cancelText.alignment = TextAlignmentOptions.Center;
        cancelText.color = Color.white;

        _searchingHudRoot.transform.SetAsLastSibling();
    }

    private void DestroySearchingHud()
    {
        if (_searchingHudRoot != null)
        {
            Destroy(_searchingHudRoot);
            _searchingHudRoot = null;
            _searchingLabel = null;
        }
    }

    private void ShowMatchFoundDialog()
    {
        if (_matchFoundRoot != null)
        {
            RefreshHud();
            return;
        }

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            return;
        }

        EnsureFont();
        _matchFoundRoot = CreateRect("RandomMatchFoundDialog", canvas.transform);
        RectTransform rootRect = _matchFoundRoot.GetComponent<RectTransform>();
        Stretch(rootRect);
        Image dim = _matchFoundRoot.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);

        GameObject panel = CreateRect("Panel", _matchFoundRoot.transform);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(360f, 200f);
        Image panelBg = panel.AddComponent<Image>();
        panelBg.color = new Color(0.1f, 0.14f, 0.2f, 0.98f);

        GameObject titleGo = CreateRect("Title", panel.transform);
        RectTransform titleRect = titleGo.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.sizeDelta = new Vector2(-24f, 36f);
        titleRect.anchoredPosition = new Vector2(0f, -16f);
        TextMeshProUGUI title = titleGo.AddComponent<TextMeshProUGUI>();
        title.font = _font;
        title.text = "Match Found";
        title.fontSize = 22;
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Center;
        title.color = Color.white;

        GameObject bodyGo = CreateRect("Body", panel.transform);
        RectTransform bodyRect = bodyGo.GetComponent<RectTransform>();
        bodyRect.anchorMin = new Vector2(0f, 0.5f);
        bodyRect.anchorMax = new Vector2(1f, 0.5f);
        bodyRect.pivot = new Vector2(0.5f, 0.5f);
        bodyRect.sizeDelta = new Vector2(-24f, 56f);
        bodyRect.anchoredPosition = new Vector2(0f, 12f);
        _matchFoundLabel = bodyGo.AddComponent<TextMeshProUGUI>();
        _matchFoundLabel.font = _font;
        _matchFoundLabel.fontSize = 15;
        _matchFoundLabel.alignment = TextAlignmentOptions.Center;
        _matchFoundLabel.color = new Color(0.85f, 0.9f, 0.95f);
        _matchFoundLabel.text = BuildMatchFoundStatusText();

        CreateDialogButton(panel.transform, "OK", new Vector2(-70f, 28f), new Color(0.18f, 0.5f, 0.28f, 1f), AcceptMatch);
        CreateDialogButton(panel.transform, "Cancel", new Vector2(70f, 28f), new Color(0.55f, 0.22f, 0.22f, 1f), DeclineMatch);

        _matchFoundRoot.transform.SetAsLastSibling();
        if (_searchingHudRoot != null)
        {
            _searchingHudRoot.SetActive(false);
        }
    }

    private void HideMatchFoundDialog()
    {
        if (_matchFoundRoot != null)
        {
            Destroy(_matchFoundRoot);
            _matchFoundRoot = null;
            _matchFoundLabel = null;
        }
    }

    private void RefreshHud()
    {
        if (_searchingLabel != null && _searchingHudRoot != null && _searchingHudRoot.activeSelf)
        {
            _searchingLabel.text = string.IsNullOrWhiteSpace(StatusMessage)
                ? $"Random Match: {Phase}"
                : StatusMessage;
        }

        if (_matchFoundLabel != null)
        {
            _matchFoundLabel.text = BuildMatchFoundStatusText();
        }

        if (_matchFoundRoot != null)
        {
            _matchFoundRoot.transform.SetAsLastSibling();
        }
        else if (_searchingHudRoot != null)
        {
            _searchingHudRoot.transform.SetAsLastSibling();
        }
    }

    private void CreateDialogButton(Transform parent, string label, Vector2 anchoredPos, Color color, Action onClick)
    {
        GameObject go = CreateRect(label + "Button", parent);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(120f, 36f);
        rect.anchoredPosition = anchoredPos;
        Image image = go.AddComponent<Image>();
        image.color = color;
        Button button = go.AddComponent<Button>();
        button.onClick.AddListener(() => onClick?.Invoke());
        GameObject textGo = CreateRect("Text", go.transform);
        Stretch(textGo.GetComponent<RectTransform>());
        TextMeshProUGUI text = textGo.AddComponent<TextMeshProUGUI>();
        text.font = _font;
        text.text = label;
        text.fontSize = 16;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
    }

    private static GameObject CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void Stretch(RectTransform rect)
    {
        Stretch(rect, Vector2.zero, Vector2.zero);
    }

    private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static void EnsureFont()
    {
        if (_font != null)
        {
            return;
        }

        _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF - Fallback");
        if (_font == null)
        {
            _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        }
    }

    private static T FindOrCreateService<T>(string objectName) where T : Component
    {
        T existing = FindObjectOfType<T>();
        if (existing != null)
        {
            return existing;
        }

        GameObject go = new GameObject(objectName);
        return go.AddComponent<T>();
    }
}
