using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using VaultPreview.VaultCache.Models;

namespace VaultPreview.VaultCache;

public interface IVaultCacheHandler
{
    Task<CharacterData?> GetCharacter(string region, string realm, string name);
    Task<IList<CharacterData>> GetAllCharacters();
    Task<bool> SaveCharacter(CharacterData characterData);
    Task<bool> DeleteCharacter(string region, string realm, string name);
}

public class VaultCacheHandler(IAmazonS3 s3Client) : IVaultCacheHandler
{
    private const string _BUCKET_NAME = "vault-preview-data";
    private const string _KEY_FORMAT = "{0}-{1}-{2}.json";

    public async Task<CharacterData?> GetCharacter(string region, string realm, string name)
    {
        try
        {
            using GetObjectResponse response = await s3Client.GetObjectAsync(new GetObjectRequest()
            {
                BucketName = _BUCKET_NAME,
                Key = string.Format(_KEY_FORMAT, region, realm, name)
            });

            return JsonSerializer.Deserialize<CharacterData>(response.ResponseStream);
        }
        catch (AmazonS3Exception e)
        {
            Console.WriteLine(e);
            return null;
        }
    }

    public async Task<IList<CharacterData>> GetAllCharacters()
    {
        IList<CharacterData> returnData = new List<CharacterData>();
        try
        {
            string? continuationToken = null;
            do
            {
                ListObjectsV2Response listResponse = await s3Client.ListObjectsV2Async(new ListObjectsV2Request()
                {
                    BucketName = _BUCKET_NAME,
                    Delimiter = "/",
                    ContinuationToken = continuationToken
                });

                foreach (S3Object aFile in listResponse.S3Objects ?? [])
                {
                    if (string.IsNullOrWhiteSpace(aFile.Key) ||
                        aFile.Key.Contains('/') ||
                        !aFile.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        using GetObjectResponse objectResponse = await s3Client.GetObjectAsync(new GetObjectRequest()
                        {
                            BucketName = _BUCKET_NAME,
                            Key = aFile.Key
                        });

                        CharacterData? aCharacter = await JsonSerializer.DeserializeAsync<CharacterData>(
                            objectResponse.ResponseStream);
                        if (aCharacter?.IsValid != true)
                            continue;

                        aCharacter.LastUpdatedTimestamp = aFile.LastModified.HasValue
                            ? new DateTimeOffset(aFile.LastModified.Value).ToUnixTimeMilliseconds()
                            : 0;
                        returnData.Add(aCharacter);
                    }
                    catch (AmazonS3Exception e)
                    {
                        Console.WriteLine(e);
                    }
                    catch (JsonException e)
                    {
                        Console.WriteLine(e);
                    }
                }

                if (listResponse.IsTruncated != true)
                    break;

                continuationToken = listResponse.NextContinuationToken;
                if (string.IsNullOrEmpty(continuationToken))
                    throw new InvalidOperationException("S3 returned a truncated character listing without a continuation token.");
            } while (true);
        }
        catch (AmazonS3Exception e)
        {
            Console.WriteLine(e);
        }

        return returnData;
    }

    public async Task<bool> SaveCharacter(CharacterData characterData)
    {
        try
        {
            using MemoryStream memoryStream = new MemoryStream();
            await JsonSerializer.SerializeAsync(memoryStream, characterData);
            memoryStream.Position = 0;

            PutObjectResponse response = await s3Client.PutObjectAsync(new PutObjectRequest()
            {
                BucketName = _BUCKET_NAME,
                Key = string.Format(_KEY_FORMAT, characterData.Region, characterData.Realm, characterData.Name),
                AutoCloseStream = true,
                InputStream = memoryStream
            });

            return true;
        }
        catch (AmazonS3Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }

    public async Task<bool> DeleteCharacter(string region, string realm, string name)
    {
        try
        {
            await s3Client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _BUCKET_NAME,
                Key = string.Format(_KEY_FORMAT, region, realm, name)
            });

            return true;
        }
        catch (AmazonS3Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }
}
