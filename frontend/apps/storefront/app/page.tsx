/*
 * 骨架頁。**這頁會被前端工作包 FE-2 整個換掉**，不要在這裡長出業務邏輯。
 *
 * 它存在的唯一理由是讓 `pnpm dev` 現在就跑得起來，
 * 並且在畫面上證明 token 有接上（顏色、圓角、字體都不是預設值就代表對了）。
 */
export default function Home() {
  return (
    <main className="mx-auto max-w-[1200px] px-4 py-10">
      <div
        className="rounded-xl p-8"
        style={{ background: 'var(--gg-gradient-banner)' }}
      >
        <h1 className="text-2xl font-extrabold text-fg">GreyGray</h1>
        <p className="mt-2 text-fg-muted">
          Soft Seoul 骨架已就緒。這頁是佔位用的，會被 FE-2 換掉。
        </p>
      </div>

      <section className="mt-8 rounded-card bg-surface p-6 shadow-card">
        <h2 className="font-display text-xl font-bold text-fg">Token 檢查</h2>
        <ul className="mt-3 space-y-1 text-sm text-fg-muted">
          <li>底色是淺粉（--gg-bg），不是白色</li>
          <li>卡片圓角 16px、陰影柔和無硬邊</li>
          <li>數字 0123456789 應為 Nunito，中文應為 Noto Sans TC</li>
        </ul>
        <button
          type="button"
          className="mt-5 rounded-pill bg-primary px-6 text-on-primary transition-colors duration-200 hover:bg-primary-hover"
        >
          主要按鈕（白字對比 4.60:1）
        </button>
      </section>
    </main>
  );
}
