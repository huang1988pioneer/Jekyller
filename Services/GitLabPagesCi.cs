namespace Jekyller.Services;

public static class GitLabPagesCi
{
    public const string FileName = ".gitlab-ci.yml";

    public const string Configuration = """
        image: ruby:3.3

        variables:
          JEKYLL_ENV: production
          LC_ALL: C.UTF-8

        cache:
          paths:
            - vendor/

        before_script:
          - bundle config set --local path 'vendor'
          - bundle install

        test:
          stage: test
          script:
            - bundle exec jekyll build -d test
          artifacts:
            paths:
              - test
          rules:
            - if: $CI_COMMIT_BRANCH != $CI_DEFAULT_BRANCH

        pages:
          stage: deploy
          script:
            - bundle exec jekyll build -d public
          artifacts:
            paths:
              - public
          rules:
            - if: $CI_COMMIT_BRANCH == $CI_DEFAULT_BRANCH
        """;

    public static string PathFor(string projectPath) => Path.Combine(projectPath, FileName);
}
