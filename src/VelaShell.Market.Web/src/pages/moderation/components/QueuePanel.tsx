import { Chip, Findings, SignatureTag } from '@/components';
import { approveVersion, downloadSample, getPackageEntries, getQueue, rejectVersion } from '@/services/moderation';
import { formatDateTime, formatRelative, formatSize } from '@/utils/format';
import { keepResult } from '@/utils/request';
import { CheckOutlined, ClockCircleOutlined, CloseOutlined, DownloadOutlined, FileZipOutlined, InfoCircleOutlined, ReloadOutlined, WarningOutlined } from '@ant-design/icons';
import { useRequest } from '@umijs/max';
import { App, Button, Empty, Input, Skeleton } from 'antd';
import { useEffect, useState } from 'react';

/** 一条队列卡片。选中的那张换成主色边框 + 淡色底,而不是只加粗字重。 */
function QueueCard({ item, active, onSelect }: { item: ModerationAPI.PendingVersion; active: boolean; onSelect: () => void }) {
  const warnings = item.findings.filter((finding) => finding.severity === 'Warning').length;
  return (
    <article className={`mod-queue-card${active ? ' mod-queue-card-active' : ''}`} onClick={onSelect}>
      <span className="mod-queue-icon">
        <FileZipOutlined />
      </span>
      <div className="mod-queue-text">
        <div className="mod-queue-title">
          <b title={item.pluginId}>{item.pluginId}</b>
          <code>v{item.version}</code>
        </div>
        <span className="mod-queue-sub">
          {item.uploadedByName ?? item.uploadedBySubject} · 等待 {formatRelative(item.uploadedAt).replace('前', '')}
        </span>
        <Chip tone="warn" icon={<WarningOutlined />}>
          {warnings > 0 ? `${warnings} 项待确认` : '需人工复核'}
        </Chip>
      </div>
    </article>
  );
}

/** 包内清单。文件名、大小,以及与检测器共用同一组扩展名表的高亮。 */
function EntryList({ versionId }: { versionId: string }) {
  const { data, loading, error } = useRequest(() => getPackageEntries(versionId), {
    formatResult: keepResult,
    refreshDeps: [versionId],
    onError: () => undefined,
  });

  if (loading) return <Skeleton active paragraph={{ rows: 3 }} title={false} />;
  if (error || !data) return <span style={{ fontSize: 12.5, color: 'var(--ink-3)' }}>清单读取失败(包可能已被搬走或删除)。</span>;

  return (
    <div className="mod-entries">
      {data.entries.map((entry) => (
        <div className="mod-entry" key={entry.path}>
          <span className={entry.flag === 'blocked' ? 'mod-entry-blocked' : entry.flag === 'suspicious' ? 'mod-entry-flag' : undefined}>{entry.path}</span>
          <span style={{ display: 'inline-flex', gap: 10, alignItems: 'center', flexShrink: 0 }}>
            {entry.flag ? <Chip tone={entry.flag === 'blocked' ? 'danger' : 'warn'}>{entry.flag === 'blocked' ? '不该出现' : '脚本 / 原生库'}</Chip> : null}
            {formatSize(entry.size)}
          </span>
        </div>
      ))}
      {data.truncated ? <div className="mod-entry">…… 只显示前 {data.entries.length} 条,共 {data.total} 条</div> : null}
    </div>
  );
}

/**
 * 隔离队列。这里处理的是**检测判为"需人工复核"的包** —— 它们既没被拒,也绝不可下载,
 * 一直留在隔离区等一个人来看。
 *
 * 左队列右详情,而不是旧版那种"一屏卡片 + 弹窗填原因":审核员要同时看见命中项、
 * 包内清单和自己正在写的处置原因,才能作出判断。分成弹窗之后,人只能靠记忆
 * 在两个界面之间搬运信息 —— 而这正是审核最容易出错的地方。
 */
