using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ToggleDatail : MonoBehaviour
{
    public FilterType filterType;
    public int id;

    public CardSOurceTypeNumber cardSOurceTypeNumber;

    public int version;
    
    public CardSourceType sourceType;
    // !以下消す
    public CardSourceType CardColor;
    public CardColor color;


    
    // Start is called before the first frame update
    void Start()
    {
        Debug.Log($"ToggleDatail - filterType: {filterType}, id: {id}, sourceType: {sourceType}, CardColor: {CardColor}");
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
