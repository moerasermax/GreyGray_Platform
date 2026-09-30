/**
 * 資訊頁（常見問題／購買流程／關於我們／服務條款）的入口、出口與文案紅線。
 *
 * 照 `_lib/__tests__/pageShell.test.ts` 的精神：**去原始碼掃，不抄清單；
 * 掃到零個算失敗**。這個專案已經在同一個位置踩過三次「每一頁單獨看都對，
 * 串起來走不通」（#30 前台無導覽、#32 三頁無出口、`/wallet` 零入口），
 * 所以入口／出口不是「看起來有連結就好」，是明確斷言連得到哪裡。
 */
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { shouldShowTabBar } from '../../_lib/tabs';
import { shouldShowTopBar } from '../../_lib/topBar';
import { ABOUT_PARAGRAPHS } from '../_content/about';
import { FAQ_GROUPS } from '../_content/faq';
import { GUIDE_SECTIONS } from '../_content/guide';
import { TERMS_SECTIONS, TERMS_LAST_UPDATED } from '../_content/terms';
import { PRIVACY_SECTIONS, PRIVACY_LAST_UPDATED } from '../_content/privacy';
import { INFO_LINKS } from '../_components/InfoLinks';

/** `(info)/__tests__/` 往上兩層就是 `apps/storefront/app`。 */
const APP_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..');

function readApp(relativePath: string): string {
  return readFileSync(join(APP_ROOT, relativePath), 'utf8');
}

const INFO_PAGES = [
  { route: '/faq', file: '(info)/faq/page.tsx', title: '常見問題' },
  { route: '/guide', file: '(info)/guide/page.tsx', title: '購買流程' },
  { route: '/about', file: '(info)/about/page.tsx', title: '關於我們' },
  { route: '/terms', file: '(info)/terms/page.tsx', title: '服務條款' },
  { route: '/privacy', file: '(info)/privacy/page.tsx', title: '隱私權政策' },
] as const;

describe('所有資訊頁都存在，恰好有一種殼', () => {
  it.each(INFO_PAGES)('$route：分頁列顯示、頂部列不顯示', ({ route }) => {
    expect(shouldShowTabBar(route)).toBe(true);
    expect(shouldShowTopBar(route)).toBe(false);
  });

  it.each(INFO_PAGES)('$file 存在且讀得到內容', ({ file }) => {
    expect(readApp(file).length).toBeGreaterThan(0);
  });
});

describe('metadata.title 只寫短標題', () => {
  it.each(INFO_PAGES)('$file 的 title 是 "$title"，不含 GreyGray（根 layout 的 template 會補）', ({ file, title }) => {
    const source = readApp(file);
    const match = source.match(/title:\s*'([^']*)'/);
    expect(match).not.toBeNull();
    expect(match?.[1]).toBe(title);
    expect(match?.[1]).not.toContain('GreyGray');
  });
});

describe('入口：匿名訪客與登入使用者都連得到所有資訊頁', () => {
  it('首頁（(shop)/page.tsx）渲染 InfoLinks——匿名訪客的唯一入口', () => {
    expect(readApp('(shop)/page.tsx')).toMatch(/<InfoLinks\b/);
  });

  it('「我的」頁（(account)/me/page.tsx）渲染 InfoLinks，並多一筆 /favorites 連結', () => {
    const source = readApp('(account)/me/page.tsx');
    expect(source).toMatch(/<InfoLinks\b/);
    expect(source).toContain("href: '/favorites'");
  });

  it('InfoLinks 本身就是資訊頁的清單——上面兩個入口最終落地在這裡', () => {
    expect(INFO_LINKS.map((link) => link.href).sort()).toEqual(['/about', '/faq', '/guide', '/privacy', '/terms']);
  });
});

