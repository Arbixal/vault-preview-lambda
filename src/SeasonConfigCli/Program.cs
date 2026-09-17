using System;
using Amazon;
using Amazon.S3;
using Amazon.Lambda;
using VaultShared.Seasons;
using VaultPreview.SeasonConfigurationInfrastructure;

namespace SeasonConfigCli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        string command = args[0].ToLowerInvariant();
        string? awsRegion = Environment.GetEnvironmentVariable("AWS_REGION");
        string? activationFunctionName = Environment.GetEnvironmentVariable("VAULT_PREVIEW_ACTIVATION_FUNCTION_NAME");
        RegionEndpoint? region = null;
        if (!string.IsNullOrEmpty(awsRegion))
        {
            region = RegionEndpoint.GetBySystemName(awsRegion);
        }

        IAmazonS3 s3Client = new AmazonS3Client(region);
        ISeasonRevisionStore store = new S3SeasonRevisionProvider(s3Client);
        ILambdaInvoker? lambdaInvoker = null;
        if (!string.IsNullOrEmpty(activationFunctionName) && region != null)
        {
            lambdaInvoker = new LambdaInvoker(new AmazonLambdaClient(region));
        }

        SeasonConfigurationCli cli = new(store, lambdaInvoker, activationFunctionName);

        return command switch
        {
            "validate" => await cli.ValidateAsync(args[1]),
            "publish" => await cli.PublishAsync(args[1], args[2], args[3]),
            "activate" => await ParseActivationAtAndExecute(
                args, cli.ActivateAsync),
            "schedule" => await cli.ScheduleAsync(args[1], args[2], ParseActivationAt(args[3])),
            "rollback" => await ParseActivationAtAndExecute(
                args, cli.RollbackAsync),
            "cancel" => await cli.CancelAsync(),
            _ => PrintUnknownCommand(command)
        };
    }

    private static async Task<int> ParseActivationAtAndExecute(
        string[] args,
        Func<string, string, DateTimeOffset?, CancellationToken, Task<int>> executor)
    {
        DateTimeOffset? activationAt = null;
        if (args.Length > 4 && !string.IsNullOrEmpty(args[4]))
        {
            if (DateTimeOffset.TryParse(args[4], out DateTimeOffset parsed))
            {
                activationAt = parsed;
            }
            else
            {
                Console.Error.WriteLine($"Invalid activation timestamp: {args[4]}");
                return 1;
            }
        }
        return await executor(args[1], args[2], activationAt, CancellationToken.None);
    }

    private static DateTimeOffset ParseActivationAt(string value)
    {
        if (DateTimeOffset.TryParse(value, out DateTimeOffset result))
        {
            return result;
        }
        throw new ArgumentException($"Invalid activation timestamp: {value}");
    }

    private static int PrintUsage()
    {
        Console.WriteLine("Usage: season-config <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  validate <source-file>              Validate a season configuration source file");
        Console.WriteLine("  publish <source-file> <season-id> <revision-id>  Publish a revision to S3");
        Console.WriteLine("  activate <season-id> <revision-id> [activation-at]  Activate a revision immediately");
        Console.WriteLine("  schedule <season-id> <revision-id> <activation-at>  Schedule a future activation");
        Console.WriteLine("  rollback <season-id> <revision-id> [activation-at]  Rollback to a revision");
        Console.WriteLine("  cancel                              Cancel pending schedule");
        return 1;
    }

    private static int PrintUnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        return PrintUsage();
    }
}
