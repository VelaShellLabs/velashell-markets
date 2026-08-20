import { request } from '@umijs/max';

/** 上传 .vpx 包送检。上传结果(含错误)由发布页自己呈现,跳过统一错误提示。 */
export async function uploadPackage(data: FormData) {
  return request<UploadsAPI.UploadResult>('/api/uploads', { method: 'POST', data, skipErrorHandler: true });
}

/** 我的上传记录(检测进度与完整报告)。 */
export async function getMyUploads() {
  return request<UploadsAPI.MyUpload[]>('/api/uploads/mine', { method: 'GET' });
}
