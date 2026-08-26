import { Space } from 'antd';
import type { ReactNode } from 'react';

/**
 * 页面外壳:统一的左右留白 + 标题区。
 *
 * 六个页面原先各写一遍 `<div className="market-page">` 加一个 `Typography.Title level={3}`,
 * 标题下的说明文字与右侧按钮的间距每处都差一点。收进来之后,页面文件里只剩内容本身。
 *
 * 标题用原生 h1/p 而不是 Typography:这一层要的是版式(22px / 700 / -0.3 字距),
 * 而 Typography.Title 的层级样式在 antd 主题里是全站共享的,为了这一处去改它不合适。
 */
export default function PageShell({ title, description, extra, children }: { title?: ReactNode; description?: ReactNode; extra?: ReactNode; children: ReactNode }) {
  return (
    <div className="market-page">
      {title || extra ? (
        <div className="market-page-head">
          <div>
            {title ? <h1 className="market-page-title">{title}</h1> : null}
            {description ? <p className="market-page-desc">{description}</p> : null}
          </div>
          {extra ? <Space wrap>{extra}</Space> : null}
        </div>
      ) : null}
      {children}
    </div>
  );
}
