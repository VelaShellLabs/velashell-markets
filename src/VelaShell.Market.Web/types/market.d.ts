/**
 * 市场核心域:插件、版本、标签,以及检测报告。
 * 命名空间按服务目录拆分(services/market -> MarketAPI),与参考架构一致。
 */
declare namespace MarketAPI {
  /** 检测发现项的严重级别。Blocking 直接拒收,Warning 转人工,Info 只是提示。 */
  type Severity = 'Info' | 'Warning' | 'Blocking';

  type Finding = {
    code: string;
    severity: Severity;
    message: string;
    path?: string | null;
  };

  type Scan = {
    verdict: string;
    startedAt: string;
    completedAt?: string;
    engines: Record<string, string>;
    findings: Finding[];
    entryCount?: number;
    unpackedBytes?: number;
    attempts?: number;
  };

  /**
   * 已发布版本的**公开**检测结论。只有结论与可复现性信息,没有 findings ——
   * 那些带包内路径的诊断信息只给上传者和审核员。
   */
  type PublicScan = {
    verdict: string;
    startedAt: string;
    completedAt?: string;
    engines: Record<string, string>;
    entryCount: number;
    unpackedBytes: number;
  };

  /** 签名状态。未签名不是错误,但它决定升级时能不能校验发布者身份连续性。 */
  type SignatureState = 'Trusted' | 'Untrusted' | 'None' | (string & {});

  type PluginSummary = {
    id: string;
    displayName: string;
    summary?: string;
    excerpt?: string;
    author?: string;
    tags: string[];
    latestVersion?: string;
    latestApiLevel?: number;
    latestMinHostVersion?: string;
    /** 最新已发布版本的签名状态,卡片上的「已验签」据此渲染。 */
    latestSignature?: SignatureState | null;
    isFeatured: boolean;
    downloads: number;
    ratingAverage: number;
    ratingCount: number;
    updatedAt: string;
  };

  type PluginPage = {
    total: number;
    page: number;
    size: number;
    items: PluginSummary[];
  };

  type TagCount = { tag: string; count: number };

  /** 一条命令贡献。 */
  type ContributedCommand = { id: string; title: string; category?: string | null };

  /** 一条连接类型贡献(协议或工作台)。 */
  type ContributedConnection = { id: string; displayName: string; defaultPort: number };

  /**
   * 声明式贡献点。回答的是"装上之后宿主里会多出什么" ——
   * **不是权限清单**,VelaShell 的清单格式目前没有权限声明字段。
   */
  type Contributes = {
    commands: ContributedCommand[];
    protocols: ContributedConnection[];
    workspaces: ContributedConnection[];
  };

  type Version = {
    version: string;
    apiLevel: number;
    minHostVersion?: string;
    minSdkVersion?: string;
    hostMode: string;
    idlePolicy?: string;
    entry?: string;
    activationEvents?: string[];
    contributes?: Contributes | null;
    packageSize: number;
    payloadSha256: string;
    fileSha256: string;
    signature: SignatureState;
    releaseNotesHtml: string;
    publishedAt?: string;
    downloads: number;
    scan?: PublicScan | null;
  };

  type PluginDetail = {
    id: string;
    displayName: string;
    summary?: string;
    descriptionHtml: string;
    descriptionMarkdown?: string;
    /** 描述被审核员移除时的原因;有值说明描述是被清空的,不是作者没写。 */
    descriptionRemovedReason?: string;
    author?: string;
    publisher?: string;
    ownerName?: string;
    tags: string[];
    homepage?: string;
    license?: string;
    isFeatured: boolean;
    downloads: number;
    ratingAverage: number;
    ratingCount: number;
    createdAt: string;
    updatedAt: string;
    versions: Version[];
  };

  /** 相关插件。两路来源分开给,页面上标题不一样。 */
  type RelatedPlugins = {
    byAuthor: PluginSummary[];
    byTags: PluginSummary[];
  };

  /** 站点概览。blockingPublished 是个不变量,正常永远是 0。 */
  type SiteStats = {
    plugins: number;
    versions: number;
    downloads: number;
    blockingPublished: number;
  };

  /** 列表查询参数。浏览页的搜索/标签/排序/分页都走这一个结构。 */
  type PluginQuery = {
    q?: string;
    tag?: string;
    sort: string;
    page: number;
    size: number;
    featured?: boolean;
  };
}
