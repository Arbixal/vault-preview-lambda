using Amazon.Lambda.Annotations;
using Amazon.Lambda.Annotations.APIGateway;
using Amazon.Lambda.Core;
using VaultPreviewLambda.Models;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace VaultPreviewLambda;

public class Function
{
    private readonly IBlizzardApiHandler _blizzardApiHandler;

    public Function(IBlizzardApiHandler blizzardApiHandler)
    {
        _blizzardApiHandler = blizzardApiHandler;
    }
    
    /// <summary>
    /// A simple function that takes a string and does a ToUpper
    /// </summary>
    /// <param name="characters">Comma-separated list of characters (e.g. bixshift-nagrand,bixsham-nagrand,bixmonk-nagrand)</param>
    /// <returns></returns>
    [LambdaFunction]
    [HttpApi(LambdaHttpMethod.Get,"/vault-progress/{characters}")]
    public async Task<IDictionary<string, CharacterProgress>> FunctionHandler(string characters)
    {
        Dictionary<string, CharacterProgress> progress = new Dictionary<string, CharacterProgress>();

        if (string.IsNullOrEmpty(characters))
        {
            return progress;
        }
        
        string[] characterList = characters.Split(",", StringSplitOptions.TrimEntries);

        foreach (string aCharacter in characterList)
        {
            string[] characterSplit = aCharacter.Split("-");
            
            Console.WriteLine($"{characterSplit[0]} from {characterSplit[1]}");
            progress.Add(aCharacter, new CharacterProgress());
        }

        await _blizzardApiHandler.Connect();

        return progress;
    }
    
    
    /*
     * Aiming for
     * {
     *  "{character-name}": {
     *    "raid": {
     *      "{boss-name}": {
     *        "mythic": false,
     *        "heroic": false,
     *        "normal": false,
     *        "lfr": false,
     *      },
     *    },
     *    "dungeons": [
     *      { "level": {level}, "name": "{dungeon-name}"},
     *    ]
     * }
     */
}
