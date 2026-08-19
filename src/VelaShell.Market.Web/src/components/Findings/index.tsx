import { Empty, Space, Tag, Tooltip, Typography } from 'antd';

const color: Record<MarketAPI.Finding['severity'], string> = {
  Blocking: 'error',
  Warning: 'warning',
  Info: 'default',
};

const label: Record<MarketAPI.Finding['severity'], string> = {
  Blocking: '阻断',
  Warning: '待复核',
  Info: '提示',
};

export const verdictTag: Record<string, { color: string; text: string }> = {
  Pending: { color: 'default', text: '排队中' },
  Passed: { color: 'success', text: '通过' },
  NeedsReview: { color: 'warning', text: '待人工复核' },
  Failed: { color: 'error', text: '未通过' },
  Errored: { color: 'error', text: '检测出错' },
};

export const statusTag: Record<string, { color: string; text: string }> = {
  Quarantined: { color: 'processing', text: '隔离中' },
  Scanning: { color: 'processing', text: '检测中' },
  Rejected: { color: 'error', text: '已拒收' },
  Published: { color: 'success', text: '已发布' },
  Withdrawn: { color: 'default', text: '已撤回' },
};

/**
 * 检测发现列表。上传者与审核员看的是同一份 —— 被拒却看不到原因,只会换来一次次盲目重传,
 * 所以这里连 code 都原样展示(它是稳定的机器可读标识,方便按代码搜文档)。
 */
export default function Findings({ findings }: { findings: MarketAPI.Finding[] }) {
  if (!findings || findings.length === 0) {
    return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="暂无检测结果" />;
  }
  return (
    <div>
      {findings.map((f, index) => (
        <div className="finding-row" key={`${f.code}-${index}`}>
          <Tag color={color[f.severity]} bordered={false} style={{ minWidth: 58, textAlign: 'center' }}>
            {label[f.severity]}
          </Tag>
          <Space direction="vertical" size={2} style={{ flex: 1, minWidth: 0 }}>
            <Space size={8} wrap>
              <Tooltip title="检测项代码">
                <span className="finding-code">{f.code}</span>
              </Tooltip>
              <Typography.Text>{f.message}</Typography.Text>
            </Space>
            {f.path ? <span className="finding-path">{f.path}</span> : null}
          </Space>
        </div>
      ))}
    </div>
  );
}
