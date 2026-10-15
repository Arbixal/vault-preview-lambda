using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using YamlDotNet.Serialization;

namespace VaultPreviewLambda.Tests;

internal static class OpenApiContractValidator
{
    private static readonly Lazy<ContractDefinition> _contract = new(_loadContract);

    public static void AssertValid(string json, string responseType)
    {
        JsonSchema schema = _contract.Value.GetSchema(responseType);
        using JsonDocument response = JsonDocument.Parse(json);
        EvaluationResults result = schema.Evaluate(
            response.RootElement,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = true,
                IncludeApplicatorErrors = true
            });

        Xunit.Assert.True(
            result.IsValid,
            $"Generated {responseType} response failed OpenAPI validation.{Environment.NewLine}{_formatErrors(result)}");
    }

    private static ContractDefinition _loadContract()
    {
        string contractPath = _findContractPath();
        IDeserializer deserializer = new DeserializerBuilder()
            .WithAttemptingUnquotedStringTypeDeserialization()
            .Build();
        object? yamlObject = deserializer.Deserialize<object>(File.ReadAllText(contractPath));
        JsonObject openApi = JsonNode.Parse(JsonSerializer.Serialize(yamlObject))!.AsObject();
        JsonObject schemas = openApi["components"]!["schemas"]!.AsObject();

        BuildOptions buildOptions = new()
        {
            Dialect = Json.Schema.OpenApi.Dialect.OpenApi_31
        };
        Json.Schema.OpenApi.MetaSchemas.Register(buildOptions);

        return new ContractDefinition(schemas, buildOptions);
    }

    private static string _findContractPath()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            string contractPath = Path.Combine(
                directory.FullName,
                "contracts", "v1", "openapi.yaml");
            if (File.Exists(contractPath))
                return contractPath;
        }

        throw new FileNotFoundException("Could not locate contracts/v1/openapi.yaml.");
    }

    private static string _formatErrors(EvaluationResults result)
    {
        List<string> errors = [];
        foreach (EvaluationResults detail in _flatten(result))
        {
            if (detail.Errors == null)
                continue;

            foreach (KeyValuePair<string, string> error in detail.Errors)
            {
                errors.Add(
                    $"{detail.InstanceLocation} [{error.Key}] {error.Value} " +
                    $"(schema: {detail.EvaluationPath})");
            }
        }

        return errors.Count == 0
            ? $"Validation failed at {result.InstanceLocation}."
            : string.Join(Environment.NewLine, errors);
    }

    private static IEnumerable<EvaluationResults> _flatten(EvaluationResults result)
    {
        yield return result;

        if (result.Details == null)
            yield break;

        foreach (EvaluationResults detail in result.Details)
        {
            foreach (EvaluationResults nested in _flatten(detail))
                yield return nested;
        }
    }

    private sealed class ContractDefinition(
        JsonObject schemas,
        BuildOptions buildOptions)
    {
        private readonly ConcurrentDictionary<string, JsonSchema> _compiledSchemas = new();

        public JsonSchema GetSchema(string responseType) =>
            _compiledSchemas.GetOrAdd(responseType, _buildSchema);

        private JsonSchema _buildSchema(string responseType)
        {
            if (!schemas.TryGetPropertyValue(responseType, out JsonNode? schema) || schema == null)
            {
                throw new InvalidOperationException(
                    $"OpenAPI schema '{responseType}' was not found in components.schemas.");
            }

            JsonNode inlinedSchema = _inlineSchema(schema, schemas, []);
            return JsonSchema.FromText(inlinedSchema.ToJsonString(), buildOptions);
        }
    }

    private static JsonNode _inlineSchema(
        JsonNode node,
        JsonObject schemas,
        HashSet<string> activeReferences)
    {
        if (node is JsonObject objectNode)
        {
            if (objectNode.TryGetPropertyValue("$ref", out JsonNode? referenceNode) &&
                referenceNode?.GetValue<string>() is string reference &&
                reference.StartsWith("#/components/schemas/", StringComparison.Ordinal))
            {
                const string prefix = "#/components/schemas/";
                string name = reference[prefix.Length..];
                if (!activeReferences.Add(name))
                    throw new InvalidOperationException($"Cyclic schema reference: {reference}");

                if (!schemas.TryGetPropertyValue(name, out JsonNode? referencedSchema) ||
                    referencedSchema == null)
                {
                    throw new InvalidOperationException($"Unknown schema reference: {reference}");
                }

                JsonNode inlined = _inlineSchema(referencedSchema, schemas, activeReferences);
                activeReferences.Remove(name);

                if (objectNode.Count == 1)
                    return inlined;

                if (inlined is not JsonObject inlinedObject)
                {
                    throw new InvalidOperationException(
                        $"Schema reference has siblings but is not an object: {reference}");
                }

                foreach (KeyValuePair<string, JsonNode?> property in objectNode)
                {
                    if (property.Key == "$ref")
                        continue;

                    inlinedObject[property.Key] = property.Value == null
                        ? null
                        : _inlineSchema(property.Value, schemas, activeReferences);
                }

                return inlinedObject;
            }

            JsonObject result = new();
            foreach (KeyValuePair<string, JsonNode?> property in objectNode)
            {
                result[property.Key] = property.Value == null
                    ? null
                    : _inlineSchema(property.Value, schemas, activeReferences);
            }

            return result;
        }

        if (node is JsonArray arrayNode)
        {
            JsonArray result = [];
            foreach (JsonNode? item in arrayNode)
            {
                result.Add(item == null ? null : _inlineSchema(item, schemas, activeReferences));
            }

            return result;
        }

        return node.DeepClone();
    }
}
