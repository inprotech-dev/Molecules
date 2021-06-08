using System;

internal static class SerializationUtilities
{
    public static string PersistedTypeName(Type type)
    {
        return $"{type.FullName}, {type.Assembly.GetName().Name}";
    }
}