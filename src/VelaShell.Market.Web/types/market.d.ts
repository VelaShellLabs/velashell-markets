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
    downloads: number;
    ratingAverage: number;
    ratingCount: number;
  };

  type PluginPage = {
    total: number;
    page: number;
    size: number;
    items: PluginSummary[];
  };

  type TagCount = { tag: string; count: number };

  type Version = {
    version: string;
    apiLevel: number;
    minHostVersion?: string;
    hostMode: string;
    packageSize: number;
    payloadSha256: string;
    fileSha256: string;
    signature: SignatureState;
    releaseNotesHtml: string;
    publishedAt?: string;
    downloads: number;
  };

  type PluginDetail = {
    id: string;
    displayName: string;
    summary?: string;
    descriptionHtml: string;
    /** 描述被审核员移除时的原因;有值说明描述是被清空的,不是作者没写。 */
    descriptionRemovedReason?: string;
    author?: string;
    publisher?: string;
    tags: string[];
    homepage?: string;
    license?: string;
    downloads: number;
    ratingAverage: number;
    ratingCount: number;
    createdAt: string;
    updatedAt: string;
    versions: Version[];
  };

  /** 列表查询参数。浏览页的搜索/标签/排序/分页都走这一个结构。 */
  type PluginQuery = {
    q?: string;
    tag?: string;
    sort: string;
    page: number;
    size: number;
  };
}
