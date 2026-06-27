using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Windows スタンドアロンビルドで、1920×1080 等のフル画面サイズ指定でもタスクバーを隠さない。
/// 作業領域（Work Area）にウィンドウを収める。
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
        Rect workArea = new Rect();
        if (!SystemParametersInfo(SpiGetWorkArea, 0, ref workArea, 0))
        {
            Screen.fullScreenMode = FullScreenMode.Windowed;
            return;
        }

        int width = workArea.right - workArea.left;
        int height = workArea.bottom - workArea.top;
        if (width <= 0 || height <= 0)
        {
            Screen.fullScreenMode = FullScreenMode.Windowed;
            return;
        }

        // フルスクリーン系は画面全体を覆うためタスクバーが隠れる。常にウィンドウモードへ。
        Screen.SetResolution(width, height, FullScreenMode.Windowed);
        TryPositionWindow(workArea, width, height);

        var runner = new GameObject(nameof(WindowsDisplayBootstrap));
        Object.DontDestroyOnLoad(runner);
        runner.hideFlags = HideFlags.HideAndDontSave;
        runner.AddComponent<WorkAreaWindowPositioner>().Initialize(workArea, width, height);
    }

    private static void TryPositionWindow(Rect workArea, int width, int height)
    {
        System.IntPtr hwnd = GetActiveWindow();
        if (hwnd == System.IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(hwnd, System.IntPtr.Zero, workArea.left, workArea.top, width, height, SwpNoZOrder | SwpShowWindow);
    }

    private sealed class WorkAreaWindowPositioner : MonoBehaviour
    {
        private Rect _workArea;
        private int _width;
        private int _height;
        private int _attemptsLeft = 5;

        public void Initialize(Rect workArea, int width, int height)
        {
            _workArea = workArea;
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

            TryPositionWindow(_workArea, _width, _height);
        }
    }
#endif
}
