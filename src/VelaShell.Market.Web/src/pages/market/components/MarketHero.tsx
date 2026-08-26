import { HOT_KEYWORDS } from '@/configs';
import { formatCompact } from '@/utils/format';
import { SafetyCertificateOutlined } from '@ant-design/icons';
import { Input, Tooltip } from 'antd';

/**
 * 浏览页首屏。
 *
 * 旧版这里是一块 135° 的紫→洋红渐变,是整站最显旧的一处 —— 那种通屏渐变在
 * 2018 年前后的 SaaS 首页上到处都是,今天只会让人觉得这个站没人维护。
 * 换成一整块深色底:它给页面一个明确的"顶",让下面的白卡片有对比,
 * 而彩色只留给插件图标 —— 那才是需要被区分的东西。
 *
 * 右边三个数字里最值得看的是第三个:**带阻断级发现却被发布出去的包**。
 * 它是个不变量,正常永远是 0;把它挂在首屏是故意的 —— 哪天它不是 0 了,
 * 第一个看见的人就是访客。
 */
export default function MarketHero({ stats, defaultValue, onSearch }: { stats?: MarketAPI.SiteStats; defaultValue?: string; onSearch: (value: string) => void }) {
  return (
    <section className="market-hero">
      <div className="market-hero-inner">
        <div className="market-hero-left">
          <span className="market-hero-badge">
            <SafetyCertificateOutlined />
            每个包都经隔离区检测后才上架
          </span>
          <h1>为 VelaShell 找一个插件</h1>
          <p className="market-hero-lede">浏览、搜索、看详情都不需要登录。上传的 .vpx 先进隔离区,通过容器校验、结构检查与病毒扫描后才对外可见。</p>

          <Input.Search
            className="market-hero-search"
            size="large"
            allowClear
            defaultValue={defaultValue}
            placeholder="搜索插件名称、id 或简介…"
            enterButton="搜索"
            onSearch={onSearch}
          />

          <div className="market-hero-hot">
            大家在找
            {HOT_KEYWORDS.map((word) => (
              <button key={word} type="button" onClick={() => onSearch(word)}>
                {word}
              </button>
            ))}
          </div>
        </div>

        <div className="market-hero-stats">
          <div className="market-hero-stat">
            <b>{stats ? formatCompact(stats.plugins) : '—'}</b>
            <span>已上架插件</span>
          </div>
          <div className="market-hero-stat-rule" />
          <div className="market-hero-stat">
            <b>{stats ? formatCompact(stats.downloads) : '—'}</b>
            <span>累计下载</span>
          </div>
          <div className="market-hero-stat-rule" />
          <Tooltip title="带阻断级检测发现、却出现在正式桶里的版本数。这个数字应当恒为 0。">
            <div className="market-hero-stat">
              <b>{stats ? stats.blockingPublished : '—'}</b>
              <span>放行的可疑包</span>
            </div>
          </Tooltip>
        </div>
      </div>
    </section>
  );
}
