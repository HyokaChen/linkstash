<script setup lang="ts">
import { ref } from 'vue'
import { toast } from 'vue-sonner'
import { SearchIcon } from '@lucide/vue'
import { api, type CandidateItem, type CollectResult } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'

const emit = defineEmits<{
  (e: 'collected', result: CollectResult): void
  (e: 'extracted', candidates: CandidateItem[]): void
}>()

const url = ref('')
const loading = ref(false)
const inputEl = ref<InstanceType<typeof Input> | null>(null)

/** 单按钮：直接收藏当前页，无需预判是否聚合页。 */
async function save() {
  const trimmed = url.value.trim()
  if (!trimmed) {
    toast.error('请输入 URL')
    return
  }
  if (!/^https?:\/\//i.test(trimmed)) {
    toast.error('URL 需以 http:// 或 https:// 开头')
    return
  }

  loading.value = true
  try {
    const data = await api<CollectResult | { url: string; error: string }>('/api/collect', {
      method: 'POST',
      body: JSON.stringify({ url: trimmed, extractLinks: false }),
    })
    if ('error' in data) {
      toast.error(data.error)
      return
    }
    toast.success('已收藏')
    emit('collected', data)
    url.value = ''
    inputEl.value?.$el.querySelector('input')?.focus()
  } catch (e: unknown) {
    const err = e as { message?: string }
    toast.error(`操作失败：${err.message || '未知错误'}`)
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <Card>
    <CardContent class="pt-4">
      <form class="flex gap-2" @submit.prevent="save">
        <div class="relative flex-1">
          <SearchIcon
            class="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground"
          />
          <Input
            ref="inputEl"
            v-model="url"
            type="url"
            inputmode="url"
            autocomplete="off"
            spellcheck="false"
            class="pl-8"
            placeholder="粘贴链接，回车收藏"
            :disabled="loading"
          />
        </div>
        <Button type="submit" :disabled="loading || !url.trim()">
          {{ loading ? '处理中…' : '收藏' }}
        </Button>
      </form>
    </CardContent>
  </Card>
</template>
