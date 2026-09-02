/**
 * #31：`<Countdown>` 讓每次載入都 hydration 失敗，整棵 React 樹被丟掉重建。
 *
 * 根因是 `useState(() => Date.now())`：伺服器算一次、瀏覽器 hydration 時再算一次，
 * 兩邊的秒數必然不同（2026-09-02 console 逐字證據 `+137:02:24` / `-137:02:25`）。
 * 功能沒壞，所以任何「這一頁對不對」的檢查都不會抓到它——
 * 跟 #30／#32 同一族：**只有在真瀏覽器裡看 console 才浮得出來**。
 *
 * ── 為什麼是掃原始碼而不是渲染 ──
 * 要真的斷言「伺服器與瀏覽器的第一次渲染輸出相同」得有 jsdom 與
 * `react-dom/server`，這個 workspace 兩樣都沒有，而派工書明文不准加相依。
 * 退而求其次，把**根因的形狀**釘住：初始 state 不可以是即時值。
 * 這一條擋得住「有人覺得佔位不好看，順手改回 `Date.now()`」——那正是它會退化的方式。
 */
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

/** `app/_lib/__tests__/` 往上五層是 `frontend/`。 */
const FRONTEND_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..', '..');
const COUNTDOWN = join(FRONTEND_ROOT, 'packages', 'ui', 'src', 'components', 'Countdown.tsx');

const SOURCE = readFileSync(COUNTDOWN, 'utf8');

/**
 * 去掉註解之後的程式碼。
 *
 * 為什麼需要它：檔案裡那段解釋 #31 的註解**自己就寫著 `Date.now()`**
 * （「初始值一定要是 null，不可以是 `Date.now()`」）。
 * 拿原始文字去斷言「`Date.now()` 只出現在 effect 裡」會被自己的說明文字咬到——
 * 第一次跑就紅了。註解要留著（它是這個決定的理由），所以掃描器要看得懂註解不是程式碼。
 */
const CODE = SOURCE.replace(/\/\*[^]*?\*\//g, '').replace(/\/\/.*$/gm, '');

describe('掃描器本身是活的', () => {
  it('真的讀到 Countdown.tsx，而且它真的是那個元件', () => {
    expect(SOURCE.length).toBeGreaterThan(200);
    expect(SOURCE).toContain('export function Countdown(');
  });
});

describe('#31：伺服器不可以算即時值', () => {
  it('★ 初始 state 不是 Date.now()——那正是 hydration 不一致的根因', () => {
    // `useState(() => Date.now())`、`useState(Date.now())` 兩種寫法都要擋。
    expect(CODE).not.toMatch(/useState\(\s*(\(\)\s*=>\s*)?Date\.now\(\)/);
  });

  it('初始值是 null＝「還不知道現在幾點」', () => {
    expect(CODE).toMatch(/useState<number \| null>\(null\)/);
  });

  it('Date.now() 只出現在 useEffect 裡（瀏覽器才跑得到的地方）', () => {
    const effectBody = /useEffect\(\(\) => \{[^]*?\}, \[\]\);/.exec(CODE)?.[0] ?? '';
    expect(effectBody).toContain('Date.now()');

    const outside = CODE.replace(effectBody, '');
    expect(outside).not.toContain('Date.now()');
  });

  it('★ 佔位不是 00:00:00——那會被讀成「已經歸零」，是宣稱一件不成立的事', () => {
    expect(CODE).toContain("const UNKNOWN_REMAINING = '--:--:--'");
    expect(CODE).not.toMatch(/UNKNOWN_REMAINING = '00:00:00'/);
  });

  it('寬度靠 tabular-nums 維持穩定，掛載後換成真數字不會跳版', () => {
    expect(CODE).toContain('tabular-nums');
  });
});
