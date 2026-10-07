using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Api_Sample;

internal class Program {
    private const int PageSize = 1;

    private static async Task<int> Main(string[] args) {
        //if (args.Length != 3) {
        //    Console.Error.WriteLine("Usage: Api_Sample <owner> <repository> <output.csv>");
        //    Console.Error.WriteLine("Set GitHub:Token in appsettings.json to authenticate and increase the GitHub API rate limit.");
        //    return 1;
        //}

        //var owner = Uri.EscapeDataString(args[0]);
        //var repository = Uri.EscapeDataString(args[1]);
        //var outputPath = args[2];

        var owner = "rustdesk";
        var repository = "rustdesk";
        var outputPath = "output.csv";

        using var httpClient = new HttpClient {
            BaseAddress = new Uri("https://api.github.com/")
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Api-Sample-GitHub-PR-Exporter/1.0");
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        try {
            var token = await GetGitHubTokenAsync();
            if (!string.IsNullOrWhiteSpace(token)) {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var pullRequests = await GetMergedPullRequestsAsync(httpClient, owner, repository);
            var users = new Dictionary<string, GitHubUser>(StringComparer.OrdinalIgnoreCase);

            await using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(true));
            await writer.WriteLineAsync(string.Join(",", new[]
            {
                "PR Number", "Author Login", "Author ID", "Author Name", "Author URL", "Author Avatar URL",
                "Author Type", "Author Company", "Author Blog", "Author Location", "Author Email", "Author Bio",
                "Author Public Repositories", "Author Followers", "Author Following", "Author Account Created At",
                "Merged By Login", "Merged By ID", "Merged By Name", "Merged By URL", "Merged By Avatar URL",
                "Merged By Type", "Merged By Company", "Merged By Blog", "Merged By Location", "Merged By Email",
                "Merged By Bio", "Merged By Public Repositories", "Merged By Followers", "Merged By Following",
                "Merged By Account Created At", "Additions", "Deletions", "Created At", "Merged At", "Time to Merge"
            }.Select(EscapeCsv)));

            foreach (var pullRequest in pullRequests) {
                var details = await GetPullRequestDetailsAsync(httpClient, owner, repository, pullRequest.Number);
                if (pullRequest.User is null) {
                    continue;
                }

                if (!users.TryGetValue(pullRequest.User.Login, out var author)) {
                    author = await GetUserAsync(httpClient, pullRequest.User.Login);
                    users[pullRequest.User.Login] = author;
                }

                var values = new string?[]
                {
                    pullRequest.Number.ToString(CultureInfo.InvariantCulture),
                    author.Login,
                    author.Id?.ToString(CultureInfo.InvariantCulture),
                    author.Name,
                    author.HtmlUrl,
                    author.AvatarUrl,
                    author.Type,
                    author.Company,
                    author.Blog,
                    author.Location,
                    author.Email,
                    author.Bio,
                    author.PublicRepos?.ToString(CultureInfo.InvariantCulture),
                    author.Followers?.ToString(CultureInfo.InvariantCulture),
                    author.Following?.ToString(CultureInfo.InvariantCulture),
                    FormatDate(author.CreatedAt),
                    author.Login,
                    author.Id?.ToString(CultureInfo.InvariantCulture),
                    author.Name,
                    author.HtmlUrl,
                    author.AvatarUrl,
                    author.Type,
                    author.Company,
                    author.Blog,
                    author.Location,
                    author.Email,
                    author.Bio,
                    author.PublicRepos?.ToString(CultureInfo.InvariantCulture),
                    author.Followers?.ToString(CultureInfo.InvariantCulture),
                    author.Following?.ToString(CultureInfo.InvariantCulture),
                    FormatDate(author.CreatedAt),
                    details.Additions.ToString(CultureInfo.InvariantCulture),
                    details.Deletions.ToString(CultureInfo.InvariantCulture),
                    FormatDate(pullRequest.CreatedAt),
                    FormatDate(pullRequest.MergedAt),
                    (pullRequest.MergedAt!.Value - pullRequest.CreatedAt).ToString("c", CultureInfo.InvariantCulture)
                };

                await writer.WriteLineAsync(string.Join(",", values.Select(EscapeCsv)));
            }

            Console.WriteLine($"Exported {pullRequests.Count} merged pull requests to {outputPath}");
            return 0;
        } catch (Exception exception) {
            Console.Error.WriteLine($"Failed to export pull requests: {exception.Message}");
            return 1;
        }
    }

    private static async Task<List<PullRequestSummary>> GetMergedPullRequestsAsync(
        HttpClient httpClient, string owner, string repository) {
        var mergedPullRequests = new List<PullRequestSummary>();

        for (var page = 1; ; page++) {
            var url = $"repos/{owner}/{repository}/pulls?state=closed&per_page={PageSize}&page={page}";
            var response = await httpClient.GetFromJsonAsync<List<PullRequestSummary>>(url) ?? [];
            mergedPullRequests.AddRange(response.Where(pullRequest => pullRequest.MergedAt.HasValue));

            if (response.Count < PageSize) {
                return mergedPullRequests;
            }

            return mergedPullRequests;
        }
    }

    private static async Task<PullRequestDetails> GetPullRequestDetailsAsync(
        HttpClient httpClient, string owner, string repository, int number) {
        return await httpClient.GetFromJsonAsync<PullRequestDetails>(
            $"repos/{owner}/{repository}/pulls/{number}")
            ?? throw new InvalidOperationException($"GitHub returned no details for pull request #{number}.");
    }

    private static async Task<GitHubUser> GetUserAsync(HttpClient httpClient, string login) {
        return await httpClient.GetFromJsonAsync<GitHubUser>($"users/{Uri.EscapeDataString(login)}")
            ?? throw new InvalidOperationException($"GitHub returned no user details for '{login}'.");
    }

    private static async Task<string?> GetGitHubTokenAsync() {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        await using var settingsStream = File.OpenRead(settingsPath);
        using var settings = await JsonDocument.ParseAsync(settingsStream);

        return settings.RootElement
            .GetProperty("GitHub")
            .GetProperty("Token")
            .GetString();
    }

    private static string FormatDate(DateTimeOffset? value) =>
        value?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string EscapeCsv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

    private sealed class PullRequestSummary {
        [JsonPropertyName("number")]
        public int Number {
            get; set;
        }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt {
            get; set;
        }

        [JsonPropertyName("merged_at")]
        public DateTimeOffset? MergedAt {
            get; set;
        }

        [JsonPropertyName("user")]
        public PullRequestUser? User {
            get; set;
        }
    }

    private sealed class PullRequestUser {
        [JsonPropertyName("login")]
        public string Login { get; set; } = string.Empty;
    }

    private sealed class PullRequestDetails {
        [JsonPropertyName("additions")]
        public int Additions {
            get; set;
        }

        [JsonPropertyName("deletions")]
        public int Deletions {
            get; set;
        }
    }

    private sealed class GitHubUser {
        [JsonPropertyName("login")]
        public string Login { get; set; } = string.Empty;

        [JsonPropertyName("id")]
        public long? Id {
            get; set;
        }

        [JsonPropertyName("name")]
        public string? Name {
            get; set;
        }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl {
            get; set;
        }

        [JsonPropertyName("avatar_url")]
        public string? AvatarUrl {
            get; set;
        }

        [JsonPropertyName("type")]
        public string? Type {
            get; set;
        }

        [JsonPropertyName("company")]
        public string? Company {
            get; set;
        }

        [JsonPropertyName("blog")]
        public string? Blog {
            get; set;
        }

        [JsonPropertyName("location")]
        public string? Location {
            get; set;
        }

        [JsonPropertyName("email")]
        public string? Email {
            get; set;
        }

        [JsonPropertyName("bio")]
        public string? Bio {
            get; set;
        }

        [JsonPropertyName("public_repos")]
        public int? PublicRepos {
            get; set;
        }

        [JsonPropertyName("followers")]
        public int? Followers {
            get; set;
        }

        [JsonPropertyName("following")]
        public int? Following {
            get; set;
        }

        [JsonPropertyName("created_at")]
        public DateTimeOffset? CreatedAt {
            get; set;
        }
    }
}
