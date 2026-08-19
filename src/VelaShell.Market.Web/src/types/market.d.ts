/** 市场 API 的响应类型。命名空间与参考架构(DriversAPI 等)保持一致。 */
declare namespace MarketAPI {
  /** /me:当前用户画像。是不是审核员由服务端说了算,前端只拿结论决定要不要露入口。 */
  type Profile = {
    name: string;
    isModerator: boolean;
  };

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
    signature: string;
    releaseNotesHtml: string;
    publishedAt?: string;
    downloads: number;
  };

  type PluginDetail = {
    id: string;
    displayName: string;
    summary?: string;
    descriptionHtml: string;
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

  type Review = {
    displayName?: string;
    rating: number;
    bodyHtml: string;
    pluginVersion?: string;
    createdAt: string;
    updatedAt: string;
  };

  type ReviewPage = {
    total: number;
    page: number;
    size: number;
    /** 各星级(1–5)的条数,画"应用商店式"分布条用。 */
    distribution?: Record<string, number>;
    items: Review[];
  };

  type MyReview = {
    rating: number;
    body?: string;
    updatedAt: string;
  };

  type Finding = {
    code: string;
    severity: 'Info' | 'Warning' | 'Blocking';
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

  type MyUpload = {
    pluginId: string;
    version: string;
    status: string;
    uploadedAt: string;
    publishedAt?: string;
    packageSize: number;
    signature: string;
    scan?: Scan | null;
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

  type PendingVersion = {
    id: string;
    pluginId: string;
    version: string;
    uploadedBySubject: string;
    uploadedAt: string;
    packageSize: number;
    signature: string;
    findings: Finding[];
  };

  type UploadResult = {
    pluginId: string;
    version: string;
  };
}
