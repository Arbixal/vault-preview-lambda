using System.Net.Http.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using System.Text.Json.Serialization;
using VaultShared;
using VaultShared.Models.Blizzard;

namespace BlizzardTokenHandler;

public class Function
{
    /// <summary>
    /// The main entry point for the Lambda function. The main function is called once during the Lambda init phase. It
    /// initializes the .NET Lambda runtime client passing in the function handler to invoke for each Lambda event and
    /// the JSON serializer to use for converting Lambda JSON format to the .NET types. 
    /// </summary>
    private static async Task Main()
    {
        Func<ILambdaContext, Task<string>> handler = FunctionHandler;
        await LambdaBootstrapBuilder.Create(handler,
                new SourceGeneratorLambdaJsonSerializer<LambdaFunctionJsonSerializerContext>())
            .Build()
            .RunAsync();
    }

    /// <summary>
    /// A simple function that takes a string and does a ToUpper.
    ///
    /// To use this handler to respond to an AWS event, reference the appropriate package from 
    /// https://github.com/aws/aws-lambda-dotnet#events
    /// and change the string input parameter to the desired event type. When the event type
    /// is changed, the handler type registered in the main method needs to be updated and the LambdaFunctionJsonSerializerContext 
    /// defined below will need the JsonSerializable updated. If the return type and event type are different then the 
    /// LambdaFunctionJsonSerializerContext must have two JsonSerializable attributes, one for each type.
    ///
    /// When using Native AOT extra testing with the deployed Lambda functions is required to ensure
    /// the libraries used in the Lambda function work correctly with Native AOT. If a runtime 
    /// error occurs about missing types or methods the most likely solution will be to remove references to trim-unsafe 
    /// code or configure trimming options. This sample defaults to partial TrimMode because currently the AWS 
    /// SDK for .NET does not support trimming. This will result in a larger executable size, and still does not 
    /// guarantee runtime trimming errors won't be hit. 
    /// </summary>
    /// <param name="context">The ILambdaContext that provides methods for logging and describing the Lambda environment.</param>
    /// <returns></returns>
    public static async Task<string> FunctionHandler(ILambdaContext context)
    {
        SecretHandler secretHandler = new SecretHandler();
        HttpClient client = new HttpClient();
        
        string? clientId = await secretHandler.GetSecret("/Blizzard/ClientId");
        string? secret = await secretHandler.GetSecret("/Blizzard/ClientSecret");

        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post,
            "https://oauth.battle.net/token?grant_type=client_credentials");

        string authValue = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{clientId}:{secret}"));
        request.Headers.Add("Authorization", $"Basic {authValue}");

        HttpResponseMessage response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();

        TokenResponse? tokenResponse = await response.Content.ReadFromJsonAsync(LambdaFunctionJsonSerializerContext.Default.TokenResponse);

        if (tokenResponse != null)
        {
            string token = tokenResponse.AccessToken;
            DateTimeOffset expires = DateTimeOffset.Now.AddSeconds(tokenResponse.ExpiresIn);
            await secretHandler.PutSecret("/Blizzard/Token", token);
            await secretHandler.PutSecret("/Blizzard/TokenExpires", expires.ToUnixTimeMilliseconds());
        }

        return "Ok";
    }
}

/// <summary>
/// This class is used to register the input event and return type for the FunctionHandler method with the System.Text.Json source generator.
/// There must be a JsonSerializable attribute for each type used as the input and return type or a runtime error will occur 
/// from the JSON serializer unable to find the serialization information for unknown types.
/// </summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(TokenResponse))]
public partial class LambdaFunctionJsonSerializerContext : JsonSerializerContext
{
    // By using this partial class derived from JsonSerializerContext, we can generate reflection free JSON Serializer code at compile time
    // which can deserialize our class and properties. However, we must attribute this class to tell it what types to generate serialization code for.
    // See https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-source-generation
}