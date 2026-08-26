import { appName } from '@/configs';
import { Link } from '@umijs/max';

/**
 * 顶栏品牌区。
 *
 * 用一个内联 SVG 而不是 `<img src="/favicon.svg">`:这个图标要跟着主色走
 * (`fill: currentColor`),而外链的 svg 拿不到页面的 CSS 变量,换主题时它不会变。
 * Vela 是船帆座 —— 帆的形状既对得上名字,也比又一个立方体/齿轮有辨识度。
 */
export default function Brand() {
  return (
    <Link to="/" className="market-brand" aria-label={appName}>
      <span className="market-brand-mark">
        <svg width="21" height="21" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M2 18a4 4 0 0 0 3.2 1.6h13.6A4 4 0 0 0 22 18" />
          <path d="M4 15h16l-2 3H6z" />
          <path d="M12 13V2L4.5 13" />
          <path d="M13.5 13H20L14 5.5" />
        </svg>
      </span>
      <span className="market-brand-name">VelaShell</span>
      <span className="market-brand-divider" />
      <span className="market-brand-sub">插件市场</span>
    </Link>
  );
}
