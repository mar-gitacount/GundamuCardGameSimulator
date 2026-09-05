using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using PlayEveryWare.EpicOnlineServices;
using PlayEveryWare.EpicOnlineServices.Samples;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Home シーン上で Device ID ログイン / Lobby / P2P / バトル開始を試す最小コントローラ。
/// UI もランタイム生成する。
/// </summary>
public class EosOnlinePlaytestController : MonoBehaviour
{
    private const string HomeSceneName = "Home";
    private const string BucketAttributeKey = "GCG_BUCKET";
    private const string DefaultBucket = "gcg-room";

    private static TMP_FontAsset _font;

    private EosDeviceIdLoginService _loginService;
    private EosP2PTestService _p2pService;
    private EOSLobbyManager _lobbyManager;

    private TMP_InputField _displayNameField;
    private TMP_InputField _bucketField;
    private TMP_InputField _remoteIdField;
    private TMP_Text _statusText;
    private TMP_Text _localIdText;
    private TMP_Text _lobbyText;
    private TMP_Text _selectedDeckText;
    private TMP_Text _logText;
    private Button _startBattleButton;
    private ScrollRect _logScrollRect;

    private readonly List<string> _logs = new List<string>();
    private Transform _uiContentRoot;

    public static void InstallOnCanvas(Canvas canvas)
    {
        if (canvas == null || canvas.GetComponentInChildren<EosOnlinePlaytestController>(true) != null)
        {
            return;
        }

        EnsureFont();

        const float panelWidth = 420f;
        const float preferredHeight = 760f;
        const float screenMargin = 24f;
        float panelHeight = Mathf.Min(preferredHeight, Screen.height - screenMargin);

        GameObject root = CreateRect("EOSOnlinePlaytestPanel", canvas.transform);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0f, 1f);
        rootRect.sizeDelta = new Vector2(panelWidth, panelHeight);
        rootRect.anchoredPosition = new Vector2(12f, -12f);

        Image background = root.AddComponent<Image>();
        background.color = new Color(0.07f, 0.09f, 0.13f, 0.93f);

        ScrollRect scroll = root.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;

