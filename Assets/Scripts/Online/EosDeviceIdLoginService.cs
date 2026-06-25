using System;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using PlayEveryWare.EpicOnlineServices;
using PlayEveryWare.EpicOnlineServices.Samples;
using UnityEngine;

/// <summary>
/// Device ID ベースの EOS Connect ログインを担当する最小サービス。
/// Epic アカウントを要求せず ProductUserId を取得する。
/// </summary>
public class EosDeviceIdLoginService : MonoBehaviour
{
    public static EosDeviceIdLoginService Instance { get; private set; }

    public event Action LoginStateChanged;
    public event Action<string> StatusChanged;

    public bool IsLoggingIn { get; private set; }
    public bool IsLoggedIn => EOSManager.Instance != null
        && EOSManager.Instance.GetProductUserId() != null
        && EOSManager.Instance.GetProductUserId().IsValid();

    public string ProductUserIdString => IsLoggedIn
        ? EOSManager.Instance.GetProductUserId().ToString()
        : string.Empty;

    private string _pendingDisplayName;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void LoginWithDeviceId(string displayName)
    {
        if (IsLoggedIn)
        {
            SetStatus("Already logged in with EOS Device ID.");
            LoginStateChanged?.Invoke();
            return;
        }

        if (IsLoggingIn)
        {
            SetStatus("EOS login already in progress.");
            return;
        }

        _pendingDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? SystemInfo.deviceName
            : displayName.Trim();

        var connectInterface = EOSManager.Instance?.GetEOSConnectInterface();
        if (connectInterface == null)
        {
            SetStatus("Could not get EOS ConnectInterface. Check EOSManager initialization.");
            return;
        }

        IsLoggingIn = true;
        SetStatus("Creating Device ID...");

        var options = new CreateDeviceIdOptions
        {
            DeviceModel = string.IsNullOrWhiteSpace(SystemInfo.deviceModel)
                ? SystemInfo.deviceName
                : SystemInfo.deviceModel
        };

        connectInterface.CreateDeviceId(ref options, null, OnCreateDeviceIdCompleted);
    }

    private void OnCreateDeviceIdCompleted(ref CreateDeviceIdCallbackInfo callbackInfo)
    {
        if (callbackInfo.ResultCode != Result.Success &&
            callbackInfo.ResultCode != Result.DuplicateNotAllowed)
        {
            IsLoggingIn = false;
            SetStatus($"Device ID creation failed: {callbackInfo.ResultCode}");
            LoginStateChanged?.Invoke();
            return;
        }

        SetStatus("Starting Connect login...");
        EOSManager.Instance.StartConnectLoginWithOptions(
            ExternalCredentialType.DeviceidAccessToken,
            null,
            _pendingDisplayName,
            OnConnectLoginCompleted);
    }

    private void OnConnectLoginCompleted(LoginCallbackInfo callbackInfo)
    {
        if (callbackInfo.ResultCode == Result.Success)
        {
            IsLoggingIn = false;
            NotifyEosSubsystemsLoggedIn();
            SetStatus($"EOS login succeeded: {ProductUserIdString}");
            LoginStateChanged?.Invoke();
            return;
        }

        if (callbackInfo.ResultCode == Result.InvalidUser)
        {
            SetStatus("Creating Connect user...");
            EOSManager.Instance.CreateConnectUserWithContinuanceToken(
                callbackInfo.ContinuanceToken,
                OnCreateConnectUserCompleted);
            return;
        }

        IsLoggingIn = false;
        SetStatus($"EOS login failed: {callbackInfo.ResultCode}");
        LoginStateChanged?.Invoke();
    }

    private void OnCreateConnectUserCompleted(CreateUserCallbackInfo callbackInfo)
    {
        if (callbackInfo.ResultCode != Result.Success)
        {
            IsLoggingIn = false;
            SetStatus($"Connect user creation failed: {callbackInfo.ResultCode}");
            LoginStateChanged?.Invoke();
            return;
        }

        SetStatus("Connect user created. Retrying login...");
        EOSManager.Instance.StartConnectLoginWithOptions(
            ExternalCredentialType.DeviceidAccessToken,
            null,
            _pendingDisplayName,
            OnConnectLoginCompleted);
    }

    private void NotifyEosSubsystemsLoggedIn()
    {
        if (EOSManager.Instance == null)
        {
            return;
        }

        EOSManager.Instance.GetOrCreateManager<EOSLobbyManager>().OnLoggedIn();

        if (EosP2PTestService.Instance != null)
        {
            EosP2PTestService.Instance.OnLoggedIn();
        }
    }

    private void SetStatus(string message)
    {
        Debug.Log("[EOS Login] " + message);
        StatusChanged?.Invoke(message);
    }
}
