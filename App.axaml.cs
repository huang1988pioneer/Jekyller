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
            var environment = new EnvironmentService(process);
            var jekyll = new JekyllService(process);
            var config = new ConfigService();
            var chirpy = new ChirpyConfigService(config);
            var themes = new ThemeService(process, config);
            var content = new ContentService();
            var github = new GitHubService(process);
            var markdown = new MarkdownPreviewService();
            _serve = new JekyllServeService();

            var home = new HomeViewModel(project, dialogs, jekyll);
            var setup = new SetupViewModel(environment, jekyll, dialogs, project);
            var configVm = new ConfigViewModel(config, project, dialogs);
            var themeVm = new ThemeViewModel(themes, config, chirpy, project, dialogs);
            var contentVm = new ContentViewModel(content, project, dialogs, markdown);
            var previewVm = new PreviewViewModel(_serve, project, dialogs);
            var githubVm = new GitHubViewModel(github, project, dialogs);

            var main = new MainViewModel(project, home, setup, configVm, themeVm, contentVm, previewVm, githubVm);

            desktop.MainWindow = new MainWindow
            {
                DataContext = main
            };

            desktop.Exit += (_, _) =>
            {
                try { _serve?.Dispose(); }
                catch { /* ignore */ }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
