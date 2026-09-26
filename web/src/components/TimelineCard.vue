<script setup lang="ts">
import { computed, ref } from 'vue'
import { toast } from 'vue-sonner'
import { CheckIcon, CopyIcon, TagIcon, TrashIcon } from '@lucide/vue'
import { api, type CollectionItem } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover'

const props = defineProps<{
  item: CollectionItem
  categories: string[]
  showDate?: boolean
}>()
const emit = defineEmits<{ (e: 'deleted'): void; (e: 'tagsChanged'): void }>()

const prefix = '#熊掌记/'
const tags = ref<string[]>(
  (props.item.tags ?? '')
    .split(',')
    .map((t) => t.trim())
    .filter(Boolean),
)
const pickerOpen = ref(false)
const saving = ref(false)

const markdown = computed(() => {
  const raw = (props.item as { markdown?: string }).markdown
  return raw
    ? raw
    : `[${props.item.title}](${props.item.url}) => ${props.item.translation}`
})

const createdAt = computed(() => {
  const d = new Date(props.item.createdAt)
  return props.showDate
    ? `${d.getMonth() + 1}月${d.getDate()}日 ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
    : `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
})

function isPicked(category: string) {
  return tags.value.includes(prefix + category)
}

async function toggleTag(category: string) {
  const full = prefix + category
  const next = isPicked(category)
    ? tags.value.filter((t) => t !== full)
    : [...tags.value, full]
  await persist(next)
}

async function persist(next: string[]) {
  saving.value = true
  try {
    const data = await api<{ tags: string }>(`/api/collections/${props.item.id}/tags`, {
      method: 'PUT',
      body: JSON.stringify({ tags: next.join(',') }),
    })
    tags.value = data.tags.split(',').map((t) => t.trim()).filter(Boolean)
    emit('tagsChanged')
  } catch (e: unknown) {
    const err = e as { message?: string }
    toast.error(`标签保存失败：${err.message || '未知错误'}`)
  } finally {
    saving.value = false
  }
}

async function copy() {
  try {
    await navigator.clipboard.writeText(markdown.value)
    toast.success('已复制 markdown')
  } catch {
    toast.error('复制失败（需 HTTPS 或授予剪贴板权限）')
  }
}

async function remove() {
  try {
    await api(`/api/collections/${props.item.id}`, { method: 'DELETE' })
    toast.success('已删除')
    emit('deleted')
  } catch (e: unknown) {
    const err = e as { message?: string }
    toast.error(`删除失败：${err.message || '未知错误'}`)
  }
}
</script>

<template>
  <Card class="gap-2 py-3 transition-colors hover:bg-accent/30">
    <CardContent class="space-y-2 px-3">
      <!-- 时间 + 标题 -->
      <div class="flex items-start gap-2">
        <span class="mt-0.5 shrink-0 font-mono text-xs text-muted-foreground/70">
          {{ createdAt }}
        </span>
        <a
          :href="item.url"
          target="_blank"
          rel="noopener noreferrer"
          class="line-clamp-2 min-w-0 flex-1 text-sm font-medium leading-snug hover:underline"
        >
          {{ item.title }}
        </a>
      </div>

      <!-- 译文 -->
      <p class="pl-[4.75rem] text-xs leading-relaxed text-muted-foreground line-clamp-2">
        {{ item.translation }}
      </p>

      <!-- 标签 + 操作 -->
      <div class="flex flex-wrap items-center gap-1.5 pl-[4.75rem]">
        <Badge
          v-for="t in tags"
          :key="t"
          variant="secondary"
          class="h-5 px-1.5 text-[11px] font-normal"
        >
          {{ t }}
        </Badge>

        <Popover v-model:open="pickerOpen">
          <PopoverTrigger as-child>
            <Button
              variant="ghost"
              size="xs"
              class="h-5 gap-1 px-1.5 text-[11px] text-muted-foreground"
              :disabled="saving"
            >
              <TagIcon class="size-3" />
              标签
            </Button>
          </PopoverTrigger>
          <PopoverContent align="start" class="w-64 p-0">
            <div class="max-h-64 overflow-y-auto p-1">
              <button
                v-for="c in categories"
                :key="c"
                type="button"
                class="flex w-full items-center justify-between rounded-md px-2 py-1.5 text-left text-sm hover:bg-accent"
                @click="toggleTag(c)"
              >
                <span>{{ prefix }}{{ c }}</span>
                <CheckIcon v-if="isPicked(c)" class="size-3.5" />
              </button>
            </div>
          </PopoverContent>
        </Popover>

        <div class="ml-auto flex items-center gap-0.5">
          <Button variant="ghost" size="icon-xs" class="text-muted-foreground" @click="copy">
            <CopyIcon class="size-3.5" />
            <span class="sr-only">复制</span>
          </Button>
          <Button variant="ghost" size="icon-xs" class="text-muted-foreground" @click="remove">
            <TrashIcon class="size-3.5" />
            <span class="sr-only">删除</span>
          </Button>
        </div>
      </div>
    </CardContent>
  </Card>
</template>
