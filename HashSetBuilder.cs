using StackExchange.Redis;

namespace CM.RedisCache;

public class HashSetBuilder
{
    private readonly List<HashEntry> entries = [];

    public static HashSetBuilder New()
    {
        return new HashSetBuilder();
    }

    public HashSetBuilder Add(string name, object value)
    {
        entries.Add(new HashEntry(name, Convert.ToString(value)));
        return this;
    }

    public HashSetBuilder AddObject(object obj)
    {
        var properties = ClassAttributesReader.BuildKeyValuePair(obj);

        foreach (var prop in properties)
        {
            Add(prop.Key, prop.Value);
        }

        return this;
    }

    public HashEntry[] Build()
    {
        return entries.ToArray();
    }
}