describe('出口：每個資訊頁都連得到其他頁與首頁', () => {
  it.each(INFO_PAGES)('$file 渲染 InfoPageFooter，並帶自己的 currentHref', ({ file, route }) => {
    const source = readApp(file);
    expect(source).toMatch(/<InfoPageFooter\b/);
    expect(source).toContain(`currentHref="${route}"`);
  });

  it('InfoPageFooter 排除目前頁面、其餘資訊頁都在，並且一定連回首頁', () => {
    const source = readApp('(info)/_components/InfoPageFooter.tsx');
    expect(source).toMatch(/exclude=\{currentHref\}/);
    expect(source).toMatch(/href="\/"/);
  });

  it('所有資訊頁互相排除自己之後，兩兩都連得到對方（掃 INFO_LINKS 本身沒有遺漏任何一頁）', () => {
    for (const page of INFO_PAGES) {
      const others = INFO_LINKS.filter((link) => link.href !== page.route).map((link) => link.href);
      expect(others.sort()).toEqual(
        INFO_PAGES.map((other) => other.route)
          .filter((route) => route !== page.route)
          .sort(),
      );
    }
  });
});

describe('文案資料的形狀', () => {
  it('常見問題恰好四組', () => {
    expect(FAQ_GROUPS.length).toBe(4);
  });

  it('常見問題題數在 10～15 之間，沒有空字串', () => {
    const totalQuestions = FAQ_GROUPS.reduce((sum, group) => sum + group.items.length, 0);
    expect(totalQuestions).toBeGreaterThanOrEqual(10);
    expect(totalQuestions).toBeLessThanOrEqual(15);

    for (const group of FAQ_GROUPS) {
      expect(group.title.trim().length).toBeGreaterThan(0);
      for (const item of group.items) {
        expect(item.question.trim().length).toBeGreaterThan(0);
        expect(item.answer.trim().length).toBeGreaterThan(0);
      }
    }
  });

  it('付款方式明確為信用卡，不再把付款方式交由付款頁決定', () => {
    const paymentFaq = FAQ_GROUPS.flatMap((group) => group.items).find(
      (item) => item.question === '可以用哪些方式付款？',
    );
    if (!paymentFaq) {
      throw new Error('找不到問題「可以用哪些方式付款？」');
    }

    expect(paymentFaq.answer).toContain('信用卡');
    expect(paymentFaq.answer).not.toContain('ATM');
    expect(paymentFaq.answer).not.toContain('超商代碼');
    expect(paymentFaq.answer).not.toContain('以付款頁顯示為準');
  });

  it('購買流程恰好兩段，每段 4～6 步，沒有空字串', () => {
    expect(GUIDE_SECTIONS.length).toBe(2);
    for (const section of GUIDE_SECTIONS) {
      expect(section.steps.length).toBeGreaterThanOrEqual(4);
      expect(section.steps.length).toBeLessThanOrEqual(6);
      for (const step of section.steps) {
        expect(step.trim().length).toBeGreaterThan(0);
      }
    }
  });

  it('關於我們有內容，沒有空字串', () => {
    expect(ABOUT_PARAGRAPHS.length).toBeGreaterThan(0);
    for (const paragraph of ABOUT_PARAGRAPHS) {
      expect(paragraph.trim().length).toBeGreaterThan(0);
    }
  });

  it('服務條款恰好八節，沒有空字串', () => {
    expect(TERMS_SECTIONS.length).toBe(8);
    for (const section of TERMS_SECTIONS) {
      expect(section.title.trim().length).toBeGreaterThan(0);
      expect(section.paragraphs.length).toBeGreaterThan(0);
      for (const paragraph of section.paragraphs) {
        expect(paragraph.text.trim().length).toBeGreaterThan(0);
      }
    }
  });

  it('最後更新日期有填、不是派工書裡的佔位字串，且是第六輪定稿日 2026-09-24', () => {
    expect(TERMS_LAST_UPDATED.trim().length).toBeGreaterThan(0);
    expect(TERMS_LAST_UPDATED).not.toContain('實作時填');
    expect(TERMS_LAST_UPDATED).toBe('2026-09-24');
  });

  it('隱私權政策恰有內容、每節都有標題與段落，沒有空字串', () => {
    expect(PRIVACY_SECTIONS.length).toBeGreaterThan(0);
    for (const section of PRIVACY_SECTIONS) {
      expect(section.title.trim().length).toBeGreaterThan(0);
      expect(section.paragraphs.length).toBeGreaterThan(0);
      for (const paragraph of section.paragraphs) {
        expect(paragraph.trim().length).toBeGreaterThan(0);
      }
    }
  });

  it('隱私權政策更新日期已填、是第六輪定稿日 2026-09-24', () => {
    expect(PRIVACY_LAST_UPDATED).toBe('2026-09-24');
  });
});

