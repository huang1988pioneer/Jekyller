param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

$viewModelPath = Join-Path $RepositoryRoot "ViewModels\GitHubViewModel.cs"
$servicePath = Join-Path $RepositoryRoot "Services\GitHubService.cs"
$viewPath = Join-Path $RepositoryRoot "Views\GitHubView.axaml"

$viewModel = Get-Content -Raw -LiteralPath $viewModelPath
$service = Get-Content -Raw -LiteralPath $servicePath
$view = Get-Content -Raw -LiteralPath $viewPath

if ($viewModel.IndexOf("LookupOwnedRepositoryAsync", [System.StringComparison]::Ordinal) -lt 0) {
    throw "Create new repo must look up an existing GitHub repository before gh repo create."
}

if ($viewModel.IndexOf("CanReuse", [System.StringComparison]::Ordinal) -lt 0) {
    throw "Create new repo must only auto-connect reusable existing repositories."
}

if ($service.IndexOf("LookupOwnedRepositoryAsync", [System.StringComparison]::Ordinal) -lt 0) {
    throw "GitHubService must expose owned-repository lookup."
}

if ($service.IndexOf("LooksLikeNameExistsError", [System.StringComparison]::Ordinal) -lt 0) {
    throw "CreateRepoAndPushAsync must fall back when gh repo create reports the name already exists."
}

if ($view.IndexOf("若 GitHub 上已有同名 repository", [System.StringComparison]::Ordinal) -lt 0) {
    throw "The GitHub page must tell users that an existing Jekyll repo is linked instead of created."
}

Write-Output "GITHUB_CREATE_EXISTING_REPO_REGRESSION_OK"
