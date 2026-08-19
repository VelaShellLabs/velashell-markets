using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using VelaShell.Market.Identity.Accounts;
using VelaShell.Market.Identity.Options;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace VelaShell.Market.Identity.Pages.Account;

/// <summary>
/// 登录页。授权端点发现浏览器还没登录时把人送到这里,登录成功后原样跳回那次授权请求,
/// 于是用户看到的是"点登录 → 输账号 → 回到商店",中间这一跳是无感的。
/// </summary>
public sealed class LoginModel(AccountStore accounts, IOptions<AccountOptions> options) : PageModel
{
    /// <summary>表单字段。</summary>
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    /// <summary>登录成功后要回到的站内地址。为空则回首页。</summary>
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>失败原因。</summary>
    public string? Error { get; private set; }

    /// <summary>是否开放自助注册,决定要不要露出注册入口。</summary>
    public bool AllowRegistration => options.Value.AllowSelfRegistration;

    /// <summary>渲染登录页。</summary>
    public void OnGet()
    {
    }

    /// <summary>校验凭据并签发会话 cookie。</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancel)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        MarketAccount? account = await accounts.FindByLoginAsync(Input.Login, cancel);
        // 账号不存在时也走一遍相同的分支:回话内容与耗时都不该泄露"这个用户名存在不存在"。
        SignInStatus status = account is null
                                  ? SignInStatus.InvalidCredentials
                                  : await accounts.CheckPasswordAsync(account, Input.Password, cancel);

        if (status != SignInStatus.Success)
        {
            Error = status switch
            {
                SignInStatus.LockedOut => "连续失败次数过多,账号已被临时锁定,请稍后再试。",
                SignInStatus.Disabled  => "该账号已停用,请联系管理员。",
                _                      => "用户名或口令不对。"
            };
            return Page();
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            SessionPrincipal.Create(account!),
            new AuthenticationProperties
            {
                IsPersistent = Input.RememberMe,
                // 授权请求带 max_age 时要用它判断"这次登录够不够新",不能少。
                IssuedUtc = DateTimeOffset.UtcNow
            });

        return this.RedirectToLocalOrHome(ReturnUrl);
    }

    /// <summary>登录表单。</summary>
    public sealed class LoginInput
    {
        /// <summary>用户名或邮箱。</summary>
        [Required(ErrorMessage = "请填写用户名或邮箱。")]
        [Display(Name = "用户名或邮箱")]
        public string Login { get; set; } = "";

        /// <summary>口令。</summary>
        [Required(ErrorMessage = "请填写口令。")]
        [Display(Name = "口令")]
        public string Password { get; set; } = "";

        /// <summary>是否保持登录状态。</summary>
        [Display(Name = "记住我")]
        public bool RememberMe { get; set; } = true;
    }
}

/// <summary>把账号翻译成本站会话用的身份主体。声明名与令牌里保持一致,省得两边各记一套。</summary>
public static class SessionPrincipal
{
    /// <summary>建一个只带 sub / name / preferred_username 的会话主体。</summary>
    public static ClaimsPrincipal Create(MarketAccount account)
    {
        ClaimsIdentity identity = new(CookieAuthenticationDefaults.AuthenticationScheme, Claims.Name, Claims.Role);
        identity.AddClaim(new(Claims.Subject, account.Id));
        identity.AddClaim(new(Claims.Name, account.DisplayName ?? account.UserName));
        identity.AddClaim(new(Claims.PreferredUsername, account.UserName));
        return new(identity);
    }
}

/// <summary>回跳地址的收口。</summary>
public static class PageRedirects
{
    /// <summary>
    /// 只允许跳回站内地址。开放重定向在登录页上尤其致命 ——
    /// 攻击者可以拿一个真实的登录页把人骗到自己的站点上。
    /// </summary>
    public static IActionResult RedirectToLocalOrHome(this PageModel page, string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && page.Url.IsLocalUrl(returnUrl)
            ? page.LocalRedirect(returnUrl)
            : page.RedirectToPage("/Index");
}
