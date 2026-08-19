import type { RequestConfig, RequestOptions } from '@umijs/max';
import { message, notification } from 'antd';
import { getAccessToken } from '@/utils/auth';

/**
 * 请求层统一配置(模式取自参考项目的 requestErrorConfig):
 * 成功与否由 HTTP 状态码表示,错误体是 ProblemDetails({status,title,detail})
 * 或本项目自己的 {error} 结构。
 */
interface ResponseStructure {
  status?: number;
  title?: string;
  detail?: string;
  error?: string;
}

export const errorConfig: RequestConfig = {
  errorConfig: {
    errorThrower: (res) => {
      // 2xx 响应体里带 status 字段的视为业务错误(ProblemDetails 直通的情况)。
      const { status, title, detail, error } = res as unknown as ResponseStructure;
      if (status && status >= 400) {
        const err: any = new Error(title ?? error);
        err.name = 'BizError';
        err.info = { title: title ?? error, status, detail };
        throw err;
      }
    },
    errorHandler: (error: any, opts: any) => {
      if (opts?.skipErrorHandler) throw error;
      if (error.name === 'BizError') {
        const errorInfo: ResponseStructure | undefined = error.info;
        if (errorInfo) {
          const { status, title, detail } = errorInfo;
          if (status === 500) {
            notification.error({ description: detail, message: title });
          } else {
            message.error(detail ?? title);
          }
        }
      } else if (error.response) {
        // 请求发出且服务器响应了非 2xx。从响应体里尽量抠出一句人话 ——
        // 直接把 500 甩给用户看没有任何帮助。
        const data = error.response.data;
        let errorMessage: string | undefined;
        if (data && typeof data === 'object') {
          errorMessage = data.error || data.detail || data.title || data.message;
        } else if (typeof data === 'string') {
          try {
            const parsed = JSON.parse(data);
            errorMessage = parsed.error || parsed.detail || parsed.title || parsed.message;
          } catch {
            errorMessage = data;
          }
        }
        message.error(errorMessage || `请求失败(${error.response.status})`);
      } else if (error.request) {
        message.error('服务器没有响应,请稍后重试。');
      } else {
        message.error('请求发送失败,请重试。');
      }
    },
  },

  // 请求拦截器:带上 Bearer 令牌。没有令牌时照常发 —— 浏览与检索本来就允许匿名。
  requestInterceptors: [
    async (config: RequestOptions) => {
      const token = await getAccessToken();
      if (token) {
        config.headers = { ...config.headers, Authorization: `Bearer ${token}` };
      }
      return config;
    },
  ],

  responseInterceptors: [],
};
