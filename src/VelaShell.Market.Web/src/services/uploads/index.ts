import { request } from '@umijs/max';

/** 上传 .vpx 包送检。上传结果(含错误)由发布页自己呈现,跳过统一错误提示。 */
export async function uploadPackage(data: FormData) {
  return request<UploadsAPI.UploadResult>('/api/uploads', { method: 'POST', data, skipErrorHandler: true });
}

/**
 * 只读预检:读出包内清单与签名状态,不落盘、不入库、不排队。
 *
 * 发布页在用户选好文件、还没点「上传并送检」之前调它 —— 把这次上传**认领的是哪个插件的
 * 哪个版本**摊开给人看。错误(容器坏了、没有 plugin.json)由页面就地展示,不走统一提示。
 */
export async function inspectPackage(file: File) {
  const body = new FormData();
  body.append('file', file);
  return request<UploadsAPI.Inspection>('/api/uploads/inspect', { method: 'POST', data: body, skipErrorHandler: true });
}

/**
 * Markdown 预览。走服务端而不是在前端塞一个 Markdown 库 ——
 * 插件页上的 HTML 是 Markdig 渲染并清洗过的,前端换一个渲染器就意味着
 * 预览与实际发布出来的东西可能不一样,而作者恰恰是靠这个预览决定"写好了没有"。
 */
export async function previewMarkdown(markdown: string) {
  return request<{ html: string }>('/api/uploads/preview', { method: 'POST', data: { markdown }, skipErrorHandler: true });
}

/** 我的上传记录(检测进度与完整报告)。 */
export async function getMyUploads() {
  return request<UploadsAPI.MyUpload[]>('/api/uploads/mine', { method: 'GET' });
}
