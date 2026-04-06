using System;

public static class PredicateExtensions
{
    public static Predicate<T> And<T>(
        this Predicate<T> a,
        Predicate<T> b)
    {
        return x => a(x) && b(x);
    }

    public static Predicate<T> Or<T>(
        this Predicate<T> a,
        Predicate<T> b)
    {
        return x => a(x) || b(x);
    }
}
