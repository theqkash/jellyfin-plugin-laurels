using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Laurels.Sections;

/// <summary>
/// Registers our sections with the Home Screen Sections plugin (IAmParadox27) through its documented
/// reflection API: PluginInterface.RegisterSection(JObject). The JObject is created with the Newtonsoft
/// assembly that plugin itself uses, so the type identity matches whatever load context it lives in.
/// </summary>
public static class HomeSectionsBridge
{
    private const string AssemblyNameFragment = ".HomeScreenSections";
    private const string PluginInterfaceTypeName = "Jellyfin.Plugin.HomeScreenSections.PluginInterface";

    public static bool IsInstalled() => FindAssembly() is not null;

    public static int RegisterAll(IEnumerable<string> sectionKeys, ILogger logger)
    {
        var assembly = FindAssembly();
        var registerMethod = assembly?.GetType(PluginInterfaceTypeName, throwOnError: false)?.GetMethod("RegisterSection", BindingFlags.Public | BindingFlags.Static);
        if (assembly is null || registerMethod is null)
        {
            logger.LogInformation("Laurels: Home Screen Sections plugin not found; no home screen sections registered.");
            return 0;
        }

        var payloadType = registerMethod.GetParameters()[0].ParameterType;
        var parse = payloadType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);
        if (parse is null)
        {
            logger.LogWarning("Laurels: Home Screen Sections API changed (no JObject.Parse); sections disabled.");
            return 0;
        }

        var registered = 0;
        foreach (var key in sectionKeys)
        {
            var definition = SectionCatalog.Resolve(key);
            if (definition is null)
            {
                logger.LogWarning("Laurels: unknown home section key '{Key}'", key);
                continue;
            }

            var payload = new Dictionary<string, object?>
            {
                ["id"] = SectionCatalog.SectionId(definition.Key),
                ["displayText"] = definition.DisplayText,
                ["limit"] = 1,
                ["additionalData"] = definition.Key,
                ["resultsAssembly"] = typeof(SectionResultsHandler).Assembly.FullName,
                ["resultsClass"] = typeof(SectionResultsHandler).FullName,
                ["resultsMethod"] = nameof(SectionResultsHandler.GetResults),
            };

            try
            {
                var jobject = parse.Invoke(null, [JsonSerializer.Serialize(payload)]);
                registerMethod.Invoke(null, [jobject]);
                registered++;
            }
            catch (Exception ex) when (ex is TargetInvocationException or ArgumentException)
            {
                logger.LogError(ex, "Laurels: registering section '{Key}' failed", key);
            }
        }

        logger.LogInformation("Laurels: registered {Count} home screen sections.", registered);
        return registered;
    }

    private static Assembly? FindAssembly() =>
        AssemblyLoadContext.All.SelectMany(c => c.Assemblies)
            .FirstOrDefault(a => a.GetName().Name?.EndsWith(AssemblyNameFragment, StringComparison.OrdinalIgnoreCase) == true);
}
