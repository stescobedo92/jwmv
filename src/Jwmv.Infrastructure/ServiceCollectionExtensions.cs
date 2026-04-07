using Jwmv.Core.Abstractions;
using Jwmv.Infrastructure.Catalog;
using Jwmv.Infrastructure.Compression;
using Jwmv.Infrastructure.Net;
using Jwmv.Infrastructure.Runtime;
using Jwmv.Infrastructure.Shell;
using Jwmv.Infrastructure.Services;
using Jwmv.Infrastructure.Storage;
using Jwmv.Infrastructure.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Jwmv.Infrastructure;

public static class ServiceCollectionExtensions
{
    public const string FoojayClientName = "foojay";
    public const string GitHubClientName = "github";

    public static IServiceCollection AddJwmvInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IAppContext, DefaultAppContext>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<JwmvPaths>();
        services.AddSingleton<IConfigStore, JsonConfigStore>();
        services.AddSingleton<ICatalogCacheStore, JsonCatalogCacheStore>();
        services.AddSingleton<IJavaInstallationStore, JsonJavaInstallationStore>();
        services.AddSingleton<IArchiveExtractor, ZipArchiveExtractor>();
        services.AddSingleton<IWindowsEnvironmentService, WindowsEnvironmentService>();
        services.AddSingleton<IShellProfileIntegrationService, ShellProfileIntegrationService>();
        services.AddSingleton<IJavaVersionManager, JavaVersionManager>();
        services.AddSingleton<ISelfUpdateService, SelfUpdateService>();
        services.AddHttpClient(FoojayClientName, client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("jwmv/1.0");
            client.Timeout = TimeSpan.FromSeconds(90);
        });
        services.AddHttpClient(GitHubClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("jwmv/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.Timeout = TimeSpan.FromSeconds(90);
        });
        services.AddSingleton<IJavaCatalogClient, FoojayCatalogClient>();
        services.AddSingleton<IArchiveDownloader, HttpArchiveDownloader>();
        return services;
    }
}