describe('隱私權政策逐項驗證個資法第 8 條必要告知欄位與第 3 條五項權利（Leader 複核要求：上一輪只驗了「非空」，沒有驗語意）', () => {
  /** 依標題片段找到對應章節、回傳段落合併文字；找不到就先讓 it 本身失敗，不要讓後面的 expect 對 undefined 取用而給出誤導訊息。 */
  function sectionText(titleFragment: string): string {
    const section = PRIVACY_SECTIONS.find((s) => s.title.includes(titleFragment));
    if (!section) {
      throw new Error(`找不到標題含「${titleFragment}」的隱私權政策章節`);
    }
    return section.paragraphs.join(' ');
  }

  it('1. 蒐集者與聯絡方式：寫明蒐集者身分，行使方式指向客服小幫手', () => {
    const text = sectionText('蒐集者與聯絡方式');
    expect(text).toContain('GreyGray');
    expect(text).toContain('客服小幫手');
  });

  it('2. 蒐集目的：涵蓋會員驗證、付款退款、配送取貨、客服、維運安全法令義務', () => {
    const text = sectionText('蒐集之目的');
    expect(text).toContain('會員註冊與身分驗證');
    expect(text).toContain('付款');
    expect(text).toContain('退款');
    expect(text).toContain('配送');
    expect(text).toContain('取貨');
    expect(text).toContain('客服');
    expect(text).toContain('維運');
    expect(text).toContain('法令遵循義務');
  });

  it('3. 個資類別：帳號識別、收件資訊、交易付款狀態、客服留言、必要 cookie/session', () => {
    const text = sectionText('蒐集之個人資料類別');
    expect(text).toContain('帳號識別');
    expect(text).toContain('收件資訊');
    expect(text).toContain('交易與付款狀態');
    expect(text).toContain('客服留言');
    expect(text).toContain('cookie');
  });

  it('4. 利用期間：完成目的所必要期間及法令爭議所需期間，不承諾固定保存年限', () => {
    const text = sectionText('利用期間');
    expect(text).toContain('必要之期間');
    expect(text).toContain('法令');
    expect(text).toContain('不承諾固定的保存年限');
  });

  it('5. 利用地區、對象與方式：地區具體到可操作、非空泛描述，對象逐一列出', () => {
    const text = sectionText('利用地區');
    expect(text).toContain('中華民國境內');
    expect(text).not.toContain('必要的實際處理地區');
    expect(text).toContain('Cloudflare');
    expect(text).toContain('美國');
    expect(text).toContain('歐盟');
    expect(text).toContain('綠界科技');
    expect(text).toContain('日本');
    expect(text).toContain('物流業者與超商');
    expect(text).toContain('技術服務廠商');
    expect(text).toContain('依法有調查或請求權限之機關');
  });

  it('6. 五項權利逐一列出：查詢/閱覽、製給複製本、補充/更正、停止蒐集處理利用、刪除', () => {
    const text = sectionText('當事人權利');
    expect(text).toContain('查詢或請求閱覽');
    expect(text).toContain('請求製給複製本');
    expect(text).toContain('請求補充或更正');
    expect(text).toContain('請求停止蒐集、處理或利用');
    expect(text).toContain('請求刪除');
  });

  it('7. 權利的行使方式：透過客服小幫手提出', () => {
    const text = sectionText('當事人權利');
    expect(text).toContain('客服小幫手');
    expect(text).toContain('提出前述請求');
  });

  it('8. 不提供資料的影響：具體寫出可能因此無法完成的服務', () => {
    const text = sectionText('不提供資料的影響');
    expect(text).toContain('無法完成');
  });
});