export default function QueuePanel() {
  const api = App.useApp();
  const [selected, setSelected] = useState<string | null>(null);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState<'approve' | 'reject' | null>(null);

  const { data, loading, refresh } = useRequest(getQueue, { formatResult: keepResult, onError: () => undefined });
  const items = data ?? [];
  const current = items.find((item) => item.id === selected) ?? items[0];

  // 队列变了(别人处理掉一条、或者刚刷新)之后,选中项跟着回到第一条,
  // 免得右边一直停在一个已经不存在的包上。
  useEffect(() => {
    if (items.length > 0 && !items.some((item) => item.id === selected)) {
      setSelected(items[0].id);
      setReason('');
    }
  }, [items, selected]);

  const act = async (kind: 'approve' | 'reject') => {
    if (!current) return;
    if (kind === 'reject' && !reason.trim()) {
      api.message.warning('驳回必须填原因 —— 不给原因等于让作者盲目重传');
      return;
    }
    setBusy(kind);
    try {
      if (kind === 'approve') {
        await approveVersion(current.id, reason.trim() || undefined);
        api.message.success(`${current.pluginId} ${current.version} 已放行`);
      } else {
        await rejectVersion(current.id, reason.trim());
        api.message.success('已驳回');
      }
      setReason('');
      refresh();
    } catch {
      // 失败信息已由统一错误处理展示。
    } finally {
      setBusy(null);
    }
  };

  const sample = async () => {
    if (!current) return;
    try {
      await downloadSample(current.id, `${current.pluginId}-${current.version}.vpx`);
    } catch {
      api.message.error('样本下载失败');
    }
  };

  if (loading && !data) {
    return <Skeleton active paragraph={{ rows: 6 }} />;
  }

  if (items.length === 0) {
    return (
      <Empty description="队列是空的 —— 没有需要人工复核的包" style={{ padding: '64px 0' }}>
        <Button icon={<ReloadOutlined />} onClick={refresh}>
          刷新
        </Button>
      </Empty>
    );
  }

  return (
    <div className="mod-split">
      <div className="mod-queue">
        <div className="mod-queue-head">
          <span>{items.length} 个包等待处置</span>
          <Button type="text" size="small" icon={<ReloadOutlined />} onClick={refresh} loading={loading}>
            刷新
          </Button>
        </div>

        {items.map((item) => (
          <QueueCard
            key={item.id}
            item={item}
            active={current?.id === item.id}
            onSelect={() => {
              setSelected(item.id);
              setReason('');
            }}
          />
        ))}

        <div className="rule-row" style={{ background: 'var(--surface-alt)', borderRadius: 9, padding: '11px 13px' }}>
          <span className="rule-mark chip-neutral">
            <InfoCircleOutlined />
          </span>
          <span style={{ fontSize: 11.5, color: 'var(--ink-3)' }}>审核是多人同时在做的。切换页签会重新拉取列表 —— 拿着五分钟前的列表点按钮只会撞 409。</span>
        </div>
      </div>

      {current ? (
        <div className="mod-detail">
          <header className="mod-detail-head">
            <div>
              <div className="mod-detail-title">
                <h3>{current.pluginId}</h3>
                <span className="mod-detail-version">v{current.version}</span>
                <Chip tone="warn" icon={<ClockCircleOutlined />}>
                  隔离中 · 待人工复核
                </Chip>
              </div>
              <div className="mod-detail-meta">
                <span>{current.uploadedByName ?? current.uploadedBySubject} 上传</span>
                <span>·</span>
                <span className="mono">{formatDateTime(current.uploadedAt)}</span>
                <span>·</span>
                <span className="mono">{formatSize(current.packageSize)}</span>
                <span>·</span>
                <SignatureTag state={current.signature} icon={false} />
                {current.publishedVersions > 0 ? (
                  <>
                    <span>·</span>
                    <span>该插件此前 {current.publishedVersions} 个版本已通过</span>
                  </>
                ) : (
                  <>
                    <span>·</span>
                    <span>这是该插件的第一个版本</span>
                  </>
                )}
              </div>
            </div>

            <div style={{ display: 'flex', gap: 9, flexWrap: 'wrap' }}>
              <Button icon={<DownloadOutlined />} onClick={sample}>
                下载样本
              </Button>
              <Button danger icon={<CloseOutlined />} loading={busy === 'reject'} onClick={() => act('reject')}>
                驳回
              </Button>
              <Button type="primary" icon={<CheckOutlined />} loading={busy === 'approve'} onClick={() => act('approve')}>
                放行并发布
              </Button>
            </div>
          </header>

          <div className="mod-detail-body">
            <section>
              <h4 className="mod-section-title">
                <span>命中项</span>
                <span className="mono" style={{ fontWeight: 400 }}>
                  {Object.entries(current.scan.engines ?? {})
                    .map(([name, version]) => `${name} ${version}`)
                    .join(' · ')}
                </span>
              </h4>
              <Findings findings={current.findings} />
            </section>

            <section>
              <h4 className="mod-section-title">
                <span>包内清单</span>
                {current.scan.entryCount ? <span style={{ fontWeight: 400 }}>{current.scan.entryCount} 个条目</span> : null}
              </h4>
              <EntryList versionId={current.id} />
            </section>

            <section>
              <h4 className="mod-section-title">
                <span>处置原因(驳回必填)</span>
              </h4>
              <Input.TextArea rows={3} value={reason} maxLength={500} showCount onChange={(event) => setReason(event.target.value)} placeholder="写清楚判断依据。驳回时这段话会展示给作者;放行时它会记进检测报告与服务端日志。" />
              <div className="mod-decision-foot" style={{ marginTop: 12 }}>
                {/* 这里刻意不放"是否通知作者"的开关:驳回原因本来就会写进检测报告,
                    而作者在「我的上传」里看的就是那份报告 —— 给一个关不掉的开关是在骗人。 */}
                <span style={{ fontSize: 11.5, color: 'var(--ink-3)' }}>驳回原因会写进检测报告,作者在「我的上传」里能看到。</span>
                <span style={{ fontSize: 11.5, color: 'var(--ink-3)' }}>放行会把包搬进正式桶并立即可下载,这一步不可撤销。</span>
              </div>
            </section>
          </div>
        </div>
      ) : null}
    </div>
  );
}
