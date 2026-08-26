import type { ReactNode } from 'react';

/** 芯片的语气。与 global.less 里的 `.chip-*` 一一对应。 */
export type ChipTone = 'neutral' | 'ok' | 'warn' | 'danger' | 'accent';

/**
 * 全站统一的状态芯片:发布状态、检测结论、签名状态、编辑推荐,长得都是这一个。
 *
 * 为什么不直接用 `<Tag>`:antd 的 Tag 有自己一套 color 体系(success/warning/…),
 * 它算出来的底色与本设计的 `--ok-soft` / `--warn-soft` 那组值对不上,深色下差得更远。
 * 状态标记在这个站里出现的密度极高(每张卡片、每一行表格、每条检测项都有),
 * 差一档底色就会到处都看得出来,所以这一个收成自己的组件,颜色只认 CSS 变量。
 */
export default function Chip({ tone = 'neutral', icon, children, title, plain }: { tone?: ChipTone; icon?: ReactNode; children: ReactNode; title?: string; plain?: boolean }) {
  return (
    <span className={`chip chip-${tone}${plain ? ' chip-plain' : ''}`} title={title}>
      {icon}
      {children}
    </span>
  );
}