describe('七日鑑賞期定稿（使用者 2026-09-24 第六輪拍板）：保留法定例外清單，不再有老闆確認項', () => {
  /** git HEAD 版 terms.ts 第五節的五行例外項目，第六輪要求逐字採用。 */
  const LEGAL_EXCEPTION_ITEMS = [
    '・依你的要求客製化生產的商品',
    '・易於腐敗、保存期限較短或解約時即將逾期的商品',
    '・經你拆封後不再適合退回的影音商品或電腦軟體',
    '・已拆封的個人衛生用品',
    '・非以有形媒介提供的數位內容或一經提供即完成的線上服務，並經你事先同意放棄解約權',
  ] as const;

  const section5 = () => {
    const section = TERMS_SECTIONS.find((s) => s.title.includes('鑑賞期與退貨'));
    if (!section) {
      throw new Error('找不到標題含「鑑賞期與退貨」的服務條款章節');
    }
    return section;
  };

  it('服務條款不再有任何老闆確認項（資料層沒有段落設 isOwnerNote，原始碼也沒有 isOwnerNote: true）', () => {
    const ownerNoteTexts = TERMS_SECTIONS.flatMap((section) => section.paragraphs)
      .filter((paragraph) => paragraph.isOwnerNote)
      .map((paragraph) => paragraph.text);
    expect(ownerNoteTexts).toEqual([]);
    expect(readApp('(info)/_content/terms.ts')).not.toMatch(/isOwnerNote:s*true/);
  });

  it('第五節含《通訊交易解除權合理例外情事適用準則》的引言與恰好五個例外項目，五行逐字等於 git HEAD 版', () => {
    const paragraphs = section5().paragraphs.map((p) => p.text);
    const intro = paragraphs.filter((text) => text.includes('通訊交易解除權合理例外情事適用準則'));
    expect(intro).toHaveLength(1);
    const introText = intro[0] ?? '';
    expect(introText).toMatch(/^8. /);
    expect(introText).toContain('不適用七日鑑賞期');
    const items = paragraphs.filter((text) => text.startsWith('・'));
    expect(items).toEqual([...LEGAL_EXCEPTION_ITEMS]);
    expect(paragraphs.indexOf(LEGAL_EXCEPTION_ITEMS[0])).toBe(paragraphs.indexOf(introText) + 1);
  });

  it('第五節第 1 點指向第 8 點的例外，不再寫「所有商品」；「無須說明理由」「不負擔費用」仍在第五節本身', () => {
    const paragraphs = section5().paragraphs.map((p) => p.text);
    const first = paragraphs.find((text) => text.startsWith('1. '));
    expect(first).toBeDefined();
    expect(first).toContain('第 8 點');
    expect(first).toContain('七日鑑賞期');
    expect(first).not.toContain('所有商品');
    const text = paragraphs.join(' ');
    expect(text).not.toContain('所有商品');
    expect(text).toContain('無須說明理由');
    expect(text).toContain('不負擔');
  });

  it('不把完整包裝、配件、贈品、發票寫成解除權成立的條件（只能是妥善保管並一併退回的請求）', () => {
    const joined = TERMS_SECTIONS.flatMap((section) => section.paragraphs.map((p) => p.text)).join(' ');
    expect(joined).not.toMatch(/完整包裝[^。]*(才|方可|始得|條件)/);
  });

  it('不把鑑賞期寫成本站可自行拒絕解約', () => {
    const joined = TERMS_SECTIONS.flatMap((section) => section.paragraphs.map((p) => p.text)).join(' ');
    expect(joined).not.toContain('拒絕解約');
  });

  it('個資段落指向隱私權政策', () => {
    const joined = TERMS_SECTIONS.flatMap((section) => section.paragraphs.map((p) => p.text)).join(' ');
    expect(joined).toContain('隱私權政策');
  });
});

