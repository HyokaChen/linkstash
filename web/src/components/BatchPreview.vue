<script setup lang="ts">
import { computed, ref } from 'vue'
import { toast } from 'vue-sonner'
import { api, type BatchItemPayload } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'

export interface PreviewRow {
  url: string
  title: string
  translation: string
  tags?: string
  isExisting: boolean
  /** 书签导入时携带，用于显示所属文件夹 */
  folderPath?: string
}

const props = defineProps<{
  rows: PreviewRow[]
  heading: string
  description?: string
  /** 已跳过（已收藏）的条数，>0 时在顶部提示 */
  skipped?: number
  /** 书签导入时携带，入库后归入该集合 */
  groupId?: string
  /** 由调用方决定集合名（如书签文件夹名）；不传则不分组 */
  groupName?: string
}>()

const emit = defineEmits<{ (e: 'saved', n: number): void; (e: 'close'): void }>()

const selected = ref<Record<number, boolean>>({})
const saving = ref(false)

const selectableRows = computed(() => props.rows.filter((_, i) => !props.rows[i].isExisting))
const selectedCount = computed(() => selectableRows.value.filter((_, i) => selected.value[i]).length)
const allChecked = computed(
  () => selectableRows.value.length > 0 && selectedCount.value === selectableRows.value.length,
)

function isPicked(index: number) {
  return selected.value[index] === true
}

function toggleAll() {
  const next = !allChecked.value
  const nextState: Record<number, boolean> = {}
  props.rows.forEach((row, i) => {
    if (!row.isExisting) nextState[i] = next
  })
  selected.value = nextState
}

async function save() {
  const items: BatchItemPayload[] = props.rows
    .map((row, i) => ({ row, picked: selected.value[i] === true }))
    .filter((x) => x.picked)
    .map((x) => ({
      url: x.row.url,
      title: x.row.title,
      translation: x.row.translation,
      groupId: props.groupId,
    }))

  if (!items.length) {
    toast.error('请至少勾选一条')
    return
  }

  saving.value = true
  try {
    const data = await api<{ saved: number }>('/api/collect/batch', {
      method: 'POST',
      body: JSON.stringify({
        items,
        groupName: props.groupId ? null : props.groupName,
        sourceUrl: null,
      }),
    })
    emit('saved', data.saved)
  } catch (e: unknown) {
    const err = e as { message?: string }
    toast.error(`批量收藏失败：${err.message || '未知错误'}`)
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <Card>
    <CardHeader class="space-y-2">
      <div class="flex items-center justify-between gap-2">
        <CardTitle>{{ heading }}</CardTitle>
        <Button size="sm" variant="ghost" @click="emit('close')">关闭</Button>
      </div>
      <CardDescription v-if="description">{{ description }}</CardDescription>
      <p v-if="skipped" class="text-xs text-muted-foreground">
        已跳过 {{ skipped }} 条已收藏的条目
      </p>
      <label class="flex cursor-pointer items-center gap-2 text-sm">
        <Checkbox :checked="allChecked" @update:checked="toggleAll" />
        全选
      </label>
    </CardHeader>

    <CardContent class="max-h-[50vh] space-y-2 overflow-y-auto">
      <div
        v-for="(row, i) in rows"
        :key="row.url"
        class="flex items-start gap-3 rounded-lg border p-3"
        :class="row.isExisting && 'opacity-50'"
      >
        <Checkbox
          v-if="!row.isExisting"
          class="mt-0.5"
          :checked="isPicked(i)"
          @update:checked="(v: boolean | 'indeterminate') => (selected[i] = v === true)"
        />
        <span v-else class="mt-0.5 flex size-4 items-center justify-center text-xs">—</span>

        <div class="min-w-0 flex-1 space-y-0.5">
          <a
            :href="row.url"
            target="_blank"
            rel="noopener noreferrer"
            class="line-clamp-1 text-sm font-medium text-primary hover:underline"
          >{{ row.title }}</a>
          <p v-if="row.translation" class="line-clamp-2 text-sm text-muted-foreground">
            {{ row.translation }}
          </p>
          <p v-if="row.folderPath" class="text-xs text-muted-foreground/70">
            📁 {{ row.folderPath }}
          </p>
          <div class="flex flex-wrap items-center gap-1 pt-0.5">
            <Badge
              v-for="t in (row.tags || '').split(',').filter(Boolean)"
              :key="t"
              variant="secondary"
              class="h-4 px-1.5 text-[10px] font-normal"
            >{{ t }}</Badge>
          </div>
          <p class="truncate text-xs text-muted-foreground/60">{{ row.url }}</p>
        </div>
      </div>
    </CardContent>

    <CardFooter>
      <Button :disabled="saving || selectedCount === 0" @click="save">
        {{ saving ? '收藏中…' : `收藏选中（${selectedCount} 条）` }}
      </Button>
    </CardFooter>
  </Card>
</template>
