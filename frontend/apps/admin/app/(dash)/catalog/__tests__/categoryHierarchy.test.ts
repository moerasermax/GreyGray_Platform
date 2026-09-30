import { describe, expect, it } from 'vitest';
import type { components } from '@greygray/api-client/admin';
import {
  CATEGORY_PARENT_HINT,
  buildCategoryInput,
  categoryOptions,
  categoryParentField,
  orderCategories,
} from '../_components/CategoryDialog';

type Category = components['schemas']['Category'];

const rootA: Category = {
  id: 'root-a',
  name: '根 A',
  imageUrl: null,
  sortOrder: 1,
  parentId: null,
};
const childA: Category = {
  id: 'child-a',
  name: '子 A',
  imageUrl: null,
  sortOrder: 2,
  parentId: rootA.id,
};
const rootB: Category = {
  id: 'root-b',
  name: '根 B',
  imageUrl: null,
  sortOrder: 3,
  parentId: null,
};

describe('FE-55 後台分類階層', () => {
  it('T7：新增、編輯根、編輯子與只改名稱都明確帶 parentId', () => {
    expect(buildCategoryInput({ name: '新增', imageUrl: '', sortOrder: '0', parentId: '' }).parentId)
      .toBeNull();
    expect(buildCategoryInput({ name: rootA.name, imageUrl: '', sortOrder: '1', parentId: '' }).parentId)
      .toBeNull();
    expect(buildCategoryInput({ name: childA.name, imageUrl: '', sortOrder: '2', parentId: rootA.id }).parentId)
      .toBe(rootA.id);
    expect(buildCategoryInput({ name: '只改名稱', imageUrl: '', sortOrder: '2', parentId: rootA.id }))
      .toMatchObject({ name: '只改名稱', parentId: rootA.id });
  });

  it('T8：上層選項只有根、排除自己；已有子分類時停用並回傳提示', () => {
    const categories = [rootA, childA, rootB];
    const rootState = categoryParentField(categories, rootA.id);
    const childState = categoryParentField(categories, childA.id);

    expect(rootState.options).toEqual([{ value: rootB.id, label: rootB.name }]);
    expect(rootState.disabled).toBe(true);
    expect(rootState.hint).toBe(CATEGORY_PARENT_HINT);
    expect(childState.options).toEqual([
      { value: rootA.id, label: rootA.name },
      { value: rootB.id, label: rootB.name },
    ]);
    expect(childState.disabled).toBe(false);
    expect(childState.hint).toBeNull();
  });

  it('T11：表格與商品選項都是根後緊接子，子分類顯示「父 › 子」', () => {
    const categories = [rootA, rootB, childA];
    expect(orderCategories(categories).map((category) => category.id)).toEqual([
      rootA.id,
      childA.id,
      rootB.id,
    ]);
    expect(categoryOptions(categories)).toEqual([
      { value: rootA.id, label: rootA.name },
      { value: childA.id, label: '根 A › 子 A' },
      { value: rootB.id, label: rootB.name },
    ]);
  });
});
