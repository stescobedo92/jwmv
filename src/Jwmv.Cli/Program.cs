using System.Reflection;
using Jwmv.Cli.Commands;
using Jwmv.Cli.Infrastructure;
using Jwmv.Core.Exceptions;
using Jwmv.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Jwmv.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 1 && (string.Equals(args[0], "--version", StringComparison.OrdinalIgnoreCase) || string.Equals(args[0], "-v", StringComparison.OrdinalIgnoreCase) || string.Equals(args[0], "version", StringComparison.OrdinalIgnoreCase)))
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "dev";
            Console.WriteLine($"jwmv {version}");
            return 0;
        }

        if (args.Length == 1 && string.Equals(args[0], "--current", StringComparison.OrdinalIgnoreCase))
        {
            args = ["current"];
        }

        var console = CreateConsole();
        var services = new ServiceCollection();
        services.AddSingleton<IAnsiConsole>(console);
        services.AddJwmvInfrastructure();

        var registrar = new TypeRegistrar(services);
        var app = new CommandApp(registrar);
        app.Configure(config =>
        {
            config.Settings.Console = console;
            config.SetApplicationName("jwmv");
            config.SetApplicationVersion(Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "dev");
            config.PropagateExceptions();

            config.AddCommand<ListCommand>("list")
                .WithAlias("ls")
                .WithDescription("Lists available SDK versions and local installations.")
                .WithExample("list")
                .WithExample("list", "gradle")
                .WithExample("list", "21-tem")
                .WithExample("list", "--refresh");

            config.AddCommand<CandidatesCommand>("candidates")
                .WithDescription("Lists supported SDK candidates.")
                .WithExample("candidates")
                .WithExample("candidates", "java")
                .WithExample("candidates", "--interactive");

            config.AddCommand<InstallCommand>("install")
                .WithDescription("Installs an SDK by candidate and version, preserving Java shorthand compatibility.")
                .WithExample("install", "21-tem")
                .WithExample("install", "gradle", "9.5.1")
                .WithExample("install", "java", "21.0.4-tem", "--default");

            config.AddCommand<UninstallCommand>("uninstall")
                .WithAlias("remove")
                .WithAlias("delete")
                .WithAlias("rm")
                .WithDescription("Removes an installed SDK version.")
                .WithExample("uninstall")
                .WithExample("uninstall", "21.0.4.7-tem")
                .WithExample("uninstall", "gradle", "9.5.1");

            config.AddCommand<InstalledCommand>("installed")
                .WithAlias("local")
                .WithDescription("Shows only the SDK versions installed on this machine.")
                .WithExample("installed")
                .WithExample("installed", "java");

            config.AddCommand<UseCommand>("use")
                .WithDescription("Emits a PowerShell activation script for a session-local SDK switch.")
                .WithExample("use", "21-tem")
                .WithExample("use", "gradle", "9.5.1")
                .WithExample("use", "21-tem", "--shell", "powershell");

            config.AddCommand<DefaultCommand>("default")
                .WithDescription("Sets the default SDK version for new Windows sessions.")
                .WithExample("default", "21-tem")
                .WithExample("default", "maven", "3.9.15");

            config.AddCommand<CurrentCommand>("current")
                .WithDescription("Shows the currently active SDK resolution.")
                .WithExample("current")
                .WithExample("current", "java");

            config.AddCommand<DoctorCommand>("doctor")
                .WithDescription("Inspects PATH, JAVA_HOME, PowerShell integration, and Java command precedence.")
                .WithExample("doctor");

            config.AddCommand<HomeCommand>("home")
                .WithDescription("Prints the home directory for the current or requested SDK version.")
                .WithExample("home")
                .WithExample("home", "17-tem")
                .WithExample("home", "gradle", "9.5.1");

            config.AddCommand<EnvCommand>("env")
                .WithDescription("Prints project activation scripts or the PowerShell profile bootstrap.")
                .WithExample("env")
                .WithExample("env", "--init")
                .WithExample("env", "--shell", "powershell");

            config.AddCommand<IntegrateCommand>("integrate")
                .WithDescription("Writes the PowerShell bootstrap into your profile so jwmv works like a shell function.")
                .WithExample("integrate")
                .WithExample("integrate", "--profile", "C:\\Users\\me\\Documents\\PowerShell\\Microsoft.PowerShell_profile.ps1");

            config.AddCommand<UpdateCommand>("update")
                .WithDescription("Refreshes the local SDK catalog cache.")
                .WithExample("update");

            config.AddCommand<SelfUpdateCommand>("selfupdate")
                .WithAlias("self-update")
                .WithDescription("Updates jwmv from the latest GitHub Release for this architecture.")
                .WithExample("selfupdate", "--check")
                .WithExample("selfupdate", "--repository", "owner/repo");

            config.AddCommand<UpgradeCommand>("upgrade")
                .WithDescription("Installs the latest package in the same major/vendor track as an installed SDK version.")
                .WithExample("upgrade")
                .WithExample("upgrade", "21.0.2.13-tem")
                .WithExample("upgrade", "gradle");

            config.AddCommand<FlushCommand>("flush")
                .WithDescription("Clears temporary files, archives, and/or the package catalog cache.")
                .WithExample("flush", "--catalog")
                .WithExample("flush", "--temp", "--archives");

            config.AddCommand<ConfigCommand>("config")
                .WithDescription("Displays the effective jwmv configuration and filesystem layout.")
                .WithExample("config");
        });

        try
        {
            return app.Run(args);
        }
        catch (JwmvException exception)
        {
            console.MarkupLine($"[red]{Markup.Escape(exception.Message)}[/]");
            return -1;
        }
        catch (Exception exception)
        {
            console.WriteException(exception, ExceptionFormats.ShortenEverything);
            return -99;
        }
    }

    private static IAnsiConsole CreateConsole()
    {
        var noColor = Environment.GetEnvironmentVariable("NO_COLOR") is not null;
        return AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = noColor ? AnsiSupport.No : AnsiSupport.Yes,
            ColorSystem = noColor ? ColorSystemSupport.NoColors : ColorSystemSupport.TrueColor,
            Interactive = Console.IsInputRedirected || Console.IsOutputRedirected ? InteractionSupport.No : InteractionSupport.Yes
        });
    }
}
