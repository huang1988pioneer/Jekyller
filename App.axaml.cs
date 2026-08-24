using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Jekyller.Services;
using Jekyller.ViewModels;
using Jekyller.Views;

namespace Jekyller;

public partial class App : Application
{
    private IJekyllServeService? _serve;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var process = new ProcessRunner();
            var project = new ProjectContext();
            var dialogs = new DialogService();
            var settings = new SettingsService();
            settings.Load();
            var environment = new EnvironmentService(process);
            var jekyll = new JekyllService(process);
            var config = new ConfigService();
            var chirpy = new ChirpyConfigService(config);
            var themes = new ThemeService(process, config);
            var content = new ContentService();
            var frontMatter = new FrontMatterService();
            var deploymentMonitor = new DeploymentMonitorService();
            var github = new GitHubService(process, config, deploymentMonitor);
            _serve = new JekyllServeService();

            project.ProjectChanged += (_, _) =>
            {
                if (project.HasProject)
                    settings.RememberOpenedProject(project.ProjectPath!);
            };

            var home = new HomeViewModel(project, dialogs, jekyll, settings);
            var setup = new SetupViewModel(environment, jekyll, dialogs, project, github, settings);
            var configVm = new ConfigViewModel(config, project, dialogs);
            var themeVm = new ThemeViewModel(themes, config, chirpy, project, dialogs);
            var contentVm = new ContentViewModel(content, project, dialogs, frontMatter, settings);
            var previewVm = new PreviewViewModel(_serve, project, dialogs);
            var githubVm = new GitHubViewModel(github, jekyll, project, dialogs, deploymentMonitor, settings);

            var autoOpenPath = settings.GetAutoOpenProjectPath();
            if (!string.IsNullOrWhiteSpace(autoOpenPath))
                project.SetProject(autoOpenPath);

            var main = new MainViewModel(project, home, setup, configVm, themeVm, contentVm, previewVm, githubVm);

            desktop.MainWindow = new MainWindow
            {
                DataContext = main
            };

            desktop.Exit += (_, _) =>
            {
                try { _serve?.Dispose(); }
                catch { /* ignore */ }
                try { githubVm.Dispose(); }
                catch { /* ignore */ }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
