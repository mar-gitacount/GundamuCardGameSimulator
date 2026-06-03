/// <summary><see cref="EffectCompareOperator"/> による整数比較。</summary>
public static class EffectCompareHelper
{
    public static bool Compare(int value, int threshold, EffectCompareOperator op)
    {
        switch (op)
        {
            case EffectCompareOperator.GreaterOrEqual:
                return value >= threshold;
            case EffectCompareOperator.Greater:
                return value > threshold;
            case EffectCompareOperator.Equal:
                return value == threshold;
            case EffectCompareOperator.LessOrEqual:
                return value <= threshold;
            case EffectCompareOperator.Less:
                return value < threshold;
            default:
                return false;
        }
    }
}
