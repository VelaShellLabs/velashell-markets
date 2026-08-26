/** 上传域:预检、送检与我的上传记录。对应 services/uploads。 */
declare namespace UploadsAPI {
  type UploadResult = {
    pluginId: string;
    version: string;
    status?: string;
    message?: string;
  };

  /**
   * 预检结果:包内清单读出来是什么样。
   *
   * `ownership` / `versionState` 是两种**上传必然失败**的情况的提前告知:
   * id 被别人认领了、或者这个版本已经发布过。没有它们的话,这两种错误都要等一次
   * 完整往返加一次隔离区占用才会浮现。
   */
  type Inspection = {
    pluginId: string;
    version: string;
    displayName: string;
    description?: string;
    author?: string;
    publisher?: string;
    apiLevel: number;
    hostMode: string;
    minHostVersion?: string;
    minSdkVersion?: string;
    entry: string;
    license?: string;
    homepage?: string;
    activationEvents: string[];
    contributes?: MarketAPI.Contributes | null;
    packageSize: number;
    payloadSha256: string;
    fileSha256: string;
    signature: MarketAPI.SignatureState;
    signatureFingerprint?: string | null;
    /** new=没人认领过 / yours=你的 / taken=已被别人认领(传上去只会 403)。 */
    ownership: 'new' | 'yours' | 'taken';
    /** new=新版本 / reupload=覆盖隔离区里那次 / published=已发布(传上去只会 409)。 */
    versionState: 'new' | 'reupload' | 'published';
    ownerName?: string | null;
  };

  type MyUpload = {
    pluginId: string;
    version: string;
    status: string;
    uploadedAt: string;
    publishedAt?: string;
    packageSize: number;
    signature: MarketAPI.SignatureState;
    scan?: MarketAPI.Scan | null;
  };
}
