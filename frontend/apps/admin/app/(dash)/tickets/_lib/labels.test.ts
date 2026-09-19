import { describe, expect, it } from 'vitest';
import {
  ticketContactText,
  ticketMenuPathText,
  ticketMessageExcerpt,
  ticketStatusLabel,
  ticketStatusTone,
} from './labels';

describe('ticketStatusLabel／ticketStatusTone', () => {
  it('open → 未結案／warning', () => {
    expect(ticketStatusLabel('open')).toBe('未結案');
    expect(ticketStatusTone('open')).toBe('warning');
  });

  it('resolved → 已結案／success', () => {
    expect(ticketStatusLabel('resolved')).toBe('已結案');
    expect(ticketStatusTone('resolved')).toBe('success');
  });

  it('未知值退回原始字串，不崩掉（鐵則 5）', () => {
    expect(ticketStatusLabel('archived')).toBe('archived');
    expect(ticketStatusTone('archived')).toBe('neutral');
  });
});

describe('ticketMessageExcerpt', () => {
  it('短訊息原樣顯示', () => {
    expect(ticketMessageExcerpt('訂單還沒到')).toBe('訂單還沒到');
  });

  it('超過長度就截斷加刪節號', () => {
    const long = '一'.repeat(50);
    const excerpt = ticketMessageExcerpt(long, 40);
    expect(excerpt.length).toBe(41);
    expect(excerpt.endsWith('…')).toBe(true);
  });

  it('前後空白先修剪再判斷長度', () => {
    expect(ticketMessageExcerpt('  哈囉  ')).toBe('哈囉');
  });
});

describe('ticketContactText', () => {
  it('兩者都有 → 用．連起來', () => {
    expect(ticketContactText({ contactEmail: 'a@b.com', contactPhone: '0912345678' })).toBe('a@b.com．0912345678');
  });

  it('只有 email', () => {
    expect(ticketContactText({ contactEmail: 'a@b.com', contactPhone: null })).toBe('a@b.com');
  });

  it('只有手機', () => {
    expect(ticketContactText({ contactEmail: null, contactPhone: '0912345678' })).toBe('0912345678');
  });

  it('都沒有 → 顯示 —，不是空字串（空字串在表格裡看起來像壞掉）', () => {
    expect(ticketContactText({ contactEmail: null, contactPhone: null })).toBe('—');
  });
});

describe('ticketMenuPathText', () => {
  it('有路徑就用 › 連起來，看得出客人是卡在哪一題', () => {
    expect(ticketMenuPathText(['運送與取貨', '運費怎麼算？'])).toBe('運送與取貨 › 運費怎麼算？');
  });

  it('空陣列要講得出「沒有」，不是空字串', () => {
    expect(ticketMenuPathText([])).not.toBe('');
  });
});
