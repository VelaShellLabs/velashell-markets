/** 审核域:隔离队列、插件治理、评价治理。对应 services/moderation。 */
declare namespace ModerationAPI {
  type PendingVersion = {
    id: string;
    pluginId: string;
    displayName: string;
    version: string;
    uploadedBySubject: string;
    uploadedByName?: string | null;
    uploadedAt: string;
    packageSize: number;
    signature: MarketAPI.SignatureState;
    /** 这个插件此前有多少个版本平安通过 —— 判断"换签名密钥"这类告警时最有用的背景。 */
    publishedVersions: number;
    scan: MarketAPI.Scan;
    findings: MarketAPI.Finding[];
  };

  /** 包内清单的一条。flag 与检测器共用同一组扩展名表。 */
  type PackageEntry = {
    path: string;
    size: number;
    compressed: number;
    flag?: 'blocked' | 'suspicious' | null;
  };

  type PackageEntries = {
    total: number;
    truncated: boolean;
    entries: PackageEntry[];
  };

  /** 插件治理列表里的一条。与 PluginSummary 不同,它**包含已下架的条目**。 */
  type ModeratedPlugin = {
    id: string;
    displayName: string;
    summary?: string;
    ownerSubject: string;
    ownerName?: string;
    latestVersion?: string;
    downloads: number;
    ratingAverage: number;
    ratingCount: number;
    isFeatured: boolean;
    featuredAt?: string;
    isUnlisted: boolean;
    unlistedReason?: string;
    unlistedAt?: string;
    /** Markdown 原文 —— 审核员要看的是作者写了什么,不是渲染结果。 */
    descriptionMarkdown: string;
    descriptionRemovedReason?: string;
    descriptionRemovedAt?: string;
    updatedAt: string;
  };

  type ModeratedPluginPage = {
    total: number;
    page: number;
    size: number;
    items: ModeratedPlugin[];
  };

  /** 评价治理列表里的一条,含已隐藏的。 */
  type ModeratedReview = {
    id: string;
    pluginId: string;
    subject: string;
    displayName?: string;
    rating: number;
    body?: string;
    pluginVersion?: string;
    createdAt: string;
    updatedAt: string;
    isHidden: boolean;
    hiddenReason?: string;
    hiddenAt?: string;
    hiddenBySubject?: string;
  };

  type ModeratedReviewPage = {
    total: number;
    page: number;
    size: number;
    items: ModeratedReview[];
  };

  /** 强制下架的结果。failedKeys 非空表示有对象没删掉,需要人工去对象存储里清。 */
  type TakedownResult = {
    deletedVersions: number;
    blockedVersions: number;
    failedKeys: string[];
  };

  type PluginQuery = {
    q?: string;
    unlisted?: boolean;
    page?: number;
    size?: number;
  };

  type ReviewQuery = {
    pluginId?: string;
    hidden?: boolean;
    q?: string;
    page?: number;
    size?: number;
  };
}
