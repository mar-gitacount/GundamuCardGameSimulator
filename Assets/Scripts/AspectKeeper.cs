using System.Collections;
using System.Collections.Generic;
// using System.Numerics;
using System.Runtime.CompilerServices;
using UnityEngine;

[ExecuteAlways]
public class AspectKeeper : MonoBehaviour
{
    // 対象カメラ
    [SerializeField] private Camera targetCamera;

    [SerializeField] private Vector2 aspectVec; 

    // Update is called once per frame
    void Update()
    {
        // 画面のアスペクト比を計算
        var screenAspect = Screen.width / (float)Screen.height;
        var targetAspect = aspectVec.x / aspectVec.y;

        // 目的のアスペクト倍率
        var  magRate = screenAspect / targetAspect;

        var viewportRect = new Rect(0, 0, 1, 1);
        if(magRate < 1)
        {
            viewportRect.width = magRate;
            viewportRect.x = 0.5f - viewportRect.width * 0.5f;
            
        }
        else
        {
            viewportRect.height = 1 / magRate;
            viewportRect.y = 0.5f - viewportRect.height * 0.5f;
        }
        viewportRect.width = magRate;
        targetCamera.rect = viewportRect;
    }
}
