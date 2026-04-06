// Assets/Scripts/CardSourceType.cs
public enum CardSourceType
{
    Unknown = 0,
    Booster = 1,
    Starter = 2,
    Promo = 3,
    Event = 4
}
public enum CardSOurceTypeNumber
{
    Unknown = 0,
    Booster = 1,
    Starter = 2,
    Promo = 3,
    Event = 4
}

public enum CardColor{
    Red = 0,
    Green = 1,
    Blue = 2,
    Yellow = 3,
    Colorless = 4,
    White = 5,
    Purple = 6,
}

public enum FilterType
{
    Version,
    Color,
    SourceType,
    Cost,
    Level
}
