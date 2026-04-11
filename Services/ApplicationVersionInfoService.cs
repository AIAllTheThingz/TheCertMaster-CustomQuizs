using System.Reflection;

namespace QuizAPI.Services;

public sealed record ApplicationVersionInfo(
    string ApplicationName,
    string EnvironmentName,
    string Version,
    string InformationalVersion,
    string ReleaseLabel,
    string BuildStamp);

public sealed class ApplicationVersionInfoService
{
    private readonly IWebHostEnvironment _environment;

    public ApplicationVersionInfoService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public ApplicationVersionInfo CreateVersionPayload()
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        var version = entryAssembly?.GetName().Version?.ToString() ?? "unknown";
        var informationalVersion = entryAssembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? version;

        var metadata = entryAssembly?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(a => a.Key, a => a.Value, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        var releaseLabel = metadata.TryGetValue("ReleaseLabel", out var label) ? (label ?? string.Empty) : string.Empty;
        var buildStamp = metadata.TryGetValue("BuildStamp", out var stamp) ? (stamp ?? string.Empty) : string.Empty;

        return new ApplicationVersionInfo(
            _environment.ApplicationName,
            _environment.EnvironmentName,
            version,
            informationalVersion,
            releaseLabel,
            buildStamp);
    }
}
