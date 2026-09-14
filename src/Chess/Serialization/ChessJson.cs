using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Chess;

public static class ChessJson
{
    private static readonly Type[] UnionTypes = typeof(ChessJson).Assembly.GetTypes()
        .Where(type => type.IsValueType && typeof(IUnion).IsAssignableFrom(type)).ToArray();

    private static readonly HashSet<Type> CaseTypes = UnionTypes
        .SelectMany(type => type.GetConstructors())
        .Select(constructor => constructor.GetParameters().Single().ParameterType).ToHashSet();

    private static readonly Dictionary<Type, object> Singletons = UnionTypes
        .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Static))
        .Where(property => CaseTypes.Contains(property.PropertyType))
        .Select(property => property.GetValue(null)!)
        .Distinct().ToDictionary(value => value.GetType());

    public static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { Configure } },
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    private static void Configure(JsonTypeInfo info)
    {
        if (info.Kind == JsonTypeInfoKind.Union && UnionTypes.Contains(info.Type))
        {
            var cases = info.UnionCases!.ToDictionary(@case => @case.CaseType.Name, @case => @case.CaseType);
            info.TypeClassifier = (ref Utf8JsonReader reader) =>
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    return null;
                }
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var isCase = reader.ValueTextEquals("case"u8);
                    reader.Read();
                    if (isCase)
                    {
                        return reader.TokenType == JsonTokenType.String && cases.TryGetValue(reader.GetString()!, out var type)
                            ? type : null;
                    }
                    reader.Skip();
                }
                return null;
            };
        }
        if (info.Kind == JsonTypeInfoKind.Object && info.Type.Assembly == typeof(ChessJson).Assembly)
        {
            var unionProperties = info.Properties.Where(property => UnionTypes.Contains(property.PropertyType)).ToArray();
            info.OnDeserialized = instance =>
            {
                foreach (var property in unionProperties)
                {
                    if (property.Get!(instance) is IUnion { Value: null })
                    {
                        throw new JsonException($"{property.Name} requires a union case.");
                    }
                }
            };
        }
        if (info.Kind == JsonTypeInfoKind.Object && CaseTypes.Contains(info.Type))
        {
            var discriminator = info.CreateJsonPropertyInfo(typeof(string), "case");
            discriminator.Get = _ => info.Type.Name;
            discriminator.Set = (_, value) =>
            {
                if (!string.Equals(value as string, info.Type.Name, StringComparison.Ordinal))
                {
                    throw new JsonException("Unexpected union case.");
                }
            };
            discriminator.IsRequired = true;
            info.Properties.Insert(0, discriminator);
            if (Singletons.TryGetValue(info.Type, out var singleton))
            {
                info.CreateObject = () => singleton;
            }
        }
    }
}
