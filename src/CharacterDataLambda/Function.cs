using Amazon.Lambda.Core;
using Amazon.Lambda.Annotations;
using VaultPreview.Blizzard;
using VaultPreview.VaultCache;
using VaultPreview.VaultCache.Models;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace CharacterDataLambda;

public class Function
{
    private readonly IBlizzardApiHandler _blizzardApiHandler;
    private readonly IVaultCacheHandler _vaultCacheHandler;

    public Function(
        IBlizzardApiHandler blizzardApiHandler,
        IVaultCacheHandler vaultCacheHandler
        )
    {
        _blizzardApiHandler = blizzardApiHandler;
        _vaultCacheHandler = vaultCacheHandler;
    }
    
    /// <summary>
    /// A simple function that takes a string and does a ToUpper.
    /// </summary>
    /// <param name="character">A characters name in the form "(region)-(realm)-(name)".</param>
    /// <param name="context">The ILambdaContext that provides methods for logging and describing the Lambda environment.</param>
    /// <returns></returns>
    [LambdaFunction]
    public async Task<string> FunctionHandler(string character, ILambdaContext context)
    {
        IList<CharacterData> characterList = new List<CharacterData>();
        
        await _blizzardApiHandler.Connect();

        if (string.IsNullOrEmpty(character))
        {
            characterList = await _vaultCacheHandler.GetAllCharacters();
        }
        else
        {
            string[] characterParts = character.Split("-");
            if (characterParts.Length != 3)
                throw new InvalidDataException($"Invoked with invalid character name '{character}'.");
            
            characterList.Add(new CharacterData(characterParts[2], characterParts[1], characterParts[0]));
        }

        foreach (CharacterData characterData in characterList)
        {
            if (!characterData.IsValid)
            {
                Console.WriteLine($"Character '{characterData.FullName}' is not valid.");
                continue;
            }

            Console.WriteLine($"Getting data for Character '{characterData.FullName}'.");
            Dictionary<int,int> response = 
                await _blizzardApiHandler.GetDelveStatistics(characterData.Region, characterData.Realm, characterData.Name);

            characterData.SetDelveData(response);
            
            // Save to S3
            Console.WriteLine($"Saving Character '{characterData.FullName}'.");
            await _vaultCacheHandler.SaveCharacter(characterData);
        }

        return "0k";
    }
}