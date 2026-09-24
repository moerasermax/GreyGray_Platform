/**
 * `hasRequiredRole` 要跟後端 `IStaffRolePolicy.Allows` 完全一致（FE-50）：
 * `actual == Owner || actual == required || required == ReadOnly`。
 *
 * 4 × 4 個角色組合逐一列出期望值，不用迴圈自己算——這張表就是規格，
 * 後端改規則時這裡要跟著改，不能靠「看起來對」。
 */
import { describe, expect, it } from 'vitest';
import { hasRequiredRole, type StaffRole } from './session';

/** [目前角色, 要求角色, 期望] */
const MATRIX: readonly (readonly [StaffRole, StaffRole, boolean])[] = [
  // Owner 全通
  ['Owner', 'Owner', true],
  ['Owner', 'Accountant', true],
  ['Owner', 'Operator', true],
  ['Owner', 'ReadOnly', true],
  // Accountant：只通自己與 ReadOnly
  ['Accountant', 'Owner', false],
  ['Accountant', 'Accountant', true],
  ['Accountant', 'Operator', false],
  ['Accountant', 'ReadOnly', true],
  // Operator：只通自己與 ReadOnly
  ['Operator', 'Owner', false],
  ['Operator', 'Accountant', false],
  ['Operator', 'Operator', true],
  ['Operator', 'ReadOnly', true],
  // ReadOnly：只通 ReadOnly
  ['ReadOnly', 'Owner', false],
  ['ReadOnly', 'Accountant', false],
  ['ReadOnly', 'Operator', false],
  ['ReadOnly', 'ReadOnly', true],
];

describe('hasRequiredRole（對齊後端 StaffRolePolicy.Allows）', () => {
  it('矩陣涵蓋全部 4 × 4 = 16 個組合，沒有重複', () => {
    const keys = new Set(MATRIX.map(([current, required]) => `${current}->${required}`));
    expect(MATRIX).toHaveLength(16);
    expect(keys.size).toBe(16);
  });

  it.each(MATRIX)('current=%s required=%s → %s', (current, required, expected) => {
    expect(hasRequiredRole(current, required)).toBe(expected);
  });

  it('Operator 不可進 Accountant', () => {
    expect(hasRequiredRole('Operator', 'Accountant')).toBe(false);
  });

  it('Accountant 不可進 Operator', () => {
    expect(hasRequiredRole('Accountant', 'Operator')).toBe(false);
  });

  it('任何角色都可進 ReadOnly', () => {
    for (const role of ['Owner', 'Accountant', 'Operator', 'ReadOnly'] as const) {
      expect(hasRequiredRole(role, 'ReadOnly')).toBe(true);
    }
  });

  it('Owner 全通', () => {
    for (const required of ['Owner', 'Accountant', 'Operator', 'ReadOnly'] as const) {
      expect(hasRequiredRole('Owner', required)).toBe(true);
    }
  });
});
