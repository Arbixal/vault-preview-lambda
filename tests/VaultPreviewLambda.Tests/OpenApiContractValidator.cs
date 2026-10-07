using System.Diagnostics;

namespace VaultPreviewLambda.Tests;

internal static class OpenApiContractValidator
{
    public static void AssertValid(string json, string responseType)
    {
        string fixturesDirectory = _findFixturesDirectory();
        string responsePath = Path.Combine(
            Path.GetTempPath(),
            $"vault-preview-contract-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(responsePath, json);
            ProcessStartInfo startInfo = new("node")
            {
                WorkingDirectory = fixturesDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("validate-response.mjs");
            startInfo.ArgumentList.Add(responsePath);
            startInfo.ArgumentList.Add(responseType);

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start the Node contract validator.");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Xunit.Assert.True(
                process.ExitCode == 0,
                $"Generated {responseType} response failed OpenAPI validation.{Environment.NewLine}{output}{error}");
        }
        finally
        {
            File.Delete(responsePath);
        }
    }

    private static string _findFixturesDirectory()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            string fixturesDirectory = Path.Combine(
                directory.FullName,
                "contracts",
                "v1",
                "fixtures");
            if (File.Exists(Path.Combine(fixturesDirectory, "package.json")))
                return fixturesDirectory;
        }

        throw new DirectoryNotFoundException("Could not locate contracts/v1/fixtures.");
    }
}
