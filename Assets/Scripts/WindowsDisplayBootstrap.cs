using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// PC（Windows スタンドアロン / エディタ Play）でゲーム画面を 480×800 の縦画面ウィンドウに固定する。
/// </summary>
public static class WindowsDisplayBootstrap
{
    public const int TargetWidth = 480;
    public const int TargetHeight = 800;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
    private const uint SpiGetWorkArea = 0x0030;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpShowWindow = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref Rect pvParam, uint fWinIni);

    [DllImport("user32.dll")]
    private static extern System.IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        System.IntPtr hWnd,
        System.IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyFixedPortraitOnStartup()
    {
        Screen.fullScreenMode = FullScreenMode.Windowed;
        Screen.SetResolution(TargetWidth, TargetHeight, FullScreenMode.Windowed);

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        Rect workArea = new Rect();
        if (!SystemParametersInfo(SpiGetWorkArea, 0, ref workArea, 0))
        {
            return;
        }

        int workWidth = workArea.right - workArea.left;
        int workHeight = workArea.bottom - workArea.top;
        if (workWidth <= 0 || workHeight <= 0)
        {
            return;
        }

        int x = workArea.left + Mathf.Max(0, (workWidth - TargetWidth) / 2);
        int y = workArea.top + Mathf.Max(0, (workHeight - TargetHeight) / 2);
        TryPositionWindow(x, y, TargetWidth, TargetHeight);

        var runner = new GameObject(nameof(WindowsDisplayBootstrap));
        Object.DontDestroyOnLoad(runner);
        runner.hideFlags = HideFlags.HideAndDontSave;
        runner.AddComponent<WorkAreaWindowPositioner>().Initialize(x, y, TargetWidth, TargetHeight);
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    private static void TryPositionWindow(int x, int y, int width, int height)
    {
        System.IntPtr hwnd = GetActiveWindow();
        if (hwnd == System.IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(hwnd, System.IntPtr.Zero, x, y, width, height, SwpNoZOrder | SwpShowWindow);
    }

    private sealed class WorkAreaWindowPositioner : MonoBehaviour
    {
        private int _x;
        private int _y;
        private int _width;
        private int _height;
        private int _attemptsLeft = 5;

        public void Initialize(int x, int y, int width, int height)
        {
            _x = x;
            _y = y;
            _width = width;
            _height = height;
        }

        private void Update()
        {
            if (_attemptsLeft-- <= 0)
            {
                Destroy(gameObject);
                return;
            }

            TryPositionWindow(_x, _y, _width, _height);
        }
    }
#endif
#endif
}
