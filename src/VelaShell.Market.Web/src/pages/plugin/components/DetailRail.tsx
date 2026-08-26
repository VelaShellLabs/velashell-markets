import { Chip, PluginIcon } from '@/components';
import { formatCount, formatDate, formatRating, formatSize } from '@/utils/format';
import { CheckOutlined, CopyOutlined, ExportOutlined, MinusOutlined, StarFilled, WarningOutlined } from '@ant-design/icons';
import { App, Tooltip } from 'antd';
import { history } from '@umijs/max';
import type { ReactNode } from 'react';

/** 一条检测项。tone 决定左边那个圆点的颜色。 */
function Check({ tone, title, detail }: { tone: 'ok' | 'warn' | 'neutral'; title: string; detail: ReactNode }) {
  const icon = tone === 'ok' ? <CheckOutlined /> : tone === 'warn' ? <WarningOutlined /> : <MinusOutlined />;
  return (
    <div className="rail-check">
      <span className={`rail-check-mark chip-${tone}`}>{icon}</span>
      <span>
        <b>{title}</b>
        <span>{detail}</span>
      </span>
    </div>
  );
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <dl className="rail-row">
      <dt>{label}</dt>
      <dd>{children}</dd>
    </dl>
  );
}

/** 可复制的哈希块。整串很长,必须能断行,否则会把右栏撑出横向滚动条。 */
function Hash({ label, value }: { label: string; value?: string }) {
  const { message } = App.useApp();
  if (!value) return null;
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      message.success('已复制');
    } catch {
      // 浏览器不给剪贴板权限时不弹错:值本身就在页面上,选中复制即可。
    }
  };
  return (
    <div className="rail-hash">
      <div className="rail-hash-head">
        <span>{label}</span>
        <Tooltip title="复制">
          <CopyOutlined style={{ cursor: 'pointer' }} onClick={copy} />
        </Tooltip>
      </div>
      <code>{value}</code>
    </div>
  );
}

const SIGNATURE_CHECK: Record<string, { tone: 'ok' | 'warn' | 'neutral'; title: string; detail: string }> = {
  Trusted: { tone: 'ok', title: '签名可信', detail: '公钥与该插件既往版本一致,发布者身份连续' },
  Untrusted: { tone: 'warn', title: '自签名', detail: '签名有效,但公钥不在受信任列表里' },
  None: { tone: 'neutral', title: '未签名', detail: '作者未对该包签名,升级时无法校验发布者身份' },
};

/**
 * 详情页右栏。
 *
 * 刻意**不用卡片套卡片**:旧版这里是三张 `<Card>`,每张里面又套一个 Descriptions 或
 * Alert,四层边框叠在一起,信息密度没上去,视觉噪音倒是满了。现在换成发丝线分区,
 * 层级靠留白与字重。
 */
export default function DetailRail({ plugin, latest, related }: { plugin: MarketAPI.PluginDetail; latest?: MarketAPI.Version; related?: MarketAPI.RelatedPlugins }) {
  const scan = latest?.scan;
  const signature = SIGNATURE_CHECK[latest?.signature ?? 'None'] ?? SIGNATURE_CHECK.None;
  // 引擎字典的键由检测流水线写死("clamav" / "vpx-static" / "signature"),见 PackageReviewPipeline。
  const clam = scan?.engines?.clamav;
  const duration = scan?.completedAt && scan?.startedAt ? (new Date(scan.completedAt).getTime() - new Date(scan.startedAt).getTime()) / 1000 : null;
  const relatedItems = [...(related?.byAuthor ?? []), ...(related?.byTags ?? [])].slice(0, 4);

  return (
    <aside className="detail-rail">
      {latest ? (
        <section className="rail-section">
          <h3 className="rail-title">安全检测</h3>
          {scan ? (
            <>
              <Check tone="ok" title="容器与结构校验" detail="无路径逃逸、重名条目与解压炸弹" />
              <Check tone="ok" title="清单一致" detail="plugin.json 的 id / 版本与包内内容一致" />
              <Check tone="ok" title="病毒扫描通过" detail={<span className="mono">{clam ? `ClamAV ${clam}` : '引擎版本未记录'}</span>} />
              <Check tone={signature.tone} title={signature.title} detail={signature.detail} />
              <p className="rail-note">
                {formatDate(scan.completedAt ?? scan.startedAt)} 完成
                {duration !== null ? `,用时 ${duration.toFixed(1)}s` : ''}
                {scan.entryCount ? ` · ${scan.entryCount} 个条目` : ''}
              </p>
            </>
          ) : (
            <>
              <Check tone={signature.tone} title={signature.title} detail={signature.detail} />
              {/* 老记录没有留存报告。这里说清楚"为什么这一栏是空的" ——
                  不说的话读者只会以为这个包没被检测过。 */}
              <p className="rail-note">这个版本发布于检测报告留存之前,报告已不可考;它当时同样走完了整条隔离流水线。</p>
            </>
          )}
        </section>
      ) : null}

      <section className="rail-section">
        <h3 className="rail-title">信息</h3>
        <Row label="插件 id">
          <span className="mono">{plugin.id}</span>
        </Row>
        <Row label="作者">{plugin.author ?? plugin.ownerName ?? '—'}</Row>
        <Row label="许可证">{plugin.license ?? '—'}</Row>
        <Row label="主页">
          {plugin.homepage ? (
            <a href={plugin.homepage} target="_blank" rel="noreferrer noopener">
              {plugin.homepage.replace(/^https?:\/\//, '')} <ExportOutlined style={{ fontSize: 11 }} />
            </a>
          ) : (
            '—'
          )}
        </Row>
        <Row label="下载量">{formatCount(plugin.downloads)}</Row>
        <Row label="评分">
          {plugin.ratingCount > 0 ? (
            <>
              <StarFilled style={{ color: 'var(--star)', fontSize: 12 }} /> {formatRating(plugin.ratingAverage, plugin.ratingCount)}
            </>
          ) : (
            '暂无评价'
          )}
        </Row>
        <Row label="最新版本">
          <span className="mono">{latest ? `v${latest.version}` : '—'}</span>
        </Row>
        <Row label="包大小">{latest ? formatSize(latest.packageSize) : '—'}</Row>
      </section>

      {latest ? (
        <section className="rail-section">
          <h3 className="rail-title">完整性校验</h3>
          <p className="rail-note" style={{ marginTop: 0 }}>
            下载后可核对校验和;或者直接用 <code className="mono">vela-plugin verify</code> 一并校验容器完整性与签名。
          </p>
          <Hash label="载荷 SHA-256" value={latest.payloadSha256} />
          <Hash label="整包 SHA-256" value={latest.fileSha256} />
        </section>
      ) : null}

      {relatedItems.length > 0 ? (
        <section className="rail-section">
          <h3 className="rail-title">{related?.byAuthor?.length ? '同一作者的其他插件' : '标签相近的插件'}</h3>
          {relatedItems.map((item) => (
            <div className="rail-related" key={item.id} onClick={() => history.push(`/plugins/${item.id}`)}>
              <PluginIcon id={item.id} name={item.displayName} size={34} />
              <span className="rail-related-text">
                <b>{item.displayName}</b>
                <span>{item.summary || item.excerpt || item.id}</span>
              </span>
              {item.ratingCount > 0 ? (
                <Chip plain>
                  <StarFilled style={{ color: 'var(--star)' }} /> {item.ratingAverage.toFixed(1)}
                </Chip>
              ) : null}
            </div>
          ))}
        </section>
      ) : null}
    </aside>
  );
}