describe('鑑賞期修正（Leader／第二模型審查確認）：19-2 條的二擇一、15 日取回與返還、不得以先寄回或檢視剝奪解除', () => {
  const joinedTerms = () => TERMS_SECTIONS.flatMap((section) => section.paragraphs.map((p) => p.text)).join(' ');

  /** 只抓「才／方可／始得 + （能／可／得）+ 解除」這種直接把條件綁在解除上的正面宣稱，不會被合法的否定句誤觸。 */
  const CONDITIONAL_RESCISSION_RE = /(才|方可|始得)(能|可|得)?解除/;
  /** 只抓「寄回／檢視」與「前提／條件」同一子句（未跨過句號）裡出現，不會被「不會因為…而受影響」這類否定句誤觸。 */
  const RESCISSION_PRECONDITION_RE = /(寄回|檢視)[^。]{0,20}(前提|條件)/;

  it('解除契約可擇一：退回商品或以可留存文字方式通知本站，不要求同時完成兩者', () => {
    const text = joinedTerms();
    expect(text).toContain('擇一');
    expect(text).toContain('退回商品');
    expect(text).toContain('通知本站解除契約');
  });

  it('明載《消費者保護法》第 19-2 條的 15 日取回與返還對價期限', () => {
    const text = joinedTerms();
    expect(text).toContain('第 19-2 條');
    expect(text).toContain('15 日內');
    expect(text).toContain('取回商品');
    expect(text).toContain('返還');
  });

  it('不得以「先寄回商品才能解除」或「檢視後才能解除」這類正面宣稱剝奪解除權', () => {
    const text = joinedTerms();
    expect(text).not.toMatch(CONDITIONAL_RESCISSION_RE);
    expect(text).not.toMatch(RESCISSION_PRECONDITION_RE);
  });

  it('明寫解除契約是否成立不受「尚未寄回商品」或「曾經檢視商品」影響', () => {
    const text = joinedTerms();
    expect(text).toContain('不會因為你尚未寄回商品而受影響');
    expect(text).toContain('不會因為你曾經檢視商品而受影響');
  });

  it('可歸責消費者的價值減損另依法求償，不描述成本站得拒絕解除', () => {
    const text = joinedTerms();
    expect(text).not.toContain('拒絕解約');
    expect(text).not.toContain('拒絕解除');
  });

  it('價值減損段落明寫「另外的求償問題」與「不影響你解除契約本身的效力」（第二模型：補正向斷言，不能只驗負向）', () => {
    const text = joinedTerms();
    expect(text).toContain('另外的求償問題');
    expect(text).toContain('不影響你解除契約本身的效力');
  });
});

describe('19-2 條最終法律精準度修正：取回地點、通知起算、退款起算與不強制自費寄回', () => {
  const joinedTerms = () => TERMS_SECTIONS.flatMap((section) => section.paragraphs.map((p) => p.text)).join(' ');

  it('取回商品的地點是原交付處所或雙方約定處所', () => {
    const text = joinedTerms();
    expect(text).toContain('原交付處所');
    expect(text).toContain('雙方約定處所');
  });

  it('本站取回商品的期限是自收到解除通知之次日起 15 日內', () => {
    const text = joinedTerms();
    expect(text).toMatch(/收到通知之次日起\s*15\s*日內[^。]*取回商品/);
  });

  it('返還對價有兩個各自獨立、都必須成立的起算點：取回商品之次日起 15 日內、收到你退回商品之次日起 15 日內（第二模型：不可用「任一 alternation 命中就過」的寬鬆判準，兩句都要各自逐字命中）', () => {
    const text = joinedTerms();
    expect(text).toContain('取回商品之次日起 15 日內');
    expect(text).toContain('收到你退回商品之次日起 15 日內');
  });

  it('返還對價不是以「收到解除通知」之次日起算（19-2 條第二項沒有這個選項，那是第一項取回商品的起算點，不是退款起算點）', () => {
    const text = joinedTerms();
    expect(text).not.toMatch(/收到(你)?解除契約通知之次日起\s*15\s*日內[^。]*返還/);
  });

  it('不要求消費者先自行付費將商品寄回', () => {
    const text = joinedTerms();
    expect(text).toContain('不要求你先自行付費');
  });
});

describe('個資目的一致性：服務條款與隱私權政策要講同一份目的清單（Leader／第二模型審查確認）', () => {
  it('服務條款的個資目的涵蓋會員驗證、付款退款、配送取貨、客服與維運安全法令義務，不再只寫「交易、出貨、客服」', () => {
    const text = TERMS_SECTIONS.flatMap((section) => section.paragraphs.map((p) => p.text)).join(' ');
    expect(text).toContain('會員註冊與身分驗證');
    expect(text).toContain('付款與退款');
    expect(text).toContain('配送與取貨');
    expect(text).toContain('客服聯繫');
    expect(text).toContain('維運安全與法令遵循義務');
  });
});

