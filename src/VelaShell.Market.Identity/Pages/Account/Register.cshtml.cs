using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using VelaShell.Market.Identity.Accounts;
using VelaShell.Market.Identity.Options;

namespace VelaShell.Market.Identity.Pages.Account;

/// <summary>
/// 注册页。注册成功直接建立会话并跳回来处 —— 用户是为了做某件事才被拦到这里的,
/// 让他再登录一次纯属多余。
/// </summary>
public sealed class RegisterModel(AccountStore accounts, IOptions<AccountOptions> options) : PageModel
{
    /// <summary>表单字段。</summary>
    [BindProperty]
    public RegisterInput Input { get; set; } = new();

    /// <summary>注册成功后要回到的站内地址。</summary>
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>失败原因。</summary>
    public string? Error { get; private set; }

    /// <summary>是否开放自助注册。</summary>
    public bool AllowRegistration => options.Value.AllowSelfRegistration;

    /// <summary>渲染注册页。</summary>
    public void OnGet()
    {
    }

    /// <summary>建账号并直接登录。</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancel)
    {
        if (!AllowRegistration)
        {
            return Forbid();
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }

        RegistrationResult result = await accounts.CreateAsync(Input.UserName, Input.Password,
            Input.Email, Input.DisplayName, cancel);

        if (!result.Succeeded)
        {
            Error = result.Error;
            return Page();
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            SessionPrincipal.Create(result.Account!),
            new AuthenticationProperties { IsPersistent = true, IssuedUtc = DateTimeOffset.UtcNow });

        return this.RedirectToLocalOrHome(ReturnUrl);
    }

    /// <summary>注册表单。</summary>
    public sealed class RegisterInput
    {
        /// <summary>用户名。更细的字符与长度规则在 <see cref="AccountStore" /> 里统一判。</summary>
        [Required(ErrorMessage = "请填写用户名。")]
        [Display(Name = "用户名")]
        public string UserName { get; set; } = "";

        /// <summary>显示名,可留空。</summary>
        [Display(Name = "显示名")]
        public string? DisplayName { get; set; }

        /// <summary>邮箱,可留空。</summary>
        [EmailAddress(ErrorMessage = "邮箱格式不对。")]
        [Display(Name = "邮箱")]
        public string? Email { get; set; }

        /// <summary>口令。</summary>
        [Required(ErrorMessage = "请填写口令。")]
        [Display(Name = "口令")]
        public string Password { get; set; } = "";

        /// <summary>确认口令。</summary>
        [Required(ErrorMessage = "请再输一次口令。")]
        [Compare(nameof(Password), ErrorMessage = "两次输入的口令不一致。")]
        [Display(Name = "确认口令")]
        public string ConfirmPassword { get; set; } = "";
    }
}
