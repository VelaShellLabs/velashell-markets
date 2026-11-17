/** 当前用户自己的东西:画像与我发布的插件。对应 services/me。 */
declare namespace MeAPI {
  /** /me:是不是审核员由服务端说了算,前端只拿结论决定要不要露入口。 */
  type Profile = {
    /** OIDC 的 sub。账户页要把它显示出来 —— 审核员名单按它配。 */
    subject: string;
    name: string;
    email?: string | null;
    isModerator: boolean;
  };

  type MyPlugin = {
    id: string;
    displayName: string;
    summary?: string;
    descriptionMarkdown: string;
    tags: string[];
    homepage?: string;
    license?: string;
    latestVersion?: string;
    downloads: number;
    ratingAverage: number;
    ratingCount: number;
    isUnlisted: boolean;
    unlistedReason?: string;
    updatedAt: string;
    trustedPublishers: TrustedPublisher[];
  };

  /** 一条可信发布者:允许这个 GitHub 工作流凭 OIDC 令牌替我发新版本。 */
  type TrustedPublisher = {
    id: string;
    provider: 'github';
    /** owner/repo,登记时 GitHub 返回的规范写法。匹配实际按仓库的数字 id,改名不影响。 */
    repository: string;
    /** 工作流文件名,如 release.yml。 */
    workflow: string;
    environment?: string | null;
    addedAt: string;
  };

  /** 商店的可信发布设置:没启用就不露登记入口。 */
  type TrustedPublishingSettings = {
    enabled: boolean;
    /** CI 申请 OIDC 令牌时要传的 audience。 */
    audience: string;
    uploadPath: string;
  };

  type TrustedPublisherRequest = {
    pluginIds: string[];
    repository: string;
    workflow: string;
    environment?: string;
  };

  type TrustedPublisherResult = {
    repository: string;
    workflow: string;
    environment?: string | null;
    added: { pluginId: string; publisherId: string }[];
    skipped: { pluginId: string; reason: string }[];
  };

  /** 可改的只有页面文案 —— id / 版本 / 兼容信息一律取自包内 plugin.json。 */
  type PluginPatch = {
    descriptionMarkdown?: string;
    tags?: string;
    homepage?: string;
  };
}
