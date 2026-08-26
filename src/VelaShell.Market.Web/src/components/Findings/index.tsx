import { SEVERITY_TAG } from '@/configs';
import { Empty, Tooltip } from 'antd';

/**
 * 检测发现列表。上传者与审核员看的是同一份 —— 被拒却看不到原因,只会换来一次次盲目重传,
 * 所以这里连 code 都原样展示(它是稳定的机器可读标识,方便按代码搜文档)。
 *
 * 排序:阻断 → 提醒 → 通过。人先要看见"卡住我的是什么",通过项是背景信息。
 */
const ORDER: Record<MarketAPI.Severity, number> = { Blocking: 0, Warning: 1, Info: 2 };

export default function Findings({ findings }: { findings?: MarketAPI.Finding[] | null }) {
  if (!findings || findings.length === 0) {
    return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="暂无检测结果" />;
  }
  const sorted = [...findings].sort((a, b) => (ORDER[a.severity] ?? 9) - (ORDER[b.severity] ?? 9));
  return (
    <div>
      {sorted.map((finding, index) => {
        const preset = SEVERITY_TAG[finding.severity] ?? { text: finding.severity, tone: 'neutral' as const };
        return (
          <div className="finding-row" key={`${finding.code}-${index}`}>
            <span className={`finding-severity chip-${preset.tone ?? 'neutral'}`}>{preset.text}</span>
            <Tooltip title="检测项代码">
              <span className="finding-code">{finding.code}</span>
            </Tooltip>
            <span className="finding-text">
              {finding.message}
              {finding.path ? <span className="finding-path">{finding.path}</span> : null}
            </span>
          </div>
        );
      })}
    </div>
  );
}
