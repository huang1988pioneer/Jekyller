namespace Jekyller.Models;

/// <summary>Form model for jekyll-theme-chirpy _config.yml fields.</summary>
public sealed class ChirpySettings
{
    // Site
    public string Title { get; set; } = string.Empty;
    public string Tagline { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string Lang { get; set; } = "en";
    public string Timezone { get; set; } = "Asia/Taipei";

    // Appearance
    public string ThemeMode { get; set; } = string.Empty; // light | dark | (empty = system)
    public string Avatar { get; set; } = string.Empty;
    public string Cdn { get; set; } = string.Empty;
    public string SocialPreviewImage { get; set; } = string.Empty;
    public bool Toc { get; set; } = true;
    public int Paginate { get; set; } = 10;

    // Social / SEO author
    public string GithubUsername { get; set; } = string.Empty;
    public string TwitterUsername { get; set; } = string.Empty;
    public string SocialName { get; set; } = string.Empty;
    public string SocialEmail { get; set; } = string.Empty;
    public string FediverseHandle { get; set; } = string.Empty;
    public string SocialLinks { get; set; } = string.Empty; // one URL per line

    // Webmaster verification
    public string GoogleVerification { get; set; } = string.Empty;
    public string BingVerification { get; set; } = string.Empty;

    // Analytics
    public string GoogleAnalyticsId { get; set; } = string.Empty;
    public string GoatCounterId { get; set; } = string.Empty;

    // Comments
    public string CommentsProvider { get; set; } = string.Empty; // disqus | utterances | giscus
    public string DisqusShortname { get; set; } = string.Empty;
    public string UtterancesRepo { get; set; } = string.Empty;
    public string UtterancesIssueTerm { get; set; } = string.Empty;
    public string GiscusRepo { get; set; } = string.Empty;
    public string GiscusRepoId { get; set; } = string.Empty;
    public string GiscusCategory { get; set; } = string.Empty;
    public string GiscusCategoryId { get; set; } = string.Empty;

    // PWA / assets
    public bool PwaEnabled { get; set; } = true;
    public bool PwaCacheEnabled { get; set; } = true;
    public bool AssetsSelfHostEnabled { get; set; }

    // Edit post action
    public bool EditPostEnabled { get; set; }
    public string EditPostUrl { get; set; } = string.Empty;
}
