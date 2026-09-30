/**
 * 整包唯一的圖示來源。純 inline SVG，不引入任何圖示套件（見 FE-2 交付說明）。
 * 統一 24×24 viewBox、線條風格（stroke = currentColor，stroke-width 1.75），
 * 用 `width`/`height="1em"` 讓大小跟著外部 `font-size`（token）走，不需要寫死 px。
 */

export interface IconProps extends React.SVGAttributes<SVGSVGElement> {
  /** 純裝飾用的圖示要設 true，讓螢幕閱讀器略過；有語意時交給外層的 aria-label。 */
  decorative?: boolean;
}

function BaseIcon({
  decorative = true,
  children,
  ...rest
}: IconProps & { children: React.ReactNode }) {
  return (
    <svg
      width="1em"
      height="1em"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden={decorative ? true : undefined}
      {...rest}
    >
      {children}
    </svg>
  );
}

export function IconHeart({ filled, ...props }: IconProps & { filled?: boolean }) {
  return (
    <BaseIcon {...props} fill={filled ? 'currentColor' : 'none'}>
      <path d="M12 20.5s-7.5-4.6-10-9.3C.6 8.1 2 4.5 5.5 3.8 8 3.3 10.3 4.6 12 7c1.7-2.4 4-3.7 6.5-3.2 3.5.7 4.9 4.3 3.5 7.4-2.5 4.7-10 9.3-10 9.3Z" />
    </BaseIcon>
  );
}

export function IconSearch(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <circle cx="11" cy="11" r="7" />
      <path d="m21 21-4.3-4.3" />
    </BaseIcon>
  );
}

export function IconEye(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M2 12s3.5-6 10-6 10 6 10 6-3.5 6-10 6-10-6-10-6Z" />
      <circle cx="12" cy="12" r="2.5" />
    </BaseIcon>
  );
}

export function IconEyeOff(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M3 3l18 18" />
      <path d="M10.6 6.2A11 11 0 0 1 12 6c6.5 0 10 6 10 6a18 18 0 0 1-3.2 3.8" />
      <path d="M6.6 6.6C3.7 8.4 2 12 2 12s3.5 6 10 6c1.5 0 2.8-.3 4-.8" />
      <path d="M9.9 9.9a3 3 0 0 0 4.2 4.2" />
    </BaseIcon>
  );
}

export function IconX(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M6 6l12 12M18 6 6 18" />
    </BaseIcon>
  );
}

export function IconMenu(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M4 6h16M4 12h16M4 18h16" />
    </BaseIcon>
  );
}

export function IconPlus(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M12 5v14M5 12h14" />
    </BaseIcon>
  );
}

export function IconMinus(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M5 12h14" />
    </BaseIcon>
  );
}

export function IconChevronDown(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="m6 9 6 6 6-6" />
    </BaseIcon>
  );
}

export function IconChevronLeft(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="m15 6-6 6 6 6" />
    </BaseIcon>
  );
}

export function IconChevronRight(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="m9 6 6 6-6 6" />
    </BaseIcon>
  );
}

export function IconCheckCircle(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <circle cx="12" cy="12" r="9" />
      <path d="m8.5 12.5 2.5 2.5 5-5" />
    </BaseIcon>
  );
}

export function IconAlertCircle(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 8v5M12 16h.01" />
    </BaseIcon>
  );
}

export function IconAlertTriangle(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M12 3.5 2 20.5h20L12 3.5Z" />
      <path d="M12 10v4M12 17h.01" />
    </BaseIcon>
  );
}

export function IconInbox(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M4 12h4l1.5 3h5L16 12h4" />
      <path d="M5.5 5h13L21 12v6a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-6L5.5 5Z" />
    </BaseIcon>
  );
}

export function IconUser(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <circle cx="12" cy="8" r="3.5" />
      <path d="M5 20c1.2-3.5 4-5.5 7-5.5s5.8 2 7 5.5" />
    </BaseIcon>
  );
}

/*
 * ── 以下三顆是 FE-23 暫放在 `apps/storefront/app/_components/TabBarIcons.tsx`、
 *    FE-24 搬回來的 ──
 * 那個檔的檔頭自己寫著「本來就該進來，只是 FE-23 動不了 packages/ui」。
 * 第四顆（分頁列的「我的」）沒有搬——它的兩條 path 與上面的 `IconUser` **逐字相同**，
 * 搬過來只會多一顆長得一樣的圖示，違反這個檔案「整包唯一的圖示來源」的用意。
 * 第五顆不用新增：頂部列的購物車用的就是下面這顆 `IconCart`，與分頁列同一顆。
 */

/** 首頁。 */
export function IconHome(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M3.5 10.5 12 3.5l8.5 7" />
      <path d="M5.5 9.5V20h13V9.5" />
      <path d="M9.5 20v-5.5h5V20" />
    </BaseIcon>
  );
}

/** 開團。一起買＝一群人，用兩個人的剪影。 */
export function IconUsers(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <circle cx="9" cy="8" r="3.25" />
      <path d="M3 19.5c1-3.2 3.4-5 6-5s5 1.8 6 5" />
      <path d="M16 5.5a3.25 3.25 0 0 1 0 6.4" />
      <path d="M17.5 14.9c1.9.7 3.2 2.3 3.8 4.6" />
    </BaseIcon>
  );
}

/** 購物車。分頁列與頂部列共用**同一顆**。 */
export function IconCart(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M3 4h2.2l2.3 10.5h9.4L19 7H6" />
      <circle cx="9.5" cy="19" r="1.4" />
      <circle cx="16.5" cy="19" r="1.4" />
    </BaseIcon>
  );
}

/** 對話框氣泡。FE-35 客服小幫手用。 */
export function IconMessageCircle(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M3.5 12c0-4.7 3.8-8.5 8.5-8.5s8.5 3.8 8.5 8.5-3.8 8.5-8.5 8.5c-1.2 0-2.4-.25-3.4-.7L4 21l1.3-4.2A8.4 8.4 0 0 1 3.5 12Z" />
      <path d="M8 11h8M8 14h5" />
    </BaseIcon>
  );
}
