import { PLUGIN_GRADIENTS } from '@/configs';

/** 稳定散列:同一个 id 每次都落到同一块渐变上,列表刷新时图标不会跳色。 */
function hash(text: string): number {
  let value = 0;
  for (let i = 0; i < text.length; i += 1) {
    value = (value * 31 + text.charCodeAt(i)) | 0;
  }
  return Math.abs(value);
}

/**
 * 插件图标。市场不存图标资源,用插件 id 映射到一组渐变色块 ——
 * 比整墙灰色首字母更容易在列表里认出来。
 */
export default function PluginIcon({ id, name, size = 44 }: { id: string; name?: string; size?: number }) {
  const initial = (name?.trim() || id).charAt(0).toUpperCase();
  return (
    <span
      className="plugin-icon"
      style={{
        width: size,
        height: size,
        fontSize: Math.round(size * 0.43),
        borderRadius: Math.round(size / 4),
        background: PLUGIN_GRADIENTS[hash(id) % PLUGIN_GRADIENTS.length],
      }}
    >
      {initial}
    </span>
  );
}
