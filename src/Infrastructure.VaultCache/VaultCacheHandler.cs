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
            ListObjectsResponse listResponse = await s3Client.ListObjectsAsync(new ListObjectsRequest()
            {
                BucketName = _BUCKET_NAME
            });

            foreach (S3Object aFile in listResponse.S3Objects)
            {
                string[] characterParts = aFile.Key.Replace(".json", "").Split("-");
                if (characterParts.Length != 3)
                    continue;
                
                CharacterData aCharacter = new CharacterData(characterParts[2], characterParts[1], characterParts[0])
                    {
                        LastUpdatedTimestamp = new DateTimeOffset(aFile.LastModified).ToUnixTimeMilliseconds()
                    };

                returnData.Add(aCharacter);
            }
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
}