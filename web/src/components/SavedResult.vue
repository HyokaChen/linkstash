<script setup lang="ts">
import { ref } from 'vue'
import { toast } from 'vue-sonner'
import { LayersIcon } from '@lucide/vue'
import { api, type CandidateItem, type CollectResult } from '@/api/client'
import LinkCard from '@/components/LinkCard.vue'
import { Button } from '@/components/ui/button'

const props = defineProps<{ result: CollectResult }>()
const emit = defineEmits<{
  (e: 'extracted', candidates: CandidateItem[], sourceUrl: string): void
  (e: 'deleted'): void
}>()

const extracting = ref(false)

/** 用户确认后才提取，避免误点浪费请求与额度。 */
async function expand() {
  extracting.value = true
  try {
    const data = await api<{ candidates: CandidateItem[] }>('/api/collect/extract', {
      method: 'POST',
      body: JSON.stringify({ url: props.result.url }),
    })
    if (!data.candidates?.length) {
      toast.error('未从该页面提取到条目')
      return
    }
    emit('extracted', data.candidates, props.result.url)
  } catch (e: unknown) {
    const err = e as { message?: string }
    toast.error(`提取失败：${err.message || '未知错误'}`)
  } finally {
    extracting.value = false
  }
}
</script>

<template>
  <div class="space-y-2">
    <LinkCard :item="result" @deleted="emit('deleted')" />

    <!-- 仅当页面含结构化条目时出现，普通页无此提示 -->
    <div
      v-if="result.itemCount > 0"
      class="flex items-center justify-between gap-3 rounded-lg border border-dashed px-4 py-3"
    >
      <div class="flex min-w-0 items-center gap-2 text-sm">
        <LayersIcon class="size-4 shrink-0 text-muted-foreground" />
        <span class="text-muted-foreground">
          本页还包含 <span class="font-medium text-foreground">{{ result.itemCount }}</span> 个条目
        </span>
      </div>
      <Button variant="outline" size="sm" :disabled="extracting" @click="expand">
        {{ extracting ? '提取中…' : '一并收藏' }}
      </Button>
    </div>
  </div>
</template>
