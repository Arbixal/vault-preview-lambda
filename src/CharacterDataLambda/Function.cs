using System.Net;
using System.Net.Http;
using Amazon.Lambda.Core;
using Amazon.Lambda.Annotations;
using VaultPreview.Blizzard;
using VaultPreview.VaultCache;
using VaultPreview.VaultCache.Models;
using VaultShared.Seasons;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace CharacterDataLambda;

public class Function
{
    private readonly IBlizzardApiHandler _blizzardApiHandler;
    private readonly IVaultCacheHandler _vaultCacheHandler;
    private readonly IActiveSeasonRevisionProvider _activeSeasonRevisionProvider;

    public Function(
        IBlizzardApiHandler blizzardApiHandler,
        IVaultCacheHandler vaultCacheHandler,
        IActiveSeasonRevisionProvider activeSeasonRevisionProvider
        )
    {
        _blizzardApiHandler = blizzardApiHandler;
        _vaultCacheHandler = vaultCacheHandler;
        _activeSeasonRevisionProvider = activeSeasonRevisionProvider;
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
        ActiveSeasonRevision? activeSeason = await _activeSeasonRevisionProvider.GetActive();
        if (activeSeason == null)
        {
            Console.WriteLine("No active season revision is configured; skipping baseline refresh.");
            return "0k";
        }

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
            Dictionary<int,int> response;
            try
            {
                response = await _blizzardApiHandler.GetDelveStatistics(
                    characterData.Region!, characterData.Realm!, characterData.Name!);
            }
            catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                Console.WriteLine($"Character '{characterData.FullName}' was not found by Blizzard. Removing its stale cache entry.");
                await _vaultCacheHandler.DeleteCharacter(
                    characterData.Region!, characterData.Realm!, characterData.Name!);
                continue;
            }

            characterData.SetDelveBaseline(response, activeSeason);

            // Save to S3
            Console.WriteLine($"Saving Character '{characterData.FullName}'.");
            await _vaultCacheHandler.SaveCharacter(characterData);
        }

        return "0k";
    }
}
