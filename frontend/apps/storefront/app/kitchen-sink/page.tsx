'use client';

/*
 * FE-2 驗收頁。展示 packages/ui 元件庫的所有元件與主要狀態。
 * **暫時的**，交付後由 FE-3 刪掉（docs/06-前端工作包.md FE-2）。
 *
 * 路徑刻意是 `app/kitchen-sink/`，不是文件裡寫的 `app/_kitchen-sink/`——
 * 底線開頭在 App Router 是 private folder，不會產生路由。
 */

import { useState } from 'react';
import {
  Avatar,
  Badge,
  BottomActionBar,
  BottomSheet,
  Button,
  Card,
  CategoryChip,
  Countdown,
  Dialog,
  EmptyState,
  ErrorState,
  FavoriteHeart,
  Field,
  IconButton,
  IconChevronLeft,
  IconChevronRight,
  IconHeart,
  IconPlus,
  Input,
  PriceDisplay,
  ProductCard,
  QuantityStepper,
  SearchBar,
  Select,
  Skeleton,
  Spinner,
  Tabs,
  Textarea,
  Toast,
  type TabItem,
} from '@greygray/ui';

function placeholderImage(label: string, bg: string): string {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="200" height="200">
    <rect width="200" height="200" fill="${bg}" />
    <text x="100" y="105" font-family="sans-serif" font-size="20" fill="#831843" text-anchor="middle">${label}</text>
  </svg>`;
  return `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;
}

const PRODUCT_IMAGE = placeholderImage('商品', '#fbcfe8');
const CATEGORY_IMAGE = placeholderImage('分類', '#ddd6fe');

const TAB_ITEMS: TabItem[] = [
  { value: 'all', label: '全部' },
  { value: 'preorder', label: '預購' },
  { value: 'stock', label: '現貨' },
  { value: 'soldout', label: '已停售（不可選）', disabled: true },
];

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="flex flex-col gap-[var(--gg-space-4)]">
      <h2 className="font-display text-[length:var(--gg-text-xl)] font-bold text-fg">{title}</h2>
      {children}
    </section>
  );
}

