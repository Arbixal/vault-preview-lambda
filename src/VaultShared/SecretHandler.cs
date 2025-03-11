using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;

namespace VaultShared;

public interface ISecretHandler
{
    Task<string?> GetSecret(string secretId);
    Task<long> GetSecretAsLong(string secretId);
    Task PutSecret<T>(string secretId, T secretValue);
}

public class SecretHandler: ISecretHandler
{
    private IAmazonSimpleSystemsManagement _systemsManagement { get; } = new AmazonSimpleSystemsManagementClient();

    public async Task<string?> GetSecret(string secretId)
    {
        GetParameterRequest secretRequest = new GetParameterRequest() { Name = secretId, WithDecryption = true };
        GetParameterResponse secretResponse = await _systemsManagement.GetParameterAsync(secretRequest);
        
        return string.IsNullOrWhiteSpace(secretResponse.Parameter.Value) ? null : secretResponse.Parameter.Value;
    }

    public async Task<long> GetSecretAsLong(string secretId)
    {
        string? secret = await GetSecret(secretId);

        return secret != null ? Convert.ToInt64(secret) : 0;
    }

    public async Task PutSecret<T>(string secretId, T secretValue)
    {
        if (secretValue == null)
            return;
        
        PutParameterRequest secretRequest = new PutParameterRequest()
            { Name = secretId, Overwrite = true, Value = secretValue.ToString() };
        PutParameterResponse secretResponse = await _systemsManagement.PutParameterAsync(secretRequest);
    }
}