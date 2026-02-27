using System.Text.Json;
using System.Text.Json.Serialization;
using GithubActivityChecker.Configuration;
using GithubActivityChecker.Models;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.SystemTextJson;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GithubActivityChecker.Services;

public class GitHubService : IGitHubService, IDisposable
{
    private readonly GraphQLHttpClient _client;
    private readonly ILogger<GitHubService> _logger;

    private const string ContributionQuery = @"
        query ($login: String!) {
          user(login: $login) {
            contributionsCollection {
              contributionCalendar {
                totalContributions
                weeks {
                  contributionDays {
                    contributionCount
                    date
                  }
                }
              }
            }
          }
        }";

    public GitHubService(IOptions<GitHubSettings> settings, ILogger<GitHubService> logger)
    {
        _logger = logger;

        var pat = settings.Value.PersonalAccessToken;
        if (string.IsNullOrWhiteSpace(pat))
        {
            _logger.LogWarning("GitHub Personal Access Token is not configured. GitHub API calls will fail. Set 'GitHub:PersonalAccessToken' in configuration or user secrets.");
            _client = new GraphQLHttpClient(
                "https://api.github.com/graphql",
                new SystemTextJsonSerializer());
            return;
        }

        _client = new GraphQLHttpClient(
            "https://api.github.com/graphql",
            new SystemTextJsonSerializer());

        _client.HttpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", pat);
        _client.HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("GithubActivityChecker/1.0");
    }

    public async Task<ContributionCalendarResult?> GetContributionCalendarAsync(
        string githubUsername, CancellationToken ct = default)
    {
        try
        {
            var request = new GraphQLRequest
            {
                Query = ContributionQuery,
                Variables = new { login = githubUsername }
            };

            var response = await _client.SendQueryAsync<ContributionResponse>(request, ct);

            if (response.Errors is { Length: > 0 })
            {
                foreach (var err in response.Errors)
                    _logger.LogWarning("GraphQL error for {Username}: {Message}", githubUsername, err.Message);
                return null;
            }

            var calendar = response.Data?.User?.ContributionsCollection?.ContributionCalendar;
            if (calendar is null)
            {
                _logger.LogWarning("No contribution calendar found for {Username}", githubUsername);
                return null;
            }

            var result = new ContributionCalendarResult
            {
                TotalContributions = calendar.TotalContributions,
                Days = calendar.Weeks
                    .SelectMany(w => w.ContributionDays)
                    .Select(d => new ContributionDay
                    {
                        Date = DateOnly.Parse(d.Date),
                        ContributionCount = d.ContributionCount
                    })
                    .OrderBy(d => d.Date)
                    .ToList()
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch contribution calendar for {Username}", githubUsername);
            return null;
        }
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    // ----- Internal deserialization models -----

    private class ContributionResponse
    {
        [JsonPropertyName("user")]
        public UserNode? User { get; set; }
    }

    private class UserNode
    {
        [JsonPropertyName("contributionsCollection")]
        public ContributionsCollectionNode? ContributionsCollection { get; set; }
    }

    private class ContributionsCollectionNode
    {
        [JsonPropertyName("contributionCalendar")]
        public ContributionCalendarNode? ContributionCalendar { get; set; }
    }

    private class ContributionCalendarNode
    {
        [JsonPropertyName("totalContributions")]
        public int TotalContributions { get; set; }

        [JsonPropertyName("weeks")]
        public List<WeekNode> Weeks { get; set; } = new();
    }

    private class WeekNode
    {
        [JsonPropertyName("contributionDays")]
        public List<DayNode> ContributionDays { get; set; } = new();
    }

    private class DayNode
    {
        [JsonPropertyName("contributionCount")]
        public int ContributionCount { get; set; }

        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;
    }
}
