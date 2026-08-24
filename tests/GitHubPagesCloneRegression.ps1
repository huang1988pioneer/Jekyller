param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

$paths = @{
    ViewModel = Join-Path $RepositoryRoot "ViewModels\GitHubViewModel.cs"
    SetupViewModel = Join-Path $RepositoryRoot "ViewModels\SetupViewModel.cs"
    Service = Join-Path $RepositoryRoot "Services\GitHubService.cs"
    View = Join-Path $RepositoryRoot "Views\GitHubView.axaml"
    SetupView = Join-Path $RepositoryRoot "Views\SetupView.axaml"
}

foreach ($path in $paths.Values) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing file: $path"
    }
}

$viewModel = Get-Content -Raw -LiteralPath $paths.ViewModel
$setupViewModel = Get-Content -Raw -LiteralPath $paths.SetupViewModel
$service = Get-Content -Raw -LiteralPath $paths.Service
$view = Get-Content -Raw -LiteralPath $paths.View
$setupView = Get-Content -Raw -LiteralPath $paths.SetupView

if ($service.IndexOf("CloneRepositoryAsync", [System.StringComparison]::Ordinal) -lt 0) {
    throw "GitHubService must expose clone-to-local."
}

if ($service.IndexOf("ListPagesSitesAsync", [System.StringComparison]::Ordinal) -lt 0) {
    throw "GitHubService must list GitHub Pages sites."
}

if ($service.IndexOf("GitHubPagesUrl.TryConvertToRepositoryUrl", [System.StringComparison]::Ordinal) -lt 0) {
    throw "Repository parsing must accept GitHub Pages site URLs."
}

if ($viewModel.IndexOf("CloneFromGitHubAsync", [System.StringComparison]::Ordinal) -lt 0) {
    throw "GitHub page must clone a Pages site when there is no local project."
}

if ($view.IndexOf("CloneFromGitHubCommand", [System.StringComparison]::Ordinal) -lt 0) {
    throw "GitHub page must offer cloning to local."
}

if ($view.IndexOf("ListPagesSitesCommand", [System.StringComparison]::Ordinal) -lt 0) {
    throw "GitHub page must list the users Pages sites."
}

if ($setupViewModel.IndexOf("CloneFromGitHubAsync", [System.StringComparison]::Ordinal) -lt 0) {
    throw "Setup page must clone a GitHub Pages site to local."
}

if ($setupView.IndexOf("CloneFromGitHubCommand", [System.StringComparison]::Ordinal) -lt 0) {
    throw "Setup page must show clone-from-GitHub Pages."
}

Write-Output "GITHUB_PAGES_CLONE_REGRESSION_OK"
