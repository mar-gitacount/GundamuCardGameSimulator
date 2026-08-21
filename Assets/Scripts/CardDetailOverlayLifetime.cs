using UnityEngine;

/// <summary>カード詳細オーバーレイ破棄時に重ね表示ガードを解除する。</summary>
public sealed class CardDetailOverlayLifetime : MonoBehaviour
{
    private void OnDestroy()
    {
        Card.NotifyDetailUiClosed();
    }
}
