/**
 * 分頁列的四個圖示。
 *
 * ── 為什麼放在這裡而不是 `packages/ui/src/components/icons` ──
 * 那才是「整包唯一的圖示來源」，這四個本來就該進去。但 `packages/ui` 不在 FE-23 的
 * `allow:` 清單裡，這一包動不了它。折衷是**四個一起放在這裡、共用同一個 base**：
 * 四個並排的圖示彼此一致最要緊，如果只把 `IconUser` 從 `@greygray/ui` 借過來，
 * 哪天那邊改了線寬，四顆裡就會有一顆跟另外三顆不一樣。
 * 規格（24×24 viewBox、`stroke = currentColor`、線寬 1.75、`1em` 尺寸）
 * 逐項比照 `packages/ui/src/components/icons/index.tsx`，搬家時可以直接貼過去。
 */
import type { TabIconName } from '../_lib/tabs';

function BaseTabIcon({ children }: { children: React.ReactNode }) {
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
      aria-hidden={true}
    >
      {children}
    </svg>
  );
}

/** 首頁。 */
function IconTabHome() {
  return (
    <BaseTabIcon>
      <path d="M3.5 10.5 12 3.5l8.5 7" />
      <path d="M5.5 9.5V20h13V9.5" />
      <path d="M9.5 20v-5.5h5V20" />
    </BaseTabIcon>
  );
}

/** 開團。一起買＝一群人，用兩個人的剪影。 */
function IconTabCampaign() {
  return (
    <BaseTabIcon>
      <circle cx="9" cy="8" r="3.25" />
      <path d="M3 19.5c1-3.2 3.4-5 6-5s5 1.8 6 5" />
      <path d="M16 5.5a3.25 3.25 0 0 1 0 6.4" />
      <path d="M17.5 14.9c1.9.7 3.2 2.3 3.8 4.6" />
    </BaseTabIcon>
  );
}

/** 購物車。 */
function IconTabCart() {
  return (
    <BaseTabIcon>
      <path d="M3 4h2.2l2.3 10.5h9.4L19 7H6" />
      <circle cx="9.5" cy="19" r="1.4" />
      <circle cx="16.5" cy="19" r="1.4" />
    </BaseTabIcon>
  );
}

/** 我的。 */
function IconTabAccount() {
  return (
    <BaseTabIcon>
      <circle cx="12" cy="8" r="3.5" />
      <path d="M5 20c1.2-3.5 4-5.5 7-5.5s5.8 2 7 5.5" />
    </BaseTabIcon>
  );
}

const ICONS: Record<TabIconName, () => React.ReactElement> = {
  home: IconTabHome,
  campaign: IconTabCampaign,
  cart: IconTabCart,
  account: IconTabAccount,
};

/** 依名稱取圖示。名稱來自 `STOREFRONT_TABS`，型別上不可能落空。 */
export function TabBarIcon({ name }: { name: TabIconName }) {
  const Icon = ICONS[name];
  return <Icon />;
}
