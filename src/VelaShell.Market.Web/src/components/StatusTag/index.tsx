import type { TagPreset } from '@/configs';
import { Tag, Tooltip } from 'antd';

/**
 * 把后端返回的状态字符串按预设表渲染成标签。
 *
 * 表里查不到的值**原样显示**而不是吞掉:后端加了新状态时,页面上会出现一个没配色的
 * 生名字,一眼就知道该来补映射;吞掉的话只会看到一片"—",谁都发现不了。
 */
export default function StatusTag({ value, presets, fallback = '—' }: { value?: string | null; presets: Record<string, TagPreset & { tip?: string }>; fallback?: string }) {
  if (!value) return <>{fallback}</>;
  const preset = presets[value] ?? { text: value };
  const tag = (
    <Tag color={preset.color} bordered={false}>
      {preset.text}
    </Tag>
  );
  return preset.tip ? <Tooltip title={preset.tip}>{tag}</Tooltip> : tag;
}
