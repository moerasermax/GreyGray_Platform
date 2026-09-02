/**
 * 分頁列的四個圖示 —— **只剩名稱到圖示的對照，SVG 已經搬進 `@greygray/ui`。**
 *
 * FE-23 因為 `packages/ui` 不在它的 `allow:` 裡，把四顆 SVG 暫放在這個檔案，
 * 檔頭寫著「搬家時可以直接貼過去」。FE-24 動得了 `packages/ui`，所以搬了：
 * `IconHome`／`IconUsers`／`IconCart` 進 `packages/ui/src/components/icons/index.tsx`，
 * 第四顆沒有新增——它的兩條 path 與那裡既有的 `IconUser` **逐字相同**。
 *
 * 這個檔案留下來只做一件事：把 `TabIconName` 這個字串對到圖示。
 * 那個型別是分頁列自己的資料（`_lib/tabs.ts` 的 `STOREFRONT_TABS`），
 * 不該讓共用圖示庫去認識它。**沒有第二份 SVG**——頂部列的購物車圖示
 * 與這裡的 `cart` 是同一顆 `IconCart`。
 */
import { IconCart, IconHome, IconUser, IconUsers } from '@greygray/ui';
import type { TabIconName } from '../_lib/tabs';

const ICONS: Record<TabIconName, (props: { className?: string }) => React.ReactElement> = {
  home: IconHome,
  campaign: IconUsers,
  cart: IconCart,
  account: IconUser,
};

/** 依名稱取圖示。名稱來自 `STOREFRONT_TABS`，型別上不可能落空。 */
export function TabBarIcon({ name }: { name: TabIconName }) {
  const Icon = ICONS[name];
  return <Icon />;
}
