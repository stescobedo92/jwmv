using Jwmv.Cli.Commands;
using Jwmv.Cli.Infrastructure;
using Jwmv.Core.Exceptions;
using Jwmv.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Reflection;

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

        var services = new ServiceCollection();
        services.AddSingleton<IAnsiConsole>(AnsiConsole.Console);
        services.AddJwmvInfrastructure();

        var registrar = new TypeRegistrar(services);
        var app = new CommandApp(registrar);
        app.Configure(config =>
        {
            config.SetApplicationName("jwmv");
            config.SetApplicationVersion(Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "dev");
            config.PropagateExceptions();

            config.AddCommand<ListCommand>("list")
                .WithAlias("ls")
                .WithDescription("Lists available Java distributions and local installations.")
                .WithExample("list")
                .WithExample("list", "21-tem")
                .WithExample("list", "--refresh");

            config.AddCommand<InstallCommand>("install")
                .WithDescription("Installs a Java distribution by alias or fuzzy identifier.")
                .WithExample("install", "21-tem")
                .WithExample("install", "21.0.4-tem", "--default");

            config.AddCommand<UninstallCommand>("uninstall")
                .WithAlias("remove")
                .WithAlias("delete")
                .WithAlias("rm")
                .WithDescription("Removes an installed Java version.")
                .WithExample("uninstall")
                .WithExample("uninstall", "21.0.4.7-tem");

            config.AddCommand<InstalledCommand>("installed")
                .WithAlias("local")
                .WithDescription("Shows only the Java versions installed on this machine.")
                .WithExample("installed");

            config.AddCommand<UseCommand>("use")
                .WithDescription("Emits a PowerShell activation script for a session-local Java switch.")
                .WithExample("use", "21-tem")
                .WithExample("use", "21-tem", "--shell", "powershell");

            config.AddCommand<DefaultCommand>("default")
                .WithDescription("Sets the default Java version for new Windows sessions.")
                .WithExample("default", "21-tem");

            config.AddCommand<CurrentCommand>("current")
                .WithDescription("Shows the currently active Java resolution.")
                .WithExample("current");

            config.AddCommand<DoctorCommand>("doctor")
                .WithDescription("Inspects PATH, JAVA_HOME, PowerShell integration, and Java command precedence.")
                .WithExample("doctor");

            config.AddCommand<HomeCommand>("home")
                .WithDescription("Prints the JAVA_HOME for the current or requested Java version.")
                .WithExample("home")
                .WithExample("home", "17-tem");

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
                .WithDescription("Refreshes the local Foojay catalog cache.")
                .WithExample("update");

            config.AddCommand<SelfUpdateCommand>("selfupdate")
                .WithAlias("self-update")
                .WithDescription("Updates jwmv from the latest GitHub Release for this architecture.")
                .WithExample("selfupdate", "--check")
                .WithExample("selfupdate", "--repository", "owner/repo");

            config.AddCommand<UpgradeCommand>("upgrade")
                .WithDescription("Installs the latest package in the same major/vendor track as the installed Java version.")
                .WithExample("upgrade")
                .WithExample("upgrade", "21.0.2.13-tem");

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
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(exception.Message)}[/]");
            return -1;
        }
        catch (Exception exception)
        {
            AnsiConsole.WriteException(exception, ExceptionFormats.ShortenEverything);
            return -99;
        }
    }
}