        GameObject viewport = CreateRect("Viewport", root.transform);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect);
        Image viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = Color.white;
        Mask mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        GameObject content = CreateRect("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 16, 16);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewportRect;
        scroll.content = contentRect;

        root.AddComponent<EosOnlinePlaytestController>();
        root.SetActive(false);
    }

    public static void OpenPanel()
    {
        Canvas canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("[EOS Online] Canvas not found. Cannot open online panel.");
            return;
        }

        EosOnlinePlaytestController panel = UnityEngine.Object.FindObjectOfType<EosOnlinePlaytestController>(true);
        if (panel == null)
        {
            InstallOnCanvas(canvas);
            panel = UnityEngine.Object.FindObjectOfType<EosOnlinePlaytestController>(true);
        }

        if (panel == null)
        {
            Debug.LogWarning("[EOS Online] Failed to create online panel.");
            return;
        }

        panel.gameObject.SetActive(true);
        panel.transform.SetAsLastSibling();
        panel.OnPanelOpened();
    }

    public static void ClosePanel()
    {
        EosOnlinePlaytestController panel = UnityEngine.Object.FindObjectOfType<EosOnlinePlaytestController>(true);
        if (panel != null)
        {
            panel.gameObject.SetActive(false);
        }
    }

    private void OnPanelOpened()
    {
        RefreshSelectedDeckLabel();
        if (DeckSettinObject.Instance != null && DeckSettinObject.Instance.HasSelectedPlayerDeck())
        {
            AppendLog($"Player deck ready: {DeckSettinObject.Instance.GetSelectedDeckDisplayName()}");
        }
        else
        {
            AppendLog("No player deck selected. Close this panel and pick a deck first.");
        }

        RefreshUi();
    }

    private void Awake()
    {
        Transform content = transform.Find("Viewport/Content");
        _uiContentRoot = content != null ? content : transform;

        _loginService = FindOrCreateService<EosDeviceIdLoginService>("EosDeviceIdLoginService");
        _p2pService = FindOrCreateService<EosP2PTestService>("EosP2PTestService");
        _lobbyManager = EOSManager.Instance.GetOrCreateManager<EOSLobbyManager>();
        BuildUi();
    }

    private void OnEnable()
    {
        _loginService.LoginStateChanged += RefreshUi;
        _loginService.StatusChanged += AppendLog;
        _p2pService.StatusChanged += AppendLog;
        _p2pService.MessageReceived += OnP2PMessageReceived;
        _lobbyManager.AddNotifyLobbyUpdate(OnLobbyUpdated);
        _lobbyManager.AddNotifyMemberUpdateReceived(OnLobbyMemberUpdated);
        EosOnlineMatchState.MatchStateChanged += RefreshUi;
        RefreshUi();
    }

    private void OnDisable()
    {
        if (_loginService != null)
        {
            _loginService.LoginStateChanged -= RefreshUi;
            _loginService.StatusChanged -= AppendLog;
        }

        if (_p2pService != null)
        {
            _p2pService.StatusChanged -= AppendLog;
            _p2pService.MessageReceived -= OnP2PMessageReceived;
        }

        if (_lobbyManager != null)
        {
            _lobbyManager.RemoveNotifyLobbyUpdate(OnLobbyUpdated);
            _lobbyManager.RemoveNotifyMemberUpdate(OnLobbyMemberUpdated);
        }

        EosOnlineMatchState.MatchStateChanged -= RefreshUi;
    }

    private void Update()
    {
        RefreshUi();
    }

    private void BuildUi()
    {
        if (_uiContentRoot == null)
        {
            return;
        }

        Transform parent = _uiContentRoot;

        CreateLabel(parent, "EOS Online Test", 24, FontStyles.Bold, Color.white);
        _selectedDeckText = CreateLabel(parent, "Player deck: (not selected)", 14, FontStyles.Normal, new Color(0.75f, 0.9f, 0.8f));
        CreateButton(parent, "Close", ClosePanel);
        _statusText = CreateLabel(parent, "EOS not connected", 14, FontStyles.Normal, new Color(0.8f, 0.88f, 0.96f));
        _localIdText = CreateLabel(parent, "Local PUID: -", 14, FontStyles.Normal, new Color(0.72f, 0.82f, 0.95f));
        _lobbyText = CreateLabel(parent, "Lobby: -", 14, FontStyles.Normal, new Color(0.72f, 0.82f, 0.95f));

        _displayNameField = CreateInputField(parent, "Display Name", SystemInfo.deviceName);
        _bucketField = CreateInputField(parent, "Bucket / Room", DefaultBucket);
        _remoteIdField = CreateInputField(parent, "Remote ProductUserId", string.Empty);

        CreateButton(parent, "Random Match", StartRandomMatch);
        CreateButton(parent, "1. Device ID Login", () => _loginService.LoginWithDeviceId(_displayNameField.text));
        CreateButton(parent, "2. Create Lobby", CreateLobby);
        CreateButton(parent, "3. Search + Join First", SearchAndJoinFirstLobby);
        CreateButton(parent, "Use Lobby Peer -> Target", UseLobbyPeerAsTarget);
        CreateButton(parent, "Send Hello", SendHello);
        CreateButton(parent, "Send Ping", SendPing);
        _startBattleButton = CreateButton(parent, "Start Online Battle (Host)", StartOnlineBattleAsHost);

        AppendDeveloperModeToggles(parent);

        CreateLabel(parent, "Log", 18, FontStyles.Bold, Color.white);
        _logText = CreateMultilineLog(parent);
    }

    /// <summary>許可端末のみ Online パネル内に開発者トグルを追加（レイアウト構造は既存のまま）。</summary>
    private void AppendDeveloperModeToggles(Transform parent)
    {
        if (!DeveloperModeAccess.IsAuthorized)
        {
            return;
        }

        CreateLabel(parent, "Developer", 18, FontStyles.Bold, new Color(0.7f, 0.95f, 0.75f));
        CreateLabel(
            parent,
            "Device: " + DeveloperModeAccess.ResolvedDeviceId,
            12,
            FontStyles.Normal,
            new Color(0.65f, 0.75f, 0.7f));
        CreateToggle(
            parent,
            "Start at cost/LV 10",
            DeveloperModeAccess.StartAtLevel10,
            value => DeveloperModeAccess.StartAtLevel10 = value);
    }

    private static Toggle CreateToggle(Transform parent, string label, bool initial, Action<bool> onChanged)
    {
        GameObject row = CreateRect(label + "ToggleRow", parent);
        LayoutElement rowLayout = row.AddComponent<LayoutElement>();
        rowLayout.preferredHeight = 40f;

        HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 10f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = true;
        h.padding = new RectOffset(0, 0, 0, 0);

        GameObject labelObj = CreateRect("Label", row.transform);
        LayoutElement labelLe = labelObj.AddComponent<LayoutElement>();
        labelLe.flexibleWidth = 1f;
        TextMeshProUGUI text = labelObj.AddComponent<TextMeshProUGUI>();
        text.font = _font;
        text.text = label;
        text.fontSize = 15;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;

        GameObject toggleObj = CreateRect("Toggle", row.transform);
        LayoutElement toggleLe = toggleObj.AddComponent<LayoutElement>();
        toggleLe.preferredWidth = 48f;
        toggleLe.flexibleWidth = 0f;
        Image bg = toggleObj.AddComponent<Image>();
        bg.color = new Color(0.2f, 0.24f, 0.3f, 1f);
        Toggle toggle = toggleObj.AddComponent<Toggle>();
        toggle.targetGraphic = bg;

        GameObject checkObj = CreateRect("Checkmark", toggleObj.transform);
        Stretch(checkObj.GetComponent<RectTransform>());
        RectTransform checkRt = checkObj.GetComponent<RectTransform>();
        checkRt.offsetMin = new Vector2(6f, 4f);
        checkRt.offsetMax = new Vector2(-6f, -4f);
        Image check = checkObj.AddComponent<Image>();
        check.color = new Color(0.35f, 0.85f, 0.45f, 1f);
        toggle.graphic = check;
        toggle.isOn = initial;
        toggle.onValueChanged.AddListener(v => onChanged?.Invoke(v));
        return toggle;
    }

    private void CreateLobby()
    {
        if (!RequireLoggedIn())
        {
            return;
        }

        string bucket = NormalizeBucket(_bucketField.text);
        Lobby lobby = new Lobby
        {
            BucketId = bucket,
            MaxNumLobbyMembers = 2,
            PresenceEnabled = false,
            AllowInvites = false,
            RTCRoomEnabled = false
        };
        lobby.Attributes.Add(new LobbyAttribute
        {
            Key = BucketAttributeKey,
            ValueType = AttributeType.String,
            AsString = bucket,
            Visibility = Epic.OnlineServices.Lobby.LobbyAttributeVisibility.Public
        });

        _lobbyManager.CreateLobby(lobby, result =>
        {
            if (result == Result.Success)
            {
                RefreshCurrentLobbyFromServer();
                int memberCount = _lobbyManager.GetCurrentLobby()?.Members.Count ?? 0;
                AppendLog($"Lobby created: {bucket} (members={memberCount})");
            }
            else
            {
                AppendLog($"Lobby create failed: {result}");
            }

            RefreshUi();
        });
    }

    private void SearchAndJoinFirstLobby()
    {
        if (!RequireLoggedIn())
        {
            return;
        }

        string bucket = NormalizeBucket(_bucketField.text);
        _lobbyManager.SearchByAttribute(BucketAttributeKey, bucket, result =>
        {
            if (result != Result.Success)
            {
                AppendLog($"Lobby search failed: {result}");
                return;
            }

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

                _lobbyManager.JoinLobby(pair.Key.Id, pair.Value, false, joinResult =>
                {
                    if (joinResult == Result.Success)
                    {
                        RefreshCurrentLobbyFromServer();
                        int memberCount = _lobbyManager.GetCurrentLobby()?.Members.Count ?? 0;
                        AppendLog($"Lobby joined: {pair.Key.Id} (members={memberCount})");
                    }
                    else
                    {
                        AppendLog($"Lobby join failed: {joinResult}");
                    }

                    UseLobbyPeerAsTarget();
                    RefreshUi();
                });
                return;
            }

            AppendLog("No joinable lobby found.");
        });
    }

    private void UseLobbyPeerAsTarget()
    {
        if (TryGetRemotePeerId(out string remotePeerId))
        {
            _remoteIdField.text = remotePeerId;
            AppendLog($"Set lobby peer as target: {remotePeerId}");
        }
        else
        {
            AppendLog("Could not get remote ProductUserId from lobby. Ensure both players are in the lobby.");
        }
    }

    private void SendHello()
    {
        SendRaw(EosOnlineBattleMessage.CreateHello("hello"));
    }

    private void SendPing()
    {
        SendRaw(EosOnlineBattleMessage.CreatePing());
    }

    private void StartRandomMatch()
    {
        EosRandomMatchService service = EosRandomMatchService.EnsureExists();
        string bucket = _bucketField != null ? NormalizeBucket(_bucketField.text) : DefaultBucket;
        AppendLog("Random Match started. You can close this panel and keep browsing.");
        service.StartRandomMatch(bucket);
        // バックグラウンド継続のためパネルは閉じてもよい
        ClosePanel();
    }

    private void StartOnlineBattleAsHost()
    {
        if (!RequireLoggedIn())
        {
            return;
        }

        if (DeckSettinObject.Instance == null || !DeckSettinObject.Instance.HasSelectedPlayerDeck())
        {
            AppendLog("Start failed: select your deck before Online Battle.");
            return;
        }

        if (DeckSettinObject.Instance.SelectedDeckContainsNotUsedOnlineCards())
        {
            AppendLog("Start failed: Not used card online.");
            return;
        }

        string remotePeerId = _remoteIdField.text?.Trim();
        if (string.IsNullOrWhiteSpace(remotePeerId))
        {
            AppendLog("Start failed: remote ProductUserId is not set.");
            return;
        }

        int seed = UnityEngine.Random.Range(1, int.MaxValue);
        bool hostGoesFirst = UnityEngine.Random.value < 0.5f;
        string lobbyId = _lobbyManager.GetCurrentLobby()?.Id ?? string.Empty;
        string message = EosOnlineBattleMessage.CreateMatchStart(seed, hostGoesFirst, lobbyId);

        if (!_p2pService.SendText(remotePeerId, message))
        {
            return;
        }

        BeginLocalBattle(
            isHost: true,
            localPlayerGoesFirst: hostGoesFirst,
            seed: seed,
            remotePeerId: remotePeerId);
    }

    private void BeginLocalBattle(bool isHost, bool localPlayerGoesFirst, int seed, string remotePeerId)
    {
        EosOnlineMatchState.BeginMatch(
            isHost,
            localPlayerGoesFirst,
            seed,
            _loginService.ProductUserIdString,
            remotePeerId);

        AppendLog($"Online battle started: seed={seed} localFirst={localPlayerGoesFirst}");

        if (DeckSettinObject.Instance != null)
        {
            DeckSettinObject.Instance.EnterBattleFromMenu();
        }
        else
        {
            AppendLog("Battle start failed: DeckSettinObject not found.");
        }
    }

    private void OnP2PMessageReceived(string peerId, string payload)
    {
        if (!EosOnlineBattleMessage.TryParse(payload, out EosOnlineBattleMessage message))
        {
            AppendLog($"Unsupported message: {payload}");
            return;
        }

        switch (message.type)
        {
            case "hello":
                AppendLog($"hello received: {message.payload}");
                break;
            case "ping":
                AppendLog("ping received -> sending pong.");
                _p2pService.SendText(peerId, EosOnlineBattleMessage.CreatePong());
                break;
            case "pong":
                AppendLog("pong received: P2P round-trip OK");
                break;
            case "MatchStart":
                // Random Match サービス側で開始する場合は二重遷移を避ける
                if (EosRandomMatchService.Instance != null
                    && EosRandomMatchService.Instance.IsMatchmakingActive)
                {
                    AppendLog("MatchStart handled by Random Match service.");
                    break;
                }

                if (EosOnlineMatchState.HasActiveMatch)
                {
                    AppendLog("MatchStart ignored: battle already active.");
                    break;
                }

                AppendLog("MatchStart received. Entering online battle.");
                BeginLocalBattle(
                    isHost: false,
                    localPlayerGoesFirst: !message.hostGoesFirst,
                    seed: message.seed,
                    remotePeerId: peerId);
                break;
            case EosOnlineBattleMessage.MatchAccept:
            case EosOnlineBattleMessage.MatchCancel:
                // Random Match サービスが処理する
                break;
            default:
                AppendLog($"Received type={message.type}");
                break;
        }

        if (string.IsNullOrWhiteSpace(_remoteIdField.text))
        {
            _remoteIdField.text = peerId;
        }
    }

    private void OnLobbyUpdated()
    {
        RefreshCurrentLobbyFromServer();
        UseLobbyPeerAsTargetIfEmpty();
        RefreshUi();
    }

    private void OnLobbyMemberUpdated(string lobbyId, ProductUserId targetUserId)
    {
        RefreshCurrentLobbyFromServer();
        int memberCount = _lobbyManager.GetCurrentLobby()?.Members.Count ?? 0;
        AppendLog($"Lobby member updated: lobby={lobbyId} target={targetUserId} members={memberCount}");
        UseLobbyPeerAsTargetIfEmpty();
        RefreshUi();
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

    private void UseLobbyPeerAsTargetIfEmpty()
    {
        if (!string.IsNullOrWhiteSpace(_remoteIdField.text))
        {
            return;
        }

        if (TryGetRemotePeerId(out string remotePeerId))
        {
            _remoteIdField.text = remotePeerId;
        }
    }

    private bool TryGetRemotePeerId(out string remotePeerId)
    {
        remotePeerId = string.Empty;
        Lobby lobby = _lobbyManager.GetCurrentLobby();
        if (lobby == null || !lobby.IsValid())
        {
            return false;
        }

        string localUserId = _loginService.ProductUserIdString;
        for (int i = 0; i < lobby.Members.Count; i++)
        {
            string candidate = lobby.Members[i].ProductId?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(candidate) &&
                !string.Equals(candidate, localUserId, StringComparison.Ordinal))
            {
                remotePeerId = candidate;
                return true;
            }
        }

        return false;
    }

    private bool RequireLoggedIn()
    {
        if (_loginService != null && _loginService.IsLoggedIn)
        {
            return true;
        }

        AppendLog("Please log in to EOS before running this action.");
        return false;
    }

    private void SendRaw(string payload)
    {
        if (!RequireLoggedIn())
        {
            return;
        }

        string remotePeerId = _remoteIdField.text?.Trim();
        if (string.IsNullOrWhiteSpace(remotePeerId))
        {
            AppendLog("Enter a remote ProductUserId.");
            return;
        }

        _p2pService.SendText(remotePeerId, payload);
    }

    private void RefreshUi()
    {
        if (_statusText == null)
        {
            return;
        }

        string loginState = _loginService != null && _loginService.IsLoggedIn
            ? "EOS logged in"
            : "EOS not connected";
        _statusText.text = $"Status: {loginState}";
        _localIdText.text = $"Local PUID: {(_loginService != null ? _loginService.ProductUserIdString : "-")}";
        RefreshSelectedDeckLabel();

        Lobby lobby = _lobbyManager != null ? _lobbyManager.GetCurrentLobby() : null;
        _lobbyText.text = lobby != null && lobby.IsValid()
            ? $"Lobby: {lobby.Id} / members={lobby.Members.Count}"
            : "Lobby: -";

        if (_startBattleButton != null)
        {
            _startBattleButton.interactable = _loginService != null &&
                _loginService.IsLoggedIn &&
                DeckSettinObject.Instance != null &&
                DeckSettinObject.Instance.HasSelectedPlayerDeck() &&
                !string.IsNullOrWhiteSpace(_remoteIdField != null ? _remoteIdField.text : string.Empty);
        }
    }

    private void RefreshSelectedDeckLabel()
    {
        if (_selectedDeckText == null)
        {
            return;
        }

        if (DeckSettinObject.Instance != null && DeckSettinObject.Instance.HasSelectedPlayerDeck())
        {
            _selectedDeckText.text = $"Player deck: {DeckSettinObject.Instance.GetSelectedDeckDisplayName()}";
        }
        else
        {
            _selectedDeckText.text = "Player deck: (not selected)";
        }
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (_logs.Count >= 100)
        {
            _logs.RemoveAt(0);
        }

        _logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        if (_logText != null)
        {
            _logText.text = string.Join("\n", _logs);
        }

        ScrollLogToBottom();
    }

    private void ScrollLogToBottom()
    {
        if (_logScrollRect == null)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();
        _logScrollRect.verticalNormalizedPosition = 0f;
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

    private static string NormalizeBucket(string rawBucket)
    {
        return string.IsNullOrWhiteSpace(rawBucket) ? DefaultBucket : rawBucket.Trim();
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

    private static GameObject CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static TMP_Text CreateLabel(Transform parent, string text, int fontSize, FontStyles style, Color color)
    {
        GameObject go = CreateRect("Label", parent);
        LayoutElement layout = go.AddComponent<LayoutElement>();
        layout.minHeight = fontSize + 10f;

        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        label.font = _font;
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.color = color;
        label.enableWordWrapping = true;
        return label;
    }

    private static TMP_InputField CreateInputField(Transform parent, string placeholder, string initialValue)
    {
        GameObject root = CreateRect(placeholder, parent);
        LayoutElement layout = root.AddComponent<LayoutElement>();
        layout.preferredHeight = 40f;

        Image bg = root.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.13f, 0.18f, 1f);

        GameObject textArea = CreateRect("Text Area", root.transform);
        RectTransform textAreaRect = textArea.GetComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(10f, 6f);
        textAreaRect.offsetMax = new Vector2(-10f, -6f);

        GameObject placeholderObj = CreateRect("Placeholder", textArea.transform);
        Stretch(placeholderObj.GetComponent<RectTransform>());
        TextMeshProUGUI placeholderText = placeholderObj.AddComponent<TextMeshProUGUI>();
        placeholderText.font = _font;
        placeholderText.text = placeholder;
        placeholderText.fontSize = 16;
        placeholderText.color = new Color(0.58f, 0.64f, 0.74f, 0.8f);

        GameObject textObj = CreateRect("Text", textArea.transform);
        Stretch(textObj.GetComponent<RectTransform>());
        TextMeshProUGUI inputText = textObj.AddComponent<TextMeshProUGUI>();
        inputText.font = _font;
        inputText.fontSize = 16;
        inputText.color = Color.white;
        inputText.text = initialValue;

        TMP_InputField inputField = root.AddComponent<TMP_InputField>();
        inputField.textViewport = textAreaRect;
        inputField.textComponent = inputText;
        inputField.placeholder = placeholderText;
        inputField.text = initialValue;
        inputField.lineType = TMP_InputField.LineType.SingleLine;
        inputField.caretWidth = 2;
        return inputField;
    }

    private static Button CreateButton(Transform parent, string label, Action onClick)
    {
        GameObject buttonObj = CreateRect(label + "Button", parent);
        LayoutElement layout = buttonObj.AddComponent<LayoutElement>();
        layout.preferredHeight = 40f;

        Image image = buttonObj.AddComponent<Image>();
        image.color = new Color(0.18f, 0.4f, 0.76f, 1f);

        Button button = buttonObj.AddComponent<Button>();
        button.onClick.AddListener(() => onClick?.Invoke());

        GameObject labelObj = CreateRect("Text", buttonObj.transform);
        Stretch(labelObj.GetComponent<RectTransform>());
        TextMeshProUGUI text = labelObj.AddComponent<TextMeshProUGUI>();
        text.font = _font;
        text.text = label;
        text.fontSize = 16;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        return button;
    }

    private TMP_Text CreateMultilineLog(Transform parent)
    {
        GameObject root = CreateRect("LogArea", parent);
        LayoutElement layout = root.AddComponent<LayoutElement>();
        layout.preferredHeight = 180f;
        layout.minHeight = 140f;

        Image bg = root.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.07f, 0.1f, 1f);

        ScrollRect scroll = root.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 20f;
        _logScrollRect = scroll;

        GameObject viewport = CreateRect("LogViewport", root.transform);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect);
        viewportRect.offsetMin = new Vector2(8f, 8f);
        viewportRect.offsetMax = new Vector2(-8f, -8f);
        Image viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = Color.white;
        Mask mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        GameObject content = CreateRect("LogContent", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(4, 4, 4, 4);
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        ContentSizeFitter contentFitter = content.AddComponent<ContentSizeFitter>();
        contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject textObj = CreateRect("LogText", content.transform);
        TextMeshProUGUI logText = textObj.AddComponent<TextMeshProUGUI>();
        logText.font = _font;
        logText.fontSize = 14;
        logText.color = new Color(0.88f, 0.92f, 0.98f);
        logText.alignment = TextAlignmentOptions.TopLeft;
        logText.enableWordWrapping = true;
        logText.overflowMode = TextOverflowModes.Overflow;
        logText.text = "Logs appear here.\nDrag inside this box to scroll past entries.";

        ContentSizeFitter textFitter = textObj.AddComponent<ContentSizeFitter>();
        textFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        textFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        LayoutElement textLayout = textObj.AddComponent<LayoutElement>();
        textLayout.flexibleWidth = 1f;

        scroll.viewport = viewportRect;
        scroll.content = contentRect;

        return logText;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
    }
}
