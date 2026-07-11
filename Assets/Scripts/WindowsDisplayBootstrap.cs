using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Windows スタンドアロンビルドでウィンドウモードを維持しつつ、
/// Player Settings で指定した解像度（例: 800×480）を作業領域内に中央配置する。
/// </summary>
public static class WindowsDisplayBootstrap
{
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyWorkAreaOnStartup()
    {
        int targetWidth = Screen.width;
        int targetHeight = Screen.height;
        if (targetWidth <= 0 || targetHeight <= 0)
        {
            targetWidth = 800;
            targetHeight = 480;
        }

        Rect workArea = new Rect();
        if (!SystemParametersInfo(SpiGetWorkArea, 0, ref workArea, 0))
        {
            Screen.SetResolution(targetWidth, targetHeight, FullScreenMode.Windowed);
            return;
        }

        int workWidth = workArea.right - workArea.left;
        int workHeight = workArea.bottom - workArea.top;
        if (workWidth <= 0 || workHeight <= 0)
        {
            Screen.SetResolution(targetWidth, targetHeight, FullScreenMode.Windowed);
            return;
        }

        int width = Mathf.Min(targetWidth, workWidth);
        int height = Mathf.Min(targetHeight, workHeight);
        int x = workArea.left + Mathf.Max(0, (workWidth - width) / 2);
        int y = workArea.top + Mathf.Max(0, (workHeight - height) / 2);

        Screen.SetResolution(width, height, FullScreenMode.Windowed);
        TryPositionWindow(x, y, width, height);

        var runner = new GameObject(nameof(WindowsDisplayBootstrap));
        Object.DontDestroyOnLoad(runner);
        runner.hideFlags = HideFlags.HideAndDontSave;
        runner.AddComponent<WorkAreaWindowPositioner>().Initialize(x, y, width, height);
    }

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
}
