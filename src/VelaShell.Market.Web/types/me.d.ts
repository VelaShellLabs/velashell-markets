/** 当前用户自己的东西:画像与我发布的插件。对应 services/me。 */
declare namespace MeAPI {
  /** /me:是不是审核员由服务端说了算,前端只拿结论决定要不要露入口。 */
  type Profile = {
    name: string;
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
  };

  /** 可改的只有页面文案 —— id / 版本 / 兼容信息一律取自包内 plugin.json。 */
  type PluginPatch = {
    descriptionMarkdown?: string;
    tags?: string;
    homepage?: string;
  };
}