describe('修改條款不再是「公告後繼續使用即視為同意」（Leader／第二模型審查確認）', () => {
  it('服務條款與隱私權政策都不再出現「即視為同意」，改為重大變更主動通知、需另行取得同意前不擴大利用', () => {
    const termsText = TERMS_SECTIONS.flatMap((section) => section.paragraphs.map((p) => p.text)).join(' ');
    const privacyText = PRIVACY_SECTIONS.flatMap((section) => section.paragraphs).join(' ');
    expect(termsText).not.toContain('即視為同意');
    expect(privacyText).not.toContain('即視為同意');
    expect(termsText).toContain('取得你的同意');
    expect(privacyText).toContain('取得你的同意');
  });
});

describe('隱私權政策的利用地區與密碼敘述修正（Leader／第二模型審查確認；FE-37 法律精準度修正再改一次地區敘述）', () => {
  it('利用地區具體列出中華民國、美國、歐盟、日本與 Cloudflare、綠界，不再只寫空泛的「必要的實際處理地區」', () => {
    const text = PRIVACY_SECTIONS.flatMap((section) => section.paragraphs).join(' ');
    expect(text).toContain('中華民國境內');
    expect(text).not.toContain('必要的實際處理地區');
    expect(text).toContain('美國');
    expect(text).toContain('歐盟');
    expect(text).toContain('日本');
    expect(text).toContain('Cloudflare');
    expect(text).toContain('綠界科技');
  });

  it('密碼敘述改為登入驗證資料，說明只保存加鹽雜湊、不保存明碼，不揭露 salt 或雜湊實際值', () => {
    const text = PRIVACY_SECTIONS.flatMap((section) => section.paragraphs).join(' ');
    expect(text).toContain('登入驗證資料');
    expect(text).toContain('PBKDF2-SHA256');
    expect(text).toContain('不會保存你的密碼明碼');
    expect(text).not.toMatch(/salt[:：]\s*\S/i);
  });
});

describe('鑑賞期規則只在服務條款寫一份，FAQ 只指路', () => {
  it('服務條款頁明確寫出七日鑑賞期（ADR-025 已由使用者拍板）', () => {
    const joined = TERMS_SECTIONS.flatMap((section) => section.paragraphs.map((p) => p.text)).join(' ');
    expect(joined).toContain('七日鑑賞期');
    expect(joined).toContain('鑑賞期');
  });

  it('FAQ 提到鑑賞期七天並指向服務條款頁，不重複寫法律細節', () => {
    const returnFaq = FAQ_GROUPS.flatMap((group) => group.items).find(
      (item) => item.question === '收到商品後想退貨怎麼辦？',
    );
    expect(returnFaq).toBeDefined();
    expect(returnFaq?.answer).toContain('七天');
    expect(returnFaq?.answer).toContain('鑑賞期');
    expect(returnFaq?.answer).toContain('服務條款');
  });

  it('FAQ 退貨題提示服務條款列出的例外商品除外（第六輪保留法定例外清單後，FAQ 不能再暗示一律可退）', () => {
    const returnFaq = FAQ_GROUPS.flatMap((group) => group.items).find(
      (item) => item.question === '收到商品後想退貨怎麼辦？',
    );
    expect(returnFaq?.answer).toContain('例外');
    expect(returnFaq?.answer).toMatch(/服務條款[^。]*例外[^。]*除外/);
  });
});

describe('FAQ 答案不准出現位置指引（FE-35 的客服小幫手直接重用 answer，讀者可能人就在小幫手裡）', () => {
  const POSITION_HINT_RE = /右下角|點.{0,4}客服小幫手/;

  it('FAQ_GROUPS 的每一則 answer 都沒有「右下角」「點…客服小幫手」這類位置指引', () => {
    const offenders = FAQ_GROUPS.flatMap((group) => group.items)
      .filter((item) => POSITION_HINT_RE.test(item.answer))
      .map((item) => item.question);
    expect(offenders).toEqual([]);
  });

  it('位置指引集中在 FAQ 頁面層級（faq/page.tsx），不在 FAQ_GROUPS 裡', () => {
    const source = readApp('(info)/faq/page.tsx');
    expect(source).toMatch(/客服小幫手/);
  });

  it('服務條款第七節「聯絡我們」是條款頁專屬內容，不會被 FAQ_GROUPS 帶到，維持提及客服小幫手位置沒問題', () => {
    const contactSection = TERMS_SECTIONS.find((section) => section.title === '七、聯絡我們');
    expect(contactSection).toBeDefined();
    expect(contactSection?.paragraphs.some((p) => p.text.includes('客服小幫手'))).toBe(true);
  });
});

