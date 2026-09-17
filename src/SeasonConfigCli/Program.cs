using Amazon;
using Amazon.Lambda;
using Amazon.S3;
using Amazon.Scheduler;
using VaultPreview.SeasonConfigurationInfrastructure;
using VaultShared.Seasons;

namespace SeasonConfigCli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
            return _printUsage();

        string command = args[0].ToLowerInvariant();
        if (command == "validate")
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("validate requires exactly one source-file argument.");
                return _printUsage();
            }

            return await new SeasonConfigurationCli(null).ValidateAsync(args[1]);
        }

        if (command != "cancel" && args.Length < 2)
        {
            Console.Error.WriteLine($"{command} is missing required arguments.");
            return _printUsage();
        }

        string? awsRegion = Environment.GetEnvironmentVariable("AWS_REGION");
        if (string.IsNullOrWhiteSpace(awsRegion))
        {
            Console.Error.WriteLine("AWS_REGION is required for AWS-backed operations.");
            return 1;
        }

        RegionEndpoint region = RegionEndpoint.GetBySystemName(awsRegion);
        string? activationFunctionName = Environment.GetEnvironmentVariable(
            "VAULT_PREVIEW_ACTIVATION_FUNCTION_NAME");
        string? activationFunctionArn = Environment.GetEnvironmentVariable(
            "VAULT_PREVIEW_ACTIVATION_FUNCTION_ARN");
        string? schedulerRoleArn = Environment.GetEnvironmentVariable(
            "VAULT_PREVIEW_SCHEDULER_ROLE_ARN");
        string? schedulerGroupName = Environment.GetEnvironmentVariable(
            "VAULT_PREVIEW_SCHEDULER_GROUP");
        string? schedulerDeadLetterQueueArn = Environment.GetEnvironmentVariable(
            "VAULT_PREVIEW_SCHEDULER_DEAD_LETTER_QUEUE_ARN");

        IAmazonS3 s3Client = new AmazonS3Client(region);
        ISeasonRevisionStore store = new S3SeasonRevisionProvider(s3Client);

        string? activationFunctionTarget = !string.IsNullOrWhiteSpace(activationFunctionName)
            ? activationFunctionName
            : activationFunctionArn;
        ILambdaInvoker? lambdaInvoker = string.IsNullOrWhiteSpace(activationFunctionTarget)
            ? null
            : new LambdaInvoker(new AmazonLambdaClient(region));
        IScheduler scheduler = new EventBridgeScheduler(new AmazonSchedulerClient(region));

        SeasonConfigurationCli cli = new(
            store,
            lambdaInvoker,
            scheduler,
            new SeasonConfigurationCliOptions(
                activationFunctionTarget,
                activationFunctionArn,
                schedulerRoleArn,
                schedulerGroupName,
                schedulerDeadLetterQueueArn));

        try
        {
            return command switch
            {
                "publish" when args.Length == 4 => await cli.PublishAsync(args[1], args[2], args[3]),
                "activate" when args.Length is 3 or 4 => await _parseTimestampAndExecute(
                    args,
                    cli.ActivateAsync),
                "schedule" when args.Length == 4 => await cli.ScheduleAsync(
                    args[1],
                    args[2],
                    _parseTimestamp(args[3])),
                "rollback" when args.Length is 3 or 4 => await _parseTimestampAndExecute(
                    args,
                    cli.RollbackAsync),
                "cancel" when args.Length == 1 => await cli.CancelAsync(),
                _ => _printUsage()
            };
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task<int> _parseTimestampAndExecute(
        string[] args,
        Func<string, string, DateTimeOffset?, CancellationToken, Task<int>> executor)
    {
        DateTimeOffset? activationAt = null;
        if (args.Length == 4)
        {
            activationAt = _parseTimestamp(args[3]);
        }

        return await executor(args[1], args[2], activationAt, CancellationToken.None);
    }

    private static DateTimeOffset _parseTimestamp(string value)
    {
        if (DateTimeOffset.TryParse(value, out DateTimeOffset result))
            return result;

        throw new ArgumentException($"Invalid activation timestamp: {value}");
    }

    private static int _printUsage()
    {
        Console.WriteLine("Usage: season-config <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  validate <source-file>                         Validate without AWS access");
        Console.WriteLine("  publish <source-file> <season-id> <revision-id> Publish a revision to S3");
        Console.WriteLine("  activate <season-id> <revision-id> [at]        Activate through Lambda");
        Console.WriteLine("  schedule <season-id> <revision-id> <at>        Schedule one-time activation");
        Console.WriteLine("  rollback <season-id> <revision-id> [at]        Roll back through Lambda");
        Console.WriteLine("  cancel                                         Cancel pending schedule");
        return 1;
    }
}
