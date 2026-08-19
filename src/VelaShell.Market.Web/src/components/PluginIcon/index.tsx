/**
 * 插件图标。市场不存图标资源,用插件 id 稳定映射到一组渐变色块 ——
 * 同一插件每次都长一个样,比整墙灰色首字母更容易在列表里认出来。
 */
const GRADIENTS = [
  'linear-gradient(135deg, #4f46e5, #7c3aed)',
  'linear-gradient(135deg, #0ea5e9, #2563eb)',
  'linear-gradient(135deg, #059669, #0d9488)',
  'linear-gradient(135deg, #ea580c, #dc2626)',
  'linear-gradient(135deg, #d946ef, #9333ea)',
  'linear-gradient(135deg, #f59e0b, #ea580c)',
  'linear-gradient(135deg, #06b6d4, #0891b2)',
  'linear-gradient(135deg, #e11d48, #be185d)',
];

const hash = (text: string) => {
  let h = 0;
  for (let i = 0; i < text.length; i += 1) {
    h = (h * 31 + text.charCodeAt(i)) | 0;
  }
  return Math.abs(h);
};

export default function PluginIcon({
  id,
  name,
  size = 44,
}: {
  id: string;
  name?: string;
  size?: number;
}) {
  const initial = (name?.trim() || id).charAt(0).toUpperCase();
  return (
    <span
      className="plugin-icon"
      style={{
        width: size,
        height: size,
        fontSize: Math.round(size * 0.43),
        borderRadius: Math.round(size / 4),
        background: GRADIENTS[hash(id) % GRADIENTS.length],
      }}
    >
      {initial}
    </span>
  );
}