/**
 * ── 不准出現沒確認過的承諾 ──
 *
 * 這是「不存在」型斷言：掃過所有文案資料，逐類確認沒有出現
 * 聯絡方式、鑑賞期、或任何時限承諾。唯一允許的數字是三個運費金額。
 *
 * 這裡故意把「哪些字串會被掃」抽成一個陣列，而不是分別在每個 it 裡各自
 * 讀一次 `FAQ_GROUPS`／`GUIDE_SECTIONS`／`ABOUT_PARAGRAPHS`——
 * 這樣自驗時只要在這個陣列裡臨時塞一個反例，就能同時驗到所有規則。
 */
const ALL_COPY_TEXT: readonly string[] = [
  ...FAQ_GROUPS.flatMap((group) => [group.title, ...group.items.flatMap((item) => [item.question, item.answer])]),
  ...GUIDE_SECTIONS.flatMap((section) => [section.title, ...section.steps]),
  ...ABOUT_PARAGRAPHS,
];

const CONTACT_INFO_RE = /電話|LINE|Line\s*ID|Email|信箱|地址|營業時間/i;
const APPRAISAL_PERIOD_RE = /鑑賞期/;
const IMMEDIATE_PROMISE_RE = /隔日|當天|立即出貨|盡快/;

/** 阿拉伯數字或中文數字 ＋ 常見時間單位；中間允許一個「個」字（例如「三個工作天」）。 */
const TIME_LIMIT_RE = /(?:[0-9]+|[一二三四五六七八九十百千萬]+)\s*(?:個)?\s*(?:工作天|營業日|天|日|小時|hr|週)/i;

/**
 * 使用者 2026-09-19 拍板：鑑賞期七天寫進服務條款，FAQ 可以指路提及。
 * 這裡不整條拿掉鑑賞期／時限的黑名單規則——那條規則的價值是擋「隨手掰一個
 * 沒人確認過的期限」，唯一已確認的例外是這一則指向服務條款的退貨 FAQ。
 * 判準是「有沒有提到服務條款」：真的引用服務條款才可能合法提到期限，
 * 隨手加的新期限不會剛好也提到服務條款。
 */
const textsAllowedToMentionTimeLimits = ALL_COPY_TEXT.filter((text) => text.includes('服務條款'));
const textsSubjectToTimeLimitBan = ALL_COPY_TEXT.filter((text) => !text.includes('服務條款'));

describe('不准出現沒確認過的承諾（客服電話、地址、LINE、Email、營業時間、未指路的鑑賞期／時限）', () => {
  it('文案資料裡沒有聯絡方式', () => {
    const offenders = ALL_COPY_TEXT.filter((text) => CONTACT_INFO_RE.test(text));
    expect(offenders).toEqual([]);
  });

  it('只有指向服務條款的那一則 FAQ 能提「鑑賞期」，其餘文案不准（ADR-025 已拍板但細節只放服務條款一處）', () => {
    const offenders = textsSubjectToTimeLimitBan.filter((text) => APPRAISAL_PERIOD_RE.test(text));
    expect(offenders).toEqual([]);
    expect(textsAllowedToMentionTimeLimits.filter((text) => APPRAISAL_PERIOD_RE.test(text)).length).toBe(1);
  });

  it('文案資料裡沒有「隔日」「當天」「立即出貨」「盡快」這類出貨時間承諾', () => {
    const offenders = ALL_COPY_TEXT.filter((text) => IMMEDIATE_PROMISE_RE.test(text));
    expect(offenders).toEqual([]);
  });

  it('沒指向服務條款的文案不准有數字＋時間單位的時限承諾（唯一允許的數字是三個運費金額，另一個例外是指路 FAQ 的七天鑑賞期）', () => {
    const offenders = textsSubjectToTimeLimitBan.filter((text) => TIME_LIMIT_RE.test(text));
    expect(offenders).toEqual([]);
  });

  it('三個運費金額確實還在（不是規則寫過頭把合法內容一起濾掉）', () => {
    const joined = ALL_COPY_TEXT.join(' ');
    expect(joined).toContain('NT$60');
    expect(joined).toContain('NT$120');
    expect(joined).toContain('NT$0');
  });
});
