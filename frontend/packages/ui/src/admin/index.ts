/**
 * 後台元件庫（FE-6）。前台是 `packages/ui/src/index.ts`／`components/**`（FE-2 的地盤），
 * 後台是這裡——密、中性、有深色模式，token 見 `tokens/admin.css`。
 *
 * 對外只從這個 barrel 匯出。`apps/admin` 用 `@greygray/ui/admin` 匯入
 * （見 `apps/admin/tsconfig.json` 的 `paths`——`packages/ui/package.json` 的
 * `exports` 欄位是共用檔，這一波先不動它，等整合時再補正式的 subpath）。
 */

export * from './icons';
export * from './useFocusTrap';
export * from './MoneyCell';
export * from './DirectionCell';
export * from './StatusPill';
export * from './EmptyState';
export * from './ErrorState';
export * from './KpiTile';
export * from './DataTable';
export * from './Field';
export * from './DateRangePicker';
export * from './FilterBar';
export * from './Dialog';
export * from './Drawer';
export * from './Toast';
export * from './AppShell';
