using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VelaShell.Market.Api.Options;

namespace VelaShell.Market.Api.Services;

/// <summary>GitHub 上一个仓库的身份:规范全名与两个数字 id。</summary>
/// <param name="FullName">规范写法的 <c>owner/repo</c>(大小写以 GitHub 为准)。</param>
/// <param name="RepositoryId">仓库 id。</param>
/// <param name="OwnerId">所有者 id。</param>
public sealed record GitHubRepositoryIdentity(string FullName, long RepositoryId, long OwnerId);

/// <summary>查仓库的结果:要么拿到身份,要么是一句能直接给人看的原因。</summary>
/// <param name="Identity">查到的身份。</param>
/// <param name="Error">失败原因。</param>
public sealed record GitHubRepositoryLookup(GitHubRepositoryIdentity? Identity, string? Error);

/// <summary>
/// 登记可信发布者时向 GitHub 查仓库的数字 id。
///
/// 之所以登记时就要查:令牌里同时有仓库名与数字 id,只存名字的话,仓库被删后有人用同名重建,
/// 那个人的工作流就能替你发版。存下 id,匹配只认 id。
/// </summary>
public sealed class GitHubRepositoryResolver(HttpClient http, IOptions<GitHubOidcOptions> options)
{
    /// <summary>HttpClient 的名字(在 Program.cs 里配置)。</summary>
    public const string ClientName = "github-api";

    /// <summary>查一个仓库。</summary>
    public async Task<GitHubRepositoryLookup> ResolveAsync(string repository, CancellationToken cancellationToken)
    {
        GitHubOidcOptions o = options.Value;
        using HttpRequestMessage request = new(HttpMethod.Get, $"{o.ApiBaseUrl.TrimEnd('/')}/repos/{repository}");
        request.Headers.Accept.Add(new("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(o.ApiToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiToken);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new(null, "暂时连不上 GitHub,请稍后再试。");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new(null, string.IsNullOrWhiteSpace(o.ApiToken)
                                     ? $"GitHub 上找不到仓库 {repository}。私有仓库需要商店配置 GitHubOidc:ApiToken 后才能登记。"
                                     : $"GitHub 上找不到仓库 {repository},或商店配置的 GitHub 令牌无权访问它。");
            }
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                return new(null, "GitHub API 限流,请稍后再试。");
            }
            if (!response.IsSuccessStatusCode)
            {
                return new(null, $"查询 GitHub 仓库失败(HTTP {(int)response.StatusCode})。");
            }

            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
            JsonElement root = json.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id)
                || !root.TryGetProperty("full_name", out JsonElement fullName)
                || !root.TryGetProperty("owner", out JsonElement owner)
                || !owner.TryGetProperty("id", out JsonElement ownerId))
            {
                return new(null, "GitHub 返回的仓库信息不完整。");
            }
            return new(new(fullName.GetString() ?? repository, id.GetInt64(), ownerId.GetInt64()), null);
        }
    }
}
