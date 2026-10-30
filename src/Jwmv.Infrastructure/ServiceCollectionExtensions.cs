using Jwmv.Core.Abstractions;
using Jwmv.Infrastructure.Catalog;
using Jwmv.Infrastructure.Compression;
using Jwmv.Infrastructure.Net;
using Jwmv.Infrastructure.Runtime;
using Jwmv.Infrastructure.Security;
using Jwmv.Infrastructure.Services;
using Jwmv.Infrastructure.Shell;
using Jwmv.Infrastructure.Storage;
using Jwmv.Infrastructure.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Jwmv.Infrastructure;

public static class ServiceCollectionExtensions
{
    public const string FoojayClientName = "foojay";
    public const string GitHubClientName = "github";
    public const string GradleClientName = "gradle";
    public const string MavenCentralClientName = "maven-central";

    public static IServiceCollection AddJwmvInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IAppContext, DefaultAppContext>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<JwmvPaths>();
        services.AddSingleton<IConfigStore, JsonConfigStore>();
        services.AddSingleton<ICatalogCacheStore, JsonCatalogCacheStore>();
        services.AddSingleton<ISdkCatalogCacheStore, JsonSdkCatalogCacheStore>();
        services.AddSingleton<IJavaInstallationStore, JsonJavaInstallationStore>();
        services.AddSingleton<ISdkInstallationStore, JsonSdkInstallationStore>();
        services.AddSingleton<IArchiveExtractor, ZipArchiveExtractor>();
        services.AddSingleton<IWindowsEnvironmentService, WindowsEnvironmentService>();
        services.AddSingleton<ISdkEnvironmentService, SdkEnvironmentService>();
        services.AddSingleton<IShellProfileIntegrationService, ShellProfileIntegrationService>();
        services.AddSingleton<IJavaVersionManager, JavaVersionManager>();
        services.AddSingleton<ISdkVersionManager, SdkVersionManager>();
        services.AddSingleton<IReleaseSignatureVerifier>(_ => new SigstoreBlobVerifier(SigstoreTrustRoot.PublicGood));
        services.AddSingleton<IUpdaterLauncher, PowerShellUpdaterLauncher>();
        services.AddSingleton<ISelfUpdateService, SelfUpdateService>();
        services.AddSingleton<IChecksumVerifier, ChecksumVerifier>();
        services.AddSingleton<ISdkCatalogProvider, FoojaySdkCatalogProvider>();
        services.AddSingleton<ISdkCatalogProvider, GradleCatalogProvider>();
        services.AddSingleton<ISdkCatalogProvider, MavenCatalogProvider>();
        services.AddSingleton<ISdkCatalogProvider, KotlinCatalogProvider>();
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
        services.AddHttpClient(GradleClientName, client =>
        {
            client.BaseAddress = new Uri("https://services.gradle.org/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("jwmv/1.0");
            client.Timeout = TimeSpan.FromSeconds(90);
        });
        services.AddHttpClient(MavenCentralClientName, client =>
        {
            client.BaseAddress = new Uri("https://repo.maven.apache.org/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("jwmv/1.0");
            client.Timeout = TimeSpan.FromSeconds(90);
        });
        services.AddSingleton<IJavaCatalogClient, FoojayCatalogClient>();
        services.AddSingleton<IArchiveDownloader, HttpArchiveDownloader>();
        return services;
    }
}
