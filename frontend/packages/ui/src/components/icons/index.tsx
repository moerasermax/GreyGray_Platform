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

export function IconX(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M6 6l12 12M18 6 6 18" />
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