export default function KitchenSinkPage() {
  const [favorited, setFavorited] = useState(false);
  const [productFavorited, setProductFavorited] = useState(true);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [sheetOpen, setSheetOpen] = useState(false);
  const [successToastOpen, setSuccessToastOpen] = useState(false);
  const [errorToastOpen, setErrorToastOpen] = useState(false);
  const [quantity, setQuantity] = useState(1);
  const [searchValue, setSearchValue] = useState('');
  const [tabValue, setTabValue] = useState('all');
  const [categorySelected, setCategorySelected] = useState('beauty');
  const [nameValue, setNameValue] = useState('');
  const [selectValue, setSelectValue] = useState('7-11');
  const [noteValue, setNoteValue] = useState('');

  const futureClosesAt = new Date(Date.now() + 2 * 60 * 60 * 1000 + 15 * 1000).toISOString();
  const pastClosesAt = new Date(Date.now() - 60 * 1000).toISOString();

  return (
    <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-8)] px-[var(--gg-space-4)] py-[var(--gg-space-8)] pb-[calc(var(--gg-bottom-bar-height)+var(--gg-space-8))]">

      <header className="flex flex-col gap-[var(--gg-space-2)]">
        <h1 className="font-display text-[length:var(--gg-text-3xl)] font-extrabold text-fg">
          Kitchen Sink —— Soft Seoul 元件庫
        </h1>
        <p className="text-fg-muted">FE-2 驗收頁，展示 packages/ui 全部元件的主要狀態。</p>
      </header>

      <Section title="Button">
        <div className="flex flex-wrap items-center gap-[var(--gg-space-3)]">
          <Button variant="primary">Primary</Button>
          <Button variant="secondary">Secondary</Button>
          <Button variant="ghost">Ghost</Button>
          <Button variant="danger">Danger</Button>
          <Button variant="primary" loading>
            處理中
          </Button>
          <Button variant="primary" disabled>
            已停用
          </Button>
        </div>
        <div className="flex flex-wrap items-center gap-[var(--gg-space-3)]">
          <Button variant="primary" size="sm">
            Small
          </Button>
          <Button variant="primary" size="md">
            Medium
          </Button>
          <Button variant="primary" size="lg">
            Large
          </Button>
        </div>
      </Section>

      <Section title="IconButton">
        <div className="flex flex-wrap items-center gap-[var(--gg-space-3)]">
          <IconButton icon={<IconHeart />} aria-label="收藏" variant="primary" />
          <IconButton icon={<IconChevronLeft />} aria-label="上一頁" variant="secondary" />
          <IconButton icon={<IconChevronRight />} aria-label="下一頁" variant="ghost" />
          <IconButton icon={<IconPlus />} aria-label="新增" variant="danger" disabled />
        </div>
      </Section>

      <Section title="Card">
        <div className="grid grid-cols-1 gap-[var(--gg-space-4)] sm:grid-cols-2">
          <Card>
            <p className="font-bold text-fg">一般卡片</p>
            <p className="text-fg-muted">圓角 16px、柔陰影、無硬邊。</p>
          </Card>
          <Card raised>
            <p className="font-bold text-fg">raised 卡片</p>
            <p className="text-fg-muted">陰影更明顯，用在浮起的內容上。</p>
          </Card>
        </div>
      </Section>

      <Section title="ProductCard">
        <div className="grid grid-cols-2 gap-[var(--gg-space-4)] sm:grid-cols-3 md:grid-cols-4">
          <ProductCard
            imageSrc={PRODUCT_IMAGE}
            imageAlt="日本藥妝面膜"
            name="日本藥妝保濕面膜 10 片裝"
            description="高保濕玻尿酸配方，敏感肌適用。"
            price={{ amountMinor: 78000, currency: 'TWD' }}
            compareAtPrice={{ amountMinor: 98000, currency: 'TWD' }}
            unitPriceLabel="每片 NT$78"
            badges={[{ variant: 'Popular' }, { variant: 'New' }]}
            favorited={productFavorited}
            onToggleFavorite={() => setProductFavorited((v) => !v)}
            onClick={() => setDialogOpen(true)}
          />
          <ProductCard
            imageSrc={PRODUCT_IMAGE}
            imageAlt="韓國美妝精華"
            name="韓國安瓶精華 30ml"
            description="即將截團，預購商品，下單後兩週到貨。"
            price={{ amountMinor: 129000, currency: 'TWD' }}
            badges={[{ variant: 'LastCall' }, { variant: '限定色', label: '限定色' }]}
            favorited={false}
          />
        </div>
      </Section>

      <Section title="CategoryChip（橫捲 + scroll-snap）">
        <div className="flex snap-x gap-[var(--gg-space-4)] overflow-x-auto pb-[var(--gg-space-2)]">
          {['美妝', '藥品', '食品', '日用品', '家電'].map((label, index) => {
            const value = `${label}-${index}`;
            return (
              <CategoryChip
                key={value}
                imageSrc={CATEGORY_IMAGE}
                imageAlt={label}
                label={label}
                selected={categorySelected === value}
                onClick={() => setCategorySelected(value)}
              />
            );
          })}
        </div>
      </Section>

      <Section title="Badge（含未知值容忍）">
        <div className="flex flex-wrap gap-[var(--gg-space-2)]">
          <Badge variant="New" />
          <Badge variant="Popular" />
          <Badge variant="LastCall" />
          <Badge variant="未知標籤" />
        </div>
      </Section>

      <Section title="FavoriteHeart">
        <FavoriteHeart pressed={favorited} onToggle={() => setFavorited((v) => !v)} />
      </Section>

      <Section title="SearchBar">
        <SearchBar value={searchValue} onChange={setSearchValue} placeholder="搜尋商品、開團" />
      </Section>

      <Section title="Avatar">
        <div className="flex items-center gap-[var(--gg-space-3)]">
          <Avatar alt="林小姐" name="林小姐" size="sm" />
          <Avatar alt="陳先生" name="陳先生" size="md" />
          <Avatar alt="沒有名字的訪客" size="lg" />
        </div>
      </Section>

      <Section title="PriceDisplay">
        <div className="flex flex-wrap items-center gap-[var(--gg-space-5)]">
          <PriceDisplay amount={{ amountMinor: 18000, currency: 'TWD' }} />
          <PriceDisplay
            amount={{ amountMinor: 18050, currency: 'TWD' }}
            showDecimals
            size="lg"
          />
          <PriceDisplay amount={{ amountMinor: 1000, currency: 'JPY' }} size="sm" />
          <PriceDisplay
            amount={{ amountMinor: 78000, currency: 'TWD' }}
            compareAtAmount={{ amountMinor: 98000, currency: 'TWD' }}
          />
        </div>
      </Section>

      <Section title="QuantityStepper">
        <QuantityStepper value={quantity} min={1} max={10} onChange={setQuantity} />
      </Section>

      <Section title="Dialog / BottomSheet / Toast">
        <div className="flex flex-wrap gap-[var(--gg-space-3)]">
          <Button onClick={() => setDialogOpen(true)}>開啟 Dialog</Button>
          <Button variant="secondary" onClick={() => setSheetOpen(true)}>
            開啟 BottomSheet
          </Button>
          <Button variant="secondary" onClick={() => setSuccessToastOpen(true)}>
            顯示成功 Toast
          </Button>
          <Button variant="danger" onClick={() => setErrorToastOpen(true)}>
            顯示失敗 Toast
          </Button>
        </div>

        <div className="flex flex-col gap-[var(--gg-space-2)]">
          <Toast
            open={successToastOpen}
            variant="success"
            message="訂單已送出。"
            onClose={() => setSuccessToastOpen(false)}
            duration={4000}
          />
          <Toast
            open={errorToastOpen}
            variant="error"
            message="庫存不足，無法送出訂單。"
            onClose={() => setErrorToastOpen(false)}
          />
        </div>

        <Dialog
          open={dialogOpen}
          onClose={() => setDialogOpen(false)}
          title="加入購物車"
          footer={
            <>
              <Button variant="secondary" onClick={() => setDialogOpen(false)}>
                取消
              </Button>
              <Button onClick={() => setDialogOpen(false)}>確認加入</Button>
            </>
          }
        >
          <p className="text-fg-muted">Esc 可以關閉，Tab 只會在對話框內循環。</p>
        </Dialog>

        <BottomSheet open={sheetOpen} onClose={() => setSheetOpen(false)} title="選擇配送方式">
          <div className="flex flex-col gap-[var(--gg-space-2)]">
            <Button variant="secondary" onClick={() => setSheetOpen(false)}>
              超商取貨 NT$60
            </Button>
            <Button variant="secondary" onClick={() => setSheetOpen(false)}>
              宅配到府 NT$120
            </Button>
          </div>
        </BottomSheet>
      </Section>

      <Section title="Skeleton（保留與實際內容相同高度）">
        <div className="grid grid-cols-2 gap-[var(--gg-space-4)] sm:grid-cols-4">
          <div className="flex flex-col gap-[var(--gg-space-2)]">
            <Skeleton variant="block" className="aspect-square w-full" />
            <Skeleton variant="text" style={{ height: 'var(--gg-text-base)' }} className="w-3/4" />
            <Skeleton variant="text" style={{ height: 'var(--gg-text-sm)' }} className="w-1/2" />
          </div>
          <Skeleton variant="circle" className="h-[var(--gg-space-8)] w-[var(--gg-space-8)]" />
        </div>
      </Section>

      <Section title="EmptyState">
        <Card padding="none">
          <EmptyState
            title="目前沒有收藏商品"
            description="逛逛商品，把喜歡的加進收藏清單。"
            action={<Button size="sm">去逛逛</Button>}
          />
        </Card>
      </Section>

      <Section title="ErrorState">
        <Card padding="none">
          <ErrorState
            title="無法載入商品，請稍後再試。"
            traceId="00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
            onRetry={() => window.location.reload()}
          />
        </Card>
      </Section>

      <Section title="Countdown">
        <div className="flex flex-wrap items-center gap-[var(--gg-space-5)]">
          <Countdown closesAt={futureClosesAt} isAcceptingOrders />
          <Countdown closesAt={pastClosesAt} isAcceptingOrders={false} />
        </div>
      </Section>

      <Section title="Field / Input / Select / Textarea">
        <div className="grid grid-cols-1 gap-[var(--gg-space-4)] sm:grid-cols-2">
          <Field label="收件人姓名" htmlFor="ks-name" required hint="請填寫身分證上的姓名">
            <Input
              id="ks-name"
              value={nameValue}
              onChange={(e) => setNameValue(e.target.value)}
              placeholder="王小美"
            />
          </Field>
          <Field label="手機號碼" htmlFor="ks-phone" error="手機號碼格式不正確">
            <Input id="ks-phone" invalid defaultValue="091234" />
          </Field>
          <Field label="取貨方式" htmlFor="ks-shipping">
            <Select
              id="ks-shipping"
              value={selectValue}
              onChange={(e) => setSelectValue(e.target.value)}
            >
              <option value="7-11">7-ELEVEN 超商取貨</option>
              <option value="family">全家 超商取貨</option>
              <option value="home">宅配到府</option>
            </Select>
          </Field>
          <Field label="備註" htmlFor="ks-note" hint="選填，例如集運需求">
            <Textarea
              id="ks-note"
              value={noteValue}
              onChange={(e) => setNoteValue(e.target.value)}
              placeholder="有其他需求嗎？"
            />
          </Field>
        </div>
      </Section>

      <Section title="Tabs">
        <Tabs items={TAB_ITEMS} value={tabValue} onChange={setTabValue} />
        <p className="text-fg-muted">目前選到：{tabValue}</p>
      </Section>

      <Section title="Spinner">
        <div className="flex items-center gap-[var(--gg-space-4)] text-[length:var(--gg-text-2xl)] text-primary">
          <Spinner label="載入中" />
        </div>
      </Section>

      <BottomActionBar>
        <div className="flex flex-1 items-center justify-between gap-[var(--gg-space-3)]">
          {/* 含運總額一律是後端算好回傳的數字，這裡固定寫死示範，不對 amountMinor 做任何運算。 */}
          <PriceDisplay amount={{ amountMinor: 78000, currency: 'TWD' }} size="lg" />
          <Button variant="primary">加入購物車（數量 {quantity}）</Button>
        </div>
      </BottomActionBar>
    </main>
  );
}
