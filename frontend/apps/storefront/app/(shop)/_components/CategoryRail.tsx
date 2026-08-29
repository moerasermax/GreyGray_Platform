import type { components } from '@greygray/api-client/storefront';
import { CategoryChipLink } from './CategoryChipLink';

type S = components['schemas'];

/** 橫捲圓形分類標（ADR-009 首頁切版第三段）。`scroll-snap` 由這個容器負責。 */
export function CategoryRail({ categories }: { categories: S['Category'][] }) {
  return (
    <div className="flex snap-x gap-[var(--gg-space-4)] overflow-x-auto pb-[var(--gg-space-2)]">
      {categories.map((category) => (
        <CategoryChipLink
          key={category.id}
          categoryId={category.id}
          imageSrc={category.imageUrl}
          imageAlt={category.name}
          label={category.name}
        />
      ))}
    </div>
  );
}
