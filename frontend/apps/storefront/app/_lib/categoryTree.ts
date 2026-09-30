import type { ListProductsQuery } from '@greygray/api-client/endpoints/storefront';
import type { components } from '@greygray/api-client/storefront';

type Category = components['schemas']['Category'];

export interface CategoryTreeNode {
  readonly root: Category;
  readonly children: readonly Category[];
}

export function buildCategoryTree(categories: readonly Category[]): CategoryTreeNode[] {
  const byId = new Map(categories.map((category) => [category.id, category]));
  const isDirectChild = (category: Category) => {
    if (!category.parentId) return false;
    const parent = byId.get(category.parentId);
    return Boolean(parent && !parent.parentId);
  };

  return categories
    .filter((category) => !isDirectChild(category))
    .map((root) => ({
      root,
      children: categories.filter((category) => category.parentId === root.id && isDirectChild(category)),
    }));
}

export function onlyRootCategories(categories: readonly Category[]): Category[] {
  return buildCategoryTree(categories).map((node) => node.root);
}

export interface CategoryPageData {
  readonly category: Category;
  readonly parent: Category | null;
  readonly children: readonly Category[];
  readonly productQuery: Omit<ListProductsQuery, 'cursor' | 'limit'>;
}

export function getCategoryPageData(
  categories: readonly Category[],
  categoryId: string,
): CategoryPageData | null {
  for (const node of buildCategoryTree(categories)) {
    if (node.root.id === categoryId) {
      return {
        category: node.root,
        parent: null,
        children: node.children,
        productQuery: node.children.length > 0
          ? { categoryId, includeDescendants: true }
          : { categoryId },
      };
    }
    const child = node.children.find((candidate) => candidate.id === categoryId);
    if (child) {
      return {
        category: child,
        parent: node.root,
        children: [],
        productQuery: { categoryId },
      };
    }
  }
  return null;
}
