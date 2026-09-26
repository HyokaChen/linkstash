<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { toast } from 'vue-sonner'
import { SearchIcon } from '@lucide/vue'
import { api, type CollectionItem } from '@/api/client'
import TimelineCard from '@/components/TimelineCard.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'

const emit = defineEmits<{ (e: 'changed'): void }>()

const items = ref<CollectionItem[]>([])
const categories = ref<string[]>([])
const total = ref(0)
const page = ref(0)
const pageSize = 20
const search = ref('')
const activeTag = ref('')
const loading = ref(false)
const hasMore = ref(true)
const sentinel = ref<HTMLElement | null>(null)

async function loadCategories() {
  try {
    const data = await api<{ categories: string[] }>('/api/tags')
    categories.value = data.categories
  } catch {
    // 标签拉取失败不阻断时间线
  }
}

async function load(reset: boolean) {
  if (loading.value) return
  loading.value = true
  try {
    const nextPage = reset ? 1 : page.value + 1
    const params = new URLSearchParams({ page: String(nextPage), pageSize: String(pageSize) })
    if (search.value.trim()) params.set('search', search.value.trim())
    if (activeTag.value) params.set('tag', activeTag.value)

    const data = await api<{ items: CollectionItem[]; total: number }>(
      `/api/collections?${params.toString()}`,
    )
    items.value = reset ? data.items : [...items.value, ...data.items]
    total.value = data.total
    page.value = nextPage
    hasMore.value = data.items.length === pageSize
  } catch (e: unknown) {
    const err = e as { message?: string }
    toast.error(`加载失败：${err.message || '未知错误'}`)
  } finally {
    loading.value = false
  }
}

function reset() {
  page.value = 0
  hasMore.value = true
  return load(true)
}

function toggleTag(cat: string) {
  const full = `#熊掌记/${cat}`
  activeTag.value = activeTag.value === full ? '' : full
  reset()
}

// 滚动到底自动加载下一页
onMounted(() => {
  loadCategories()
  reset()

  const observer = new IntersectionObserver(
    (entries) => {
      if (entries[0]?.isIntersecting && hasMore.value && !loading.value) load(false)
    },
    { rootMargin: '200px' },
  )
  if (sentinel.value) observer.observe(sentinel.value)
  ;(window as unknown as { __tlObserver?: IntersectionObserver }).__tlObserver = observer
})

watch(() => items.value.length, () => emit('changed'))
</script>

<template>
  <div class="space-y-3">
    <!-- 搜索 + 标签筛选 -->
    <div class="space-y-2">
      <div class="relative">
        <SearchIcon
          class="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground"
        />
        <Input
          v-model="search"
          class="pl-8"
          placeholder="搜索标题 / 翻译 / URL"
          @keyup.enter="reset"
        />
      </div>
      <div class="-mx-1 flex items-center gap-1 overflow-x-auto px-1 pb-1">
        <Button
          variant="ghost"
          size="xs"
          class="h-6 shrink-0 px-2 text-xs"
          :class="!activeTag && 'bg-accent text-accent-foreground'"
          @click="activeTag = ''; reset()"
        >
          全部
        </Button>
        <Button
          v-for="c in categories"
          :key="c"
          variant="ghost"
          size="xs"
          class="h-6 shrink-0 px-2 font-normal"
          :class="activeTag === `#熊掌记/${c}` && 'bg-accent text-accent-foreground'"
          @click="toggleTag(c)"
        >
          #熊掌记/{{ c }}
        </Button>
      </div>
      <p v-if="total" class="text-xs text-muted-foreground">
        共 {{ total }} 条<Badge v-if="activeTag" variant="secondary" class="ml-1.5 h-4 text-[10px]">
          {{ activeTag }}
        </Badge>
      </p>
    </div>

    <!-- 时间线 -->
    <div class="relative space-y-2 pl-3">
      <span class="absolute top-2 bottom-2 left-0 w-px bg-border" />
      <TimelineCard
        v-for="item in items"
        :key="item.id"
        :item="item"
        :categories="categories"
        @deleted="reset"
        @tags-changed="emit('changed')"
      />
    </div>

    <div ref="sentinel" class="h-4" />
    <p v-if="loading" class="py-4 text-center text-xs text-muted-foreground">加载中…</p>
    <p v-else-if="!items.length" class="py-10 text-center text-sm text-muted-foreground">
      收藏库为空，粘贴 URL 开始收藏
    </p>
    <p v-else-if="!hasMore" class="py-4 text-center text-xs text-muted-foreground">
      已经到底了
    </p>
  </div>
</template>
