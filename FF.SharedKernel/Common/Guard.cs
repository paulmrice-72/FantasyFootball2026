namespace FF.SharedKernel.Common;

public static class Guard
{
    public static T AgainstNull<T>(T? value, string paramName)
    {
        if (value is null)
            throw new ArgumentNullException(paramName, $"{paramName} cannot be null.");
        return value;
    }

    public static string AgainstNullOrEmpty(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{paramName} cannot be null or empty.", paramName);
        return value;
    }

    public static int AgainstNegative(int value, string paramName)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(paramName, $"{paramName} cannot be negative.");
        return value;
    }

    public static decimal AgainstNegative(decimal value, string paramName)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(paramName, $"{paramName} cannot be negative.");
        return value;
    }

    public static T AgainstInvalidEnum<T>(T value, string paramName) where T : Enum
    {
        if (!Enum.IsDefined(typeof(T), value))
            throw new ArgumentOutOfRangeException(paramName, $"{paramName} is not a valid {typeof(T).Name}.");
        return value;
    }
}