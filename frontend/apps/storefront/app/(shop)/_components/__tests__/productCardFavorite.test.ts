import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const source = readFileSync(
  join(dirname(fileURLToPath(import.meta.url)), '..', 'ProductCardLink.tsx'),
  'utf8',
);

describe('ProductCardLink 的未定價分支', () => {
  const marker = source.indexOf('契約允許未定價商品出現在最愛清單');
  const branchEnd = source.indexOf('\n  );\n\n  return', marker);
  const unpricedBranch = source.slice(marker, branchEnd);

  it('掃描器真的找到未定價分支', () => {
    expect(marker).toBeGreaterThan(-1);
    expect(branchEnd).toBeGreaterThan(marker);
  });

  it('priceFrom: null 仍畫 FavoriteHeart', () => {
    expect(unpricedBranch).toContain('<FavoriteHeart');
  });

  it('未定價愛心沿用 preventDefault + stopPropagation 的導航攔截', () => {
    expect(unpricedBranch).toContain('onClick={suppressCardNavigation}');
  });

  it('卡片仍由原本的 Link 包住，沒有改掉 FE-22 的結構裁決', () => {
    expect(source).toContain('<Link href={`/products/${product.id}`} className="block h-full"');
    expect(source).toContain('{card}\n      </Link>');
  });
});
