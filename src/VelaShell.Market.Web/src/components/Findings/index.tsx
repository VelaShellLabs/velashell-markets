import { SEVERITY_TAG } from '@/configs';
import { Empty, Space, Tag, Tooltip, Typography } from 'antd';

/**
 * 检测发现列表。上传者与审核员看的是同一份 —— 被拒却看不到原因,只会换来一次次盲目重传,
 * 所以这里连 code 都原样展示(它是稳定的机器可读标识,方便按代码搜文档)。
 */
export default function Findings({ findings }: { findings?: MarketAPI.Finding[] | null }) {
  if (!findings || findings.length === 0) {
    return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="暂无检测结果" />;
  }
  return (
    <div>
      {findings.map((finding, index) => {
        const preset = SEVERITY_TAG[finding.severity] ?? { text: finding.severity };
        return (
          <div className="finding-row" key={`${finding.code}-${index}`}>
            <Tag color={preset.color} bordered={false} className="finding-severity">
              {preset.text}
            </Tag>
            <Space direction="vertical" size={2} style={{ flex: 1, minWidth: 0 }}>
              <Space size={8} wrap>
                <Tooltip title="检测项代码">
                  <span className="finding-code">{finding.code}</span>
                </Tooltip>
                <Typography.Text>{finding.message}</Typography.Text>
              </Space>
              {finding.path ? <span className="finding-path">{finding.path}</span> : null}
            </Space>
          </div>
        );
      })}
    </div>
  );
}
