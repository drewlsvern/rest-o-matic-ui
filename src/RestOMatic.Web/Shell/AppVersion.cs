using System.Reflection;

namespace RestOMatic.Web.Shell;

public static class AppVersion
{
    /// <summary>The version this app was built as, for example <c>1.2.3</c> or <c>0.0.0-dev</c>.</summary>
    public static string Current { get; } = FromInformationalVersion(
        typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>
    /// The SDK appends <c>+commit</c> to the informational version when it
    /// can see a git repository. That suffix is build metadata, not part of
    /// the version, so it is dropped.
    /// </summary>
    public static string FromInformationalVersion(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return "unknown";
        }

        var metadata = informationalVersion.IndexOf('+');
        return metadata < 0 ? informationalVersion : informationalVersion[..metadata];
    }
}
