/**
 * 组件目录。统一管理对外输出的组件,页面一律从 `@/components` 取,
 * 不再出现深到三层的相对路径(参考架构的 src/components/index.ts)。
 */
export { default as Brand } from './Brand';
export { default as Chip } from './Chip';
export type { ChipTone } from './Chip';
export { default as Findings } from './Findings';
export { default as PageShell } from './PageShell';
export { default as PipelineStrip } from './PipelineStrip';
export { default as PluginIcon } from './PluginIcon';
export { default as RequireAuth } from './RequireAuth';
export { default as SignatureTag } from './SignatureTag';
export { default as StatusTag } from './StatusTag';
export { default as ThemeProvider } from './ThemeProvider';
export { default as ThemeSwitch } from './ThemeSwitch';
export { default as ThemeSync } from './ThemeSync';
export { default as UserMenu } from './UserMenu';
