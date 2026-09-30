import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const PRODUCT_PAGE = join(dirname(fileURLToPath(import.meta.url)), '..', 'page.tsx');
const SOURCE = readFileSync(PRODUCT_PAGE, 'utf8');
const CODE = SOURCE.replace(/\/\*[^]*?\*\//g, '').replace(/\/\/.*$/gm, '');

function paragraphFor(expression: string): string {
  const match = new RegExp(`<p className="([^"]*)">\\s*\\{${expression}\\}\\s*</p>`).exec(CODE);
  return match?.[1] ?? '';
}

describe('商品完整描述保留換行', () => {
  it('T6 掃描器是活的', () => {
    expect(SOURCE.length).toBeGreaterThan(1_000);
    expect(SOURCE).toContain('export default async function ProductDetailPage');
    expect(paragraphFor('product.description')).not.toBe('');
    expect(paragraphFor('product.shortDescription')).not.toBe('');
  });

  it('T6：只有完整描述使用 whitespace-pre-line', () => {
    expect(paragraphFor('product.description').split(/\s+/)).toContain('whitespace-pre-line');
    expect(paragraphFor('product.shortDescription').split(/\s+/)).not.toContain('whitespace-pre-line');
  });
});
