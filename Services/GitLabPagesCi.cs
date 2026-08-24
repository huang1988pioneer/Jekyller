namespace Jekyller.Services;

public static class GitLabPagesCi
{
    public const string FileName = ".gitlab-ci.yml";

    public const string Configuration = """
        default:
          image: "hugomods/hugo:0.164.0"

        variables:
          GIT_SUBMODULE_STRATEGY: recursive

        test:
          script:
            - hugo --gc --minify
          rules:
            - if: $CI_COMMIT_BRANCH != $CI_DEFAULT_BRANCH

        create-pages:
          script:
            - hugo --gc --minify
          pages: true
          artifacts:
            paths:
              - public
          rules:
            - if: $CI_COMMIT_BRANCH == $CI_DEFAULT_BRANCH
          environment: production
        """;

    public static string PathFor(string projectPath) => Path.Combine(projectPath, FileName);
}
