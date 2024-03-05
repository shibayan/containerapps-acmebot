using System.Reflection;

namespace ContainerApps.Acmebot.Internal;

internal static class Constants
{
    public static string ApplicationVersion { get; } = typeof(Startup).Assembly
                                                                      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                                                                      ?.InformationalVersion;
}
