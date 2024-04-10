using System.ComponentModel;
using System.Reflection;

namespace CM.RedisCache;

internal sealed class ClassAttributesReader
{
    internal static List<KeyValuePair<string, string>> BuildKeyValuePair<T>(T instance) where T : class, new()
    {
        var keyValuePairs = new List<KeyValuePair<string, string>>();

        // get all class properties
        var properties = instance.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToArray();

        // loop on properties
        foreach (var prop in properties)
        {
            // Property Type
            var t = instance.GetType().GetProperty(prop.Name)?.PropertyType;
            
            string? value = null;

            if (IsPrimitiveType(t))
            {
                value = prop.GetValue(instance) != null
                    ? prop.GetValue(instance).ToString()
                    : string.Empty;
            }

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                // If the property is nullable, retrieve the underlying value
                value = prop.GetValue(instance)?.ToString();
            }

            if (value != null)
            {
                var attribute = prop
                    .GetCustomAttributes(typeof(DisplayNameAttribute), true)
                    .Cast<DisplayNameAttribute>().SingleOrDefault();

                if (attribute != null)
                {
                    keyValuePairs.Add(new KeyValuePair<string, string>(attribute.DisplayName, value));
                }
                else
                {
                    keyValuePairs.Add(new KeyValuePair<string, string>(prop.Name, value));
                }
            }
        }

        return keyValuePairs;
    }

    private static bool IsPrimitiveType(Type t)
    {
        return t.IsPrimitive || t == typeof(decimal) || t == typeof(string) || t == typeof(DateTime) || t == typeof(Guid);
    }
}