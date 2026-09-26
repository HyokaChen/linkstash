<script setup lang="ts">
import { computed, ref } from 'vue'
import { toast } from 'vue-sonner'
import { api, type CandidateItem } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'

const props = defineProps<{ candidates: CandidateItem[] }>()
const emit = defineEmits<{
  (e: 'saved', n: number): void
  (e: 'close'): void
}>()

const selected = ref<Record<number, boolean>>(
  Object.fromEntries(props.candidates.map((_, i) => [i, true])),
)

const allChecked = computed(() => props.candidates.every((_, i) => selected.value[i]))
const selectedCount = computed(() => props.candidates.filter((_, i) => selected.value[i]).length)

function toggleAll() {
  const next = !allChecked.value
  props.candidates.forEach((_, i) => (selected.value[i] = next))
}

const loading = ref(false)

async function save() {
  const items = props.candidates
    .map((c, i) => ({ c, picked: selected.value[i] }))
    .filter((x) => x.picked)
    .map((x) => x.c)
  if (!items.length) {
    toast.error('请至少勾选一条')
    return
  }
  loading.value = true
  try {
    const data = await api<{ saved: number }>('/api/collect/batch', {
      method: 'POST',
      body: JSON.stringify({ items }),
    })
    emit('saved', data.saved)
  } catch (e: unknown) {
    const err = e as { message?: string }
    toast.error(`批量收藏失败：${err.message || '未知错误'}`)
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <Card>
    <CardHeader class="space-y-2">
      <div class="flex items-center justify-between gap-2">
        <CardTitle>提取到 {{ candidates.length }} 条链接</CardTitle>
        <Button size="sm" variant="ghost" @click="emit('close')">关闭</Button>
      </div>
      <CardDescription>勾选你要收藏的链接，批量入库</CardDescription>
      <label class="flex cursor-pointer items-center gap-2 text-sm">
        <Checkbox :checked="allChecked" @update:checked="toggleAll" />
        全选
      </label>
    </CardHeader>
    <CardContent class="max-h-[50vh] space-y-2 overflow-y-auto">
      <label
        v-for="(c, i) in candidates"
        :key="c.url"
        class="flex cursor-pointer items-start gap-3 rounded-lg border p-3"
      >
        <Checkbox v-model:checked="selected[i]" class="mt-0.5" />
        <div class="min-w-0 space-y-0.5">
          <a
            :href="c.url"
            target="_blank"
            rel="noopener noreferrer"
            class="line-clamp-1 text-sm font-medium text-primary hover:underline"
          >{{ c.title }}</a>
          <p class="line-clamp-2 text-sm text-muted-foreground">{{ c.translation }}</p>
          <p class="line-clamp-1 text-xs text-muted-foreground/70">{{ c.url }}</p>
        </div>
      </label>
    </CardContent>
    <CardFooter>
      <Button :disabled="loading" @click="save">
        {{ loading ? '收藏中…' : `收藏选中（${selectedCount} 条）` }}
      </Button>
    </CardFooter>
  </Card>
</template>