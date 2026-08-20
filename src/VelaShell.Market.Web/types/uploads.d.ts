/** 上传域:送检与我的上传记录。对应 services/uploads。 */
declare namespace UploadsAPI {
  type UploadResult = {
    pluginId: string;
    version: string;
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
