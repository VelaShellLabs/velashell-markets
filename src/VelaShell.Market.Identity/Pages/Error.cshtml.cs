using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenIddict.Abstractions;

namespace VelaShell.Market.Identity.Pages;

/// <summary>
/// 出错页。协议层的失败(redirect_uri 不在白名单、客户端没注册、PKCE 校验不过……)
/// 到这里时 OpenIddict 已经把原因放进响应对象里,直接照实说 ——
/// 这类错误几乎都是接入方配错了,给一句"出错了"只会让人对着空白页猜。
/// </summary>
public sealed class ErrorModel : PageModel
{
    /// <summary>标题。</summary>
    public string Title { get; private set; } = "出错了";

    /// <summary>给用户看的描述。</summary>
    public string Description { get; private set; } = "请回到插件市场重试一次。";

    /// <summary>OAuth 错误码,没有则为空。</summary>
    public string? Error { get; private set; }

    /// <summary>按状态码或 OpenIddict 的响应决定显示什么。</summary>
    public void OnGet([FromQuery] int? code)
    {
        OpenIddictResponse? response = HttpContext.GetOpenIddictServerResponse();
        if (response is not null && !string.IsNullOrEmpty(response.Error))
        {
            Title = "这次授权请求没能通过";
            Description = response.ErrorDescription ?? "认证服务拒绝了这次请求。";
            Error = response.Error;
            return;
        }

        (Title, Description) = code switch
        {
            400 => ("请求不对", "这次请求缺了必要的参数,或者参数值不合法。"),
            401 => ("需要登录", "这个页面要登录之后才能访问。"),
            403 => ("没有权限", "当前账号不能做这件事。"),
            404 => ("页面不存在", "地址可能敲错了。"),
            _   => ("出错了", "请回到插件市场重试一次;如果一直这样,请联系管理员。")
        };
    }
}
